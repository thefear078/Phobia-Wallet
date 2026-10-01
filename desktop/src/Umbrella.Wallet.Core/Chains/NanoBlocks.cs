using System.Buffers.Binary;
using System.Numerics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Digests;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>What a node says an account is: the block to build on, the balance (raw), the representative.</summary>
public sealed record NanoAccountState(byte[] Frontier, BigInteger Balance, string Representative);

/// <summary>A block sent to the account that it has not pocketed yet.</summary>
public sealed record NanoReceivable(byte[] Hash, BigInteger Amount);

/// <summary>A signed state block, ready for <c>process</c>.</summary>
public sealed record NanoStateBlock(
    string Account, byte[] Previous, string Representative, BigInteger Balance, byte[] Link,
    byte[] Signature, ulong Work, byte[] Hash);

/// <summary>
/// Nano state blocks: the hash, the signature and the proof of work — the three things a send or a
/// receive needs before a node will accept it.
///
/// <para>The hash is BLAKE2b-256 over a fixed layout: a 32-byte preamble whose last byte is 6 (the state
/// block type), then account, previous, representative (32-byte public keys), balance (raw, 16 bytes
/// big-endian) and link (32 bytes — the destination's public key for a send, the source block's hash
/// for a receive).</para>
///
/// <para>The signature is ed25519 with Nano's hash: BLAKE2b-512 everywhere standard ed25519 uses
/// SHA-512 (the same substitution that makes the public key, see <see cref="NanoAccounts.PublicKey"/>).
/// A standard ed25519 signer produces a signature no node will accept.</para>
///
/// <para>The work is an 8-byte nonce whose BLAKE2b-64 together with the block's root (previous, or the
/// account key for an account's first block), read little-endian, reaches the network's threshold.</para>
///
/// <para>Pinned by <c>NanoBlockTests</c>: the signature and work to the Nano documentation's signed block,
/// the hash to a real mainnet block as the node reports it.</para>
/// </summary>
public static class NanoBlocks
{
    /// <summary>Since the V21 epoch: sends and changes need the higher threshold, receives the lower.</summary>
    public const ulong SendThreshold = 0xfffffff800000000;

    public const ulong ReceiveThreshold = 0xfffffe0000000000;

    /// <summary>The order of ed25519's base point group.</summary>
    private static readonly BigInteger L =
        BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");

    /// <summary>The hash a state block is signed and identified by.</summary>
    public static byte[] Hash(byte[] account, byte[] previous, byte[] representative, BigInteger balanceRaw, byte[] link)
    {
        Require(account, nameof(account));
        Require(previous, nameof(previous));
        Require(representative, nameof(representative));
        Require(link, nameof(link));
        if (balanceRaw.Sign < 0 || balanceRaw.GetByteCount(isUnsigned: true) > 16)
            throw new ArgumentOutOfRangeException(nameof(balanceRaw), "A Nano balance is 0 to 2^128-1 raw.");

        var preamble = new byte[32];
        preamble[31] = 6;
        var balance = new byte[16];
        var raw = balanceRaw.ToByteArray(isUnsigned: true, isBigEndian: true);
        raw.CopyTo(balance, 16 - raw.Length);

        var digest = new Blake2bDigest(256);
        foreach (var part in new[] { preamble, account, previous, representative, balance, link })
            digest.BlockUpdate(part, 0, part.Length);
        var hash = new byte[32];
        digest.DoFinal(hash, 0);
        return hash;
    }

