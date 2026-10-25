using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Core.Watch;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Seedless watch-only wallets (roadmap H.5): a wallet made from extended public keys and addresses,
/// with no phrase on this machine. Balances, history and receive addresses work as in any wallet;
/// nothing can be signed, and every signing path refuses the wallet by its type rather than by a flag
/// on a screen.
///
/// The wallet is opened by its password like any other, and its vault is the same two-slot file; what
/// the slot holds is a key to the sealed description beside it (<see cref="WatchWalletFile"/>).
/// </summary>
public partial class MainViewModel
{
    /// <summary>What the open wallet watches; null for a wallet with a phrase.</summary>
    private WatchSpec? _watchSpec;

    /// <summary>The address source of each watched account, by its row's derivation key.</summary>
    private readonly Dictionary<string, XpubAddressSource> _watchSources = new(StringComparer.Ordinal);

    /// <summary>Each watched account's last scan: the next unused receive address and its coins.</summary>
    private readonly Dictionary<string, UtxoScanResult> _watchScans = new(StringComparer.Ordinal);

    /// <summary>True while a watch-only wallet is open: there is nothing here that can sign.</summary>
    public bool IsWatchWallet => _watchSpec is not null;

    private const string WatchXpubPrefix = "watch-xpub:";

    // ---- adding one ---------------------------------------------------------------------------------

    /// <summary>The pasted description: xpubs, descriptors and addresses, one a line.</summary>
    [ObservableProperty] private string _watchSpecInput = string.Empty;

    /// <summary>What the paste will watch, line by line, as the user types.</summary>
    public ObservableCollection<string> WatchSpecPreview { get; } = [];

    /// <summary>The lines that will not be used, and why.</summary>
    public ObservableCollection<string> WatchSpecIssues { get; } = [];

    public bool HasWatchSpecIssues => WatchSpecIssues.Count > 0;

    public bool HasWatchSpecPreview => WatchSpecPreview.Count > 0;

    public bool IsWatchStage => !NeedsDisclaimer && !HasVault && SetupStage == "Watch";

    partial void OnWatchSpecInputChanged(string value) => PreviewWatchSpec();

    private WatchParseResult ParseWatchSpec(string text) =>
        WatchSpecParser.Parse(text, address => DetectChain(address));

    private void PreviewWatchSpec()
    {
        WatchSpecPreview.Clear();
        WatchSpecIssues.Clear();
        var result = ParseWatchSpec(WatchSpecInput);
        foreach (var x in result.Spec.Xpubs)
            WatchSpecPreview.Add($"{SymbolFor(x.Chain)} · {WatchScriptLabel(x.Script)} · {Shorten(x.ExtPubKey)}");
        foreach (var a in result.Spec.Addresses)
            WatchSpecPreview.Add($"{a.Symbol} · {Shorten(a.Address)}");
        foreach (var issue in result.Issues)
            WatchSpecIssues.Add(string.Format(Loc.Instance["watch.lineIssue"], issue.Line, Loc.Instance[issue.Code]));
        OnPropertyChanged(nameof(HasWatchSpecIssues));
        OnPropertyChanged(nameof(HasWatchSpecPreview));
    }

    private static string WatchScriptLabel(WatchScript script) => script switch
    {
        WatchScript.Legacy => Loc.Instance["watch.script.legacy"],
        WatchScript.NestedSegwit => Loc.Instance["watch.script.nested"],
        WatchScript.Segwit => Loc.Instance["watch.script.segwit"],
        _ => Loc.Instance["watch.script.taproot"],
    };

    [RelayCommand]
    private void GoToWatch()
    {
        ClearPasswordFields();
        WatchSpecInput = string.Empty;
        SetupStage = "Watch";
    }

