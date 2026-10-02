using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>Where one stake account stands, read from the chain.</summary>
public enum SolanaStakeState
{
    /// <summary>Created but delegated to nobody: all of it can be withdrawn.</summary>
    Initialized,

    /// <summary>Delegated this epoch; it starts earning from the next one.</summary>
    Activating,

    /// <summary>Delegated and earning.</summary>
    Active,

    /// <summary>Undelegated this epoch; it is free from the next one.</summary>
    Deactivating,

    /// <summary>Undelegated and cooled down: it can be withdrawn.</summary>
    Inactive,
}

/// <summary>One stake account the wallet controls.</summary>
public sealed record SolanaStakeAccount(
    string Address, ulong Lamports, ulong RentReserve, string? Voter, ulong Delegated, SolanaStakeState State)
{
    /// <summary>What can be withdrawn now: everything once nothing is delegated or the cooldown is over.</summary>
    public bool CanWithdraw => State is SolanaStakeState.Initialized or SolanaStakeState.Inactive;

    /// <summary>What can be undelegated now.</summary>
    public bool CanDeactivate => State is SolanaStakeState.Activating or SolanaStakeState.Active;
}

/// <summary>A validator a stake can be delegated to.</summary>
public sealed record SolanaValidator(string VoteAccount, ulong ActivatedLamports, int Commission);

/// <summary>
/// Native Solana staking with the Stake program: the stake account is created at an address derived
/// from the wallet's own key and a seed ("phobia-stake-0", "-1", …), so the wallet's key alone creates,
/// finds and controls it — one signature, no second keypair to keep. The wallet is both staker and
/// withdrawer, and there is no lockup.
///
/// Instruction layouts follow the Stake and System programs (bincode, little-endian), and are pinned by
/// <c>SolanaStakeTests</c> byte for byte; a live validator's simulation checks a whole stake transaction.
/// </summary>
public static class SolanaStake
{
    public const string ProgramAddress = "Stake11111111111111111111111111111111111111";
    public const string RentSysvar = "SysvarRent111111111111111111111111111111111";
    public const string ClockSysvar = "SysvarC1ock11111111111111111111111111111111";
    public const string StakeHistorySysvar = "SysvarStakeHistory1111111111111111111111111";
    public const string StakeConfigAddress = "StakeConfig11111111111111111111111111111111";

    /// <summary>A stake account's size in bytes.</summary>
    public const ulong AccountSpace = 200;

    /// <summary>How many seeds the wallet looks at for its own stake accounts.</summary>
    public const int SeedSlots = 16;

    /// <summary>The epoch that means "never" in a delegation (u64::MAX).</summary>
    public const ulong NeverEpoch = ulong.MaxValue;

    private static readonly byte[] SystemProgramId = new byte[32];

    public static byte[] ProgramId => Key(ProgramAddress);

    public static string SeedFor(int index) => $"phobia-stake-{index}";

    /// <summary>System program's create_with_seed: SHA-256(base ‖ seed ‖ owner).</summary>
    public static byte[] AddressWithSeed(byte[] basePublicKey, string seed, byte[] owner)
    {
        if (basePublicKey.Length != 32 || owner.Length != 32) throw new ArgumentException("Keys are 32 bytes.");
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        if (seedBytes.Length > 32) throw new ArgumentException("A seed is at most 32 bytes.", nameof(seed));
        return SHA256.HashData([.. basePublicKey, .. seedBytes, .. owner]);
    }

    /// <summary>The wallet's stake account for seed slot <paramref name="index"/>.</summary>
    public static byte[] StakeAccountFor(byte[] wallet, int index) => AddressWithSeed(wallet, SeedFor(index), ProgramId);

    /// <summary>System program, CreateAccountWithSeed (3): the funder pays, the base signs.</summary>
    public static SolanaInstruction CreateAccountWithSeed(
        byte[] funder, byte[] created, byte[] basePublicKey, string seed, ulong lamports, ulong space, byte[] owner)
    {
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        using var data = new MemoryStream();
        data.Write(BitConverter.GetBytes(3u));
        data.Write(basePublicKey);
        data.Write(BitConverter.GetBytes((ulong)seedBytes.Length));
        data.Write(seedBytes);
        data.Write(BitConverter.GetBytes(lamports));
        data.Write(BitConverter.GetBytes(space));
        data.Write(owner);
        return new SolanaInstruction(SystemProgramId,
        [
            new SolanaAccountMeta(funder, true, true),
            new SolanaAccountMeta(created, false, true),
            new SolanaAccountMeta(basePublicKey, true, false),
        ], data.ToArray());
    }

    /// <summary>Stake program, Initialize (0): who may delegate (staker) and withdraw (withdrawer); no lockup.</summary>
    public static SolanaInstruction Initialize(byte[] stake, byte[] staker, byte[] withdrawer)
    {
        using var data = new MemoryStream();
        data.Write(BitConverter.GetBytes(0u));
        data.Write(staker);
        data.Write(withdrawer);
        data.Write(BitConverter.GetBytes(0L));    // lockup: unix timestamp
        data.Write(BitConverter.GetBytes(0UL));   // lockup: epoch
        data.Write(new byte[32]);                 // lockup: custodian
        return new SolanaInstruction(ProgramId,
        [
            new SolanaAccountMeta(stake, false, true),
            new SolanaAccountMeta(Key(RentSysvar), false, false),
        ], data.ToArray());
    }

    /// <summary>Stake program, DelegateStake (2).</summary>
    public static SolanaInstruction Delegate(byte[] stake, byte[] voteAccount, byte[] staker) => new(ProgramId,
    [
        new SolanaAccountMeta(stake, false, true),
        new SolanaAccountMeta(voteAccount, false, false),
        new SolanaAccountMeta(Key(ClockSysvar), false, false),
        new SolanaAccountMeta(Key(StakeHistorySysvar), false, false),
        new SolanaAccountMeta(Key(StakeConfigAddress), false, false),
        new SolanaAccountMeta(staker, true, false),
    ], BitConverter.GetBytes(2u));

