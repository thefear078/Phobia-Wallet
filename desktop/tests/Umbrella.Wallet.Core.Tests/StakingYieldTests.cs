using System.Globalization;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The yearly yield shown on the staking cards. It was three strings typed into the wallet ("~3–5%",
/// "~6–7%", "~15–20%") beside positions and actions that were real — the one static thing on the page.
/// It is now each network's own rule applied to its current figures; these pin the arithmetic against
/// figures read from the networks on 2026-10-10.
/// </summary>
public sealed class StakingYieldTests
{
    [Fact]
    public void Cosmos_is_inflation_less_the_community_tax_on_the_supply_over_what_is_bonded()
    {
        // inflation 10%, community tax 2%, 337.6M ATOM bonded of 535.3M.
        var apr = StakingYield.Cosmos(0.10m, 0.02m, 337_625_893_419_145m, 535_339_586_806_730m);

        Assert.NotNull(apr);
        Assert.Equal(15.54m, Math.Round(apr!.Value, 2));
    }

    [Fact]
    public void Solana_is_validator_inflation_over_the_staked_share()
    {
        // 3.608% inflation to validators with two thirds of all SOL staked.
        var apr = StakingYield.Solana(0.03608m, 400_000_000m, 600_000_000m);

        Assert.Equal(5.41m, Math.Round(apr!.Value, 2));
    }

    [Fact]
    public void Tron_is_the_vote_reward_of_a_year_shared_by_the_votes_that_earn_it()
    {
        // 128 TRX a block to the voters of the 127 leading representatives; 44 billion votes cast.
        var apr = StakingYield.Tron(128_000_000, 44_000_000_000);

        Assert.Equal(3.06m, Math.Round(apr!.Value, 2));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(128_000_000, 0)]
    [InlineData(-1, 100)]
    public void Without_a_figure_there_is_no_yield_rather_than_a_guess(long pay, long votes)
    {
        Assert.Null(StakingYield.Tron(pay, votes));
        Assert.Null(StakingYield.Cosmos(0m, 0.02m, 1m, 1m));
        Assert.Null(StakingYield.Cosmos(0.1m, 0.02m, 0m, 1m));
        Assert.Null(StakingYield.Solana(0.03m, 0m, 1m));
        Assert.Null(StakingYield.Solana(0.03m, 2m, 1m));   // more staked than exists: a bad reading, not 1.5%
    }

    [Fact]
    public void The_vote_reward_is_read_from_the_chain_parameters()
    {
        using var doc = JsonDocument.Parse("""
            {"chainParameter":[{"key":"getWitnessPayPerBlock","value":8000000},
                               {"key":"getWitness127PayPerBlock","value":128000000},
                               {"key":"getAllowNewReward","value":1}]}
            """);

        Assert.Equal(128_000_000, TronStakingClient.VotePayPerBlock(doc.RootElement));
        using var none = JsonDocument.Parse("""{"chainParameter":[{"key":"getWitnessPayPerBlock","value":8000000}]}""");
        Assert.Null(TronStakingClient.VotePayPerBlock(none.RootElement));
    }

    [Fact]
    public void It_is_shown_to_one_decimal_as_an_approximation()
    {
        Assert.Equal("≈ 15.5%", StakingYield.Format(15.54m, CultureInfo.InvariantCulture));
    }
}
