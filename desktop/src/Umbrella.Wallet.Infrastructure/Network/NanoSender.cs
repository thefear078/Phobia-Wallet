using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>What a Nano send will do, read from the node before anything is signed.</summary>
/// <param name="Receivable">Payments sent to this account and not pocketed yet, largest first — the ones
/// to pocket before sending, because a Nano account can only spend what its own chain has received.</param>
/// <param name="ToPocket">How many of <paramref name="Receivable"/> the send needs pocketed first.</param>
public sealed record NanoSendQuote(
    string From, string To, decimal Amount, BigInteger AmountRaw, NanoAccountState? State,
    IReadOnlyList<NanoReceivable> Receivable, int ToPocket, string Representative);

/// <summary>A send's outcome. <see cref="Unclear"/>: a block went out and the node did not say whether it
/// took it — never retried, because a new block on the same chain would conflict or pay twice.</summary>
public sealed record NanoSendResult(bool Ok, string? Hash, string? Error, bool Unclear = false);

/// <summary>
/// Sends XNO: pockets the payments the send needs (receive blocks), then publishes the send block.
///
/// <para>Every block is built, signed and given its proof of work here (<see cref="NanoBlocks"/>): the
/// hash pinned to a mainnet block, the signature to the Nano documentation's own, the work computed on
/// this computer — public nodes either charge for <c>work_generate</c> or rate-limit it, and work done
/// here asks nobody. Only the finished blocks go out, to the node the balance comes from.</para>
///
/// <para>There is no fee. A new account's first block names a representative — the one its first
/// payment's sender uses, so the choice is the network's own, not this wallet's.</para>
/// </summary>
public sealed class NanoSender
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    /// <summary>When no first payment names one: a long-running public representative.</summary>
    public const string FallbackRepresentative = "nano_1natrium1o3z5519ifou7xii8crpxpk8y65qmkih8e8bpsjri651oza8imdd";

    private static string Root => ChainEndpoints.Resolve("XNO", "https://rpc.nano.to");

    private static readonly BigInteger RawPerXno = BigInteger.Pow(10, 30);

    /// <summary>1.5 XNO → 1500000000000000000000000000000 raw, exactly (anything past 30 decimals is cut).</summary>
    public static BigInteger ToRaw(decimal xno)
    {
        if (xno <= 0) return BigInteger.Zero;
        var text = xno.ToString("0.##############################", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var whole = dot < 0 ? text : text[..dot];
        var frac = dot < 0 ? "" : text[(dot + 1)..];
        frac = frac.Length > 30 ? frac[..30] : frac.PadRight(30, '0');
        return BigInteger.Parse(whole + frac, CultureInfo.InvariantCulture);
    }

    public async Task<(NanoSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amount, CancellationToken ct = default)
    {
        var destination = NanoAccounts.Normalize(to);
        if (destination is null) return (null, "That is not a Nano address (nano_…).");
        var amountRaw = ToRaw(amount);
        if (amountRaw <= 0) return (null, "Enter an amount above zero.");

        using var info = await PostAsync(NanoBlocks.AccountInfoRequest(NanoAccounts.Normalize(from)!), ct);
        if (info is null) return (null, "The Nano node did not answer — try again in a moment.");
        var state = NanoBlocks.ParseAccountInfo(info.RootElement, out var unopened);
        if (state is null && !unopened) return (null, "The Nano node could not read this account.");

        using var pending = await PostAsync(NanoBlocks.ReceivableRequest(NanoAccounts.Normalize(from)!), ct);
        var receivable = pending is null ? [] : NanoBlocks.ParseReceivable(pending.RootElement);

        var balance = state?.Balance ?? BigInteger.Zero;
        var toPocket = 0;
        foreach (var r in receivable)
        {
            if (balance >= amountRaw) break;
            balance += r.Amount;
            toPocket++;
        }
        if (balance < amountRaw)
            return (null, $"Not enough XNO: the account holds {NanoAccounts.ToXno(balance)} XNO, received payments included.");

        var representative = state?.Representative;
        if (representative is null && receivable.Count > 0)
            representative = await RepresentativeOfAsync(receivable[0].Hash, ct);
        representative ??= FallbackRepresentative;

        return (new NanoSendQuote(NanoAccounts.Normalize(from)!, destination, amount, amountRaw, state, receivable, toPocket,
            representative), null);
    }

    /// <summary>
    /// Pockets what the send needs, then sends. <paramref name="progress"/> hears each step: "pocket"
    /// with the count, "work", "publish".
    /// </summary>
    public async Task<NanoSendResult> SendAsync(
        byte[] privateKey, NanoSendQuote quote, IProgress<(string Step, int Index, int Count)>? progress = null,
        CancellationToken ct = default)
    {
        var publicKey = NanoAccounts.PublicKey(privateKey);
        if (NanoAccounts.Address(publicKey) != quote.From)
            return new NanoSendResult(false, null, "The key does not match the account being sent from.");

        var previous = quote.State?.Frontier ?? new byte[32];
        var balance = quote.State?.Balance ?? BigInteger.Zero;

        for (var i = 0; i < quote.ToPocket; i++)
        {
            var pay = quote.Receivable[i];
            progress?.Report(("pocket", i + 1, quote.ToPocket));
            balance += pay.Amount;
            var work = await Task.Run(() => NanoBlocks.GenerateWork(
                NanoBlocks.WorkRoot(previous, publicKey), NanoBlocks.ReceiveThreshold, ct), ct);
            var block = NanoBlocks.Build(privateKey, previous, quote.Representative, balance, pay.Hash, work);
            var opening = previous.All(b => b == 0);
            var (hash, error, unclear) = await ProcessAsync(block, opening ? "open" : "receive", ct);
            if (hash is null)
                return new NanoSendResult(false, null,
                    $"Could not pocket a received payment first: {error}", unclear);
            previous = block.Hash;
        }

        progress?.Report(("work", 0, 0));
        var sendWork = await Task.Run(() => NanoBlocks.GenerateWork(previous, NanoBlocks.SendThreshold, ct), ct);
        var send = NanoBlocks.Build(privateKey, previous, quote.Representative, balance - quote.AmountRaw,
            NanoAccounts.TryDecode(quote.To)!, sendWork);

        progress?.Report(("publish", 0, 0));
        var (sent, sendError, sendUnclear) = await ProcessAsync(send, "send", ct);
        return sent is null
            ? new NanoSendResult(false, null, sendError, sendUnclear)
            : new NanoSendResult(true, sent, null);
    }

    /// <summary>
    /// Publishes a block. "Old block" means the node already has it — it went out earlier (a retry after a
    /// lost answer), which is success with this block's own hash.
    /// </summary>
    private static async Task<(string? Hash, string? Error, bool Unclear)> ProcessAsync(
        NanoStateBlock block, string subtype, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsJsonAsync(Root, NanoBlocks.ProcessRequest(block, subtype), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The block may have reached the node before the connection failed.
            return (null, $"No answer from the Nano node ({ex.Message}). Check the account before sending again.", true);
        }

        using (response)
        {
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var hash = NanoBlocks.ParseProcessed(doc.RootElement, out var error);
                if (hash is not null) return (hash, null, false);
                if (error is not null && error.Contains("Old block", StringComparison.OrdinalIgnoreCase))
                    return (Convert.ToHexString(block.Hash), null, false);
                return (null, $"The Nano node refused the block: {error}", false);
            }
            catch (JsonException)
            {
                return (null, "The Nano node's answer could not be read. Check the account before sending again.", true);
            }
        }
    }

    /// <summary>The representative named by the account that sent a block — for a new account's first block.</summary>
    private static async Task<string?> RepresentativeOfAsync(byte[] blockHash, CancellationToken ct)
    {
        using var doc = await PostAsync(new { action = "block_info", json_block = "true", hash = Convert.ToHexString(blockHash) }, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("contents", out var contents)) return null;
        var rep = contents.TryGetProperty("representative", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        return NanoAccounts.Normalize(rep);
    }

    private static async Task<JsonDocument?> PostAsync(object request, CancellationToken ct)
    {
        try
        {
            using var res = await Http.PostAsJsonAsync(Root, request, ct);
            if (!res.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
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
}
