using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// How many transactions one history read asks each source for.
///
/// These were 25–30 everywhere: an address with two hundred transfers showed the newest couple of dozen
/// and nothing older, ever. Each is now what its source hands out for the price of ONE request — no
/// more calls, no more addresses told to anybody, just a longer answer. What is read is kept and added
/// to (HistoryStore), so older pages still accumulate behind these.
/// </summary>
public static class HistoryPage
{
    /// <summary>TronGrid: 200 a page is its maximum.</summary>
    public const int Tron = 200;
    /// <summary>Etherscan-style explorers (Routescan, Blockscout).</summary>
    public const int Evm = 100;
    /// <summary>toncenter: 100 a call.</summary>
    public const int Ton = 100;
    /// <summary>The XRP Ledger's account_tx.</summary>
    public const int Xrp = 100;
    /// <summary>Horizon: 200 a page is its maximum.</summary>
    public const int Stellar = 200;
    /// <summary>Statescan.</summary>
    public const int Polkadot = 100;
    /// <summary>A Cosmos archive node answers slowly and limits hard: a modest page.</summary>
    public const int Cosmos = 50;
    /// <summary>BlockCypher's maximum.</summary>
    public const int BlockCypher = 50;
}

/// <summary>One normalized on-chain transaction, ready to show in the Transactions list.</summary>
public sealed record ChainTx(
    string Kind,          // "Sent" | "Received"
    string Asset,         // "BTC", "USDT", "TRX"…
    string Amount,        // human amount, already signed-friendly (no sign; Kind carries direction)
    string Counterparty,  // the other address (truncated by the caller if desired)
    long UnixMs,          // for sorting / relative time
    string Explorer,      // full explorer URL for the tx
    string Hash);         // tx id, for de-duplication

