using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// The last USD → fiat rates this device read, so the chosen currency is right from the first frame
/// after a start or a switch, with no network call in between. Public market data only — nothing about
/// the wallet. A rate is used for at most a week: older than that it is not what anyone would quote.
/// </summary>
public sealed class FxRateCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private readonly string _path;
    private Snapshot? _snapshot;

    public FxRateCache(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "fx-rates.json");

    public sealed record Snapshot(long At, Dictionary<string, decimal> Rates);

    /// <summary>When the rates were read (UTC), or the epoch when there are none.</summary>
    public DateTimeOffset At => DateTimeOffset.FromUnixTimeSeconds(Load()?.At ?? 0);

    /// <summary>The rate for <paramref name="code"/> if one was read in the last week, else null.</summary>
    public decimal? RateFor(string code)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1m;
        var snap = Load();
        if (snap is null || DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(snap.At) > MaxAge) return null;
        return snap.Rates.TryGetValue(code.ToUpperInvariant(), out var r) && r > 0m ? r : null;
    }

    public void Save(IReadOnlyDictionary<string, decimal> rates)
    {
        _snapshot = new Snapshot(DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            rates.Where(kv => kv.Value > 0m).ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value));
        try { AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_snapshot)); }
        catch { /* the rates stay in memory for this session; the next read writes them again */ }
    }

    private Snapshot? Load()
    {
        if (_snapshot is not null) return _snapshot;
        try
        {
            if (File.Exists(_path)) _snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path));
        }
        catch
        {
            // unreadable → no cache; the next read writes a good one
        }
        return _snapshot;
    }
}
