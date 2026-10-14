using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Which of a chain's servers are answering is found out before any address is sent to one: every
/// server is asked something that names nothing of the user's, and one that hangs goes to the back.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ExplorerWarmUpTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(150);

    private static Func<string, CancellationToken, Task<bool>> Servers(params (string Url, int Ms, bool Ok)[] servers) =>
        async (url, ct) =>
        {
            var (_, ms, ok) = servers.Single(s => s.Url == url);
            await Task.Delay(ms, ct);
            return ok;
        };

    [Fact]
    public async Task A_server_that_hangs_is_benched_without_waiting_for_it()
    {
        var benched = new List<string>();
        var started = DateTimeOffset.UtcNow;

        var result = await ExplorerWarmUp.RunAsync(
            Servers(("hangs", 5_000, true), ("quick", 20, true), ("ok", 60, true)),
            ["hangs", "quick", "ok"], Patience, Grace, benched.Add, CancellationToken.None);

        Assert.Equal(new[] { "hangs" }, result);
        Assert.Equal(new[] { "hangs" }, benched);
        // Not the whole patience, let alone the five seconds the hanging one would have taken.
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromMilliseconds(700));
    }

    [Fact]
    public async Task A_server_that_refuses_is_benched_too()
    {
        var result = await ExplorerWarmUp.RunAsync(
            Servers(("refuses", 10, false), ("answers", 30, true)),
            ["refuses", "answers"], Patience, Grace, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "refuses" }, result);
    }

    [Fact]
    public async Task When_nothing_answers_nothing_is_benched()
    {
        // No network at all says nothing about one server against another.
        var benched = new List<string>();

        var result = await ExplorerWarmUp.RunAsync(
            Servers(("a", 10, false), ("b", 20, false)), ["a", "b"], Patience, Grace, benched.Add, CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(benched);
    }

    [Fact]
    public async Task Servers_that_all_answer_keep_their_order()
    {
        var result = await ExplorerWarmUp.RunAsync(
            Servers(("a", 40, true), ("b", 10, true), ("c", 70, true)), ["a", "b", "c"], Patience, Grace, _ => { }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task A_server_that_throws_counts_as_not_answering()
    {
        var result = await ExplorerWarmUp.RunAsync(
            (url, _) => url == "broken" ? throw new HttpRequestException("reset") : Task.FromResult(true),
            ["broken", "fine"], Patience, Grace, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "broken" }, result);
    }

    [Fact]
    public async Task One_server_alone_is_not_asked()
    {
        var asked = 0;
        var result = await ExplorerWarmUp.RunAsync(
            (_, _) => { asked++; return Task.FromResult(false); }, ["only"], Patience, Grace, _ => { }, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void The_questions_name_no_address_and_skip_a_chain_whose_server_the_user_chose()
    {
        ChainEndpoints.ClearAll();
        try
        {
            var all = ExplorerWarmUp.Questions();
            Assert.Contains(all, q => q.StartsWith("https://mempool.space/api", StringComparison.Ordinal));
            Assert.Contains(all, q => q.Contains("bitcore", StringComparison.Ordinal));
            // The newest block's height, and nothing else: no address, no key, no transaction.
            Assert.All(all, q => Assert.True(
                q.EndsWith("/blocks/tip/height", StringComparison.Ordinal) || q.EndsWith("/block/tip", StringComparison.Ordinal), q));

            ChainEndpoints.SetOverride("BTC", "https://my.own.server/api");
            var chosen = ExplorerWarmUp.Questions();
            Assert.DoesNotContain(chosen, q => q.Contains("mempool.space", StringComparison.Ordinal));
            Assert.DoesNotContain(chosen, q => q.Contains("my.own.server", StringComparison.Ordinal));   // never probed
        }
        finally
        {
            ChainEndpoints.ClearAll();
        }
    }
}
