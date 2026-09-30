using System.Text.Json;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Crypto.Digests;
using Umbrella.Wallet.Core.Codecs;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What a mainnet Decred address pays to.</summary>
public enum DecredAddressKind
{
    /// <summary>Ds… — pay to the hash of a secp256k1 public key.</summary>
    PublicKeyHash,

    /// <summary>Dc… — pay to script hash.</summary>
    ScriptHash,
}

/// <summary>
/// Decred mainnet addresses (receive and balance).
///
/// A Decred address looks like Bitcoin's and is not: the public-key hash is RIPEMD-160 of a
/// <b>BLAKE-256</b> digest (not SHA-256), the version prefix is two bytes (0x073F renders as "Ds",
/// 0x071A as "Dc"), and the Base58 checksum is the first four bytes of a DOUBLE BLAKE-256. A Bitcoin
/// library would encode the same key into a valid-looking address nobody could spend from, so the
/// whole path is pinned to Decred's own dcrd vectors and to Trust Wallet's phrase-to-address vectors
/// (<c>DecredReceiveTests</c>).
/// </summary>
public static class DecredAddress
{
    private static readonly byte[] PubKeyHashPrefix = [0x07, 0x3F];   // Ds
    private static readonly byte[] ScriptHashPrefix = [0x07, 0x1A];   // Dc

    /// <summary>BIP44 coin type 42 — the path Trust Wallet, Ledger and Exodus derive.</summary>
    public const int CoinType = 42;

    /// <summary>1 DCR is 10^8 atoms.</summary>
    public const decimal AtomsPerDcr = 100_000_000m;

    /// <summary>RIPEMD-160(BLAKE-256(data)) — Decred's Hash160.</summary>
    public static byte[] Hash160(ReadOnlySpan<byte> data)
    {
        var blake = Blake256.Hash(data);
        var ripemd = new RipeMD160Digest();
        ripemd.BlockUpdate(blake, 0, blake.Length);
        var output = new byte[20];
        ripemd.DoFinal(output, 0);
        return output;
    }

    /// <summary>The Ds… address of a 33-byte compressed secp256k1 public key.</summary>
    public static string FromPublicKey(ReadOnlySpan<byte> compressedPublicKey)
    {
        if (compressedPublicKey.Length != 33)
            throw new ArgumentException("A Decred address is made from a 33-byte compressed public key.", nameof(compressedPublicKey));
        return Encode(PubKeyHashPrefix, Hash160(compressedPublicKey));
    }

    /// <summary>The Ds… address of a 20-byte public-key hash.</summary>
    public static string FromPublicKeyHash(ReadOnlySpan<byte> hash160) => Encode(PubKeyHashPrefix, hash160);

    /// <summary>The kind and hash an address carries, or null when it is not a valid mainnet Decred address.</summary>
    public static (DecredAddressKind Kind, byte[] Hash)? TryDecode(string? address)
    {
        byte[] raw;
        try
        {
            raw = Encoders.Base58.DecodeData((address ?? string.Empty).Trim());
        }
        catch (FormatException)
        {
            return null;
        }

        // 2 version bytes + 20-byte hash + 4-byte checksum.
        if (raw.Length != 26) return null;
        var body = raw.AsSpan(0, 22);
        var checksum = Blake256.DoubleHash(body)[..4];
        if (!checksum.AsSpan().SequenceEqual(raw.AsSpan(22, 4))) return null;

        var hash = raw[2..22];
        if (raw[0] == PubKeyHashPrefix[0] && raw[1] == PubKeyHashPrefix[1]) return (DecredAddressKind.PublicKeyHash, hash);
        if (raw[0] == ScriptHashPrefix[0] && raw[1] == ScriptHashPrefix[1]) return (DecredAddressKind.ScriptHash, hash);
        return null;
    }

    public static bool IsValid(string? address) => TryDecode(address) is not null;

    /// <summary>
    /// The balance in a dcrdata <c>/address/{addr}/totals</c> answer: unspent DCR. An address the chain
    /// has never seen answers with zeros, which is a real zero; anything that is not this shape is null
    /// (unknown), never 0.
    /// </summary>
    public static decimal? ParseTotals(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("dcr_unspent", out var unspent)) return null;
        return unspent.ValueKind == JsonValueKind.Number && unspent.TryGetDecimal(out var dcr) && dcr >= 0 ? dcr : null;
    }

    private static string Encode(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> hash160)
    {
        if (hash160.Length != 20) throw new ArgumentException("A public-key hash is 20 bytes.", nameof(hash160));
        var body = new byte[22];
        prefix.CopyTo(body);
        hash160.CopyTo(body.AsSpan(2));
        var checksum = Blake256.DoubleHash(body)[..4];
        return Encoders.Base58.EncodeData([.. body, .. checksum]);
    }
}
