using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>An Exolix rate: what an amount buys at the floating rate, and the pair's limits.</summary>
public sealed record ExolixRate(decimal FromAmount, decimal ToAmount, decimal MinAmount, decimal MaxAmount);

/// <summary>An Exolix exchange order: where to pay (and the memo some chains need) and what it pays out.</summary>
public sealed record ExolixOrder(string Id, string DepositAddress, string? DepositExtraId, decimal Amount, decimal AmountTo);

/// <summary>
/// Exolix, an instant exchange — the route for Monero, Nano and Decred, which no decentralised swap
/// reaches. It is custodial for the minutes a swap takes: the coins are paid to Exolix's address and it
/// pays the other coin out to the wallet's own. The wallet says so before anything is created.
///
/// <para>No account and no key: the public API quotes and creates orders as is. An order is only
/// created when the user confirms, and paying it is an ordinary send they review like any other.</para>
/// </summary>
public sealed class ExolixSwapClient
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Swaps);

    public const string Root = "https://exolix.com/api/v2";

    /// <summary>The page where an order can be followed.</summary>
    public static string TrackUrl(string id) => $"https://exolix.com/transaction/{Uri.EscapeDataString(id)}";

    public async Task<(ExolixRate? Rate, string? Error)> RateAsync(
        string coinFrom, string networkFrom, string coinTo, string networkTo, decimal amount, CancellationToken ct = default)
    {
        try
        {
            var url = $"{Root}/rate?coinFrom={coinFrom}&networkFrom={networkFrom}&coinTo={coinTo}&networkTo={networkTo}" +
                      $"&amount={amount.ToString(CultureInfo.InvariantCulture)}&rateType=float";
            using var res = await Http.GetAsync(url, ct);
            return ParseRate(await res.Content.ReadAsStringAsync(ct));
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

    /// <summary>A rate, or the exchange's reason: "Such exchange pair is not available", an amount below
    /// the minimum (with the minimum), and so on.</summary>
    public static (ExolixRate? Rate, string? Error) ParseRate(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (Str(root, "error") is { Length: > 0 } error) return (null, error);

        var rate = new ExolixRate(Num(root, "fromAmount"), Num(root, "toAmount"), Num(root, "minAmount"), Num(root, "maxAmount"));
        var message = Str(root, "message");
        return rate.ToAmount > 0 ? (rate, null) : (null, message.Length > 0 ? message : null);
    }

    /// <summary>Creates the order: pay <paramref name="amount"/> of the source coin to the returned
    /// address; the other coin goes to <paramref name="withdrawalAddress"/>, a refund to
    /// <paramref name="refundAddress"/>.</summary>
    public async Task<(ExolixOrder? Order, string? Error)> CreateAsync(
        string coinFrom, string networkFrom, string coinTo, string networkTo, decimal amount,
        string withdrawalAddress, string? refundAddress, CancellationToken ct = default)
    {
        try
        {
            var body = new Dictionary<string, object>
            {
                ["coinFrom"] = coinFrom,
                ["networkFrom"] = networkFrom,
                ["coinTo"] = coinTo,
                ["networkTo"] = networkTo,
                ["amount"] = amount,
                ["withdrawalAddress"] = withdrawalAddress,
                ["rateType"] = "float",
            };
            if (!string.IsNullOrEmpty(refundAddress)) body["refundAddress"] = refundAddress;
            using var res = await Http.PostAsJsonAsync($"{Root}/transactions", body, ct);
            return ParseOrder(await res.Content.ReadAsStringAsync(ct));
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

    /// <summary>An order, or the reason it was refused ("error", or the "errors" map of field → reason).</summary>
    public static (ExolixOrder? Order, string? Error) ParseOrder(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (Str(root, "error") is { Length: > 0 } error) return (null, error);
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            return (null, string.Join(", ", errors.EnumerateObject().Select(e =>
                e.Value.ValueKind == JsonValueKind.String ? e.Value.GetString() : e.Value.GetRawText())));

        var id = Str(root, "id");
        var deposit = Str(root, "depositAddress");
        if (id.Length == 0 || deposit.Length == 0) return (null, Str(root, "message") is { Length: > 0 } m ? m : null);
        var extra = Str(root, "depositExtraId");
        return (new ExolixOrder(id, deposit, extra.Length > 0 ? extra : null, Num(root, "amount"), Num(root, "amountTo")), null);
    }

    /// <summary>The order's state (wait, confirmation, confirmed, exchanging, sending, success, overdue,
    /// refunded…); null when the exchange did not answer.</summary>
    public async Task<string?> StatusAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync($"{Root}/transactions/{Uri.EscapeDataString(id)}", ct);
            using var doc = JsonDocument.Parse(json);
            return Str(doc.RootElement, "status") is { Length: > 0 } s ? s : null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return 0m;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var dbl)) return (decimal)dbl;
        return p.ValueKind == JsonValueKind.String &&
               decimal.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0m;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}
