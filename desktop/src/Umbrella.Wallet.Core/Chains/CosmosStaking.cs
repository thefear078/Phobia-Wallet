using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Codecs;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One message of a Cosmos transaction: its type URL and its protobuf bytes.</summary>
public sealed record CosmosMessage(string TypeUrl, byte[] Value);

/// <summary>A Cosmos Hub transaction of any messages, signed by one account in SIGN_MODE_DIRECT.</summary>
public sealed record CosmosTx(
    string Signer,
    IReadOnlyList<CosmosMessage> Messages,
    string Memo,
    ulong TimeoutHeight,
    byte[] PublicKey,
    ulong AccountNumber,
    ulong Sequence,
    ulong Fee,
    ulong GasLimit,
    string ChainId);

/// <summary>A validator on the Hub's active set.</summary>
public sealed record CosmosValidator(string Address, string Moniker, decimal Commission, decimal BondedAtom);

/// <summary>One delegation of this wallet: to whom, how much, and the rewards it has earned so far.</summary>
public sealed record CosmosDelegation(string Validator, decimal StakedAtom, decimal RewardAtom);

/// <summary>ATOM on its way out of a delegation (21 days on the Hub).</summary>
public sealed record CosmosUnbonding(string Validator, decimal Atom, DateTimeOffset CompletesAt);

/// <summary>
/// Cosmos Hub staking: delegate ATOM to a validator, claim what it has earned, undelegate.
///
/// The same transaction shape as a send (<see cref="CosmosTransactions"/>) — body, auth info, sign doc,
/// SHA-256 and a compact secp256k1 signature — with staking messages in the body instead of a transfer.
/// <c>CosmosStakingTests</c> pins that a transfer built here is byte-identical to the send path's.
/// </summary>
public static class CosmosStaking
{
    public const string ValidatorPrefix = "cosmosvaloper";
    public const string MsgDelegateType = "/cosmos.staking.v1beta1.MsgDelegate";
    public const string MsgUndelegateType = "/cosmos.staking.v1beta1.MsgUndelegate";
    public const string MsgWithdrawRewardType = "/cosmos.distribution.v1beta1.MsgWithdrawDelegatorReward";
    public const string MsgSendType = "/cosmos.bank.v1beta1.MsgSend";

    /// <summary>How long undelegated ATOM takes to come back.</summary>
    public static readonly TimeSpan UnbondingTime = TimeSpan.FromDays(21);

    private const string PubKeyType = "/cosmos.crypto.secp256k1.PubKey";
    private const ulong SignModeDirect = 1;

    /// <summary>A checksum-verified <c>cosmosvaloper1…</c> address.</summary>
    public static bool IsValidValidator(string? address) =>
        Bech32.TryDecode(address?.Trim(), out var hrp, out var data) && hrp == ValidatorPrefix && data.Length == 20;

    public static CosmosMessage Delegate(string delegator, string validator, ulong micro) =>
        new(MsgDelegateType, DelegationBody(delegator, validator, micro));

    public static CosmosMessage Undelegate(string delegator, string validator, ulong micro) =>
        new(MsgUndelegateType, DelegationBody(delegator, validator, micro));

    public static CosmosMessage WithdrawReward(string delegator, string validator)
    {
        Check(delegator, validator);
        return new(MsgWithdrawRewardType, new Proto().String(1, delegator).String(2, validator).ToArray());
    }

    /// <summary>A plain transfer, so the generic encoder can be checked against the send path's.</summary>
    public static CosmosMessage Send(string from, string to, ulong micro)
    {
        var coin = new Proto().String(1, CosmosHub.Denom).String(2, micro.ToString(CultureInfo.InvariantCulture));
        return new(MsgSendType, new Proto().String(1, from).String(2, to).Message(3, coin).ToArray());
    }

