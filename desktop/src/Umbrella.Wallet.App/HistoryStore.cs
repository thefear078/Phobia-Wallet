using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App;

/// <summary>
/// The on-chain history each wallet has read so far, kept on this device so it is there the next time —
/// and so it grows.
///
/// History used to live in memory only. Every unlock read every chain again, an explorer that did not
/// answer left its coin's rows simply absent, and a source that hands out twelve transactions at a time
/// could never show the thirteenth. A wallet with a year of transfers looked as if it had been born the
/// day it was imported here. Kept, each read ADDS to what was read before: what an explorer gave once
/// stays listed when it is rate-limited tomorrow, and older pages accumulate behind the newest.
///
/// One plain-text file per wallet, beside the activity log and holding the same kind of thing — public
/// transaction ids, amounts and counterparties, already on a blockchain for anyone to read. Clearing
/// the history (Settings) and removing a wallet delete it.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>More than this many rows and the oldest are let go: the file stays small enough to read at once.</summary>
    public const int MaxRows = 3000;

    private readonly string _folder;

    public HistoryStore(string? folder = null) =>
        _folder = folder ?? Path.Combine(AppPaths.DataRoot, "history");

    private string PathFor(string walletId)
    {
        // A wallet id is a short identifier the registry made; anything else in it must not become a path.
        var safe = new string(walletId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return Path.Combine(_folder, (safe.Length == 0 ? "main" : safe) + ".json");
    }

    public IReadOnlyList<ChainTx> Load(string walletId)
    {
        try
        {
            var path = PathFor(walletId);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<ChainTx>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            // corrupt/unreadable → as if nothing had been read yet
        }

        return [];
    }

    public void Save(string walletId, IEnumerable<ChainTx> rows)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            AtomicFile.WriteAllText(PathFor(walletId), JsonSerializer.Serialize(rows.Take(MaxRows).ToList()));
        }
        catch
        {
            // non-fatal: this read just is not remembered
        }
    }

    public void Delete(string walletId)
    {
        try { File.Delete(PathFor(walletId)); }
        catch { /* non-fatal */ }
    }

    public void Clear()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// What was read now together with what was read before, newest first, each transaction once. A row
    /// read now replaces the kept one for the same transaction — an amount or a time an explorer has
    /// since corrected is taken, not argued with.
    /// </summary>
    public static IReadOnlyList<ChainTx> Merge(IEnumerable<ChainTx> fresh, IEnumerable<ChainTx> kept)
    {
        var byKey = new Dictionary<string, ChainTx>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in fresh.Concat(kept))
            byKey.TryAdd(KeyOf(row), row);   // fresh comes first, so it wins
        return byKey.Values.OrderByDescending(r => r.UnixMs).ToList();
    }

    /// <summary>
    /// One row per transaction and direction and asset: a swap can be a send of one asset and a receipt
    /// of another under one id, and both are history.
    /// </summary>
    public static string KeyOf(ChainTx row) =>
        $"{(row.Hash.Length > 0 ? row.Hash : row.Explorer)}|{row.Asset}|{row.Kind}";
}
