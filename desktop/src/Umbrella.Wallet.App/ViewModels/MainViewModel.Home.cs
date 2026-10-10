using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Amounts;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>A theme offered as a swatch on the home screen.</summary>
public sealed record ThemeSwatch(string Id, string Name, IBrush Fill, IBrush Accent, bool IsActive)
{
    /// <summary>The outline: the theme's own accent on the theme in use, a faint hairline otherwise.</summary>
    public IBrush Edge => IsActive ? Accent : new SolidColorBrush(Color.Parse("#2EFFFFFF"));
}

/// <summary>
/// The home screen of the gold design: the balance chart, the Market card's tabs, the theme swatches,
/// and whether the window is wide enough to put the assets and the transactions side by side.
/// </summary>
public partial class MainViewModel
{
    // ================= layout =================

    /// <summary>The window's width, reported by the view. Only layout reads it.</summary>
    [ObservableProperty] private double _windowWidth = 1240;

    /// <summary>Assets and recent transactions side by side once there is room, stacked otherwise.</summary>
    public int HomeColumns => WindowWidth >= 1380 && !MobileMode ? 2 : 1;

    partial void OnWindowWidthChanged(double value) => OnPropertyChanged(nameof(HomeColumns));

    // ================= the balance chart =================

    /// <summary>The chart's window: 1D, 1W, 1M or 1Y.</summary>
    [ObservableProperty] private string _portfolioRange = "1D";

    /// <summary>What today's holdings were worth across the window, oldest first, in USD.</summary>
    [ObservableProperty] private IReadOnlyList<double> _portfolioSeries = [];

    /// <summary>The line under the chart: what it is, and what it leaves out. Shown whole as the
    /// short note's tooltip.</summary>
    [ObservableProperty] private string _portfolioChartNote = string.Empty;

    /// <summary>The same note in a few words, for the narrow card beside the balance.</summary>
    [ObservableProperty] private string _portfolioChartNoteShort = string.Empty;

    /// <summary>Shown in place of the chart when there is nothing to draw, and why.</summary>
    [ObservableProperty] private string _portfolioChartStatus = string.Empty;

    public bool HasPortfolioSeries => PortfolioSeries.Count > 1;

    /// <summary>When the series was drawn: point i of n is that moment minus the window times (1 − i/(n−1)).</summary>
    private DateTime _portfolioSeriesAt = DateTime.Now;

    partial void OnPortfolioSeriesChanged(IReadOnlyList<double> value)
    {
        _portfolioSeriesAt = DateTime.Now;
        OnPropertyChanged(nameof(HasPortfolioSeries));
        NotifyPortfolioPoints();
    }

    private void NotifyPortfolioPoints()
    {
        OnPropertyChanged(nameof(PortfolioPointLabels));
        OnPropertyChanged(nameof(PortfolioPointCaptions));
    }

    /// <summary>What the balance chart says under the pointer: that point's worth in the chosen
    /// currency — today's holdings at that moment's price — or dots while the balance is hidden.</summary>
    public IReadOnlyList<string> PortfolioPointLabels =>
        PortfolioSeries.Select(v => IsBalanceHidden ? "•••••" : Fx.Money(v)).ToList();

    /// <summary>Under each point's value: when it was, and how far the worth had moved since the start
    /// of the window. The last point is "now".</summary>
    public IReadOnlyList<string> PortfolioPointCaptions
    {
        get
        {
            var series = PortfolioSeries;
            var n = series.Count;
            if (n < 2) return [];
            var first = series[0];
            var captions = new List<string>(n);
            for (var i = 0; i < n; i++)
            {
                var when = i == n - 1 ? Loc.Instance["chart.now"] : PortfolioTimeLabel(PortfolioRange, _portfolioSeriesAt, i, n);
                var move = first > 0 ? (series[i] - first) / first * 100 : 0;
                captions.Add($"{when} · {(move >= 0 ? "+" : "−")}{Math.Abs(move).ToString("0.00", Fx.Culture)}%");
            }
            return captions;
        }
    }

