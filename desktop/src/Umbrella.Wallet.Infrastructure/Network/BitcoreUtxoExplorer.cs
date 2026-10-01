using System.Text.Json;
using Umbrella.Wallet.Core.Utxo;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// The live <see cref="IUtxoExplorer"/> over BitPay's keyless Bitcore API, for Dogecoin.
///
/// Dogecoin had one source, BlockCypher, whose free tier is 200 requests an hour per IP. One HD scan is
/// forty-odd requests (the gap limit on two branches), so a few refreshes used the hour up and the
/// balance read "unknown" for the rest of it — "Limits reached" on every request. Bitcore answers the
/// same questions without a published hourly cap.
///
/// Every failure throws, so the scanner reports "unknown" rather than "empty".
/// </summary>
public sealed class BitcoreUtxoExplorer : IUtxoExplorer
{
    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);

    public const string Root = "https://api.bitcore.io";

    private readonly string _base;

    public BitcoreUtxoExplorer(string chain) => _base = BaseFor(chain);

    /// <summary>The API root for a chain, e.g. <c>https://api.bitcore.io/api/DOGE/mainnet</c>.</summary>
    public static string BaseFor(string chain) => $"{Root}/api/{chain.ToUpperInvariant()}/mainnet";

    public async Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
    {
        // Every coin the address ever received, spent or not; one is enough to know it was used.
        using var res = await ExplorerHttp.GetAsync(Http, $"{_base}/address/{Uri.EscapeDataString(address)}/?limit=1", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Bitcore answered something other than a list of coins.");
        var n = doc.RootElement.GetArrayLength();
        return new AddressActivity(n > 0, n);
    }

    public async Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
    {
        // A generous limit so a busy address is not silently cut short (that would under-report the
        // balance and could strand coins).
        using var res = await ExplorerHttp.GetAsync(Http,
            $"{_base}/address/{Uri.EscapeDataString(address)}/?unspent=true&limit=2000", ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseUtxos(doc.RootElement);
    }

    /// <summary>
    /// The spendable outputs in a Bitcore coin list. Bitcore marks an unspent output with spentHeight -2;
    /// -1 is "being spent by a transaction in the mempool", which must not be offered again. A mintHeight
    /// of zero or below is a coin still in the mempool.
    /// </summary>
    public static IReadOnlyList<ExplorerUtxo> ParseUtxos(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Bitcore answered something other than a list of coins.");

        var list = new List<ExplorerUtxo>();
        foreach (var coin in root.EnumerateArray())
        {
            if (!coin.TryGetProperty("mintTxid", out var h) || h.GetString() is not { Length: 64 } txid) continue;
            if (!coin.TryGetProperty("mintIndex", out var i) || !i.TryGetInt32(out var vout) || vout < 0) continue;
            if (!coin.TryGetProperty("value", out var v) || !v.TryGetInt64(out var value) || value <= 0) continue;
            if (coin.TryGetProperty("spentTxid", out var s) && s.GetString() is { Length: > 0 }) continue;
            if (coin.TryGetProperty("spentHeight", out var sh) && sh.TryGetInt64(out var spentHeight) && spentHeight != -2) continue;
            var confirmed = coin.TryGetProperty("mintHeight", out var mh) && mh.TryGetInt64(out var height) && height > 0;
            list.Add(new ExplorerUtxo(txid, vout, value, confirmed));
        }

        return list;
    }
}

/// <summary>
/// Several explorers answering the same questions, asked in turn: the ones not refusing right now first
/// (shared with every other explorer through <see cref="ExplorerHttp.Bench"/>). A server that refused or
/// did not answer is benched; the last failure is rethrown, so "every server failed" stays "unknown".
/// </summary>
public sealed class FailoverUtxoExplorer : IUtxoExplorer
{
    private readonly IReadOnlyList<(string Url, IUtxoExplorer Explorer)> _explorers;

    public FailoverUtxoExplorer(params (string Url, IUtxoExplorer Explorer)[] explorers) => _explorers = explorers;

    /// <summary>
    /// Dogecoin's explorers: Bitcore, then BlockCypher. A server the user chose in the endpoint picker is
    /// the only one asked — quietly sending their addresses somewhere else is what the picker exists to end.
    /// </summary>
    public static IUtxoExplorer ForDogecoin()
    {
        if (Umbrella.Wallet.Core.Safety.ChainEndpoints.OverrideFor("DOGE") is not null)
            return BlockCypherUtxoExplorer.For("DOGE");
        return new FailoverUtxoExplorer(
            (BitcoreUtxoExplorer.BaseFor("DOGE"), new BitcoreUtxoExplorer("DOGE")),
            (BlockCypherUtxoExplorer.DefaultRoot, BlockCypherUtxoExplorer.For("DOGE")));
    }

    public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct) =>
        TryEachAsync(e => e.GetActivityAsync(address, ct), ct);

    public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct) =>
        TryEachAsync(e => e.GetUtxosAsync(address, ct), ct);

    private async Task<T> TryEachAsync<T>(Func<IUtxoExplorer, Task<T>> request, CancellationToken ct)
    {
        Exception? last = null;
        var healthy = ExplorerHttp.HealthyFirst(_explorers.Select(e => e.Url).ToList());
        foreach (var url in healthy)
        {
            ct.ThrowIfCancellationRequested();
            var explorer = _explorers.First(e => e.Url == url).Explorer;
            try
            {
                return await request(explorer);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                if (_explorers.Count > 1 && ExplorerHttp.IsTransient(ex)) ExplorerHttp.Bench(url);
            }
        }

        throw last ?? new InvalidOperationException("No explorer to ask.");
    }
}
