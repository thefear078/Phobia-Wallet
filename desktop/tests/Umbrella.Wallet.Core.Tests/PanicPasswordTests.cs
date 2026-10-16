using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The panic password (roadmap P1.3): typed on the unlock screen, it erases this device's wallets and
/// the app comes back as freshly installed.
///
/// Written the way it goes wrong. The worst failure is not a panic password that does nothing — it is
/// one that fires when it should not: on the real password, on the decoy handed over under duress, or on
/// a wallet whose password happens to be the same. So most of these prove what never wipes.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PanicPasswordTests : IDisposable
{
    private const string Real = "umbrella-real-vault-2026";
    private const string PanicPw = "umbrella-panic-wipe-2026";
    private const string Decoy = "umbrella-decoy-vault-2026";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-panic-{Guid.NewGuid():N}");

    private string VaultPath => Path.Combine(_directory, "vault.json");
    private string GuardPath => Path.Combine(_directory, PanicGuard.FileName);

    public void Dispose()
    {
        TestDataIsolation.RestoreBaselineSettings();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }

    // ---- the guard file ----------------------------------------------------------------------------------

    [Fact]
    public void The_file_looks_the_same_set_or_not()
    {
        Directory.CreateDirectory(_directory);
        var guard = new PanicGuard(_directory);

        guard.EnsureExists();
        var unset = File.ReadAllBytes(GuardPath);
        guard.Set(PanicPw);
        var set = File.ReadAllBytes(GuardPath);
        guard.Clear();
        var cleared = File.ReadAllBytes(GuardPath);

        Assert.Equal(PanicGuard.FileSize, unset.Length);
        Assert.Equal(PanicGuard.FileSize, set.Length);
        Assert.Equal(PanicGuard.FileSize, cleared.Length);
        Assert.NotEqual(unset, set);
        Assert.NotEqual(set, cleared);
    }

    [Fact]
    public void Only_the_password_set_matches_and_a_cleared_guard_matches_nothing()
    {
        Directory.CreateDirectory(_directory);
        var guard = new PanicGuard(_directory);
        guard.EnsureExists();
        Assert.False(guard.Matches(PanicPw));

        guard.Set(PanicPw);
        Assert.True(guard.Matches(PanicPw));
        Assert.False(guard.Matches(Real));
        Assert.False(guard.Matches(PanicPw + " "));
        Assert.False(guard.Matches(""));
        Assert.False(guard.Matches(null));

        guard.Clear();
        Assert.False(guard.Matches(PanicPw));
    }

    [Fact]
    public void Starting_up_never_overwrites_a_set_guard()
    {
        Directory.CreateDirectory(_directory);
        new PanicGuard(_directory).Set(PanicPw);

        new PanicGuard(_directory).EnsureExists();

        Assert.True(new PanicGuard(_directory).Matches(PanicPw));
    }

    [Fact]
    public void A_missing_or_damaged_file_never_fires()
    {
        Directory.CreateDirectory(_directory);
        var guard = new PanicGuard(_directory);
        Assert.False(guard.Matches(PanicPw));            // no file at all

        File.WriteAllBytes(GuardPath, [1, 2, 3]);
        Assert.False(guard.Matches(PanicPw));            // wrong size
    }

    // ---- in the wallet ------------------------------------------------------------------------------------

    private async Task<MainViewModel> NewWalletAsync()
    {
        TestDataIsolation.RestoreBaselineSettings();
        var vm = new MainViewModel(new EncryptedFileSeedVault(VaultPath));
        vm.Password = Real;
        vm.ConfirmPassword = Real;
        await vm.CreateWalletCommand.ExecuteAsync(null);
        vm.ConfirmPhraseBackupCommand.Execute(null);
        await BackgroundRefresh.SettleAsync(vm);
        Assert.True(vm.IsUnlocked);
        return vm;
    }

    private static async Task SetPanicAsync(MainViewModel vm, string current = Real, string panic = PanicPw)
    {
        vm.PanicVaultPassword = current;
        vm.PanicNewPassword = panic;
        vm.PanicConfirmPassword = panic;
        await vm.SetPanicPasswordCommand.ExecuteAsync(null);
    }

    private static async Task UnlockWithAsync(MainViewModel vm, string password)
    {
        vm.LockVault();
        vm.Password = password;
        await vm.UnlockCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task The_panic_password_erases_the_wallet_and_says_nothing()
    {
        var vm = await NewWalletAsync();
        vm.WalletName = "Savings";
        await SetPanicAsync(vm);
        Assert.Equal(Loc.Instance["panic.set"], vm.PanicStatus);

        await UnlockWithAsync(vm, PanicPw);

        Assert.False(vm.IsUnlocked);
        Assert.False(vm.HasVault);
        Assert.True(vm.IsWelcomeStage);
        Assert.False(File.Exists(VaultPath));
        Assert.Equal(string.Empty, vm.Password);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal(string.Empty, vm.FormError);
        // What described the wallet goes with it, rather than waiting in memory for the next save.
        Assert.Equal(string.Empty, vm.WalletName);
        // A fresh install's guard: present, the right size, matching nothing.
        Assert.Equal(PanicGuard.FileSize, new FileInfo(GuardPath).Length);
        Assert.False(new PanicGuard(_directory).Matches(PanicPw));
    }

    [Fact]
    public async Task The_real_password_still_opens_the_wallet()
    {
        var vm = await NewWalletAsync();
        await SetPanicAsync(vm);

        await UnlockWithAsync(vm, Real);

        Assert.True(vm.IsUnlocked);
        Assert.True(File.Exists(VaultPath));
    }

    [Fact]
    public async Task The_decoy_still_opens_the_decoy()
    {
        var vm = await NewWalletAsync();
        vm.DuressVaultPassword = Real;
        vm.DuressNewPassword = Decoy;
        vm.DuressConfirmPassword = Decoy;
        await vm.SetDuressWalletCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.DuressError);
        await SetPanicAsync(vm);
        Assert.Equal(string.Empty, vm.PanicError);

        await UnlockWithAsync(vm, Decoy);

        Assert.True(vm.IsUnlocked);
        Assert.True(File.Exists(VaultPath));
    }

    [Fact]
    public async Task The_panic_password_works_after_forced_mistakes()
    {
        // Somebody standing over the user can make them get it wrong first; the throttle that follows
        // must not stand between the user and the wipe.
        var vm = await NewWalletAsync();
        await SetPanicAsync(vm);
        for (var i = 0; i < 4; i++) await UnlockWithAsync(vm, "not-the-password-at-all");

        await UnlockWithAsync(vm, PanicPw);

        Assert.False(vm.HasVault);
        Assert.False(File.Exists(VaultPath));
    }

    [Fact]
    public async Task It_cannot_be_the_wallet_password_or_any_password_that_opens_a_wallet_here()
    {
        var vm = await NewWalletAsync();

        await SetPanicAsync(vm, panic: Real);
        Assert.Equal(Loc.Instance["panic.errSame"], vm.PanicError);

        vm.DuressVaultPassword = Real;
        vm.DuressNewPassword = Decoy;
        vm.DuressConfirmPassword = Decoy;
        await vm.SetDuressWalletCommand.ExecuteAsync(null);
        await SetPanicAsync(vm, panic: Decoy);
        Assert.Equal(Loc.Instance["panic.errOpens"], vm.PanicError);

        await SetPanicAsync(vm, current: "not-the-password-at-all");
        Assert.Equal(Loc.Instance["duress.errWrongPw"], vm.PanicError);

        // None of the refusals set anything.
        Assert.False(new PanicGuard(_directory).Matches(Real));
        Assert.False(new PanicGuard(_directory).Matches(Decoy));
        Assert.False(new PanicGuard(_directory).Matches(PanicPw));
    }

    [Fact]
    public async Task A_password_chosen_later_cannot_be_the_panic_password()
    {
        var vm = await NewWalletAsync();
        await SetPanicAsync(vm);

        vm.ChangePwCurrent = Real;
        vm.ChangePwNew = PanicPw;
        vm.ChangePwConfirm = PanicPw;
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["password.errTaken"], vm.FormError);
        Assert.False(string.IsNullOrEmpty(await new EncryptedFileSeedVault(VaultPath).UnlockAsync(Real)));   // unchanged

        vm.DuressVaultPassword = Real;
        vm.DuressNewPassword = PanicPw;
        vm.DuressConfirmPassword = PanicPw;
        await vm.SetDuressWalletCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["password.errTaken"], vm.DuressError);
    }

    [Fact]
    public async Task Removing_it_leaves_a_guard_that_matches_nothing()
    {
        var vm = await NewWalletAsync();
        await SetPanicAsync(vm);

        vm.PanicVaultPassword = Real;
        await vm.RemovePanicPasswordCommand.ExecuteAsync(null);

        Assert.Equal(PanicGuard.FileSize, new FileInfo(GuardPath).Length);
        Assert.False(new PanicGuard(_directory).Matches(PanicPw));
        await UnlockWithAsync(vm, PanicPw);
        Assert.True(File.Exists(VaultPath));   // a wrong password now, nothing more
    }

    [Fact]
    public async Task Locking_forgets_passwords_typed_into_the_security_forms()
    {
        var vm = await NewWalletAsync();
        vm.PanicVaultPassword = Real;
        vm.PanicNewPassword = PanicPw;
        vm.DuressVaultPassword = Real;

        vm.LockVault();

        Assert.Equal(string.Empty, vm.PanicVaultPassword);
        Assert.Equal(string.Empty, vm.PanicNewPassword);
        Assert.Equal(string.Empty, vm.DuressVaultPassword);
    }

    [Fact]
    public async Task Delete_everything_erases_the_folder_the_wallet_lives_in()
    {
        // It used to wipe the app's default data folder whatever the wallet's own was — the same folder
        // in the app, but it meant the check of this command never saw the vault go.
        var vm = await NewWalletAsync();
        vm.WalletName = "Savings";

        vm.DeleteConfirmation = "DELETE";
        vm.DeleteVaultCommand.Execute(null);

        Assert.False(File.Exists(VaultPath));
        Assert.Equal(string.Empty, vm.WalletName);
        Assert.True(File.Exists(GuardPath));
    }
}
