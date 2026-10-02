using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using NBitcoin;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A staking transaction ready to sign: what it does in words, and what to sign.</summary>
public sealed record StakeQuote(string Symbol, string Action, decimal Amount, string Counterparty, object Payload);

/// <summary>How a staking transaction ended.</summary>
public sealed record StakeOutcome(bool Ok, string? TxId, string? Error, bool Unclear = false);

// =====================================================================================================
// TRON — Stake 2.0: freeze TRX, vote with the TRON Power it gives, claim the rewards, unfreeze.
// =====================================================================================================

/// <summary>
/// TRON staking through TronGrid. TronGrid builds each transaction; <see cref="TronStaking"/> checks
/// its bytes against what was asked before anything is signed, and <see cref="TronTransactionSender"/>
/// signs and broadcasts it exactly as it does a send.
/// </summary>
public sealed class TronStakingClient
{
    private const string ApiBase = "https://api.trongrid.io";
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Build => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private readonly TronTransactionSender _sender = new();

    public async Task<TronStakePosition?> PositionAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var account = await PostAsync(Read, "/wallet/getaccount", new { address, visible = true }, ct);
            if (account is null) return null;
            var reward = await PostAsync(Read, "/wallet/getReward", new { address, visible = true }, ct);
            return ParsePosition(account.Value, reward, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The account's frozen TRX, votes, pending reward and unfreezing TRX, as the node reports them.</summary>
    public static TronStakePosition ParsePosition(JsonElement account, JsonElement? reward, DateTimeOffset now)
    {
        long frozen = 0, votes = 0, unfreezing = 0, withdrawable = 0;
        string? votedFor = null;
        DateTimeOffset? next = null;

        if (account.TryGetProperty("frozenV2", out var f) && f.ValueKind == JsonValueKind.Array)
            foreach (var e in f.EnumerateArray())
                if (e.TryGetProperty("amount", out var a) && a.TryGetInt64(out var sun)) frozen += sun;

        if (account.TryGetProperty("votes", out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var e in v.EnumerateArray())
            {
                if (e.TryGetProperty("vote_count", out var c) && c.TryGetInt64(out var n)) votes += n;
                votedFor ??= e.TryGetProperty("vote_address", out var va) ? va.GetString() : null;
            }

        if (account.TryGetProperty("unfrozenV2", out var u) && u.ValueKind == JsonValueKind.Array)
            foreach (var e in u.EnumerateArray())
            {
                if (!e.TryGetProperty("unfreeze_amount", out var a) || !a.TryGetInt64(out var sun)) continue;
                var at = e.TryGetProperty("unfreeze_expire_time", out var t) && t.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : now;
                if (at <= now) withdrawable += sun;
                else
                {
                    unfreezing += sun;
                    if (next is null || at < next) next = at;
                }
            }

        var rewardSun = reward is { } r && r.TryGetProperty("reward", out var rr) && rr.TryGetInt64(out var rs) ? rs : 0;
        return new TronStakePosition(frozen, votes, votedFor, rewardSun, unfreezing, withdrawable, next);
    }

    /// <summary>The elected Super Representatives (those producing blocks), most-voted first.</summary>
    public async Task<IReadOnlyList<TronWitness>> WitnessesAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await Read.GetAsync($"{ApiBase}/wallet/listwitnesses?visible=true", ct);
            if (!res.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return ParseWitnesses(doc.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<TronWitness> ParseWitnesses(JsonElement root)
    {
        if (!root.TryGetProperty("witnesses", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray()
            .Where(w => w.TryGetProperty("isJobs", out var j) && j.ValueKind == JsonValueKind.True)
            .Select(w => (Address: w.TryGetProperty("address", out var a) ? a.GetString() : null,
                Url: w.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                Votes: w.TryGetProperty("voteCount", out var c) && c.TryGetInt64(out var n) ? n : 0))
            .Where(w => w.Address is { Length: 34 } && w.Address.StartsWith('T'))
            .OrderByDescending(w => w.Votes)
            .Select((w, i) => new TronWitness(w.Address!, w.Url, w.Votes, i + 1))
            .ToList();
    }

    /// <summary>Freeze <paramref name="trx"/> for energy (TRON Power either way), checked.</summary>
    public Task<(StakeQuote? Quote, string? Error)> FreezeAsync(string owner, decimal trx, CancellationToken ct = default) =>
        BuildAsync("/wallet/freezebalancev2", owner, "stake", trx, owner,
            new { owner_address = owner, frozen_balance = Sun(trx), resource = "ENERGY", visible = true },
            raw => TronStaking.CheckFreeze(raw, Address21(owner), Sun(trx), TronResource.Energy), ct);

    /// <summary>All of <paramref name="votes"/> for one Super Representative, checked.</summary>
    public Task<(StakeQuote? Quote, string? Error)> VoteAsync(string owner, string witness, long votes, CancellationToken ct = default) =>
        BuildAsync("/wallet/votewitnessaccount", owner, "vote", votes, witness,
            new { owner_address = owner, votes = new[] { new { vote_address = witness, vote_count = votes } }, visible = true },
            raw => TronStaking.CheckVote(raw, Address21(owner), Address21(witness), votes), ct);

    /// <summary>Unfreeze <paramref name="trx"/> of the energy stake; it can be withdrawn 14 days later.</summary>
    public Task<(StakeQuote? Quote, string? Error)> UnfreezeAsync(string owner, decimal trx, TronResource resource, CancellationToken ct = default) =>
        BuildAsync("/wallet/unfreezebalancev2", owner, "unstake", trx, owner,
            new { owner_address = owner, unfreeze_balance = Sun(trx), resource = resource == TronResource.Energy ? "ENERGY" : "BANDWIDTH", visible = true },
            raw => TronStaking.CheckUnfreeze(raw, Address21(owner), Sun(trx), resource), ct);

    /// <summary>Claim the voting rewards (TRON allows one claim a day).</summary>
    public Task<(StakeQuote? Quote, string? Error)> ClaimAsync(string owner, decimal reward, CancellationToken ct = default) =>
        BuildAsync("/wallet/withdrawbalance", owner, "claim", reward, owner,
            new { owner_address = owner, visible = true },
            raw => TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawBalance, Address21(owner)), ct);

    /// <summary>Move TRX whose 14-day unfreeze is over back to the spendable balance.</summary>
    public Task<(StakeQuote? Quote, string? Error)> WithdrawAsync(string owner, decimal trx, CancellationToken ct = default) =>
        BuildAsync("/wallet/withdrawexpireunfreeze", owner, "withdraw", trx, owner,
            new { owner_address = owner, visible = true },
            raw => TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawExpireUnfreeze, Address21(owner)), ct);

    public async Task<StakeOutcome> SignAndBroadcastAsync(StakeQuote quote, Key key, CancellationToken ct = default)
    {
        if (quote.Payload is not TronSendQuote tx) return new StakeOutcome(false, null, "Nothing to sign.");
        var (ok, id, error, unclear) = await _sender.SignAndBroadcastAsync(tx, key, ct);
        return new StakeOutcome(ok, id, error, unclear);
    }

    private static async Task<(StakeQuote? Quote, string? Error)> BuildAsync(
        string path, string owner, string action, decimal amount, string counterparty, object request,
        Func<byte[], string?> check, CancellationToken ct)
    {
        try
        {
            var text = await PostTextAsync(Build, path, request, ct);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var apiError))
                return (null, $"TRON refused to build this: {apiError.GetString()}");
            if (!root.TryGetProperty("raw_data_hex", out var hexEl) || hexEl.GetString() is not { } hex ||
                !root.TryGetProperty("txID", out var idEl) || idEl.GetString() is not { } id)
                return (null, "TRON did not return a transaction. Nothing was signed.");

            var raw = Convert.FromHexString(hex);
            if (!string.Equals(TronStaking.TxId(raw), id, StringComparison.OrdinalIgnoreCase))
                return (null, "TRON returned a transaction whose id does not match its contents. Nothing was signed.");
            if (check(raw) is { } why)
                return (null, $"TRON returned a different transaction than the one asked for ({why}). Nothing was signed.");

            return (new StakeQuote("TRX", action, amount, counterparty,
                new TronSendQuote("TRX", owner, counterparty, amount, false, text)), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"Could not build the TRON transaction: {ex.Message}");
        }
    }

    private static async Task<JsonElement?> PostAsync(HttpClient http, string path, object request, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await PostTextAsync(http, path, request, ct));
        return doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty("Error", out _)
            ? doc.RootElement.Clone() : null;
    }

    /// <summary>
    /// TronGrid without an API key answers three requests a second and then suspends the caller for
    /// five; a stake is two transactions back to back (freeze, then vote), which met that limit. A
    /// refusal for the rate is waited out and asked again, twice at most.
    /// </summary>
    private static async Task<string> PostTextAsync(HttpClient http, string path, object request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var res = await http.PostAsJsonAsync($"{ApiBase}{path}", request, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            var limited = (int)res.StatusCode == 429 || text.Contains("request rate exceeded", StringComparison.OrdinalIgnoreCase);
            if (!limited || attempt >= 2) return text;
            await Task.Delay(TimeSpan.FromSeconds(5.5), ct);
        }
    }

    public static long Sun(decimal trx) => decimal.ToInt64(decimal.Round(trx * TronStaking.SunPerTrx, 0, MidpointRounding.ToZero));

    /// <summary>A TRON address in its 21-byte form (0x41 ‖ 20 bytes), checksum verified.</summary>
    public static byte[] Address21(string address)
    {
        var full = Encoders.Base58.DecodeData(address);
        if (full.Length != 25) throw new FormatException("Invalid TRON address length.");
        var payload = full[..21];
        if (!full[21..].AsSpan().SequenceEqual(SHA256.HashData(SHA256.HashData(payload))[..4]))
            throw new FormatException("Invalid TRON address checksum.");
        return payload;
    }
}

// =====================================================================================================
// Solana — native stake accounts at seeds of the wallet's own key.
// =====================================================================================================

public sealed record SolStakePosition(IReadOnlyList<SolanaStakeAccount> Accounts, ulong Epoch)
{
    public ulong Staked => (ulong)Accounts.Where(a => a.State is SolanaStakeState.Active or SolanaStakeState.Activating)
        .Sum(a => (decimal)(a.Lamports - Math.Min(a.Lamports, a.RentReserve)));
    public ulong Withdrawable => (ulong)Accounts.Where(a => a.CanWithdraw).Sum(a => (decimal)a.Lamports);
    public ulong CoolingDown => (ulong)Accounts.Where(a => a.State == SolanaStakeState.Deactivating).Sum(a => (decimal)a.Lamports);
}

public sealed class SolanaStakingClient
{
    /// <summary>The stake accounts at the wallet's seeds and any others it is the withdrawer of.</summary>
    public async Task<SolStakePosition?> PositionAsync(string wallet, CancellationToken ct = default)
    {
        if (!SolanaKeys.TryDecode(wallet, out var walletKey)) return null;
        foreach (var server in SolanaNetwork.Servers())
        {
            var (epochInfo, _) = await SolanaNetwork.CallAsync(server, SolanaRpc.Request("getEpochInfo", new { commitment = "confirmed" }), ct);
            if (epochInfo is not { } ei || !ei.TryGetProperty("epoch", out var ep) || !ep.TryGetUInt64(out var epoch)) continue;

            var addresses = Enumerable.Range(0, SolanaStake.SeedSlots)
                .Select(i => SolanaKeys.Encode(SolanaStake.StakeAccountFor(walletKey, i))).ToArray();
            var (multi, _) = await SolanaNetwork.CallAsync(server, SolanaRpc.Request("getMultipleAccounts",
                addresses, new { encoding = "jsonParsed", commitment = "confirmed" }), ct);
            if (multi is not { } m || !m.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) continue;

            var found = new Dictionary<string, SolanaStakeAccount>(StringComparer.Ordinal);
            var i = 0;
            foreach (var account in values.EnumerateArray())
            {
                if (SolanaStake.Parse(addresses[i], account, epoch, wallet) is { } parsed) found[parsed.Address] = parsed;
                i++;
            }

            // Stake accounts made by another app (Phantom, Solflare) that name this wallet as withdrawer.
            // Not every public server answers this; the seed accounts above are enough on their own.
            var (others, _) = await SolanaNetwork.CallAsync(server, SolanaRpc.Request("getProgramAccounts",
                SolanaStake.ProgramAddress, new
                {
                    encoding = "jsonParsed",
                    commitment = "confirmed",
                    filters = new object[] { new { memcmp = new { offset = 44, bytes = wallet } } },
                }), ct);
            if (others is { ValueKind: JsonValueKind.Array } list)
                foreach (var entry in list.EnumerateArray())
                    if (entry.TryGetProperty("pubkey", out var pk) && pk.GetString() is { } address && !found.ContainsKey(address) &&
                        entry.TryGetProperty("account", out var acc) && SolanaStake.Parse(address, acc, epoch, wallet) is { } other)
                        found[address] = other;

            return new SolStakePosition(found.Values.OrderBy(a => a.Address, StringComparer.Ordinal).ToList(), epoch);
        }
        return null;
    }

