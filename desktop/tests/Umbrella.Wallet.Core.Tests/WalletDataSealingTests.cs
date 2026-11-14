using System.Text.Json;
using Umbrella.Wallet.App;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Each wallet's own records on the device — history, activity, swaps, balances, watched addresses,
/// what a scan found — are sealed with a key from that wallet's secret (PSB exception E-1, closed).
///
/// They were plain text, keyed by the wallet entry alone, and one entry can open to several wallets: the
/// duress password opens a decoy in the vault's other slot, a passphrase a hidden wallet behind the same
/// phrase. All of them read the same files, so the decoy listed the real wallet's history and sends
/// (PHB-2026-11). Keyed by the secret, each has its own.
/// </summary>
public sealed class WalletDataSealingTests : IDisposable
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string DecoyPhrase = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-sealing-{Guid.NewGuid():N}");

    public WalletDataSealingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- the key -------------------------------------------------------------------------------------

    [Fact]
    public void The_tag_follows_the_secret_and_the_passphrase()
    {
        var real = WalletDataKey.For("main", Phrase);

        Assert.Equal(real.Tag, WalletDataKey.For("main", Phrase).Tag);           // same wallet, same files
        Assert.NotEqual(real.Tag, WalletDataKey.For("main", DecoyPhrase).Tag);   // the decoy: its own
        Assert.NotEqual(real.Tag, WalletDataKey.For("main", Phrase, "x").Tag);   // a hidden wallet: its own
        Assert.Matches("^[0-9a-f]{16}$", real.Tag);
        Assert.Equal($"main-{real.Tag}.bin", real.FileName);
    }

    [Fact]
    public void The_key_never_prints_its_secret()
    {
        var key = WalletDataKey.For("main", Phrase, "hidden passphrase");

        Assert.DoesNotContain("abandon", key.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", key.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_sealed_by_one_key_does_not_open_with_another()
    {
        var path = Path.Combine(_dir, "r.bin");
        WalletDataKey.For("main", Phrase).Write(path, new List<string> { "secret" }, "test-v1");

        Assert.Null(WalletDataKey.For("main", DecoyPhrase).Read<List<string>>(path, "test-v1"));
        Assert.Null(WalletDataKey.For("main", Phrase).Read<List<string>>(path, "another-store-v1"));
        Assert.Equal("secret", Assert.Single(WalletDataKey.For("main", Phrase).Read<List<string>>(path, "test-v1")!));
    }

    // ---- the activity log ----------------------------------------------------------------------------

    [Fact]
    public void The_shared_activity_file_is_taken_apart_wallet_by_wallet_and_then_removed()
    {
        var legacy = Path.Combine(_dir, "activity.json");
        var folder = Path.Combine(_dir, "activity");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new List<ActivityStore.Entry>
        {
            new("Sent", "BTC", "0.1", "bc1qfriend", "now", "https://x/tx/1", WalletId: "w1"),
            new("Sent", "ETH", "2", "0xshop", "now", "https://x/tx/2", WalletId: "w2"),
            new("Security", "Vault", "unlocked", "this device", "now", null),            // saved before rows had a wallet
            new("Sent", "LTC", "5", "ltc1qgone", "now", "https://x/tx/3", WalletId: "removed"),
        }));
        var onDevice = new[] { "w1", "w2" };

        var w1 = new ActivityStore(folder, legacy).Load(WalletDataKey.For("w1", Phrase), onDevice);
        Assert.Equal(new[] { "BTC", "Vault" }, w1.Select(e => e.Asset));
        Assert.True(File.Exists(legacy));   // w2 has not been opened yet

        var w2 = new ActivityStore(folder, legacy).Load(WalletDataKey.For("w2", DecoyPhrase), onDevice);
        Assert.Equal("ETH", Assert.Single(w2).Asset);
        Assert.False(File.Exists(legacy));  // nothing left for any wallet still listed

        // Sealed, and read back from the sealed files from now on.
        Assert.Equal(2, new ActivityStore(folder, legacy).Load(WalletDataKey.For("w1", Phrase)).Count);
        Assert.DoesNotContain("bc1qfriend", string.Concat(Directory.GetFiles(folder).Select(File.ReadAllText)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_decoy_is_not_shown_the_real_wallets_sends()
    {
        var store = new ActivityStore(Path.Combine(_dir, "activity"), Path.Combine(_dir, "activity.json"));
        store.Save(WalletDataKey.For("main", Phrase),
            [new ActivityStore.Entry("Sent", "BTC", "1.5", "bc1qcold", "now", "https://x/tx/9", WalletId: "main")]);

        Assert.Empty(store.Load(WalletDataKey.For("main", DecoyPhrase)));
    }

    // ---- swaps ---------------------------------------------------------------------------------------

    [Fact]
    public void The_shared_swap_file_is_taken_apart_the_same_way()
    {
        var legacy = Path.Combine(_dir, "swaps.json");
        var folder = Path.Combine(_dir, "swaps");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        File.WriteAllText(legacy, JsonSerializer.Serialize(new List<SwapStore.Entry>
        {
            new("w1", "NearIntents", "SOL", "ETH", 1m, 0.04m, "dep-1", null, null, "tx1", now),
            new("w2", "Exolix", "BTC", "XMR", 0.01m, 2m, "dep-2", null, "o", "tx2", now),
        }));

        var w1 = new SwapStore(folder, legacy).Load(WalletDataKey.For("w1", Phrase), ["w1", "w2"]);
        Assert.Equal("dep-1", Assert.Single(w1).DepositAddress);
        var w2 = new SwapStore(folder, legacy).Load(WalletDataKey.For("w2", Phrase), ["w1", "w2"]);
        Assert.Equal("dep-2", Assert.Single(w2).DepositAddress);
        Assert.False(File.Exists(legacy));
    }

    // ---- watched addresses ---------------------------------------------------------------------------

    [Fact]
    public async Task Watched_addresses_go_to_the_first_wallet_opened_and_belong_to_it()
    {
        var legacy = Path.Combine(_dir, "watch-addresses.json");
        var folder = Path.Combine(_dir, "watched");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new List<WatchAddress> { new("BTC", "bc1qcoldstorage", "cold") }));

        var real = WalletDataKey.For("main", Phrase);
        var store = new WatchAddressStore(folder, legacy);
        Assert.Equal("bc1qcoldstorage", Assert.Single(await store.LoadAsync(real)).Address);
        Assert.False(File.Exists(legacy));
        Assert.DoesNotContain("bc1qcoldstorage", File.ReadAllText(Assert.Single(Directory.GetFiles(folder))), StringComparison.Ordinal);

        // The decoy has a list of its own, and saving it leaves the real one as it was.
        var decoy = WalletDataKey.For("main", DecoyPhrase);
        Assert.Empty(await store.LoadAsync(decoy));
        await store.SaveAsync(decoy, [new WatchAddress("ETH", "0xdecoy", "")]);
        Assert.Equal("bc1qcoldstorage", Assert.Single(await store.LoadAsync(real)).Address);
    }

    // ---- delete everything ---------------------------------------------------------------------------

    [Fact]
    public void Delete_everything_removes_the_sealed_records_and_any_plain_file_left_from_before()
    {
        var key = WalletDataKey.For("main", Phrase);
        foreach (var store in new[] { "history", "activity", "swaps", "balances", "watched", "found" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, store));
            key.Write(Path.Combine(_dir, store, key.FileName), new List<string> { "x" }, store);
        }
        foreach (var legacy in new[] { "activity.json", "swaps.json", "balances.json", "watch-addresses.json" })
            File.WriteAllText(Path.Combine(_dir, legacy), "[]");

        var result = DataWiper.WipeAll(_dir);

        Assert.Empty(result.Failed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }
}
