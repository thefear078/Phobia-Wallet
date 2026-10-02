using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>One wallet in the registry. <see cref="IsLegacy"/> marks the original single vault, which
/// keeps its historic path (<c>data/vault.json</c>); every other wallet lives under <c>data/wallets/</c>.</summary>
/// <summary><see cref="Coins"/> restricts which coins a wallet shows (null/empty = all coins).</summary>
/// <summary><see cref="MoneroScanFrom"/>: for a wallet imported from a Monero 25-word seed, the block its
/// Monero wallet starts scanning from (the seed itself does not say how old it is). Null = from the start.</summary>
public sealed record WalletEntry(
    string Id, string Label, bool IsLegacy, string? Color = null, IReadOnlyList<string>? Coins = null,
    ulong? MoneroScanFrom = null);

/// <summary>A wallet taken out of the list. Its encrypted vault is kept on this device, so it can be put
/// back; only a full data wipe deletes it.</summary>
public sealed record RemovedWallet(string Key, string Label, string? Color, DateTimeOffset RemovedAt);

/// <summary>
/// Binance-style multi-wallet registry. Tracks several independent wallets — each its own
/// password-encrypted seed vault — and which one is active. It never touches the seeds themselves
/// (that stays with <see cref="EncryptedFileSeedVault"/>); it only records labels, ids and the
/// active selection, and resolves each wallet's vault path.
///
/// Safety invariants:
///  • The pre-existing single vault is always preserved and, once present, registered as the first
///    "Main wallet" — upgrading never moves or rewrites it.
///  • Adding a wallet is purely additive; it can never overwrite another wallet's vault.
///  • The active wallet can't be removed, and removing a wallet never deletes its vault: the file is
///    moved to <c>wallets/removed/</c> and can be restored. Only a full data wipe deletes it.
/// </summary>
public sealed class WalletRegistry
{
    private readonly string _indexPath;
    private readonly string _legacyVaultPath;
    private readonly Func<string, string> _managedVaultPath;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly List<WalletEntry> _wallets = [];
    private string? _activeId;

    public WalletRegistry()
        : this(AppPaths.WalletsIndexFile, AppPaths.VaultFile, AppPaths.WalletVaultFile)
    {
    }

    public WalletRegistry(string indexPath, string legacyVaultPath, Func<string, string> managedVaultPath)
    {
        _indexPath = indexPath;
        _legacyVaultPath = legacyVaultPath;
        _managedVaultPath = managedVaultPath;
        Load();
    }

    /// <summary>Re-reads the registry from disk. Used after a full data wipe, when every vault file is
    /// gone and the in-memory list must reflect the now-empty state.</summary>
    public void ReloadFromDisk() => Load();

    public IReadOnlyList<WalletEntry> Wallets => _wallets;

    /// <summary>The selected wallet, or the first one, or null when no wallet exists yet.</summary>
    public WalletEntry? Active =>
        _wallets.FirstOrDefault(w => w.Id == _activeId) ?? _wallets.FirstOrDefault();

    public bool HasAnyWallet => _wallets.Count > 0;

    /// <summary>Absolute path to a wallet's encrypted vault file.</summary>
    public string VaultPathFor(WalletEntry entry) =>
        entry.IsLegacy ? _legacyVaultPath : _managedVaultPath(entry.Id);

    /// <summary>Path the very first wallet should be created at when the registry is empty — the legacy
    /// location, so a fresh install writes exactly where every prior version did.</summary>
    public string FirstWalletVaultPath => _legacyVaultPath;

    /// <summary>Registers the first wallet (the legacy vault) after it has been created on a fresh
    /// install. No-op if a legacy wallet is already present.</summary>
    public WalletEntry EnsureLegacyRegistered(string label = "Main wallet")
    {
        var existing = _wallets.FirstOrDefault(w => w.IsLegacy);
        if (existing is not null) return existing;

        var entry = new WalletEntry("main", label, IsLegacy: true);
        _wallets.Insert(0, entry);
        _activeId ??= entry.Id;
        Save();
        return entry;
    }

