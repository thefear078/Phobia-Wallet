using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure;
using Watched = Umbrella.Wallet.Infrastructure.Network.WatchAddress;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>An exchange in the Connect picker: its name, a badge of its initials in its brand colour.</summary>
public sealed record ExchangeTile(string Name, string Initials, string Color, bool IsSelected);

/// <summary>A watched address as Connect lists it: the coin, its name, the address, and what it holds now.</summary>
public sealed record WatchRowViewModel(Watched Watch, string Symbol, string Label, string ShortAddress, string Network, string Holding)
{
    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Avalonia.Media.Imaging.Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>A connected exchange account as Connect lists it.</summary>
public sealed record ExchangeRowViewModel(ExchangeCredential Credential, string Label, string Exchange, string Initials, string Color, string Holding);

/// <summary>
/// Connect: other wallets' addresses watched read-only, and exchange accounts read through read-only API
/// keys — shown the way the rest of the wallet shows money: each with its coin or exchange, what it
/// holds right now, and one place to add another.
/// </summary>
public partial class MainViewModel
{
    /// <summary>"Wallets" (watched addresses) or "Exchanges".</summary>
    [ObservableProperty] private string _connectTab = "Wallets";

    public bool IsConnectWallets => ConnectTab != "Exchanges";
    public bool IsConnectExchanges => ConnectTab == "Exchanges";

    partial void OnConnectTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsConnectWallets));
        OnPropertyChanged(nameof(IsConnectExchanges));
    }

    [RelayCommand]
    private void SelectConnectTab(string? tab) => ConnectTab = tab == "Exchanges" ? "Exchanges" : "Wallets";

    public IReadOnlyList<ExchangeTile> ExchangeTiles =>
        SupportedExchanges.Select(n => new ExchangeTile(n, ExchangeInitials(n), ExchangeColor(n), n == ExchangeName)).ToList();

    [RelayCommand]
    private void SelectExchange(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        ExchangeName = name;
        ExchangeError = string.Empty;
        OnPropertyChanged(nameof(ExchangeTiles));
    }

    public static string ExchangeInitials(string name) => name.ToUpperInvariant() switch
    {
        "BINANCE" => "BN",
        "BYBIT" => "BY",
        "OKX" => "OKX",
        "KRAKEN" => "KR",
        "KUCOIN" => "KC",
        "GATE.IO" => "GT",
        "MEXC" => "MX",
        "BITGET" => "BG",
        _ when name.Contains("CryptoBot", StringComparison.OrdinalIgnoreCase) => "CB",
        _ => name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant(),
    };

    public static string ExchangeColor(string name) => name.ToUpperInvariant() switch
    {
        "BINANCE" => "#C99A0A",
        "BYBIT" => "#D08C00",
        "OKX" => "#2F2F35",
        "KRAKEN" => "#5741D9",
        "KUCOIN" => "#1F9E83",
        "GATE.IO" => "#2354E6",
        "MEXC" => "#1972E2",
        "BITGET" => "#1593A3",
        _ => "#229ED9",
    };

    public ObservableCollection<WatchRowViewModel> WatchRows { get; } = [];
    public ObservableCollection<ExchangeRowViewModel> ExchangeRows { get; } = [];

    public bool HasWatchRows => WatchRows.Count > 0;
    public bool HasExchangeRows => ExchangeRows.Count > 0;
    public string WatchCountLabel => WatchRows.Count.ToString(Fx.Culture);
    public string ExchangeCountLabel => ExchangeRows.Count.ToString(Fx.Culture);

    /// <summary>What everything connected is worth — watched addresses and exchanges together.</summary>
    [ObservableProperty] private string _connectedValueLabel = "—";

    /// <summary>Rebuilds both lists from the watched addresses, the exchanges and the rows they hold.</summary>
    private void RebuildConnectRows()
    {
        double total = 0;
        WatchRows.Clear();
        foreach (var w in WatchAddresses)
        {
            var rows = Accounts.Where(a => a.SupportStatus == "Watch" && a.Address.Equals(w.Address, StringComparison.OrdinalIgnoreCase)).ToList();
            var native = rows.FirstOrDefault(a => string.Equals(a.Symbol, w.Chain, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
            var value = rows.Where(a => a.Balance != BalanceRead.Unknown).Sum(a => a.Price * a.Amount);
            total += value;
            var symbol = native?.Symbol ?? w.Chain.ToUpperInvariant();
            var network = Umbrella.Wallet.Core.Chains.ChainCatalog.All.FirstOrDefault(c => c.Symbol == symbol)?.Name ?? w.Chain;
            var holding = native is null
                ? "—"
                : native.Balance == BalanceRead.Unknown
                    ? Loc.Instance["connect.notRead"]
                    : $"{native.Amount.ToString("0.######", Fx.Culture)} {symbol}" + (value > 0 && !IsBalanceHidden ? $" · {Fx.Money(value)}" : "");
            WatchRows.Add(new WatchRowViewModel(w, symbol, string.IsNullOrWhiteSpace(w.Label) ? Shorten(w.Address) : w.Label,
                Shorten(w.Address), network, holding));
        }

        ExchangeRows.Clear();
        foreach (var c in Exchanges)
        {
            var rows = Accounts.Where(a => a.SupportStatus == "Exchange" && a.Address == c.Label && a.Derivation == c.Exchange).ToList();
            var value = rows.Sum(a => a.Price * a.Amount);
            total += value;
            var holding = rows.Count == 0
                ? Loc.Instance["connect.notRead"]
                : string.Format(Loc.Instance["connect.assets"], rows.Count) + (IsBalanceHidden ? "" : $" · {Fx.Money(value)}");
            ExchangeRows.Add(new ExchangeRowViewModel(c, c.Label, c.Exchange, ExchangeInitials(c.Exchange), ExchangeColor(c.Exchange), holding));
        }

        ConnectedValueLabel = IsBalanceHidden ? "•••••" : Fx.Money(total);
        OnPropertyChanged(nameof(HasWatchRows));
        OnPropertyChanged(nameof(HasExchangeRows));
        OnPropertyChanged(nameof(WatchCountLabel));
        OnPropertyChanged(nameof(ExchangeCountLabel));
    }
}
