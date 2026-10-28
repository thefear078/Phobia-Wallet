using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>One pool this account has NEAR in: staked, unstaking or ready, and whether it can be withdrawn now.</summary>
public sealed record NearPoolStake(string Pool, BigInteger Staked, BigInteger Unstaked, bool Withdrawable);

public sealed record NearStakePosition(IReadOnlyList<NearPoolStake> Pools)
{
    public BigInteger Staked => Pools.Aggregate(BigInteger.Zero, (s, p) => s + p.Staked);
    public BigInteger Unstaking => Pools.Where(p => !p.Withdrawable).Aggregate(BigInteger.Zero, (s, p) => s + p.Unstaked);
    public BigInteger Withdrawable => Pools.Where(p => p.Withdrawable).Aggregate(BigInteger.Zero, (s, p) => s + p.Unstaked);
}

/// <summary>A validator's pool in the stake form: its account, the NEAR staked with it, and its fee.</summary>
public sealed record NearPool(string Id, BigInteger Stake, decimal? FeePercent);

/// <summary>
/// NEAR staking (roadmap N.16) through the network's own RPC: the pools come from the current validator
/// set, a position from each pool's own view methods, the yield from the protocol's inflation and the
/// total staked. Which pools an account has used is asked of FastNEAR's index — the RPC has no such
/// question. Calls are built by <see cref="NearStaking"/>, signed here, and submitted the way a transfer is.
/// </summary>
public sealed class NearStakingClient
{
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    private const string DefaultRpc = "https://free.rpc.fastnear.com";
    public const string FastNearApi = "https://api.fastnear.com";

