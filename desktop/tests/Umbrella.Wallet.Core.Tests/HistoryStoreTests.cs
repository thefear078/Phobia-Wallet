using System.Text;
using System.Text.Json;
using Umbrella.Wallet.App;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The history a wallet has read is kept and added to. Without that, an explorer that answers nothing
/// today makes last month's transfers disappear, and a source that lists a page at a time can never
/// show what is behind the first page — a wallet with a year of history looked a day old.
///
/// Kept sealed, per wallet and secret: the decoy that the duress password opens used to be shown the
/// real wallet's whole history, because both are the same wallet entry.
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string DecoyPhrase = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"umbrella-history-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static WalletDataKey Key(string wallet, string phrase = Phrase, string passphrase = "") =>
        WalletDataKey.For(wallet, phrase, passphrase);

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
        new HistoryStore(_folder).Save(Key("wallet-1"), [Tx("a", 100)]);
        new HistoryStore(_folder).Save(Key("wallet-2", DecoyPhrase), [Tx("z", 900)]);

        var again = new HistoryStore(_folder);
        Assert.Equal("a", Assert.Single(again.Load(Key("wallet-1"))).Hash);
        Assert.Equal("z", Assert.Single(again.Load(Key("wallet-2", DecoyPhrase))).Hash);
        Assert.Empty(again.Load(Key("wallet-3")));
    }

    [Fact]
    public void The_file_on_disk_says_nothing_about_the_transfers()
    {
        new HistoryStore(_folder).Save(Key("w"), [Tx("deadbeefcafe", 100, amount: "123.456")]);

        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Assert.Single(Directory.GetFiles(_folder))));
        Assert.DoesNotContain("deadbeefcafe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("123.456", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_decoy_and_the_hidden_wallet_never_see_the_real_history()
    {
        var store = new HistoryStore(_folder);
        store.Save(Key("main"), [Tx("real", 100)]);

        // The duress password: same wallet entry, the other slot's phrase.
        Assert.Empty(store.Load(Key("main", DecoyPhrase)));
        // A passphrase: same phrase, another wallet behind it.
        Assert.Empty(store.Load(Key("main", Phrase, "hidden")));

        // Each writes its own; the real history is untouched.
        store.Save(Key("main", DecoyPhrase), [Tx("decoy", 200)]);
        store.Save(Key("main", Phrase, "hidden"), [Tx("hidden", 300)]);
        Assert.Equal("real", Assert.Single(store.Load(Key("main"))).Hash);
        Assert.Equal("decoy", Assert.Single(store.Load(Key("main", DecoyPhrase))).Hash);
        Assert.Equal("hidden", Assert.Single(store.Load(Key("main", Phrase, "hidden"))).Hash);
    }

    [Fact]
    public void A_plain_file_from_before_is_taken_over_sealed_and_removed()
    {
        Directory.CreateDirectory(_folder);
        var legacy = Path.Combine(_folder, "wallet-1.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new List<ChainTx> { Tx("old", 50) }));

        var store = new HistoryStore(_folder);
        Assert.Equal("old", Assert.Single(store.Load(Key("wallet-1"))).Hash);

        Assert.False(File.Exists(legacy));
        Assert.Equal("old", Assert.Single(store.Load(Key("wallet-1"))).Hash);   // now from the sealed file
    }

    [Fact]
    public void Clearing_removes_every_wallets_history()
    {
        var store = new HistoryStore(_folder);
        store.Save(Key("wallet-1"), [Tx("a", 100)]);
        store.Save(Key("wallet-2"), [Tx("z", 900)]);

        store.Clear();

        Assert.Empty(store.Load(Key("wallet-1")));
        Assert.Empty(store.Load(Key("wallet-2")));
    }

    [Fact]
    public void Deleting_a_wallet_takes_every_one_of_its_files_and_no_one_elses()
    {
        var store = new HistoryStore(_folder);
        store.Save(Key("w"), [Tx("a", 100)]);
        store.Save(Key("w", DecoyPhrase), [Tx("b", 100)]);
        store.Save(Key("w-2"), [Tx("c", 100)]);   // a wallet whose id starts like the first one's

        store.Delete("w");

        Assert.Empty(store.Load(Key("w")));
        Assert.Empty(store.Load(Key("w", DecoyPhrase)));
        Assert.Equal("c", Assert.Single(store.Load(Key("w-2"))).Hash);
    }

    [Fact]
    public void A_wallet_id_cannot_become_a_path_out_of_the_folder()
    {
        var store = new HistoryStore(_folder);
        store.Save(Key("../../escape"), [Tx("a", 100)]);

        Assert.Single(Directory.GetFiles(_folder));                      // written inside, under a safe name
        Assert.False(File.Exists(Path.Combine(_folder, "..", "..", "escape.json")));
        Assert.Equal("a", Assert.Single(store.Load(Key("../../escape"))).Hash);
    }

    [Fact]
    public void The_file_stops_growing_at_its_limit_and_keeps_the_newest()
    {
        var store = new HistoryStore(_folder);
        var many = Enumerable.Range(0, HistoryStore.MaxRows + 50).Select(i => Tx($"h{i}", i)).ToList();

        store.Save(Key("w"), HistoryStore.Merge(many, []));

        var kept = store.Load(Key("w"));
        Assert.Equal(HistoryStore.MaxRows, kept.Count);
        Assert.Equal($"h{HistoryStore.MaxRows + 49}", kept[0].Hash);     // the newest is first, and is kept
    }
}