    /// <summary>
    /// Makes the watch-only wallet: the description is sealed first, then the vault that holds its key
    /// is written — so a vault never exists whose description is missing. A description left behind by
    /// a vault that then failed to be written opens with nothing: its key was never stored.
    /// </summary>
    [RelayCommand]
    private async Task ImportWatchWalletAsync()
    {
        var parsed = ParseWatchSpec(WatchSpecInput);
        if (parsed.Issues.Count > 0)
        {
            var first = parsed.Issues[0];
            Fail(string.Format(Loc.Instance["watch.lineIssue"], first.Line, Loc.Instance[first.Code]));
            return;
        }

        if (parsed.Spec.IsEmpty)
        {
            Fail(Loc.Instance["watch.empty"]);
            return;
        }

        var pw = ReuseAppPassword ? _sessionPassword! : Password;
        if (!ValidateVaultPassword(pw)) return;

        await RunBusyAsync(async () =>
        {
            var secret = WatchWalletFile.NewSecret();
            WatchWalletFile.Save(_vault.VaultPath, secret, parsed.Spec);
            await _vault.CreateAsync(secret, pw);
            SetSessionPassword(pw);
            HasVault = true;
            FinalizeWalletRegistration();
            SetUnlocked(secret);
            RecoveryPhrase = string.Empty;
            PendingPhraseBackup = false;
            WatchSpecInput = string.Empty;
            ClearPasswordFields();
            ActiveSection = "Portfolio";
            StatusMessage = Loc.Instance["watch.added"];
            await RefreshLiveDataAsync();
        });
    }

    // ---- opening one --------------------------------------------------------------------------------

    /// <summary>
    /// Reads the description of a watch-only wallet whose vault was just opened. A missing or damaged
    /// description opens an empty wallet that says so: still nothing that can sign, and nothing guessed.
    /// </summary>
    private void LoadWatchSpec(string secret)
    {
        _watchSources.Clear();
        _watchScans.Clear();
        if (!WatchWalletFile.IsWatchSecret(secret))
        {
            _watchSpec = null;
            OnPropertyChanged(nameof(IsWatchWallet));
            return;
        }

        try
        {
            _watchSpec = WatchWalletFile.Load(_vault.VaultPath, secret);
        }
        catch
        {
            _watchSpec = WatchSpec.Empty;
            ShowToast(Loc.Instance["watch.missing"], isError: true);
        }

        for (var i = 0; i < _watchSpec.Xpubs.Count; i++)
        {
            try { _watchSources[WatchXpubPrefix + i] = new XpubAddressSource(_watchSpec.Xpubs[i]); }
            catch { /* a key that no longer decodes is a row that cannot be read, not a crash */ }
        }

        OnPropertyChanged(nameof(IsWatchWallet));
    }

    /// <summary>The rows of a watch-only wallet: one per watched account, one per watched address.</summary>
    private void DeriveWatchAccounts()
    {
        foreach (var (key, source) in _watchSources)
        {
            var symbol = SymbolFor(source.Chain);
            Accounts.Add(new WalletAccountViewModel(
                symbol, $"{ChainName(source.Chain)} · {WatchScriptLabel(source.Watched.Script)}", "Watch",
                source.At(0, 0, UtxoScriptKind.Default).Address, key, 0, 0, ChainName(source.Chain), 0));
        }

        foreach (var a in _watchSpec!.Addresses)
        {
            Accounts.Add(new WalletAccountViewModel(
                a.Symbol, $"Watch · {a.Symbol}", "Watch", a.Address, "external", 0, 0, a.Symbol, 0));
        }

        RebuildWatchPsbtAccounts();
        var first = Accounts.FirstOrDefault();
        ShortAddress = first is null ? string.Empty : Shorten(first.Address);
        WalletLabel = ActiveWalletLabel;
        RefreshHoldings();
        RecalcBalance();
    }

    private static string ChainName(ChainId chain) =>
        ChainCatalog.All.FirstOrDefault(c => c.Id == chain)?.Name ?? chain.ToString().ToUpperInvariant();

    /// <summary>The watched single addresses, read with the device's own watch list.</summary>
    private IEnumerable<WatchAddress> WatchSpecTargets() =>
        _watchSpec?.Addresses.Select(a => new WatchAddress(a.Symbol, a.Address, string.Empty)) ?? [];

    // ---- reading one --------------------------------------------------------------------------------

