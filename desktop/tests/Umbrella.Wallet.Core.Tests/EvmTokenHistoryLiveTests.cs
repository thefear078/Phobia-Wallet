using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Token history (roadmap N.15) from the real sources: USDC transfers of a cross-chain relayer, a public
/// address that moves USDC on every one of these networks each day. <c>Category=Live</c>: CI skips it.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class EvmTokenHistoryLiveTests
{
    private const string Relayer = "0xf70da97812CB96acDF810712Aa562db8dfA3dbEF";

    public static TheoryData<string, string> Sources() => new()
    {
        { "Ethereum", "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48" },
        { "Optimism", "0x0b2C639c533813f4Aa9D7837CAf62653d097Ff85" },
        { "Avalanche", "0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E" },
        { "Arbitrum One", "0xaf88d065e77c8cC2239327C5EDb3A432268e5831" },
        { "Polygon", "0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359" },
        { "Base", "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913" },
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Each_network_answers_with_the_tokens_transfers(string network, string usdc)
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
        ChainEndpoints.ClearAll();

        var rows = await new OnChainHistoryClient().GetEvmTokenHistoryAsync(
            Relayer, network, [new OnChainHistoryClient.HeldToken(usdc, "USDC", 6)]);

        Assert.True(rows.Count > 0, $"{network}: no USDC rows");
        Assert.All(rows, r => Assert.Equal("USDC", r.Asset));
        Assert.All(rows, r => Assert.True(r.UnixMs > 0));
        Assert.All(rows, r => Assert.True(decimal.Parse(r.Amount, System.Globalization.CultureInfo.InvariantCulture) > 0));
    }
}
