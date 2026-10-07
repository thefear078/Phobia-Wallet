using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.App.Views;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Mobile;

/// <summary>
/// The Android app's one activity: Avalonia's host for the shared <see cref="Umbrella.Wallet.App.App"/>,
/// which shows the phone layout (Views/MobileShell). Portrait, edge to edge on the wallet's own colour.
/// </summary>
[Activity(
    Label = "Phobia",
    Theme = "@style/PhobiaTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity<Umbrella.Wallet.App.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        UseBundledServices();   // before base.OnCreate: that is where the wallet starts
        MobileShell.SecretOnScreenChanged += ProtectScreen;
        base.OnCreate(savedInstanceState);
        ProtectScreen(MobileShell.IsSecretOnScreen);
#if PHOBIA_SELFTEST
        _ = SelfTestAsync();
#endif
    }

#if PHOBIA_SELFTEST
    /// <summary>The emulator check: run the bundled Tor from the native-library folder, the way a user's
    /// "Tor on" does, and log how far it got. CI reads the "PHOBIA-SELFTEST" lines from logcat.</summary>
    private static int _selfTestStarted;

    private static async Task SelfTestAsync()
    {
        // Once per process: Android may create the activity twice at launch, and a second run would end the
        // first run's Tor (as a leftover) and report it as a failure.
        if (Interlocked.Exchange(ref _selfTestStarted, 1) == 1) return;
        const string tag = "PHOBIA-SELFTEST";
        Android.Util.Log.Info(tag, $"tor path {EmbeddedTorService.TorExecutablePath} present={EmbeddedTorService.IsBundlePresent}");
        Android.Util.Log.Info(tag, $"monero path {MoneroRpcService.ExecutablePath} present={MoneroRpcService.IsBundlePresent}");
        using var tor = new EmbeddedTorService();
        var progress = new Progress<string>(m => Android.Util.Log.Info(tag, m));
        var (ok, message) = await tor.StartAsync(progress);
        Android.Util.Log.Info(tag, ok ? $"TOR-OK {message}" : $"TOR-FAIL {message}");
    }
#endif

    /// <summary>
    /// Tor and monero-wallet-rpc ship inside the APK as lib*.so in the native-library folder — the one
    /// place Android lets an app run a program from (its own files are mounted no-exec), the way Tor
    /// Browser and Orbot run Tor. Android unpacks them there at install; the wallet is told where.
    /// </summary>
    private void UseBundledServices()
    {
        var libs = ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrEmpty(libs)) return;
        var tor = Path.Combine(libs, "libTor.so");
        var monero = Path.Combine(libs, "libmonero-wallet-rpc.so");
        if (File.Exists(monero)) MoneroRpcService.ExecutableOverride = monero;
        if (!File.Exists(tor)) return;
        EmbeddedTorService.ExecutableOverride = tor;
        MainViewModel.HasBundledServices = true;
        MainViewModel.StartTorAutomatically = true;
    }

    /// <summary>Back to the front: the same as a desktop window regaining focus — Tor restarted if the
    /// phone ended it in the background, stale balances read again.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        var view = (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;
        (view?.DataContext as MainViewModel)?.OnWindowActivated();
    }

    protected override void OnDestroy()
    {
        MobileShell.SecretOnScreenChanged -= ProtectScreen;
        base.OnDestroy();
    }

    /// <summary>While a seed phrase or a Monero key is on screen: no screenshots, no recording, and a
    /// blank thumbnail in the recent apps.</summary>
    private void ProtectScreen(bool secret)
    {
        if (secret) Window?.AddFlags(WindowManagerFlags.Secure);
        else Window?.ClearFlags(WindowManagerFlags.Secure);
    }
}
