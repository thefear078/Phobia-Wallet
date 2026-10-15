namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// What staking pays in a year, worked out from each network's own figures instead of written into the
/// wallet as "~6–7%".
///
/// A percentage typed into the source is right the day it is typed. These are the networks' stated
/// rules applied to their current state — the reward issued, shared among what is staked — before any
/// validator's commission, which differs per validator and is shown beside each of them. Pure
/// arithmetic; the callers fetch the inputs.
/// </summary>
public static class StakingYield
{
    /// <summary>TRON produces a block every three seconds.</summary>
    public const long TronBlocksPerYear = 28_800L * 365;

    /// <summary>The Super Representatives whose voters share the vote reward.</summary>
    public const int TronRewardedWitnesses = 127;

    /// <summary>
    /// TRON, percent a year: the reward paid each block to the voters of the 127 leading Super
    /// Representatives (the <c>getWitness127PayPerBlock</c> chain parameter, in sun), shared by every vote
    /// cast for them. One vote is one frozen TRX.
    /// </summary>
    public static decimal? Tron(long votePayPerBlockSun, long votesForRewardedWitnesses) =>
        votePayPerBlockSun <= 0 || votesForRewardedWitnesses <= 0
            ? null
            : votePayPerBlockSun / 1_000_000m * TronBlocksPerYear / votesForRewardedWitnesses * 100m;

    /// <summary>
    /// Solana, percent a year: the inflation paid to validators and their stakers
    /// (<c>getInflationRate</c>), over the share of all SOL that is staked.
    /// </summary>
    public static decimal? Solana(decimal validatorInflation, decimal stakedLamports, decimal totalLamports) =>
        validatorInflation <= 0 || stakedLamports <= 0 || totalLamports <= 0 || stakedLamports > totalLamports
            ? null
            : validatorInflation / (stakedLamports / totalLamports) * 100m;

    /// <summary>
    /// Cosmos Hub, percent a year: the inflation, less the community tax, issued on the whole supply
    /// and paid to what is bonded.
    /// </summary>
    public static decimal? Cosmos(decimal inflation, decimal communityTax, decimal bonded, decimal supply) =>
        inflation <= 0 || bonded <= 0 || supply <= 0 || communityTax < 0 || communityTax >= 1
            ? null
            : inflation * (1 - communityTax) * supply / bonded * 100m;

    /// <summary>"≈ 15.5%" — one decimal is all these figures are good for.</summary>
    public static string Format(decimal percent, IFormatProvider culture) =>
        "≈ " + percent.ToString("0.0", culture) + "%";
}
