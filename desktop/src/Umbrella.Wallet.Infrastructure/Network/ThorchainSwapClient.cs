using System.Globalization;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// A ready-to-execute cross-chain swap quote from THORChain. THORChain is a decentralised,
/// non-custodial network: you send the source asset to <see cref="InboundAddress"/> with
/// <see cref="Memo"/> attached (an OP_RETURN on UTXO chains), and the network delivers
/// <see cref="ExpectedOut"/> of the target asset to your own address. No custodian ever holds the
/// funds and there is no account or API key.
///
/// All on-chain amounts THORChain returns use a fixed 1e8 base regardless of the asset's native
/// decimals, so every value here is already converted to a human decimal.
/// </summary>
public sealed record SwapQuote(
    string FromSymbol,
    string ToSymbol,
    decimal AmountIn,
    string InboundAddress,
    string Memo,
    decimal ExpectedOut,
    decimal TotalFee,
    int SlippageBps,
    int TotalBps,
    long ExpiryUnix,
    decimal RecommendedMinIn,
    decimal DustThreshold,
    int EtaSeconds,
    string? Router,
    decimal MinOut = 0m)
{
    public DateTimeOffset Expiry => DateTimeOffset.FromUnixTimeSeconds(ExpiryUnix);
    public bool IsExpired => DateTimeOffset.UtcNow >= Expiry;
    public bool BelowMinimum => AmountIn < RecommendedMinIn;
}