    /// <summary>The wall-clock moment of point <paramref name="i"/> of <paramref name="n"/>, evenly spaced
    /// across the window that ends at <paramref name="end"/>, in the wallet's language.</summary>
    public static string PortfolioTimeLabel(string range, DateTime end, int i, int n)
    {
        var span = range switch
        {
            "1W" => TimeSpan.FromDays(7),
            "1M" => TimeSpan.FromDays(30),
            "1Y" => TimeSpan.FromDays(365),
            _ => TimeSpan.FromHours(24),
        };
        var t = end - TimeSpan.FromTicks((long)(span.Ticks * (1 - (double)i / Math.Max(1, n - 1))));
        return range switch
        {
            "1W" => t.ToString("ddd d MMM · HH:mm", Fx.Culture),
            "1M" => t.ToString("d MMM · HH:mm", Fx.Culture),
            "1Y" => t.ToString("d MMM yyyy", Fx.Culture),
            _ => t.ToString("ddd HH:mm", Fx.Culture),
        };
    }

    /// <summary>The label at the chart's lit end: the balance now, or dots while it is hidden.</summary>
    public string HeroEndLabel => IsBalanceHidden ? "•••••" : $"{CurrencySymbol}{TotalBalanceMain}{BalanceDisplayCents}";

    /// <summary>The market window each chart window reads its prices from.</summary>
    private static string MarketRangeFor(string range) => range switch
    {
        "1W" => "7D",
        "1M" => "30D",
        "1Y" => "1Y",
        _ => "24H",
    };

    /// <summary>Points on the chart. Every price series is resampled onto the same count, so coins
    /// whose sources report different numbers of candles still add up moment by moment.</summary>
    private const int PortfolioPoints = 96;

    /// <summary>Price history per market window, per coin, and when it was fetched.</summary>
    private readonly Dictionary<string, (DateTimeOffset At, Dictionary<string, IReadOnlyList<double>> Series)> _seriesByRange =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan SeriesReuse = TimeSpan.FromMinutes(10);

    private int _portfolioChartVersion;

