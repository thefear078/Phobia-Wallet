using System.Security.Cryptography;
using System.Text;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// The user's own explorer API key (roadmap N.15), sealed per wallet the way the address book is
/// (<see cref="SealedFile"/>): AES-256-GCM under a key derived from the wallet's secret, readable only
/// while that wallet is open.
/// It is a credential — whoever holds it makes requests that Etherscan ties to the user's account — so
/// it never sits on disk in the clear, and a wipe removes it.
/// </summary>
public sealed class ExplorerKeyStore
{
    private const string DerivationDomain = "phobia-explorer-key-v1";

    private readonly string _root;

    public ExplorerKeyStore(string? root = null) => _root = root ?? AppPaths.DataRoot;

    /// <summary>One file per wallet: a key entered for one wallet is not used by another.</summary>
    public string PathFor(string walletId) =>
        Path.Combine(_root, "explorer-key-" + string.Concat(walletId.Where(char.IsLetterOrDigit)) + ".bin");

    /// <summary>The wallet's key, or null when none was saved (or it does not open).</summary>
    public string? Load(string walletId, string secret)
    {
        var plain = SealedFile.Read(PathFor(walletId), secret, DerivationDomain);
        if (plain is null) return null;   // another wallet's file, or damaged: no key, never a crash
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(string walletId, string secret, string apiKey)
    {
        var plain = Encoding.UTF8.GetBytes(apiKey.Trim());
        try
        {
            SealedFile.Write(PathFor(walletId), plain, secret, DerivationDomain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Remove(string walletId)
    {
        try { File.Delete(PathFor(walletId)); }
        catch { /* nothing to remove */ }
    }
}
