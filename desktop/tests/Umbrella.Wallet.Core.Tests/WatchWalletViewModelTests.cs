using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Core.Watch;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A seedless watch-only wallet in the app (roadmap H.5): it opens with its password, shows what it
/// watches, survives lock, removal, restore and backup with its description — and every path that
/// would sign refuses it by its type.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class WatchWalletViewModelTests : IDisposable
{
    private const string Password = "umbrella-watch-only-2026";
    private const string Zpub = "zpub6rFR7y4Q2AijBEqTUquhVz398htDFrtymD9xYYfG1m4wAcvPhXNfE3EfH1r1ADqtfSdVCToUG868RvUUkgDKf31mGDtKsAYz2oz2AGutZYs";
    private const string Eth = "0x000000000000000000000000000000000000dEaD";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-watchvm-{Guid.NewGuid():N}");
    private string VaultPath => Path.Combine(_dir, "vault.json");

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private MainViewModel NewViewModel()
    {
        TestDataIsolation.RestoreBaselineSettings();
        return new MainViewModel(new EncryptedFileSeedVault(VaultPath));
    }

    private static async Task ImportWatchAsync(MainViewModel vm, string spec)
    {
        vm.GoToWatchCommand.Execute(null);
        vm.WatchSpecInput = spec;
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.ImportWatchWalletCommand.ExecuteAsync(null);
        await BackgroundRefresh.SettleAsync(vm);
    }

    [Fact]
    public async Task It_opens_with_what_it_watches_and_nothing_that_signs()
    {
        var vm = NewViewModel();
        await ImportWatchAsync(vm, Zpub + "\n" + Eth);

        Assert.True(vm.IsUnlocked);
        Assert.True(vm.IsWatchWallet);
        var btc = vm.Accounts.Single(a => a.Symbol == "BTC");
        Assert.Equal(("Watch", "bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu"), (btc.SupportStatus, btc.Address));
        Assert.Contains(vm.Accounts, a => a.Symbol == "ETH" && a.Address == Eth && a.SupportStatus == "Watch");

        // On disk it is a vault like any other, and its description is sealed beside it.
        Assert.Equal(DeniableVaultFormat.FileSize, new FileInfo(VaultPath).Length);
        var sealedBytes = File.ReadAllText(WatchWalletFile.PathFor(VaultPath));
        Assert.DoesNotContain("zpub", sealedBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("dEaD", sealedBytes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lock_and_unlock_bring_it_back_as_it_was()
    {
        var vm = NewViewModel();
        await ImportWatchAsync(vm, Zpub);

        vm.LockVault();
        Assert.False(vm.IsWatchWallet);
        vm.Password = Password;
        await vm.UnlockCommand.ExecuteAsync(null);
        await BackgroundRefresh.SettleAsync(vm);

        Assert.True(vm.IsWatchWallet);
        Assert.Contains(vm.Accounts, a => a.Symbol == "BTC" && a.SupportStatus == "Watch");
    }

    public static TheoryData<string> SigningCommands() => new()
    {
        nameof(MainViewModel.PrepareSendCommand), nameof(MainViewModel.ConfirmSendCommand),
        nameof(MainViewModel.LoadCoinControlCommand), nameof(MainViewModel.GetSwapQuoteCommand),
        nameof(MainViewModel.ConfirmSwapCommand), nameof(MainViewModel.PrepareStakeActionCommand),
        nameof(MainViewModel.ConfirmStakeActionCommand), nameof(MainViewModel.SignMessageCommand),
        nameof(MainViewModel.ExportSendPsbtCommand), nameof(MainViewModel.ReviewPsbtCommand),
        nameof(MainViewModel.SignPsbtCommand), nameof(MainViewModel.RevealPhraseCommand),
        nameof(MainViewModel.RevealMoneroKeysCommand), nameof(MainViewModel.RevealXpubsCommand),
    };

    [Theory]
    [MemberData(nameof(SigningCommands))]
    public async Task Every_path_that_signs_refuses_it(string command)
    {
        var vm = NewViewModel();
        await ImportWatchAsync(vm, Zpub);
        vm.FormError = string.Empty;

        var cmd = typeof(MainViewModel).GetProperty(command)!.GetValue(vm);
        switch (cmd)
        {
            case IAsyncRelayCommand a: await a.ExecuteAsync(null); break;
            case IRelayCommand r: r.Execute(null); break;
            default: Assert.Fail($"{command} is not a command"); break;
        }

        Assert.Equal(Loc.Instance["watch.cannotSign"], vm.FormError);
    }

    [Fact]
    public async Task A_payment_is_offered_as_a_psbt_and_only_from_coins_it_has_read()
    {
        var vm = NewViewModel();
        await ImportWatchAsync(vm, Zpub + "\nDOGE:xpub6BosfCnifzxcFwrSzQiqu2DBVTshkCXacvNsWGYJVVhhawA7d4R5WSWGFNbi8Aw6ZRc1brxMyWMzG3DSSSSoekkudhUd9yLb6qx39T9nMdj");

        // Bitcoin and Litecoin have PSBT; the Dogecoin account is watched but not offered.
        Assert.True(vm.CanMakeWatchPsbt);
        Assert.Single(vm.WatchPsbtAccounts);

        // Offline here, so the account's coins were never read: no PSBT is made from a guess.
        vm.WatchPsbtTo = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";
        vm.WatchPsbtAmount = "0.001";
        await vm.MakeWatchPsbtCommand.ExecuteAsync(null);

        Assert.Equal(Loc.Instance["watch.psbt.notRead"], vm.WatchPsbtError);
        Assert.False(vm.HasWatchPsbt);
    }

    [Fact]
    public async Task It_has_no_phrase_to_check()
    {
        var vm = NewViewModel();
        await ImportWatchAsync(vm, Zpub);
        vm.SettingsPassword = Password;
        vm.PhraseCheckInput = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        await vm.CheckWrittenPhraseCommand.ExecuteAsync(null);

        Assert.Equal(Loc.Instance["watch.noPhrase"], vm.PhraseCheckResult);
    }

    [Fact]
    public async Task A_line_it_cannot_use_is_named_and_nothing_is_created()
    {
        var vm = NewViewModel();
        const string bareXpub = "xpub6BosfCnifzxcFwrSzQiqu2DBVTshkCXacvNsWGYJVVhhawA7d4R5WSWGFNbi8Aw6ZRc1brxMyWMzG3DSSSSoekkudhUd9yLb6qx39T9nMdj";

        await ImportWatchAsync(vm, Eth + "\n" + bareXpub);

        Assert.False(vm.HasVault);
        Assert.False(File.Exists(VaultPath));
        Assert.Equal(string.Format(Loc.Instance["watch.lineIssue"], 2, Loc.Instance["watch.err.bareXpub"]), vm.FormError);
        Assert.True(vm.HasWatchSpecIssues);
    }

    [Fact]
    public async Task Removing_and_restoring_it_keeps_its_description_with_its_vault()
    {
        var vm = NewViewModel();
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        var mainId = vm.Wallets.Single(w => w.IsActive).Id;

        vm.NewWalletLabel = "Ledger";
        vm.BeginAddWalletCommand.Execute(null);
        vm.GoToWatchCommand.Execute(null);
        vm.WatchSpecInput = Zpub;
        await vm.ImportWatchWalletCommand.ExecuteAsync(null);
        await BackgroundRefresh.SettleAsync(vm);
        Assert.True(vm.IsWatchWallet);
        var watchId = vm.Wallets.Single(w => w.IsActive).Id;

        await vm.SwitchWalletCommand.ExecuteAsync(mainId);
        Assert.False(vm.IsWatchWallet);
        vm.RemoveWalletCommand.Execute(watchId);
        vm.RemoveWalletCommand.Execute(watchId);
        var removedDir = Path.Combine(_dir, "wallets", "removed");
        Assert.Single(Directory.GetFiles(removedDir, "*.vault.json.watch"));

        vm.RestoreRemovedWalletCommand.Execute(Assert.Single(vm.RemovedWallets).Key);
        await vm.SwitchWalletCommand.ExecuteAsync(vm.Wallets.Single(w => w.Label == "Ledger").Id);
        await BackgroundRefresh.SettleAsync(vm);

        Assert.True(vm.IsWatchWallet);
        Assert.Contains(vm.Accounts, a => a.Address == "bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu");
    }

    [Fact]
    public async Task A_backup_of_it_verifies_only_with_its_description()
    {
        Directory.CreateDirectory(_dir);
        var secret = WatchWalletFile.NewSecret();
        await new EncryptedFileSeedVault(VaultPath).CreateAsync(secret, Password);
        WatchWalletFile.Save(VaultPath, secret, WatchSpecParser.Parse(Zpub, _ => null).Spec);

        string Bundle(bool withSpec)
        {
            var bundle = new Dictionary<string, string?>
            {
                ["magic"] = "umbrella-backup-v1",
                ["vaultBase64"] = Convert.ToBase64String(File.ReadAllBytes(VaultPath)),
            };
            if (withSpec) bundle["watchSpecBase64"] = Convert.ToBase64String(File.ReadAllBytes(WatchWalletFile.PathFor(VaultPath)));
            var path = Path.Combine(_dir, $"backup-{withSpec}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(bundle));
            return path;
        }

        var good = await VaultBackup.VerifyAsync(Bundle(withSpec: true), Password);
        var missing = await VaultBackup.VerifyAsync(Bundle(withSpec: false), Password);

        Assert.True(good.Ok, good.Message);
        Assert.False(missing.Ok);
        Assert.Equal(VaultBackupReason.DamagedPhrase, missing.Reason);
    }
}
