using System.Security.Cryptography;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What frozen TRX pays for under Stake 2.0. Both give TRON Power, which is what votes.</summary>
public enum TronResource
{
    Bandwidth = 0,
    Energy = 1,
}

/// <summary>A Super Representative a stake's votes can go to.</summary>
public sealed record TronWitness(string Address, string Url, long Votes, int Rank);

/// <summary>Where this wallet's TRON staking stands.</summary>
public sealed record TronStakePosition(
    long FrozenSun,
    long VotesCast,
    string? VotedFor,
    long RewardSun,
    long UnfreezingSun,
    long WithdrawableSun,
    DateTimeOffset? NextUnfreeze);

/// <summary>
/// TRON Stake 2.0 transactions, checked before they are signed.
///
/// TronGrid builds the unsigned transaction (as it does for every TRON send here); this class reads the
/// protobuf it returned — <c>raw_data_hex</c>, the exact bytes the signature covers — and confirms it is
/// ONE contract of the expected type, from this wallet, for exactly the amount, resource and Super
/// Representative asked for, and that the id to be signed is the SHA-256 of those bytes. A server that
/// built anything else gets a refusal instead of a signature.
/// </summary>
public static class TronStaking
{
    // Contract types (core/Tron.proto, Transaction.Contract.ContractType).
    public const int VoteWitness = 4;
    public const int WithdrawBalance = 13;
    public const int FreezeBalanceV2 = 54;
    public const int UnfreezeBalanceV2 = 55;
    public const int WithdrawExpireUnfreeze = 56;

    /// <summary>TRON's smallest unit: one TRX is a million sun.</summary>
    public const long SunPerTrx = 1_000_000;

    /// <summary>How long unfrozen TRX waits before it can be withdrawn (Stake 2.0).</summary>
    public static readonly TimeSpan UnfreezeWait = TimeSpan.FromDays(14);

    /// <summary>The single contract inside a transaction's raw bytes: its type and its parameter's value.</summary>
    public static (int Type, byte[] Value)? ReadContract(byte[] rawData)
    {
        (int, byte[])? found = null;
        foreach (var (field, wire, value, bytes) in Fields(rawData))
        {
            if (field != 11 || wire != 2) continue;
            if (found is not null) return null;   // more than one contract: never what was asked for
            int type = 0;
            byte[]? parameterValue = null;
            foreach (var (f, w, v, b) in Fields(bytes!))
            {
                if (f == 1 && w == 0) type = (int)v;
                else if (f == 2 && w == 2)
                    foreach (var (af, aw, _, ab) in Fields(b!))
                        if (af == 2 && aw == 2) parameterValue = ab;
            }
            if (parameterValue is null) return null;
            found = (type, parameterValue);
        }
        return found;
    }

    /// <summary>The id a TRON transaction is signed by: SHA-256 of its raw bytes, hex.</summary>
    public static string TxId(byte[] rawData) => Convert.ToHexString(SHA256.HashData(rawData)).ToLowerInvariant();

    /// <summary>Null when <paramref name="rawData"/> freezes exactly this, from this owner; else why not.</summary>
    public static string? CheckFreeze(byte[] rawData, byte[] owner, long sun, TronResource resource) =>
        CheckAmount(rawData, FreezeBalanceV2, owner, sun, resource);

    /// <summary>Null when <paramref name="rawData"/> unfreezes exactly this, from this owner; else why not.</summary>
    public static string? CheckUnfreeze(byte[] rawData, byte[] owner, long sun, TronResource resource) =>
        CheckAmount(rawData, UnfreezeBalanceV2, owner, sun, resource);

