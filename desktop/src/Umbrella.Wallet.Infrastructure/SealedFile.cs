using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// The files sealed under a key derived from the open wallet's secret: transaction notes, exchange keys,
/// the address book, the user's explorer key. Readable while that wallet is open, meaningless to anything
/// that copies the file.
///
/// <list type="bullet">
/// <item><b>v2</b> (2026-10-28 on): <c>"PSB2" | nonce(12) | tag(16) | ciphertext</c> — AES-256-GCM, key
/// HKDF-SHA256(secret, info = the store's domain), the marker and the domain bound in as associated data
/// so a file of one store cannot be passed off as another's. Written atomically and owner-only.</item>
/// <item><b>v1</b> (before): <c>nonce | tag | ciphertext</c>, key SHA-256(domain ":" secret). Sound with a
/// high-entropy secret, but not HKDF, which the Altumptic baseline (PSB §2.1) asks for. Still read, so
/// nothing written before is lost — and rewritten as v2 the moment it is read.</item>
/// </list>
/// </summary>
public static class SealedFile
{
    private static readonly byte[] Marker = "PSB2"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Seals <paramref name="plaintext"/> in the v2 format.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> plaintext, string secret, string domain)
    {
        var key = KeyV2(secret, domain);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var blob = new byte[Marker.Length + NonceSize + TagSize + plaintext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext,
                blob.AsSpan(Marker.Length + NonceSize + TagSize),
                blob.AsSpan(Marker.Length + NonceSize, TagSize),
                Aad(domain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        Marker.CopyTo(blob, 0);
        nonce.CopyTo(blob, Marker.Length);
        return blob;
    }

    /// <summary>The plaintext of a v2 or v1 blob, or null when it does not open with this secret.</summary>
    public static byte[]? Open(byte[] blob, string secret, string domain, out bool legacy)
    {
        legacy = false;
        if (blob.Length >= Marker.Length + NonceSize + TagSize && blob.AsSpan(0, Marker.Length).SequenceEqual(Marker) &&
            TryOpen(blob.AsSpan(Marker.Length), KeyV2(secret, domain), Aad(domain)) is { } v2)
            return v2;

        // v1 — also the (one in four billion) v1 file whose random nonce happens to start with the marker.
        if (blob.Length < NonceSize + TagSize) return null;
        var v1 = TryOpen(blob, KeyV1(secret, domain), ReadOnlySpan<byte>.Empty);
        legacy = v1 is not null;
        return v1;
    }

    /// <summary>
    /// Reads a sealed file: its plaintext, or null when there is none or it does not open. A v1 file is
    /// rewritten as v2 on the spot; if that write fails, it is simply tried again on the next read.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(string path, string secret, string domain, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        byte[] blob;
        try { blob = await File.ReadAllBytesAsync(path, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }

        var plain = Open(blob, secret, domain, out var legacy);
        if (plain is not null && legacy)
        {
            try { await AtomicFile.WriteAllBytesAsync(path, Seal(plain, secret, domain), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* stays v1 until the next read */ }
        }

        return plain;
    }

    public static byte[]? Read(string path, string secret, string domain) =>
        ReadAsync(path, secret, domain).GetAwaiter().GetResult();

    /// <summary>Seals and writes atomically, owner-only.</summary>
    public static async Task WriteAsync(string path, byte[] plaintext, string secret, string domain, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await AtomicFile.WriteAllBytesAsync(path, Seal(plaintext, secret, domain), ct);
    }

    public static void Write(string path, byte[] plaintext, string secret, string domain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.WriteAllBytes(path, Seal(plaintext, secret, domain));
    }

    private static byte[]? TryOpen(ReadOnlySpan<byte> body, byte[] key, ReadOnlySpan<byte> aad)
    {
        var plain = new byte[body.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(body[..NonceSize], body[(NonceSize + TagSize)..], body.Slice(NonceSize, TagSize), plain, aad);
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Aad(string domain) => [.. Marker, .. Encoding.UTF8.GetBytes(domain)];

    private static byte[] KeyV2(string secret, string domain)
    {
        var ikm = Encoding.UTF8.GetBytes(secret);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: Encoding.UTF8.GetBytes(domain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }

    private static byte[] KeyV1(string secret, string domain) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(domain + ":" + secret));
}
