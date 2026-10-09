using System.Net.Http.Json;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A reviewed Decred payment: the coins, the fee and the change.</summary>
public sealed record DcrSendQuote(
    string From,
    string To,
    decimal Amount,
    long AmountAtoms,
    long FeeAtoms,
    decimal FeeDcr,
    long ChangeAtoms,
    IReadOnlyList<DecredUtxo> Inputs,
    byte[] FromScript,
    byte[] ToScript,
    bool ChangeSweptToFee)
{
    public int InputCount => Inputs.Count;
}

/// <summary>How a Decred broadcast ended. <see cref="Unclear"/> is never offered as a retry.</summary>
public sealed record DcrSendResult(bool Ok, string? TxId, string? Error, bool Unclear = false);

/// <summary>
/// Decred payments from the wallet's BIP44 address (roadmap N.12).
///
/// The coins are read from dcrdata (the Decred project's explorer, or the user's chosen server, with a
/// community instance behind it); the transaction is built and signed here with Decred's own signature
/// hash (<see cref="DecredTransactions"/>, pinned to transactions the network accepted). Every input
/// repeats the amount, block height and position-in-block of the coin it spends — dcrd refuses an input
/// whose "fraud proof" disagrees with its own record — so those are read for each chosen coin from the
/// transaction that created it, and its output is checked to pay this address with that amount.
/// </summary>
public sealed class DecredTransactionSender
{
    private const string DefaultRoot = "https://dcrdata.decred.org";

    private static HttpClient ChainHttp => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient BroadcastHttp => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    /// <summary>Where a finished transaction can be looked at.</summary>
    public static string ExplorerFor(string txId) => $"https://dcrdata.decred.org/tx/{txId}";

    private static IReadOnlyList<string> Roots =>
        ChainEndpoints.Candidates("DCR", DefaultRoot).Select(r => r.TrimEnd('/')).ToList();

    /// <summary>
    /// Reads the address's coins and plans the payment. Nothing is signed here, and nothing is guessed:
    /// an explorer that cannot be read is an error, never an empty wallet.
    /// </summary>
    public async Task<(DcrSendQuote? Quote, string? Error)> PrepareAsync(
        string fromAddress, string toAddress, decimal amount, CancellationToken ct = default)
    {
        var from = fromAddress.Trim();
        var to = toAddress.Trim();

        if (DecredAddress.TryDecode(from) is not { Kind: DecredAddressKind.PublicKeyHash } fromDecoded)
            return (null, "This wallet spends from its Ds… address only.");
        var toScript = DecredTransactions.ScriptFor(to);
        if (toScript is null)
            return (null, "That is not a Decred mainnet address (Ds… or Dc…).");
        if (string.Equals(from, to, StringComparison.Ordinal))
            return (null, "That is this wallet's own address — the payment would only pay the network fee.");
        if (!DecredSendRules.TryToAtoms(amount, out var amountAtoms))
            return (null, "Enter a positive DCR amount with at most 8 decimal places.");

        var fromScript = DecredTransactions.PayToPubKeyHash(fromDecoded.Hash);
        var (coins, coinError) = await FetchCoinsAsync(from, fromScript, ct);
        if (coins is null) return (null, coinError);

        var (plan, planError) = DecredSendRules.Plan(coins, amountAtoms, toScript, fromScript);
        if (plan is null) return (null, planError);

        // The fraud proof of each chosen coin, from the transaction that made it.
        var inputs = new List<DecredUtxo>(plan.Inputs.Count);
        foreach (var coin in plan.Inputs)
        {
            var (proven, why) = await ProveAsync(coin, fromScript, ct);
            if (proven is null) return (null, why);
            inputs.Add(proven);
        }

        return (new DcrSendQuote(
            From: from,
            To: to,
            Amount: amount,
            AmountAtoms: plan.AmountAtoms,
            FeeAtoms: plan.FeeAtoms,
            FeeDcr: DecredSendRules.ToDcr(plan.FeeAtoms),
            ChangeAtoms: plan.ChangeAtoms,
            Inputs: inputs,
            FromScript: fromScript,
            ToScript: plan.ToScript,
            ChangeSweptToFee: plan.ChangeSweptToFee), null);
    }

