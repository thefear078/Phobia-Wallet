using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// "Every wallet's balance" (Settings → Wallets, off by default): the figure of each wallet that is not
/// open, kept current in the background.
/// </summary>
public partial class MainViewModel
{
    private CancellationTokenSource? _otherTotalsCts;
    private DateTimeOffset _otherTotalsAt = DateTimeOffset.MinValue;

    /// <summary>How long a read of the other wallets stays good before the next refresh repeats it.</summary>
    private static readonly TimeSpan OtherTotalsReuse = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads the balance of every wallet that is not open. A wallet opened on this device before is read
    /// at the addresses it remembered — public data, no key needed. One never opened here has none: its
    /// vault is opened with the password in use, its addresses derived and the phrase dropped again, the
    /// same work a switch does. A wallet with a different password keeps no figure rather than a wrong
    /// one. Nothing is read while the option is off.
    /// </summary>
    private async Task RefreshOtherWalletTotalsAsync(bool force = false)
    {
        if (!ShowAllWalletTotals || !IsUnlocked) return;
        if (!force && DateTimeOffset.UtcNow - _otherTotalsAt < OtherTotalsReuse) return;
        _otherTotalsAt = DateTimeOffset.UtcNow;

        _otherTotalsCts?.Cancel();
        var cts = _otherTotalsCts = new CancellationTokenSource();
        var ct = cts.Token;
        var epoch = _lockEpoch;
        var password = _sessionPassword;
        var activeId = _registry.Active?.Id;
        var prices = MarketPriceBook();

        foreach (var wallet in _registry.Wallets.Where(w => w.Id != activeId).ToList())
        {
            if (ct.IsCancellationRequested || epoch != _lockEpoch) return;
            try
            {
                var cached = _balanceStore.Load(wallet.Id).ToList();
                var fromCache = cached.Count > 0;
                var entries = fromCache ? cached : await AddressesOfAsync(wallet, password, ct);
                if (entries.Count == 0) continue;

                var fresh = await ReadEntriesAsync(entries, fromCache, prices, ct);
                if (ct.IsCancellationRequested || epoch != _lockEpoch) return;
                _balanceStore.Save(wallet.Id, fresh);
                RefreshWalletList();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // one wallet unread: the others still count
            }
        }
    }

    /// <summary>Today's USD price of every coin the market knows, by symbol.</summary>
    private Dictionary<string, double> MarketPriceBook() =>
        Market.Where(m => m.HasPrice)
            .GroupBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Price, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each remembered holding at today's price, its amount read again where one address holds the
    /// whole balance. A Bitcoin-family figure from the cache is the wallet's full HD scan, which one
    /// address cannot reproduce — it is kept as it was, like tokens and anything else without a reader.
    /// </summary>
    private async Task<List<BalanceStore.Entry>> ReadEntriesAsync(
        List<BalanceStore.Entry> entries, bool fromCache, Dictionary<string, double> prices, CancellationToken ct)
    {
        var reads = entries.Select(async e =>
        {
            var price = prices.TryGetValue(e.Symbol, out var p) ? p : e.Price;
            var chain = ParseChain(e.Symbol);
            var info = chain is null ? null : ChainCatalog.All.FirstOrDefault(c => c.Id == chain);
            var isBase = info is not null && (e.Network.Length == 0 || e.Network == info.Name);
            var keep = !isBase || (fromCache && UtxoScanChains.Contains(e.Symbol, StringComparer.OrdinalIgnoreCase));
            if (keep) return e with { Price = price };

            var read = await _balances.GetBalanceAsync(chain!.Value, e.Address, ct);
            return read is null ? e with { Price = price } : e with { Amount = (double)read.NativeAmount, Price = price };
        });
        return (await Task.WhenAll(reads)).ToList();
    }

    /// <summary>The receive address of every coin a never-opened wallet holds, as empty holdings to read.
    /// The phrase lives only inside this method.</summary>
    private async Task<List<BalanceStore.Entry>> AddressesOfAsync(WalletEntry wallet, string? password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password)) return [];
        var vault = new EncryptedFileSeedVault(_registry.VaultPathFor(wallet));
        if (!vault.Exists) return [];

        string mnemonic;
        try
        {
            mnemonic = await vault.UnlockAsync(password);
        }
        catch
        {
            return [];   // its own password: no figure, never a guess
        }

        return await Task.Run(() =>
        {
            var list = new List<BalanceStore.Entry>();
            if (!_mnemonics.Validate(mnemonic).IsValid)
            {
                if (TonMnemonic.IsTonMnemonic(mnemonic))
                {
                    var (tonAddress, _) = TonMnemonic.DeriveWallet(mnemonic);
                    list.Add(new BalanceStore.Entry("TON", tonAddress, 0, 0, 0, "The Open Network"));
                }
                return list;   // a Monero seed: its balance needs the Monero service, not an address
            }

            var deriver = new HdAddressDeriver();
            foreach (var chain in ChainCatalog.All)
            {
                if (wallet.Coins is { Count: > 0 } coins && !coins.Contains(chain.Symbol, StringComparer.OrdinalIgnoreCase)) continue;
                if (!ChainCatalog.HasRealAddress(chain.Id) || chain.Id == ChainId.Xmr) continue;
                try
                {
                    list.Add(new BalanceStore.Entry(
                        chain.Symbol, deriver.DeriveReceiveAddress(mnemonic, chain.Id).Address, 0, 0, 0, chain.Name));
                }
                catch
                {
                    // a chain this phrase cannot derive: left out
                }
            }
            return list;
        }, ct);
    }
}
