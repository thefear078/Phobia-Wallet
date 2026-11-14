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
/// Encrypted-at-rest storage for exchange API keys (<see cref="SealedFile"/>).
///
/// The encryption key is derived from the wallet's secret rather than the vault password, so the
/// credentials are readable exactly while the wallet is unlocked and survive a password change,
/// and no second secret has to be retained in memory.
///
/// Only ever store READ-ONLY exchange keys — see <see cref="ExchangeConnectors"/>. Nothing here
/// can withdraw, and no withdrawal endpoint is implemented anywhere in the app.
/// </summary>
public sealed class ExchangeCredentialStore
{
    private const string DerivationDomain = "umbrella-exchange-keys-v1";

    private readonly string _path;

    public ExchangeCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, "exchanges.bin");
    }

    public bool Exists => File.Exists(_path);

    public async Task<List<ExchangeCredential>> LoadAsync(
        string mnemonic, CancellationToken ct = default)
    {
        var plaintext = await SealedFile.ReadAsync(_path, mnemonic, DerivationDomain, ct);
        if (plaintext is null) return [];   // a corrupt or foreign file must not brick the wallet
        try
        {
            return JsonSerializer.Deserialize<List<ExchangeCredential>>(Encoding.UTF8.GetString(plaintext)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SaveAsync(
        IEnumerable<ExchangeCredential> credentials, string mnemonic, CancellationToken ct = default)
    {
        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credentials.ToList()));
        try
        {
            await SealedFile.WriteAsync(_path, plaintext, mnemonic, DerivationDomain, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