    /// <summary>Null when <paramref name="rawData"/> casts exactly these votes, all of them for one Super
    /// Representative, from this owner; else why not.</summary>
    public static string? CheckVote(byte[] rawData, byte[] owner, byte[] witness, long votes)
    {
        if (ReadContract(rawData) is not { } c) return "The transaction does not hold exactly one contract.";
        if (c.Type != VoteWitness) return $"Expected a vote, got contract type {c.Type}.";
        byte[]? from = null;
        var ballots = new List<(byte[] To, long Count)>();
        foreach (var (f, w, _, b) in Fields(c.Value))
        {
            if (f == 1 && w == 2) from = b;
            else if (f == 2 && w == 2)
            {
                byte[]? to = null;
                long count = 0;
                foreach (var (vf, vw, vv, vb) in Fields(b!))
                {
                    if (vf == 1 && vw == 2) to = vb;
                    else if (vf == 2 && vw == 0) count = (long)vv;
                }
                if (to is null) return "A vote names no Super Representative.";
                ballots.Add((to, count));
            }
        }
        if (from is null || !from.AsSpan().SequenceEqual(owner)) return "The vote is not from this wallet.";
        if (ballots.Count != 1) return "The vote is not for exactly one Super Representative.";
        if (!ballots[0].To.AsSpan().SequenceEqual(witness)) return "The vote is for a different Super Representative.";
        if (ballots[0].Count != votes) return "The vote count is not the one asked for.";
        return null;
    }

    /// <summary>Null when <paramref name="rawData"/> is a contract of <paramref name="type"/> that carries
    /// only this owner (claiming rewards, withdrawing unfrozen TRX); else why not.</summary>
    public static string? CheckOwnerOnly(byte[] rawData, int type, byte[] owner)
    {
        if (ReadContract(rawData) is not { } c) return "The transaction does not hold exactly one contract.";
        if (c.Type != type) return $"Expected contract type {type}, got {c.Type}.";
        byte[]? from = null;
        foreach (var (f, w, _, b) in Fields(c.Value))
        {
            if (f == 1 && w == 2) from = b;
            else return "The transaction carries more than the owner.";
        }
        return from is not null && from.AsSpan().SequenceEqual(owner) ? null : "The transaction is not from this wallet.";
    }

    private static string? CheckAmount(byte[] rawData, int type, byte[] owner, long sun, TronResource resource)
    {
        if (ReadContract(rawData) is not { } c) return "The transaction does not hold exactly one contract.";
        if (c.Type != type) return $"Expected contract type {type}, got {c.Type}.";
        byte[]? from = null;
        long amount = 0;
        var res = 0;   // BANDWIDTH is protobuf's default and is left out of the bytes
        foreach (var (f, w, v, b) in Fields(c.Value))
        {
            if (f == 1 && w == 2) from = b;
            else if (f == 2 && w == 0) amount = (long)v;
            else if (f == 3 && w == 0) res = (int)v;
        }
        if (from is null || !from.AsSpan().SequenceEqual(owner)) return "The transaction is not from this wallet.";
        if (amount != sun) return "The amount is not the one asked for.";
        if (res != (int)resource) return "The resource is not the one asked for.";
        return null;
    }

    /// <summary>The fields of one protobuf message: number, wire type, varint value or length-delimited bytes.</summary>
    private static IEnumerable<(int Field, int Wire, ulong Value, byte[]? Bytes)> Fields(byte[] message)
    {
        var at = 0;
        var list = new List<(int, int, ulong, byte[]?)>();
        while (at < message.Length)
        {
            var key = Varint(message, ref at);
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);
            switch (wire)
            {
                case 0:
                    list.Add((field, 0, Varint(message, ref at), null));
                    break;
                case 1:
                    if (at + 8 > message.Length) throw new FormatException("Truncated protobuf.");
                    at += 8;
                    break;
                case 2:
                    var length = (int)Varint(message, ref at);
                    if (length < 0 || at + length > message.Length) throw new FormatException("Truncated protobuf.");
                    list.Add((field, 2, 0, message.AsSpan(at, length).ToArray()));
                    at += length;
                    break;
                case 5:
                    if (at + 4 > message.Length) throw new FormatException("Truncated protobuf.");
                    at += 4;
                    break;
                default:
                    throw new FormatException($"Unsupported protobuf wire type {wire}.");
            }
        }
        return list;
    }

    private static ulong Varint(byte[] data, ref int at)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (at >= data.Length) throw new FormatException("Truncated varint.");
            var b = data[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new FormatException("Varint too long.");
    }
}
