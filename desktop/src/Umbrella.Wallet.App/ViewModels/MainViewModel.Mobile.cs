using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// What the phone layout (the Android app, <see cref="Views.MobileShell"/>) shows that the desktop
/// does not ask for in the same shape: the 24h move as its own line, the unread-activity note and the
/// bell's dot, a short list of assets and four market tiles on the home screen, and the two sheets —
/// the crystal button's quick actions and the wallet switcher.
/// </summary>
public partial class MainViewModel
{
    // ---------------- the balance card ----------------

    /// <summary>"-¥0.00 (24h)" — the 24h move in money, beside its percentage.</summary>
    public string Change24hDelta
    {
        get
        {
            // The same arithmetic as the balance card's 24h label: the value-weighted average move.
            var valued = WalletValueRows();
            var shown = (double)Fx.Rate * valued.Sum(v => v.Value);
            double weighted = 0, weight = 0;
            foreach (var (value, change) in valued)
            {
                if (value <= 0) continue;
                weighted += change * value;
                weight += value;
            }
            var move = weight > 0 ? shown * (weighted / weight / 100.0) : 0;
            return IsBalanceHidden
                ? "•••"
                : $"{(move >= 0 ? "+" : "-")}{Fx.Symbol}{Math.Abs(move).ToString("N2", Fx.Culture)} (24h)";
        }
    }

    // ---------------- unread activity ----------------

    /// <summary>Events newer than the last time the activity feed was opened.</summary>
    public int UnreadActivityCount
    {
        get
        {
            var seen = _uiSettings.ActivitySeenAtMs;
            return seen == 0 ? 0 : Activity.Count(a => a.UnixMs > seen);
        }
    }

    public bool HasUnreadActivity => UnreadActivityCount > 0;

    public string UnreadActivityLabel => string.Format(Loc.Instance["mobile.unread"], UnreadActivityCount);

    private void NotifyUnreadActivity()
    {
        if (_uiSettings.ActivitySeenAtMs == 0 && Activity.Count > 0)
        {
            // First run: what is already there is not news.
            _uiSettings.ActivitySeenAtMs = Activity.Max(a => a.UnixMs);
            _uiSettings.Save();
        }
        OnPropertyChanged(nameof(UnreadActivityCount));
        OnPropertyChanged(nameof(HasUnreadActivity));
        OnPropertyChanged(nameof(UnreadActivityLabel));
    }

    private void MarkActivitySeen()
    {
        var newest = Activity.Count == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : Activity.Max(a => a.UnixMs);
        if (newest <= _uiSettings.ActivitySeenAtMs) return;
        _uiSettings.ActivitySeenAtMs = newest;
        _uiSettings.Save();
        NotifyUnreadActivity();
    }

    // ---------------- home lists ----------------

    /// <summary>The first assets of the list for the home screen, or all of them after "See all".</summary>
    public IReadOnlyList<HoldingRowViewModel> HomeAssets => HomeAssetsExpanded ? Holdings.ToList() : Holdings.Take(4).ToList();

    [ObservableProperty] private bool _homeAssetsExpanded;

    public string HomeAssetsToggleLabel => Loc.Instance[HomeAssetsExpanded ? "mobile.showLess" : "mobile.seeAll"];

    partial void OnHomeAssetsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(HomeAssets));
        OnPropertyChanged(nameof(HomeAssetsToggleLabel));
    }

    [RelayCommand]
    private void ToggleHomeAssets() => HomeAssetsExpanded = !HomeAssetsExpanded;

    /// <summary>Four market tiles: Bitcoin, Ethereum, Litecoin, Dogecoin when priced.</summary>
    public IReadOnlyList<MarketRowViewModel> MarketOverviewRows =>
        Market.Where(m => m.HasPrice).Take(4).ToList();

    /// <summary>"Your assets performance in the last 7 days", for the chart's window.</summary>
    public string PortfolioCaption => Loc.Instance["mobile.perf." + PortfolioRange];

    partial void OnPortfolioRangeChanged(string value) => OnPropertyChanged(nameof(PortfolioCaption));

    private void NotifyHomeLists()
    {
        OnPropertyChanged(nameof(HomeAssets));
        OnPropertyChanged(nameof(MarketOverviewRows));
        OnPropertyChanged(nameof(Change24hDelta));
    }

    // ---------------- sheets ----------------

    /// <summary>The crystal button's quick actions.</summary>
    [ObservableProperty] private bool _isQuickSheetOpen;

    /// <summary>The wallet switcher under the wallet's name.</summary>
    [ObservableProperty] private bool _isWalletSheetOpen;

    [RelayCommand]
    private void ToggleQuickSheet()
    {
        IsWalletSheetOpen = false;
        IsQuickSheetOpen = !IsQuickSheetOpen;
    }

    [RelayCommand]
    private void ToggleWalletSheet()
    {
        IsQuickSheetOpen = false;
        IsWalletSheetOpen = !IsWalletSheetOpen;
    }

    /// <summary>A quick action: close the sheet and open the section.</summary>
    [RelayCommand]
    private void QuickAction(string? section)
    {
        IsQuickSheetOpen = false;
        IsWalletSheetOpen = false;
        if (!string.IsNullOrWhiteSpace(section)) SelectSection(section);
    }

    /// <summary>A market tile on the phone's home: open Market on that coin's chart.</summary>
    [RelayCommand]
    private async Task OpenMarketCoin(MarketRowViewModel? row)
    {
        SelectSection("Market");
        if (row is not null) await SelectMarketCoinAsync(row);
    }

    /// <summary>The phone's back gesture: close a sheet, else go home. False when already home.</summary>
    public bool GoBack()
    {
        if (IsQuickSheetOpen || IsWalletSheetOpen)
        {
            IsQuickSheetOpen = false;
            IsWalletSheetOpen = false;
            return true;
        }
        if (!IsUnlocked || ActiveSection == "Portfolio") return false;
        SelectSection("Portfolio");
        return true;
    }
}