    /// <summary>Creates a new managed wallet entry (its vault must then be written at
    /// <see cref="VaultPathFor"/>). Does not change the active selection.</summary>
    public WalletEntry Add(string label)
    {
        var clean = string.IsNullOrWhiteSpace(label) ? "Wallet" : label.Trim();
        var id = NewId();
        var entry = new WalletEntry(id, clean, IsLegacy: false);
        _wallets.Add(entry);
        Save();
        return entry;
    }

    public void SetActive(string id)
    {
        if (_wallets.Any(w => w.Id == id))
        {
            _activeId = id;
            Save();
        }
    }

    public void Rename(string id, string label)
    {
        var i = _wallets.FindIndex(w => w.Id == id);
        if (i < 0) return;
        var clean = string.IsNullOrWhiteSpace(label) ? _wallets[i].Label : label.Trim();
        _wallets[i] = _wallets[i] with { Label = clean };
        Save();
    }

    /// <summary>Takes a wallet out of the list. A managed wallet's vault is MOVED to
    /// <c>wallets/removed/</c>, never deleted — one click used to erase a wallet's only copy of its
    /// encrypted seed. The legacy entry is de-registered and <c>data/vault.json</c> stays where it is.
    /// Throws when asked to remove the active wallet.</summary>
    public void Remove(string id)
    {
        var entry = _wallets.FirstOrDefault(w => w.Id == id);
        if (entry is null) return;
        if (entry.Id == Active?.Id)
        {
            throw new InvalidOperationException("Switch to another wallet before removing this one.");
        }

        _wallets.Remove(entry);
        if (_activeId == id) _activeId = _wallets.FirstOrDefault()?.Id;

        if (!entry.IsLegacy)
        {
            var path = _managedVaultPath(entry.Id);
            if (File.Exists(path))
            {
                // Kept, not deleted. If the move fails the wallet stays in the list: a removal that could
                // lose the vault is not a removal this registry makes.
                var key = $"{entry.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                Directory.CreateDirectory(RemovedDir);
                File.Move(path, Path.Combine(RemovedDir, key + ".vault.json"));
                var removed = LoadRemoved();
                removed.Add(new RemovedRow(key, entry.Id, entry.Label, entry.Color, entry.Coins?.ToList(),
                    entry.MoneroScanFrom, DateTimeOffset.UtcNow));
                SaveRemoved(removed);
            }
        }

        Save();
    }

    /// <summary>Wallets taken out of the list whose vaults are still on this device, newest first.</summary>
    public IReadOnlyList<RemovedWallet> Removed =>
        LoadRemoved()
            .Where(r => File.Exists(Path.Combine(RemovedDir, r.Key + ".vault.json")))
            .OrderByDescending(r => r.RemovedAt)
            .Select(r => new RemovedWallet(r.Key, r.Label, r.Color, r.RemovedAt))
            .ToList();

    /// <summary>Puts a removed wallet back in the list, its vault back where wallets live. Its old id is
    /// kept unless something has taken it since. Returns null when there is nothing to restore.</summary>
    public WalletEntry? Restore(string key)
    {
        var removed = LoadRemoved();
        var row = removed.FirstOrDefault(r => r.Key == key);
        var source = Path.Combine(RemovedDir, key + ".vault.json");
        if (row is null || !File.Exists(source)) return null;

        var id = _wallets.Any(w => w.Id == row.Id) || File.Exists(_managedVaultPath(row.Id)) ? NewId() : row.Id;
        var target = _managedVaultPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target);

