using System.Text;
using System.Text.Json;
using Umbrella.Wallet.App;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The display cache that makes the portfolio total appear the instant a wallet unlocks, instead of
/// flashing $0 while the network catches up.
///
/// The interesting case is Ethereum's L2 rollups. ETH on mainnet, Arbitrum, Base, Optimism and Linea
/// are five different balances that all report the symbol "ETH" at the SAME 0x address — so anything
/// keyed on symbol + address alone sees five copies of one holding. Restoring the cache used to build
/// a dictionary from exactly that key, which throws on the second row, and it throws on the unlock
/// path, where a display cache has no business failing at all.
///
/// The cache is sealed per wallet and secret (<see cref="WalletDataKey"/>); a cache an earlier version
/// wrote in plain text is taken over by the wallet opened first.
/// </summary>
public sealed class BalanceStoreTests : IDisposable
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string DecoyPhrase = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-balance-{Guid.NewGuid():N}");
    private string Folder => Path.Combine(_dir, "balances");
    private string Legacy => Path.Combine(_dir, "balances.json");

    private BalanceStore Store() => new(Folder, Legacy);
    private static WalletDataKey Key(string wallet = "main", string phrase = Phrase) => WalletDataKey.For(wallet, phrase);

    public BalanceStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Eth_on_several_rollups_is_several_holdings_not_one()
    {
        // The same coin at the same address on four networks. If the network were not part of the
        // identity these would be indistinguishable, and four real balances would read as one.
        var entries = new[]
        {
            new BalanceStore.Entry("ETH", "0xabc", 1.0, 3000, 0, "Ethereum"),
            new BalanceStore.Entry("ETH", "0xabc", 2.0, 3000, 0, "Arbitrum"),
            new BalanceStore.Entry("ETH", "0xabc", 3.0, 3000, 0, "Base"),
            new BalanceStore.Entry("ETH", "0xabc", 4.0, 3000, 0, "Linea"),
        };

        Store().Save(Key(), entries);
        var loaded = Store().Load(Key());

        Assert.Equal(4, loaded.Count);
        Assert.Equal(
            new[] { "Arbitrum", "Base", "Ethereum", "Linea" },
            loaded.Select(e => e.Network).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // And the keys they are matched back by are genuinely distinct.
        var keys = loaded.Select(e => e.Symbol + "|" + e.Address + "|" + e.Network).ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);

        // The old key, for contrast: symbol + address alone collapses all four onto one string, which
        // is why building a dictionary from it threw on the second row — on the unlock path, where a
        // display cache failing takes the whole balance restore with it.
        var oldKeys = loaded.Select(e => e.Symbol + "|" + e.Address).ToList();
        Assert.Single(oldKeys.Distinct());
        Assert.Throws<ArgumentException>(() => oldKeys.ToDictionary(k => k, k => k));
    }

    [Fact]
    public void Nothing_readable_is_left_on_disk()
    {
        Store().Save(Key(), [new BalanceStore.Entry("BTC", "bc1qsecretaddress", 0.5, 60000, 0, "Bitcoin")]);

        var file = Assert.Single(Directory.GetFiles(Folder));
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
        Assert.DoesNotContain("bc1qsecretaddress", text, StringComparison.Ordinal);
        Assert.DoesNotContain("60000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cache_written_in_plain_text_before_is_taken_over_and_removed()
    {
        // Old files are real: somebody updates the wallet and their balances.json predates sealing, and
        // even the network field. It degrades to an empty network, never to an exception on unlock.
        File.WriteAllText(Legacy,
            """
            {"Wallets":{"main":[{"Symbol":"ETH","Address":"0xabc","Amount":1.5,"Price":3000,"Change":2}],
                        "other":[{"Symbol":"BTC","Address":"bc1q","Amount":1,"Price":60000,"Change":0}]}}
            """);

        var entry = Assert.Single(Store().Load(Key()));
        Assert.Equal("ETH", entry.Symbol);
        Assert.Equal(1.5, entry.Amount);
        Assert.Equal(string.Empty, entry.Network);

        // Sealed now, and the plain file — another wallet's figures included — is gone.
        Assert.False(File.Exists(Legacy));
        Assert.Equal("ETH", Assert.Single(Store().Load(Key())).Symbol);
    }

    [Fact]
    public void A_plain_file_holding_two_rows_with_the_same_key_is_survivable()
    {
        // Written by an older build, hand-edited, or half-flushed. Loading must hand back what is
        // there and let the caller decide — the caller groups rather than building a dictionary that
        // throws on the duplicate.
        File.WriteAllText(Legacy,
            """
            {"Wallets":{"main":[
              {"Symbol":"ETH","Address":"0xabc","Amount":1,"Price":3000,"Change":0,"Network":""},
              {"Symbol":"ETH","Address":"0xabc","Amount":2,"Price":3100,"Change":0,"Network":""}
            ]}}
            """);

        var loaded = Store().Load(Key());
        Assert.Equal(2, loaded.Count);

        var byKey = loaded
            .GroupBy(e => e.Symbol + "|" + e.Address + "|" + e.Network, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2.0, Assert.Single(byKey).Value.Amount);
    }

    [Fact]
    public void A_corrupt_file_is_no_cache_rather_than_a_crash()
    {
        File.WriteAllText(Legacy, "{ this is not json");
        Assert.Empty(Store().Load(Key()));

        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, Key().FileName), "not sealed at all");
        Assert.Empty(Store().Load(Key()));
    }

    [Fact]
    public void Each_wallet_keeps_its_own_figures()
    {
        Store().Save(Key("one"), [new BalanceStore.Entry("BTC", "bc1q", 0.5, 60000, 0, "Bitcoin")]);
        Store().Save(Key("two", DecoyPhrase), [new BalanceStore.Entry("LTC", "ltc1q", 9, 80, 0, "Litecoin")]);

        Assert.Equal("BTC", Assert.Single(Store().Load(Key("one"))).Symbol);
        Assert.Equal("LTC", Assert.Single(Store().Load(Key("two", DecoyPhrase))).Symbol);
    }

    [Fact]
    public void The_decoy_behind_the_same_wallet_never_sees_or_touches_the_real_figures()
    {
        // The duress password opens the vault's other slot: same wallet entry, another phrase.
        Store().Save(Key(), [new BalanceStore.Entry("BTC", "bc1qreal", 3, 60000, 0, "Bitcoin")]);
        var realFile = Path.Combine(Folder, Key().FileName);
        var before = File.ReadAllBytes(realFile);

        var decoy = Key("main", DecoyPhrase);
        Assert.Empty(Store().Load(decoy));
        Store().Save(decoy, [new BalanceStore.Entry("BTC", "bc1qdecoy", 0.01, 60000, 0, "Bitcoin")]);

        Assert.Equal(before, File.ReadAllBytes(realFile));
        Assert.Equal("bc1qreal", Assert.Single(Store().Load(Key())).Address);
        Assert.Equal("bc1qdecoy", Assert.Single(Store().Load(decoy)).Address);
    }

    [Fact]
    public void The_network_survives_a_round_trip_through_json()
    {
        // Guards the serializer specifically: a defaulted record parameter is easy to add to the type
        // and still lose on the way to disk.
        var entry = new BalanceStore.Entry("ETH", "0xabc", 1, 3000, 0, "Optimism");
        var json = JsonSerializer.Serialize(entry);

        Assert.Contains("Optimism", json, StringComparison.Ordinal);
        Assert.Equal("Optimism", JsonSerializer.Deserialize<BalanceStore.Entry>(json)!.Network);
    }
}