    /// <summary>Every pool with NEAR of this account in it. Null when a read did not answer — never a guessed zero.</summary>
    public async Task<NearStakePosition?> PositionAsync(string account, CancellationToken ct = default)
    {
        IReadOnlyList<string>? pools;
        try
        {
            using var res = await Read.GetAsync($"{FastNearApi}/v1/account/{Uri.EscapeDataString(account)}/staking", ct);
            if (!res.IsSuccessStatusCode) return null;
            pools = ParseStakedPools(await res.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
        if (pools is null) return null;

        var rpc = ChainEndpoints.Resolve("NEAR", DefaultRpc);
        var args = JsonSerializer.Serialize(new { account_id = account });
        var list = new List<NearPoolStake>();
        foreach (var pool in pools.Where(NearStaking.IsPoolId))
        {
            var staked = await ViewAsync(rpc, pool, "get_account_staked_balance", args, ct);
            var unstaked = await ViewAsync(rpc, pool, "get_account_unstaked_balance", args, ct);
            var ready = await ViewAsync(rpc, pool, "is_account_unstaked_balance_available", args, ct);
            if (ParseYocto(staked) is not { } s || ParseYocto(unstaked) is not { } u || ready is not ("true" or "false")) return null;
            if (s > 0 || u > 0) list.Add(new NearPoolStake(pool, s, u, ready == "true" && u > 0));
        }

        return new NearStakePosition(list);
    }

    /// <summary>The pools of the current validators, largest stake first, with each one's fee where it answered.</summary>
    public async Task<IReadOnlyList<NearPool>> PoolsAsync(CancellationToken ct = default)
    {
        var rpc = ChainEndpoints.Resolve("NEAR", DefaultRpc);
        var validators = await RpcAsync(rpc, "validators", new object?[] { null }, ct);
        if (validators is null) return [];
        var pools = ParseValidators(validators.Value).Where(p => NearStaking.IsPoolId(p.Id)).OrderByDescending(p => p.Stake).Take(40).ToList();

        var withFees = new List<NearPool>(pools.Count);
        foreach (var pool in pools)
        {
            var fee = ParseFee(await ViewAsync(rpc, pool.Id, "get_reward_fee_fraction", "{}", ct));
            withFees.Add(pool with { FeePercent = fee });
        }

        return withFees;
    }

    /// <summary>
    /// The yearly yield from the protocol's own figures: the share of new supply that goes to validators,
    /// spread over what is staked — before the pool's fee. Null when any figure did not answer.
    /// </summary>
    public async Task<decimal?> YieldAsync(CancellationToken ct = default)
    {
        var rpc = ChainEndpoints.Resolve("NEAR", DefaultRpc);
        var config = await RpcAsync(rpc, "EXPERIMENTAL_protocol_config", new { finality = "final" }, ct);
        var block = await RpcAsync(rpc, "block", new { finality = "final" }, ct);
        var validators = await RpcAsync(rpc, "validators", new object?[] { null }, ct);
        if (config is null || block is null || validators is null) return null;
        return YieldFrom(config.Value, block.Value, validators.Value);
    }

    /// <summary>
    /// Builds one pool call for review: the account's balance (for a stake) and its key's nonce and a
    /// recent block hash, from the user's chosen server or the listed ones in order.
    /// </summary>
    public async Task<(StakeQuote? Quote, string? Error)> PrepareAsync(
        string from, byte[] publicKey, string method, string pool, decimal amountNear, CancellationToken ct = default)
    {
        if (!NearStaking.IsPoolId(pool)) return (null, "That is not a NEAR staking pool.");
        var yocto = BigInteger.Zero;
        if (method is NearStaking.DepositAndStake or NearStaking.Unstake && !NearTransactions.TryToYocto(amountNear, out yocto))
            return (null, "Enter a positive NEAR amount.");

        foreach (var rpc in ChainEndpoints.Candidates("NEAR", DefaultRpc))
        {
            var host = new Uri(rpc).Host;
            var account = await NearTransactionSender.QueryAsync(rpc, new { request_type = "view_account", finality = "final", account_id = from }, ct);
            if (account is null) continue;
            if (NearRpc.IsUnknownAccount(account.Value))
                return (null, "This NEAR account has not received anything yet, so it does not exist on chain and cannot stake.");
            var state = NearRpc.ParseViewAccount(account.Value);
            if (state is null) return (null, $"{host} answered with account data this wallet does not understand. Nothing was sent.");
            if (method == NearStaking.DepositAndStake && yocto + NearRpc.FeeReserveYocto > state.Spendable)
                return (null, $"Not enough NEAR. Spendable: {Near(state.Spendable)} NEAR, needed: {Near(yocto + NearRpc.FeeReserveYocto)} NEAR including gas.");
            if (method != NearStaking.DepositAndStake && NearRpc.FeeReserveYocto > state.Spendable)
                return (null, "Not enough NEAR to pay this call's gas.");

            var key = await NearTransactionSender.QueryAsync(rpc, new
            {
                request_type = "view_access_key", finality = "final", account_id = from,
                public_key = NearTransactions.PublicKeyString(publicKey),
            }, ct);
            var access = key is { } k ? NearRpc.ParseAccessKey(k) : null;
            if (access is null) return (null, $"Could not read this account's key from {host}. Nothing was sent.");

            var call = NearStaking.Call(from, publicKey, access.Value.Nonce + 1, access.Value.BlockHash, pool, method, yocto);
            var action = method switch
            {
                NearStaking.DepositAndStake => "stake",
                NearStaking.Unstake or NearStaking.UnstakeAll => "unstake",
                _ => "withdraw",
            };
            return (new StakeQuote("NEAR", action, amountNear, pool, new NearStakePayload(call, rpc)), null);
        }

        return (null, "No NEAR server answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<StakeOutcome> SignAndBroadcastAsync(StakeQuote quote, byte[] seed, CancellationToken ct = default)
    {
        if (quote.Payload is not NearStakePayload payload) return new StakeOutcome(false, null, "Not a NEAR staking call.");
        string signed, hash;
        try
        {
            (signed, hash, _) = NearTransactions.Sign(payload.Call, seed);
        }
        catch (Exception ex)
        {
            return new StakeOutcome(false, null, $"Could not sign the transaction: {ex.Message}");
        }

        var outcome = await NearTransactionSender.SubmitAsync(payload.Rpc, payload.Call.SignerId, signed, hash, ct);
        return outcome.Outcome switch
        {
            NearSubmitOutcome.Included => new StakeOutcome(true, outcome.Hash, null),
            NearSubmitOutcome.Rejected => new StakeOutcome(false, outcome.Hash, outcome.Message),
            _ => new StakeOutcome(false, outcome.Hash, outcome.Message, Unclear: true),
        };
    }

    // ---- reading -----------------------------------------------------------------------------------

    /// <summary>FastNEAR's answer: the pools an account has staked with.</summary>
    public static IReadOnlyList<string>? ParseStakedPools(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pools", out var pools) || pools.ValueKind != JsonValueKind.Array) return null;
            return pools.EnumerateArray()
                .Select(p => p.TryGetProperty("pool_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
                .Where(id => id is not null).Select(id => id!).Distinct().ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The validators' pools and stakes from <c>validators</c>.</summary>
    public static IReadOnlyList<NearPool> ParseValidators(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var r) || !r.TryGetProperty("current_validators", out var cur) || cur.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<NearPool>();
        foreach (var v in cur.EnumerateArray())
        {
            if (v.TryGetProperty("account_id", out var id) && id.ValueKind == JsonValueKind.String &&
                v.TryGetProperty("stake", out var st) && st.ValueKind == JsonValueKind.String &&
                BigInteger.TryParse(st.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var stake))
                list.Add(new NearPool(id.GetString()!, stake, null));
        }

        return list;
    }

    /// <summary>
    /// yield = max inflation × (1 − protocol treasury share) × total supply ÷ total staked, as a percent.
    /// </summary>
    public static decimal? YieldFrom(JsonElement config, JsonElement block, JsonElement validators)
    {
        try
        {
            var c = config.GetProperty("result");
            var inflation = Fraction(c.GetProperty("max_inflation_rate"));
            var treasury = Fraction(c.GetProperty("protocol_reward_rate"));
            var supply = BigInteger.Parse(block.GetProperty("result").GetProperty("header").GetProperty("total_supply").GetString()!, CultureInfo.InvariantCulture);
            var staked = ParseValidators(validators).Aggregate(BigInteger.Zero, (s, v) => s + v.Stake);
            if (inflation is null || treasury is null || staked <= 0) return null;
            var ratio = (decimal)(supply * 1_000_000 / staked) / 1_000_000m;
            return decimal.Round(inflation.Value * (1 - treasury.Value) * ratio * 100, 2);
        }
        catch
        {
            return null;
        }
    }

    private static decimal? Fraction(JsonElement f) =>
        f.ValueKind == JsonValueKind.Array && f.GetArrayLength() == 2 && f[1].GetDecimal() != 0 ? f[0].GetDecimal() / f[1].GetDecimal() : null;

    /// <summary>The fee from <c>get_reward_fee_fraction</c>: {"numerator":n,"denominator":d}.</summary>
    public static decimal? ParseFee(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var n = doc.RootElement.GetProperty("numerator").GetDecimal();
            var d = doc.RootElement.GetProperty("denominator").GetDecimal();
            return d > 0 ? decimal.Round(n / d * 100, 2) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A view method's balance answer, a quoted yoctoNEAR number.</summary>
    public static BigInteger? ParseYocto(string? json)
    {
        if (json is null) return null;
        var text = json.Trim().Trim('"');
        return BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>A pool's view method: the JSON it returned, or null when nothing answered.</summary>
    private static async Task<string?> ViewAsync(string rpc, string pool, string method, string args, CancellationToken ct)
    {
        var answer = await NearTransactionSender.QueryAsync(rpc, new
        {
            request_type = "call_function", finality = "final", account_id = pool, method_name = method,
            args_base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(args)),
        }, ct);
        return answer is { } a ? NearRpc.ParseCallResult(a) : null;
    }

    private static async Task<JsonElement?> RpcAsync(string rpc, string method, object parameters, CancellationToken ct)
    {
        try
        {
            using var res = await Read.PostAsJsonAsync(rpc, new { jsonrpc = "2.0", id = "umbrella", method, @params = parameters }, ct);
            if (!res.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static string Near(BigInteger yocto) => NearTransactions.ToNear(yocto).ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>A pool call ready to sign, and the server it was read from.</summary>
public sealed record NearStakePayload(NearStakeCall Call, string Rpc);
