using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// PHB-2026-11: the decoy that the duress password opens was shown the real wallet's history and sends.
/// Both are the same wallet entry, and the records on the device were kept per entry — so the moment
/// the decoy was unlocked in front of whoever demanded it, the Activity screen listed the transfers the
/// duress password exists to hide. Records are now sealed per secret (<see cref="WalletDataKey"/>).
///
/// Tested through the view model, the way it happens: the real wallet has history and a send on this
/// device; the decoy is unlocked; nothing of them is listed. Then the owner unlocks and everything is
/// there — so the first check cannot pass merely because nothing was shown at all.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class DuressRecordsTests : IDisposable
{
    private const string Password = "umbrella-duress-records-2026";
    private const string DuressPassword = "umbrella-duress-decoy-pw-2026";
    private const string DecoyPhrase = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-duress-records-{Guid.NewGuid():N}");
    private string? _walletId;

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        if (_walletId is { } id)
        {
            new HistoryStore().Delete(id);
            foreach (var folder in new[] { "activity", "balances", "swaps", "watched", "found" })
            foreach (var file in WalletDataKey.FilesOf(Path.Combine(AppPaths.DataRoot, folder), id))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }

        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static IEnumerable<ActivityRowViewModel> Rows(MainViewModel vm) =>
        vm.Activity.Concat(vm.FilteredActivity).Concat(vm.Transactions);

    private static bool IsReal(ActivityRowViewModel row) =>
        (row.Explorer ?? string.Empty).Contains("realtx", StringComparison.Ordinal);

    /// <summary>The history read starts after the unlock returns; wait for it to start and finish.</summary>
    private static async Task HistoryReadAsync(MainViewModel vm)
    {
        for (var i = 0; i < 100 && !vm.HistoryLoading && !vm.HistorySynced; i++) await Task.Delay(100);
        for (var i = 0; i < 600 && vm.HistoryLoading; i++) await Task.Delay(100);
    }

    [Fact]
    public async Task The_decoy_lists_nothing_of_the_real_wallet_and_the_owner_still_sees_it_all()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vaultPath = Path.Combine(_dir, "vault.json");
        var vm = new MainViewModel(new EncryptedFileSeedVault(vaultPath));
        vm.Password = Password;
        vm.ConfirmPassword = Password;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        _walletId = vm.Wallets.Single(w => w.IsActive).Id;

        var realPhrase = await new EncryptedFileSeedVault(vaultPath).UnlockAsync(Password);
        await new EncryptedFileSeedVault(vaultPath).AddDuressWalletAsync(Password, DecoyPhrase, DuressPassword);

        // What the real wallet keeps on this device: a transfer read from the chain, and a send.
        var realKey = WalletDataKey.For(_walletId, realPhrase);
        new HistoryStore().Save(realKey,
            [new ChainTx("Received", "BTC", "1.25", "bc1qpayer", 1_700_000_000_000, "https://mempool.space/tx/realtx-history", "realtx-history")]);
        new ActivityStore().Save(realKey,
            [new ActivityStore.Entry("Sent", "BTC", "0.5", "bc1qpayee", "now", "https://mempool.space/tx/realtx-send", UnixMs: 1_700_000_100_000, WalletId: _walletId)]);

        // Under duress.
        vm.LockVault();
        vm.Password = DuressPassword;
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(vm.IsUnlocked);
        await HistoryReadAsync(vm);

        Assert.DoesNotContain(Rows(vm), IsReal);

        // The owner.
        vm.LockVault();
        vm.Password = Password;
        await vm.UnlockCommand.ExecuteAsync(null);
        await HistoryReadAsync(vm);

        Assert.Contains(Rows(vm), r => (r.Explorer ?? "").Contains("realtx-send", StringComparison.Ordinal));
        Assert.Contains(Rows(vm), r => (r.Explorer ?? "").Contains("realtx-history", StringComparison.Ordinal));
    }
}
