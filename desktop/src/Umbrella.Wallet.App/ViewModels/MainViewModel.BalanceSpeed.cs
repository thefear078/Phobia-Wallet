using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The balance keeping up with what the user has just done.
///
/// A send used to change nothing on screen until every chain had been read again — the better part of a
/// minute over Tor — and money arriving was noticed at the next two-minute refresh. Neither needed the
/// whole wallet read: what left is known the moment the network accepts it, and what arrives lands on
/// the one address the Receive screen is showing.
///
/// Nothing here invents a balance. The amount shown after a send is the last amount READ less what the
/// wallet itself has just sent, until an explorer's own figure has caught up; and a read that fails
/// leaves the row exactly as it was.
/// </summary>
public partial class MainViewModel
{
    /// <summary>What a send took out of a row, held until an explorer's reading shows it gone.</summary>
    private sealed record PendingDebit(double Before, double Debit, DateTimeOffset Until);

    private readonly Dictionary<string, PendingDebit> _pendingDebits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a debit is held when no explorer ever shows it: after this, the reading wins.</summary>
    private static readonly TimeSpan PendingDebitLife = TimeSpan.FromMinutes(5);

    /// <summary>When the one row a send came from is read again, after the send.</summary>
    private static readonly TimeSpan[] FollowBalanceDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(90)];

    /// <summary>How often the address on the Receive screen is read while that screen is open.</summary>
    private static readonly TimeSpan ReceiveWatchEvery = TimeSpan.FromSeconds(15);

    /// <summary>The network fee of the send under review, when it is paid in the coin being sent.</summary>
    private decimal _reviewFee;
    private string _reviewFeeSymbol = string.Empty;

    private static string RowKey(WalletAccountViewModel row) => $"{row.Symbol}|{row.Address}|{row.Chain}|{row.Derivation}";

    /// <summary>A row read by itself with one balance call: a coin at a single address of its own.</summary>
    private bool CanReadAlone(WalletAccountViewModel row) =>
        row.SupportStatus is "Ready" or "Receive only" && row.Derivation != EvmSideDerivation &&
        ParseChain(row.Symbol) is not null && IsRealAddress(row.Address) &&
        !UtxoScanChains.Contains(row.Symbol, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The network has taken a send: the row it came from shows it gone at once — the amount, and the
    /// fee when that is paid in the same coin — instead of after the next full refresh.
    /// </summary>
    private void ShowDebitNow(string? sendKey, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(sendKey) || amount <= 0) return;
        var row = AccountForSendKey(sendKey);
        if (row is null || row.Balance == BalanceRead.Unknown) return;
        var at = IndexOfAccount(row);
        if (at < 0) return;

        var fee = string.Equals(_reviewFeeSymbol, row.Symbol, StringComparison.OrdinalIgnoreCase) ? _reviewFee : 0m;
        var debit = (double)(amount + fee);
        // A second send before the first has been seen: both are still owed to the reading.
        var owed = _pendingDebits.TryGetValue(RowKey(row), out var earlier) && earlier.Until > DateTimeOffset.UtcNow
            ? earlier
            : null;
        _pendingDebits[RowKey(row)] = new PendingDebit(
            owed?.Before ?? row.Amount, (owed?.Debit ?? 0) + debit, DateTimeOffset.UtcNow + PendingDebitLife);

        Accounts[at] = row with { Amount = Math.Max(0, row.Amount - debit) };
        RefreshHoldings();
        RecalcBalance();
    }

    /// <summary>
    /// A fresh reading, less whatever this wallet has sent that the reading does not show yet. Once the
    /// reading has dropped by itself — the explorer has seen the send — the debit is forgotten.
    /// </summary>
    private double WithPendingDebit(WalletAccountViewModel row, double read)
    {
        var key = RowKey(row);
        if (!_pendingDebits.TryGetValue(key, out var pending)) return read;
        var (shown, caughtUp) = BalanceReadout.LessUnseenDebit(read, pending.Before, pending.Debit);
        if (pending.Until <= DateTimeOffset.UtcNow || caughtUp)
        {
            _pendingDebits.Remove(key);
            return read;
        }

        return shown;
    }

    /// <summary>Reads one row's balance again, by itself. A read that fails changes nothing.</summary>
    private async Task<bool> RefreshOneBalanceAsync(WalletAccountViewModel target, CancellationToken ct)
    {
        if (!CanReadAlone(target)) return false;
        ChainBalance? read;
        try { read = await _balances.GetBalanceAsync(ParseChain(target.Symbol)!.Value, target.Address, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch { read = null; }
        if (read is null || ct.IsCancellationRequested) return false;

        var at = IndexOfAccount(target);
        if (at < 0) return false;
        var current = Accounts[at];
        var amount = WithPendingDebit(current, (double)read.NativeAmount);
        if (amount == current.Amount && current.Balance == BalanceRead.Live) return true;

        Accounts[at] = current with { Amount = amount, Balance = BalanceRead.Live };
        RefreshHoldings();
        RecalcBalance();
        SaveBalanceCache();
        return true;
    }

    /// <summary>
    /// After a send: the row it came from is read again at short intervals, by itself, until the
    /// explorer shows the send — rather than the whole wallet being read again and again.
    /// </summary>
    private async Task FollowBalanceAsync(string? sendKey)
    {
        var epoch = _lockEpoch;
        var row = string.IsNullOrWhiteSpace(sendKey) ? null : AccountForSendKey(sendKey);
        if (row is null) return;
        var key = RowKey(row);
        var alone = CanReadAlone(row);

        foreach (var wait in FollowBalanceDelays)
        {
            try { await Task.Delay(wait); } catch { return; }
            if (epoch != _lockEpoch || !IsUnlocked) return;
            if (!CanAskTheNetwork) continue;

            if (alone) await RefreshOneBalanceAsync(row, CancellationToken.None);
            else if (!IsRefreshing && wait >= TimeSpan.FromSeconds(12)) await RefreshLiveDataAsync();   // tokens, rollups, the scanned chains

            if (!_pendingDebits.ContainsKey(key)) return;   // the explorer has caught up
        }
    }

    /// <summary>False when nothing can leave the device at all (the kill-switch with no Tor up yet).</summary>
    private static bool CanAskTheNetwork =>
        !(PublicHttp.RequireProxy && string.IsNullOrEmpty(PublicHttp.ActiveProxy));

    // ---- money arriving ------------------------------------------------------------------------------

    private CancellationTokenSource? _receiveWatchCts;

    /// <summary>
    /// While the Receive screen is open, the address it shows is read every few seconds — one call, to
    /// the server that address's balance already comes from — so money arriving is seen as it arrives.
    /// </summary>
    private void WatchReceiveAddress(bool watching)
    {
        _receiveWatchCts?.Cancel();
        _receiveWatchCts = null;
        if (!watching || !IsUnlocked) return;

        var cts = _receiveWatchCts = new CancellationTokenSource();
        _ = WatchReceiveAddressAsync(cts.Token);
    }

    private async Task WatchReceiveAddressAsync(CancellationToken ct)
    {
        var epoch = _lockEpoch;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(ReceiveWatchEvery, ct); } catch { return; }
            if (epoch != _lockEpoch || !IsUnlocked || !IsReceive) return;
            if (!CanAskTheNetwork || IsRefreshing) continue;

            var row = Accounts.FirstOrDefault(a =>
                a.Symbol == SelectedReceiveSymbol &&
                string.Equals(a.Address, SelectedReceiveAddress, StringComparison.OrdinalIgnoreCase) && CanReadAlone(a));
            if (row is null) continue;

            var before = row.Amount;
            var known = row.Balance != BalanceRead.Unknown;
            if (!await RefreshOneBalanceAsync(row, ct) || ct.IsCancellationRequested) continue;

            var at = IndexOfAccount(row);
            if (at < 0 || !known) continue;
            var arrived = Accounts[at].Amount - before;
            if (arrived <= 0) continue;

            ShowToast(string.Format(Loc.Instance["receive.arrived"], Fmt((decimal)arrived), row.Symbol), isError: false);
            if (!_isTonWallet && !_isMoneroWallet) _ = LoadOnChainHistoryAsync();   // and its row in Activity
        }
    }
}
