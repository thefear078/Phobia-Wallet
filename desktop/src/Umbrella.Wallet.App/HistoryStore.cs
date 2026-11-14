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
/// One sealed file per wallet and secret (<see cref="WalletDataKey"/>): the amounts and counterparties of
/// someone's transfers say a great deal about them even though each is on a public chain, and a decoy or
/// hidden wallet must never be shown the real one's. A plain-text file an earlier version wrote is taken
/// over, sealed, by the first wallet opened under that entry, and then removed. Clearing the history
/// (Settings) deletes them all.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>More than this many rows and the oldest are let go: the file stays small enough to read at once.</summary>
    public const int MaxRows = 3000;

    private const string Domain = "phobia-history-v1";

    private readonly string _folder;

    public HistoryStore(string? folder = null) =>
        _folder = folder ?? Path.Combine(AppPaths.DataRoot, "history");

    private string PathFor(WalletDataKey key) => Path.Combine(_folder, key.FileName);

    private string LegacyPathFor(string walletId) => Path.Combine(_folder, WalletDataKey.SafeId(walletId) + ".json");

    public IReadOnlyList<ChainTx> Load(WalletDataKey key)
    {
        try
        {
            var path = PathFor(key);
            if (File.Exists(path)) return key.Read<List<ChainTx>>(path, Domain) ?? [];

            var legacy = LegacyPathFor(key.WalletId);
            if (!File.Exists(legacy)) return [];
            var rows = JsonSerializer.Deserialize<List<ChainTx>>(File.ReadAllText(legacy)) ?? [];
            Save(key, rows);
            if (File.Exists(path)) File.Delete(legacy);   // only once the sealed copy is on disk
            return rows;
        }
        catch
        {
            // corrupt/unreadable → as if nothing had been read yet
        }

        return [];
    }

    public void Save(WalletDataKey key, IEnumerable<ChainTx> rows)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            key.Write(PathFor(key), rows.Take(MaxRows).ToList(), Domain);
        }
        catch
        {
            // non-fatal: this read just is not remembered
        }
    }

    /// <summary>Every record of one wallet entry, whichever secret sealed it, and its old plain-text file.</summary>
    public void Delete(string walletId)
    {
        foreach (var file in WalletDataKey.FilesOf(_folder, walletId))
        {
            try { File.Delete(file); }
            catch { /* non-fatal */ }
        }

        try { File.Delete(LegacyPathFor(walletId)); }
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
