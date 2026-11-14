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
/// One sealed file per wallet and secret (<see cref="WalletDataKey"/>): the route, the pair, the amounts
/// and the one-time deposit address. The old shared plain-text file (<c>swaps.json</c>) is taken apart the
/// same way as the activity log. Cleared with the history and by "Delete everything".
/// </summary>
public sealed class SwapStore
{
    /// <summary>Finished swaps older than this are dropped; unfinished ones stop being asked about.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    private const string Domain = "phobia-swaps-v1";

    private readonly string _folder;
    private readonly string _legacyPath;

    public SwapStore(string? folder = null, string? legacyPath = null)
    {
        _folder = folder ?? Path.Combine(AppPaths.DataRoot, "swaps");
        _legacyPath = legacyPath ?? Path.Combine(AppPaths.DataRoot, "swaps.json");
    }

    /// <param name="Status">The route's own last word (PENDING_DEPOSIT, PROCESSING, SUCCESS, REFUNDED…).</param>
    /// <param name="Ended">True once the route has said something final.</param>
    public sealed record Entry(
        string WalletId, string Provider, string FromKey, string ToKey, decimal AmountIn, decimal ExpectedOut,
        string DepositAddress, string? Memo, string? OrderId, string? TxId, long StartedMs,
        string Status = "", bool Ended = false);

    /// <summary>This wallet's swaps.</summary>
    public IReadOnlyList<Entry> Load(WalletDataKey key, IReadOnlyCollection<string>? walletsOnDevice = null)
    {
        try
        {
            var path = Path.Combine(_folder, key.FileName);
            if (File.Exists(path)) return key.Read<List<Entry>>(path, Domain) ?? [];
            return TakeOverLegacy(key, walletsOnDevice);
        }
        catch
        {
            // corrupt/unreadable → nothing to follow
        }

        return [];
    }

    /// <summary>Adds the swap, or replaces the one paid to the same deposit address.</summary>
    public void Upsert(WalletDataKey key, Entry entry)
    {
        var oldest = DateTimeOffset.UtcNow.Subtract(Keep).ToUnixTimeMilliseconds();
        var all = Load(key)
            .Where(e => e.StartedMs >= oldest && !Same(e, entry))
            .Append(entry)
            .OrderByDescending(e => e.StartedMs)
            .ToList();
        Save(key, all);
    }

    public void Clear()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch { /* non-fatal */ }
        try { if (File.Exists(_legacyPath)) File.Delete(_legacyPath); }
        catch { /* non-fatal */ }
    }

    private void Save(WalletDataKey key, List<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            key.Write(Path.Combine(_folder, key.FileName), entries, Domain);
        }
        catch
        {
            // non-fatal: this swap is followed until the wallet closes, as before
        }
    }

    private IReadOnlyList<Entry> TakeOverLegacy(WalletDataKey key, IReadOnlyCollection<string>? walletsOnDevice)
    {
        if (!File.Exists(_legacyPath)) return [];
        var all = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_legacyPath)) ?? [];

        var mine = all.Where(e => e.WalletId == key.WalletId).ToList();
        Save(key, mine);
        if (!File.Exists(Path.Combine(_folder, key.FileName))) return mine;

        var rest = all.Where(e => e.WalletId != key.WalletId &&
                                  (walletsOnDevice is null || walletsOnDevice.Contains(e.WalletId))).ToList();
        if (rest.Count == 0) File.Delete(_legacyPath);
        else AtomicFile.WriteAllText(_legacyPath, JsonSerializer.Serialize(rest));
        return mine;
    }

    private static bool Same(Entry a, Entry b) =>
        string.Equals(a.DepositAddress, b.DepositAddress, StringComparison.Ordinal) &&
        string.Equals(a.Memo ?? string.Empty, b.Memo ?? string.Empty, StringComparison.Ordinal) &&
        a.StartedMs == b.StartedMs;
}
