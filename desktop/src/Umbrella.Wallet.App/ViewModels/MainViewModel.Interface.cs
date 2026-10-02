using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Small helpers around the screens: the "back to top" button and the notes a user can close for good.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Settings switch: the round "back to top" button on long pages.</summary>
    public bool ScrollToTopEnabled
    {
        get => _uiSettings.ScrollToTopButton;
        set
        {
            if (_uiSettings.ScrollToTopButton == value) return;
            _uiSettings.ScrollToTopButton = value;
            _uiSettings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Swap's note on how routes are chosen.</summary>
    public const string SwapIntroNotice = "swap.intro";

    /// <summary>Buy's three-step "how it works" card.</summary>
    public const string BuyHowToNotice = "buy.howTo";

    public bool ShowSwapIntro => !IsNoticeDismissed(SwapIntroNotice);

    public bool ShowBuyHowTo => !IsNoticeDismissed(BuyHowToNotice);

    public bool HasDismissedNotices => _uiSettings.DismissedNotices.Count > 0;

    private bool IsNoticeDismissed(string id) =>
        _uiSettings.DismissedNotices.Contains(id, StringComparer.Ordinal);

    /// <summary>Closes a note for good — until Settings brings closed notes back.</summary>
    [RelayCommand]
    private void DismissNotice(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || IsNoticeDismissed(id)) return;
        _uiSettings.DismissedNotices.Add(id);
        _uiSettings.Save();
        NotifyNotices();
    }

    [RelayCommand]
    private void RestoreNotices()
    {
        if (_uiSettings.DismissedNotices.Count == 0) return;
        _uiSettings.DismissedNotices.Clear();
        _uiSettings.Save();
        NotifyNotices();
        ShowToast(Loc.Instance["status.notesRestored"], isError: false);
    }

    private void NotifyNotices()
    {
        OnPropertyChanged(nameof(ShowSwapIntro));
        OnPropertyChanged(nameof(ShowBuyHowTo));
        OnPropertyChanged(nameof(HasDismissedNotices));
    }
}
