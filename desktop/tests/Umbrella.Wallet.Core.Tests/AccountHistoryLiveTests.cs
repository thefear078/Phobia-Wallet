using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The account history readers against the real default servers, for accounts known to have activity.
/// <c>Category=Live</c>: run by hand, never in CI — the suite must not fail because a public node is busy.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class AccountHistoryLiveTests
{
    private static void Online()
    {
        ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
    }

    [Fact]
    public async Task Xrp_history_reads_real_payments()
    {
        Online();
        var rows = await new AccountHistoryClient().GetXrpAsync("rEb8TK3gBgk5auZkwc6sHnwrGVJH8DuaLh", limit: 10);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("XRP", r.Asset));
        Assert.All(rows, r => Assert.True(r.UnixMs > new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task Stellar_history_reads_real_payments()
    {
        Online();
        var rows = await new AccountHistoryClient().GetStellarAsync("GAHK7EEG2WWHVKDNT4CEQFZGKF2LGDSW2IVM4S5DP42RBW3K6BTODB4A", limit: 10);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("XLM", r.Asset));
    }

    [Fact]
    public async Task Near_history_reads_real_transfers()
    {
        Online();
        // An exchange account with frequent plain transfers — including amounts past what a decimal holds
        // in yocto, which is the case the parser has to scale rather than drop.
        var rows = await new AccountHistoryClient().GetNearAsync("binance1.near", limit: 10);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("NEAR", r.Asset));
        Assert.All(rows, r => Assert.StartsWith("https://nearblocks.io/txns/", r.Explorer));
    }

    [Fact]
    public async Task Cosmos_history_reads_the_whole_index_of_an_archive_node()
    {
        Online();
        // An account with seven known sends going back to 2022 (height 12930884). A pruned server answered
        // with none of them — or four — depending on which machine behind its address took the request.
        var rows = await new AccountHistoryClient().GetCosmosAsync("cosmos19rl4cm2hmr8afy4kldpxz3fka4jguq0auqdal4");
        Assert.Contains(rows, r => r.Kind == "Sent" && r.Hash.StartsWith("5CC770CE67", StringComparison.Ordinal) && r.UnixMs < new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
        Assert.Contains(rows, r => r.Kind == "Received");
        Assert.All(rows, r => Assert.Equal("ATOM", r.Asset));
        Assert.All(rows, r => Assert.StartsWith("https://www.mintscan.io/cosmos/tx/", r.Explorer));
    }

    public static TheoryData<int> EvmSideSources() => new() { 0, 1, 2 };

    [Theory]
    [MemberData(nameof(EvmSideSources))]
    public async Task Each_evm_network_with_a_keyless_indexer_reads_real_transfers(int which)
    {
        Online();
        var (network, api, asset, explorer) = OnChainHistoryClient.EvmSideHistory[which];
        // A widely used public address with activity on every one of these networks.
        var rows = await new OnChainHistoryClient().GetEvmAsync("0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045", api, asset, explorer);
        Assert.True(rows.Count > 0, $"{network}: no rows from {api}");
        Assert.All(rows, r => Assert.Equal(asset, r.Asset));
        Assert.All(rows, r => Assert.StartsWith(explorer, r.Explorer));
    }

    [Fact]
    public async Task Polkadot_history_reads_asset_hub_and_the_relay_chain()
    {
        Online();
        var rows = await new AccountHistoryClient().GetPolkadotAsync("15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5", limit: 10);
        Assert.Contains(rows, r => r.Explorer.StartsWith("https://assethub-polkadot.subscan.io/extrinsic/", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Explorer.StartsWith("https://polkadot.subscan.io/extrinsic/", StringComparison.Ordinal));
        // Its own sends on Asset Hub link by the hash they were submitted under, as the Send screen does.
        Assert.Contains(rows, r => r.Kind == "Sent" && r.Explorer.Contains("/extrinsic/0x", StringComparison.Ordinal));
        Assert.All(rows, r => Assert.Equal("DOT", r.Asset));
    }
}