/// <summary>
/// Non-custodial cross-chain swaps via THORChain's public quote API. Read-only: it only fetches
/// quotes (inbound vault address + memo + expected output). The actual on-chain send is done by the
/// chain's own transaction sender, so the private key never leaves the existing signing path.
/// </summary>
public sealed class ThorchainSwapClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Swaps);

    // Public THORNode endpoints, tried in order, each sent an x-client-id header. Nine Realms, long
    // the default node, is gone (its host stopped resolving in October 2026), and so is Liquify's old
    // thornode host; THORChain's dev docs now name Liquify's gateway. THORWallet runs the other public
    // node, and the cosmos.directory proxy mirrors the same REST surface as a last resort.
    private static readonly string[] Endpoints =
    [
        "https://gateway.liquify.com/chain/thorchain_api",
        "https://thorchain-thornode-lb-1.thorwallet.org",
        "https://rest.cosmos.directory/thorchain",
    ];

    /// <summary>The nodes asked for a quote, in order (for the tests that keep the list honest).</summary>
    public static IReadOnlyList<string> QuoteEndpoints => Endpoints;

    /// <summary>Wallet symbol -> THORChain asset id. Only assets THORChain actually supports.</summary>
    private static readonly Dictionary<string, string> AssetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "BTC.BTC",
        ["ETH"] = "ETH.ETH",
        ["LTC"] = "LTC.LTC",
        ["DOGE"] = "DOGE.DOGE",
        ["BCH"] = "BCH.BCH",
        // Native L1 gas coins THORChain delivers to a 0x address (the wallet's own ETH key/address).
        // BNB is the BSC (BNB Smart Chain) coin — THORChain's old Beacon-Chain BNB.BNB is dead.
        ["AVAX"] = "AVAX.AVAX",
        ["BNB"] = "BSC.BNB",
        // ERC-20 stablecoins on Ethereum — the asset id carries the token contract. THORChain delivers
        // these to the same 0x address; the wallet already shows ERC-20 balances there. NOTE: this "USDT"
        // is ETHEREUM (ERC-20) USDT — distinct from the wallet's TRON (TRC-20) USDT send path.
        ["USDC"] = "ETH.USDC-0XA0B86991C6218B36C1D19D4A2E9EB0CE3606EB48",
        ["USDT"] = "ETH.USDT-0XDAC17F958D2EE523A2206206994597C13D831EC7",
    };

    /// <summary>Assets the wallet can send AND attach a memo to today. UTXO chains (BTC/LTC/DOGE/BCH) carry
    /// the THORChain memo as an OP_RETURN; ETH carries it as calldata of a router.depositWithExpiry call.
    /// BCH joined once its SIGHASH_FORKID send path landed — it now swaps both ways.</summary>
    public static readonly IReadOnlyList<string> SendableFrom = ["BTC", "LTC", "DOGE", "ETH", "BCH"];

    /// <summary>Assets the wallet holds a receive address for, so THORChain can deliver the output.
    /// BCH/AVAX/BNB are swap TARGETS the wallet can't (yet) swap FROM: BCH's send path is gated, and the
    /// EVM L1s (AVAX, BNB-on-BSC) would each need their own THORChain router-deposit path to be a source.
    /// As targets they're safe — THORChain delivers them to the user's own address (a CashAddr for BCH,
    /// the shared ETH 0x address for AVAX, BNB and the ERC-20 stablecoins USDC/USDT), which the wallet
    /// already derives and shows a balance for.</summary>
    public static readonly IReadOnlyList<string> ReceivableTo =
        ["BTC", "ETH", "LTC", "DOGE", "BCH", "AVAX", "BNB", "USDC", "USDT"];

    public static bool Supports(string symbol) => AssetIds.ContainsKey(symbol);

    /// <summary>
    /// The destination address in the exact form THORChain expects for a given asset. Verified against
    /// the live quote API: BCH must be the CashAddr BODY (e.g. <c>qqyx…</c>) — the wallet displays and
    /// stores it with the <c>bitcoincash:</c> URI scheme, but THORChain rejects that prefixed form and
    /// returns an empty quote, so it is stripped here. Every other asset passes through unchanged.
    /// </summary>
    public static string NormalizeDestination(string toSymbol, string destination)
    {
        if (string.IsNullOrEmpty(destination)) return destination;
        if (string.Equals(toSymbol, "BCH", StringComparison.OrdinalIgnoreCase) &&
            destination.StartsWith("bitcoincash:", StringComparison.OrdinalIgnoreCase))
        {
            return destination["bitcoincash:".Length..];
        }
        return destination;
    }

    /// <summary>
    /// Fetches a live swap quote. <paramref name="destination"/> must be the user's own receive
    /// address for <paramref name="toSymbol"/> — it is where THORChain sends the output, and it is
    /// baked into the (signed) memo, so a wrong address would send the proceeds astray.
    /// </summary>
    public async Task<(SwapQuote? Quote, string? Error)> GetQuoteAsync(
        string fromSymbol, string toSymbol, decimal amountIn, string destination,
        CancellationToken ct = default)
    {
        if (!AssetIds.TryGetValue(fromSymbol, out var fromAsset))
            return (null, $"THORChain does not support {fromSymbol}.");
        if (!AssetIds.TryGetValue(toSymbol, out var toAsset))
            return (null, $"THORChain does not support {toSymbol}.");
        if (string.Equals(fromSymbol, toSymbol, StringComparison.OrdinalIgnoreCase))
            return (null, "Choose two different assets.");
        return await GetQuoteForAssetsAsync(fromAsset, toAsset, fromSymbol, toSymbol, amountIn, destination, ct);
    }

    /// <summary>
    /// A quote between two THORChain asset ids ("TRON.USDT-TR7…", "XRP.XRP", "GAIA.ATOM"…), for the swap
    /// catalog's coins on every network THORChain trades. <paramref name="toSymbol"/> only shapes the
    /// destination (Bitcoin Cash without its URI scheme) and labels the quote.
    /// </summary>
    public async Task<(SwapQuote? Quote, string? Error)> GetQuoteForAssetsAsync(
        string fromAsset, string toAsset, string fromSymbol, string toSymbol, decimal amountIn, string destination,
        CancellationToken ct = default)
    {
        if (string.Equals(fromAsset, toAsset, StringComparison.OrdinalIgnoreCase))
            return (null, "Choose two different assets.");
        if (amountIn <= 0) return (null, "Amount must be positive.");
        if (string.IsNullOrWhiteSpace(destination))
            return (null, "No destination address for the target asset.");

        // THORChain wants the asset's canonical address form (e.g. BCH without the bitcoincash: scheme).
        destination = NormalizeDestination(toSymbol, destination);

        // THORChain amounts are always 1e8, independent of the asset's native decimals.
        var amount1e8 = (long)decimal.Truncate(amountIn * 100_000_000m);
        if (amount1e8 <= 0) return (null, "Amount is too small.");

        var query = QuoteQuery(fromAsset, toAsset, amount1e8, destination);

        string? lastError = null;
        foreach (var host in Endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, host + query);
                req.Headers.TryAddWithoutValidation("x-client-id", "umbrella-wallet");
                using var res = await Http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(body)) { lastError = "Empty response."; continue; }

                var (quote, error) = ParseQuote(body, fromSymbol, toSymbol, amountIn);
                if (quote is not null) return (quote, null);
                lastError = error;
                // A definitive THORChain error (bad pair, halted pool) shouldn't be retried on the next host.
                if (error is not null && error.StartsWith("THORChain:", StringComparison.Ordinal))
                    return (null, error);
            }
            // Our cancel stops the search; a host that timed out is just one host that did not answer.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return (null, $"Could not reach THORChain — check your connection (or Tor). {lastError}");
    }

    /// <summary>
    /// How far the price may move against the user between the quote and the swap, in basis points.
    ///
    /// THORChain puts a price limit in the memo only when asked for one: quoted without it, the memo's
    /// limit is empty and the swap runs at whatever the pool gives when the deposit is seen — after a
    /// large move, or a trade in front of it, for much less than was shown. With it, a swap that would
    /// fill below the limit is refunded instead (less the network's fees), and the screen can say the
    /// least the user will receive. 3%: room for an ordinary move during the minutes a deposit takes.
    /// </summary>
    public const int ToleranceBps = 300;

    /// <summary>The quote request, the price limit included.</summary>
    public static string QuoteQuery(string fromAsset, string toAsset, long amount1e8, string destination) =>
        $"/thorchain/quote/swap?from_asset={Uri.EscapeDataString(fromAsset)}" +
        $"&to_asset={Uri.EscapeDataString(toAsset)}" +
        $"&amount={amount1e8}" +
        $"&destination={Uri.EscapeDataString(destination)}" +
        $"&liquidity_tolerance_bps={ToleranceBps}";

    /// <summary>
    /// The least the swap delivers, read from the memo's limit field — "=:ASSET:DEST:LIMIT" or
    /// "=:ASSET:DEST:LIMIT/INTERVAL/QUANTITY", the limit in 1e8 units, sometimes written with an
    /// exponent ("1234e3"). 0 when the memo carries no limit.
    /// </summary>
    public static decimal MemoLimitOut(string memo)
    {
        var parts = (memo ?? string.Empty).Split(':');
        if (parts.Length < 4) return 0m;
        var limit = parts[3].Split('/')[0].Trim();
        if (limit.Length == 0) return 0m;
        var e = limit.IndexOfAny(['e', 'E']);
        if (e < 0)
            return decimal.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var whole) ? whole / 100_000_000m : 0m;
        if (!decimal.TryParse(limit[..e], NumberStyles.None, CultureInfo.InvariantCulture, out var mantissa) ||
            !int.TryParse(limit[(e + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var exponent) || exponent > 20)
            return 0m;
        var value = mantissa;
        for (var i = 0; i < exponent; i++) value *= 10;
        return value / 100_000_000m;
    }

    /// <summary>
    /// Parses a THORChain quote body into a <see cref="SwapQuote"/>. Network-free so it can be pinned
    /// to a real captured response. Returns (quote, null) on success, (null, error) otherwise; a
    /// "THORChain:"-prefixed error is definitive (don't retry another host).
    /// </summary>
    public static (SwapQuote? Quote, string? Error) ParseQuote(
        string body, string fromSymbol, string toSymbol, decimal amountIn)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, "Empty response.");
        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement; }
        catch { return (null, "Malformed quote."); }

        // THORChain reports problems as a non-empty "error" string (sometimes alongside a 200).
        if (root.TryGetProperty("error", out var err) && !string.IsNullOrWhiteSpace(err.GetString()))
            return (null, Humanise(err.GetString()));

        if (!root.TryGetProperty("inbound_address", out var inbound) ||
            !root.TryGetProperty("memo", out var memo) ||
            !root.TryGetProperty("expected_amount_out", out var outAmt))
        {
            return (null, "Malformed quote.");
        }

        var hasFees = root.TryGetProperty("fees", out var fees) && fees.ValueKind == JsonValueKind.Object;
        var quote = new SwapQuote(
            FromSymbol: fromSymbol.ToUpperInvariant(),
            ToSymbol: toSymbol.ToUpperInvariant(),
            AmountIn: amountIn,
            InboundAddress: inbound.GetString() ?? "",
            Memo: memo.GetString() ?? "",
            ExpectedOut: From1e8(outAmt),
            TotalFee: hasFees && fees.TryGetProperty("total", out var ft) ? From1e8(ft) : 0m,
            SlippageBps: hasFees && fees.TryGetProperty("slippage_bps", out var sb) ? sb.GetInt32() : 0,
            TotalBps: hasFees && fees.TryGetProperty("total_bps", out var tb) ? tb.GetInt32() : 0,
            ExpiryUnix: root.TryGetProperty("expiry", out var ex) ? ex.GetInt64() : 0,
            RecommendedMinIn: root.TryGetProperty("recommended_min_amount_in", out var rmin) ? From1e8(rmin) : 0m,
            DustThreshold: root.TryGetProperty("dust_threshold", out var dust) ? From1e8(dust) : 0m,
            EtaSeconds:
                (root.TryGetProperty("inbound_confirmation_seconds", out var ics) ? ics.GetInt32() : 0) +
                (root.TryGetProperty("outbound_delay_seconds", out var ods) ? ods.GetInt32() : 0),
            Router: root.TryGetProperty("router", out var rt) ? rt.GetString() : null,
            MinOut: MemoLimitOut(memo.GetString() ?? string.Empty));

        if (string.IsNullOrWhiteSpace(quote.InboundAddress) || string.IsNullOrWhiteSpace(quote.Memo))
            return (null, "THORChain returned no inbound address (the pool may be halted).");

        return (quote, null);
    }

    /// <summary>
    /// The swap on THORChain's network explorer, found by the funding transaction. THORChain keeps
    /// transaction ids upper-case and without "0x" (an ETH deposit's hash included), so the link uses
    /// that form. Nine Realms' tracker, linked here before, no longer resolves.
    /// </summary>
    public static string TrackUrl(string txId)
    {
        var id = (txId ?? string.Empty).Trim();
        if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) id = id[2..];
        return $"https://thorchain.net/tx/{Uri.EscapeDataString(id.ToUpperInvariant())}";
    }

    private static decimal From1e8(JsonElement e)
    {
        var raw = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v / 100_000_000m : 0m;
    }

    private static string Humanise(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "THORChain could not quote this swap." : $"THORChain: {error}";
}