    public async Task<IReadOnlyList<SolanaValidator>> ValidatorsAsync(CancellationToken ct = default)
    {
        foreach (var server in SolanaNetwork.Servers())
        {
            var (result, _) = await SolanaNetwork.CallAsync(server, SolanaRpc.Request("getVoteAccounts", new { commitment = "confirmed" }), ct);
            if (result is { } r && SolanaStake.ParseValidators(r) is { Count: > 0 } list) return list;
        }
        return [];
    }

    /// <summary>A new stake of <paramref name="sol"/> to <paramref name="voteAccount"/> at the next free seed.</summary>
    public async Task<(StakeQuote? Quote, string? Error)> StakeAsync(
        string wallet, decimal sol, string voteAccount, SolStakePosition position, CancellationToken ct = default)
    {
        if (!SolanaKeys.TryDecode(wallet, out var walletKey)) return (null, "This is not a Solana address.");
        if (!SolanaKeys.TryDecode(voteAccount, out var vote)) return (null, "That is not a validator's vote account.");
        if (!SolanaRpc.TryToLamports(sol, out var lamports) || lamports == 0) return (null, "Enter a positive amount with at most 9 decimal places.");

        var taken = position.Accounts.Select(a => a.Address).ToHashSet(StringComparer.Ordinal);
        var slot = Enumerable.Range(0, SolanaStake.SeedSlots)
            .FirstOrDefault(i => !taken.Contains(SolanaKeys.Encode(SolanaStake.StakeAccountFor(walletKey, i))), -1);
        if (slot < 0) return (null, "Every stake slot of this wallet is in use. Withdraw a finished stake first.");

        foreach (var server in SolanaNetwork.Servers())
        {
            var rent = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getMinimumBalanceForRentExemption", SolanaStake.AccountSpace), ct);
            if (rent is not { } reserve) continue;
            var balance = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getBalance", wallet, new { commitment = "confirmed" }), ct);
            const ulong feeAllowance = 10_000;
            if (balance is { } b && b < lamports + reserve + feeAllowance)
                return (null, $"Not enough SOL: staking {SolanaRpc.Sol(lamports)} needs {SolanaRpc.Sol(lamports + reserve + feeAllowance)} " +
                              $"including the stake account's {SolanaRpc.Sol(reserve)} reserve (returned when you withdraw) and the fee.");
            var instructions = SolanaStake.StakeInstructions(walletKey, slot, vote, lamports, reserve);
            return (new StakeQuote("SOL", "stake", sol, voteAccount, (server, instructions)), null);
        }
        return (null, "No Solana server answered. Check your connection (or Tor). Nothing was sent.");
    }

