using System.Text.RegularExpressions;
using Umbrella.Wallet.App;
using Umbrella.Wallet.App.Controls;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The balance chart under the pointer, Settings search in the wallet's own language, notes that close
/// for good, and a wallet's total that stays the whole wallet's.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class InterfaceAndSearchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-ui-{Guid.NewGuid():N}");

    public InterfaceAndSearchTests() => MainViewModel.FetchCurrencyOnStart = false;

    private MainViewModel NewViewModel()
    {
        TestDataIsolation.RestoreBaselineSettings();
        return new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));
    }

    /// <summary>The wallet started again on the same settings file (NewViewModel resets it first).</summary>
    private MainViewModel Reopened() => new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    // ---------------- the balance chart ----------------

    /// <summary>A wallet of stablecoins that moved by hundredths of a percent drew as a mountain range:
    /// the scale stretched whatever moved to the full height. The floor keeps it the flat line it is.</summary>
    [Fact]
    public void The_scale_keeps_a_stablecoin_wallet_flat_and_a_real_move_its_own()
    {
        var (min, max) = GlowChart.Scale([100.0, 100.05, 99.97, 100.02], 0.01);
        Assert.True(max - min >= 1.0 - 1e-9);   // at least 1% of the value
        Assert.True(min < 99.97 && max > 100.05);

        var (bare, top) = GlowChart.Scale([100.0, 100.05, 99.97, 100.02], 0);
        Assert.Equal(99.97, bare, 6);
        Assert.Equal(100.05, top, 6);

        var (low, high) = GlowChart.Scale([100.0, 120.0], 0.01);   // a real move keeps its scale
        Assert.Equal(100, low, 6);
        Assert.Equal(120, high, 6);
    }

    [Fact]
    public void The_pointer_picks_the_nearest_point()
    {
        Assert.Equal(0, GlowChart.NearestIndex(-5, 100, 96));
        Assert.Equal(95, GlowChart.NearestIndex(150, 100, 96));
        Assert.Equal(48, GlowChart.NearestIndex(50.6, 100, 96));
        Assert.Equal(0, GlowChart.NearestIndex(10, 100, 1));
    }

    [Fact]
    public void Each_point_names_its_moment_across_the_window()
    {
        Fx.SetLanguage("en");
        var end = new DateTime(2026, 10, 2, 12, 0, 0);
        Assert.Equal(end.AddHours(-24).ToString("ddd HH:mm", Fx.Culture), MainViewModel.PortfolioTimeLabel("1D", end, 0, 96));
        Assert.Equal(end.AddDays(-3.5).ToString("ddd d MMM · HH:mm", Fx.Culture), MainViewModel.PortfolioTimeLabel("1W", end, 1, 3));
        Assert.Equal(end.ToString("d MMM yyyy", Fx.Culture), MainViewModel.PortfolioTimeLabel("1Y", end, 95, 96));
    }

    // ---------------- Settings search ----------------

    [Theory]
    [InlineData("uk", "тема", "settings.theme")]
    [InlineData("uk", "Мова", "settings.language")]
    [InlineData("uk", "фраза", "settings.recovery")]
    [InlineData("uk", "проксі", "settings.proxy")]
    [InlineData("uk", "seed", "settings.recovery")]
    [InlineData("uk", "нагору", "settings.scrollTop")]
    [InlineData("uk", "гаманець додати", "settings.walletAddTitle")]
    [InlineData("ru", "тема", "settings.theme")]
    [InlineData("en", "proxy", "settings.proxy")]
    [InlineData("en", "Theme", "settings.theme")]
    [InlineData("de", "sprache", "settings.language")]
    public void Settings_search_finds_a_setting_in_the_wallets_language(string language, string query, string titleKey)
    {
        var before = Loc.Instance.CurrentCode;
        try
        {
            Loc.Instance.CurrentCode = language;
            Assert.Contains(MainViewModel.SearchSettings(query), s => s.TitleKey == titleKey);
        }
        finally
        {
            Loc.Instance.CurrentCode = before;
        }
    }

    [Fact]
    public void Settings_search_shows_titles_as_sentences_and_finds_nothing_for_nonsense()
    {
        Assert.Empty(MainViewModel.SearchSettings("qqqzzzxx"));
        Assert.Empty(MainViewModel.SearchSettings("   "));
        Assert.Equal("Tor · hides your IP", SettingsShortcut.SentenceCase("TOR · HIDES YOUR IP"));
        Assert.Equal("Custom proxy (SOCKS5)", SettingsShortcut.SentenceCase("CUSTOM PROXY (SOCKS5)"));
        Assert.Equal("Already mixed Case", SettingsShortcut.SentenceCase("Already mixed Case"));
    }

    /// <summary>The index is written from the Settings screen. A card added there and not here would be
    /// a setting nobody can find, so every card's heading must be in it.</summary>
    [Fact]
    public void Every_settings_card_can_be_found()
    {
        var xaml = File.ReadAllLines(Path.Combine(RepoRoot(), "desktop", "src", "Umbrella.Wallet.App", "Views", "MainWindow.axaml"));
        var headings = new List<string>();
        for (var i = 0; i < xaml.Length; i++)
        {
            if (!Regex.IsMatch(xaml[i], @"<Border Classes=""card""[^>]*IsVisible=""\{Binding IsTab\w+\}""")) continue;
            for (var j = i; j < Math.Min(xaml.Length, i + 12); j++)
            {
                var key = Regex.Match(xaml[j], @"\[([a-zA-Z0-9_.]+)\], Source=\{x:Static app:Loc\.Instance\}");
                if (!key.Success) continue;
                headings.Add(key.Groups[1].Value);
                break;
            }
        }

        Assert.True(headings.Count > 20, $"only {headings.Count} cards read from the screen");
        var indexed = MainViewModel.SettingsIndex.Select(s => s.TitleKey).ToHashSet();
        Assert.Empty(headings.Where(h => !indexed.Contains(h)));
    }

    [Fact]
    public void Picking_a_result_opens_its_pane_and_asks_for_its_card()
    {
        var vm = NewViewModel();
        string? asked = null;
        vm.SettingsFocusRequested += title => asked = title;
        var proxy = MainViewModel.SettingsIndex.First(s => s.TitleKey == "settings.proxy");

        vm.SettingsSearch = "proxy";
        Assert.Contains(proxy, vm.SettingsResults);
        vm.OpenSettingResultCommand.Execute(proxy);

        Assert.Equal("Privacy", vm.SettingsTab);
        Assert.Equal(string.Empty, vm.SettingsSearch);
        Assert.Equal(Loc.Instance["settings.proxy"], asked);
    }

    // ---------------- notes that close ----------------

    [Fact]
    public void A_closed_note_stays_closed_until_settings_brings_it_back()
    {
        var vm = NewViewModel();
        try
        {
            Assert.True(vm.ShowSwapIntro);
            Assert.True(vm.ShowBuyHowTo);

            vm.DismissNoticeCommand.Execute(MainViewModel.SwapIntroNotice);
            Assert.False(vm.ShowSwapIntro);
            Assert.True(vm.ShowBuyHowTo);
            Assert.True(vm.HasDismissedNotices);
            Assert.False(Reopened().ShowSwapIntro);   // remembered

            vm.RestoreNoticesCommand.Execute(null);
            Assert.True(vm.ShowSwapIntro);
            Assert.False(vm.HasDismissedNotices);
        }
        finally
        {
            vm.RestoreNoticesCommand.Execute(null);
        }
    }

    [Fact]
    public void The_back_to_top_button_can_be_turned_off()
    {
        var vm = NewViewModel();
        try
        {
            Assert.True(vm.ScrollToTopEnabled);
            vm.ScrollToTopEnabled = false;
            Assert.False(Reopened().ScrollToTopEnabled);
        }
        finally
        {
            vm.ScrollToTopEnabled = true;
        }
    }

    // ---------------- wallets ----------------

    [Fact]
    public void A_wallet_the_app_named_reads_in_the_wallets_language()
    {
        var before = Loc.Instance.CurrentCode;
        try
        {
            Loc.Instance.CurrentCode = "uk";
            Assert.Equal("Гаманець 2", MainViewModel.WalletDisplayName("Wallet 2"));
            Assert.Equal("Wallet 2 old", MainViewModel.WalletDisplayName("Wallet 2 old"));   // typed by someone: kept
            Assert.Equal("Savings", MainViewModel.WalletDisplayName("Savings"));
        }
        finally
        {
            Loc.Instance.CurrentCode = before;
        }
    }

    /// <summary>With "Bitcoin" picked above the asset list, the next refresh turned the TOTAL into the
    /// Bitcoin figure, and the wallet switcher showed that as the wallet's balance.</summary>
    [Fact]
    public void The_total_is_the_whole_wallet_whatever_the_list_is_filtered_to()
    {
        var vm = NewViewModel();
        vm.Accounts.Clear();
        vm.Accounts.Add(new WalletAccountViewModel("BTC", "Bitcoin", "Ready", "bc1q", "BIP84", 50_000, 1, "Bitcoin", 0,
            Balance: BalanceRead.Live));
        vm.Accounts.Add(new WalletAccountViewModel("USDT", "Tether USD · TRC20", "Ready", "TX", "TRC20 on TRON", 1, 500, "TRON", 0,
            Balance: BalanceRead.Live));

        var animations = vm.AnimationsEnabled;
        vm.AnimationsEnabled = false;   // the total counts up on a timer otherwise
        try
        {
            vm.SetChainFilterCommand.Execute("All");
            vm.RecomputeHoldingsForTest();
            var whole = vm.TotalBalanceMain;
            var expected = (50_500 * (double)Fx.Rate).ToString("N2", Fx.Culture);
            Assert.StartsWith(whole, expected);          // BTC and USDT together
            vm.SetChainFilterCommand.Execute("Bitcoin");
            vm.RecomputeHoldingsForTest();               // the next refresh, with the filter still on

            Assert.Single(vm.Holdings);                  // the list is filtered…
            Assert.Equal(whole, vm.TotalBalanceMain);    // …the total is not
        }
        finally
        {
            vm.SetChainFilterCommand.Execute("All");
            vm.AnimationsEnabled = animations;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        MainViewModel.FetchCurrencyOnStart = true;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
