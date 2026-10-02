using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Caches the last-seen market prices on this device so the Market list shows real numbers the instant
/// it opens, instead of dashes that fill in over a few seconds. Public price data only — no secrets.
/// The live refresh overwrites it moments later; this is purely to kill the "gradual populate" flicker.
/// </summary>
public sealed class MarketCache
{
    private readonly string _path;

    public MarketCache(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "market.json");

    /// <summary>A coin's last price and, when the market list drew it, its candles for one chart window
    /// (open, high, low, close, volume each), so sparklines and the first chart of a session draw at once.
    /// The candle fields are last and defaulted: a market.json from before them still loads.</summary>
    public sealed record Entry(
        string Symbol, double Price, double Change, string? Range = null, long At = 0, List<double[]>? Candles = null);

    public IReadOnlyList<Entry> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            // corrupt/unreadable → no cache, harmless
        }

        return [];
    }

    public void Save(IEnumerable<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries.ToList()));
        }
        catch
        {
            // non-fatal
        }
    }
}
