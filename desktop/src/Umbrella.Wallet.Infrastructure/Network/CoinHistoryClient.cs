using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Transaction history for the coins that had none: Nano, Decred, Dogecoin and transparent Zcash.
///
/// Each is read from the server its balance already comes from (the user's chosen one where the wallet
/// lets them choose), so the history adds no new party that learns the address. Best-effort like every
/// history reader: a server that does not answer yields an empty list, never an error.
///
/// The UTXO coins are judged as a whole wallet, not address by address: a transaction's effect is what
/// it paid to the wallet's addresses minus what it spent from them, so change coming back is netted out
/// of "sent" — the same rule as Bitcoin's history.
/// </summary>
public sealed class CoinHistoryClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    // --- Nano ----------------------------------------------------------------------------------------

    /// <summary>The account's pocketed blocks, newest first, from the node its balance is read from.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetNanoAsync(string address, int count = 25, CancellationToken ct = default)
    {
        try
        {
            var root = ChainEndpoints.Resolve("XNO", "https://rpc.nano.to");
            var request = new { action = "account_history", account = address.ToLowerInvariant(), count = count.ToString(CultureInfo.InvariantCulture) };
            using var res = await Http.PostAsJsonAsync(root, request, ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseNano(await res.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The sends and receives in an <c>account_history</c> answer. Amounts are raw (10^30 per XNO).
    /// Only confirmed blocks are listed; an unconfirmed one is not money that moved yet.
    /// </summary>
    public static List<ChainTx> ParseNano(string json)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var block in history.EnumerateArray())
        {
            var type = Str(block, "type");
            if (type is not ("send" or "receive")) continue;
            if (Str(block, "confirmed") == "false") continue;
            var amount = OnChainHistoryClient.ScaleDown(Str(block, "amount"), 30);
            if (amount == "0") continue;
            var hash = Str(block, "hash");
            var seconds = Long(block, "local_timestamp");
            list.Add(new ChainTx(
                type == "receive" ? "Received" : "Sent", "XNO", amount, Str(block, "account"),
                seconds * 1000, $"https://nanolooker.com/block/{hash}", hash));
        }

        return list;
    }

    // --- Decred --------------------------------------------------------------------------------------

    /// <summary>Recent Decred transactions touching any of the wallet's addresses, from dcrdata.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetDecredAsync(
        IReadOnlyCollection<string> addresses, int count = 25, CancellationToken ct = default)
    {
        if (addresses.Count == 0) return [];
        try
        {
            var root = ChainEndpoints.Resolve("DCR", "https://dcrdata.decred.org").TrimEnd('/');
            var joined = string.Join(",", addresses.Select(Uri.EscapeDataString));
            using var res = await Http.GetAsync($"{root}/insight/api/addrs/{joined}/txs?from=0&to={count}", ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseInsight(await res.Content.ReadAsStringAsync(ct), addresses, "DCR",
                hash => $"https://dcrdata.decred.org/tx/{hash}");
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// An Insight <c>addrs/…/txs</c> page as wallet transactions: what the transaction paid to the
    /// wallet's addresses minus what it spent from them. Positive is a receive; negative is a send, its
    /// amount including the fee (what left the wallet); zero (moving coins between its own addresses
    /// for nothing) is left out.
    /// </summary>
    public static List<ChainTx> ParseInsight(
        string json, IReadOnlyCollection<string> own, string symbol, Func<string, string> explorer)
    {
        var list = new List<ChainTx>();
        var mine = new HashSet<string>(own, StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;

        foreach (var tx in items.EnumerateArray())
        {
            decimal paidIn = 0, spent = 0;
            string? from = null, to = null;
            if (tx.TryGetProperty("vin", out var vin) && vin.ValueKind == JsonValueKind.Array)
            {
                foreach (var input in vin.EnumerateArray())
                {
                    var addr = Str(input, "addr");
                    if (mine.Contains(addr)) spent += Dec(input, "value");
                    else if (addr.Length > 0) from ??= addr;
                }
            }

            if (tx.TryGetProperty("vout", out var vout) && vout.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in vout.EnumerateArray())
                {
                    var value = Dec(output, "value");
                    var addresses = output.TryGetProperty("scriptPubKey", out var spk) &&
                                    spk.TryGetProperty("addresses", out var a) && a.ValueKind == JsonValueKind.Array
                        ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                        : [];
                    if (addresses.Any(mine.Contains)) paidIn += value;
                    else if (addresses.Count > 0) to ??= addresses[0];
                }
            }

            var net = paidIn - spent;
            if (net == 0) continue;
            var hash = Str(tx, "txid");
            var seconds = Long(tx, "time");
            if (seconds == 0) seconds = Long(tx, "blocktime");
            list.Add(new ChainTx(
                net > 0 ? "Received" : "Sent", symbol, Amount(Math.Abs(net)),
                (net > 0 ? from : to) ?? "", seconds * 1000, explorer(hash), hash));
        }

        return list;
    }

    // --- Dogecoin ------------------------------------------------------------------------------------

    /// <summary>Recent Dogecoin transactions across the wallet's addresses, from BlockCypher (where the
    /// balance comes from).</summary>
    public async Task<IReadOnlyList<ChainTx>> GetDogecoinAsync(
        IReadOnlyCollection<string> addresses, int limit = 25, CancellationToken ct = default)
    {
        var pages = new List<string>();
        foreach (var address in addresses)
        {
            try
            {
                using var res = await Http.GetAsync(
                    $"https://api.blockcypher.com/v1/doge/main/addrs/{Uri.EscapeDataString(address)}?limit={limit}", ct);
                if (res.IsSuccessStatusCode) pages.Add(await res.Content.ReadAsStringAsync(ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // one address unreadable: the others still count
            }
        }

        try
        {
            return ParseBlockcypherRefs(pages, "DOGE", hash => $"https://live.blockcypher.com/doge/tx/{hash}");
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// BlockCypher <c>txrefs</c> from one or more of the wallet's addresses, netted per transaction: an
    /// output to the wallet (<c>tx_input_n</c> −1) adds, an input spent from it (<c>tx_output_n</c> −1)
    /// subtracts. Values are in the coin's smallest unit (10^-8).
    /// </summary>
    public static List<ChainTx> ParseBlockcypherRefs(IEnumerable<string> pages, string symbol, Func<string, string> explorer)
    {
        var net = new Dictionary<string, (long Value, long UnixMs)>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            using var doc = JsonDocument.Parse(page);
            foreach (var name in new[] { "txrefs", "unconfirmed_txrefs" })
            {
                if (!doc.RootElement.TryGetProperty(name, out var refs) || refs.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in refs.EnumerateArray())
                {
                    var hash = Str(r, "tx_hash");
                    if (hash.Length == 0) continue;
                    var value = Long(r, "value");
                    var signed = Long(r, "tx_input_n") == -1 ? value : -value;
                    var at = DateTimeOffset.TryParse(Str(r, "confirmed"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds()
                        : DateTimeOffset.TryParse(Str(r, "received"), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var u) ? u.ToUnixTimeMilliseconds() : 0;
                    var current = net.TryGetValue(hash, out var c) ? c : (0, at);
                    net[hash] = (current.Value + signed, Math.Max(current.UnixMs, at));
                }
            }
        }

        return net
            .Where(kv => kv.Value.Value != 0)
            .OrderByDescending(kv => kv.Value.UnixMs)
            .Select(kv => new ChainTx(
                kv.Value.Value > 0 ? "Received" : "Sent", symbol, Amount(Math.Abs(kv.Value.Value) / 100_000_000m),
                "", kv.Value.UnixMs, explorer(kv.Key), kv.Key))
            .ToList();
    }

    // --- Zcash (transparent) -------------------------------------------------------------------------

    /// <summary>Recent transparent ZEC transactions for a t-address: Blockchair, then 3xpl — the same two
    /// sources, in the same order, as its balance.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetZcashAsync(string address, CancellationToken ct = default)
    {
        try
        {
            using var res = await Http.GetAsync(
                $"https://api.blockchair.com/zcash/dashboards/address/{Uri.EscapeDataString(address)}?transaction_details=true&limit=25", ct);
            if (res.IsSuccessStatusCode)
            {
                var rows = ParseBlockchairZcash(await res.Content.ReadAsStringAsync(ct), address);
                if (rows.Count > 0) return rows;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Blockchair turns busy IPs (Tor exits among them) away: 3xpl below
        }

        try
        {
            using var res = await Http.GetAsync(
                $"https://sandbox-api.3xpl.com/zcash/address/{Uri.EscapeDataString(address)}?data=events&limit=100", ct);
            if (!res.IsSuccessStatusCode) return [];
            return Parse3xplZcash(await res.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Blockchair's address dashboard: each transaction's <c>balance_change</c> in zatoshi and
    /// its UTC time.</summary>
    public static List<ChainTx> ParseBlockchairZcash(string json, string address)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return list;
        foreach (var entry in data.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("transactions", out var txs) || txs.ValueKind != JsonValueKind.Array) continue;
            foreach (var tx in txs.EnumerateArray())
            {
                var change = Long(tx, "balance_change");
                if (change == 0) continue;
                var hash = Str(tx, "hash");
                var at = DateTime.TryParseExact(Str(tx, "time"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                    ? new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeMilliseconds() : 0;
                list.Add(new ChainTx(
                    change > 0 ? "Received" : "Sent", "ZEC", Amount(Math.Abs(change) / 100_000_000m), "",
                    at, ZcashExplorer(hash), hash));
            }
        }

        return list;
    }

    /// <summary>3xpl's events for a Zcash address: one or more signed <c>effect</c>s (zatoshi) per
    /// transaction, summed per transaction.</summary>
    public static List<ChainTx> Parse3xplZcash(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("events", out var events) ||
            !events.TryGetProperty("zcash-main", out var main) || main.ValueKind != JsonValueKind.Array)
            return [];

        var byTx = new Dictionary<string, (long Effect, long UnixMs)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var e in main.EnumerateArray())
        {
            if (e.TryGetProperty("failed", out var f) && f.ValueKind == JsonValueKind.True) continue;
            var hash = Str(e, "transaction");
            if (hash.Length == 0 || !long.TryParse(Str(e, "effect"), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var effect)) continue;
            var at = DateTimeOffset.TryParse(Str(e, "time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : 0;
            if (!byTx.TryGetValue(hash, out var current)) { order.Add(hash); current = (0, at); }
            byTx[hash] = (current.Effect + effect, Math.Max(current.UnixMs, at));
        }

        return order
            .Where(h => byTx[h].Effect != 0)
            .Select(h => new ChainTx(
                byTx[h].Effect > 0 ? "Received" : "Sent", "ZEC", Amount(Math.Abs(byTx[h].Effect) / 100_000_000m), "",
                byTx[h].UnixMs, ZcashExplorer(h), h))
            .ToList();
    }

    /// <summary>The link a ZEC send made here stores, scheme added — so the two collapse into one row.</summary>
    private static string ZcashExplorer(string hash) => $"https://blockchair.com/zcash/transaction/{hash}";

    // --- helpers -------------------------------------------------------------------------------------

    private static string Amount(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.String => p.GetString() ?? "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => p.GetRawText(),
                _ => "",
            }
            : "";

    private static long Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String &&
            long.TryParse(p.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }

    private static decimal Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        return p.ValueKind == JsonValueKind.String &&
               decimal.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }
}
