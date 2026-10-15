using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// The swaps this device has paid for, kept so they can be followed to their end.
///
/// A swap used to be followed only in memory: close the wallet — or just lock it — and nothing asked
/// about it again. Its row in Activity said "confirmed" from the moment the payment left, whatever
/// became of the swap: completed, refunded, or sitting short of its quote. Kept here, an unfinished swap
/// is picked up again at the next unlock, and its row says what the route itself says.
///
/// One plain-text file, like the activity log beside it: the route, the pair, the amounts and the
/// one-time deposit address — nothing that is not already in the user's own transaction on a public
/// chain. Cleared with the history and by "Delete everything".
/// </summary>
public sealed class SwapStore
{
    /// <summary>Finished swaps older than this are dropped; unfinished ones stop being asked about.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    private readonly string _path;

    public SwapStore(string? path = null) =>
        _path = path ?? Path.Combine(AppPaths.DataRoot, "swaps.json");

    /// <param name="Status">The route's own last word (PENDING_DEPOSIT, PROCESSING, SUCCESS, REFUNDED…).</param>
    /// <param name="Ended">True once the route has said something final.</param>
    public sealed record Entry(
        string WalletId, string Provider, string FromKey, string ToKey, decimal AmountIn, decimal ExpectedOut,
        string DepositAddress, string? Memo, string? OrderId, string? TxId, long StartedMs,
        string Status = "", bool Ended = false);

    public IReadOnlyList<Entry> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
        }
        catch
        {
            // corrupt/unreadable → nothing to follow
        }

        return [];
    }

    /// <summary>Adds the swap, or replaces the one paid to the same deposit address.</summary>
    public void Upsert(Entry entry)
    {
        var oldest = DateTimeOffset.UtcNow.Subtract(Keep).ToUnixTimeMilliseconds();
        var all = Load()
            .Where(e => e.StartedMs >= oldest && !Same(e, entry))
            .Append(entry)
            .OrderByDescending(e => e.StartedMs)
            .ToList();
        Save(all);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* non-fatal */ }
    }

    private void Save(List<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // non-fatal: this swap is followed until the wallet closes, as before
        }
    }

    private static bool Same(Entry a, Entry b) =>
        string.Equals(a.DepositAddress, b.DepositAddress, StringComparison.Ordinal) &&
        string.Equals(a.Memo ?? string.Empty, b.Memo ?? string.Empty, StringComparison.Ordinal) &&
        a.StartedMs == b.StartedMs;
}
