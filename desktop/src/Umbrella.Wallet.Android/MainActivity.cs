using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Umbrella.Wallet.App.Views;

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
        MobileShell.SecretOnScreenChanged += ProtectScreen;
        base.OnCreate(savedInstanceState);
        ProtectScreen(MobileShell.IsSecretOnScreen);
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