        var entry = new WalletEntry(id, row.Label, IsLegacy: false, row.Color, row.Coins, row.MoneroScanFrom);
        _wallets.Add(entry);
        Save();
        removed.Remove(row);
        SaveRemoved(removed);
        return entry;
    }

    /// <summary>Where removed wallets' vaults wait, beside the live ones.</summary>
    private string RemovedDir => Path.Combine(Path.GetDirectoryName(_managedVaultPath("probe"))!, "removed");

    private string RemovedIndexPath => Path.Combine(RemovedDir, "removed.json");

    private List<RemovedRow> LoadRemoved()
    {
        try
        {
            return File.Exists(RemovedIndexPath)
                ? JsonSerializer.Deserialize<List<RemovedRow>>(File.ReadAllText(RemovedIndexPath), JsonOptions) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveRemoved(List<RemovedRow> rows)
    {
        Directory.CreateDirectory(RemovedDir);
        var tmp = $"{RemovedIndexPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rows, JsonOptions), Encoding.UTF8);
        File.Move(tmp, RemovedIndexPath, overwrite: true);
    }

    private sealed record RemovedRow(
        string Key, string Id, string Label, string? Color, List<string>? Coins, ulong? MoneroScanFrom,
        DateTimeOffset RemovedAt);

    // --- Persistence ---------------------------------------------------------

    private void Load()
    {
        _wallets.Clear();
        _activeId = null;

        try
        {
            if (File.Exists(_indexPath))
            {
                var index = JsonSerializer.Deserialize<IndexFile>(
                    File.ReadAllText(_indexPath), JsonOptions);
                if (index?.Wallets is not null)
                {
                    foreach (var row in index.Wallets)
                    {
                        if (!string.IsNullOrWhiteSpace(row.Id))
                        {
                            _wallets.Add(new WalletEntry(
                                row.Id, row.Label ?? "Wallet", row.Legacy, row.Color, row.Coins, row.MoneroScanFrom));
                        }
                    }
                    _activeId = index.Active;
                }
            }
        }
        catch
        {
            // A corrupt index must never lock the user out: fall back to legacy-vault detection below.
            _wallets.Clear();
            _activeId = null;
        }

        // Defensive: if the legacy vault exists on disk but isn't represented (fresh upgrade, or a
        // damaged index), register it as the first wallet so the user's funds are always reachable.
        if (File.Exists(_legacyVaultPath) && !_wallets.Any(w => w.IsLegacy))
        {
            _wallets.Insert(0, new WalletEntry("main", "Main wallet", IsLegacy: true));
            _activeId ??= "main";
            Save();
        }

        if (_activeId is null || _wallets.All(w => w.Id != _activeId))
        {
            _activeId = _wallets.FirstOrDefault()?.Id;
        }
    }

    private void Save()
    {
        try
        {
            var index = new IndexFile(
                _activeId,
                _wallets.Select(w => new Row(w.Id, w.Label, w.IsLegacy, w.Color, w.Coins?.ToList(), w.MoneroScanFrom))
                    .ToList());

            var dir = Path.GetDirectoryName(_indexPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = $"{_indexPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(index, JsonOptions), Encoding.UTF8);
            File.Move(tmp, _indexPath, overwrite: true);
        }
        catch
        {
            // Persisting the index is best-effort; the wallets themselves are never at risk.
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Sets (or clears, with null) a wallet's colour tag. Persisted.</summary>
    public void SetColor(string id, string? color)
    {
        var i = _wallets.FindIndex(w => w.Id == id);
        if (i < 0) return;
        _wallets[i] = _wallets[i] with { Color = string.IsNullOrWhiteSpace(color) ? null : color };
        Save();
    }

    /// <summary>Restricts a wallet to specific coins (null/empty = all). Persisted.</summary>
    public void SetCoins(string id, IReadOnlyList<string>? coins)
    {
        var i = _wallets.FindIndex(w => w.Id == id);
        if (i < 0) return;
        var value = coins is { Count: > 0 } ? coins : null;
        _wallets[i] = _wallets[i] with { Coins = value };
        Save();
    }

    /// <summary>Records where an imported Monero seed's wallet starts scanning (null = the first block).</summary>
    public void SetMoneroScanFrom(string id, ulong? height)
    {
        var i = _wallets.FindIndex(w => w.Id == id);
        if (i < 0) return;
        _wallets[i] = _wallets[i] with { MoneroScanFrom = height };
        Save();
    }

    private sealed record IndexFile(string? Active, List<Row> Wallets);
    private sealed record Row(
        string Id, string? Label, bool Legacy, string? Color = null, List<string>? Coins = null,
        ulong? MoneroScanFrom = null);
}
