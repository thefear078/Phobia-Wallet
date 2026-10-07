using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.App.Views;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Umbrella.Wallet.UiTests.HeadlessApp))]

namespace Umbrella.Wallet.UiTests;

/// <summary>The real App (its styles, themes and templates), on Avalonia's off-screen platform.</summary>
public sealed class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Umbrella.Wallet.App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>
/// Every screen of the wallet, drawn, and read back for what a person would see.
///
/// 1,800 view-model tests passed while the home page's market card printed
/// "MarketRowViewModel { Symbol = BTC, Name = … }" six times: the row template it named lived on another
/// page, nothing failed, and the list fell back to each row's ToString(). Only drawing the screen shows
/// that. These open a wallet, visit every section on the desktop and on the phone layout, and fail on any
/// text that is an object's name rather than words — a record's "Type { Property = … }" or a .NET type
/// name — and on any section whose drawing throws.
/// </summary>
public sealed class RenderedScreensTests
{
    private const string Password = "phobia-ui-tests-vault-2026";

    /// <summary>The desktop window's sections, as the navigation names them.</summary>
    private static readonly string[] Sections =
    [
        "Portfolio", "Market", "Send", "Receive", "Activity", "Swap", "Security", "Settings", "Staking",
        "Nfts", "Discover", "Buy", "P2p", "Connect", "News",
    ];

    /// <summary>A record's ToString ("MarketRowViewModel { Symbol = BTC") or a bare type name.</summary>
    private static readonly Regex RawObject = new(@"\b[A-Z]\w+ \{ [A-Z]\w* = |\bUmbrella\.Wallet\.\w+");

    [ModuleInitializer]
    internal static void Offline()
    {
        // A throwaway data folder, the kill-switch armed with no proxy (nothing leaves the machine), no
        // Tor start, and the first-run consent already given, as the view-model suite does.
        var dir = Path.Combine(Path.GetTempPath(), $"phobia-ui-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("UMBRELLA_DATA_DIR", dir);
        File.WriteAllText(Path.Combine(dir, "ui-settings.json"),
            $$"""{"TorOnlyMode":true,"Language":"en","FloatingCrystals":false,"CrystalGlints":false,"CardShine":false,"AcceptedTermsVersion":{{FirstRunConsent.CurrentVersion}}}""");
        // Last session's prices, so the market lists — the home card among them — have rows to draw.
        File.WriteAllText(Path.Combine(dir, "market.json"),
            """[{"Symbol":"BTC","Price":83910.2,"Change":-2.41},{"Symbol":"ETH","Price":2608.42,"Change":3.94},{"Symbol":"LTC","Price":67.46,"Change":-3.43},{"Symbol":"DOGE","Price":0.09,"Change":4.91},{"Symbol":"TRX","Price":0.33,"Change":-1.0},{"Symbol":"SOL","Price":111.3,"Change":0.5}]""");
        MainViewModel.StartTorAutomatically = false;
        MainViewModel.FetchCurrencyOnStart = false;
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(true);
    }

    [AvaloniaFact]
    public void Every_desktop_section_draws_words_not_objects()
    {
        var vm = OpenWallet();
        var window = new MainWindow { DataContext = vm, Width = 1320, Height = 900 };
        window.Show();
        Pump();

        Assert.NotEmpty(vm.RailMarketRows);   // the home card has rows to draw, so this test can fail
        var problems = new List<string>();
        foreach (var section in Sections)
        {
            vm.ActiveSection = section;
            Pump();
            problems.AddRange(RawText(window).Select(t => $"{section}: {t}"));
        }

        window.Close();
        Assert.True(problems.Count == 0, "Objects drawn as text:\n" + string.Join("\n", problems.Distinct()));
    }

    [AvaloniaFact]
    public void The_phone_layout_draws_words_not_objects()
    {
        var vm = OpenWallet();
        var window = new Window { Width = 390, Height = 844, Content = new MobileShell { DataContext = vm } };
        window.Show();
        Pump();

        var problems = new List<string>(RawText(window).Select(t => $"Home: {t}"));
        foreach (var section in new[] { "Activity", "Discover", "Settings", "Send", "Receive", "Swap", "Market" })
        {
            vm.ActiveSection = section;
            Pump();
            problems.AddRange(RawText(window).Select(t => $"{section}: {t}"));
        }

        window.Close();
        Assert.True(problems.Count == 0, "Objects drawn as text:\n" + string.Join("\n", problems.Distinct()));
    }

    private static MainViewModel OpenWallet()
    {
        UiSettings.LoadAndApply();
        var vm = new MainViewModel(new WalletRegistry())
        {
            Password = Password,
            ConfirmPassword = Password,
        };
        Wait(vm.CreateWalletCommand.ExecuteAsync(null));
        vm.ConfirmPhraseBackupCommand.Execute(null);
        Pump();
        Assert.True(vm.IsUnlocked, "the test wallet did not open");
        return vm;
    }

    private static IEnumerable<string> RawText(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(text => RawObject.IsMatch(text))
            .Select(text => text.Length > 120 ? text[..120] + "…" : text);

    private static void Pump()
    {
        for (var i = 0; i < 12; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(90);
        while (!task.IsCompleted && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(task.IsCompleted, "timed out");
        Pump();
    }
}
