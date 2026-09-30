using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Transaction history for account-based chains that had none: XRP, Stellar and NEAR.
///
/// Each is read from the SAME server the balance comes from — the one the user chose, or the default —
/// so showing history adds no new party that learns the address. Each is best-effort: a server that
/// does not answer yields an empty list, and the Activity screen's coverage note keeps saying which
/// coins' history could not be read, rather than an empty list passing for "no transactions".
///
/// The explorer links are exactly the ones the Send screen stores for a transaction (scheme included),
/// so a payment made in this wallet and the same payment read back from the chain collapse into one row.
///
/// Cosmos Hub is NOT here, on purpose. Its public REST servers prune their transaction index: the same
/// search for an account with known sends answered with them once in nine tries across three servers,
/// and "0" the other eight — indistinguishable from an account that never moved anything. A history
/// that is usually empty for a reason nobody can see is worse than none, which the coverage note says.
/// </summary>
public sealed class AccountHistoryClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    /// <summary>Seconds between the Unix epoch and the XRP Ledger's (2000-01-01T00:00:00Z).</summary>
    private const long RippleEpoch = 946_684_800;

    // --- XRP -----------------------------------------------------------------------------------------

    /// <summary>Recent XRP payments to and from an account, newest first.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetXrpAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var root = ChainEndpoints.Resolve("XRP", "https://xrplcluster.com");
            var request = new
            {
                method = "account_tx",
                @params = new object[]
                {
                    new { account = address, limit, ledger_index_min = -1, ledger_index_max = -1, forward = false },
                },
            };
            using var res = await Http.PostAsJsonAsync(root, request, ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseXrp(await res.Content.ReadAsStringAsync(ct), address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The XRP payments in an <c>account_tx</c> answer.
    ///
    /// The amount shown is <c>delivered_amount</c> from the metadata, never the transaction's
    /// <c>Amount</c>. A "partial payment" can name a large Amount and deliver almost nothing; reading
    /// Amount is the classic way a wallet or exchange is fooled into crediting money that never arrived.
    /// Only validated, successful payments in XRP itself are listed; issued-currency payments and every
    /// other transaction type are left out rather than shown with an XRP figure they do not have.
    /// </summary>
    public static List<ChainTx> ParseXrp(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("transactions", out var txs) || txs.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var entry in txs.EnumerateArray())
        {
            if (entry.TryGetProperty("validated", out var v) && v.ValueKind == JsonValueKind.False) continue;

            // Newer servers call it tx_json (with the hash beside it); older ones call it tx.
            var tx = entry.TryGetProperty("tx_json", out var tj) ? tj : entry.TryGetProperty("tx", out var t) ? t : default;
            if (tx.ValueKind != JsonValueKind.Object || Str(tx, "TransactionType") != "Payment") continue;
            if (!entry.TryGetProperty("meta", out var meta) || Str(meta, "TransactionResult") != "tesSUCCESS") continue;

            // XRP is a string of drops; an issued currency is an object and is not this coin.
            if (!meta.TryGetProperty("delivered_amount", out var delivered) || delivered.ValueKind != JsonValueKind.String)
                continue;

            var from = Str(tx, "Account");
            var to = Str(tx, "Destination");
            var incoming = to == me;
            if (!incoming && from != me) continue;

            var hash = Str(entry, "hash");
            if (hash.Length == 0) hash = Str(tx, "hash");

            var ts = XrpCloseTime(entry, tx);

            list.Add(new ChainTx(
                incoming ? "Received" : "Sent", "XRP", OnChainHistoryClient.ScaleDown(delivered.GetString()!, 6),
                incoming ? from : to, ts, $"https://livenet.xrpl.org/transactions/{hash}", hash));
        }

        return list;
    }

    /// <summary>
    /// When the ledger holding the transaction closed, in Unix milliseconds, or 0 when the answer does not
    /// say. API v1 puts <c>date</c> (seconds since 2000) inside <c>tx</c>; API v2 and Clio put it — or
    /// <c>close_time_iso</c> — on the entry beside <c>tx_json</c>. All three are read, so a row never
    /// loses its time because the chosen server speaks the newer API.
    /// </summary>
    private static long XrpCloseTime(JsonElement entry, JsonElement tx)
    {
        var seconds = Long(tx, "date");
        if (seconds <= 0) seconds = Long(entry, "date");
        if (seconds > 0) return (seconds + RippleEpoch) * 1000;

        return DateTimeOffset.TryParse(Str(entry, "close_time_iso"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;
    }

    // --- Stellar -------------------------------------------------------------------------------------

    /// <summary>Recent native XLM payments (and the account's creation) for a G… address, newest first.</summary>
    public async Task<IReadOnlyList<ChainTx>> GetStellarAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var root = ChainEndpoints.Resolve("XLM", "https://horizon.stellar.org").TrimEnd('/');
            using var res = await Http.GetAsync(
                $"{root}/accounts/{Uri.EscapeDataString(address)}/payments?order=desc&limit={limit}", ct);
            // 404: an address the ledger does not know yet — no account, so no history.
            if (!res.IsSuccessStatusCode) return [];
            return ParseStellar(await res.Content.ReadAsStringAsync(ct), address);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The native XLM movements in a Horizon <c>/payments</c> page: payments and account creations.
    /// Other assets, path payments and failed transactions are left out.
    /// </summary>
    public static List<ChainTx> ParseStellar(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("_embedded", out var embedded) ||
            !embedded.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var r in records.EnumerateArray())
        {
            if (r.TryGetProperty("transaction_successful", out var ok) && ok.ValueKind == JsonValueKind.False) continue;

            string from, to, amount;
            switch (Str(r, "type"))
            {
                case "payment" when Str(r, "asset_type") == "native":
                    from = Str(r, "from");
                    to = Str(r, "to");
                    amount = Str(r, "amount");
                    break;
                case "create_account":
                    from = Str(r, "funder");
                    to = Str(r, "account");
                    amount = Str(r, "starting_balance");
                    break;
                default:
                    continue;
            }

            var incoming = to == me;
            if (!incoming && from != me) continue;

            var hash = Str(r, "transaction_hash");
            var ts = DateTimeOffset.TryParse(Str(r, "created_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;

            list.Add(new ChainTx(
                incoming ? "Received" : "Sent", "XLM", TrimAmount(amount),
                incoming ? from : to, ts, $"https://stellar.expert/explorer/public/tx/{hash}", hash));
        }

        return list;
    }

    // --- NEAR ----------------------------------------------------------------------------------------

    /// <summary>
    /// Recent NEAR transfers to and from an implicit account, from the NearBlocks indexer. A NEAR node
    /// keeps no per-account index, so history needs one; NearBlocks is also where the Send screen already
    /// links a transaction, so the only new thing it learns is the account (declared on the counterparty
    /// list, contacted only while the account's history is being read).
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetNearAsync(string account, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            using var res = await Http.GetAsync(
                $"https://api.nearblocks.io/v1/account/{Uri.EscapeDataString(account)}/txns-only?per_page={limit}", ct);
            if (!res.IsSuccessStatusCode) return [];
            return ParseNear(await res.Content.ReadAsStringAsync(ct), account);
        }
        catch
        {
            return [];
        }
    }

    private const decimal YoctoPerNear = 1_000_000_000_000_000_000_000_000m;

    /// <summary>
    /// The NEAR transfers in a NearBlocks <c>txns-only</c> page: TRANSFER actions of successful
    /// transactions this account signed or received. Function calls (tokens, contracts) are left out
    /// rather than shown with a NEAR amount they did not move.
    ///
    /// <para>What this does NOT list: NEAR a CONTRACT sends to the account (some exchange withdrawals,
    /// unwrapping wNEAR). Those arrive as receipts inside someone else's transaction, not as transactions
    /// of this account. The receipt-level listing would catch them, but it is dominated by gas refunds from
    /// "system"; the cleaner source is used and the gap is stated here and in the changelog.</para>
    ///
    /// <para>NearBlocks prints yoctoNEAR as JSON numbers, sometimes in exponent form. They are read from the
    /// raw text as decimals; only an amount too large for a decimal in yocto (~79,000 NEAR and up) goes
    /// through a double, which rounds it far below anything a history row shows.</para>
    /// </summary>
    public static List<ChainTx> ParseNear(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("txns", out var txns) || txns.ValueKind != JsonValueKind.Array) return list;

        foreach (var tx in txns.EnumerateArray())
        {
            if (!tx.TryGetProperty("outcomes", out var outcomes) ||
                !outcomes.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.True)
                continue;

            var from = Str(tx, "signer_account_id");
            var to = Str(tx, "receiver_account_id");
            var incoming = to == me && from != me;
            if (!incoming && from != me) continue;

            if (!tx.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) continue;
            decimal near = 0;
            foreach (var action in actions.EnumerateArray())
            {
                if (Str(action, "action") != "TRANSFER" || !action.TryGetProperty("deposit", out var deposit)) continue;
                var raw = deposit.GetRawText().Trim('"');
                if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var yocto) && yocto > 0)
                    near += yocto / YoctoPerNear;
                // Past ~79,000 NEAR the yocto figure no longer fits a decimal: scale it as a double instead
                // of dropping the row — a rounded figure in history beats a transfer that is not there.
                else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var big) && big > 0)
                    near += (decimal)(big / 1e24);
            }

            if (near <= 0) continue;

            var hash = Str(tx, "transaction_hash");
            var ns = Str(tx, "block_timestamp");
            var ts = decimal.TryParse(ns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nanos)
                ? (long)(nanos / 1_000_000m)
                : 0;

            list.Add(new ChainTx(
                incoming ? "Received" : "Sent", "NEAR", near.ToString("0.########################", CultureInfo.InvariantCulture),
                incoming ? from : to, ts, $"https://nearblocks.io/txns/{hash}", hash));
        }

        return list;
    }

    // --- helpers -------------------------------------------------------------------------------------

    /// <summary>"12.5000000" → "12.5": Horizon prints seven decimals whatever the amount.</summary>
    private static string TrimAmount(string amount) =>
        amount.Contains('.') ? amount.TrimEnd('0').TrimEnd('.') : amount;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    private static long Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String &&
            long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return 0;
    }
}
