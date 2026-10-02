using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App.Views;

/// <summary>
/// The Android app's single view. The phone's back gesture closes a sheet or returns home, and every
/// section opens at its top.
/// </summary>
public partial class MobileShell : UserControl
{
    /// <summary>
    /// Raised with true while a seed phrase or a Monero key is on screen, and false when it goes. The
    /// Android activity turns it into FLAG_SECURE - no screenshot, no screen recording, a blank thumbnail
    /// in the recent apps - as the desktop blocks capture of the same screens.
    /// </summary>
    public static event Action<bool>? SecretOnScreenChanged;

    /// <summary>Whether a secret is on screen now, for a listener that subscribes late.</summary>
    public static bool IsSecretOnScreen { get; private set; }

    private MainViewModel? _observed;
    private TopLevel? _topLevel;

    public MobileShell()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_observed is not null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = DataContext as MainViewModel;
            if (_observed is not null) _observed.PropertyChanged += OnViewModelPropertyChanged;
            ReportSecret();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null) _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Handled means "stay in the app"; at home the gesture leaves it, as a phone expects.
        if (_observed?.GoBack() == true) e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ActiveSection)) MobileScroll.Offset = default;
        if (e.PropertyName is nameof(MainViewModel.IsBackupStage)
            or nameof(MainViewModel.IsSettingsPhraseVisible)
            or nameof(MainViewModel.IsMoneroKeysVisible))
        {
            ReportSecret();
        }
    }

    private void ReportSecret()
    {
        var shown = _observed is { } vm && (vm.IsBackupStage || vm.IsSettingsPhraseVisible || vm.IsMoneroKeysVisible);
        if (shown == IsSecretOnScreen) return;
        IsSecretOnScreen = shown;
        SecretOnScreenChanged?.Invoke(shown);
    }
}
