using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The user's own explorer key (roadmap N.15): sealed per wallet, never readable on disk, gone with a
/// wipe, refused when Etherscan does not accept it — and used only where nothing answers without one.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class ExplorerKeyTests : IDisposable
{
    private const string Key = "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456";
    private const string Password = "umbrella-explorer-key-2026";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-explkey-{Guid.NewGuid():N}");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void It_is_sealed_to_its_wallet_and_readable_only_with_its_secret()
    {
        var store = new ExplorerKeyStore(_dir);
        store.Save("main", "secret-one", Key);

        Assert.DoesNotContain(Key, File.ReadAllText(store.PathFor("main")), StringComparison.Ordinal);
        Assert.Equal(Key, store.Load("main", "secret-one"));
        Assert.Null(store.Load("main", "secret-two"));   // another wallet's secret opens nothing
        Assert.Null(store.Load("savings", "secret-one")); // another wallet has its own file

        store.Remove("main");
        Assert.Null(store.Load("main", "secret-one"));
    }

    [Fact]
    public void A_wipe_removes_every_wallets_key()
    {
        var store = new ExplorerKeyStore(_dir);
        store.Save("main", "s1", Key);
        store.Save("abc123", "s2", Key);

        DataWiper.WipeAll(_dir);

        Assert.Empty(Directory.GetFiles(_dir, "explorer-key-*.bin"));
    }

    [Fact]
    public async Task A_key_that_is_not_one_or_that_etherscan_refuses_is_not_saved()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_dir, "vault.json")));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);

        vm.ExplorerKeyInput = "short";
        await vm.SaveExplorerKeyCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["explorerKey.bad"], vm.ExplorerKeyError);

        // Offline here: Etherscan cannot confirm the key, so it is not kept.
        vm.ExplorerKeyInput = Key;
        await vm.SaveExplorerKeyCommand.ExecuteAsync(null);
        Assert.StartsWith(Loc.Instance["explorerKey.refused"].Split("{0}")[0], vm.ExplorerKeyError, StringComparison.Ordinal);
        Assert.False(vm.HasExplorerKey);
        Assert.Empty(Directory.GetFiles(_dir, "explorer-key-*.bin"));
    }

    [Fact]
    public async Task A_saved_key_comes_back_with_its_wallet_and_goes_with_the_lock()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(Path.Combine(_dir, "vault.json")));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        var walletId = vm.Wallets.Single(w => w.IsActive).Id;
        var secret = await new EncryptedFileSeedVault(Path.Combine(_dir, "vault.json")).UnlockAsync(Password);
        new ExplorerKeyStore(_dir).Save(walletId, secret, Key);

        vm.LockVault();
        Assert.False(vm.HasExplorerKey);
        vm.Password = Password;
        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.True(vm.HasExplorerKey);
    }

    [Fact]
    public void Only_the_networks_nothing_serves_without_a_key_are_read_with_it()
    {
        // Every keyed network is one the keyless sources leave out — the key is never a shortcut.
        foreach (var (network, _, _, _) in OnChainHistoryClient.KeyedNetworks)
        {
            if (network == "BSC") Assert.False(OnChainHistoryClient.HasEvmTokenHistory(network));
            else Assert.False(OnChainHistoryClient.HasEvmSideHistory(network));
        }
    }
}

/// <summary>Keyed history against Etherscan itself — only when a key is supplied (<c>ETHERSCAN_API_KEY</c>).</summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class ExplorerKeyLiveTests
{
    [Fact]
    public async Task With_a_real_key_the_keyed_networks_answer()
    {
        var key = Environment.GetEnvironmentVariable("ETHERSCAN_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return;   // nothing to check without one
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);

        var client = new OnChainHistoryClient();
        Assert.Null(await client.CheckEtherscanKeyAsync(key, 1));
        var report = new List<string>();
        foreach (var (network, _, _, _) in OnChainHistoryClient.KeyedNetworks.Where(n => n.Network != "BSC"))
        {
            var rows = await client.GetKeyedNativeAsync(key, network, "0xf977814e90da44bfa03b6295a0616a897441acec");
            report.Add($"{network}: {(rows is null ? "refused" : rows.Count + " rows")}");
        }

        Assert.True(report.Count > 0, string.Join("; ", report));
    }
}