    /// <summary>
    /// Scans every watched account the way a phrase's accounts are scanned — the full gap walk first,
    /// then the short refresh past what was last seen used — and shows the next unused receive address.
    /// </summary>
    private async Task RefreshWatchAccountsAsync(
        IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)> prices, CancellationToken ct)
    {
        if (_watchSources.Count == 0) return;
        await ExplorerWarmUp.EnsureAsync(ct);

        foreach (var (key, source) in _watchSources.ToList())
        {
            var symbol = SymbolFor(source.Chain);
            var previous = _watchScans.GetValueOrDefault(key);
            var floors = previous is null
                ? UtxoScanFloors.None
                : new UtxoScanFloors(null, previous.HighestUsedExternalIndex, null, previous.HighestUsedInternalIndex);

            UtxoScanResult? scan;
            try
            {
                scan = await _utxoScanner.ScanAsync(source, UtxoExplorerFor(symbol), floors, ct: ct,
                    gapLimit: previous is null ? null : UtxoAccountScanner.RefreshGapLimit);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { scan = null; }
            ct.ThrowIfCancellationRequested();

            var at = -1;
            for (var i = 0; i < Accounts.Count; i++)
                if (Accounts[i].Derivation == key) { at = i; break; }
            if (at < 0) continue;
            var row = Accounts[at];

            if (scan is null)
            {
                // Nobody answered: the last figure stays, marked as such (P0.6).
                if (row.Balance == BalanceRead.Live) Accounts[at] = row with { Balance = BalanceRead.Cached };
                continue;
            }

            _watchScans[key] = scan;
            var amount = (scan.ConfirmedSat + scan.PendingSat) / 100_000_000d;
            var next = scan.HighestUsedExternalIndex is { } used ? used + 1 : 0u;
            Accounts[at] = WithPrice(row with
            {
                Amount = amount,
                Address = source.At(0, next, UtxoScriptKind.Default).Address,
                Balance = scan.Partial ? BalanceRead.Cached : BalanceRead.Live,
            }, prices);
            RefreshHoldings();
            RecalcBalance();
        }
    }

    /// <summary>The history reads of a watch-only wallet: its accounts' used addresses, and its addresses.</summary>
    private List<Task<IReadOnlyList<ChainTx>>> WatchHistoryReads(HashSet<string> knownSolana)
    {
        var reads = new List<Task<IReadOnlyList<ChainTx>>>();
        foreach (var (key, source) in _watchSources)
        {
            var scan = _watchScans.GetValueOrDefault(key);
            var floors = scan is null
                ? UtxoScanFloors.None
                : new UtxoScanFloors(null, scan.HighestUsedExternalIndex, null, scan.HighestUsedInternalIndex);
            var own = Umbrella.Wallet.Core.Psbt.OwnScripts.For(source, floors);
            var plan = HistoryAddresses.Plan(own, floors, XpubAddressSource.NetworkFor(source.Chain));
            var symbol = SymbolFor(source.Chain);
            reads.Add(symbol switch
            {
                "DOGE" => _coinHistory.GetDogecoinAsync(plan.Query.Take(10).ToList()),
                "LTC" when Umbrella.Wallet.Core.Safety.ChainEndpoints.OverrideFor("LTC") is null
                    => _coinHistory.GetLitecoinAsync(plan.Query.Take(10).ToList()),
                _ => ReadUtxoHistoryAsync(symbol, plan),
            });
        }

        foreach (var a in _watchSpec?.Addresses ?? [])
        {
            if (ParseChain(a.Symbol) is { } chain) reads.AddRange(SingleAddressHistoryReads(chain, a.Address, knownSolana));
        }

        return reads;
    }

    // ---- refusing to sign ---------------------------------------------------------------------------

    /// <summary>
    /// True — after saying why — when the open wallet cannot sign. Every path that would derive a key
    /// asks this first: a watch-only wallet's secret is not a phrase, and nothing may be derived from it
    /// as though it were.
    /// </summary>
    private bool RefuseWatchOnly()
    {
        if (!IsWatchWallet) return false;
        Fail(Loc.Instance["watch.cannotSign"]);
        return true;
    }
}
