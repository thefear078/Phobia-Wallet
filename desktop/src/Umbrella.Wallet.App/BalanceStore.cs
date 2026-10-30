using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Caches the last-seen balances/prices of the open wallet on this device, so the portfolio total shows
/// instantly on unlock or wallet-switch instead of flashing $0 while the network catches up. It is a
/// display cache only (device-local, never a server); the authoritative amounts always come from the
/// live refresh, which overwrites this a moment later.
///
/// One sealed file per wallet and secret (<see cref="WalletDataKey"/>) — how much someone holds is the
/// first thing a thief wants to know. The old shared plain-text file (<c>balances.json</c>) is read once
/// for the wallet opened first and then deleted: the other wallets' figures in it are only a cache, read
/// again when each is opened.
/// </summary>
public sealed class BalanceStore
{
    private const string Domain = "phobia-balances-v1";

    private readonly string _folder;
    private readonly string _legacyPath;

    public BalanceStore(string? folder = null, string? legacyPath = null)
    {
        _folder = folder ?? Path.Combine(AppPaths.DataRoot, "balances");
        _legacyPath = legacyPath ?? Path.Combine(AppPaths.DataRoot, "balances.json");
    }

    /// <summary>
    /// One cached holding, matched back to an account by symbol + address + network.
    ///
    /// The network is part of the identity, not decoration. Ethereum and its L2 rollups all report the
    /// symbol "ETH" at the SAME 0x address, so ETH on Arbitrum and ETH on Base are indistinguishable
    /// without it — four separate holdings collapsing onto one key. It is last and defaulted so a
    /// cache written before this field existed still loads.
    /// </summary>
    public sealed record Entry(
        string Symbol, string Address, double Amount, double Price, double Change, string Network = "");

    private sealed record Wrapper(Dictionary<string, List<Entry>> Wallets);

    public IReadOnlyList<Entry> Load(WalletDataKey key)
    {
        try
        {
            var path = Path.Combine(_folder, key.FileName);
            if (File.Exists(path)) return key.Read<List<Entry>>(path, Domain) ?? [];

            if (!File.Exists(_legacyPath)) return [];
            var legacy = JsonSerializer.Deserialize<Wrapper>(File.ReadAllText(_legacyPath));
            var mine = legacy?.Wallets is not null && legacy.Wallets.TryGetValue(key.WalletId, out var list) ? list : [];
            if (mine.Count > 0) Save(key, mine);
            File.Delete(_legacyPath);
            return mine;
        }
        catch
        {
            // corrupt/unreadable → no cache, harmless
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
            // non-fatal: the cache just won't persist this time
        }
    }

    public void Clear()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch { /* non-fatal */ }
        try { if (File.Exists(_legacyPath)) File.Delete(_legacyPath); }
        catch { /* non-fatal */ }
    }
}
