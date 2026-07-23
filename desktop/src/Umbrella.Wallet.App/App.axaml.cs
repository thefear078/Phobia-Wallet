using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.App.Views;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Relocate anything an earlier version wrote to the system drive before the vault
            // is opened, so the wallet keeps working and stops growing C:.
            AppPaths.MigrateLegacyData();

            // Theme and language before the first window paints, so nothing flashes the
            // default palette on the way to the user's choice.
            UiSettings.LoadAndApply();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new EncryptedFileSeedVault()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}