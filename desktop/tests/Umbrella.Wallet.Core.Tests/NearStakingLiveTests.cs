using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>NEAR staking reads against the real network (roadmap N.16). <c>Category=Live</c>: CI skips it.</summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class NearStakingLiveTests(ITestOutputHelper output)
{
    private static void Online()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
        ChainEndpoints.ClearAll();
    }

    [Fact]
    public async Task The_pools_are_the_validators_and_name_their_fees()
    {
        Online();
        var pools = await new NearStakingClient().PoolsAsync();

        Assert.True(pools.Count >= 10, $"{pools.Count} pools");
        Assert.All(pools, p => Assert.True(NearStaking.IsPoolId(p.Id), p.Id));
        Assert.True(pools.Count(p => p.FeePercent is >= 0 and <= 100) >= pools.Count / 2);
        output.WriteLine(string.Join(", ", pools.Take(5).Select(p => $"{p.Id} {p.FeePercent}%")));
    }

    [Fact]
    public async Task A_stakers_position_is_read_from_the_pools_themselves()
    {
        Online();
        // A public account that has staked with pools for years.
        var position = await new NearStakingClient().PositionAsync("root.near");

        Assert.NotNull(position);
        output.WriteLine(string.Join("; ", position!.Pools.Select(p =>
            $"{p.Pool} staked {NearTransactions.ToNear(p.Staked)} unstaked {NearTransactions.ToNear(p.Unstaked)} ready {p.Withdrawable}")));
    }

    [Fact]
    public async Task The_yield_comes_from_the_protocols_figures()
    {
        Online();
        var yield = await new NearStakingClient().YieldAsync();

        Assert.NotNull(yield);
        Assert.InRange(yield!.Value, 0.5m, 30m);
        output.WriteLine($"NEAR staking yield {yield}%");
    }
}
