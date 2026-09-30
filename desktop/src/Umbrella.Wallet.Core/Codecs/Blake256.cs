using System.Buffers.Binary;
using System.Numerics;

namespace Umbrella.Wallet.Core.Codecs;

/// <summary>
/// BLAKE-256 — the SHA-3 finalist (14 rounds), not BLAKE2. Decred hashes public keys and address
/// checksums with it, and nothing in .NET or BouncyCastle provides it, so it is written here from the
/// specification (Aumasson et al., "SHA-3 proposal BLAKE", v1.3) and pinned to the specification's own
/// test vectors (<c>Blake256Tests</c>) before anything is built on it.
///
/// Only one-shot hashing of short inputs is needed, so the message is padded whole rather than streamed.
/// </summary>
public static class Blake256
{
    private static readonly uint[] Iv =
    [
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
    ];

    // The first digits of pi.
    private static readonly uint[] C =
    [
        0x243F6A88, 0x85A308D3, 0x13198A2E, 0x03707344, 0xA4093822, 0x299F31D0, 0x082EFA98, 0xEC4E6C89,
        0x452821E6, 0x38D01377, 0xBE5466CF, 0x34E90C6C, 0xC0AC29B7, 0xC97C50DD, 0x3F84D5B5, 0xB5470917,
    ];

    private static readonly byte[][] Sigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
    ];

    private const int Rounds = 14;

    /// <summary>BLAKE-256 of <paramref name="data"/>, 32 bytes.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var h = (uint[])Iv.Clone();
        var bitLength = (ulong)data.Length * 8;
        var padded = Pad(data);

        for (var block = 0; block * 64 < padded.Length; block++)
        {
            // The counter is the number of MESSAGE bits up to and including this block — padding is not
            // counted — and a block holding padding only is compressed with a counter of zero.
            var start = (ulong)block * 512;
            var counter = start < bitLength ? Math.Min(bitLength, start + 512) : 0UL;
            Compress(h, padded.AsSpan(block * 64, 64), counter);
        }

        var digest = new byte[32];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4), h[i]);
        return digest;
    }

    /// <summary>BLAKE-256 applied twice: Decred's address checksum.</summary>
    public static byte[] DoubleHash(ReadOnlySpan<byte> data) => Hash(Hash(data));

    /// <summary>
    /// Appends a 1 bit, zeros up to 447 mod 512 bits, another 1 bit, then the 64-bit big-endian length.
    /// When exactly one byte is left for the two 1 bits, they share it (0x81).
    /// </summary>
    private static byte[] Pad(ReadOnlySpan<byte> data)
    {
        var n = data.Length;
        var total = ((n + 1 + 8 + 63) / 64) * 64;
        var padded = new byte[total];
        data.CopyTo(padded);
        padded[n] = 0x80;
        padded[total - 9] |= 0x01;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(total - 8), (ulong)n * 8);
        return padded;
    }

    private static void Compress(uint[] h, ReadOnlySpan<byte> block, ulong counter)
    {
        Span<uint> m = stackalloc uint[16];
        for (var i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);

        var t0 = (uint)counter;
        var t1 = (uint)(counter >> 32);

        Span<uint> v = stackalloc uint[16];
        for (var i = 0; i < 8; i++) v[i] = h[i];
        v[8] = C[0];            // the salt is zero, so s ^ c is just c
        v[9] = C[1];
        v[10] = C[2];
        v[11] = C[3];
        v[12] = t0 ^ C[4];
        v[13] = t0 ^ C[5];
        v[14] = t1 ^ C[6];
        v[15] = t1 ^ C[7];

        for (var r = 0; r < Rounds; r++)
        {
            var s = Sigma[r % 10];
            G(v, m, s, 0, 4, 8, 12, 0);
            G(v, m, s, 1, 5, 9, 13, 2);
            G(v, m, s, 2, 6, 10, 14, 4);
            G(v, m, s, 3, 7, 11, 15, 6);
            G(v, m, s, 0, 5, 10, 15, 8);
            G(v, m, s, 1, 6, 11, 12, 10);
            G(v, m, s, 2, 7, 8, 13, 12);
            G(v, m, s, 3, 4, 9, 14, 14);
        }

        for (var i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
    }

    private static void G(Span<uint> v, ReadOnlySpan<uint> m, byte[] s, int a, int b, int c, int d, int i)
    {
        v[a] += v[b] + (m[s[i]] ^ C[s[i + 1]]);
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
        v[a] += v[b] + (m[s[i + 1]] ^ C[s[i]]);
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
    }
}