    /// <summary>
    /// The 64-byte signature of <paramref name="message"/> (a block hash) by a 32-byte private key:
    /// h = BLAKE2b-512(key); a = clamp(h[0..32]); r = H(h[32..64] ‖ M) mod L; R = [r]B;
    /// k = H(R ‖ A ‖ M) mod L; S = (r + k·a) mod L; signature = R ‖ S.
    /// </summary>
    public static byte[] Sign(byte[] privateKey, byte[] message)
    {
        if (privateKey.Length != 32) throw new ArgumentException("A Nano private key is 32 bytes.", nameof(privateKey));
        var h = Blake2b512(privateKey);
        var scalar = h[..32];
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;
        try
        {
            var publicKey = AdaKeys.ScalarMultBase(scalar);
            var a = LeInt(scalar);
            var r = LeInt(Blake2b512(Concat(h[32..], message))) % L;
            var rPoint = AdaKeys.ScalarMultBase(ToLe32(r));
            var k = LeInt(Blake2b512(Concat(rPoint, publicKey, message))) % L;
            var s = (r + k * a) % L;
            return Concat(rPoint, ToLe32(s));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(h);
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

    /// <summary>The value a work nonce reaches for a root: BLAKE2b-64(nonce, little-endian ‖ root),
    /// read little-endian. The nonce is the 16-hex-digit work string as a big-endian number.</summary>
    public static ulong WorkValue(ulong work, byte[] root)
    {
        Require(root, nameof(root));
        Span<byte> input = stackalloc byte[40];
        BinaryPrimitives.WriteUInt64LittleEndian(input, work);
        root.CopyTo(input[8..]);
        return WorkValue(input);
    }

    public static bool IsWorkValid(ulong work, byte[] root, ulong threshold) => WorkValue(work, root) >= threshold;

    /// <summary>
    /// Finds a work nonce for <paramref name="root"/> on every core. At the send threshold that is
    /// about 2^29 hashes on average — seconds to a minute on a desktop CPU; receives need 64 times fewer.
    /// </summary>
    public static ulong GenerateWork(byte[] root, ulong threshold, CancellationToken ct = default)
    {
        Require(root, nameof(root));
        var found = 0UL;
        var done = 0;
        var workers = Math.Max(1, Environment.ProcessorCount);
        var start = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));

        var r0 = BinaryPrimitives.ReadUInt64LittleEndian(root.AsSpan(0, 8));
        var r1 = BinaryPrimitives.ReadUInt64LittleEndian(root.AsSpan(8, 8));
        var r2 = BinaryPrimitives.ReadUInt64LittleEndian(root.AsSpan(16, 8));
        var r3 = BinaryPrimitives.ReadUInt64LittleEndian(root.AsSpan(24, 8));

        Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = workers }, worker =>
        {
            // Each worker walks its own stride of the nonce space from a random start.
            for (var nonce = start + (ulong)worker; Volatile.Read(ref done) == 0; nonce += (ulong)workers)
            {
                if (FastWorkValue(nonce, r0, r1, r2, r3) >= threshold)
                {
                    if (Interlocked.CompareExchange(ref done, 1, 0) == 0) found = nonce;
                    return;
                }
                if ((nonce & 0xFFFF) == 0 && ct.IsCancellationRequested) return;
            }
        });

        ct.ThrowIfCancellationRequested();
        return found;
    }

    // --- Talking to a node ------------------------------------------------------------------------

    /// <summary><c>account_info</c> with the representative: the frontier to build on, the balance, and
    /// the representative a new block keeps.</summary>
    public static object AccountInfoRequest(string account) =>
        new { action = "account_info", account, representative = "true" };

    /// <summary>The account's state, or null when the node does not know the account (never opened) or
    /// the answer is malformed. <paramref name="unopened"/> tells the two apart.</summary>
    public static NanoAccountState? ParseAccountInfo(JsonElement root, out bool unopened)
    {
        unopened = false;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("error", out var error))
        {
            unopened = error.ValueKind == JsonValueKind.String &&
                       string.Equals(error.GetString(), "Account not found", StringComparison.OrdinalIgnoreCase);
            return null;
        }

        var frontier = HexField(root, "frontier");
        var representative = root.TryGetProperty("representative", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() : null;
        if (frontier is null || representative is null || NanoAccounts.TryDecode(representative) is null) return null;
        if (!root.TryGetProperty("balance", out var b) || b.ValueKind != JsonValueKind.String ||
            !BigInteger.TryParse(b.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var balance))
            return null;
        return new NanoAccountState(frontier, balance, representative);
    }

    /// <summary><c>receivable</c>: blocks sent to the account and not pocketed, with their amounts.</summary>
    public static object ReceivableRequest(string account, int count = 20) =>
        new { action = "receivable", account, count = count.ToString(CultureInfo.InvariantCulture), threshold = "1" };

    /// <summary>The receivable blocks, largest first. Both answer shapes are read: hash → amount, and
    /// hash → { amount, source }. A node that still says "pending" is read the same way.</summary>
    public static IReadOnlyList<NanoReceivable> ParseReceivable(JsonElement root)
    {
        var list = new List<NanoReceivable>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("blocks", out var blocks) ||
            blocks.ValueKind != JsonValueKind.Object)
            return list;   // "blocks": "" is how a node says none

        foreach (var entry in blocks.EnumerateObject())
        {
            if (entry.Name.Length != 64) continue;
            byte[] hash;
            try { hash = Convert.FromHexString(entry.Name); }
            catch (FormatException) { continue; }

            var amountText = entry.Value.ValueKind switch
            {
                JsonValueKind.String => entry.Value.GetString(),
                JsonValueKind.Object when entry.Value.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.String
                    => a.GetString(),
                _ => null,
            };
            if (BigInteger.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0)
                list.Add(new NanoReceivable(hash, amount));
        }

        return list.OrderByDescending(r => r.Amount).ToList();
    }

    /// <summary><c>process</c> for a signed block, in the JSON form a node accepts.</summary>
    public static object ProcessRequest(NanoStateBlock block, string subtype) => new
    {
        action = "process",
        json_block = "true",
        subtype,
        block = new
        {
            type = "state",
            account = block.Account,
            previous = Convert.ToHexString(block.Previous),
            representative = block.Representative,
            balance = block.Balance.ToString(CultureInfo.InvariantCulture),
            link = Convert.ToHexString(block.Link),
            signature = Convert.ToHexString(block.Signature),
            work = WorkHex(block.Work),
        },
    };

    /// <summary>The hash a node returns for a block it took, or null with its error.</summary>
    public static string? ParseProcessed(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object) { error = "unreadable answer"; return null; }
        if (root.TryGetProperty("error", out var e)) { error = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(); return null; }
        return root.TryGetProperty("hash", out var h) && h.ValueKind == JsonValueKind.String && h.GetString()!.Length == 64
            ? h.GetString()
            : null;
    }

    /// <summary>
    /// A signed block, work attached. <paramref name="previous"/> is all zeros for an account's first
    /// (open) block; its work root is then the account's own key.
    /// </summary>
    public static NanoStateBlock Build(
        byte[] privateKey, byte[] previous, string representative, BigInteger balance, byte[] link, ulong work)
    {
        var publicKey = NanoAccounts.PublicKey(privateKey);
        var rep = NanoAccounts.TryDecode(representative)
                  ?? throw new ArgumentException("Not a Nano address.", nameof(representative));
        var hash = Hash(publicKey, previous, rep, balance, link);
        return new NanoStateBlock(
            NanoAccounts.Address(publicKey), previous, NanoAccounts.Normalize(representative)!, balance, link,
            Sign(privateKey, hash), work, hash);
    }

    /// <summary>The root a block's work is computed over: previous, or the account key when there is none.</summary>
    public static byte[] WorkRoot(byte[] previous, byte[] accountPublicKey) =>
        previous.All(b => b == 0) ? accountPublicKey : previous;

    private static byte[]? HexField(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        var text = p.GetString();
        if (text is null || text.Length != 64) return null;
        try { return Convert.FromHexString(text); }
        catch (FormatException) { return null; }
    }

    /// <summary>A work nonce as the node prints it: 16 lower-case hex digits, big-endian.</summary>
    public static string WorkHex(ulong work) => work.ToString("x16");

    public static ulong ParseWork(string hex) =>
        ulong.Parse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

    // --- BLAKE2b-64 of exactly 40 bytes (nonce ‖ root), unrolled ------------------------------------
    // The work search hashes the same shape billions of times, so it skips the general digest: one
    // compression of a single, final 40-byte block with an 8-byte output, the message words read straight
    // from the nonce and the root. Checked against BouncyCastle's BLAKE2b on random inputs (NanoBlockTests).

    private static readonly ulong[] Iv =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
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
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
    ];

    /// <summary>The work value of <paramref name="nonce"/> for a root given as four little-endian words.</summary>
    public static ulong FastWorkValue(ulong nonce, ulong r0, ulong r1, ulong r2, ulong r3)
    {
        Span<ulong> m = stackalloc ulong[16];
        m[0] = nonce;
        m[1] = r0;
        m[2] = r1;
        m[3] = r2;
        m[4] = r3;

        var h0 = Iv[0] ^ 0x01010008UL;   // parameter block: digest length 8, no key, fanout 1, depth 1
        Span<ulong> v = stackalloc ulong[16];
        v[0] = h0; v[1] = Iv[1]; v[2] = Iv[2]; v[3] = Iv[3];
        v[4] = Iv[4]; v[5] = Iv[5]; v[6] = Iv[6]; v[7] = Iv[7];
        v[8] = Iv[0]; v[9] = Iv[1]; v[10] = Iv[2]; v[11] = Iv[3];
        v[12] = Iv[4] ^ 40UL;            // 40 bytes hashed
        v[13] = Iv[5];
        v[14] = ~Iv[6];                  // the last block
        v[15] = Iv[7];

        for (var round = 0; round < 12; round++)
        {
            var s = Sigma[round];
            G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }

        return h0 ^ v[0] ^ v[8];
    }

    private static void G(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = ulong.RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d];
        v[b] = ulong.RotateRight(v[b] ^ v[c], 63);
    }

    private static ulong WorkValue(ReadOnlySpan<byte> input)
    {
        var digest = new Blake2bDigest(64);
        digest.BlockUpdate(input);
        Span<byte> output = stackalloc byte[8];
        digest.DoFinal(output);
        return BinaryPrimitives.ReadUInt64LittleEndian(output);
    }

    private static byte[] Blake2b512(byte[] data)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(data, 0, data.Length);
        var output = new byte[64];
        digest.DoFinal(output, 0);
        return output;
    }

    private static void Require(byte[] value, string name)
    {
        if (value is null || value.Length != 32) throw new ArgumentException($"{name} must be 32 bytes.", name);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, at);
            at += part.Length;
        }
        return result;
    }

    private static BigInteger LeInt(byte[] le) => new(le, isUnsigned: true, isBigEndian: false);

    private static byte[] ToLe32(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var le = new byte[32];
        Array.Copy(raw, le, Math.Min(raw.Length, 32));
        return le;
    }
}