    /// <summary>Undelegate one stake account; it can be withdrawn once the epoch ends.</summary>
    public (StakeQuote? Quote, string? Error) Deactivate(string wallet, SolanaStakeAccount account)
    {
        if (!account.CanDeactivate) return (null, "This stake is not delegated.");
        if (!SolanaKeys.TryDecode(wallet, out var walletKey) || !SolanaKeys.TryDecode(account.Address, out var stake))
            return (null, "Invalid address.");
        return (new StakeQuote("SOL", "unstake", SolanaRpc.ToSol(account.Lamports), account.Address,
            (SolanaNetwork.Servers()[0], (IReadOnlyList<SolanaInstruction>)[SolanaStake.Deactivate(stake, walletKey)])), null);
    }

    /// <summary>Withdraw a whole undelegated stake account back to the wallet (its reserve included).</summary>
    public (StakeQuote? Quote, string? Error) Withdraw(string wallet, SolanaStakeAccount account)
    {
        if (!account.CanWithdraw) return (null, "This stake is still delegated or cooling down.");
        if (!SolanaKeys.TryDecode(wallet, out var walletKey) || !SolanaKeys.TryDecode(account.Address, out var stake))
            return (null, "Invalid address.");
        return (new StakeQuote("SOL", "withdraw", SolanaRpc.ToSol(account.Lamports), account.Address,
            (SolanaNetwork.Servers()[0], (IReadOnlyList<SolanaInstruction>)[SolanaStake.Withdraw(stake, walletKey, walletKey, account.Lamports)])), null);
    }

