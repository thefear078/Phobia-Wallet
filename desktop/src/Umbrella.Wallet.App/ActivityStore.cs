using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Persists the activity / transaction feed to the local data folder so it survives closing the app —
/// on this device only, never a server. Best-effort: a read/write failure just means an empty history,
/// never a crash.
///
/// One sealed file per wallet and secret (<see cref="WalletDataKey"/>). It used to be one plain-text file
/// for every wallet, filtered by wallet id on the way in, which listed the real wallet's sends in the
/// decoy that the duress password opens. That file (<c>activity.json</c>) is taken apart on first use:
/// each wallet, when it is next opened, takes its own rows (and the rows older versions saved without a
/// wallet), and the file is removed once no wallet still on this device has rows left in it.
/// </summary>
public sealed class ActivityStore
{
    private const string Domain = "phobia-activity-v1";

    private readonly string _folder;
    private readonly string _legacyPath;

    public ActivityStore(string? folder = null, string? legacyPath = null)
    {
        _folder = folder ?? Path.Combine(AppPaths.DataRoot, "activity");
        _legacyPath = legacyPath ?? Path.Combine(AppPaths.DataRoot, "activity.json");
    }

    /// <summary>A stored activity row (the view-model's core fields, without the computed display bits).
    /// <c>Status</c> is optional so history written by older versions still deserializes (defaults to
    /// "Confirmed"). Retry context is deliberately not persisted — a stale destination must not be resent.</summary>
    /// <remarks><c>UnixMs</c> is when it happened, so a saved row keeps its place among the chain's own after
    /// a restart (it used to come back with no time and sink to the bottom). <c>WalletId</c> is the wallet
    /// a transfer was made from. Both default so older files load.</remarks>
    public sealed record Entry(string Kind, string Asset, string Amount, string Counterparty, string When,
        string? Explorer, string Status = "Confirmed", long UnixMs = 0, string? WalletId = null,
        string? CounterpartyFull = null);

    /// <param name="key">The open wallet's key.</param>
    /// <param name="walletsOnDevice">The wallet entries still listed: rows of any other wallet in the old
    /// shared file are let go when it is taken apart.</param>
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
            // corrupt/unreadable → start fresh
        }

        return [];
    }

    public void Save(WalletDataKey key, IEnumerable<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            key.Write(Path.Combine(_folder, key.FileName), entries.ToList(), Domain);
        }
        catch
        {
            // non-fatal: history just won't persist this time
        }
    }

    public void Clear()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch { /* non-fatal */ }
        try { if (File.Exists(_legacyPath)) File.Delete(_legacyPath); }
        catch { /* non-fatal */ }
    }

    private IReadOnlyList<Entry> TakeOverLegacy(WalletDataKey key, IReadOnlyCollection<string>? walletsOnDevice)
    {
        if (!File.Exists(_legacyPath)) return [];
        var all = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_legacyPath)) ?? [];

        var mine = all.Where(e => string.IsNullOrEmpty(e.WalletId) || e.WalletId == key.WalletId).ToList();
        Save(key, mine);
        if (!File.Exists(Path.Combine(_folder, key.FileName))) return mine;   // not sealed: the old file stays

        var rest = all.Where(e => e.WalletId is { Length: > 0 } id && id != key.WalletId &&
                                  (walletsOnDevice is null || walletsOnDevice.Contains(id))).ToList();
        if (rest.Count == 0) File.Delete(_legacyPath);
        else AtomicFile.WriteAllText(_legacyPath, JsonSerializer.Serialize(rest));
        return mine;
    }
}
