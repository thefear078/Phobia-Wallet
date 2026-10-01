using System.Text.Json;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// When Tronscan rate-limits, TRON's tokens are read from TronGrid instead — before, USDT disappeared
/// from the holdings and the total until Tronscan answered again. TronGrid gives contracts and raw
/// amounts only, so just the known stablecoins are read from it; unknown contracts (airdrops, mostly)
/// wait for Tronscan.
/// </summary>
public class TronTokenFallbackTests
{
    [Fact]
    public void KnownStablecoinsAreReadAndUnknownContractsAreLeftForTronscan()
    {
        // The shape of a real answer for an account holding USDT and four airdropped tokens.
        const string json = """
            {"data":[{"address":"41c8599111f29c1e1e061265b4af93ea1f274ad78a","trc20":[
              {"TQGaH1PigTUJsSbCootv52Hi92Gx2Hbmw8":"1000000"},
              {"TCMjU3taxp19xNWMFQdQw45CYwQcqrsYqA":"1000000000"},
              {"TVh4nokXoSxQGxh7T6Tn6NTb2uSUhAhLwb":"55178000000"},
              {"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t":"8235005783"}]}],"success":true}
            """;
        using var doc = JsonDocument.Parse(json);
        var tokens = PublicChainBalanceClient.ParseTronGridTokens(doc.RootElement)!;

        var usdt = Assert.Single(tokens);
        Assert.Equal("USDT", usdt.Symbol);
        Assert.Equal(8235.005783m, usdt.Amount);
    }

    [Fact]
    public void AnInactiveAccountHasNoTokensButNoAnswerIsNotEmpty()
    {
        using (var empty = JsonDocument.Parse("""{"data":[],"success":true}"""))
            Assert.Empty(PublicChainBalanceClient.ParseTronGridTokens(empty.RootElement)!);
        using (var refused = JsonDocument.Parse("""{"Error":"request rate exceeded"}"""))
            Assert.Null(PublicChainBalanceClient.ParseTronGridTokens(refused.RootElement));
    }
}