    public async Task<StakeOutcome> SignAndBroadcastAsync(StakeQuote quote, string wallet, byte[] privateKey, CancellationToken ct = default)
    {
        if (quote.Payload is not ValueTuple<string, IReadOnlyList<SolanaInstruction>> payload)
            return new StakeOutcome(false, null, "Nothing to sign.");
        if (!SolanaKeys.TryDecode(wallet, out var walletKey)) return new StakeOutcome(false, null, "Invalid address.");

        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        if (!publicKey.AsSpan().SequenceEqual(walletKey))
            return new StakeOutcome(false, null, "Key does not match the wallet's address — refusing to sign.");

        var outcome = await SolanaNetwork.SignSubmitAndFollowAsync(payload.Item1, walletKey, payload.Item2, privateKey, ct);
        return outcome.Outcome switch
        {
            SolSubmitOutcome.Included => new StakeOutcome(true, outcome.Signature, null),
            SolSubmitOutcome.Unknown or SolSubmitOutcome.Pending => new StakeOutcome(false, outcome.Signature, outcome.Message, Unclear: true),
            _ => new StakeOutcome(false, outcome.Signature, outcome.Message ?? "The network refused the transaction."),
        };
    }
}

// =====================================================================================================
// Cosmos Hub — delegate, claim, undelegate.
// =====================================================================================================

public sealed record AtomStakePosition(
    IReadOnlyList<CosmosDelegation> Delegations, IReadOnlyList<CosmosUnbonding> Unbonding)
{
    public decimal Staked => Delegations.Sum(d => d.StakedAtom);
    public decimal Rewards => Delegations.Sum(d => d.RewardAtom);
    public decimal Unbonding_ => Unbonding.Sum(u => u.Atom);
}

public sealed class CosmosStakingClient
{
    private const string DefaultServer = "https://cosmos-rest.publicnode.com";
    private const ulong ProvisionalGas = 300_000;
    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    private static IReadOnlyList<string> Servers() =>
        ChainEndpoints.Candidates("ATOM", DefaultServer).Select(s => s.TrimEnd('/')).ToList();