    /// <summary>Stake program, Deactivate (5): undelegate; the stake is free after the epoch ends.</summary>
    public static SolanaInstruction Deactivate(byte[] stake, byte[] staker) => new(ProgramId,
    [
        new SolanaAccountMeta(stake, false, true),
        new SolanaAccountMeta(Key(ClockSysvar), false, false),
        new SolanaAccountMeta(staker, true, false),
    ], BitConverter.GetBytes(5u));

    /// <summary>Stake program, Withdraw (4): lamports out of the stake account to <paramref name="to"/>.</summary>
    public static SolanaInstruction Withdraw(byte[] stake, byte[] to, byte[] withdrawer, ulong lamports) => new(ProgramId,
    [
        new SolanaAccountMeta(stake, false, true),
        new SolanaAccountMeta(to, false, true),
        new SolanaAccountMeta(Key(ClockSysvar), false, false),
        new SolanaAccountMeta(Key(StakeHistorySysvar), false, false),
        new SolanaAccountMeta(withdrawer, true, false),
    ], [.. BitConverter.GetBytes(4u), .. BitConverter.GetBytes(lamports)]);

    /// <summary>
    /// A new stake in one transaction: create the account at the next free seed with the amount plus
    /// its rent reserve, make the wallet its staker and withdrawer, delegate it.
    /// </summary>
    public static IReadOnlyList<SolanaInstruction> StakeInstructions(
        byte[] wallet, int seedIndex, byte[] voteAccount, ulong stakeLamports, ulong rentReserve)
    {
        var seed = SeedFor(seedIndex);
        var stake = AddressWithSeed(wallet, seed, ProgramId);
        return
        [
            CreateAccountWithSeed(wallet, stake, wallet, seed, checked(stakeLamports + rentReserve), AccountSpace, ProgramId),
            Initialize(stake, wallet, wallet),
            Delegate(stake, voteAccount, wallet),
        ];
    }

    /// <summary>
    /// A stake account as the RPC's "jsonParsed" encoding describes it, placed against the current
    /// epoch. Null when the account is not a stake account this wallet controls.
    /// </summary>
    public static SolanaStakeAccount? Parse(string address, JsonElement account, ulong currentEpoch, string wallet)
    {
        if (account.ValueKind != JsonValueKind.Object) return null;
        if (!account.TryGetProperty("lamports", out var lamportsEl) || !lamportsEl.TryGetUInt64(out var lamports)) return null;
        if (!account.TryGetProperty("data", out var data) || !data.TryGetProperty("parsed", out var parsed)) return null;
        var type = parsed.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (!parsed.TryGetProperty("info", out var info) || !info.TryGetProperty("meta", out var meta)) return null;

        // Only accounts this wallet can withdraw from are its own.
        var withdrawer = meta.TryGetProperty("authorized", out var auth) && auth.TryGetProperty("withdrawer", out var w)
            ? w.GetString() : null;
        if (!string.Equals(withdrawer, wallet, StringComparison.Ordinal)) return null;

        var reserve = U64(meta, "rentExemptReserve") ?? 0;
        if (type == "initialized")
            return new SolanaStakeAccount(address, lamports, reserve, null, 0, SolanaStakeState.Initialized);
        if (type != "delegated") return null;

        if (!info.TryGetProperty("stake", out var stake) || !stake.TryGetProperty("delegation", out var delegation)) return null;
        var voter = delegation.TryGetProperty("voter", out var v) ? v.GetString() : null;
        var delegated = U64(delegation, "stake") ?? 0;
        var activation = U64(delegation, "activationEpoch") ?? 0;
        var deactivation = U64(delegation, "deactivationEpoch") ?? NeverEpoch;

        var state = deactivation != NeverEpoch
            ? (deactivation >= currentEpoch ? SolanaStakeState.Deactivating : SolanaStakeState.Inactive)
            : (activation >= currentEpoch ? SolanaStakeState.Activating : SolanaStakeState.Active);
        return new SolanaStakeAccount(address, lamports, reserve, voter, delegated, state);
    }

    /// <summary>Validators that vote and keep a commission at or below <paramref name="maxCommission"/>,
    /// the most-staked first, from getVoteAccounts' "current" list.</summary>
    public static IReadOnlyList<SolanaValidator> ParseValidators(JsonElement result, int maxCommission = 10, int take = 12)
    {
        if (!result.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Array) return [];
        var list = new List<SolanaValidator>();
        foreach (var v in current.EnumerateArray())
        {
            var vote = v.TryGetProperty("votePubkey", out var vp) ? vp.GetString() : null;
            var stake = v.TryGetProperty("activatedStake", out var s) && s.TryGetUInt64(out var st) ? st : 0;
            var commission = v.TryGetProperty("commission", out var c) && c.TryGetInt32(out var cm) ? cm : 100;
            var voting = !v.TryGetProperty("epochVoteAccount", out var ev) || ev.ValueKind != JsonValueKind.False;
            if (vote is null || !voting || commission > maxCommission || stake == 0) continue;
            list.Add(new SolanaValidator(vote, stake, commission));
        }
        return list.OrderByDescending(x => x.ActivatedLamports).Take(take).ToList();
    }

    private static ulong? U64(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetUInt64(out var n) => n,
            JsonValueKind.String when ulong.TryParse(p.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static byte[] Key(string address) =>
        SolanaKeys.TryDecode(address, out var key) ? key : throw new InvalidOperationException($"Bad built-in address {address}.");
}
