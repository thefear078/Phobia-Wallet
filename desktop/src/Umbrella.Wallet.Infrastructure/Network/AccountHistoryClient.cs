using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Transaction history for account-based chains: XRP, Stellar, NEAR, Cosmos Hub and Polkadot.
///
/// XRP and Stellar are read from the SAME server the balance comes from — the one the user chose, or
/// the default — so showing history adds no new party that learns the address. A node of NEAR, Cosmos
/// or Polkadot keeps no complete per-account index, so those need an indexer, each declared on the
/// counterparty list. Each is best-effort: a server that does not answer yields an empty list.
///
/// The explorer links are exactly the ones the Send screen stores for a transaction, so a payment made
/// in this wallet and the same payment read back from the chain collapse into one row.
///
/// Cosmos Hub's ordinary public REST servers prune their transaction index: the same search for an
/// account with known sends answered with them once in nine tries across three servers, and "0" the
/// other eight. Its history therefore comes from an ARCHIVE node (CryptoCrew's, with the index from the
/// chain's start), never from a pruned one that would show "no transactions" for a reason nobody sees.
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

    // --- Cosmos Hub ----------------------------------------------------------------------------------

    /// <summary>
    /// CryptoCrew's Cosmos Hub archive node (a community-funded grant): REST with the transaction index
    /// kept from the chain's start. Ordinary public servers prune it, which reads as "no transactions".
    /// </summary>
    public const string CosmosArchive = "https://rest.cosmoshub-main.ccvalidators.com";

    /// <summary>The archive node answers about one request a second; a second one sooner gets 429.</summary>
    private static readonly TimeSpan CosmosArchiveSpacing = TimeSpan.FromMilliseconds(1_200);

    /// <summary>
    /// Recent ATOM movements of an address, newest first: what it sent and what it received. From the
    /// server the user chose for Cosmos if there is one (their choice of who learns the address), else
    /// the archive node.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetCosmosAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var root = (ChainEndpoints.OverrideFor("ATOM") ?? CosmosArchive).TrimEnd('/');
            var sent = await SearchCosmosAsync(root, $"message.sender='{address}'", limit, ct);
            await Task.Delay(CosmosArchiveSpacing, ct);
            var received = await SearchCosmosAsync(root, $"transfer.recipient='{address}'", limit, ct);

            var rows = new List<ChainTx>();
            if (sent is not null) rows.AddRange(ParseCosmos(sent, address));
            if (received is not null) rows.AddRange(ParseCosmos(received, address));
            // A transaction found by both searches is one row per direction.
            return rows.DistinctBy(r => (r.Hash, r.Kind)).OrderByDescending(r => r.UnixMs).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<string?> SearchCosmosAsync(string root, string query, int limit, CancellationToken ct)
    {
        var url = $"{root}/cosmos/tx/v1beta1/txs?query={Uri.EscapeDataString(query)}&pagination.limit={limit}&order_by=2";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var res = await Http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode != 429) return null;
            await Task.Delay(CosmosArchiveSpacing, ct);   // rate-limited: once more, a second later
        }

        return null;
    }

    /// <summary>A transfer of under 0.0001 ATOM ...</summary>
    private const long CosmosSpamDustUatom = 100;

    /// <summary>... in a transaction paying more than this many addresses is a mass "airdrop".</summary>
    private const int CosmosSpamRecipients = 10;

    /// <summary>
    /// The ATOM movements in a <c>/cosmos/tx/v1beta1/txs</c> answer, one row per transaction and
    /// direction: bank sends and multi-sends (to or from this address) and IBC transfers of ATOM out of
    /// it. Failed transactions are left out, and so is one kind of spam: a few micro-ATOM sent with a
    /// thousand others in one transaction, whose memo advertises a "claim" site — on 2026-10-08 that was
    /// most of what a real account had received. A single small transfer to this address still shows.
    /// </summary>
    public static List<ChainTx> ParseCosmos(string json, string me)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("txs", out var txs) || txs.ValueKind != JsonValueKind.Array ||
            !doc.RootElement.TryGetProperty("tx_responses", out var responses) || responses.ValueKind != JsonValueKind.Array)
            return list;

        var count = Math.Min(txs.GetArrayLength(), responses.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            var response = responses[i];
            if (Long(response, "code") != 0) continue;
            if (!txs[i].TryGetProperty("body", out var body) ||
                !body.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) continue;

            long inUatom = 0, outUatom = 0;
            string inFrom = "", outTo = "";
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            foreach (var m in messages.EnumerateArray())
            {
                switch (Str(m, "@type"))
                {
                    case "/cosmos.bank.v1beta1.MsgSend":
                    {
                        var from = Str(m, "from_address");
                        var to = Str(m, "to_address");
                        recipients.Add(to);
                        var uatom = Uatom(m, "amount");
                        if (to == me && from != me) { inUatom += uatom; if (inFrom.Length == 0) inFrom = from; }
                        else if (from == me && to != me) { outUatom += uatom; if (outTo.Length == 0) outTo = to; }
                        break;
                    }

                    case "/cosmos.bank.v1beta1.MsgMultiSend":
                    {
                        var payer = m.TryGetProperty("inputs", out var ins) && ins.ValueKind == JsonValueKind.Array && ins.GetArrayLength() > 0
                            ? Str(ins[0], "address") : "";
                        if (m.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var o in outs.EnumerateArray())
                            {
                                var to = Str(o, "address");
                                recipients.Add(to);
                                if (to == me && payer != me) { inUatom += Uatom(o, "coins"); if (inFrom.Length == 0) inFrom = payer; }
                                else if (payer == me && to != me) { outUatom += Uatom(o, "coins"); if (outTo.Length == 0) outTo = to; }
                            }
                        }

                        break;
                    }

                    case "/ibc.applications.transfer.v1.MsgTransfer":
                    {
                        if (Str(m, "sender") != me || !m.TryGetProperty("token", out var token) || Str(token, "denom") != "uatom") break;
                        if (long.TryParse(Str(token, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ibc) && ibc > 0)
                        {
                            outUatom += ibc;
                            if (outTo.Length == 0) outTo = Str(m, "receiver");
                        }

                        break;
                    }
                }
            }

            if (inUatom > 0 && inUatom < CosmosSpamDustUatom && recipients.Count > CosmosSpamRecipients) inUatom = 0;

            var hash = Str(response, "txhash");
            var ts = DateTimeOffset.TryParse(Str(response, "timestamp"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;
            var explorer = $"https://www.mintscan.io/cosmos/tx/{hash}";   // what the Send screen stores

            if (outUatom > 0) list.Add(new ChainTx("Sent", "ATOM", Atom(outUatom), outTo, ts, explorer, hash));
            if (inUatom > 0) list.Add(new ChainTx("Received", "ATOM", Atom(inUatom), inFrom, ts, explorer, hash));
        }

        return list;
    }

    private static long Uatom(JsonElement holder, string coinsProperty)
    {
        if (!holder.TryGetProperty(coinsProperty, out var coins) || coins.ValueKind != JsonValueKind.Array) return 0;
        long sum = 0;
        foreach (var c in coins.EnumerateArray())
        {
            if (Str(c, "denom") == "uatom" &&
                long.TryParse(Str(c, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                sum += n;
        }

        return sum;
    }

    private static string Atom(long uatom) =>
        (uatom / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);

    // --- Polkadot ------------------------------------------------------------------------------------

    /// <summary>Statescan (OpenSquare): keyless per-account transfer indexes for Asset Hub and the relay chain.</summary>
    public const string StatescanAssetHub = "https://ahp-api.statescan.io";
    public const string StatescanRelay = "https://polkadot-api.statescan.io";

    /// <summary>
    /// Recent DOT transfers of an account, newest first: on Asset Hub, where balances live since 2025,
    /// and on the relay chain before that. Subscan, which most wallets use, now refuses requests without
    /// an API key; a node keeps no per-account index.
    /// </summary>
    public async Task<IReadOnlyList<ChainTx>> GetPolkadotAsync(string address, int limit = 25, CancellationToken ct = default)
    {
        var rows = new List<ChainTx>();
        var who = Uri.EscapeDataString(address);
        try
        {
            using var transfers = await Http.GetAsync($"{StatescanAssetHub}/accounts/{who}/transfers?page=0&page_size={limit}", ct);
            if (transfers.IsSuccessStatusCode)
            {
                // The account's own extrinsics carry the hashes its sends were submitted under — what the
                // Send screen links to — so a payment made here and read back from the chain is one row.
                IReadOnlyDictionary<(long, long), string> hashes = new Dictionary<(long, long), string>();
                try
                {
                    using var extrinsics = await Http.GetAsync($"{StatescanAssetHub}/accounts/{who}/extrinsics?page=0&page_size={limit}", ct);
                    if (extrinsics.IsSuccessStatusCode) hashes = ParseStatescanHashes(await extrinsics.Content.ReadAsStringAsync(ct));
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Without hashes the rows still show, linked by block and position.
                }

                rows.AddRange(ParsePolkadot(await transfers.Content.ReadAsStringAsync(ct), address, hashes, "https://assethub-polkadot.subscan.io"));
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // best effort, like every history source
        }

        try
        {
            using var relay = await Http.GetAsync($"{StatescanRelay}/accounts/{who}/transfers?page=0&page_size={limit}", ct);
            if (relay.IsSuccessStatusCode)
                rows.AddRange(ParsePolkadot(await relay.Content.ReadAsStringAsync(ct), address,
                    new Dictionary<(long, long), string>(), "https://polkadot.subscan.io"));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
        }

        return rows.OrderByDescending(r => r.UnixMs).ToList();
    }

    private const decimal PlanckPerDot = 10_000_000_000m;

    /// <summary>
    /// The DOT movements in a Statescan <c>/accounts/{address}/transfers</c> page. Other assets on Asset
    /// Hub (USDT and the rest) are left out rather than shown as DOT, and so are transfers of nothing.
    /// A row links to Subscan by the extrinsic's hash when this account signed it, else by block-index.
    /// </summary>
    public static List<ChainTx> ParsePolkadot(string json, string me,
        IReadOnlyDictionary<(long Block, long Index), string> hashes, string explorerHost)
    {
        var list = new List<ChainTx>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;

        foreach (var t in items.EnumerateArray())
        {
            if (!t.TryGetProperty("isNativeAsset", out var native) || native.ValueKind != JsonValueKind.True) continue;
            var from = Str(t, "from");
            var to = Str(t, "to");
            var incoming = to == me && from != me;
            if (!incoming && !(from == me && to != me)) continue;

            if (!decimal.TryParse(Str(t, "balance"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var planck) || planck <= 0)
                continue;

            if (!t.TryGetProperty("indexer", out var indexer)) continue;
            var block = Long(indexer, "blockHeight");
            var index = Long(indexer, "extrinsicIndex");
            var ts = Long(indexer, "blockTime");

            var id = hashes.TryGetValue((block, index), out var hash) ? hash : $"{block}-{index}";
            var dot = (planck / PlanckPerDot).ToString("0.##########", CultureInfo.InvariantCulture);
            list.Add(new ChainTx(incoming ? "Received" : "Sent", "DOT", dot, incoming ? from : to, ts,
                $"{explorerHost}/extrinsic/{id}", id));
        }

        return list;
    }

    /// <summary>(block, extrinsic index) → hash, from a Statescan <c>/accounts/{address}/extrinsics</c> page.</summary>
    public static IReadOnlyDictionary<(long, long), string> ParseStatescanHashes(string json)
    {
        var map = new Dictionary<(long, long), string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return map;
        foreach (var e in items.EnumerateArray())
        {
            if (Str(e, "hash") is not { Length: > 0 } hash || !e.TryGetProperty("indexer", out var indexer)) continue;
            map[(Long(indexer, "blockHeight"), Long(indexer, "extrinsicIndex"))] = hash;
        }

        return map;
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
