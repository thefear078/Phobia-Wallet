using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Finding money an imported phrase holds where this wallet does not look.
///
/// A phrase brought over from MetaMask, Ledger Live, Phantom, Solflare, TronLink or an older Bitcoin
/// wallet can hold its coins at a different derivation path — a second MetaMask account, Solflare's
/// three-level Solana path, a legacy "1…" Bitcoin address. Imported here, it showed zero and looked
/// like the wallet "does not pull my wallets". Each wallet is scanned once (and again on request) at
/// the paths those apps use (<see cref="HdAddressDeriver.DeriveAlternativeAccounts"/>); anything with a
/// balance is kept for that wallet and shown as a watch-only row in its holdings, counted in its total
/// and named after the app whose path it is. Sending from those paths is not offered — the row says
/// so — so nothing can be signed from a key the user did not knowingly ask for.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private bool _isDiscovering;

    [ObservableProperty] private string _discoveryStatus = string.Empty;

    /// <summary>What the active wallet holds at other paths (from its last scan).</summary>
    private List<FoundAccountEntry> _foundAccounts = [];

    /// <summary>Wallets whose automatic scan has been started this session.</summary>
    private readonly HashSet<string> _discoveryStarted = new(StringComparer.Ordinal);

    private static readonly HashSet<string> MajorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC", "DAI", "USDD", "TUSD", "BUSD", "FDUSD", "PYUSD", "WETH", "WBTC", "LINK", "UNI",
    };

    private const string FoundDomain = "phobia-found-accounts-v1";

    private static string FoundFolder => Path.Combine(AppPaths.DataRoot, "found");

    /// <summary>A plain-text file an earlier version wrote: taken over, sealed, by the wallet opened first.</summary>
    private static string LegacyFoundFile(string walletId) =>
        Path.Combine(FoundFolder, $"{WalletDataKey.SafeId(walletId)}.json");

    /// <summary>Loads the active wallet's found accounts (on unlock and on every switch).</summary>
    private void LoadFoundAccounts()
    {
        _foundAccounts = (_dataKey is { } key ? ReadFound(key) : null)?.Accounts ?? [];
        OnPropertyChanged(nameof(HasFoundAccounts));
        DiscoveryStatus = _foundAccounts.Count > 0
            ? string.Format(Loc.Instance["found.some"], _foundAccounts.Count)
            : string.Empty;
    }

    /// <summary>
    /// What a scan of other wallets' paths found for this phrase — sealed per wallet and secret
    /// (<see cref="WalletDataKey"/>): it lists addresses that hold money, which is exactly what must not
    /// be read off the disk, nor shown in a decoy opened by the duress password.
    /// </summary>
    private static FoundAccountsFile? ReadFound(WalletDataKey key)
    {
        try
        {
            var file = Path.Combine(FoundFolder, key.FileName);
            if (File.Exists(file)) return key.Read<FoundAccountsFile>(file, FoundDomain);

            var legacy = LegacyFoundFile(key.WalletId);
            if (!File.Exists(legacy)) return null;
            var found = JsonSerializer.Deserialize<FoundAccountsFile>(File.ReadAllText(legacy));
            if (found is null) return null;
            WriteFound(key, found);
            if (File.Exists(file)) File.Delete(legacy);   // only once the sealed copy is on disk
            return found;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteFound(WalletDataKey key, FoundAccountsFile found)
    {
        try
        {
            Directory.CreateDirectory(FoundFolder);
            key.Write(Path.Combine(FoundFolder, key.FileName), found, FoundDomain);
        }
        catch
        {
            // Not worth failing over: the scan simply runs again next time.
        }
    }

    /// <summary>The found accounts as watch targets for the balance refresh — only when the user chose
    /// to see them; by default the assets are this wallet's own accounts.</summary>
    private IEnumerable<WatchAddress> FoundWatchTargets() =>
        ShowFoundAccounts ? _foundAccounts.Select(f => new WatchAddress(f.Chain, f.Address, FoundLabel(f))) : [];

    /// <summary>The account a found address is, for naming its tokens ("TronLink, account 3").</summary>
    private string? FoundOwner(string address)
    {
        var found = _foundAccounts.FirstOrDefault(f => f.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
        if (found is null) return null;
        var label = FoundLabel(found);
        var cut = label.IndexOf(" · ", StringComparison.Ordinal);
        return cut >= 0 ? label[(cut + 3)..] : label;
    }

    public bool HasFoundAccounts => _foundAccounts.Count > 0;

    /// <summary>Settings switch: show the found accounts in the assets (view-only).</summary>
    public bool ShowFoundAccounts
    {
        get => _uiSettings.ShowFoundAccounts;
        set
        {
            if (_uiSettings.ShowFoundAccounts == value) return;
            _uiSettings.ShowFoundAccounts = value;
            _uiSettings.Save();
            OnPropertyChanged();
            if (value)
            {
                if (IsUnlocked) _ = RefreshLiveDataAsync();
                return;
            }

            // Off: take their rows (the coin and any tokens at those addresses) out right away.
            var addresses = _foundAccounts.Select(f => f.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Accounts.Where(a => addresses.Contains(a.Address)).ToList()) Accounts.Remove(row);
            RefreshHoldings();
            RecalcBalance();
            SaveBalanceCache();
        }
    }

    /// <summary>"Ethereum · MetaMask, account 2" in the wallet's language.</summary>
    private static string FoundLabel(FoundAccountEntry f)
    {
        var coin = ParseChain(f.Chain) is { } chain ? ChainCatalog.Get(chain).Name : f.Chain;
        var originKey = "found.origin." + f.Origin;
        var origin = Loc.Instance[originKey];
        if (origin == originKey) origin = f.Origin;
        return string.Format(Loc.Instance["found.label"], coin, origin, f.Account);
    }

    /// <summary>Starts the one automatic scan a wallet gets, after its balances first load.</summary>
    private void MaybeDiscoverAutomatically()
    {
        if (!StartTorAutomatically || string.IsNullOrEmpty(_unlockedMnemonic) || IsDiscovering || IsWatchWallet) return;
        if (_dataKey is not { } key) return;
        if (!_discoveryStarted.Add(key.ToString())) return;
        if (ReadFound(key) is { Scanned: true }) return;
        _ = DiscoverAsync(userAsked: false);
    }

    [RelayCommand]
    private Task DiscoverOtherPathsAsync() => DiscoverAsync(userAsked: true);

    private async Task DiscoverAsync(bool userAsked)
    {
        if (IsDiscovering || string.IsNullOrEmpty(_unlockedMnemonic) || IsWatchWallet) return;
        if (_dataKey is not { } dataKey) return;
        var epoch = _lockEpoch;
        var walletId = ActiveWalletCacheKey;
        var mnemonic = _unlockedMnemonic!;

        IsDiscovering = true;
        DiscoveryStatus = Loc.Instance["found.scanning"];
        try
        {
            IReadOnlyList<AlternativeAccount> candidates;
            try
            {
                candidates = await Task.Run(() => _deriver.DeriveAlternativeAccounts(mnemonic));
            }
            catch
            {
                DiscoveryStatus = string.Empty;   // a Monero-only or otherwise non-BIP39 wallet: nothing to look at
                return;
            }

            using var gate = new SemaphoreSlim(3);
            var unanswered = 0;
            var results = await Task.WhenAll(candidates.Select(async candidate =>
            {
                await gate.WaitAsync();
                try
                {
                    var balance = await _balances.GetBalanceAsync(candidate.Chain, candidate.Address, CancellationToken.None);
                    if (balance is null)
                    {
                        Interlocked.Increment(ref unanswered);
                        return null;
                    }

                    if (balance.NativeAmount > 0) return candidate;
                    // An account holding only tokens (USDT on TRON, above all) is still money.
                    var tokens = candidate.Chain switch
                    {
                        ChainId.Tron => await _balances.GetTronTokensAsync(candidate.Address, CancellationToken.None),
                        ChainId.Eth => await _balances.GetEthTokensAsync(candidate.Address, CancellationToken.None),
                        _ => [],
                    };
                    return tokens.Any(t => t.Amount > 0 && MajorTokens.Contains(t.Symbol)) ? candidate : null;
                }
                catch
                {
                    Interlocked.Increment(ref unanswered);
                    return null;
                }
                finally
                {
                    gate.Release();
                }
            }));

            // Locked or switched meanwhile: these belong to a wallet no longer on screen.
            if (epoch != _lockEpoch || walletId != ActiveWalletCacheKey) return;

            var found = results.Where(r => r is not null)
                .Select(r => new FoundAccountEntry(SymbolFor(r!.Chain), r.Path, r.Address, r.Origin, r.Account))
                .ToList();
            // Most servers silent: not a real "nothing here" — the next unlock tries again.
            var complete = unanswered <= candidates.Count / 4;
            WriteFound(dataKey, new FoundAccountsFile(complete, found));
            _foundAccounts = found;
            OnPropertyChanged(nameof(HasFoundAccounts));

            DiscoveryStatus = found.Count > 0
                ? string.Format(Loc.Instance["found.some"], found.Count)
                : complete ? Loc.Instance["found.none"] : Loc.Instance["found.incomplete"];
            if (found.Count > 0)
            {
                ShowToast(DiscoveryStatus, isError: false);
                if (ShowFoundAccounts) _ = RefreshLiveDataAsync();
            }
            else if (userAsked)
            {
                ShowToast(DiscoveryStatus, isError: !complete);
            }
        }
        finally
        {
            IsDiscovering = false;
        }
    }
}

/// <summary>What a wallet's scan of other paths found; <paramref name="Scanned"/> is false when too
/// many servers did not answer for "nothing found" to be trusted.</summary>
public sealed record FoundAccountsFile(bool Scanned, List<FoundAccountEntry> Accounts);

/// <summary>One account found at another wallet's path (public data only: no keys).</summary>
public sealed record FoundAccountEntry(string Chain, string Path, string Address, string Origin, uint Account);
