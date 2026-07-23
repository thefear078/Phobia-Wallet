using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record ChainBalance(ChainId Chain, string Address, decimal NativeAmount, string Symbol);

/// <summary>
/// Shared, reconfigurable HTTP client for every public endpoint. All balance/price traffic goes
/// through <see cref="Shared"/>, so enabling Tor swaps one client and routes everything at once.
/// </summary>
public static class PublicHttp
{
    /// <summary>
    /// HttpClient sends no User-Agent by default, and CoinGecko answers such requests with
    /// 403 "Please add a descriptive User-Agent to your request." That turned every price
    /// lookup into an empty result, which surfaced as $0.00 balances and a dead market list.
    /// </summary>
    public const string UserAgent = "UmbrellaWallet/1.0 (desktop; non-custodial)";

    private static HttpClient _shared = Build(null);

    public static HttpClient Shared => _shared;

    /// <summary>The active SOCKS5 proxy URI (Tor), or null when going direct.</summary>
    public static string? ActiveProxy { get; private set; }

    /// <summary>
    /// Route all public requests through a SOCKS5 proxy (e.g. Tor at socks5://127.0.0.1:9050),
    /// or pass null/empty to go direct. Rebuilds the shared client.
    /// </summary>
    public static void SetProxy(string? socks5Uri)
    {
        var normalized = string.IsNullOrWhiteSpace(socks5Uri) ? null : socks5Uri.Trim();
        var old = _shared;
        _shared = Build(normalized);
        ActiveProxy = normalized;
        try { old.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>Quick TCP reachability check for a proxy host:port (does not prove it is Tor).</summary>
    public static async Task<bool> IsProxyReachableAsync(string host, int port, CancellationToken ct = default)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(2500, ct));
            return done == connect && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static HttpClient Create(int timeoutSeconds) => Shared;

    private static HttpClient Build(string? socks5Uri)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        if (!string.IsNullOrWhiteSpace(socks5Uri))
        {
            // .NET 6+ SocketsHttpHandler understands socks5:// proxies via WebProxy.
            handler.Proxy = new System.Net.WebProxy(socks5Uri);
            handler.UseProxy = true;
        }

        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return http;
    }
}

/// <summary>
/// Public-RPC / explorer balances — no API keys required.
/// </summary>
public sealed class PublicChainBalanceClient
{
    private static HttpClient Http => PublicHttp.Shared;

    private static readonly string[] EthRpcs =
    [
        "https://cloudflare-eth.com",
        "https://rpc.ankr.com/eth",
        "https://eth.drpc.org",
    ];

    public async Task<ChainBalance?> GetBalanceAsync(
        ChainId chain,
        string address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            address.StartsWith("Unlock", StringComparison.Ordinal) ||
            address.StartsWith("Adapter", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return chain switch
            {
                ChainId.Btc => await GetBtcAsync(address, cancellationToken),
                ChainId.Ltc => await GetEsploraAsync(
                    "https://litecoinspace.org/api", ChainId.Ltc, address, "LTC", 8, cancellationToken),
                ChainId.Doge => await GetBlockcypherAsync("doge", ChainId.Doge, address, "DOGE", 8, cancellationToken),
                ChainId.Eth => await GetEthAsync(address, cancellationToken),
                ChainId.Tron => await GetTronAsync(address, cancellationToken),
                ChainId.Sol => await GetSolAsync(address, cancellationToken),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ChainBalance?> GetBtcAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://blockstream.info/api/address/{Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var funded = doc.RootElement.GetProperty("chain_stats").GetProperty("funded_txo_sum").GetInt64();
        var spent = doc.RootElement.GetProperty("chain_stats").GetProperty("spent_txo_sum").GetInt64();
        var sats = funded - spent;
        return new ChainBalance(ChainId.Btc, address, sats / 100_000_000m, "BTC");
    }

    private static async Task<ChainBalance?> GetEsploraAsync(
        string apiBase, ChainId chain, string address, string symbol, int decimals, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"{apiBase}/address/{Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var funded = doc.RootElement.GetProperty("chain_stats").GetProperty("funded_txo_sum").GetInt64();
        var spent = doc.RootElement.GetProperty("chain_stats").GetProperty("spent_txo_sum").GetInt64();
        var raw = funded - spent;
        var amount = raw / (decimal)Math.Pow(10, decimals);
        return new ChainBalance(chain, address, amount, symbol);
    }

    private static async Task<ChainBalance?> GetBlockcypherAsync(
        string coin, ChainId chain, string address, string symbol, int decimals, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://api.blockcypher.com/v1/{coin}/main/addrs/{Uri.EscapeDataString(address)}/balance", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var bal = doc.RootElement.GetProperty("balance").GetInt64();
        return new ChainBalance(chain, address, bal / (decimal)Math.Pow(10, decimals), symbol);
    }

    private static async Task<ChainBalance?> GetEthAsync(string address, CancellationToken ct)
    {
        foreach (var rpc in EthRpcs)
        {
            try
            {
                var payload = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "eth_getBalance",
                    @params = new object[] { address, "latest" },
                };
                using var res = await Http.PostAsJsonAsync(rpc, payload, ct);
                if (!res.IsSuccessStatusCode) continue;
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("result", out var result)) continue;
                var hex = result.GetString();
                if (string.IsNullOrWhiteSpace(hex)) continue;
                var wei = System.Numerics.BigInteger.Parse(hex.AsSpan(2), NumberStyles.HexNumber);
                var eth = (decimal)wei / 1_000_000_000_000_000_000m;
                return new ChainBalance(ChainId.Eth, address, eth, "ETH");
            }
            catch
            {
                /* try next RPC */
            }
        }

        return null;
    }