    /// <summary>
    /// Signs the reviewed payment and broadcasts it. The id is known before the broadcast (it hashes only
    /// the prefix), so an answer that never arrives is settled against the chain instead of being called
    /// a failure the user would retry — and pay twice for.
    /// </summary>
    public async Task<DcrSendResult> SignAndBroadcastAsync(DcrSendQuote quote, Key key, CancellationToken ct = default)
    {
        // The key must be the one behind the address the user reviewed.
        if (!string.Equals(DecredAddress.FromPublicKey(key.PubKey.ToBytes()), quote.From, StringComparison.Ordinal))
            return new DcrSendResult(false, null, "This transaction does not belong to the unlocked wallet.");

        byte[] raw;
        string txId;
        try
        {
            (raw, txId) = Build(quote, key);
        }
        catch (Exception ex)
        {
            return new DcrSendResult(false, null, $"Could not build the Decred transaction: {ex.Message}");
        }

        var hex = Convert.ToHexString(raw).ToLowerInvariant();
        string? lastWords = null;

        // The same signed bytes to each server in turn until one gives a clear answer; one transaction id.
        foreach (var root in Roots)
        {
            bool httpOk;
            string body;
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(ExplorerHttp.AttemptDeadline + TimeSpan.FromSeconds(7));
                using var res = await BroadcastHttp.PostAsJsonAsync($"{root}/insight/api/tx/send", new { rawtx = hex }, attempt.Token);
                httpOk = res.IsSuccessStatusCode;
                body = (await res.Content.ReadAsStringAsync(ct)).Trim();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastWords = ex.Message;
                continue;   // gone with no answer: the next server gets the same bytes
            }

            switch (UtxoBroadcast.Classify(httpOk, body))
            {
                case UtxoBroadcastAnswer.Accepted:
                    return new DcrSendResult(true, txId, null);
                case UtxoBroadcastAnswer.Rejected:
                    return new DcrSendResult(false, null, $"Decred refused the transaction: {Trim(body)}");
                default:
                    lastWords = Trim(body);
                    break;
            }
        }

