using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Amounts;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>How a send ended, as the receipt shows it.</summary>
public enum SendReceiptState
{
    /// <summary>A confirmed block holds it — the sender followed it there.</summary>
    Confirmed,
    /// <summary>The network accepted it; a block has not been seen yet.</summary>
    Broadcast,
    /// <summary>No clear answer: it may be on its way. Never a failure, never a retry.</summary>
    Unclear,
}

/// <summary>
/// The receipt of one send: what went, to whom, under which id, and how far the network had taken it
/// when the wallet stopped watching. Everything on it can be copied, and the whole card can be saved
/// as a picture — a line of green text that ran off the edge of the window could be neither.
/// </summary>
public sealed record SendReceiptVm(
    SendReceiptState State,
    string Symbol,
    string Amount,
    string Fiat,
    string To,
    string TxId,
    string ExplorerUrl,
    string Fee,
    string When,
    string Note,
    string Detail = "")
{
    public bool IsConfirmed => State == SendReceiptState.Confirmed;
    public bool IsUnclear => State == SendReceiptState.Unclear;

    public string Title => Loc.Instance[State switch
    {
        SendReceiptState.Confirmed => "receipt.titleConfirmed",
        SendReceiptState.Broadcast => "receipt.titleBroadcast",
        _ => "receipt.titleUnclear",
    }];

    public string StatusText => Loc.Instance[State switch
    {
        SendReceiptState.Confirmed => "receipt.stConfirmed",
        SendReceiptState.Broadcast => "receipt.stBroadcast",
        _ => "receipt.stUnclear",
    }];

    /// <summary>Green once a block holds it, the theme's accent while it is on its way, amber when unknown.</summary>
    public string StatusColor => State switch
    {
        SendReceiptState.Confirmed => "UmPos",
        SendReceiptState.Broadcast => "UmAccentBright",
        _ => "#E7CA83",
    };

    public string Sticker => State switch
    {
        SendReceiptState.Confirmed => "avares://Umbrella.Wallet.App/Assets/stickers/money.json",
        SendReceiptState.Broadcast => "avares://Umbrella.Wallet.App/Assets/stickers/up.json",
        _ => "avares://Umbrella.Wallet.App/Assets/stickers/loading.json",
    };

    /// <summary>The drawn emblem — a tick, a paper plane, a clock — for when stickers are off (they are
    /// opt-in), so the receipt never opens with a blank space where its picture should be.</summary>
    public Avalonia.Media.Geometry Emblem => State switch
    {
        SendReceiptState.Confirmed => TickGlyph,
        SendReceiptState.Broadcast => PlaneGlyph,
        _ => ClockGlyph,
    };

    private static readonly Avalonia.Media.Geometry TickGlyph =
        Avalonia.Media.StreamGeometry.Parse("M5 12.6 L10 17.4 L19.2 6.8");
    internal static readonly Avalonia.Media.Geometry PlaneGlyph =
        Avalonia.Media.StreamGeometry.Parse("M3.5 11.2 L20.5 3.8 L15 20.2 L11.4 13 Z M11.4 13 L20.5 3.8");
    private static readonly Avalonia.Media.Geometry ClockGlyph =
        Avalonia.Media.StreamGeometry.Parse("M12 3.5 C16.7 3.5 20.5 7.3 20.5 12 C20.5 16.7 16.7 20.5 12 20.5 C7.3 20.5 3.5 16.7 3.5 12 C3.5 7.3 7.3 3.5 12 3.5 Z M12 7.5 V12 L15.2 14");

    public bool HasFiat => Fiat.Length > 0;
    public bool HasFee => Fee.Length > 0;
    public bool HasNote => Note.Length > 0;
    public bool HasDetail => Detail.Length > 0 && Detail != Note;
    public bool HasTxId => TxId.Length > 0;
    public bool HasLink => ExplorerUrl.Length > 0;

    /// <summary>The id in two halves' worth of characters — enough to compare, short enough to fit.</summary>
    public string TxIdShort => TxId.Length > 22 ? $"{TxId[..10]}…{TxId[^10..]}" : TxId;

    public string ToShort => To.Length > 22 ? $"{To[..10]}…{To[^10..]}" : To;

    /// <summary>The receipt as plain text, for pasting into a chat or a note.</summary>
    public string AsText()
    {
        var L = Loc.Instance;
        var lines = new System.Collections.Generic.List<string>
        {
            $"Phobia Wallet — {Title}",
            $"{L["receipt.amount"]}: {Amount}{(HasFiat ? $" ({Fiat})" : string.Empty)}",
            $"{L["receipt.to"]}: {To}",
        };
        if (HasFee) lines.Add($"{L["receipt.fee"]}: {Fee}");
        if (HasTxId) lines.Add($"{L["receipt.txid"]}: {TxId}");
        lines.Add($"{L["receipt.status"]}: {StatusText} · {When}");
        if (HasLink) lines.Add(ExplorerUrl);
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// The send sheet: the one amount box with its unit switch, the compact review that opens over the
/// page, and the receipt that replaces it when the send is done.
///
/// The fund path is untouched — <see cref="MainViewModel.SendAmount"/> is still the single value that
/// is quoted and signed. This only decides what is on screen while that happens.
/// </summary>
public partial class MainViewModel
{
    // ---- One amount box, two units -----------------------------------------------------------------
    // There were two boxes, the currency one on top with nothing but a grey watermark to say so. In a
    // live test on 2026-10-10 a number typed into it, meant as the coin, was read as the display
    // currency and went out a thousand times smaller — twice — and the recipient saw nothing arrive.
    // One box now, and the unit it takes is written on it.

    /// <summary>True while the amount box takes the display currency instead of the coin.</summary>
    [ObservableProperty] private bool _sendAmountInFiat;

    public bool SendAmountInCoin => !SendAmountInFiat;

    /// <summary>The unit the box takes right now: "SOL", or "CNY".</summary>
    public string SendAmountUnit => SendAmountInFiat ? Fx.Code : SelectedSendAsset?.DisplayTicker ?? string.Empty;

    /// <summary>The paper plane of the "sending" face of the sheet.</summary>
    public Avalonia.Media.Geometry SendingEmblem => SendReceiptVm.PlaneGlyph;

    /// <summary>"0.005 SOL" — what a currency amount comes to, without the "=" of the old hint.</summary>
    public string SendWillSendCoin => SendFiatCoinEquiv.TrimStart('=', ' ');

    /// <summary>"Enter in CNY" / "Enter in SOL" — what the switch beside the box changes it to.</summary>
    public string SendAmountSwitchLabel => string.Format(Loc.Instance["send.unitSwitch"],
        SendAmountInFiat ? SelectedSendAsset?.DisplayTicker ?? string.Empty : Fx.Code);

    // Set while the currency box is being filled from the coin amount, so that fill is not read back
    // as the user typing a (rounded) currency figure over the exact coin amount they entered.
    private bool _fillingFiatFromCoin;

    [RelayCommand]
    private void ToggleSendAmountUnit()
    {
        if (!SendAmountInFiat)
        {
            if (!FiatInputAvailable) return;   // no price, nothing to convert with
            _fillingFiatFromCoin = true;
            try { SendFiatAmount = FiatConvert.CoinToFiatText(SendAmount, PriceForSelected() * Fx.Rate); }
            finally { _fillingFiatFromCoin = false; }
            SendAmountInFiat = true;
        }
        else
        {
            SendAmountInFiat = false;
        }
    }

    partial void OnSendAmountInFiatChanged(bool value) => NotifySendAmountUnit();

    private void NotifySendAmountUnit()
    {
        OnPropertyChanged(nameof(SendAmountInCoin));
        OnPropertyChanged(nameof(SendAmountUnit));
        OnPropertyChanged(nameof(SendAmountSwitchLabel));
    }

    // ---- The sheet ---------------------------------------------------------------------------------

    /// <summary>True from Confirm until the network has answered (or stopped answering).</summary>
    [ObservableProperty] private bool _isSendingNow;

    /// <summary>The receipt of the send that has just finished; null when there is none on screen.</summary>
    [ObservableProperty] private SendReceiptVm? _sendReceipt;

    public bool HasSendReceipt => SendReceipt is not null;

    /// <summary>The sheet is over the page while there is something to review, a send in flight, or a
    /// receipt to read.</summary>
    public bool IsSendSheetOpen => HasSendQuote || IsSendingNow || HasSendReceipt;

    public bool IsSendReviewShown => HasSendQuote && !IsSendingNow && !HasSendReceipt;

    /// <summary>Amber notes worth reading before confirming — shown at the top of the review, not under it.</summary>
    public bool HasSendReviewWarnings => SendSimulationWarnings.Count > 0;

    public bool HasSendError => !string.IsNullOrEmpty(SendError);

    partial void OnIsSendingNowChanged(bool value) => NotifySendSheet();

    partial void OnSendReceiptChanged(SendReceiptVm? value) => NotifySendSheet();

    partial void OnSendErrorChanged(string value) => OnPropertyChanged(nameof(HasSendError));

    private void NotifySendSheet()
    {
        OnPropertyChanged(nameof(HasSendReceipt));
        OnPropertyChanged(nameof(IsSendSheetOpen));
        OnPropertyChanged(nameof(IsSendReviewShown));
        OnPropertyChanged(nameof(HasSendReviewWarnings));
    }

    /// <summary>The fee as the review stated it ("0.000005 SOL"), kept for the receipt.</summary>
    private string _reviewFeeText = string.Empty;

    /// <summary>Builds the receipt. Called before the quotes are cleared, while the review still knows
    /// the fee and the fiat value.</summary>
    private void ShowSendReceipt(SendReceiptState state, string symbol, decimal amount, string to,
        string? reference, string? explorer, string? note = null)
    {
        var link = string.IsNullOrWhiteSpace(explorer) ? string.Empty
            : explorer.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? explorer : $"https://{explorer}";
        SendReceipt = new SendReceiptVm(
            state, symbol,
            $"{Fmt(amount)} {symbol}",
            FiatEquivalentLabel(symbol, amount),
            to.Trim(),
            reference ?? string.Empty,
            link,
            _reviewFeeText,
            DateTime.Now.ToString("d MMM yyyy · HH:mm", Fx.Culture),
            // An unclear ending is explained in the wallet's language; the sender's own words follow it.
            state == SendReceiptState.Unclear ? Loc.Instance["receipt.unclearNote"] : note ?? string.Empty,
            state == SendReceiptState.Unclear ? note ?? string.Empty : string.Empty);
    }

    [RelayCommand]
    private void CloseSendReceipt()
    {
        SendReceipt = null;
        SendSuccess = string.Empty;
        DismissSendLeakReport();
    }

    [RelayCommand]
    private async Task CopyReceiptTxIdAsync()
    {
        if (SendReceipt is not { HasTxId: true } r) return;
        await CopyTextAsync(r.TxId);
        ShowToast(Loc.Instance["receipt.copiedId"], isError: false);
    }

    [RelayCommand]
    private async Task CopyReceiptAddressAsync()
    {
        if (SendReceipt is not { } r) return;
        await CopyTextAsync(r.To);
        ShowToast(Loc.Instance["receipt.copiedAddress"], isError: false);
    }

    /// <summary>Copied, not opened: the system browser would ask the explorer about this transaction
    /// from the user's own address, around Tor. Paste it into Tor Browser.</summary>
    [RelayCommand]
    private async Task CopyReceiptLinkAsync()
    {
        if (SendReceipt is not { HasLink: true } r) return;
        await CopyTextAsync(r.ExplorerUrl);
        ShowToast(Loc.Instance["toast.linkCopied"], isError: false);
    }

    [RelayCommand]
    private async Task CopyReceiptTextAsync()
    {
        if (SendReceipt is not { } r) return;
        await CopyTextAsync(r.AsText());
        ShowToast(Loc.Instance["receipt.copiedAll"], isError: false);
    }

    /// <summary>Said by the sheet after it has written the picture (the view owns the pixels).</summary>
    public void ReceiptImageSaved(bool ok) =>
        ShowToast(Loc.Instance[ok ? "receipt.savedImage" : "receipt.saveFailed"], isError: !ok);

    /// <summary>"phobia-receipt-SOL-20261010-0017.png"</summary>
    public string ReceiptFileName =>
        $"phobia-receipt-{SendReceipt?.Symbol ?? "tx"}-{DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.png";

    // ---- The password to stop asking for the password ----------------------------------------------
    // "Ask for the password before every send" could be switched off by anyone at an unlocked wallet —
    // which is exactly the person it is there to stop. Switching it ON is free; switching it OFF takes
    // the password once more.

    /// <summary>True while the password is being asked for to switch the send password off.</summary>
    [ObservableProperty] private bool _sendPasswordOffAsked;

    [ObservableProperty] private string _sendPasswordOffInput = string.Empty;

    [ObservableProperty] private string _sendPasswordOffError = string.Empty;

    public bool HasSendPasswordOffError => SendPasswordOffError.Length > 0;

    partial void OnSendPasswordOffErrorChanged(string value) => OnPropertyChanged(nameof(HasSendPasswordOffError));

    /// <summary>What the switch in Settings is bound to: on at once, off only through the password.</summary>
    public bool RequirePasswordForSendSwitch
    {
        get => RequirePasswordForSend;
        set
        {
            if (value == RequirePasswordForSend)
            {
                if (value) CancelSendPasswordOff();
                return;
            }

            if (value)
            {
                RequirePasswordForSend = true;
                CancelSendPasswordOff();
            }
            else
            {
                // Not yet: ask first. The switch is told it is still on, so it springs back.
                SendPasswordOffAsked = true;
                SendPasswordOffError = string.Empty;
            }

            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void CancelSendPasswordOff()
    {
        SendPasswordOffAsked = false;
        SendPasswordOffInput = string.Empty;
        SendPasswordOffError = string.Empty;
        OnPropertyChanged(nameof(RequirePasswordForSendSwitch));
    }

    [RelayCommand]
    private async Task ConfirmSendPasswordOffAsync()
    {
        var typed = SendPasswordOffInput ?? string.Empty;
        SendPasswordOffInput = string.Empty;
        if (typed.Length == 0 || _unlockedMnemonic is null)
        {
            SendPasswordOffError = Loc.Instance["send.passwordNeeded"];
            return;
        }

        // The same test a send makes: the vault opened with it must give back THIS wallet's phrase.
        string opened;
        try { opened = await _vault.UnlockAsync(typed); }
        catch
        {
            SendPasswordOffError = Loc.Instance["send.passwordWrong"];
            return;
        }

        if (!string.Equals(opened.Trim(), _unlockedMnemonic.Trim(), StringComparison.Ordinal))
        {
            SendPasswordOffError = Loc.Instance["send.passwordWrong"];
            return;
        }

        RequirePasswordForSend = false;
        SendPasswordOffAsked = false;
        SendPasswordOffError = string.Empty;
        OnPropertyChanged(nameof(RequirePasswordForSendSwitch));
    }
}
