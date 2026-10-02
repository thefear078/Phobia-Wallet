using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>A NEAR Intents token: its 1Click asset id and decimals.</summary>
public sealed record IntentsToken(string AssetId, int Decimals);

/// <summary>
/// A NEAR Intents quote. A dry quote (for showing the rate) has no deposit address; a real one has the
/// one-time address to pay, the memo when the chain needs one, and the deadline after which a deposit
/// is no longer swapped.
/// </summary>
public sealed record IntentsQuote(
    decimal AmountIn,
    decimal AmountOut,
    decimal MinAmountOut,
    decimal AmountInUsd,
    decimal AmountOutUsd,
    int TimeEstimateSeconds,
    string? DepositAddress,
    string? DepositMemo,
    DateTimeOffset? Deadline);

/// <summary>
/// Swaps through NEAR Intents' 1Click API: ask for a quote, pay the quote's one-time deposit address
/// with an ordinary transfer, and the solvers deliver the other coin to the wallet's own address. A
/// swap that cannot be filled is refunded to the paying address.
///
/// <para>No account and no key. Without a partner key the service adds a 0.25% platform fee, which the
/// quote already includes — the wallet shows it rather than hiding it in the rate.</para>
/// </summary>
public sealed class NearIntentsSwapClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Swaps);

    public const string Root = "https://1click.chaindefuser.com";

    /// <summary>The platform fee an unauthenticated quote carries, in basis points.</summary>
    public const int KeylessFeeBps = 25;

    private static readonly TimeSpan TokenListReuse = TimeSpan.FromHours(1);
    private static (DateTimeOffset At, Dictionary<(string, string), IntentsToken> Map)? _tokens;

    /// <summary>
    /// The asset id and decimals of a coin on a blockchain, from the live token list (cached for an hour).
    /// Asset ids are the service's own and have changed before (Toncoin became GRAM), so they are looked
    /// up, not hard-coded.
    /// </summary>
    public async Task<IntentsToken?> ResolveAsync(string blockchain, string symbol, CancellationToken ct = default)
    {
        var map = await TokensAsync(ct);
        return map is not null && map.TryGetValue((blockchain.ToLowerInvariant(), symbol.ToUpperInvariant()), out var t) ? t : null;
    }

    private static async Task<Dictionary<(string, string), IntentsToken>?> TokensAsync(CancellationToken ct)
    {
        if (_tokens is { } cached && DateTimeOffset.UtcNow - cached.At < TokenListReuse) return cached.Map;
        try
        {
            var json = await Http.GetStringAsync($"{Root}/v0/tokens", ct);
            var map = ParseTokens(json);
            if (map.Count > 0) _tokens = (DateTimeOffset.UtcNow, map);
            return map;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return _tokens?.Map;
        }
    }

    /// <summary>The token list keyed by (blockchain, SYMBOL).</summary>
    public static Dictionary<(string, string), IntentsToken> ParseTokens(string json)
    {
        var map = new Dictionary<(string, string), IntentsToken>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            var chain = Str(t, "blockchain");
            var symbol = Str(t, "symbol");
            var id = Str(t, "assetId");
            if (chain.Length == 0 || symbol.Length == 0 || id.Length == 0) continue;
            if (!t.TryGetProperty("decimals", out var d) || !d.TryGetInt32(out var decimals)) continue;
            map.TryAdd((chain.ToLowerInvariant(), symbol.ToUpperInvariant()), new IntentsToken(id, decimals));
        }
        return map;
    }

    /// <summary>
    /// A quote for <paramref name="amount"/> of <paramref name="from"/> into <paramref name="to"/>,
    /// paid from <paramref name="refundTo"/> (where a failed swap is returned) and delivered to
    /// <paramref name="recipient"/>. <paramref name="dry"/>: a rate only, no deposit address issued.
    /// </summary>
    public async Task<(IntentsQuote? Quote, string? Error)> QuoteAsync(
        IntentsToken from, IntentsToken to, decimal amount, string refundTo, string recipient, bool dry,
        bool memoDeposit, int slippageBps = 100, CancellationToken ct = default)
    {
        var smallest = ToSmallest(amount, from.Decimals);
        if (smallest <= BigInteger.Zero) return (null, "amount");

        var request = new Dictionary<string, object>
        {
            ["dry"] = dry,
            ["swapType"] = "EXACT_INPUT",
            ["slippageTolerance"] = slippageBps,
            ["originAsset"] = from.AssetId,
            ["depositType"] = "ORIGIN_CHAIN",
            ["destinationAsset"] = to.AssetId,
            ["amount"] = smallest.ToString(CultureInfo.InvariantCulture),
            ["refundTo"] = refundTo,
            ["refundType"] = "ORIGIN_CHAIN",
            ["recipient"] = recipient,
            ["recipientType"] = "DESTINATION_CHAIN",
            ["deadline"] = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        if (memoDeposit) request["depositMode"] = "MEMO";

        try
        {
            using var res = await Http.PostAsJsonAsync($"{Root}/v0/quote", request, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return ParseQuote(body, from.Decimals, to.Decimals);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>A quote answer, or the service's own reason ("message") when it declines.</summary>
    public static (IntentsQuote? Quote, string? Error) ParseQuote(string body, int fromDecimals, int toDecimals)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (!root.TryGetProperty("quote", out var q) || q.ValueKind != JsonValueKind.Object)
            return (null, Str(root, "message") is { Length: > 0 } m ? m : null);

        var amountIn = FromSmallest(Str(q, "amountIn"), fromDecimals);
        var amountOut = FromSmallest(Str(q, "amountOut"), toDecimals);
        if (amountOut <= 0) return (null, Str(root, "message") is { Length: > 0 } m2 ? m2 : null);

        var deposit = Str(q, "depositAddress");
        var memo = Str(q, "depositMemo");
        DateTimeOffset? deadline = DateTimeOffset.TryParse(Str(q, "deadline"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var dl) ? dl : null;
        return (new IntentsQuote(
            amountIn,
            amountOut,
            FromSmallest(Str(q, "minAmountOut"), toDecimals),
            Dec(Str(q, "amountInUsd")),
            Dec(Str(q, "amountOutUsd")),
            q.TryGetProperty("timeEstimate", out var te) && te.TryGetDouble(out var secs) ? (int)secs : 0,
            deposit.Length > 0 ? deposit : null,
            memo.Length > 0 ? memo : null,
            deadline), null);
    }

    /// <summary>Tells the service which transaction paid the deposit address, so it starts sooner.
    /// Optional: the deposit is found on-chain anyway.</summary>
    public async Task SubmitDepositAsync(string txHash, string depositAddress, string? memo, CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, string> { ["txHash"] = txHash, ["depositAddress"] = depositAddress };
            if (!string.IsNullOrEmpty(memo)) body["memo"] = memo;
            using var _ = await Http.PostAsJsonAsync($"{Root}/v0/deposit/submit", body, ct);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>The swap's state: PENDING_DEPOSIT, KNOWN_DEPOSIT_TX, PROCESSING, SUCCESS, REFUNDED,
    /// INCOMPLETE_DEPOSIT or FAILED; null when the service did not answer.</summary>
    public async Task<string?> StatusAsync(string depositAddress, string? memo, CancellationToken ct = default)
    {
        try
        {
            var url = $"{Root}/v0/status?depositAddress={Uri.EscapeDataString(depositAddress)}";
            if (!string.IsNullOrEmpty(memo)) url += $"&depositMemo={Uri.EscapeDataString(memo)}";
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return Str(doc.RootElement, "status") is { Length: > 0 } s ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public static BigInteger ToSmallest(decimal amount, int decimals)
    {
        if (amount <= 0) return BigInteger.Zero;
        var text = amount.ToString("0.############################", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var whole = dot < 0 ? text : text[..dot];
        var frac = dot < 0 ? "" : text[(dot + 1)..];
        frac = frac.Length > decimals ? frac[..decimals] : frac.PadRight(decimals, '0');
        return BigInteger.Parse(whole + frac, CultureInfo.InvariantCulture);
    }

    public static decimal FromSmallest(string raw, int decimals)
    {
        if (!BigInteger.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return 0m;
        var text = OnChainHistoryClient.ScaleDown(value.ToString(CultureInfo.InvariantCulture), decimals);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static decimal Dec(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}
