using System.Security.Cryptography;
using System.Text;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// The user's own explorer API key (roadmap N.15), sealed per wallet the way the address book is:
/// AES-256-GCM under a key derived from the wallet's secret, readable only while that wallet is open.
/// It is a credential — whoever holds it makes requests that Etherscan ties to the user's account — so
/// it never sits on disk in the clear, and a wipe removes it.
/// </summary>
public sealed class ExplorerKeyStore
{
    private const string DerivationDomain = "phobia-explorer-key-v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _root;

    public ExplorerKeyStore(string? root = null) => _root = root ?? AppPaths.DataRoot;

    /// <summary>One file per wallet: a key entered for one wallet is not used by another.</summary>
    public string PathFor(string walletId) =>
        Path.Combine(_root, "explorer-key-" + string.Concat(walletId.Where(char.IsLetterOrDigit)) + ".bin");

    /// <summary>The wallet's key, or null when none was saved (or it does not open).</summary>
    public string? Load(string walletId, string secret)
    {
        var path = PathFor(walletId);
        if (!File.Exists(path)) return null;
        try
        {
            var blob = File.ReadAllBytes(path);
            if (blob.Length < NonceSize + TagSize) return null;
            var plain = new byte[blob.Length - NonceSize - TagSize];
            var key = DeriveKey(secret);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain);
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            return null;   // another wallet's file, or damaged: no key, never a crash
        }
    }

    public void Save(string walletId, string secret, string apiKey)
    {
        var plain = Encoding.UTF8.GetBytes(apiKey.Trim());
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        var key = DeriveKey(secret);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceSize);
        cipher.CopyTo(blob, NonceSize + TagSize);
        Directory.CreateDirectory(_root);
        AtomicFile.WriteAllBytes(PathFor(walletId), blob);
    }

    public void Remove(string walletId)
    {
        try { File.Delete(PathFor(walletId)); }
        catch { /* nothing to remove */ }
    }

    private static byte[] DeriveKey(string secret) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(DerivationDomain + ":" + secret));
}
