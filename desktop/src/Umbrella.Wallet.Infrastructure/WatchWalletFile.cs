using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Umbrella.Wallet.Core.Watch;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Where a seedless watch-only wallet keeps what it watches (roadmap H.5).
///
/// The wallet's vault is the same two-slot file every wallet has, opened by the same password — so a
/// watch-only wallet looks like any other on disk and keeps the duress and panic passwords. What its
/// slot holds is not a phrase but a random key, marked <see cref="Prefix"/>; that key seals the
/// description (<see cref="WatchSpec"/>) in a file beside the vault. The description has to be sealed:
/// an extended public key names every address the account will ever use, which is a wallet's whole
/// history to anybody who reads it. And it cannot live in the vault itself — a slot holds 254 bytes,
/// less than two extended keys.
/// </summary>
public static class WatchWalletFile
{
    /// <summary>What a watch-only wallet's vault slot starts with.</summary>
    public const string Prefix = "watch:v1:";

    private static readonly byte[] Magic = "PWS1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>A fresh secret for a new watch-only wallet's vault slot.</summary>
    public static string NewSecret() => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>True when an unlocked vault slot belongs to a watch-only wallet.</summary>
    public static bool IsWatchSecret(string? secret) =>
        secret is not null && secret.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The sealed description beside a vault.</summary>
    public static string PathFor(string vaultPath) => vaultPath + ".watch";

    public static void Save(string vaultPath, string secret, WatchSpec spec)
    {
        var key = KeyFrom(secret);
        var plain = Encoding.UTF8.GetBytes(spec.ToJson());
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var file = new byte[Magic.Length + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(file, 0);
        nonce.CopyTo(file, Magic.Length);
        tag.CopyTo(file, Magic.Length + NonceSize);
        cipher.CopyTo(file, Magic.Length + NonceSize + TagSize);
        AtomicFile.WriteAllBytes(PathFor(vaultPath), file);
    }

    /// <summary>The description, or an exception when the file is missing, damaged or not this wallet's.</summary>
    public static WatchSpec Load(string vaultPath, string secret)
    {
        var file = File.ReadAllBytes(PathFor(vaultPath));
        if (file.Length < Magic.Length + NonceSize + TagSize || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a watch-only wallet description.");

        var nonce = file.AsSpan(Magic.Length, NonceSize);
        var tag = file.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = file.AsSpan(Magic.Length + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        var key = KeyFrom(secret);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            return WatchSpec.FromJson(Encoding.UTF8.GetString(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] KeyFrom(string secret)
    {
        if (!IsWatchSecret(secret)) throw new ArgumentException("Not a watch-only wallet's secret.", nameof(secret));
        var ikm = Convert.FromBase64String(secret[Prefix.Length..]);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: "phobia watch-only description v1"u8.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }
}