    private static async Task<ChainBalance?> GetSolAsync(string address, CancellationToken ct)
    {
        // Solana public JSON-RPC: getBalance returns lamports (1 SOL = 1e9 lamports).
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "getBalance",
            @params = new object[] { address },
        };
        using var res = await Http.PostAsJsonAsync("https://api.mainnet-beta.solana.com", payload, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
        if (!result.TryGetProperty("value", out var value)) return null;
        var lamports = value.GetInt64();
        return new ChainBalance(ChainId.Sol, address, lamports / 1_000_000_000m, "SOL");
    }

    private static async Task<ChainBalance?> GetTronAsync(string address, CancellationToken ct)
    {
        using var res = await Http.GetAsync(
            $"https://apilist.tronscanapi.com/api/account?address={Uri.EscapeDataString(address)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("balance", out var balEl)) return null;
        var sun = balEl.GetInt64();
        return new ChainBalance(ChainId.Tron, address, sun / 1_000_000m, "TRX");
    }

    /// <summary>
    /// USDT-TRC20 balance for a TRON address (the "TRC20" a user usually means — Tether on TRON).
    /// Reads the token list from tronscan; returns null if it can't be determined.
    /// </summary>
    public async Task<decimal?> GetTronUsdtAsync(string address, CancellationToken cancellationToken = default)
    {
        const string usdtContract = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
        if (string.IsNullOrWhiteSpace(address) || !address.StartsWith('T')) return null;

        try
        {
            using var res = await Http.GetAsync(
                $"https://apilist.tronscanapi.com/api/account?address={Uri.EscapeDataString(address)}",
                cancellationToken);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            // tronscan returns "trc20token_balances": [{ tokenId, balance, tokenDecimal, tokenAbbr }]
            if (!doc.RootElement.TryGetProperty("trc20token_balances", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var token in tokens.EnumerateArray())
            {
                var id = token.TryGetProperty("tokenId", out var tid) ? tid.GetString() : null;
                var abbr = token.TryGetProperty("tokenAbbr", out var ta) ? ta.GetString() : null;
                var isUsdt = string.Equals(id, usdtContract, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(abbr, "USDT", StringComparison.OrdinalIgnoreCase);
                if (!isUsdt) continue;

                var decimals = token.TryGetProperty("tokenDecimal", out var td) ? td.GetInt32() : 6;
                var rawStr = token.TryGetProperty("balance", out var b) ? b.GetString() : null;
                if (!System.Numerics.BigInteger.TryParse(rawStr, out var raw)) continue;
                return (decimal)raw / (decimal)Math.Pow(10, decimals);
            }

            return 0m; // address exists but holds no USDT
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// USD prices from public endpoints, no API key. CoinGecko first, Binance as a fallback so a
/// single provider being down or rate-limiting does not blank the whole wallet.
/// </summary>
public sealed class PublicMarketRatesClient
{
    private static HttpClient Http => PublicHttp.Shared;

    private static readonly Dictionary<string, string> CoinIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin",
        ["ETH"] = "ethereum",
        ["LTC"] = "litecoin",
        ["DOGE"] = "dogecoin",
        ["TRX"] = "tron",
        ["SOL"] = "solana",
        ["TON"] = "the-open-network",
        ["XMR"] = "monero",
        ["ADA"] = "cardano",
        // Tether trades a cent either side of $1; quoting it beats assuming exactly 1.00.
        ["USDT"] = "tether",
    };

    /// <summary>Binance USDT pairs. XMR is absent — Binance delisted it in 2024.</summary>
    private static readonly Dictionary<string, string> BinancePairs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "BTCUSDT",
        ["ETH"] = "ETHUSDT",
        ["LTC"] = "LTCUSDT",
        ["DOGE"] = "DOGEUSDT",
        ["TRX"] = "TRXUSDT",
        ["SOL"] = "SOLUSDT",
        ["TON"] = "TONUSDT",
        ["ADA"] = "ADAUSDT",
    };

    /// <summary>Chart windows offered in the Market view.</summary>
    public static IReadOnlyList<string> ChartRanges { get; } = ["1H", "24H", "7D", "30D", "1Y"];

    /// <summary>
    /// Real price history at a resolution that matches the window.
    ///
    /// Binance klines are the primary source: they go down to one-minute candles, so a 1H chart is
    /// actually 60 real points rather than the handful of daily closes the old sparkline drew.
    /// CoinGecko is the fallback for coins with no Binance USDT pair (e.g. XMR on some regions).
    /// </summary>
    public async Task<IReadOnlyList<double>> GetPriceSeriesAsync(
        string symbol, string range, CancellationToken cancellationToken = default)
    {
        var (interval, limit, days) = range.ToUpperInvariant() switch
        {
            "1H" => ("1m", 60, "1"),
            "24H" => ("15m", 96, "1"),
            "7D" => ("2h", 84, "7"),
            "30D" => ("8h", 90, "30"),
            _ => ("1d", 365, "365"),
        };

        var pair = BinancePairs.GetValueOrDefault(symbol.ToUpperInvariant());
        if (pair is not null)
        {
            try
            {
                var url = $"https://api.binance.com/api/v3/klines?symbol={pair}&interval={interval}&limit={limit}";
                using var res = await Http.GetAsync(url, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await res.Content.ReadAsStreamAsync(cancellationToken),
                        cancellationToken: cancellationToken);

                    var candles = new List<double>();
                    foreach (var candle in doc.RootElement.EnumerateArray())
                    {
                        // [openTime, open, high, low, close, ...] — close is index 4.
                        if (candle.GetArrayLength() > 4 &&
                            double.TryParse(candle[4].GetString(), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out var close))
                        {
                            candles.Add(close);
                        }
                    }

                    if (candles.Count > 1) return candles;
                }
            }
            catch
            {
                // fall through to CoinGecko
            }
        }

        return await GetGeckoSeriesAsync(symbol, days, cancellationToken);
    }

    private async Task<IReadOnlyList<double>> GetGeckoSeriesAsync(
        string symbol, string days, CancellationToken cancellationToken)
    {
        var id = CoinIds.GetValueOrDefault(symbol.ToUpperInvariant());
        if (id is null) return Array.Empty<double>();

        try
        {
            var url = $"https://api.coingecko.com/api/v3/coins/{id}/market_chart?vs_currency=usd&days={days}";
            using var res = await Http.GetAsync(url, cancellationToken);
            if (!res.IsSuccessStatusCode) return Array.Empty<double>();

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("prices", out var prices)) return Array.Empty<double>();

            var series = new List<double>();
            foreach (var point in prices.EnumerateArray())
            {
                if (point.GetArrayLength() >= 2) series.Add(point[1].GetDouble());
            }

            return series;
        }
        catch
        {
            return Array.Empty<double>();
        }
    }

    /// <summary>
    /// 7-day USD price series for one coin (CoinGecko market_chart). Returns an empty list on any
    /// failure so the caller can just show "no chart data" rather than crash.
    /// </summary>
    public async Task<IReadOnlyList<double>> GetSparklineAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var id = CoinIds.GetValueOrDefault(symbol.ToUpperInvariant());
        if (id is null) return Array.Empty<double>();

        try
        {
            var url =
                $"https://api.coingecko.com/api/v3/coins/{id}/market_chart?vs_currency=usd&days=7&interval=daily";
            using var res = await Http.GetAsync(url, cancellationToken);
            if (!res.IsSuccessStatusCode) return Array.Empty<double>();

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("prices", out var prices)) return Array.Empty<double>();

            var series = new List<double>();
            foreach (var point in prices.EnumerateArray())
            {
                // Each entry is [timestampMs, price].
                if (point.GetArrayLength() >= 2)
                {
                    series.Add(point[1].GetDouble());
                }
            }

            return series;
        }
        catch
        {
            return Array.Empty<double>();
        }
    }

