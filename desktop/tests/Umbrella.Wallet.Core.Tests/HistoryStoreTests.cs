using Umbrella.Wallet.App;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The history a wallet has read is kept and added to. Without that, an explorer that answers nothing
/// today makes last month's transfers disappear, and a source that lists a page at a time can never
/// show what is behind the first page — a wallet with a year of history looked a day old.
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"umbrella-history-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static ChainTx Tx(string hash, long ms, string kind = "Sent", string asset = "SOL", string amount = "1") =>
        new(kind, asset, amount, "other", ms, $"https://solscan.io/tx/{hash}", hash);

    [Fact]
    public void What_was_read_before_stays_when_this_read_brings_nothing()
    {
        var merged = HistoryStore.Merge(fresh: [], kept: [Tx("a", 100), Tx("b", 200)]);

        Assert.Equal(new[] { "b", "a" }, merged.Select(t => t.Hash));   // newest first
    }

    [Fact]
    public void A_new_read_adds_to_the_old_and_lists_each_transaction_once()
    {
        var merged = HistoryStore.Merge(
            fresh: [Tx("c", 300), Tx("b", 200)],
            kept: [Tx("b", 200), Tx("a", 100)]);

        Assert.Equal(new[] { "c", "b", "a" }, merged.Select(t => t.Hash));
    }

    [Fact]
    public void A_row_read_now_replaces_the_kept_one_for_the_same_transaction()
    {
        var merged = HistoryStore.Merge(
            fresh: [Tx("a", 100, amount: "2")],
            kept: [Tx("a", 100, amount: "1")]);

        Assert.Equal("2", Assert.Single(merged).Amount);
    }

    [Fact]
    public void A_swap_keeps_both_of_its_legs()
    {
        // One transaction id: out goes one asset, in comes another. Both are history.
        var merged = HistoryStore.Merge(
            fresh: [Tx("s", 100, "Sent", "ETH"), Tx("s", 100, "Received", "USDT")],
            kept: []);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Each_wallet_has_its_own_history_and_it_survives_a_restart()
    {
        new HistoryStore(_folder).Save("wallet-1", [Tx("a", 100)]);
        new HistoryStore(_folder).Save("wallet-2", [Tx("z", 900)]);

        var again = new HistoryStore(_folder);
        Assert.Equal("a", Assert.Single(again.Load("wallet-1")).Hash);
        Assert.Equal("z", Assert.Single(again.Load("wallet-2")).Hash);
        Assert.Empty(again.Load("wallet-3"));
    }

    [Fact]
    public void Clearing_removes_every_wallets_history()
    {
        var store = new HistoryStore(_folder);
        store.Save("wallet-1", [Tx("a", 100)]);
        store.Save("wallet-2", [Tx("z", 900)]);

        store.Clear();

        Assert.Empty(store.Load("wallet-1"));
        Assert.Empty(store.Load("wallet-2"));
    }

    [Fact]
    public void A_wallet_id_cannot_become_a_path_out_of_the_folder()
    {
        var store = new HistoryStore(_folder);
        store.Save("../../escape", [Tx("a", 100)]);

        Assert.Single(Directory.GetFiles(_folder));                      // written inside, under a safe name
        Assert.False(File.Exists(Path.Combine(_folder, "..", "..", "escape.json")));
        Assert.Equal("a", Assert.Single(store.Load("../../escape")).Hash);
    }

    [Fact]
    public void The_file_stops_growing_at_its_limit_and_keeps_the_newest()
    {
        var store = new HistoryStore(_folder);
        var many = Enumerable.Range(0, HistoryStore.MaxRows + 50).Select(i => Tx($"h{i}", i)).ToList();

        store.Save("w", HistoryStore.Merge(many, []));

        var kept = store.Load("w");
        Assert.Equal(HistoryStore.MaxRows, kept.Count);
        Assert.Equal($"h{HistoryStore.MaxRows + 49}", kept[0].Hash);     // the newest is first, and is kept
    }
}