        return await SettleAsync(txId, lastWords, ct);
    }

    /// <summary>
    /// Builds and signs the reviewed payment: the wire bytes and the id they hash to. Pure — no network —
    /// so the exact bytes a send would publish are checked in a test, signatures included.
    /// </summary>
    public static (byte[] Raw, string TxId) Build(DcrSendQuote quote, Key key)
    {
        var outputs = new List<DecredTxOut> { new(quote.AmountAtoms, 0, quote.ToScript) };
        if (quote.ChangeAtoms > 0) outputs.Add(new DecredTxOut(quote.ChangeAtoms, 0, quote.FromScript));

        var unsigned = quote.Inputs.Select(u => new DecredTxIn(
            PrevHash: ReverseHex(u.TxId),
            PrevIndex: u.Index,
            Tree: u.Tree,
            Sequence: DecredTransactions.FinalSequence,
            ValueIn: u.Atoms,
            BlockHeight: u.Height,
            BlockIndex: u.BlockIndex,
            SignatureScript: [])).ToList();

        var tx = new DecredTx(DecredTransactions.TxVersion, unsigned, outputs, LockTime: 0, Expiry: 0);
        var publicKey = key.PubKey.ToBytes();

        var signed = new List<DecredTxIn>(unsigned.Count);
        for (var i = 0; i < unsigned.Count; i++)
        {
            var hash = new uint256(DecredTransactions.SignatureHash(tx, i, quote.FromScript));
            var signature = key.Sign(hash, useLowR: false);

            // Checked before anything leaves the device: a signature that does not verify here would be
            // refused there, and the user would be told something vaguer.
            if (!key.PubKey.Verify(hash, signature))
                throw new InvalidOperationException("A signature did not verify.");

            signed.Add(unsigned[i] with { SignatureScript = DecredTransactions.SignatureScript(signature.ToDER(), publicKey) });
        }

        var final = tx with { Inputs = signed };
        return (DecredTransactions.Serialize(final), DecredTransactions.TxId(final));
    }

    /// <summary>
    /// A GET of a dcrdata <paramref name="path"/> from the first server that answers it, each given
    /// <see cref="ExplorerHttp.AttemptDeadline"/>. When a server refused for rate (429) and none answered,
    /// the round is tried once more after a short pause: "not now" is not "no".
    /// </summary>
    private static async Task<(string? Body, string? Error)> GetFromServersAsync(string path, CancellationToken ct)
    {
        string? lastError = null;
        for (var round = 0; round < 2; round++)
        {
            var rateLimited = false;
            foreach (var root in Roots)
            {
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    attempt.CancelAfter(ExplorerHttp.AttemptDeadline);
                    using var res = await ChainHttp.GetAsync(root + path, attempt.Token);
                    if (res.IsSuccessStatusCode) return (await res.Content.ReadAsStringAsync(attempt.Token), null);
                    rateLimited |= (int)res.StatusCode == 429;
                    lastError = $"the explorer answered {(int)res.StatusCode}";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            if (!rateLimited) break;
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return (null, lastError);
    }

    /// <summary>The address's unspent coins paying exactly its own script, from the first server that answers.</summary>
    private static async Task<(IReadOnlyList<DecredUtxo>? Coins, string? Error)> FetchCoinsAsync(
        string address, byte[] script, CancellationToken ct)
    {
        var (body, error) = await GetFromServersAsync($"/insight/api/addr/{Uri.EscapeDataString(address)}/utxo", ct);
        if (body is null) return (null, $"Could not read this address's Decred ({error}). Nothing was sent.");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (ParseCoins(doc.RootElement, script) is { } coins) return (coins, null);
        }
        catch (JsonException)
        {
            // not JSON: the same answer as one of the wrong shape
        }

        return (null, "The explorer's list of this address's coins could not be read. Nothing was sent.");
    }

    /// <summary>
    /// The coins in an Insight <c>/addr/{address}/utxo</c> answer that pay <paramref name="script"/>; null
    /// when the answer is not that shape. A coin paying any other script — a stake output, say — is not
    /// this key's to spend this way, so it is left out rather than signed for wrongly.
    /// </summary>
    public static IReadOnlyList<DecredUtxo>? ParseCoins(JsonElement root, byte[] script)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;
        var expected = Convert.ToHexString(script);
        var coins = new List<DecredUtxo>();
        foreach (var u in root.EnumerateArray())
        {
            if (u.ValueKind != JsonValueKind.Object) continue;
            if (!u.TryGetProperty("txid", out var id) || id.GetString() is not { Length: 64 } txId) continue;
            if (!u.TryGetProperty("vout", out var v) || !v.TryGetUInt32(out var index)) continue;
            if (!u.TryGetProperty("satoshis", out var s) || !s.TryGetInt64(out var atoms) || atoms <= 0) continue;
            if (!u.TryGetProperty("scriptPubKey", out var spk) ||
                !string.Equals(spk.GetString(), expected, StringComparison.OrdinalIgnoreCase)) continue;

            // Unconfirmed coins have no height yet; the planner leaves them for later.
            var height = u.TryGetProperty("height", out var h) && h.TryGetInt64(out var hv) && hv > 0 ? (uint)hv : 0u;
            coins.Add(new DecredUtxo(txId.ToLowerInvariant(), index, atoms, height, BlockIndex: 0, Tree: 0));
        }

        return coins;
    }

    /// <summary>
    /// The block height, position-in-block and tree of a coin, read from the transaction that created it
    /// — and a check that its output really pays this address that amount, so a server that misreported
    /// the coin cannot make the wallet sign for something else.
    /// </summary>
    private static async Task<(DecredUtxo? Coin, string? Error)> ProveAsync(DecredUtxo coin, byte[] script, CancellationToken ct)
    {
        var (body, error) = await GetFromServersAsync($"/api/tx/{coin.TxId}", ct);
        if (body is null) return (null, $"Could not read where a coin came from ({error}). Nothing was sent.");

        try
        {
            using var doc = JsonDocument.Parse(body);
            // An answer that contradicts the coin is not retried elsewhere: it is reported.
            return ReadOrigin(doc.RootElement, coin, script);
        }
        catch (JsonException)
        {
            return (null, "The explorer described a coin's transaction in a way this wallet cannot read. Nothing was sent.");
        }
    }

    /// <summary>The coin with its block position filled in from a dcrdata <c>/api/tx/{id}</c> answer, or why not.</summary>
    public static (DecredUtxo? Coin, string? Error) ReadOrigin(JsonElement tx, DecredUtxo coin, byte[] script)
    {
        const string unreadable = "The explorer described a coin's transaction in a way this wallet cannot read. Nothing was sent.";
        if (tx.ValueKind != JsonValueKind.Object) return (null, unreadable);
        if (!tx.TryGetProperty("block", out var block) || block.ValueKind != JsonValueKind.Object ||
            !block.TryGetProperty("blockheight", out var bh) || !bh.TryGetInt64(out var height) || height <= 0 ||
            !block.TryGetProperty("blockindex", out var bi) || !bi.TryGetInt64(out var index) || index < 0 || index > uint.MaxValue)
            return (null, "A coin's transaction is not in a block yet. Wait for a confirmation and try again.");

        var tree = tx.TryGetProperty("tree", out var t) && t.TryGetInt32(out var tv) ? tv : -1;
        if (tree != 0)
            return (null, "One of this address's coins comes from a staking transaction, which this wallet does not spend.");

        if (!tx.TryGetProperty("vout", out var vouts) || vouts.ValueKind != JsonValueKind.Array ||
            coin.Index >= vouts.GetArrayLength()) return (null, unreadable);
        var vout = vouts[(int)coin.Index];
        if (!vout.TryGetProperty("value", out var value) || !value.TryGetDecimal(out var dcr) ||
            !vout.TryGetProperty("scriptPubKey", out var spk) || !spk.TryGetProperty("hex", out var hex))
            return (null, unreadable);

        if (decimal.Round(dcr * DecredSendRules.AtomsPerDcr) != coin.Atoms ||
            !string.Equals(hex.GetString(), Convert.ToHexString(script), StringComparison.OrdinalIgnoreCase))
            return (null, "The explorer's account of a coin contradicts itself. Nothing was signed.");

        return (coin with { Height = (uint)height, BlockIndex = (uint)index, Tree = 0 }, null);
    }

    /// <summary>
    /// Asks the servers whether the transaction is there after an answer that settled nothing. Found means
    /// sent; not found means unclear — never failed, because it may still be in a mempool.
    /// </summary>
    private static async Task<DcrSendResult> SettleAsync(string txId, string? reason, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : 5), ct);
            foreach (var root in Roots)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(ExplorerHttp.AttemptDeadline);
                    using var res = await ChainHttp.GetAsync($"{root}/api/tx/{txId}", deadline.Token);
                    if (res.IsSuccessStatusCode) return new DcrSendResult(true, txId, null);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // the next server
                }
            }
        }

        var because = reason is null ? string.Empty : $" ({reason})";
        return new DcrSendResult(false, txId,
            $"Decred gave no clear answer{because}. The payment {txId} may still be on its way — check the explorer before sending again.",
            Unclear: true);
    }

    private static string Trim(string body) =>
        body.Length == 0 ? "the explorer said nothing" : body.Length > 300 ? body[..300] : body;

    /// <summary>A transaction id as explorers print it, in the byte order an input carries it.</summary>
    private static byte[] ReverseHex(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Array.Reverse(bytes);
        return bytes;
    }
}