    public async Task<AtomStakePosition?> PositionAsync(string address, CancellationToken ct = default)
    {
        foreach (var server in Servers())
        {
            var delegations = await GetAsync($"{server}/cosmos/staking/v1beta1/delegations/{address}", ct);
            if (delegations is null) continue;
            var rewards = await GetAsync($"{server}/cosmos/distribution/v1beta1/delegators/{address}/rewards", ct);
            var unbonding = await GetAsync($"{server}/cosmos/staking/v1beta1/delegators/{address}/unbonding_delegations", ct);
            return new AtomStakePosition(
                CosmosStaking.ParseDelegations(delegations.Value, rewards),
                unbonding is { } u ? CosmosStaking.ParseUnbonding(u) : []);
        }
        return null;
    }

    public async Task<IReadOnlyList<CosmosValidator>> ValidatorsAsync(CancellationToken ct = default)
    {
        foreach (var server in Servers())
        {
            var body = await GetAsync($"{server}/cosmos/staking/v1beta1/validators?status=BOND_STATUS_BONDED&pagination.limit=200", ct);
            if (body is { } b && CosmosStaking.ParseValidators(b) is { Count: > 0 } list) return list;
        }
        return [];
    }

    /// <summary>Builds and simulates a staking transaction; the quote carries the gas and fee the node asked for.</summary>
    public async Task<(StakeQuote? Quote, string? Error)> PrepareAsync(
        string from, byte[] publicKey, string action, decimal amount, string counterparty,
        IReadOnlyList<CosmosMessage> messages, decimal needsAvailable, CancellationToken ct = default)
    {
        foreach (var server in Servers())
        {
            var host = new Uri(server).Host;
            var info = await GetAsync($"{server}/cosmos/base/tendermint/v1beta1/node_info", ct);
            if (info is null) continue;
            if (CosmosSendRules.ParseNodeNetwork(info.Value) != CosmosTransactions.HubChainId)
                return (null, $"{host} did not say it is on the Cosmos Hub. Nothing was sent.");

            var (accountStatus, accountBody) = await GetWithStatusAsync($"{server}/cosmos/auth/v1beta1/accounts/{from}", ct);
            var (lookup, account) = CosmosSendRules.ParseAccount(accountStatus, accountBody);
            if (lookup != CosmosAccountLookup.Found || account is null)
                return (null, lookup == CosmosAccountLookup.NotFound
                    ? "This Cosmos address has not received anything yet."
                    : $"Could not read this account from {host}. Nothing was sent.");
            if (account.PublicKey is { } onChain && !onChain.AsSpan().SequenceEqual(publicKey))
                return (null, "The chain has a different key on record for this address. Nothing was sent.");

            var balanceBody = await GetWithStatusAsync($"{server}/cosmos/bank/v1beta1/balances/{from}/by_denom?denom={CosmosHub.Denom}", ct);
            if (CosmosHub.ParseBalance(balanceBody.Status, balanceBody.Body) is not { } available)
                return (null, $"Could not read the balance from {host}. Nothing was sent.");

            var price = await GetAsync($"{server}/feemarket/v1/gas_price/{CosmosHub.Denom}", ct);
            if ((price is { } p ? CosmosSendRules.ParseGasPrice(p) : null) is not { } gasPrice)
                return (null, $"Could not read the network's gas price from {host}. Nothing was sent.");

            var (_, provisionalFee) = CosmosSendRules.FeeFor(ProvisionalGas, gasPrice);
            var draft = new CosmosTx(from, messages, "", 0, publicKey, account.AccountNumber, account.Sequence,
                provisionalFee, ProvisionalGas, CosmosTransactions.HubChainId);
            var (simStatus, simBody) = await PostAsync(Read, $"{server}/cosmos/tx/v1beta1/simulate",
                new { tx_bytes = Convert.ToBase64String(CosmosStaking.SimulationBytes(draft)) }, ct);
            var (gasUsed, simError) = CosmosSendRules.ParseSimulation(simStatus, simBody);
            if (gasUsed is not { } used)
                return (null, simError is null
                    ? $"{host} could not work out the gas. Nothing was sent."
                    : $"The network would refuse this: {simError}. Nothing was sent.");

            var (gasLimit, fee) = CosmosSendRules.FeeFor(used, gasPrice);
            var feeAtom = CosmosTransactions.ToAtom(fee);
            if (needsAvailable + feeAtom > available)
                return (null, $"Not enough available ATOM: {available:0.######} ATOM, needed {needsAvailable + feeAtom:0.######} ATOM including the fee.");

            var ready = draft with { Fee = fee, GasLimit = gasLimit };
            return (new StakeQuote("ATOM", action, amount, counterparty, (server, ready)), null);
        }
        return (null, "No Cosmos Hub node answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<StakeOutcome> SignAndBroadcastAsync(StakeQuote quote, Key key, CancellationToken ct = default)
    {
        if (quote.Payload is not ValueTuple<string, CosmosTx> payload) return new StakeOutcome(false, null, "Nothing to sign.");
        var (server, draft) = payload;

        var latest = await GetAsync($"{server}/cosmos/base/tendermint/v1beta1/blocks/latest", ct);
        if ((latest is { } lb ? CosmosSendRules.ParseLatestBlock(lb) : null) is not { } block || block.ChainId != CosmosTransactions.HubChainId)
            return new StakeOutcome(false, null, "Could not read the chain's latest block just before signing. Nothing was sent.");
        var (accountStatus, accountBody) = await GetWithStatusAsync($"{server}/cosmos/auth/v1beta1/accounts/{draft.Signer}", ct);
        var (_, account) = CosmosSendRules.ParseAccount(accountStatus, accountBody);
        if (account is null || account.Sequence != draft.Sequence)
            return new StakeOutcome(false, null, "This account has sent something since the review. Review it again. Nothing was sent.");

        var timeout = block.Height + CosmosSendRules.TimeoutBlocks;
        byte[] tx;
        string hash;
        try
        {
            (tx, hash) = CosmosStaking.Sign(draft with { TimeoutHeight = timeout }, key);
        }
        catch (Exception ex)
        {
            return new StakeOutcome(false, null, $"Could not sign: {ex.Message}");
        }

        var (submitStatus, submitBody) = await PostAsync(Submit, $"{server}/cosmos/tx/v1beta1/txs",
            new { tx_bytes = Convert.ToBase64String(tx), mode = "BROADCAST_MODE_SYNC" }, ct);
        var broadcast = CosmosSendRules.ParseBroadcast(submitStatus, submitBody);
        if (broadcast.Outcome == CosmosSubmitOutcome.Rejected) return new StakeOutcome(false, null, broadcast.Reason);

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
            var (status, body) = await GetWithStatusAsync($"{server}/cosmos/tx/v1beta1/txs/{hash}", ct);
            var (outcome, reason) = CosmosSendRules.ParseLookup(status, body);
            if (outcome == CosmosSubmitOutcome.Included) return new StakeOutcome(true, hash, null);
            if (outcome == CosmosSubmitOutcome.FailedFeeCharged) return new StakeOutcome(false, hash, reason ?? "The transaction failed on chain (the fee was charged).");
        }
        return new StakeOutcome(false, hash,
            $"The network has not confirmed it yet. It can only be included up to block {timeout}. Check {hash} on an explorer before trying again.",
            Unclear: true);
    }

    public static ulong Micro(decimal atom) =>
        CosmosTransactions.TryToMicro(atom, out var micro) ? micro : throw new ArgumentException("Enter a positive amount with at most 6 decimal places.");

    private static async Task<JsonElement?> GetAsync(string url, CancellationToken ct) => (await GetWithStatusAsync(url, ct)).Body;

    private static async Task<(int Status, JsonElement? Body)> GetWithStatusAsync(string url, CancellationToken ct)
    {
        try
        {
            using var res = await Read.GetAsync(url, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            return ((int)res.StatusCode, doc.RootElement.Clone());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (0, null);
        }
    }

    private static async Task<(int Status, JsonElement? Body)> PostAsync(HttpClient http, string url, object request, CancellationToken ct)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(url, request, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            return ((int)res.StatusCode, doc.RootElement.Clone());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (0, null);
        }
    }
}
