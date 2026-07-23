using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>An exchange API connection. The secret is only ever held decrypted in memory.</summary>
public sealed record ExchangeCredential(
    string Exchange,
    string Label,
    string ApiKey,
    string ApiSecret,
    string? Passphrase);

/// <summary>
/// Encrypted-at-rest storage for exchange API keys.
///
/// The encryption key is derived from the wallet seed rather than the vault password, so the
/// credentials are readable exactly while the wallet is unlocked and survive a password change,
/// and no second secret has to be retained in memory.
///
/// Only ever store READ-ONLY exchange keys — see <see cref="ExchangeConnectors"/>. Nothing here
/// can withdraw, and no withdrawal endpoint is implemented anywhere in the app.
/// </summary>
public sealed class ExchangeCredentialStore
{
    private const string DerivationDomain = "umbrella-exchange-keys-v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _path;

    public ExchangeCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "exchanges.bin");
    }

    public bool Exists => File.Exists(_path);

    public async Task<List<ExchangeCredential>> LoadAsync(
        string mnemonic, CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];

        try
        {
            var blob = await File.ReadAllBytesAsync(_path, ct);
            if (blob.Length < NonceSize + TagSize) return [];

            var nonce = blob[..NonceSize];
            var tag = blob[NonceSize..(NonceSize + TagSize)];
            var ciphertext = blob[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];

            var key = DeriveKey(mnemonic);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                var json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<List<ExchangeCredential>>(json) ?? [];
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch
        {
            // A corrupt or foreign file must not brick the wallet — treat as "no connections".
            return [];
        }
    }

    public async Task SaveAsync(
        IEnumerable<ExchangeCredential> credentials, string mnemonic, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(credentials.ToList());
        var plaintext = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        var key = DeriveKey(mnemonic);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, blob, ct);
        File.Move(temp, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static byte[] DeriveKey(string mnemonic) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(DerivationDomain + ":" + mnemonic));
}
