using System.Security.Cryptography;
using System.Text;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The sealed files (notes, exchange keys, address book, explorer key) moved to HKDF-SHA256 for the
/// Altumptic baseline (PSB §2.1). What was written before must still open — and be rewritten in the new
/// format the moment it is read.
/// </summary>
public sealed class SealedFileTests : IDisposable
{
    private const string Secret = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Domain = "umbrella-tx-notes-v1";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-sealed-{Guid.NewGuid():N}");

    public SealedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>A file exactly as the stores wrote it before: nonce | tag | ciphertext, key SHA-256(domain:secret).</summary>
    private static byte[] V1(string plaintext, string secret = Secret, string domain = Domain)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(domain + ":" + secret));
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    [Fact]
    public void A_sealed_file_opens_with_its_secret_and_nothing_else()
    {
        var path = Path.Combine(_dir, "notes.bin");
        SealedFile.Write(path, Encoding.UTF8.GetBytes("{\"tx\":\"salary\"}"), Secret, Domain);

        Assert.Equal("PSB2"u8.ToArray(), File.ReadAllBytes(path)[..4]);
        Assert.DoesNotContain("salary", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal("{\"tx\":\"salary\"}", Encoding.UTF8.GetString(SealedFile.Read(path, Secret, Domain)!));
        Assert.Null(SealedFile.Read(path, Secret + " x", Domain));
    }

    [Fact]
    public void A_file_of_one_store_does_not_open_as_another()
    {
        var path = Path.Combine(_dir, "exchanges.bin");
        SealedFile.Write(path, "[]"u8.ToArray(), Secret, "umbrella-exchange-keys-v1");

        Assert.Null(SealedFile.Read(path, Secret, "umbrella-address-book-v1"));
    }

    [Fact]
    public void A_file_written_before_opens_and_is_rewritten_in_the_new_format()
    {
        var path = Path.Combine(_dir, "old.bin");
        File.WriteAllBytes(path, V1("{\"tx\":\"coffee\"}"));

        Assert.Equal("{\"tx\":\"coffee\"}", Encoding.UTF8.GetString(SealedFile.Read(path, Secret, Domain)!));

        // Rewritten on that read: v2 now, still the same notes, and the old key no longer opens it.
        var upgraded = File.ReadAllBytes(path);
        Assert.Equal("PSB2"u8.ToArray(), upgraded[..4]);
        Assert.Equal("{\"tx\":\"coffee\"}", Encoding.UTF8.GetString(SealedFile.Open(upgraded, Secret, Domain, out var legacy)!));
        Assert.False(legacy);
    }

    [Fact]
    public void A_changed_byte_is_refused_not_misread()
    {
        var path = Path.Combine(_dir, "notes.bin");
        SealedFile.Write(path, "{\"a\":\"b\"}"u8.ToArray(), Secret, Domain);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);

        Assert.Null(SealedFile.Read(path, Secret, Domain));
    }

    [Fact]
    public async Task The_notes_store_reads_an_old_file_and_keeps_every_note()
    {
        var path = Path.Combine(_dir, "tx-notes-main.bin");
        File.WriteAllBytes(path, V1("{\"abc\":\"rent\",\"def\":\"gift\"}"));

        var notes = await new TxNoteStore(path: path).LoadAsync(Secret);

        Assert.Equal(new Dictionary<string, string> { ["abc"] = "rent", ["def"] = "gift" }, notes);
        Assert.Equal("PSB2"u8.ToArray(), File.ReadAllBytes(path)[..4]);
    }
}
