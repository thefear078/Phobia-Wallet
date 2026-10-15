using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// History for the EVM networks no Etherscan-shaped indexer serves without a key — Base, Arbitrum,
/// Linea, BNB Chain, Polygon — read from 3xpl's list of what changed an address's balance.
///
/// The figures and hashes are made up; the shape is the one the service answers with.
/// </summary>
public sealed class EvmEventsHistoryTests
{
    private const string Module = "base-main";
    private const string Explorer = "https://basescan.org/tx/";

    private static string Event(string tx, string effect, string? extra = null, bool failed = false, string time = "2026-01-02T03:04:05.000000Z") =>
        "{\"block\":1,\"transaction\":\"" + tx + "\",\"sort_key\":1,\"time\":\"" + time + "\",\"currency\":\"ethereum\"," +
        "\"effect\":\"" + effect + "\",\"failed\":" + (failed ? "true" : "false") + "," +
        "\"extra\":" + (extra is null ? "null" : "\"" + extra + "\"") + ",\"extra_indexed\":null}";

    private static string Answer(params string[] events) =>
        "{\"data\":{\"events\":{\"" + Module + "\":[" + string.Join(",", events) + "]}},\"context\":{\"code\":200}}";

    [Fact]
    public void Money_coming_in_is_one_received_row()
    {
        var rows = OnChainHistoryClient.ParseEvmEvents(Answer(Event("0xaa", "+250000000000000000")), Module, "ETH", Explorer);

        var row = Assert.Single(rows);
        Assert.Equal("Received", row.Kind);
        Assert.Equal("ETH", row.Asset);
        Assert.Equal("0.25", row.Amount);
        Assert.Equal(Explorer + "0xaa", row.Explorer);
        Assert.Equal("0xaa", row.Hash);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeMilliseconds(), row.UnixMs);
    }

    [Fact]
    public void A_send_shows_what_was_sent_and_not_what_the_network_took()
    {
        // The amount, then the fee and the burnt part of it, each its own event.
        var rows = OnChainHistoryClient.ParseEvmEvents(
            Answer(Event("0xbb", "-1000000000000000000"), Event("0xbb", "-21000000000000", "f"), Event("0xbb", "-9000000000000", "b")),
            Module, "ETH", Explorer);

        var row = Assert.Single(rows);
        Assert.Equal("Sent", row.Kind);
        Assert.Equal("1", row.Amount);   // as every other chain's rows: the amount, fee apart
    }

    [Fact]
    public void A_transaction_that_moved_nothing_is_no_row()
    {
        // A contract call paid for by this address, and somebody's zero-value "dusting" of it.
        var rows = OnChainHistoryClient.ParseEvmEvents(
            Answer(Event("0xcc", "-0"), Event("0xcc", "-30000000000000", "f"), Event("0xdd", "+0")),
            Module, "ETH", Explorer);

        Assert.Empty(rows);
    }

    [Fact]
    public void A_failed_transaction_is_no_row()
    {
        var rows = OnChainHistoryClient.ParseEvmEvents(Answer(Event("0xee", "-500000000000000000", failed: true)), Module, "ETH", Explorer);

        Assert.Empty(rows);
    }

    [Fact]
    public void What_went_out_and_came_back_in_one_transaction_is_netted()
    {
        // 1 out to a contract, 0.4 returned by it inside the same transaction: 0.6 sent.
        var rows = OnChainHistoryClient.ParseEvmEvents(
            Answer(Event("0xff", "-1000000000000000000"), Event("0xff", "+400000000000000000")), Module, "ETH", Explorer);

        var row = Assert.Single(rows);
        Assert.Equal("Sent", row.Kind);
        Assert.Equal("0.6", row.Amount);
    }

    [Fact]
    public void An_amount_larger_than_a_64_bit_number_is_read_whole()
    {
        // 25 coins in wei is past what a long holds.
        var rows = OnChainHistoryClient.ParseEvmEvents(Answer(Event("0x11", "+25000000000000000000")), Module, "BNB", "https://bscscan.com/tx/");

        Assert.Equal("25", Assert.Single(rows).Amount);
    }

    [Fact]
    public void The_order_the_service_answers_in_is_kept()
    {
        var rows = OnChainHistoryClient.ParseEvmEvents(
            Answer(Event("0x03", "+3", time: "2026-03-01T00:00:00.000000Z"),
                   Event("0x02", "+2", time: "2026-02-01T00:00:00.000000Z"),
                   Event("0x01", "+1", time: "2026-01-01T00:00:00.000000Z")),
            Module, "ETH", Explorer);

        Assert.Equal(new[] { "0x03", "0x02", "0x01" }, rows.Select(r => r.Hash));
    }

    [Theory]
    [InlineData("""{"data":"This is not a valid blockchain","context":{"code":400}}""")]
    [InlineData("""{"data":{"events":{"other-main":[]}}}""")]
    [InlineData("""{"data":{"events":{"base-main":null}}}""")]
    [InlineData("[]")]
    public void An_answer_that_is_not_a_list_of_events_is_no_rows(string json)
    {
        Assert.Empty(OnChainHistoryClient.ParseEvmEvents(json, Module, "ETH", Explorer));
    }

    [Fact]
    public void A_refusal_in_an_explorers_envelope_is_not_taken_for_an_empty_history()
    {
        Assert.True(OnChainHistoryClient.IsTxlist("""{"status":"1","message":"OK","result":[]}"""));
        Assert.True(OnChainHistoryClient.IsTxlist("""{"status":"0","message":"No transactions found","result":[]}"""));
        Assert.False(OnChainHistoryClient.IsTxlist("""{"status":"0","message":"NOTOK","result":"Max rate limit reached"}"""));
        Assert.False(OnChainHistoryClient.IsTxlist("<html>Just a moment...</html>"));
    }

    [Fact]
    public void Every_network_read_at_the_ethereum_address_is_known_to_have_a_source_or_not()
    {
        foreach (var network in new[] { "Base", "Arbitrum", "Linea", "BSC", "Polygon", "Optimism", "zkSync Era", "Avalanche" })
            Assert.True(OnChainHistoryClient.HasEvmSideHistory(network), network);

        // Nothing answers for these two without a key: the Activity screen names them as gaps.
        Assert.False(OnChainHistoryClient.HasEvmSideHistory("Fantom"));
        Assert.False(OnChainHistoryClient.HasEvmSideHistory("Cronos"));
    }

    [Fact]
    public void Each_networks_rows_link_where_a_send_made_here_links()
    {
        // The Send screen stores "<explorer>/tx/<hash>" from the sender's own table; a history row with
        // another link would show the same payment twice.
        foreach (var (network, _, _, explorer) in OnChainHistoryClient.EvmSideEvents)
        {
            var chain = EthTransactionSender.Chains.Values.Single(c =>
                c.Name.StartsWith(network, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("https://" + chain.ExplorerTx, explorer);
        }
    }
}

/// <summary>
/// 3xpl refuses requests that arrive in a burst; the wallet asks it one at a time, apart, and once
/// more when it is refused all the same.
/// </summary>
public sealed class ThreeXplPacingTests
{
    private static readonly TimeSpan Apart = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan Refused = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task Requests_made_together_reach_it_one_at_a_time_and_apart()
    {
        var starts = new List<DateTimeOffset>();
        var running = 0;
        var overlapped = false;
        async Task<(int, string?)> Ask(string url, CancellationToken ct)
        {
            if (Interlocked.Increment(ref running) > 1) overlapped = true;
            lock (starts) starts.Add(DateTimeOffset.UtcNow);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref running);
            return (200, url);
        }

        var answers = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => ThreeXpl.GetAsync(Ask, "u" + i, Apart, Refused, CancellationToken.None)));

        Assert.Equal(4, answers.Count(a => a is not null));
        Assert.False(overlapped);
        var ordered = starts.OrderBy(t => t).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i] - ordered[i - 1] >= Apart - TimeSpan.FromMilliseconds(25), $"{(ordered[i] - ordered[i - 1]).TotalMilliseconds} ms apart");
    }

    [Fact]
    public async Task A_refused_request_is_asked_once_more_and_no_more_than_that()
    {
        var asked = 0;
        var answer = await ThreeXpl.GetAsync(
            (_, _) => Task.FromResult<(int, string?)>(++asked == 1 ? (429, null) : (200, "ok")), "u", Apart, Refused, CancellationToken.None);
        Assert.Equal("ok", answer);
        Assert.Equal(2, asked);

        asked = 0;
        answer = await ThreeXpl.GetAsync(
            (_, _) => { asked++; return Task.FromResult<(int, string?)>((429, null)); }, "u", Apart, Refused, CancellationToken.None);
        Assert.Null(answer);
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task Another_kind_of_refusal_is_not_asked_again()
    {
        var asked = 0;
        var answer = await ThreeXpl.GetAsync(
            (_, _) => { asked++; return Task.FromResult<(int, string?)>((400, null)); }, "u", Apart, Refused, CancellationToken.None);

        Assert.Null(answer);
        Assert.Equal(1, asked);
    }
}
