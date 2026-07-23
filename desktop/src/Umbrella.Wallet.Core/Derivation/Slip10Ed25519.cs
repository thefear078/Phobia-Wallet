using System.Security.Cryptography;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// SLIP-0010 hierarchical key derivation over the ed25519 curve.
///
/// ed25519 supports hardened derivation only, so every path segment is hardened. This is the
/// scheme Solana wallets (Phantom, solana-keygen) use for m/44'/501'/0'/0'. Verified against the
/// official SLIP-0010 ed25519 test vectors in the test suite.
/// </summary>
public static class Slip10Ed25519
{
    private const uint HardenedOffset = 0x80000000u;
    private static readonly byte[] Curve = "ed25519 seed"u8.ToArray();

    /// <summary>
    /// Derives the 32-byte ed25519 private scalar seed at the given hardened path from a BIP39 seed.
    /// Each index is hardened automatically; pass unhardened numbers (e.g. 44, 501, 0, 0).
    /// </summary>
    public static byte[] DerivePrivateKey(byte[] bip39Seed, IReadOnlyList<uint> path)
    {
        var i = HmacSha512(Curve, bip39Seed);
        var key = i[..32];
        var chainCode = i[32..];

        foreach (var index in path)
        {
            var hardened = index | HardenedOffset;
            // data = 0x00 || key(32) || ser32(hardenedIndex)
            var data = new byte[1 + 32 + 4];
            data[0] = 0x00;
            Buffer.BlockCopy(key, 0, data, 1, 32);
            data[33] = (byte)(hardened >> 24);
            data[34] = (byte)(hardened >> 16);
            data[35] = (byte)(hardened >> 8);
            data[36] = (byte)hardened;

            i = HmacSha512(chainCode, data);
            key = i[..32];
            chainCode = i[32..];
        }

        return key;
    }

    /// <summary>Ed25519 public key (32 bytes) for a SLIP-0010 private scalar seed.</summary>
    public static byte[] PublicKey(byte[] privateKey)
    {
        var pub = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(privateKey, 0, pub, 0);
        return pub;
    }

    private static byte[] HmacSha512(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA512(key);
        return hmac.ComputeHash(data);
    }
}
