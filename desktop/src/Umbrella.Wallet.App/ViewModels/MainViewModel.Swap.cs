using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Amounts;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Swapping any coin the wallet holds for any coin it can receive, on whichever network each lives.
///
/// <para>Three routes, tried in order of trust (<see cref="SwapCatalog.RoutesFor"/>): THORChain, where
/// nobody holds the coins; NEAR Intents, where a contract does and refunds the payer if the swap cannot
/// be filled; and Exolix, an exchange that holds them for the minutes of the swap — used only for Monero,
/// Nano and Decred, which nothing decentralised reaches. The screen names the route and what it means
/// for the user's coins before anything is paid.</para>
///
/// <para>THORChain deposits from Bitcoin, Litecoin, Dogecoin, Bitcoin Cash and Ethereum are signed here,
/// memo included. Every other route ends in an ordinary payment — to a NEAR Intents deposit address, an
/// Exolix order or a THORChain Cosmos vault — which opens on the Send screen, filled in, for the same
/// review and password as any send.</para>
/// </summary>
public partial class MainViewModel
{
    private readonly ThorchainSwapClient _thorchain = new();
    private readonly NearIntentsSwapClient _intents = new();
    private readonly ExolixSwapClient _exolix = new();

    /// <summary>The THORChain quote behind a THORChain plan (kept for the in-wallet deposit).</summary>
    private SwapQuote? _swapQuote;

    /// <summary>What the review card shows: the route and its numbers.</summary>
    private SwapPlan? _swapPlan;

    private sealed record SwapPlan(
        SwapProvider Provider, SwapAsset From, SwapAsset To, decimal AmountIn, decimal ExpectedOut,
        string Destination, string Refund, SwapQuote? Thor, IntentsQuote? Intents, ExolixRate? Exolix);

    /// <summary>A swap payment handed to the Send screen and not sent yet.</summary>
    private sealed record PendingSwap(
        SwapProvider Provider, SwapAsset From, SwapAsset To, decimal AmountIn, decimal ExpectedOut,
        string DepositAddress, string? Memo, string? OrderId, string? TxId = null);

    private PendingSwap? _pendingSwap;
    private PendingSwap? _trackedSwap;

    /// <summary>The coins the open wallet can pay with. Starts as the whole catalog and is only ever
    /// narrowed in place, never emptied: a picker whose selection arrives before its items drops it, and
    /// the binding does not offer the same value twice — the pay-with picker came up blank.</summary>
    public ObservableCollection<SwapAsset> SwapFromOptions { get; } = new(SwapCatalog.All);

    /// <summary>The coins the paying coin can be swapped for, that this wallet has an address for.</summary>
    public ObservableCollection<SwapAsset> SwapToOptions { get; } = new(SwapCatalog.All);

    /// <summary>The paying coin's catalog key ("BTC", "USDT@TRON").</summary>
    [ObservableProperty] private string _swapFromSymbol = "BTC";

    /// <summary>The bought coin's catalog key.</summary>
    [ObservableProperty] private string _swapToSymbol = "ETH";

    [ObservableProperty] private string _swapAmount = string.Empty;
    [ObservableProperty] private bool _hasSwapQuote;
    [ObservableProperty] private bool _swapBusy;
    [ObservableProperty] private string _swapError = string.Empty;
    [ObservableProperty] private string _swapSuccess = string.Empty;
    [ObservableProperty] private string _swapExpectedOut = string.Empty;
    [ObservableProperty] private string _swapRateText = string.Empty;
    [ObservableProperty] private string _swapFeeText = string.Empty;
    [ObservableProperty] private string _swapEtaText = string.Empty;
    [ObservableProperty] private string _swapDestination = string.Empty;
    [ObservableProperty] private string _swapExpiryText = string.Empty;
    [ObservableProperty] private string _swapWarning = string.Empty;

    /// <summary>"THORChain — nobody holds your coins in between", or the route's own honest line.</summary>
    [ObservableProperty] private string _swapRouteText = string.Empty;

    /// <summary>The route holds the coins during the swap (Exolix): the review says so in a warning colour.</summary>
    [ObservableProperty] private bool _swapRouteCustodial;

    /// <summary>"Confirm & swap" when the wallet signs the deposit here; "Continue to payment" when the
    /// payment opens on the Send screen.</summary>
    [ObservableProperty] private string _swapConfirmLabel = string.Empty;

    /// <summary>The swap that was paid, while it is being followed.</summary>
    [ObservableProperty] private bool _hasSwapTrack;
    [ObservableProperty] private string _swapTrackTitle = string.Empty;
    [ObservableProperty] private string _swapTrackStatus = string.Empty;
    [ObservableProperty] private string _swapTrackUrl = string.Empty;

    /// <summary>The banner over the Send screen while it holds a swap payment.</summary>
    [ObservableProperty] private string _swapPaymentBanner = string.Empty;

    public bool HasSwapPayment =>
        _pendingSwap is not null && string.Equals(SendTo?.Trim(), _pendingSwap.DepositAddress, StringComparison.Ordinal);

