using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Settings → Backup: "Check the phrase you wrote down" (roadmap P1.8, <see cref="PhraseCheck"/>).
///
/// The wallet password unlocks the vault's phrase for the comparison only; the phrase is never put on
/// screen, and what was typed is cleared as soon as it has been compared. The answer is "this restores
/// this wallet" or which word positions differ — enough to fix a paper backup, nothing that would help
/// somebody who does not already hold it.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _phraseCheckInput = string.Empty;
    [ObservableProperty] private string _phraseCheckResult = string.Empty;
    [ObservableProperty] private bool _phraseCheckOk;

    public bool HasPhraseCheckResult => PhraseCheckResult.Length > 0;

    partial void OnPhraseCheckResultChanged(string value) => OnPropertyChanged(nameof(HasPhraseCheckResult));

    /// <summary>Green for "restores this wallet", amber for anything else.</summary>
    public string PhraseCheckColor => PhraseCheckOk ? "UmPos" : "#E7CA83";

    partial void OnPhraseCheckOkChanged(bool value) => OnPropertyChanged(nameof(PhraseCheckColor));

    /// <summary>Forgets what was typed and said (on lock, so the next person at the PC sees nothing).</summary>
    private void ClearPhraseCheck()
    {
        PhraseCheckInput = string.Empty;
        PhraseCheckResult = string.Empty;
        PhraseCheckOk = false;
    }

    [RelayCommand]
    private async Task CheckWrittenPhraseAsync()
    {
        PhraseCheckResult = string.Empty;
        PhraseCheckOk = false;
        if (!HasVault) return;
        if (PhraseCheck.Words(PhraseCheckInput).Count == 0)
        {
            PhraseCheckResult = Loc.Instance["backup.check.empty"];
            return;
        }

        if (SettingsPassword.Length < MinPasswordLength)
        {
            PhraseCheckResult = string.Format(Loc.Instance["backup.check.password"], MinPasswordLength);
            return;
        }

        await RunBusyAsync(async () =>
        {
            string stored;
            try
            {
                stored = await _vault.UnlockAsync(SettingsPassword);
            }
            catch
            {
                PhraseCheckResult = Loc.Instance["backup.check.wrongPassword"];
                return;
            }
            finally
            {
                SettingsPassword = string.Empty;
            }

            var result = PhraseCheck.Compare(stored, PhraseCheckInput);
            PhraseCheckInput = string.Empty;   // what was typed is not kept
            PhraseCheckOk = result.Matches;
            PhraseCheckResult = result switch
            {
                { Matches: true } => string.Format(Loc.Instance["backup.check.ok"], result.StoredCount),
                { Mismatches.Count: 0 } => string.Format(Loc.Instance["backup.check.count"], result.TypedCount, result.StoredCount),
                { Mismatches.Count: 1 } => string.Format(Loc.Instance["backup.check.oneDiff"], result.Mismatches[0]),
                _ => string.Format(Loc.Instance["backup.check.diff"], string.Join(", ", result.Mismatches)),
            };
        });
    }
}
