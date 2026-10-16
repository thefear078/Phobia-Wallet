using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// The panic password (roadmap P1.3): a password that, typed where the wallet password goes, erases
/// this device's wallet data instead of opening it.
///
/// What is kept is a salted PBKDF2-SHA256 hash, in a file that always exists and always has the same
/// size: with no panic password set it holds random bytes. Somebody reading the disk cannot tell whether
/// one is set — the same reason the vault always has two slots (the duress password, P1.1).
///
/// The hash protects a trigger, not a key: whoever brute-forced it would learn the password that wipes
/// the wallet, nothing more. So it is cheaper than the vault's Argon2id — it is checked before every
/// unlock and must not double the wait.
/// </summary>
public sealed class PanicGuard
{
    public const string FileName = "unlock-guard.bin";

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 210_000;

    private readonly string _path;

    public PanicGuard(string dataRoot) => _path = Path.Combine(dataRoot, FileName);

    /// <summary>The size of the file whatever it holds.</summary>
    public static int FileSize => SaltBytes + HashBytes;

    /// <summary>Writes random bytes when there is no file yet, so a set guard and an unset one look alike.</summary>
    public void EnsureExists()
    {
        try
        {
            if (File.Exists(_path)) return;
            WriteRandom();
        }
        catch
        {
            // a guard that cannot be written is one that never fires; the wallet still opens
        }
    }

    /// <summary>True when <paramref name="password"/> is the panic password. Constant-time compare.</summary>
    public bool Matches(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        byte[] stored;
        try
        {
            if (!File.Exists(_path)) return false;
            stored = File.ReadAllBytes(_path);
        }
        catch
        {
            return false;
        }

        if (stored.Length != FileSize) return false;
        var salt = stored.AsSpan(0, SaltBytes);
        var expected = stored.AsSpan(SaltBytes, HashBytes);
        var actual = Derive(password, salt);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    /// <summary>Makes <paramref name="password"/> the panic password.</summary>
    public void Set(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);
        var content = new byte[FileSize];
        salt.CopyTo(content, 0);
        hash.CopyTo(content, SaltBytes);
        CryptographicOperations.ZeroMemory(hash);
        AtomicFile.WriteAllBytes(_path, content);
    }

    /// <summary>Removes the panic password — by writing random bytes, so the file looks as it did.</summary>
    public void Clear() => WriteRandom();

    private void WriteRandom() => AtomicFile.WriteAllBytes(_path, RandomNumberGenerator.GetBytes(FileSize));

    private static byte[] Derive(string password, ReadOnlySpan<byte> salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
