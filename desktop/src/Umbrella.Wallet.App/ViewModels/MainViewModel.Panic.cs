using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The panic password (roadmap P1.3): typed on the unlock screen in place of the wallet password, it
/// erases every wallet this app keeps on the device and the app comes back as freshly installed.
///
/// The duress password (P1.1) is for being made to open the wallet; this is for the device being about
/// to be taken. Both follow the same rule: the screen never says whether one is set, and the files look
/// the same either way (<see cref="PanicGuard"/>).
///
/// A panic password that opened a wallet would turn that wallet's own unlock into its erasure, so it is
/// refused if it opens any vault on the device — the removed ones too, since those can be put back — and
/// every password chosen afterwards (a new wallet, a changed password, a decoy) is refused if it is the
/// panic password.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _panicVaultPassword = string.Empty;
    [ObservableProperty] private string _panicNewPassword = string.Empty;
    [ObservableProperty] private string _panicConfirmPassword = string.Empty;
    [ObservableProperty] private string _panicStatus = string.Empty;
    [ObservableProperty] private string _panicError = string.Empty;

    private PanicGuard? _panic;

    /// <summary>The guard beside this device's vaults, written as noise the first time it is needed.</summary>
    private PanicGuard Panic
    {
        get
        {
            if (_panic is null)
            {
                _panic = new PanicGuard(_registry.DataDirectory);
                _panic.EnsureExists();
            }

            return _panic;
        }
    }

    private void ClearPanicFields()
    {
        PanicVaultPassword = string.Empty;
        PanicNewPassword = string.Empty;
        PanicConfirmPassword = string.Empty;
    }

    /// <summary>
    /// The panic password was typed: erase, and say nothing. Whoever is watching sees the first-run
    /// screen of an app with no wallet in it — the same thing a fresh install shows.
    /// </summary>
    private void PanicWipe()
    {
        ClearPasswordFields();
        ClearPanicFields();
        WipeThisDevice();
        FormError = string.Empty;
        StatusMessage = string.Empty;
        PanicStatus = string.Empty;
        PanicError = string.Empty;
    }

    [RelayCommand]
    private async Task SetPanicPasswordAsync()
    {
        PanicError = string.Empty;
        PanicStatus = string.Empty;

        if (PanicNewPassword.Length < MinPasswordLength)
        {
            PanicError = string.Format(Loc.Instance["panic.errShort"], MinPasswordLength);
            return;
        }

        if (!string.Equals(PanicNewPassword, PanicConfirmPassword, StringComparison.Ordinal))
        {
            PanicError = Loc.Instance["panic.errMismatch"];
            return;
        }

        if (string.Equals(PanicNewPassword, PanicVaultPassword, StringComparison.Ordinal))
        {
            PanicError = Loc.Instance["panic.errSame"];
            return;
        }

        await RunBusyAsync(async () =>
        {
            // The wallet password first: setting a trap on an unlocked PC must take more than reaching it.
            try
            {
                await _vault.UnlockAsync(PanicVaultPassword);
            }
            catch
            {
                PanicError = Loc.Instance["duress.errWrongPw"];
                return;
            }

            // It must open nothing here — any wallet, either slot, removed or not.
            foreach (var file in _registry.EveryVaultFile())
            {
                try
                {
                    await new EncryptedFileSeedVault(file).UnlockAsync(PanicNewPassword);
                }
                catch
                {
                    continue;   // does not open this one: as it should be
                }

                PanicError = Loc.Instance["panic.errOpens"];
                return;
            }

            var chosen = PanicNewPassword;
            await Task.Run(() => Panic.Set(chosen));
            ClearPanicFields();
            // Not written to the activity log: a line saying a panic password exists is the one thing
            // somebody who later gets this wallet opened should not be able to read.
            PanicStatus = Loc.Instance["panic.set"];
        });
    }

    /// <summary>Puts the guard back to noise. Like the decoy's removal, it cannot say whether one was set.</summary>
    [RelayCommand]
    private async Task RemovePanicPasswordAsync()
    {
        PanicError = string.Empty;
        PanicStatus = string.Empty;

        await RunBusyAsync(async () =>
        {
            try
            {
                await _vault.UnlockAsync(PanicVaultPassword);
            }
            catch
            {
                PanicError = Loc.Instance["duress.errWrongPw"];
                return;
            }

            await Task.Run(() => Panic.Clear());
            ClearPanicFields();
            PanicStatus = Loc.Instance["duress.removed"];
        });
    }
}
