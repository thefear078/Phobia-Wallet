using Avalonia.Platform.Storage;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoLockDelay = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer _autoLockTimer = new() { Interval = AutoLockDelay };
    private MainViewModel? _observed;

    // SetWindowDisplayAffinity: WDA_EXCLUDEFROMCAPTURE (0x11) makes the window render black in
    // screenshots and screen-share while the seed phrase is visible. WDA_NONE (0) turns it off.
    private const uint WdaNone = 0x00;
    private const uint WdaExcludeFromCapture = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public MainWindow()
    {
        InitializeComponent();
        _autoLockTimer.Tick += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.LockVault();
            }
        };
        PointerPressed += OnUserActivity;
        KeyDown += OnUserActivity;
        Opened += (_, _) => ResetAutoLock();
        // The view owns the window, so it supplies the file dialog the view-model asks for.
        if (DataContext is MainViewModel backupVm)
        {
            backupVm.PickFileAsync = async (suggested, save) =>
            {
                var storage = StorageProvider;
                if (save)
                {
                    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save Umbrella backup",
                        SuggestedFileName = suggested,
                        DefaultExtension = "json",
                        FileTypeChoices = [new FilePickerFileType("Umbrella backup") { Patterns = ["*.json"] }],
                    });
                    return file?.TryGetLocalPath();
                }

                var opened = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Restore Umbrella backup",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Umbrella backup") { Patterns = ["*.json"] }],
                });
                return opened.Count > 0 ? opened[0].TryGetLocalPath() : null;
            };
        }

        Closed += (_, _) =>
        {
            _autoLockTimer.Stop();
            // Never leave the bundled Tor / Monero processes running after the wallet closes.
            if (DataContext is MainViewModel vm)
            {
                vm.ShutdownTor();
                vm.ShutdownMonero();
            }
        };
        DataContextChanged += OnDataContextChanged;
    }

    // --- Title-bar dragging (custom chrome) ----------------------------------
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    // --- Screenshot protection while the seed phrase is on screen -------------
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observed = DataContext as MainViewModel;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelPropertyChanged;
            UpdateCaptureProtection();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsBackupStage)
            or nameof(MainViewModel.IsSettingsPhraseVisible)
            or nameof(MainViewModel.IsMoneroKeysVisible))
        {
            UpdateCaptureProtection();
        }
    }

    private void UpdateCaptureProtection()
    {
        if (_observed is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetHandle();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Any on-screen secret (seed phrase or Monero spend key) blocks screen capture.
        var secretVisible = _observed.IsBackupStage
            || _observed.IsSettingsPhraseVisible
            || _observed.IsMoneroKeysVisible;
        SetWindowDisplayAffinity(handle, secretVisible ? WdaExcludeFromCapture : WdaNone);
    }

    private IntPtr TryGetHandle()
    {
        try
        {
            return TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private void OnUserActivity(object? sender, EventArgs eventArgs) => ResetAutoLock();

    private void ResetAutoLock()
    {
        _autoLockTimer.Stop();
        _autoLockTimer.Start();
    }
}
