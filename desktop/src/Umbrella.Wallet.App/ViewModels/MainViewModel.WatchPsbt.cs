using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NBitcoin;
using Umbrella.Wallet.Core.Amounts;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Psbt;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>A watched account a PSBT can be made from (Bitcoin and Litecoin, the PSBT chains).</summary>
public sealed record WatchPsbtChoice(string Key, string Label);

/// <summary>
/// Paying from a watch-only wallet (roadmap H.5): the payment is made here as an UNSIGNED PSBT — from
/// the coins this wallet's own scan found, with change to the account's next unused change address —
/// and signed in the wallet that holds the keys. Nothing here signs.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<WatchPsbtChoice> WatchPsbtAccounts { get; } = [];

    [ObservableProperty] private WatchPsbtChoice? _watchPsbtAccount;
    [ObservableProperty] private string _watchPsbtTo = string.Empty;
    [ObservableProperty] private string _watchPsbtAmount = string.Empty;
    [ObservableProperty] private string _watchPsbtText = string.Empty;
    [ObservableProperty] private string _watchPsbtSummary = string.Empty;
    [ObservableProperty] private string _watchPsbtError = string.Empty;

    /// <summary>True when the PSBT names no key origin, so a hardware wallet may not recognise it.</summary>
    [ObservableProperty] private bool _watchPsbtNoOrigin;

    private PSBT? _watchPsbt;

    public bool CanMakeWatchPsbt => IsWatchWallet && WatchPsbtAccounts.Count > 0;

    public bool HasWatchPsbt => WatchPsbtText.Length > 0;

    partial void OnWatchPsbtTextChanged(string value) => OnPropertyChanged(nameof(HasWatchPsbt));

    partial void OnWatchPsbtAccountChanged(WatchPsbtChoice? value) => ClearWatchPsbt(keepForm: true);

    /// <summary>The PSBT chains among the watched accounts.</summary>
    private void RebuildWatchPsbtAccounts()
    {
        WatchPsbtAccounts.Clear();
        foreach (var (key, source) in _watchSources)
        {
            if (source.Chain is not (ChainId.Btc or ChainId.Ltc)) continue;
            WatchPsbtAccounts.Add(new WatchPsbtChoice(key,
                $"{SymbolFor(source.Chain)} · {WatchScriptLabel(source.Watched.Script)} · {Shorten(source.Watched.ExtPubKey)}"));
        }

        WatchPsbtAccount = WatchPsbtAccounts.FirstOrDefault();
        OnPropertyChanged(nameof(CanMakeWatchPsbt));
    }

    private void ClearWatchPsbt(bool keepForm = false)
    {
        _watchPsbt = null;
        WatchPsbtText = string.Empty;
        WatchPsbtSummary = string.Empty;
        WatchPsbtError = string.Empty;
        WatchPsbtNoOrigin = false;
        if (keepForm) return;
        WatchPsbtTo = string.Empty;
        WatchPsbtAmount = string.Empty;
    }

    [RelayCommand]
    private async Task MakeWatchPsbtAsync()
    {
        ClearWatchPsbt(keepForm: true);
        if (WatchPsbtAccount is not { } choice || !_watchSources.TryGetValue(choice.Key, out var source)) return;

        if (!_watchScans.TryGetValue(choice.Key, out var scan) || scan.Partial)
        {
            WatchPsbtError = Loc.Instance["watch.psbt.notRead"];
            return;
        }

        if (!AmountInput.TryParsePositive(WatchPsbtAmount, out var amount))
        {
            WatchPsbtError = Loc.Instance["watch.psbt.amount"];
            return;
        }

        var symbol = SymbolFor(source.Chain);
        double rate;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            rate = await BitcoinTransactionSender.FeeRateAsync(symbol, SelectedFeeLevel, cts.Token);
        }
        catch
        {
            WatchPsbtError = Loc.Instance["watch.psbt.noFee"];
            return;
        }

        var change = scan.HighestUsedInternalIndex is { } used ? used + 1 : 0u;
        var result = WatchPsbt.Build(source, scan.Utxos, WatchPsbtTo, (long)decimal.Round(amount * 100_000_000m), rate, change);
        if (result.Psbt is null)
        {
            WatchPsbtError = result.Error ?? Loc.Instance["send.errPrepareFailed"];
            return;
        }

        _watchPsbt = result.Psbt;
        WatchPsbtText = result.Psbt.ToBase64();
        WatchPsbtNoOrigin = WatchPsbt.Origin(source.Watched) is null;
        WatchPsbtSummary = string.Format(Loc.Instance["watch.psbt.summary"],
            $"{Fmt(amount)} {symbol}", $"{Fmt(result.FeeSat / 100_000_000m)} {symbol}", $"{rate:0.#}",
            $"{Fmt(result.ChangeSat / 100_000_000m)} {symbol}");
    }

    [RelayCommand]
    private async Task CopyWatchPsbtAsync()
    {
        if (HasWatchPsbt) await CopyTextAsync(WatchPsbtText);
    }

    [RelayCommand]
    private async Task SaveWatchPsbtAsync()
    {
        if (_watchPsbt is not null) await SavePsbtAsync(_watchPsbt, "phobia-watch-payment.psbt");
    }
}