    public SwapAsset? SwapFromAsset
    {
        get => SwapCatalog.Find(SwapFromSymbol);
        set
        {
            if (value is null) ReassertSwapSelection();   // the picker let go of it while its list was empty
            else if (!string.Equals(value.Key, SwapFromSymbol, StringComparison.OrdinalIgnoreCase)) SwapFromSymbol = value.Key;
        }
    }

    public SwapAsset? SwapToAsset
    {
        get => SwapCatalog.Find(SwapToSymbol);
        set
        {
            if (value is null) ReassertSwapSelection();
            else if (!string.Equals(value.Key, SwapToSymbol, StringComparison.OrdinalIgnoreCase)) SwapToSymbol = value.Key;
        }
    }

    /// <summary>
    /// A ComboBox whose list is empty when its selection arrives drops the selection and writes null back;
    /// re-announcing the choice once the list has its items puts it back. Posted, because the picker is
    /// still in the middle of that change when it writes the null.
    /// </summary>
    private void ReassertSwapSelection()
    {
        if (Avalonia.Application.Current is null) return;   // tests: no picker to reassert
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Only a choice the list can show: re-announcing one it cannot would bounce null back for ever.
            if (SwapFromAsset is { } from && SwapFromOptions.Contains(from)) OnPropertyChanged(nameof(SwapFromAsset));
            if (SwapToAsset is { } to && SwapToOptions.Contains(to)) OnPropertyChanged(nameof(SwapToAsset));
        });
    }

    partial void OnSwapFromSymbolChanged(string value)
    {
        OnPropertyChanged(nameof(SwapFromAsset));
        RebuildSwapToOptions();
        InvalidateSwap();
        OnPropertyChanged(nameof(SwapFromBalanceLabel));
        ScheduleSwapQuote();
    }

    partial void OnSwapToSymbolChanged(string value)
    {
        OnPropertyChanged(nameof(SwapToAsset));
        InvalidateSwap();
        ScheduleSwapQuote();
    }

    partial void OnSwapAmountChanged(string value) { InvalidateSwap(); ScheduleSwapQuote(); }

    /// <summary>"From wallet «Main wallet»" — the swap is paid from the open wallet, said on the screen.</summary>
    public string SwapWalletLabel => string.Format(Loc.Instance["swap.fromWallet"], ActiveWalletLabel);

    /// <summary>What the open wallet holds of the paying coin, and its value — or a dash when that balance
    /// has not been read (never a zero standing in for "unknown").</summary>
    public string SwapFromBalanceLabel
    {
        get
        {
            var account = SwapPayingAccount();
            if (account is null) return string.Empty;
            if (account.Balance == BalanceRead.Unknown) return string.Format(Loc.Instance["swap.balance"], $"— {account.Symbol}");
            var amount = ((decimal)account.Amount).ToString("#,0.########", Fx.Culture);
            var fiat = account.Price > 0 && account.Amount > 0 ? $" ≈ {Fx.Money(account.Amount * account.Price)}" : string.Empty;
            return string.Format(Loc.Instance["swap.balance"], $"{amount} {account.Symbol}{fiat}");
        }
    }

    private WalletAccountViewModel? SwapPayingAccount() =>
        SwapFromAsset is { } from && SwapSendKeyFor(from) is { } key ? AccountForSendKey(key) : null;

    /// <summary>Puts the whole balance of the paying coin into the amount (the network fee is still taken
    /// when it is sent, so the review says what is left).</summary>
    [RelayCommand]
    private void SwapUseBalance()
    {
        var account = SwapPayingAccount();
        if (account is null || account.Balance == BalanceRead.Unknown || account.Amount <= 0) return;
        SwapAmount = ((decimal)account.Amount).ToString("0.########", CultureInfo.InvariantCulture);
    }

    // --- Which coins ---------------------------------------------------------------------------------

    /// <summary>The Send screen's key for paying with <paramref name="asset"/>: fixed for a native coin;
    /// for a token, the Send picker's own entry for it (offered only while it is held).</summary>
    private string? SwapSendKeyFor(SwapAsset asset)
    {
        if (asset.SendKey is not null) return asset.SendKey;
        if (asset.Contract is null) return null;
        return SendableAssetOptions.FirstOrDefault(o =>
            o.Symbol.EndsWith(asset.Contract, StringComparison.OrdinalIgnoreCase))?.Symbol;
    }

    /// <summary>The wallet's own address for the account that receives (and pays, and is refunded)
    /// <paramref name="asset"/>; null when the open wallet has none.</summary>
    private string? SwapAddressFor(SwapAsset asset) =>
        Accounts.FirstOrDefault(a => string.Equals(a.Symbol, asset.AddressSymbol, StringComparison.OrdinalIgnoreCase)
                                     && a.Derivation != EvmSideDerivation && IsRealAddress(a.Address))?.Address;

    /// <summary>Whether the open wallet can pay with <paramref name="asset"/>: a send path for it and an
    /// account to send from.</summary>
    private bool CanPayWith(SwapAsset asset)
    {
        var key = SwapSendKeyFor(asset);
        if (key is null || SwapAddressFor(asset) is null) return false;
        if (asset.SendKey is null) return true;   // a token's key exists only while it can be sent
        return SendableSymbols.Contains(key) || key is "TRX" or "USDT" or "XMR";
    }

    /// <summary>Rebuilds both pickers from what the open wallet holds and can receive. Called whenever the
    /// sendable assets change, so a token that arrives becomes payable without reopening the screen.</summary>
    private void RebuildSwapOptions()
    {
        // Locked, nothing is payable; emptying the lists would only lose the pickers' selections.
        if (!IsUnlocked) return;
        SyncSwapList(SwapFromOptions, SwapCatalog.All.Where(CanPayWith).ToList());

        if (SwapFromAsset is null || !SwapFromOptions.Contains(SwapFromAsset))
        {
            var first = SwapFromOptions.FirstOrDefault();
            if (first is not null) SwapFromSymbol = first.Key;
        }
        RebuildSwapToOptions();
        OnPropertyChanged(nameof(SwapFromAsset));
        OnPropertyChanged(nameof(SwapFromBalanceLabel));
        ReassertSwapSelection();
    }

    /// <summary>Keeps "To" to the coins a route can deliver from the paying coin, and that this wallet
    /// has an address for. Fixes the selection when it stops being valid.</summary>
    private void RebuildSwapToOptions()
    {
        var from = SwapFromAsset;
        var wanted = from is null
            ? []
            : SwapCatalog.All.Where(a => SwapCatalog.CanSwap(from, a) && SwapAddressFor(a) is not null).ToList();
        SyncSwapList(SwapToOptions, wanted);
        if (SwapToAsset is null || !SwapToOptions.Contains(SwapToAsset))
        {
            var pick = SwapToOptions.FirstOrDefault(a => a.Key == "ETH" || a.Key == "BTC") ?? SwapToOptions.FirstOrDefault();
            SwapToSymbol = pick?.Key ?? string.Empty;
        }
        OnPropertyChanged(nameof(SwapToAsset));
    }

    /// <summary>
    /// Makes a picker list exactly <paramref name="wanted"/>, in order, without ever emptying it: a
    /// ComboBox whose list is cleared drops its selection, and the pay-with picker came up blank.
    /// </summary>
    private static void SyncSwapList(ObservableCollection<SwapAsset> target, System.Collections.Generic.IReadOnlyList<SwapAsset> wanted)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = target.IndexOf(wanted[i]);
            if (at < 0) target.Insert(i, wanted[i]);
            else if (at != i) target.Move(at, i);
        }
    }

    /// <summary>Swaps the two coins — the ⇅ button. Only when the bought coin can also pay.</summary>
    [RelayCommand]
    private void FlipSwap()
    {
        var newFrom = SwapToAsset;
        var newTo = SwapFromAsset;
        if (newFrom is null || newTo is null || !SwapFromOptions.Contains(newFrom)) return;
        SwapFromSymbol = newFrom.Key;   // rebuilds the To options…
        if (SwapToOptions.Contains(newTo)) SwapToSymbol = newTo.Key;   // …then pins To to the old From
    }

    private CancellationTokenSource? _swapQuoteDelay;

    /// <summary>Asks for a quote by itself shortly after the pair or amount stops changing.</summary>
    private void ScheduleSwapQuote()
    {
        _swapQuoteDelay?.Cancel();
        if (_unlockedMnemonic is null || !AmountInput.TryParsePositive(SwapAmount, out _)) return;
        var cts = _swapQuoteDelay = new CancellationTokenSource();
        _ = Task.Delay(700, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (cts.IsCancellationRequested || SwapBusy || HasSwapQuote) return;
                await GetSwapQuoteAsync();
            });
        }, TaskScheduler.Default);
    }

    private void InvalidateSwap()
    {
        HasSwapQuote = false;
        _swapQuote = null;
        _swapPlan = null;
        SwapSuccess = string.Empty;
        // The last quote's figures go with it: a pair nothing can carry must not keep showing what the
        // previous pair would have paid.
        SwapExpectedOut = string.Empty;
    }

    private static ChainId? SwapChainId(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTC" => ChainId.Btc,
        "LTC" => ChainId.Ltc,
        "ETH" => ChainId.Eth,
        "DOGE" => ChainId.Doge,
        "BCH" => ChainId.Bch,
        _ => null,
    };

    /// <summary>The route's name as the screen shows it.</summary>
    private static string SwapProviderName(SwapProvider provider) => provider switch
    {
        SwapProvider.Thorchain => "THORChain",
        SwapProvider.NearIntents => "NEAR Intents",
        _ => "Exolix",
    };

    /// <summary>Bitcoin Cash without its URI scheme — the form every swap service wants.</summary>
    private static string BareAddress(string address) =>
        address.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase) ? address["bitcoincash:".Length..] : address;

    // --- Quote ---------------------------------------------------------------------------------------

    /// <summary>Step 1: a live quote for the pair and amount, from the most trusted route that trades it.</summary>
    [RelayCommand]
    private async Task GetSwapQuoteAsync()
    {
        SwapError = string.Empty;
        SwapSuccess = string.Empty;
        SwapWarning = string.Empty;
        InvalidateSwap();

        if (_unlockedMnemonic is null) { SwapError = Loc.Instance["swap.err.unlock"]; return; }
        var from = SwapFromAsset;
        var to = SwapToAsset;
        if (from is null || to is null) { SwapError = Loc.Instance["swap.err.unsupported"]; return; }
        if (from.Key == to.Key) { SwapError = Loc.Instance["swap.err.same"]; return; }
        // Same reason as the send field: a comma decimal must not be read as a group separator.
        if (!AmountInput.TryParsePositive(SwapAmount, out var amount)) { SwapError = Loc.Instance["swap.err.amount"]; return; }

        var destination = SwapAddressFor(to);
        if (destination is null) { SwapError = string.Format(Loc.Instance["swap.err.cannotReceive"], to.Display); return; }
        var refund = SwapAddressFor(from);
        if (refund is null || !CanPayWith(from)) { SwapError = string.Format(Loc.Instance["swap.err.fromUnsupported"], from.Display); return; }

        var routes = SwapCatalog.RoutesFor(from, to);
        if (routes.Count == 0) { SwapError = string.Format(Loc.Instance["swap.notAvailable"], from.Display, to.Display); return; }

        SwapBusy = true;
        var reasons = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var route in routes)
            {
                var (plan, reason) = await QuoteRouteAsync(route, from, to, amount, destination, refund);
                if (plan is not null)
                {
                    ShowSwapPlan(plan);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(reason)) reasons.Add($"{SwapProviderName(route)}: {reason}");
            }
            SwapError = reasons.Count > 0 ? string.Join("\n", reasons) : Loc.Instance["swap.err.noQuote"];
        }
        finally
        {
            SwapBusy = false;
        }
    }

    private async Task<(SwapPlan? Plan, string? Reason)> QuoteRouteAsync(
        SwapProvider route, SwapAsset from, SwapAsset to, decimal amount, string destination, string refund)
    {
        switch (route)
        {
            case SwapProvider.Thorchain:
            {
                var (q, error) = await _thorchain.GetQuoteForAssetsAsync(from.Thor!, to.Thor!, from.Symbol, to.Symbol, amount, destination);
                return q is null
                    ? (null, error)
                    : (new SwapPlan(route, from, to, amount, q.ExpectedOut, destination, refund, q, null, null), null);
            }
            case SwapProvider.NearIntents:
            {
                var fromToken = await _intents.ResolveAsync(from.Intents!.Value.Blockchain, from.Intents.Value.Symbol);
                var toToken = await _intents.ResolveAsync(to.Intents!.Value.Blockchain, to.Intents.Value.Symbol);
                if (fromToken is null || toToken is null) return (null, Loc.Instance["swap.err.notListed"]);
                var (q, error) = await _intents.QuoteAsync(fromToken, toToken, amount, BareAddress(refund),
                    BareAddress(destination), dry: true, memoDeposit: from.Key == "XLM");
                return q is null
                    ? (null, error)
                    : (new SwapPlan(route, from, to, amount, q.AmountOut, destination, refund, null, q, null), null);
            }
            default:
            {
                var (rate, error) = await _exolix.RateAsync(from.Exolix!.Value.Coin, from.Exolix.Value.Network,
                    to.Exolix!.Value.Coin, to.Exolix.Value.Network, amount);
                if (rate is null) return (null, error);
                if (rate.MinAmount > 0 && amount < rate.MinAmount)
                    return (null, string.Format(Loc.Instance["swap.err.minimum"], Fmt(rate.MinAmount), from.Symbol));
                return (new SwapPlan(route, from, to, amount, rate.ToAmount, destination, refund, null, null, rate), null);
            }
        }
    }

    private void ShowSwapPlan(SwapPlan plan)
    {
        _swapPlan = plan;
        _swapQuote = plan.Thor;
        var (from, to) = (plan.From, plan.To);

        SwapExpectedOut = $"{Fmt(plan.ExpectedOut)} {to.Symbol}";
        SwapRateText = $"1 {from.Symbol} ≈ {Fmt(plan.ExpectedOut / plan.AmountIn)} {to.Symbol}";
        SwapDestination = plan.Destination;
        SwapRouteCustodial = plan.Provider == SwapProvider.Exolix;
        SwapRouteText = Loc.Instance[plan.Provider switch
        {
            SwapProvider.Thorchain => "swap.via.thor",
            SwapProvider.NearIntents => "swap.via.intents",
            _ => "swap.via.exolix",
        }];
        SwapConfirmLabel = plan.Provider == SwapProvider.Thorchain && SwapChainId(from.Key) is not null
            ? Loc.Instance["swap.confirmBtn"]
            : Loc.Instance["swap.continueToSend"];
        SwapExpiryText = string.Empty;
        SwapWarning = string.Empty;

        switch (plan.Provider)
        {
            case SwapProvider.Thorchain:
            {
                var quote = plan.Thor!;
                var toPrice = PriceOf(to.Symbol);
                var feeFiat = toPrice > 0 ? $" ≈ {Fx.Money((double)quote.TotalFee * toPrice)}" : string.Empty;
                SwapFeeText = $"{Fmt(quote.TotalFee)} {to.Symbol}{feeFiat} · {quote.TotalBps / 100.0:0.##}%";
                SwapEtaText = EtaText(quote.EtaSeconds);
                var mins = Math.Max(0, (int)(quote.Expiry - DateTimeOffset.UtcNow).TotalMinutes);
                SwapExpiryText = string.Format(Loc.Instance["swap.quoteValid"], mins, Loc.Instance["unit.min"]);
                if (quote.BelowMinimum)
                    SwapWarning = string.Format(Loc.Instance["swap.belowMin"], Fmt(quote.RecommendedMinIn), from.Symbol);
                break;
            }
            case SwapProvider.NearIntents:
            {
                var q = plan.Intents!;
                var cost = q.AmountInUsd > 0 && q.AmountOutUsd > 0 ? q.AmountInUsd - q.AmountOutUsd : MarketCost(plan);
                SwapFeeText = cost is { } c && c >= 0
                    ? string.Format(Loc.Instance["swap.fee.intents"], Fx.Money((double)c))
                    : Loc.Instance["swap.fee.inRate"];
                SwapEtaText = EtaText(q.TimeEstimateSeconds);
                break;
            }
            default:
            {
                SwapFeeText = MarketCost(plan) is { } c && c >= 0
                    ? string.Format(Loc.Instance["swap.fee.rate"], Fx.Money((double)c))
                    : Loc.Instance["swap.fee.inRate"];
                SwapEtaText = Loc.Instance["swap.eta.exchange"];
                break;
            }
        }

        HasSwapQuote = true;
    }

    private double PriceOf(string symbol) =>
        Market.FirstOrDefault(m => string.Equals(m.Symbol, symbol, StringComparison.OrdinalIgnoreCase))?.Price ?? 0;

    /// <summary>What the swap costs at market prices: the value paid minus the value received; null when
    /// either price is unknown.</summary>
    private decimal? MarketCost(SwapPlan plan)
    {
        var inPrice = PriceOf(plan.From.Symbol);
        var outPrice = PriceOf(plan.To.Symbol);
        if (inPrice <= 0 || outPrice <= 0) return null;
        return plan.AmountIn * (decimal)inPrice - plan.ExpectedOut * (decimal)outPrice;
    }

    private static string EtaText(int seconds) => seconds <= 0
        ? Loc.Instance["swap.eta.unknown"]
        : seconds >= 60
            ? $"~{(seconds + 59) / 60} {Loc.Instance["unit.min"]}"
            : $"~{seconds} {Loc.Instance["unit.sec"]}";

    // --- Confirm -------------------------------------------------------------------------------------

    /// <summary>Step 2: THORChain from a coin this wallet deposits itself is signed and sent here; every
    /// other route makes its deposit address and opens the payment on the Send screen.</summary>
    [RelayCommand]
    private async Task ConfirmSwapAsync()
    {
        if (_unlockedMnemonic is null || _swapPlan is null) { SwapError = Loc.Instance["swap.err.quoteFirst"]; return; }
        var plan = _swapPlan;
        SwapError = string.Empty;

        switch (plan.Provider)
        {
            case SwapProvider.Thorchain when SwapChainId(plan.From.Key) is not null:
                await ConfirmThorInWalletAsync(plan);
                break;
            case SwapProvider.Thorchain:
                await ConfirmThorViaSendAsync(plan);
                break;
            case SwapProvider.NearIntents:
                await ConfirmIntentsAsync(plan);
                break;
            default:
                await ConfirmExolixAsync(plan);
                break;
        }
    }

    /// <summary>A fresh THORChain quote; null (with the reason on screen) when the vault answered
    /// differently, expired, or moved more than 3% against the user since they looked.</summary>
    private async Task<SwapQuote?> FreshThorQuoteAsync(SwapPlan plan)
    {
        var (fresh, error) = await _thorchain.GetQuoteForAssetsAsync(
            plan.From.Thor!, plan.To.Thor!, plan.From.Symbol, plan.To.Symbol, plan.AmountIn, plan.Destination);
        if (fresh is null) { SwapError = error ?? Loc.Instance["swap.err.refresh"]; return null; }
        if (fresh.IsExpired) { SwapError = Loc.Instance["swap.err.expired"]; return null; }
        if (fresh.ExpectedOut < plan.ExpectedOut * 0.97m)
        {
            ShowSwapPlan(plan with { ExpectedOut = fresh.ExpectedOut, Thor = fresh });
            SwapError = Loc.Instance["swap.err.moved"];
            StatusMessage = Loc.Instance["status.swapRateChanged"];
            return null;
        }
        return fresh;
    }

    /// <summary>THORChain from BTC/LTC/DOGE/BCH (OP_RETURN memo) or ETH (router calldata), signed here.</summary>
    private async Task ConfirmThorInWalletAsync(SwapPlan plan)
    {
        var from = plan.From.Symbol;
        var to = plan.To.Symbol;
        var fromChain = SwapChainId(plan.From.Key)!.Value;

        await RunBusyAsync(async () =>
        {
            StatusMessage = Loc.Instance["status.refreshingQuote"];
            // A fresh quote immediately before sending: vaults rotate and quotes expire, so a stale inbound
            // address or memo would send the deposit into the void.
            var fresh = await FreshThorQuoteAsync(plan);
            if (fresh is null) return;

            StatusMessage = Loc.Instance["status.signingSwap"];
            var fromAddr = _deriver.DeriveReceiveAddress(_unlockedMnemonic!, fromChain).Address;
            var walletId = _registry.Active?.Id ?? "default";

            bool ok;
            string? txid, sendErr;

            if (fromChain == ChainId.Eth)
            {
                // ETH-from: a router.depositWithExpiry call carrying the ETH as value and the swap memo as
                // calldata. THORChain gives the router and the inbound vault in the quote.
                if (string.IsNullOrWhiteSpace(fresh.Router)) { SwapError = Loc.Instance["swap.err.ethBuild"]; return; }
                var expiry = fresh.ExpiryUnix > 0
                    ? new System.Numerics.BigInteger(fresh.ExpiryUnix)
                    : new System.Numerics.BigInteger(DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds());
                var (eq, eErr) = await _ethSender.PrepareSwapAsync(
                    fromAddr, fresh.Router!, fresh.InboundAddress, plan.AmountIn, fresh.Memo, expiry);
                if (eq is null) { SwapError = eErr ?? Loc.Instance["swap.err.ethBuild"]; return; }

                var priv = _deriver.DeriveEthereumPrivateKey(_unlockedMnemonic!);
                try
                {
                    var res = await _ethSender.SignAndBroadcastSwapAsync(eq, priv);
                    // An unclear answer leaves the swap unconfirmed rather than failed: the deposit may be in
                    // the mempool, and sending it again would pay the vault twice.
                    (ok, txid, sendErr) = (res.Ok, res.TxHash, res.Unclear
                        ? $"{res.Error} The swap was not marked failed: check the transaction before trying again."
                        : res.Error);
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
                }
            }
            else
            {
                // Same multisource HD path as a normal send: UTXOs from every owned address, each signed with
                // its own key. The THORChain memo rides as an OP_RETURN in the same transaction.
                if (!_utxoScans.TryGetValue(from, out var scan) || scan is null)
                {
                    var fl = _addrIndex.FloorsFor(walletId, from);
                    scan = await _utxoScanner.ScanAsync(_unlockedMnemonic!, fromChain, UtxoExplorerFor(from), fl);
                    if (!scan.Partial) _utxoScans[from] = scan;
                }
                if (scan.Partial) { SwapError = Loc.Instance["swap.err.syncing"]; return; }

                // THORChain returns the BCH vault as a bare CashAddr; NBitcoin's BCash parser needs the prefix.
                var inbound = fresh.InboundAddress;
                if (fromChain == ChainId.Bch && !inbound.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase))
                    inbound = "bitcoincash:" + inbound;

                var (quote, btcPlan, request, prepErr) = await _btcSender.PrepareHdAsync(
                    from, scan.Utxos, fromAddr, inbound, plan.AmountIn, memo: fresh.Memo);
                if (quote is null || btcPlan is null || request is null) { SwapError = prepErr ?? Loc.Instance["swap.err.deposit"]; return; }

                bool depositUnclear;
                (ok, txid, sendErr, depositUnclear) = await _btcSender.SignAndBroadcastHdAsync(
                    _unlockedMnemonic!, walletId, _addrIndex, from, btcPlan, request);
                if (depositUnclear) sendErr = $"{sendErr} The swap was not marked failed: check the deposit before trying again.";
                _utxoScans.Remove(from);
            }

            if (ok && txid is not null)
            {
                var track = ThorchainSwapClient.TrackUrl(txid);
                SwapSuccess = string.Format(Loc.Instance["swap.sent"], txid, Fmt(fresh.ExpectedOut), to, track);
                StatusMessage = Loc.Instance["status.swapBroadcast"];
                InvalidateSwap();
                SwapAmount = string.Empty;
                PushActivity("Swap", $"{from}→{to}", $"-{Fmt(plan.AmountIn)}", Shorten(fresh.InboundAddress), "now", track);
                TrackSwap(new PendingSwap(SwapProvider.Thorchain, plan.From, plan.To, plan.AmountIn, fresh.ExpectedOut,
                    fresh.InboundAddress, fresh.Memo, null, txid));
                await RefreshLiveDataAsync();
            }
            else
            {
                SwapError = sendErr ?? Loc.Instance["swap.err.broadcast"];
                StatusMessage = Loc.Instance["status.swapFailed"];
            }
        });
    }

    /// <summary>THORChain from the Cosmos Hub: a send to the vault with the swap memo as the transaction
    /// memo, reviewed on the Send screen.</summary>
    private async Task ConfirmThorViaSendAsync(SwapPlan plan)
    {
        SwapBusy = true;
        try
        {
            var fresh = await FreshThorQuoteAsync(plan);
            if (fresh is null) return;
            await OpenSwapPaymentAsync(new PendingSwap(SwapProvider.Thorchain, plan.From, plan.To, plan.AmountIn,
                fresh.ExpectedOut, fresh.InboundAddress, fresh.Memo, null));
        }
        finally
        {
            SwapBusy = false;
        }
    }

    /// <summary>NEAR Intents: the real quote, with its one-time deposit address, then the payment.</summary>
    private async Task ConfirmIntentsAsync(SwapPlan plan)
    {
        SwapBusy = true;
        try
        {
            var fromToken = await _intents.ResolveAsync(plan.From.Intents!.Value.Blockchain, plan.From.Intents.Value.Symbol);
            var toToken = await _intents.ResolveAsync(plan.To.Intents!.Value.Blockchain, plan.To.Intents.Value.Symbol);
            if (fromToken is null || toToken is null) { SwapError = Loc.Instance["swap.err.notListed"]; return; }

            var (q, error) = await _intents.QuoteAsync(fromToken, toToken, plan.AmountIn, BareAddress(plan.Refund),
                BareAddress(plan.Destination), dry: false, memoDeposit: plan.From.Key == "XLM");
            if (q?.DepositAddress is null) { SwapError = error ?? Loc.Instance["swap.err.noQuote"]; return; }
            if (q.AmountOut < plan.ExpectedOut * 0.97m)
            {
                ShowSwapPlan(plan with { ExpectedOut = q.AmountOut, Intents = q });
                SwapError = Loc.Instance["swap.err.moved"];
                return;
            }

            await OpenSwapPaymentAsync(new PendingSwap(SwapProvider.NearIntents, plan.From, plan.To, plan.AmountIn,
                q.AmountOut, q.DepositAddress, q.DepositMemo, null));
        }
        finally
        {
            SwapBusy = false;
        }
    }

    /// <summary>Exolix: the order (created only now, on confirm), then the payment.</summary>
    private async Task ConfirmExolixAsync(SwapPlan plan)
    {
        SwapBusy = true;
        try
        {
            var (order, error) = await _exolix.CreateAsync(
                plan.From.Exolix!.Value.Coin, plan.From.Exolix.Value.Network,
                plan.To.Exolix!.Value.Coin, plan.To.Exolix.Value.Network,
                plan.AmountIn, BareAddress(plan.Destination), BareAddress(plan.Refund));
            if (order is null) { SwapError = error ?? Loc.Instance["swap.err.noQuote"]; return; }

            await OpenSwapPaymentAsync(new PendingSwap(SwapProvider.Exolix, plan.From, plan.To,
                order.Amount > 0 ? order.Amount : plan.AmountIn,
                order.AmountTo > 0 ? order.AmountTo : plan.ExpectedOut,
                order.DepositAddress, order.DepositExtraId, order.Id));
        }
        finally
        {
            SwapBusy = false;
        }
    }

    /// <summary>
    /// Puts a swap payment on the Send screen — coin, deposit address, amount and memo filled in — and
    /// opens its review. Nothing is signed until the user confirms there, with the same checks and
    /// password as any send.
    /// </summary>
    private async Task OpenSwapPaymentAsync(PendingSwap payment)
    {
        var key = SwapSendKeyFor(payment.From);
        if (key is null) { SwapError = string.Format(Loc.Instance["swap.err.fromUnsupported"], payment.From.Display); return; }

        _pendingSwap = payment;
        SwapPaymentBanner = string.Format(Loc.Instance["swap.payBanner"], SwapProviderName(payment.Provider),
            $"{Fmt(payment.ExpectedOut)} {payment.To.Symbol}", payment.To.Network);

        var option = SendableAssetOptions.FirstOrDefault(o => o.Symbol.Equals(key, StringComparison.OrdinalIgnoreCase));
        _choosingSendAsset = true;
        try
        {
            if (option is not null) SelectedSendAsset = option;
            else SendChain = key;
        }
        finally
        {
            _choosingSendAsset = false;
        }

        SendTo = payment.DepositAddress;
        SendAmount = payment.AmountIn.ToString("0.##################", CultureInfo.InvariantCulture);
        SendMemo = payment.Memo ?? string.Empty;
        OnPropertyChanged(nameof(HasSwapPayment));
        ActiveSection = "Send";
        await PrepareSendCommand.ExecuteAsync(null);
    }

    /// <summary>Called when a send is broadcast (or may have been): if it paid the pending swap, the swap
    /// is now under way — logged, told to the service, and followed.</summary>
    private void OnSwapPaymentSent(string? txid, string to)
    {
        var payment = _pendingSwap;
        if (payment is null || !string.Equals(to.Trim(), payment.DepositAddress, StringComparison.Ordinal)) return;
        _pendingSwap = null;
        OnPropertyChanged(nameof(HasSwapPayment));

        if (payment.Provider == SwapProvider.NearIntents && txid is not null)
            _ = _intents.SubmitDepositAsync(txid, payment.DepositAddress, payment.Memo);

        var track = SwapTrackLink(payment with { TxId = txid });
        PushActivity("Swap", $"{payment.From.Symbol}→{payment.To.Symbol}", $"-{Fmt(payment.AmountIn)}",
            SwapProviderName(payment.Provider), "now", track);
        TrackSwap(payment with { TxId = txid });
        InvalidateSwap();
        SwapAmount = string.Empty;
    }

    private static string? SwapTrackLink(PendingSwap swap) => swap.Provider switch
    {
        SwapProvider.Exolix when swap.OrderId is not null => ExolixSwapClient.TrackUrl(swap.OrderId),
        SwapProvider.Thorchain when swap.TxId is not null => ThorchainSwapClient.TrackUrl(swap.TxId),
        _ => null,
    };

    // --- Following a swap ----------------------------------------------------------------------------

    private CancellationTokenSource? _swapTrackCts;

    private void TrackSwap(PendingSwap swap)
    {
        _trackedSwap = swap;
        SwapTrackTitle = $"{swap.From.Display} → {swap.To.Display} · ≈ {Fmt(swap.ExpectedOut)} {swap.To.Symbol} · {SwapProviderName(swap.Provider)}";
        SwapTrackStatus = Loc.Instance["swap.st.sent"];
        SwapTrackUrl = SwapTrackLink(swap) ?? string.Empty;
        HasSwapTrack = true;

        _swapTrackCts?.Cancel();
        var cts = _swapTrackCts = new CancellationTokenSource();
        _ = FollowSwapAsync(cts.Token);
    }

    /// <summary>Asks for the swap's state every half minute until it ends (or two hours pass).</summary>
    private async Task FollowSwapAsync(CancellationToken ct)
    {
        for (var i = 0; i < 240 && !ct.IsCancellationRequested; i++)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(i == 0 ? 15 : 30), ct); }
            catch (OperationCanceledException) { return; }
            if (await RefreshSwapStatusCoreAsync()) return;
        }
    }

    [RelayCommand]
    private async Task RefreshSwapStatusAsync() => await RefreshSwapStatusCoreAsync();

    /// <summary>Reads the swap's state; true when it has ended (done, refunded or failed).</summary>
    private async Task<bool> RefreshSwapStatusCoreAsync()
    {
        var swap = _trackedSwap;
        if (swap is null) return true;
        var raw = swap.Provider switch
        {
            SwapProvider.NearIntents => await _intents.StatusAsync(swap.DepositAddress, swap.Memo),
            SwapProvider.Exolix when swap.OrderId is not null => await _exolix.StatusAsync(swap.OrderId),
            _ => null,
        };
        if (raw is null || !ReferenceEquals(swap, _trackedSwap)) return swap.Provider == SwapProvider.Thorchain;

        var (key, ended) = SwapStatusKey(raw);
        SwapTrackStatus = Loc.Instance[key];
        if (ended && key == "swap.st.done") await RefreshLiveDataAsync();
        return ended;
    }

    /// <summary>A route's own state word mapped onto the screen's few, and whether it is final.</summary>
    public static (string Key, bool Ended) SwapStatusKey(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "PENDING_DEPOSIT" or "WAIT" => ("swap.st.waiting", false),
        "KNOWN_DEPOSIT_TX" or "CONFIRMATION" or "CONFIRMED" => ("swap.st.seen", false),
        "PROCESSING" or "EXCHANGING" or "SENDING" => ("swap.st.processing", false),
        "INCOMPLETE_DEPOSIT" => ("swap.st.incomplete", false),
        "SUCCESS" => ("swap.st.done", true),
        "REFUNDED" => ("swap.st.refunded", true),
        "FAILED" or "OVERDUE" or "DELETED" or "ERROR" => ("swap.st.failed", true),
        _ => ("swap.st.processing", false),
    };

    [RelayCommand]
    private void DismissSwapTrack()
    {
        _swapTrackCts?.Cancel();
        _trackedSwap = null;
        HasSwapTrack = false;
    }

    /// <summary>A lock ends the swap's screen state: the payment and the following belong to this wallet.</summary>
    private void ForgetSwapState()
    {
        _swapTrackCts?.Cancel();
        _pendingSwap = null;
        _trackedSwap = null;
        HasSwapTrack = false;
        InvalidateSwap();
    }

    [RelayCommand]
    private void CancelSwap()
    {
        InvalidateSwap();
        SwapError = string.Empty;
        SwapWarning = string.Empty;
        StatusMessage = Loc.Instance["status.swapCancelled"];
    }
}
