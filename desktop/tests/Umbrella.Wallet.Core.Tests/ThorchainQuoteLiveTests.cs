using System.Net.Http;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Real quotes from the THORNode endpoints the swap screen asks. In October 2026 every one of the old
/// list had stopped answering — Nine Realms' host no longer resolved, Liquify's old host failed TLS and
/// the cosmos.directory proxy returned 503 — and nothing said so until a swap was tried. This asks each
/// node for the same quote, with the price limit (roadmap N.9), and needs at least one to answer.
///
/// <c>Category=Live</c>: CI skips it; run it with <c>dotnet test --filter Category=Live</c>. The
/// destination is a well-known burn address, never a user's.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class ThorchainQuoteLiveTests
{
    private const string Burn = "0x000000000000000000000000000000000000dEaD";

    [Fact]
    public async Task At_least_one_node_quotes_a_swap_with_its_price_limit()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var query = ThorchainSwapClient.QuoteQuery("BTC.BTC", "ETH.ETH", 1_000_000, Burn);
        var answered = new List<string>();
        var report = new List<string>();

        foreach (var host in ThorchainSwapClient.QuoteEndpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, host + query);
                req.Headers.TryAddWithoutValidation("x-client-id", "umbrella-wallet");
                using var res = await http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                var (quote, error) = ThorchainSwapClient.ParseQuote(body, "BTC", "ETH", 0.01m);
                if (quote is null)
                {
                    report.Add($"{host}: {(int)res.StatusCode} {error}");
                    continue;
                }

                // A node that answers must honour the limit: a memo without one would swap at any price.
                Assert.True(quote.MinOut > 0, $"{host} quoted without a price limit: {quote.Memo}");
                Assert.True(quote.MinOut < quote.ExpectedOut, host);
                Assert.Contains(Burn, quote.Memo, StringComparison.OrdinalIgnoreCase);
                answered.Add(host);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                report.Add($"{host}: {ex.GetType().Name} {ex.Message}");
            }
        }

        Assert.True(answered.Count > 0, "No THORNode answered:\n" + string.Join('\n', report));
    }
}
