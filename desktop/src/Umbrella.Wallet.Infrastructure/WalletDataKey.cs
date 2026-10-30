using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// What one wallet's own records on this device are sealed with (Altumptic PSB §2.3): its history, its
/// activity log, the swaps it paid for, its cached balances, the addresses it watches, what a scan found.
///
/// The key is the secret the vault slot opened to (the recovery phrase, or a watch-only wallet's key)
/// together with a hidden wallet's passphrase. The file it seals is named by the wallet AND a tag derived
/// from that secret, because one wallet entry can open to more than one wallet: the duress password opens
/// the decoy in the vault's other slot, and a passphrase opens a hidden wallet behind the same phrase.
/// Keyed by the wallet entry alone, every one of them read the same files — the decoy, opened in front of
/// whoever demanded it, listed the real wallet's history. Keyed by the secret, each has files of its own
/// that the others can neither open nor overwrite.
///
/// What remains visible to someone who can list the data folder: how many differently-keyed sets of
/// records a wallet entry has, i.e. that more than one password or passphrase has been used with it.
/// </summary>
public sealed class WalletDataKey
{
    private const int TagBytes = 8;

    private WalletDataKey(string walletId, string secret, string tag)
    {
        WalletId = walletId;
        Secret = secret;
        Tag = tag;
    }

    /// <summary>The registry's id of the wallet entry these records belong to.</summary>
    public string WalletId { get; }

    /// <summary>16 hex characters derived from the secret: what tells two keyed record sets apart.</summary>
    public string Tag { get; }

    /// <summary>The sealing secret. Never printed: <see cref="ToString"/> shows the wallet and tag only.</summary>
    private string Secret { get; }

    public static WalletDataKey For(string walletId, string secret, string passphrase = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        // A newline cannot occur in a phrase or a watch-only key, so phrase+passphrase pairs never collide.
        var material = string.IsNullOrEmpty(passphrase) ? secret : secret + "\n" + passphrase;
        var bytes = Encoding.UTF8.GetBytes(material);
        try
        {
            var tag = HKDF.DeriveKey(HashAlgorithmName.SHA256, bytes, TagBytes, info: "phobia-wallet-data-tag-v1"u8.ToArray());
            return new WalletDataKey(walletId, material, Convert.ToHexString(tag).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>This key's file in a store's folder: <c>&lt;wallet&gt;-&lt;tag&gt;.bin</c>.</summary>
    public string FileName => $"{SafeId(WalletId)}-{Tag}.bin";

    /// <summary>A wallet id as a file name: letters, digits, '-' and '_' only; "main" when nothing is left.</summary>
    public static string SafeId(string walletId)
    {
        var safe = new string((walletId ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length == 0 ? "main" : safe;
    }

    /// <summary>
    /// Every keyed file of one wallet in a folder — exactly <c>&lt;wallet&gt;-&lt;16 hex&gt;.bin</c>, so wallet
    /// "a" never takes wallet "a-b"'s files with it.
    /// </summary>
    public static IEnumerable<string> FilesOf(string folder, string walletId)
    {
        if (!Directory.Exists(folder)) return [];
        var prefix = SafeId(walletId) + "-";
        return Directory.EnumerateFiles(folder, prefix + "*.bin").Where(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var tag = name.Length == prefix.Length + TagBytes * 2 ? name[prefix.Length..] : string.Empty;
            return tag.Length == TagBytes * 2 && tag.All(char.IsAsciiHexDigitLower);
        }).ToList();
    }

    /// <summary>Reads and opens a sealed JSON record; null when it is missing, sealed by another key, or damaged.</summary>
    public T? Read<T>(string path, string domain) where T : class
    {
        var plain = SealedFile.Read(path, Secret, domain);
        if (plain is null) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Seals a JSON record (written whole, owner-only).</summary>
    public void Write<T>(string path, T value, string domain)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            SealedFile.Write(path, plain, Secret, domain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public override string ToString() => $"{SafeId(WalletId)}-{Tag}";
}