    [RelayCommand]
    private async Task SetPortfolioRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == PortfolioRange) return;
        PortfolioRange = range;
        await RefreshPortfolioChartAsync();
    }

    /// <summary>Kept by <see cref="LoadSparklinesAsync"/>, so the chart reuses what the market list already fetched.</summary>
    private void RememberSeries(string marketRange, string symbol, IReadOnlyList<double> series)
    {
        if (!_seriesByRange.TryGetValue(marketRange, out var entry))
        {
            entry = (DateTimeOffset.UtcNow, new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase));
            _seriesByRange[marketRange] = entry;
        }

        entry.Series[symbol] = series;
    }

    /// <summary>
    /// Price history for EVERY coin in the market list, whatever this wallet holds, asked for in the same
    /// order by every copy of the wallet (alphabetical). Asking only for the coins you own - or asking for
    /// them first - would tell the price server which coins those are; this tells it nothing it could not
    /// see from any other copy. <paramref name="progress"/> gets what has arrived so far after each answer,
    /// so a caller can draw as soon as the coins it needs are in.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<double>>> MarketSeriesAsync(
        string marketRange, Action<IReadOnlyDictionary<string, IReadOnlyList<double>>>? progress = null)
    {
        var wanted = MarketSeriesSymbols();
        if (_seriesByRange.TryGetValue(marketRange, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < SeriesReuse &&
            wanted.All(s => cached.Series.ContainsKey(s) || Market.FirstOrDefault(m => m.Symbol == s) is not { HasPrice: true }))
        {
            return cached.Series;
        }

        var fetched = cached.Series is { } previous
            ? new Dictionary<string, IReadOnlyList<double>>(previous, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);

        // Four at a time, in the list's fixed order.
        using var gate = new SemaphoreSlim(4);
        var arrived = new object();
        var results = await Task.WhenAll(wanted.Where(s => !fetched.ContainsKey(s)).Select(async symbol =>
        {
            await gate.WaitAsync();
            try
            {
                var series = await _rates.GetPriceSeriesAsync(symbol, marketRange, CancellationToken.None);
                if (progress is not null && series.Count > 1)
                {
                    Dictionary<string, IReadOnlyList<double>> soFar;
                    lock (arrived)
                    {
                        fetched[symbol] = series;
                        soFar = new Dictionary<string, IReadOnlyList<double>>(fetched, StringComparer.OrdinalIgnoreCase);
                    }
                    progress(soFar);
                }
                return (symbol, series);
            }
            catch
            {
                // A coin without history is left out of the chart and named under it — never guessed.
                return (symbol, (IReadOnlyList<double>)[]);
            }
            finally
            {
                gate.Release();
            }
        }));
        foreach (var (symbol, series) in results)
        {
            if (series.Count > 1) fetched[symbol] = series;
        }

        _seriesByRange[marketRange] = (DateTimeOffset.UtcNow, fetched);
        return fetched;
    }

    /// <summary>The coins whose price history the wallet ever asks for: the market list, which is the same
    /// in every copy, in alphabetical order. Never "the coins this wallet holds".</summary>
    public List<string> MarketSeriesSymbols() =>
        Market.Select(m => m.Symbol).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

    /// <summary>
    /// What the wallet held at each moment of the window, valued at that moment's price.
    ///
    /// It used to be today's holdings at past prices — the wallet kept no history to do better — so a
    /// wallet funded an hour ago drew a day of balance it never had. With the transfers it has read
    /// (<see cref="PortfolioMoves"/>), each coin's amount is worked back through them. A coin whose
    /// transfers are not known is still drawn at today's amount, and the note under the chart says which
    /// kind of line it is.
    /// </summary>
    private async Task RefreshPortfolioChartAsync()
    {
        var version = Interlocked.Increment(ref _portfolioChartVersion);

        var end = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var start = end - (long)PortfolioSpan(PortfolioRange).TotalMilliseconds;
        var moves = PortfolioMoves();

        var holdings = Accounts
            .Where(a => a.SupportStatus is "Ready" or "Watch" or "Exchange" or "Receive only"
                        && !a.IsSuspectedSpam && a.Balance != BalanceRead.Unknown && a.Amount > 0)
            .GroupBy(a => a.Symbol.ToUpperInvariant())
            .Select(g => (Symbol: g.Key, Amount: g.Sum(a => a.Amount), Price: g.Max(a => a.Price)))
            .ToList();

        // Coins that are gone now but were here inside the window: sent away an hour ago, they were
        // still part of the balance this morning.
        foreach (var (symbol, list) in moves)
        {
            if (holdings.Any(h => h.Symbol == symbol) || !BalanceHistory.AnyWithin(list, start, end)) continue;
            var price = Accounts.Where(a => string.Equals(a.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Price).DefaultIfEmpty(0).Max();
            holdings.Add((symbol, 0, price));
        }

        if (holdings.Count == 0)
        {
            PortfolioSeries = [];
            PortfolioChartStatus = Loc.Instance["chart.empty"];
            PortfolioChartNote = string.Empty;
            PortfolioChartNoteShort = string.Empty;
            return;
        }

        if (!HasPortfolioSeries) PortfolioChartStatus = Loc.Instance["chart.loading"];

        // At once from the prices already on this device (the market list's and the last session's), so
        // a range switch draws instantly; then again when the network has answered.
        var marketRange = MarketRangeFor(PortfolioRange);
        var symbols = holdings.Select(h => h.Symbol).ToList();
        var known = PeekSeries(marketRange, symbols);
        if (known.Count == symbols.Count) DrawPortfolio(holdings, known, moves, start, end);

        // The whole market list is asked for (see MarketSeriesAsync); the chart is drawn the moment the
        // coins held are all in, not when the last coin of the list arrives.
        var drawnEarly = false;
        var series = await MarketSeriesAsync(marketRange, soFar =>
        {
            if (drawnEarly || version != _portfolioChartVersion || !symbols.All(soFar.ContainsKey)) return;
            drawnEarly = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (version == _portfolioChartVersion) DrawPortfolio(holdings, soFar, moves, start, end);
            });
        });
        if (version != _portfolioChartVersion) return;   // a newer request superseded this one
        DrawPortfolio(holdings, series, moves, start, end);
    }

    /// <summary>How far back each window reaches.</summary>
    private static TimeSpan PortfolioSpan(string range) => range switch
    {
        "1W" => TimeSpan.FromDays(7),
        "1M" => TimeSpan.FromDays(30),
        "1Y" => TimeSpan.FromDays(365),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>
    /// Every transfer this wallet knows of, per coin: the chain's own history as read so far together
    /// with what the wallet itself sent. Failed sends moved nothing and are left out.
    /// </summary>
    private Dictionary<string, List<BalanceMove>> PortfolioMoves()
    {
        var moves = new Dictionary<string, List<BalanceMove>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in MergedActivity())
        {
            if (row.Kind is not ("Sent" or "Received") || row.UnixMs <= 0 || row.Status == "Failed") continue;
            if (!double.TryParse(row.Amount.TrimStart('+', '-'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0) continue;
            var symbol = row.Asset.ToUpperInvariant();
            if (!moves.TryGetValue(symbol, out var list)) moves[symbol] = list = [];
            list.Add(new BalanceMove(row.UnixMs, row.Kind == "Sent" ? -amount : amount));
        }
        return moves;
    }

    /// <summary>Price history already on this device for each coin: fetched this session, or the candles
    /// the last session kept. However old — the fetch that follows replaces it.</summary>
    private Dictionary<string, IReadOnlyList<double>> PeekSeries(string marketRange, IEnumerable<string> symbols)
    {
        var found = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
        _seriesByRange.TryGetValue(marketRange, out var cached);
        foreach (var symbol in symbols)
        {
            if (cached.Series is { } kept && kept.TryGetValue(symbol, out var series) && series.Count > 1)
                found[symbol] = series;
            else if (Umbrella.Wallet.Infrastructure.Network.PublicMarketRatesClient.TryPeekCandles(symbol, marketRange, out var candles, out _)
                     && candles.Count > 1)
                found[symbol] = candles.Select(c => c.Close).ToList();
        }
        return found;
    }

    /// <summary>How far a coin's price history may end from its live price and still be the same market
    /// seen a moment apart: within this, the history is lined up with the live price.</summary>
    private const double PriceAnchorTolerance = 0.03;

    /// <summary>What was held at each moment, valued at that moment's price. The last point is the live
    /// price, the same one the balance above the chart is counted at, so "now" on the chart is the balance.</summary>
    private void DrawPortfolio(
        List<(string Symbol, double Amount, double Price)> holdings, IReadOnlyDictionary<string, IReadOnlyList<double>> series,
        IReadOnlyDictionary<string, List<BalanceMove>> moves, long start, long end)
    {
        var total = new double[PortfolioPoints];
        var live = 0.0;
        var liveKnown = true;
        var byTransfers = false;
        var leftOut = new List<string>();
        foreach (var (symbol, amount, price) in holdings)
        {
            if (!series.TryGetValue(symbol, out var prices) || prices.Count < 2)
            {
                leftOut.Add(symbol);
                continue;
            }

            var transfers = moves.TryGetValue(symbol, out var known) ? known : [];
            var amounts = BalanceHistory.AmountsOver(amount, transfers, start, end, PortfolioPoints);
            byTransfers |= BalanceHistory.AnyWithin(transfers, start, end);

            // The history and the live price come from two sources a moment apart. Setting only the last
            // point to the live figure drew a cliff at the right-hand edge whenever they differed by a
            // percent — on a quiet day, the largest thing on the chart. Within a few percent the whole
            // history is lined up with the live price instead; further apart they are not the same
            // market at all (a stale history), and the step is left to say so.
            var last = prices[^1];
            var anchor = price > 0 && last > 0 && Math.Abs((price / last) - 1) <= PriceAnchorTolerance ? price / last : 1.0;

            for (var i = 0; i < PortfolioPoints; i++) total[i] += amounts[i] * Resample(prices, i, PortfolioPoints) * anchor;
            if (amount <= 0) continue;
            if (price > 0) live += amount * price;
            else liveKnown = false;
        }
        if (liveKnown && live > 0) total[^1] = live;

        // The same line again (the network confirmed what was already drawn): leave it, and the pointer's
        // place on it, alone.
        if (leftOut.Count == 0 && PortfolioSeries.Count == total.Length && PortfolioSeries.SequenceEqual(total)) return;

        if (leftOut.Count == holdings.Count)
        {
            PortfolioSeries = [];
            PortfolioChartStatus = Loc.Instance["chart.noHistory"];
            PortfolioChartNote = string.Empty;
            PortfolioChartNoteShort = string.Empty;
            return;
        }

        PortfolioSeries = total;
        PortfolioChartStatus = string.Empty;
        // Which kind of line this is: worked back through the wallet's transfers, or (none of them in
        // this window) simply today's holdings at past prices.
        PortfolioChartNote = leftOut.Count > 0
            ? string.Format(Loc.Instance["chart.noteLeftOut"], string.Join(", ", leftOut))
            : Loc.Instance[byTransfers ? "chart.noteHistory" : "chart.note"];
        PortfolioChartNoteShort = leftOut.Count > 0
            ? string.Format(Loc.Instance["chart.noteShortLeftOut"], string.Join(", ", leftOut))
            : Loc.Instance[byTransfers ? "chart.noteHistoryShort" : "chart.noteShort"];
    }

    /// <summary>The value at point <paramref name="i"/> of <paramref name="count"/>, read linearly off a
    /// series of any length. The first and last points are the series' own first and last values.</summary>
    public static double Resample(IReadOnlyList<double> series, int i, int count)
    {
        if (series.Count == 1 || count < 2) return series[^1];
        var position = (double)i * (series.Count - 1) / (count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, series.Count - 1);
        var t = position - lower;
        return series[lower] + ((series[upper] - series[lower]) * t);
    }

    /// <summary>Holdings changed: redraw, reusing the price history already fetched.</summary>
    private string _portfolioChartSignature = string.Empty;

    /// <summary>Goes up whenever the transfers the wallet knows of change, so the chart follows them.</summary>
    private int _portfolioMovesVersion;

    private void PortfolioMovesChanged()
    {
        _portfolioMovesVersion++;
        SchedulePortfolioChart();
    }

    private void SchedulePortfolioChart()
    {
        var signature = string.Join(";", Accounts
            .Where(a => a.Amount > 0 && a.Balance != BalanceRead.Unknown)
            .Select(a => $"{a.Symbol}:{a.Amount:R}")
            .OrderBy(s => s, StringComparer.Ordinal)) + "|" + PortfolioRange + "|" + _portfolioMovesVersion;
        if (signature == _portfolioChartSignature) return;
        _portfolioChartSignature = signature;
        _ = RefreshPortfolioChartAsync();
    }

    // ================= the Market card =================

    /// <summary>Top (the market list's own order), Gainers or Losers.</summary>
    [ObservableProperty] private string _railMarketTab = "Top";

    public ObservableCollection<MarketRowViewModel> RailMarketRows { get; } = [];

    private const int RailMarketShown = 6;

    [RelayCommand]
    private void SetRailMarketTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        RailMarketTab = tab;
        RebuildRailMarket();
    }

    private void RebuildRailMarket()
    {
        // Snapshot — market refresh / watchlist toggles can mutate ObservableCollections while we build.
        var market = Market.ToArray();
        var gainers = MarketGainers.ToArray();
        var losers = MarketLosers.ToArray();
        var rows = RailMarketTab switch
        {
            "Gainers" => gainers,
            "Losers" => losers,
            _ => market.Where(m => m.HasPrice).Take(RailMarketShown).ToArray(),
        };

        RailMarketRows.Clear();
        foreach (var row in rows.Take(RailMarketShown)) RailMarketRows.Add(row);
    }

    // ================= theme swatches =================

    /// <summary>The four themes offered on the home screen — Phobia's own blue first; every other one is
    /// in Settings.</summary>
    private static readonly string[] SwatchThemes = ["phobia", "umbrella", "navy", "black"];

    public IReadOnlyList<ThemeSwatch> ThemeSwatches => SwatchThemes
        .Where(Theming.IsKnown)
        .Select(id =>
        {
            var p = Theming.PaletteOf(id)!;
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(p["UmAccentDim"]), 0),
                    new GradientStop(Color.Parse(p["UmBg"]), 0.75),
                },
            };
            var name = Theming.Themes.First(t => t.Id == id).Name;
            return new ThemeSwatch(id, name, fill, new SolidColorBrush(Color.Parse(p["UmAccentBright"])), id == ThemeId);
        })
        .ToList();

    [RelayCommand]
    private void SelectThemeSwatch(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        ThemeId = id;
        OnPropertyChanged(nameof(ThemeSwatches));
    }
}