    private static byte[] DelegationBody(string delegator, string validator, ulong micro)
    {
        Check(delegator, validator);
        if (micro == 0) throw new ArgumentException("A delegation moves a positive amount.", nameof(micro));
        var coin = new Proto().String(1, CosmosHub.Denom).String(2, micro.ToString(CultureInfo.InvariantCulture));
        return new Proto().String(1, delegator).String(2, validator).Message(3, coin).ToArray();
    }

    private static void Check(string delegator, string validator)
    {
        if (!CosmosHub.IsValidAddress(delegator)) throw new ArgumentException("Not a Cosmos address.", nameof(delegator));
        if (!IsValidValidator(validator)) throw new ArgumentException("Not a Cosmos validator address.", nameof(validator));
    }

    public static byte[] BodyBytes(CosmosTx tx)
    {
        if (tx.Messages.Count == 0) throw new ArgumentException("A transaction carries at least one message.", nameof(tx));
        var body = new Proto();
        foreach (var m in tx.Messages)
            body.Message(1, new Proto().String(1, m.TypeUrl).Bytes(2, m.Value));
        return body.String(2, tx.Memo).UInt64(3, tx.TimeoutHeight).ToArray();
    }

    public static byte[] AuthInfoBytes(CosmosTx tx)
    {
        if (tx.PublicKey.Length != 33) throw new ArgumentException("A compressed secp256k1 key is 33 bytes.", nameof(tx));
        var pubKey = new Proto().Bytes(1, tx.PublicKey);
        var pubKeyAny = new Proto().String(1, PubKeyType).Bytes(2, pubKey.ToArray());
        var modeInfo = new Proto().Message(1, new Proto().UInt64(1, SignModeDirect));
        var signer = new Proto().Message(1, pubKeyAny).Message(2, modeInfo).UInt64(3, tx.Sequence);
        var feeCoin = new Proto().String(1, CosmosHub.Denom).String(2, tx.Fee.ToString(CultureInfo.InvariantCulture));
        var fee = new Proto().Message(1, feeCoin).UInt64(2, tx.GasLimit);
        return new Proto().Message(1, signer).Message(2, fee).ToArray();
    }

