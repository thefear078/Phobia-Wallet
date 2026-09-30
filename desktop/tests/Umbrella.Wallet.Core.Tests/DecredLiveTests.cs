using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>The Decred balance against dcrdata itself. <c>Category=Live</c>: run by hand, never in CI.</summary>
[Trait("Category", "Live")]
public sealed class DecredLiveTests
{
    [Fact]
    public async Task Dcrdata_answers_for_an_address_the_wallet_derives()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
        ChainEndpoints.ClearAll();

        // Trust Wallet's vector account: never funded, so the answer must be a real zero — and it must be
        // an answer, since anything else would read as unknown.
        var balance = await new PublicChainBalanceClient().GetBalanceAsync(ChainId.Dcr, "DsVMHD5D86dpRnt2GPZvv4bYUJZg6B9Pzqa");

        Assert.NotNull(balance);
        Assert.Equal(0m, balance.NativeAmount);
        Assert.Equal("DCR", balance.Symbol);
    }
}