/// <summary>
/// Reads real transaction history straight from public explorers for the user's OWN addresses, so
/// transactions made before the wallet was ever opened still show up. Keyless endpoints only, and it
/// rides the shared HTTP client — so it goes through Tor / the custom proxy exactly like balances.
///
/// First chains: TRON (TRC-20, e.g. USDT) and Bitcoin. Others (ETH/SOL/…) follow the same shape.
/// </summary>
public sealed class OnChainHistoryClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    /// <summary>TRC-20 token transfers (USDT and friends) for a TRON base58 address.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTronTrc20Async(
        string address, int limit = HistoryPage.Tron, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.trongrid.io/v1/accounts/{Uri.EscapeDataString(address)}" +
                      $"/transactions/trc20?limit={limit}&only_confirmed=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTronTrc20(json, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Any Esplora-style explorer (Blockstream for BTC, litecoinspace for LTC…).</summary>
    public Task<IReadOnlyList<ChainTx>> GetEsploraAsync(
        string apiBase, string address, string symbol, string explorerTxBase, CancellationToken ct = default) =>
        GetEsploraAsync(apiBase, address, new HashSet<string>(StringComparer.Ordinal) { address }, symbol, explorerTxBase, ct);

    /// <summary>
    /// The transactions touching <paramref name="address"/>, each judged against EVERY address in
    /// <paramref name="own"/> — see <see cref="ParseEsplora(string, IReadOnlySet{string}, string, string)"/>.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetEsploraAsync(
        string apiBase, string address, IReadOnlySet<string> own, string symbol, string explorerTxBase,
        CancellationToken ct = default) =>
        await TryEsploraAsync(apiBase, address, own, symbol, explorerTxBase, ct) ?? [];

    /// <summary>
    /// The same read across every server the balance scan would ask (<see cref="EsploraUtxoExplorer.BasesFor"/>),
    /// the ones refusing right now last. Bitcoin history read one server only: over Tor mempool.space
    /// often refuses, and the Activity screen showed no Bitcoin at all while the balance — which falls
    /// back — was read fine.
    /// </summary>
    private async Task<IReadOnlyList<ChainTx>> GetEsploraAnyAsync(
        string symbol, string address, IReadOnlySet<string> own, string explorerTxBase, CancellationToken ct)
    {
        var bases = EsploraUtxoExplorer.BasesFor(symbol);
        foreach (var apiBase in ExplorerHttp.HealthyFirst(bases))
        {
            var txs = await TryEsploraAsync(apiBase, address, own, symbol, explorerTxBase, ct, bench: bases.Count > 1);
            if (txs is not null) return txs;
        }

        return [];
    }

    /// <summary>One server's answer, or null when it gave none (refused, failed, timed out).</summary>
    private static async Task<IReadOnlyList<ChainTx>?> TryEsploraAsync(
        string apiBase, string address, IReadOnlySet<string> own, string symbol, string explorerTxBase,
        CancellationToken ct, bool bench = false)
    {
        try
        {
            var url = $"{apiBase}/address/{Uri.EscapeDataString(address)}/txs";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                if (bench && ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)) ExplorerHttp.Bench(apiBase);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseEsplora(json, own, symbol, explorerTxBase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return [];
        }
        catch
        {
            if (bench) ExplorerHttp.Bench(apiBase);
            return null;
        }
    }

    /// <summary>Native TRX transfers for a TRON base58 (T…) address, via Trongrid.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTronNativeAsync(
        string address, int limit = HistoryPage.Tron, CancellationToken ct = default)
    {
        string meHex;
        try { meHex = TronBase58ToHex(address); } catch { return []; }
        try
        {
            var url = $"https://api.trongrid.io/v1/accounts/{Uri.EscapeDataString(address)}" +
                      $"/transactions?limit={limit}&only_confirmed=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTronNative(json, meHex);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Confirmed Bitcoin transactions, from the same Esplora server the balance is read from (the
    /// user's choice, or mempool.space).</summary>
    public Task<IReadOnlyList<ChainTx>> GetBitcoinAsync(string address, CancellationToken ct = default) =>
        GetBitcoinAsync(address, new HashSet<string>(StringComparer.Ordinal) { address }, ct);

    public Task<IReadOnlyList<ChainTx>> GetBitcoinAsync(string address, IReadOnlySet<string> own, CancellationToken ct = default) =>
        GetEsploraAnyAsync("BTC", address, own, "https://mempool.space/tx/", ct);

    /// <summary>Confirmed Litecoin transactions, via litecoinspace (same Esplora API).</summary>
    public Task<IReadOnlyList<ChainTx>> GetLitecoinAsync(string address, CancellationToken ct = default) =>
        GetLitecoinAsync(address, new HashSet<string>(StringComparer.Ordinal) { address }, ct);

    public Task<IReadOnlyList<ChainTx>> GetLitecoinAsync(string address, IReadOnlySet<string> own, CancellationToken ct = default) =>
        GetEsploraAnyAsync("LTC", address, own, "https://litecoinspace.org/tx/", ct);

    /// <summary>
    /// Bitcoin Cash transactions, via Haskoin's keyless <c>transactions/full</c> API — the same explorer
    /// the wallet already uses for BCH balances and UTXOs (Blockchair's free tier IP-blacklists a busy
    /// caller). Haskoin takes the bare CashAddr, so the "bitcoincash:" scheme is stripped from the query;
    /// addresses in the response carry the scheme, and the parser compares on the scheme-stripped form.
    /// </summary>
    public Task<IReadOnlyList<ChainTx>> GetBitcoinCashAsync(
        string address, int limit = 50, CancellationToken ct = default) =>
        GetBitcoinCashAsync(address, new HashSet<string>(StringComparer.Ordinal) { address }, limit, ct);

    public async Task<IReadOnlyList<ChainTx>> GetBitcoinCashAsync(
        string address, IReadOnlySet<string> own, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var bare = StripCashScheme(address);
            var url = $"https://api.haskoin.com/bch/address/{Uri.EscapeDataString(bare)}" +
                      $"/transactions/full?limit={limit}";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseHaskoinFull(json, own, "BCH", "https://blockchair.com/bitcoin-cash/transaction/");
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Native ETH transfers, via Blockscout's keyless API (Etherscan-compatible shape) — and, when
    /// Blockscout does not answer with a list at all (down, a challenge page, "rate limit reached"),
    /// from 3xpl. An answered, empty list is an answer: nothing else is asked.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetEthereumAsync(string address, CancellationToken ct = default)
    {
        try
        {
            var url = "https://eth.blockscout.com/api?module=account&action=txlist" +
                      $"&address={Uri.EscapeDataString(address)}&sort=desc&page=1&offset={HistoryPage.Evm}";
            using var res = await Http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                if (IsTxlist(json)) return ParseEvmTxlist(json, address, "ETH", "https://etherscan.io/tx/", 18);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // not an answer: 3xpl below
        }

        return await GetEvmEventsAsync(address, "ethereum", "ETH", "https://etherscan.io/tx/", ct);
    }

    /// <summary>
    /// True when an Etherscan-shaped answer carries a LIST of transactions, even an empty one. A refusal
    /// comes in the same envelope with a sentence where the list should be.
    /// </summary>
    public static bool IsTxlist(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The other EVM networks with a keyless, Etherscan-shaped history API, and the explorer each one's
    /// sends are linked to (what the Send screen stores, so a payment made here is one row). Checked
    /// 2026-10-08: Blockscout's Base, Arbitrum and Polygon instances answer with a Cloudflare challenge,
    /// Routescan serves only Ethereum and Avalanche, and the Etherscan family and Ankr need an API key —
    /// those networks are read from 3xpl instead (<see cref="EvmSideEvents"/>).
    /// </summary>
    public static readonly IReadOnlyList<(string Network, string Api, string Asset, string Explorer)> EvmSideHistory =
    [
        // optimism.blockscout.com answers 301 to explorer.optimism.io since October 2026: asked directly,
        // so the request is not made twice and the privacy page names the host that really answers.
        ("Optimism", "https://explorer.optimism.io/api", "ETH", "https://optimistic.etherscan.io/tx/"),
        ("zkSync Era", "https://zksync.blockscout.com/api", "ETH", "https://explorer.zksync.io/tx/"),
        ("Avalanche", "https://api.routescan.io/v2/network/mainnet/evm/43114/etherscan/api", "AVAX", "https://snowtrace.io/tx/"),
    ];

    /// <summary>Native transfers on one of <see cref="EvmSideHistory"/>'s networks.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetEvmAsync(
        string address, string api, string asset, string explorer, CancellationToken ct = default)
    {
        try
        {
            var url = $"{api}?module=account&action=txlist&address={Uri.EscapeDataString(address)}&sort=desc&page=1&offset={HistoryPage.Evm}";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseEvmTxlist(await res.Content.ReadAsStringAsync(ct), address, asset, explorer, 18);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The EVM networks no Etherscan-shaped indexer serves without a key, read from 3xpl's keyless API
    /// instead — the source Zcash's history already falls back to. Checked 2026-10-10 against a public
    /// address: Base, Arbitrum One, Linea, BNB Chain and Polygon answer with real transfers. Fantom
    /// answers with nothing for an address that is busy there, and Cronos is not a chain it knows —
    /// those two stay without history, and the Activity screen says so.
    ///
    /// The second column is 3xpl's own name for the chain; the explorer is the one a send made here is
    /// linked to, so the two are one row.
    /// </summary>
    public static readonly IReadOnlyList<(string Network, string Chain, string Asset, string Explorer)> EvmSideEvents =
    [
        ("Base", "base", "ETH", "https://basescan.org/tx/"),
        ("Arbitrum", "arbitrum-one", "ETH", "https://arbiscan.io/tx/"),
        ("Linea", "linea", "ETH", "https://lineascan.build/tx/"),
        ("BSC", "bnb", "BNB", "https://bscscan.com/tx/"),
        ("Polygon", "polygon", "MATIC", "https://polygonscan.com/tx/"),
    ];

    /// <summary>True when a network read at the Ethereum address has a history source of either kind.</summary>
    public static bool HasEvmSideHistory(string network) =>
        EvmSideHistory.Any(s => s.Network.Equals(network, StringComparison.OrdinalIgnoreCase)) ||
        EvmSideEvents.Any(s => s.Network.Equals(network, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How many events one 3xpl read asks for. It answers 1, 10, 100 or 1000 for the same single
    /// request; a send is three events (the amount, the fee, the burn), so a thousand is some hundreds
    /// of transfers — a wallet's whole history on a rollup, in one call.
    /// </summary>
    public const int EvmEventsPage = 1000;

    /// <summary>Native transfers on one of <see cref="EvmSideEvents"/>'s networks.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetEvmEventsAsync(
        string address, string chain, string asset, string explorer, CancellationToken ct = default)
    {
        try
        {
            var url = $"{ThreeXpl.Root}/{chain}/address/{Uri.EscapeDataString(address)}" +
                      $"?data=events&limit={EvmEventsPage}";
            var json = await ThreeXpl.GetAsync(Http, url, ct);   // one at a time: it refuses a burst
            return json is null ? [] : ParseEvmEvents(json, chain + "-main", asset, explorer);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 3xpl's events for an address on an EVM chain: every change to the address's balance, signed, in
    /// wei. A transfer in is one event; a transfer out is the amount and, apart from it, what the
    /// network took (marked <c>f</c> for the fee, <c>b</c> for the part burnt). Rows show what MOVED, as
    /// every other chain's do, so the marked events are left out; what is left is added up per
    /// transaction, and a transaction that moved nothing (a contract call, somebody's zero-value
    /// "dusting" of the address) is no row at all.
    ///
    /// The events name the transaction, not who was on the other side of it — the row's counterparty is
    /// left empty rather than guessed.
    /// </summary>
    public static List<ChainTx> ParseEvmEvents(string json, string module, string asset, string explorerTxBase)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Object ||
            !events.TryGetProperty(module, out var main) || main.ValueKind != JsonValueKind.Array)
            return [];

        var byTx = new Dictionary<string, (System.Numerics.BigInteger Moved, long UnixMs)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var e in main.EnumerateArray())
        {
            if (e.TryGetProperty("failed", out var failed) && failed.ValueKind == JsonValueKind.True) continue;
            if (e.TryGetProperty("extra", out var extra) && extra.ValueKind != JsonValueKind.Null) continue;   // fee, burn, reward
            var hash = Str(e, "transaction");
            if (hash.Length == 0 ||
                !System.Numerics.BigInteger.TryParse(Str(e, "effect"), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var effect)) continue;
            var at = DateTimeOffset.TryParse(Str(e, "time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : 0;
            if (!byTx.TryGetValue(hash, out var current)) { order.Add(hash); current = (System.Numerics.BigInteger.Zero, at); }
            byTx[hash] = (current.Moved + effect, Math.Max(current.UnixMs, at));
        }

        return order
            .Where(h => !byTx[h].Moved.IsZero)
            .Select(h => new ChainTx(
                byTx[h].Moved.Sign > 0 ? "Received" : "Sent", asset,
                ScaleDown(System.Numerics.BigInteger.Abs(byTx[h].Moved).ToString(CultureInfo.InvariantCulture), 18),
                "", byTx[h].UnixMs, explorerTxBase + h, h))
            .ToList();
    }

    // ---- the user's own explorer key (roadmap N.15) ---------------------------------------------------

    /// <summary>Etherscan's multichain API: one key for every chain it serves, picked by chain id.</summary>
    public const string EtherscanV2 = "https://api.etherscan.io/v2/api";

    /// <summary>
    /// The networks read through the user's own Etherscan key — only the ones nothing answers for
    /// without one, because the key ties every request it makes to the user's account at Etherscan:
    /// native history on Fantom and Cronos, token history on BNB Chain.
    /// </summary>
    public static readonly IReadOnlyList<(string Network, int ChainId, string Asset, string Explorer)> KeyedNetworks =
    [
        ("Fantom", 250, "FTM", "https://ftmscan.com/tx/"),
        ("Cronos", 25, "CRO", "https://cronoscan.com/tx/"),
        ("BSC", 56, "BNB", "https://bscscan.com/tx/"),
    ];

    /// <summary>
    /// Native transfers on a keyed network; null when Etherscan refused (a chain its free tier does not
    /// serve, a key it does not know) — which is not the same as an answer with nothing in it.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>?> GetKeyedNativeAsync(string apiKey, string network, string address, CancellationToken ct = default)
    {
        var source = KeyedNetworks.FirstOrDefault(n => n.Network.Equals(network, StringComparison.OrdinalIgnoreCase));
        if (source.Network is null) return null;
        var json = await EtherscanAsync(apiKey, source.ChainId, "txlist", address, ct);
        return json is null ? null : ParseEvmTxlist(json, address, source.Asset, source.Explorer, 18);
    }

    /// <summary>Token transfers on a keyed network, for the given contracts only; null when refused.</summary>
    public async Task<IReadOnlyList<ChainTx>?> GetKeyedTokensAsync(
        string apiKey, string network, string address, IReadOnlyList<HeldToken> tokens, CancellationToken ct = default)
    {
        var source = KeyedNetworks.FirstOrDefault(n => n.Network.Equals(network, StringComparison.OrdinalIgnoreCase));
        if (source.Network is null || tokens.Count == 0) return null;
        var json = await EtherscanAsync(apiKey, source.ChainId, "tokentx", address, ct);
        return json is null ? null : ParseEvmTokenTx(json, address, tokens, source.Explorer);
    }

    /// <summary>
    /// What Etherscan answers a key with: null when it works, otherwise its own words ("Invalid API
    /// Key", or that its free tier does not serve a chain). Asks for the newest block number — no address.
    /// </summary>
    public async Task<string?> CheckEtherscanKeyAsync(string apiKey, int chainId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{EtherscanV2}?chainid={chainId}&module=proxy&action=eth_blockNumber&apikey={Uri.EscapeDataString(apiKey.Trim())}";
            using var res = await Http.GetAsync(url, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String &&
                result.GetString() is { } r && r.StartsWith("0x", StringComparison.Ordinal))
                return null;
            return root.TryGetProperty("result", out var why) && why.ValueKind == JsonValueKind.String
                ? why.GetString()
                : "No answer";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task<string?> EtherscanAsync(string apiKey, int chainId, string action, string address, CancellationToken ct)
    {
        try
        {
            var url = $"{EtherscanV2}?chainid={chainId}&module=account&action={action}" +
                      $"&address={Uri.EscapeDataString(address)}&sort=desc&page=1&offset={HistoryPage.Evm}" +
                      $"&apikey={Uri.EscapeDataString(apiKey.Trim())}";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync(ct);
            return IsTxlist(json) ? json : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A token this wallet shows, as its history is read: only these contracts' transfers become rows.</summary>
    public sealed record HeldToken(string Contract, string Symbol, int Decimals);

    /// <summary>
    /// Where each EVM network's token transfers are read (roadmap N.15), by the network name the Holdings
    /// rows carry: an Etherscan-shaped <c>tokentx</c> API where one answers without a key, otherwise
    /// 3xpl's ERC-20 module — Ethereum has both, 3xpl as the fallback. Checked 2026-10-16: BNB Chain has
    /// neither (3xpl keeps no BEP-20 module for it, BscScan wants a key), so the Activity screen names its
    /// tokens as not read rather than showing them with no history.
    /// </summary>
    public static readonly IReadOnlyList<(string Network, string? Api, string? Chain, string Explorer)> EvmTokenHistory =
    [
        ("Ethereum", "https://eth.blockscout.com/api", "ethereum", "https://etherscan.io/tx/"),
        ("Optimism", "https://explorer.optimism.io/api", null, "https://optimistic.etherscan.io/tx/"),
        ("Avalanche", "https://api.routescan.io/v2/network/mainnet/evm/43114/etherscan/api", null, "https://snowtrace.io/tx/"),
        ("Arbitrum One", null, "arbitrum-one", "https://arbiscan.io/tx/"),
        ("Base", null, "base", "https://basescan.org/tx/"),
        ("Polygon", null, "polygon", "https://polygonscan.com/tx/"),
    ];

    /// <summary>True when a network's token transfers have a source.</summary>
    public static bool HasEvmTokenHistory(string network) =>
        EvmTokenHistory.Any(s => s.Network.Equals(network, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Transfers of <paramref name="tokens"/> at <paramref name="address"/> on one network. Only the
    /// contracts given count: an airdropped "USDT" on another contract — the look-alike that address
    /// poisoning sends — never becomes a row, whatever it calls itself.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetEvmTokenHistoryAsync(
        string address, string network, IReadOnlyList<HeldToken> tokens, CancellationToken ct = default)
    {
        var source = EvmTokenHistory.FirstOrDefault(s => s.Network.Equals(network, StringComparison.OrdinalIgnoreCase));
        if (source.Network is null || tokens.Count == 0) return [];

        if (source.Api is not null)
        {
            try
            {
                var url = $"{source.Api}?module=account&action=tokentx&address={Uri.EscapeDataString(address)}" +
                          $"&sort=desc&page=1&offset={HistoryPage.Evm}";
                using var res = await Http.GetAsync(url, ct);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(ct);
                    if (IsTxlist(json)) return ParseEvmTokenTx(json, address, tokens, source.Explorer);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // not an answer: 3xpl below, where there is one
            }
        }

        if (source.Chain is null) return [];
        try
        {
            var module = source.Chain + "-erc-20";
            var url = $"{ThreeXpl.Root}/{source.Chain}/address/{Uri.EscapeDataString(address)}" +
                      $"?data=events&from={module}&limit={EvmEventsPage}";
            var json = await ThreeXpl.GetAsync(Http, url, ct);
            return json is null ? [] : ParseEvmTokenEvents(json, module, tokens, source.Explorer);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// An Etherscan-shaped <c>tokentx</c> list: one entry per token transfer, with its contract, both
    /// sides and the raw amount. The decimals come from the wallet's own row, not the answer — a wrong
    /// decimals value is an amount wrong by powers of ten. A transfer to oneself moves nothing and is no row.
    /// </summary>
    public static List<ChainTx> ParseEvmTokenTx(string json, string me, IReadOnlyList<HeldToken> tokens, string explorerTxBase)
    {
        var known = Known(tokens);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) return [];

        var moves = new Dictionary<(string Hash, string Contract), (System.Numerics.BigInteger Moved, long UnixMs, string Counter)>();
        var order = new List<(string, string)>();
        foreach (var tx in result.EnumerateArray())
        {
            var contract = Str(tx, "contractAddress").ToLowerInvariant();
            if (!known.ContainsKey(contract)) continue;
            var hash = Str(tx, "hash");
            if (hash.Length == 0 ||
                !System.Numerics.BigInteger.TryParse(Str(tx, "value"), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) continue;
            var incoming = Str(tx, "to").Equals(me, StringComparison.OrdinalIgnoreCase);
            var outgoing = Str(tx, "from").Equals(me, StringComparison.OrdinalIgnoreCase);
            if (incoming == outgoing) continue;   // to oneself, or not this address at all
            var signed = incoming ? value : -value;
            var key = (hash, contract);
            if (!moves.TryGetValue(key, out var m))
            {
                order.Add(key);
                m = (System.Numerics.BigInteger.Zero, Long(tx, "timeStamp") * 1000, incoming ? Str(tx, "from") : Str(tx, "to"));
            }
            moves[key] = (m.Moved + signed, m.UnixMs, m.Counter);
        }

        return Rows(order, moves, known, explorerTxBase);
    }

    /// <summary>
    /// 3xpl's ERC-20 events for an address: each a signed change in one token (its contract in
    /// <c>currency</c>, after the module's name), summed per transaction and token. The events name no
    /// other side, so the row's counterparty is left empty rather than guessed.
    /// </summary>
    public static List<ChainTx> ParseEvmTokenEvents(string json, string module, IReadOnlyList<HeldToken> tokens, string explorerTxBase)
    {
        var known = Known(tokens);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Object ||
            !events.TryGetProperty(module, out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        var prefix = module + "/";
        var moves = new Dictionary<(string Hash, string Contract), (System.Numerics.BigInteger Moved, long UnixMs, string Counter)>();
        var order = new List<(string, string)>();
        foreach (var e in list.EnumerateArray())
        {
            if (e.TryGetProperty("failed", out var failed) && failed.ValueKind == JsonValueKind.True) continue;
            var currency = Str(e, "currency");
            if (!currency.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var contract = currency[prefix.Length..].ToLowerInvariant();
            if (!known.ContainsKey(contract)) continue;
            var hash = Str(e, "transaction");
            if (hash.Length == 0 ||
                !System.Numerics.BigInteger.TryParse(Str(e, "effect"), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var effect)) continue;
            var at = DateTimeOffset.TryParse(Str(e, "time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : 0;
            var key = (hash, contract);
            if (!moves.TryGetValue(key, out var m)) { order.Add(key); m = (System.Numerics.BigInteger.Zero, at, ""); }
            moves[key] = (m.Moved + effect, Math.Max(m.UnixMs, at), m.Counter);
        }

        return Rows(order, moves, known, explorerTxBase);
    }

    private static Dictionary<string, HeldToken> Known(IReadOnlyList<HeldToken> tokens) =>
        tokens.Where(t => t.Contract.Length > 0)
            .GroupBy(t => t.Contract.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

    private static List<ChainTx> Rows(
        List<(string Hash, string Contract)> order,
        Dictionary<(string Hash, string Contract), (System.Numerics.BigInteger Moved, long UnixMs, string Counter)> moves,
        Dictionary<string, HeldToken> known, string explorerTxBase) =>
        order
            .Where(k => !moves[k].Moved.IsZero)
            .Select(k =>
            {
                var (moved, at, counter) = moves[k];
                var token = known[k.Contract];
                return new ChainTx(
                    moved.Sign > 0 ? "Received" : "Sent", token.Symbol,
                    ScaleDown(System.Numerics.BigInteger.Abs(moved).ToString(CultureInfo.InvariantCulture), token.Decimals),
                    counter, at, explorerTxBase + k.Hash, k.Hash);
            })
            .ToList();

    /// <summary>Native TON transfers for a user-friendly (UQ/EQ) address, via toncenter's keyless API.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetTonAsync(string address, int limit = HistoryPage.Ton, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://toncenter.com/api/v2/getTransactions?address={Uri.EscapeDataString(address)}" +
                      $"&limit={limit}&archival=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return [];
            var json = await res.Content.ReadAsStringAsync(ct);
            return ParseTon(json, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Native ADA transfers for a bech32 (addr1…) address, via Koios's keyless API (two calls:
    /// recent tx hashes, then their full input/output sets).</summary>
    public async Task<IReadOnlyList<ChainTx>> GetCardanoAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            using var body1 = new System.Net.Http.StringContent(
                $"{{\"_addresses\":[\"{address}\"]}}", System.Text.Encoding.UTF8, "application/json");
            using var res1 = await Http.PostAsync("https://api.koios.rest/api/v1/address_txs", body1, ct);
            if (!res1.IsSuccessStatusCode) return [];
            var j1 = await res1.Content.ReadAsStringAsync(ct);

            var hashes = new List<string>();
            using (var d1 = JsonDocument.Parse(j1))
            {
                if (d1.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var t in d1.RootElement.EnumerateArray())
                    {
                        var h = Str(t, "tx_hash");
                        if (h.Length > 0) hashes.Add(h);
                        if (hashes.Count >= limit) break;
                    }
            }
            if (hashes.Count == 0) return [];

            var hashList = string.Join(",", hashes.Select(h => $"\"{h}\""));
            using var body2 = new System.Net.Http.StringContent(
                $"{{\"_tx_hashes\":[{hashList}]}}", System.Text.Encoding.UTF8, "application/json");
            using var res2 = await Http.PostAsync("https://api.koios.rest/api/v1/tx_info", body2, ct);
            if (!res2.IsSuccessStatusCode) return [];
            var j2 = await res2.Content.ReadAsStringAsync(ct);
            return ParseCardanoTxInfo(j2, address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>How many signatures of an address are listed in one call (the node's own maximum is 1000).</summary>
    public const int SolanaSignaturePage = 200;

    /// <summary>
    /// SOL history for an address: its signatures, newest first, and the transactions behind those that
    /// are not in <paramref name="known"/> — up to <paramref name="maxNew"/> of them in one go.
    ///
    /// It read the twelve newest from one server and nothing else, ever: an account with a hundred
    /// transfers showed twelve, and none at all whenever that one server refused the batch. Now the
    /// listed servers are tried in turn, the list goes back <see cref="SolanaSignaturePage"/> signatures,
    /// and what the wallet has already read is not fetched again — so each read reaches further back
    /// than the last (the caller keeps what was read: <c>HistoryStore</c>).
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetSolanaAsync(
        string address, IReadOnlySet<string>? known = null, int maxNew = 40, CancellationToken ct = default)
    {
        foreach (var server in Umbrella.Wallet.Core.Safety.ChainEndpoints.Candidates("SOL", "https://api.mainnet-beta.solana.com"))
        {
            if (await TrySolanaAsync(server, address, known, maxNew, ct) is { } rows) return rows;
        }

        return [];
    }

    /// <summary>Null when this server did not give the signature list at all — the next one is asked.</summary>
    private static async Task<IReadOnlyList<ChainTx>?> TrySolanaAsync(
        string server, string address, IReadOnlySet<string>? known, int maxNew, CancellationToken ct)
    {
        List<string> wanted;
        try
        {
            var sigReq = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getSignaturesForAddress\",\"params\":[\"" +
                         address + "\",{\"limit\":" + SolanaSignaturePage + "}]}";
            using var body = new System.Net.Http.StringContent(sigReq, System.Text.Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync(server, body, ct);
            if (!res.IsSuccessStatusCode) return null;
            if (SolanaSignaturesToRead(await res.Content.ReadAsStringAsync(ct), known, maxNew) is not { } listed) return null;
            wanted = listed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }

        // Ten at a time: public nodes turn away a large batch whole, and a small one that fails costs
        // only its own ten. What was read before a failure is kept.
        var rows = new List<ChainTx>();
        foreach (var chunk in wanted.Chunk(10))
        {
            try
            {
                var reqs = string.Join(",", chunk.Select((sg, i) =>
                    "{\"jsonrpc\":\"2.0\",\"id\":" + i + ",\"method\":\"getTransaction\",\"params\":[\"" + sg +
                    "\",{\"encoding\":\"json\",\"maxSupportedTransactionVersion\":0}]}"));
                using var body = new System.Net.Http.StringContent("[" + reqs + "]", System.Text.Encoding.UTF8, "application/json");
                using var res = await Http.PostAsync(server, body, ct);
                if (!res.IsSuccessStatusCode) break;
                rows.AddRange(ParseSolanaTransactions(await res.Content.ReadAsStringAsync(ct), address));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                break;
            }
        }

        return rows;
    }

    /// <summary>
    /// From a <c>getSignaturesForAddress</c> answer: the signatures worth fetching — successful ones the
    /// wallet has not read yet, newest first, at most <paramref name="maxNew"/>. Null when the answer is
    /// not a signature list (an error, a rate-limit page), which is not the same as an empty history.
    /// </summary>
    public static List<string>? SolanaSignaturesToRead(string json, IReadOnlySet<string>? known, int maxNew)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return null;

        var wanted = new List<string>();
        foreach (var entry in result.EnumerateArray())
        {
            if (wanted.Count >= maxNew) break;
            // A transaction that failed moved nothing but the fee; it is not a transfer to list.
            if (entry.TryGetProperty("err", out var err) && err.ValueKind != JsonValueKind.Null) continue;
            var signature = Str(entry, "signature");
            if (signature.Length == 0 || known?.Contains(signature) == true) continue;
            wanted.Add(signature);
        }

        return wanted;
    }

    // ---- Parsers (static + string-in, so they can be unit-tested without the network) ----

    /// <summary>
    /// Parses a batched Solana <c>getTransaction</c> response into normalized SOL rows by the net change
    /// to the address's own lamport balance (pre → post). For the fee-payer (index 0) the network fee is
    /// backed out of a send so the shown amount is what actually left. Lamports are 1e9.
    /// </summary>
    public static List<ChainTx> ParseSolanaTransactions(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : new[] { root };

        foreach (var item in items)
        {
            if (!item.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) continue;
            if (!result.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object) continue;
            if (meta.TryGetProperty("err", out var errEl) && errEl.ValueKind != JsonValueKind.Null) continue; // failed tx

            if (!result.TryGetProperty("transaction", out var txn) ||
                !txn.TryGetProperty("message", out var msg) ||
                !msg.TryGetProperty("accountKeys", out var keys) || keys.ValueKind != JsonValueKind.Array) continue;

            var idx = -1;
            var k = 0;
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String && key.GetString() == me) { idx = k; break; }
                k++;
            }
            if (idx < 0) continue;

            var pre = meta.TryGetProperty("preBalances", out var pb) ? LamportsAt(pb, idx) : 0;
            var post = meta.TryGetProperty("postBalances", out var pob) ? LamportsAt(pob, idx) : 0;
            var fee = Long(meta, "fee");
            var delta = post - pre;
            if (delta == 0) continue;

            var incoming = delta > 0;
            var lamports = incoming ? delta : (-delta - (idx == 0 ? fee : 0));
            if (lamports <= 0) continue;

            var amount = ScaleDown(lamports.ToString(CultureInfo.InvariantCulture), 9);
            var ts = Long(result, "blockTime") * 1000;
            var sig = txn.TryGetProperty("signatures", out var sgs) && sgs.ValueKind == JsonValueKind.Array &&
                      sgs.GetArrayLength() > 0 ? sgs[0].GetString() ?? "" : "";

            // The other side: the account whose balance moved the most the opposite way — whoever was
            // paid by a send, whoever paid for a receipt. (The row used to name nobody.)
            var other = "";
            long otherMove = 0;
            var at = 0;
            foreach (var key in keys.EnumerateArray())
            {
                var move = (meta.TryGetProperty("postBalances", out var pob2) ? LamportsAt(pob2, at) : 0) -
                           (meta.TryGetProperty("preBalances", out var pb2) ? LamportsAt(pb2, at) : 0);
                var opposite = incoming ? -move : move;
                if (at != idx && opposite > otherMove && key.ValueKind == JsonValueKind.String)
                {
                    otherMove = opposite;
                    other = key.GetString() ?? "";
                }
                at++;
            }

            outList.Add(new ChainTx(incoming ? "Received" : "Sent", "SOL", amount, other, ts,
                $"https://solscan.io/tx/{sig}", sig));
        }
        return outList;
    }

    private static long LamportsAt(JsonElement arr, int idx)
    {
        if (arr.ValueKind != JsonValueKind.Array) return 0;
        var i = 0;
        foreach (var e in arr.EnumerateArray())
        {
            if (i == idx) return e.TryGetInt64(out var v) ? v : 0;
            i++;
        }
        return 0;
    }

    /// <summary>
    /// Parses a Koios <c>tx_info</c> payload into normalized ADA rows, using the same net-effect logic as
    /// Bitcoin: sum the address's own inputs vs outputs (lovelace, 1e6) to tell a send from a receive.
    /// </summary>
    public static List<ChainTx> ParseCardanoTxInfo(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "tx_hash");
            long inMine = 0, outMine = 0, outToOthers = 0;
            string firstOther = "";

            if (tx.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
                foreach (var i in ins.EnumerateArray())
                    if (AdaAddr(i) == me) inMine += Long(i, "value");

            if (tx.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                foreach (var o in outs.EnumerateArray())
                {
                    var addr = AdaAddr(o);
                    var val = Long(o, "value");
                    if (addr == me) outMine += val;
                    else { outToOthers += val; if (firstOther.Length == 0) firstOther = addr; }
                }

            var ts = Long(tx, "tx_timestamp") * 1000;
            var incoming = outMine - inMine >= 0;
            var shown = incoming ? outMine : outToOthers;
            var amount = ScaleDown(shown.ToString(CultureInfo.InvariantCulture), 6);
            if (amount == "0") continue;
            var counter = incoming ? "" : firstOther;
            outList.Add(new ChainTx(incoming ? "Received" : "Sent", "ADA", amount, counter, ts,
                $"https://cardanoscan.io/transaction/{hash}", hash));
        }
        return outList;
    }

    private static string AdaAddr(JsonElement e) =>
        e.TryGetProperty("payment_addr", out var pa) && pa.TryGetProperty("bech32", out var b) &&
        b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";

    /// <summary>
    /// Parses a toncenter <c>getTransactions</c> payload into normalized rows. TON semantics: an
    /// incoming transfer carries the value on <c>in_msg</c> (with a real source); an outgoing one has an
    /// empty in_msg source and the transfers sit in <c>out_msgs</c>. Amounts are nanoTON (1e9).
    /// </summary>
    public static List<ChainTx> ParseTon(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var tx in result.EnumerateArray())
        {
            var ts = Long(tx, "utime") * 1000;
            var hash = tx.TryGetProperty("transaction_id", out var tid) ? Str(tid, "hash") : "";
            var explorer = $"https://tonviewer.com/transaction/{Uri.EscapeDataString(hash)}";

            // Incoming: in_msg has a non-empty source and a positive value.
            if (tx.TryGetProperty("in_msg", out var inMsg))
            {
                var src = Str(inMsg, "source");
                var val = Long(inMsg, "value");
                if (src.Length > 0 && val > 0)
                {
                    outList.Add(new ChainTx("Received", "TON", ScaleDown(val.ToString(CultureInfo.InvariantCulture), 9),
                        src, ts, explorer, hash));
                    continue; // a plain incoming tx doesn't also count as a send
                }
            }

            // Outgoing: the wallet's own external message fans out to out_msgs.
            if (tx.TryGetProperty("out_msgs", out var outs) && outs.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in outs.EnumerateArray())
                {
                    var dst = Str(m, "destination");
                    var val = Long(m, "value");
                    if (dst.Length == 0 || val <= 0) continue;
                    outList.Add(new ChainTx("Sent", "TON", ScaleDown(val.ToString(CultureInfo.InvariantCulture), 9),
                        dst, ts, explorer, hash));
                }
            }
        }
        return outList;
    }

    public static List<ChainTx> ParseTronTrc20(string json, string me)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var row in data.EnumerateArray())
        {
            var from = Str(row, "from");
            var to = Str(row, "to");
            if (from.Length == 0 && to.Length == 0) continue;

            var incoming = string.Equals(to, me, StringComparison.OrdinalIgnoreCase);
            var symbol = "TOKEN";
            var decimals = 6;
            if (row.TryGetProperty("token_info", out var ti))
            {
                symbol = Str(ti, "symbol") is { Length: > 0 } s ? s : symbol;
                if (ti.TryGetProperty("decimals", out var d) && d.TryGetInt32(out var dec)) decimals = dec;
            }

            var raw = Str(row, "value");
            var amount = ScaleDown(raw, decimals);
            var ts = Long(row, "block_timestamp");
            var hash = Str(row, "transaction_id");
            var counter = incoming ? from : to;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                $"https://tronscan.org/#/transaction/{hash}", hash));
        }
        return outList;
    }

    public static List<ChainTx> ParseBitcoin(string json, string me) =>
        ParseEsplora(json, me, "BTC", "https://blockstream.info/tx/");

    /// <summary>Parses any Esplora /address/{a}/txs payload (BTC, LTC…) into normalized rows.</summary>
    public static List<ChainTx> ParseEsplora(string json, string me, string symbol, string explorerTxBase) =>
        ParseEsplora(json, new HashSet<string>(StringComparer.Ordinal) { me }, symbol, explorerTxBase);

    /// <summary>
    /// The same, judged against the wallet's WHOLE set of addresses (roadmap P0.1, §3.2.4).
    ///
    /// Judged per address, a send from receive #0 with change back to an internal address counted the
    /// change as "sent to others" — a 0.001 BTC payment showed as 0.00999 sent — and a spend funded
    /// only by change never appeared at all. With the set, change back to the wallet is netted out and
    /// "sent" is exactly what left it.
    /// </summary>
    public static List<ChainTx> ParseEsplora(string json, IReadOnlySet<string> own, string symbol, string explorerTxBase)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "txid");

            // Sum of inputs that are mine, and outputs that are mine (in satoshis).
            long inMine = 0, outMine = 0, outTotalToOthers = 0;
            string firstOtherOut = "";
            if (tx.TryGetProperty("vin", out var vin) && vin.ValueKind == JsonValueKind.Array)
                foreach (var v in vin.EnumerateArray())
                    if (v.TryGetProperty("prevout", out var po) && own.Contains(Str(po, "scriptpubkey_address")))
                        inMine += Long(po, "value");

            if (tx.TryGetProperty("vout", out var vout) && vout.ValueKind == JsonValueKind.Array)
                foreach (var o in vout.EnumerateArray())
                {
                    var addr = Str(o, "scriptpubkey_address");
                    var val = Long(o, "value");
                    if (own.Contains(addr)) outMine += val;
                    else { outTotalToOthers += val; if (firstOtherOut.Length == 0) firstOtherOut = addr; }
                }

            var ts = 0L;
            if (tx.TryGetProperty("status", out var st) && st.TryGetProperty("block_time", out var bt) &&
                bt.TryGetInt64(out var secs)) ts = secs * 1000;

            // Net effect on us: inputs we funded are "spent"; outputs to us are "received".
            var net = outMine - inMine; // sats
            bool incoming = net >= 0;
            long shownSats = incoming ? outMine : (inMine - outMine); // received-to-us, or sent-to-others
            if (!incoming && outTotalToOthers > 0) shownSats = outTotalToOthers;
            var amount = ScaleDown(shownSats.ToString(CultureInfo.InvariantCulture), 8);
            var counter = incoming ? "" : firstOtherOut;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                explorerTxBase + hash, hash));
        }
        return outList;
    }

    /// <summary>
    /// Parses a Haskoin <c>transactions/full</c> payload into normalized rows, using the same net-effect
    /// logic as Bitcoin: sum the address's own inputs vs outputs (satoshis, 1e8) to tell a send from a
    /// receive, and for a send show the amount that actually left to others (change back to us and the fee
    /// excluded). CashAddr comparison ignores the "bitcoincash:" scheme so a scheme-carrying "me" — the
    /// form the wallet derives — still matches Haskoin's scheme-carrying addresses. A coinbase input has
    /// no address, which reads as empty and simply never matches "me".
    /// </summary>
    public static List<ChainTx> ParseHaskoinFull(string json, string me, string symbol, string explorerTxBase) =>
        ParseHaskoinFull(json, new HashSet<string>(StringComparer.Ordinal) { me }, symbol, explorerTxBase);

    /// <summary>The same, judged against the wallet's whole address set (see ParseEsplora).</summary>
    public static List<ChainTx> ParseHaskoinFull(string json, IReadOnlySet<string> own, string symbol, string explorerTxBase)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;

        var ownBare = own.Select(StripCashScheme).ToHashSet(StringComparer.Ordinal);
        foreach (var tx in doc.RootElement.EnumerateArray())
        {
            var hash = Str(tx, "txid");

            long inMine = 0, outMine = 0, outTotalToOthers = 0;
            string firstOtherOut = "";
            if (tx.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array)
                foreach (var i in ins.EnumerateArray())
                    if (ownBare.Contains(StripCashScheme(Str(i, "address"))))
                        inMine += Long(i, "value");

            if (tx.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                foreach (var o in outs.EnumerateArray())
                {
                    var addr = Str(o, "address");
                    var val = Long(o, "value");
                    if (ownBare.Contains(StripCashScheme(addr))) outMine += val;
                    else { outTotalToOthers += val; if (firstOtherOut.Length == 0) firstOtherOut = addr; }
                }

            var ts = Long(tx, "time") * 1000; // Haskoin's "time" is unix seconds (mempool: first-seen)
            var net = outMine - inMine; // sats
            bool incoming = net >= 0;
            long shownSats = incoming ? outMine : (inMine - outMine); // received-to-us, or sent-out incl. fee
            if (!incoming && outTotalToOthers > 0) shownSats = outTotalToOthers; // prefer the recipient amount
            var amount = ScaleDown(shownSats.ToString(CultureInfo.InvariantCulture), 8);
            if (amount == "0") continue;
            var counter = incoming ? "" : firstOtherOut;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts,
                explorerTxBase + hash, hash));
        }
        return outList;
    }

    /// <summary>Drops the "bitcoincash:" scheme from a CashAddr; leaves other strings unchanged.</summary>
    private static string StripCashScheme(string a) =>
        a.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase) ? a["bitcoincash:".Length..] : a;

    /// <summary>Decodes a TRON base58check (T…) address to its 21-byte hex form (41 + 20 bytes),
    /// as used inside raw transaction data. Throws on an invalid address.</summary>
    public static string TronBase58ToHex(string base58) =>
        Convert.ToHexString(NBitcoin.DataEncoders.Encoders.Base58Check.DecodeData(base58));

    /// <summary>Parses a Trongrid native /transactions payload into TRX transfer rows. <paramref name="meHex"/>
    /// is the user's own address in hex (41…), used to tell sends from receives.</summary>
    public static List<ChainTx> ParseTronNative(string json, string meHex)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("raw_data", out var raw) ||
                !raw.TryGetProperty("contract", out var contracts) ||
                contracts.ValueKind != JsonValueKind.Array) continue;

            foreach (var c in contracts.EnumerateArray())
            {
                if (Str(c, "type") != "TransferContract") continue;             // native TRX only
                if (!c.TryGetProperty("parameter", out var param) ||
                    !param.TryGetProperty("value", out var val)) continue;

                var owner = Str(val, "owner_address");
                var to = Str(val, "to_address");
                if (owner.Length == 0 && to.Length == 0) continue;

                var incoming = string.Equals(to, meHex, StringComparison.OrdinalIgnoreCase);
                var amount = ScaleDown(Long(val, "amount").ToString(CultureInfo.InvariantCulture), 6); // SUN→TRX
                if (amount == "0") continue;
                var ts = Long(row, "block_timestamp");
                var hash = Str(row, "txID");
                var counterHex = incoming ? owner : to;
                var counter = TryHexToTronBase58(counterHex);
                outList.Add(new ChainTx(
                    incoming ? "Received" : "Sent", "TRX", amount, counter, ts,
                    $"https://tronscan.org/#/transaction/{hash}", hash));
            }
        }
        return outList;
    }

    /// <summary>Best-effort hex(41…) → base58 (T…) for display; returns the hex unchanged on failure.</summary>
    private static string TryHexToTronBase58(string hex)
    {
        try
        {
            if (hex.Length == 0) return "";
            var bytes = Convert.FromHexString(hex);
            return NBitcoin.DataEncoders.Encoders.Base58Check.EncodeData(bytes);
        }
        catch
        {
            return hex;
        }
    }

    /// <summary>Parses an Etherscan/Blockscout <c>txlist</c> payload (native EVM transfers).</summary>
    public static List<ChainTx> ParseEvmTxlist(
        string json, string me, string symbol, string explorerTxBase, int decimals)
    {
        var outList = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var tx in result.EnumerateArray())
        {
            if (Str(tx, "isError") == "1") continue;              // failed tx
            var from = Str(tx, "from");
            var to = Str(tx, "to");
            if (from.Length == 0 && to.Length == 0) continue;

            var amount = ScaleDown(Str(tx, "value"), decimals);
            if (amount == "0") continue;                          // contract call, no value moved

            var incoming = string.Equals(to, me, StringComparison.OrdinalIgnoreCase);
            var ts = Long(tx, "timeStamp") * 1000;                // seconds → ms
            var hash = Str(tx, "hash");
            var counter = incoming ? from : to;
            outList.Add(new ChainTx(
                incoming ? "Received" : "Sent", symbol, amount, counter, ts, explorerTxBase + hash, hash));
        }
        return outList;
    }

    // ---- helpers ----
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static long Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String &&
            long.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }

    /// <summary>Divides an integer string by 10^decimals and trims trailing zeros, culture-invariant.</summary>
    public static string ScaleDown(string raw, int decimals)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0";
        if (!System.Numerics.BigInteger.TryParse(raw, out var value)) return "0";
        if (decimals <= 0) return value.ToString(CultureInfo.InvariantCulture);

        var divisor = System.Numerics.BigInteger.Pow(10, decimals);
        var whole = System.Numerics.BigInteger.DivRem(value, divisor, out var frac);
        if (frac.IsZero) return whole.ToString(CultureInfo.InvariantCulture);

        var fracStr = frac.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0').TrimEnd('0');
        return $"{whole.ToString(CultureInfo.InvariantCulture)}.{fracStr}";
    }
}