    public static (byte[] TxBytes, string Hash) Sign(CosmosTx tx, Key key)
    {
        var own = key.PubKey.Compress().ToBytes();
        if (!own.AsSpan().SequenceEqual(tx.PublicKey) ||
            CosmosHub.AddressFromAccountId(key.PubKey.Compress().Hash.ToBytes()) != tx.Signer)
            throw new InvalidOperationException("The signing key does not belong to this address.");

        var body = BodyBytes(tx);
        var authInfo = AuthInfoBytes(tx);
        var signDoc = new Proto().Bytes(1, body).Bytes(2, authInfo).String(3, tx.ChainId).UInt64(4, tx.AccountNumber).ToArray();
        var signature = key.SignCompact(new uint256(SHA256.HashData(signDoc)), forceLowR: false).Signature;

        var bytes = new Proto().Bytes(1, body).Bytes(2, authInfo).Bytes(3, signature).ToArray();
        return (bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>The transaction with an empty signature, for the node's simulation (gas).</summary>
    public static byte[] SimulationBytes(CosmosTx tx) =>
        new Proto().Bytes(1, BodyBytes(tx)).Bytes(2, AuthInfoBytes(tx)).Bytes(3, [], always: true).ToArray();

    // --- reading the chain ---------------------------------------------------------------------------

    /// <summary>The bonded, unjailed validators from <c>/cosmos/staking/v1beta1/validators</c>, the most
    /// bonded first.</summary>
    public static IReadOnlyList<CosmosValidator> ParseValidators(JsonElement body)
    {
        if (!body.TryGetProperty("validators", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CosmosValidator>();
        foreach (var v in list.EnumerateArray())
        {
            var address = Str(v, "operator_address");
            if (address is null || !IsValidValidator(address)) continue;
            if (v.TryGetProperty("jailed", out var jailed) && jailed.ValueKind == JsonValueKind.True) continue;
            var moniker = v.TryGetProperty("description", out var d) ? Str(d, "moniker") ?? address : address;
            var rate = v.TryGetProperty("commission", out var c) && c.TryGetProperty("commission_rates", out var r)
                ? Dec(Str(r, "rate")) ?? 1m : 1m;
            var tokens = Dec(Str(v, "tokens")) ?? 0m;
            result.Add(new CosmosValidator(address, moniker.Trim(), rate, tokens / CosmosHub.MicroPerAtom));
        }
        return result.OrderByDescending(v => v.BondedAtom).ToList();
    }

    /// <summary>Delegations from <c>/cosmos/staking/v1beta1/delegations/{address}</c>, with the rewards
    /// from <c>/cosmos/distribution/v1beta1/delegators/{address}/rewards</c> beside each.</summary>
    public static IReadOnlyList<CosmosDelegation> ParseDelegations(JsonElement delegations, JsonElement? rewards)
    {
        var earned = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (rewards is { } rw && rw.TryGetProperty("rewards", out var rlist) && rlist.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rlist.EnumerateArray())
            {
                var validator = Str(r, "validator_address");
                if (validator is null || !r.TryGetProperty("reward", out var coins) || coins.ValueKind != JsonValueKind.Array) continue;
                foreach (var coin in coins.EnumerateArray())
                    if (Str(coin, "denom") == CosmosHub.Denom && Dec(Str(coin, "amount")) is { } micro)
                        earned[validator] = micro / CosmosHub.MicroPerAtom;
            }
        }

        if (!delegations.TryGetProperty("delegation_responses", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CosmosDelegation>();
        foreach (var d in list.EnumerateArray())
        {
            var validator = d.TryGetProperty("delegation", out var del) ? Str(del, "validator_address") : null;
            if (validator is null || !d.TryGetProperty("balance", out var balance) || Str(balance, "denom") != CosmosHub.Denom) continue;
            var micro = Dec(Str(balance, "amount")) ?? 0m;
            if (micro <= 0) continue;
            result.Add(new CosmosDelegation(validator, micro / CosmosHub.MicroPerAtom, earned.GetValueOrDefault(validator)));
        }
        return result;
    }

    /// <summary>Unbonding entries from <c>/cosmos/staking/v1beta1/delegators/{address}/unbonding_delegations</c>.</summary>
    public static IReadOnlyList<CosmosUnbonding> ParseUnbonding(JsonElement body)
    {
        if (!body.TryGetProperty("unbonding_responses", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CosmosUnbonding>();
        foreach (var u in list.EnumerateArray())
        {
            var validator = Str(u, "validator_address");
            if (validator is null || !u.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in entries.EnumerateArray())
            {
                var micro = Dec(Str(e, "balance")) ?? 0m;
                if (micro <= 0 || !DateTimeOffset.TryParse(Str(e, "completion_time"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var at)) continue;
                result.Add(new CosmosUnbonding(validator, micro / CosmosHub.MicroPerAtom, at));
            }
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Cosmos decimals ("0.050000000000000000") and integers, read exactly.</summary>
    private static decimal? Dec(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // An 18-place Cosmos decimal is within decimal's 28 significant digits for any realistic amount.
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private sealed class Proto
    {
        private readonly MemoryStream _ms = new();

        public Proto UInt64(int field, ulong value)
        {
            if (value == 0) return this;
            Varint((ulong)(field << 3));
            Varint(value);
            return this;
        }

        public Proto String(int field, string value) =>
            value.Length == 0 ? this : Bytes(field, Encoding.UTF8.GetBytes(value));

        public Proto Message(int field, Proto message) => Bytes(field, message.ToArray(), always: true);

        public Proto Bytes(int field, byte[] value, bool always = false)
        {
            if (value.Length == 0 && !always) return this;
            Varint((ulong)((field << 3) | 2));
            Varint((ulong)value.Length);
            _ms.Write(value);
            return this;
        }

        public byte[] ToArray() => _ms.ToArray();

        private void Varint(ulong v)
        {
            while (v >= 0x80)
            {
                _ms.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }

            _ms.WriteByte((byte)v);
        }
    }
}