    public async Task<IReadOnlyDictionary<string, (decimal Usd, decimal Change24h)>> GetUsdPricesAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var list = symbols.Select(s => s.ToUpperInvariant()).Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<string, (decimal, decimal)>();
        }

        var fromCoinGecko = await TryCoinGeckoAsync(list, cancellationToken);
        if (fromCoinGecko.Count > 0)
        {
            return fromCoinGecko;
        }

        return await TryBinanceAsync(list, cancellationToken);
    }

    private static async Task<Dictionary<string, (decimal, decimal)>> TryCoinGeckoAsync(
        List<string> list,
        CancellationToken ct)
    {
        var empty = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        var ids = list.Select(s => CoinIds.GetValueOrDefault(s)).Where(id => id is not null).Distinct();
        var idParam = string.Join(",", ids!);
        if (string.IsNullOrEmpty(idParam)) return empty;

        try
        {
            var url =
                $"https://api.coingecko.com/api/v3/simple/price?ids={idParam}&vs_currencies=usd&include_24hr_change=true";
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return empty;

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var byId = CoinIds.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!byId.TryGetValue(prop.Name, out var symbol)) continue;
                var usd = prop.Value.TryGetProperty("usd", out var u) ? u.GetDecimal() : 0m;
                var ch = prop.Value.TryGetProperty("usd_24h_change", out var c) ? c.GetDecimal() : 0m;
                map[symbol] = (usd, ch);
            }

            return map;
        }
        catch
        {
            return empty;
        }
    }

    private static async Task<Dictionary<string, (decimal, decimal)>> TryBinanceAsync(
        List<string> list,
        CancellationToken ct)
    {
        var map = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        var pairs = list
            .Select(s => (Symbol: s, Pair: BinancePairs.GetValueOrDefault(s)))
            .Where(x => x.Pair is not null)
            .ToList();
        if (pairs.Count == 0) return map;

        try
        {
            var quoted = string.Join(",", pairs.Select(p => $"\"{p.Pair}\""));
            var url = "https://api.binance.com/api/v3/ticker/24hr?symbols=" +
                      Uri.EscapeDataString("[" + quoted + "]");

            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return map;

            using var doc = await JsonDocument.ParseAsync(
                await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var byPair = pairs.ToDictionary(p => p.Pair!, p => p.Symbol, StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var pair = row.GetProperty("symbol").GetString();
                if (pair is null || !byPair.TryGetValue(pair, out var symbol)) continue;
                var price = decimal.Parse(
                    row.GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture);
                var change = decimal.Parse(
                    row.GetProperty("priceChangePercent").GetString()!, CultureInfo.InvariantCulture);
                map[symbol] = (price, change);
            }

            return map;
        }
        catch
        {
            return map;
        }
    }
}

/// <summary>Local watch-only addresses linked from external wallets / explorers.</summary>
public sealed class WatchAddressStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WatchAddressStore(string? path = null)
    {
        _path = path ?? AppPaths.WatchAddressesFile;
    }

    public async Task<IReadOnlyList<WatchAddress>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return Array.Empty<WatchAddress>();
        await using var stream = File.OpenRead(_path);
        var rows = await JsonSerializer.DeserializeAsync<List<WatchAddress>>(stream, JsonOptions, ct);
        return rows ?? [];
    }

    public async Task SaveAsync(IEnumerable<WatchAddress> rows, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, rows.ToList(), JsonOptions, ct);
    }
}

public sealed record WatchAddress(string Chain, string Address, string Label);
