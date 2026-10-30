using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Encrypted-at-rest private notes on transactions — the user's own bookkeeping ("salary", "sold the
/// car", "coffee"), keyed by transaction id. Unlike the address book (which holds only public
/// addresses), a note can quietly link an on-chain payment to a real-world identity, so it is NEVER
/// written in the clear: notes are sealed with AES-256-GCM under a key derived from the wallet's secret
/// (<see cref="SealedFile"/>) — readable exactly while the wallet is unlocked, and unreadable to anything
/// that copies the file.
///
/// Mirrors <see cref="ExchangeCredentialStore"/>: same sealing, same fail-closed handling (a corrupt or
/// foreign file reads as "no notes", never a crash). Per wallet, so each wallet's notes are separate and
/// each is sealed under its own key.
/// </summary>
public sealed class TxNoteStore
{
    private const string DerivationDomain = "umbrella-tx-notes-v1";

    private readonly string _path;

    public TxNoteStore(string? walletId = null, string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataRoot, FileName(walletId));
    }

    /// <summary>Per-wallet file so a second wallet's notes never overwrite the first's.</summary>
    private static string FileName(string? walletId)
    {
        var id = string.IsNullOrWhiteSpace(walletId) ? "default" : walletId;
        // Keep the filename filesystem-safe regardless of the wallet id.
        var safe = new string(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return $"tx-notes-{safe}.bin";
    }

    public bool Exists => File.Exists(_path);

    /// <summary>Decrypts the notes for this wallet: a map of transaction id → note text.</summary>
    public async Task<Dictionary<string, string>> LoadAsync(string mnemonic, CancellationToken ct = default)
    {
        var plaintext = await SealedFile.ReadAsync(_path, mnemonic, DerivationDomain, ct);
        if (plaintext is null) return new();   // none, or the wrong secret, or damaged: never a crash
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plaintext)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Encrypts and persists the whole note map for this wallet (atomic replace).</summary>
    public async Task SaveAsync(
        IReadOnlyDictionary<string, string> notes, string mnemonic, CancellationToken ct = default)
    {
        // Drop empties so clearing a note removes it rather than storing a blank.
        var trimmed = notes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(trimmed));
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
