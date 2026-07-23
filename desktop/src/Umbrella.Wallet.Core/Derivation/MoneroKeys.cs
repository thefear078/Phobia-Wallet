using System.Numerics;
using System.Reflection;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>A complete Monero account: the two secret keys and the mainnet primary address.</summary>
public sealed record MoneroWallet(
    string Address,
    string SecretSpendKeyHex,
    string SecretViewKeyHex,
    string PublicSpendKeyHex,
    string PublicViewKeyHex);

/// <summary>
/// Monero key and address construction.
///
/// Monero cannot be derived through BIP44 — it has no such path and its scalars are reduced mod
/// the ed25519 group order rather than clamped. So the spend key is derived from the vault seed
/// through an explicit, documented Umbrella path, and the resulting account is exported as
/// (address + secret spend key + secret view key), which is exactly what "Restore from keys" in
/// Feather / monero-wallet-cli consumes. That keeps the account portable outside Umbrella.
///
/// Every primitive here is pinned in <c>MoneroKeysTests</c>: the ed25519 base point, Keccak-256,
/// and Monero's base58 (verified by decoding a real published mainnet address and re-checking
/// its keccak checksum). Nothing ships unless those pass.
/// </summary>
public static class MoneroKeys
{
    /// <summary>Mainnet standard address prefix (18 = 0x12).</summary>
    public const byte MainnetPrefix = 18;

    /// <summary>Domain separator so the Monero key can never collide with another chain's.</summary>
    private const string DerivationDomain = "umbrella-monero-v1";

    /// <summary>ed25519 group order ℓ = 2^252 + 27742317777372353535851937790883648493.</summary>
    private static readonly BigInteger GroupOrder =
        BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");

    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Monero base58 encodes 8-byte blocks; a partial block maps to fewer characters.</summary>
    private static readonly int[] EncodedBlockSizes = [0, 2, 3, 5, 6, 7, 9, 10, 11];

    private static readonly MethodInfo ScalarMultBaseEncoded =
        typeof(Org.BouncyCastle.Math.EC.Rfc8032.Ed25519).GetMethod(
            "ScalarMultBaseEncoded",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(byte[]), typeof(byte[]), typeof(int)],
            null)
        ?? throw new InvalidOperationException(
            "BouncyCastle no longer exposes Ed25519.ScalarMultBaseEncoded — Monero support must be re-verified.");

    /// <summary>Builds the Monero account for a wallet seed (the BIP39 seed bytes).</summary>
    public static MoneroWallet FromSeed(byte[] walletSeed)
    {
        ArgumentNullException.ThrowIfNull(walletSeed);

        var domain = Encoding.UTF8.GetBytes(DerivationDomain);
        var material = new byte[domain.Length + walletSeed.Length];
        Buffer.BlockCopy(domain, 0, material, 0, domain.Length);
        Buffer.BlockCopy(walletSeed, 0, material, domain.Length, walletSeed.Length);

        // Monero secret keys are scalars reduced mod ℓ (sc_reduce32), never clamped.
        var spend = ScReduce32(Keccak256(material));
        var view = ScReduce32(Keccak256(spend));

        var publicSpend = ScalarMultBase(spend);
        var publicView = ScalarMultBase(view);

        return new MoneroWallet(
            BuildAddress(MainnetPrefix, publicSpend, publicView),
            Convert.ToHexString(spend).ToLowerInvariant(),
            Convert.ToHexString(view).ToLowerInvariant(),
            Convert.ToHexString(publicSpend).ToLowerInvariant(),
            Convert.ToHexString(publicView).ToLowerInvariant());
    }

    /// <summary>address = base58( prefix ‖ publicSpend ‖ publicView ‖ keccak256(that)[..4] ).</summary>
    public static string BuildAddress(byte prefix, byte[] publicSpend, byte[] publicView)
    {
        var body = new byte[1 + 32 + 32];
        body[0] = prefix;
        Buffer.BlockCopy(publicSpend, 0, body, 1, 32);
        Buffer.BlockCopy(publicView, 0, body, 33, 32);

        var checksum = Keccak256(body);
        var full = new byte[body.Length + 4];
        Buffer.BlockCopy(body, 0, full, 0, body.Length);
        Buffer.BlockCopy(checksum, 0, full, body.Length, 4);
        return EncodeBase58(full);
    }

    /// <summary>Decodes an address and verifies its checksum. Used to validate user input.</summary>
    public static bool TryDecodeAddress(
        string address, out byte prefix, out byte[] publicSpend, out byte[] publicView)
    {
        prefix = 0;
        publicSpend = [];
        publicView = [];
        try
        {
            var raw = DecodeBase58(address);
            if (raw.Length != 69) return false;

            var body = raw[..65];
            var expected = Keccak256(body)[..4];
            if (!raw[65..].SequenceEqual(expected)) return false;

            prefix = body[0];
            publicSpend = body[1..33];
            publicView = body[33..65];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reduces a 32-byte little-endian value modulo ℓ, returning 32 little-endian bytes.</summary>
    public static byte[] ScReduce32(byte[] input)
    {
        // Monero scalars are little-endian; BigInteger is fed big-endian and unsigned so the
        // top bit can never be read as a sign.
        var le = input[..32];
        var be = le.Reverse().ToArray();
        var reduced = new BigInteger(be, isUnsigned: true, isBigEndian: true) % GroupOrder;

        var outLe = new byte[32];
        var bytes = reduced.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(bytes, 0, outLe, 0, Math.Min(bytes.Length, 32));
        return outLe;
    }

    /// <summary>Raw ed25519 scalar × basepoint (no SHA-512 pre-hash, no clamping) — Monero's convention.</summary>
    public static byte[] ScalarMultBase(byte[] scalar)
    {
        var result = new byte[32];
        ScalarMultBaseEncoded.Invoke(null, [scalar, result, 0]);
        return result;
    }

    /// <summary>Keccak-256 (the original submission, which Monero uses — not NIST SHA3-256).</summary>
    public static byte[] Keccak256(byte[] data)
    {
        var digest = new KeccakDigest(256);
        digest.BlockUpdate(data, 0, data.Length);
        var hash = new byte[32];
        digest.DoFinal(hash, 0);
        return hash;
    }

    public static string EncodeBase58(byte[] data)
    {
        var sb = new StringBuilder();
        var fullBlocks = data.Length / 8;
        var remainder = data.Length % 8;

        for (var i = 0; i < fullBlocks; i++)
        {
            EncodeBlock(data.AsSpan(i * 8, 8), sb);
        }

        if (remainder > 0)
        {
            EncodeBlock(data.AsSpan(fullBlocks * 8, remainder), sb);
        }

        return sb.ToString();
    }

    public static byte[] DecodeBase58(string value)
    {
        var output = new List<byte>();
        var fullBlocks = value.Length / 11;
        var remainder = value.Length % 11;

        for (var i = 0; i < fullBlocks; i++)
        {
            output.AddRange(DecodeBlock(value.Substring(i * 11, 11), 8));
        }

        if (remainder > 0)
        {
            var size = Array.IndexOf(EncodedBlockSizes, remainder);
            if (size < 0) throw new FormatException("Invalid Monero base58 length.");
            output.AddRange(DecodeBlock(value.Substring(fullBlocks * 11, remainder), size));
        }

        return output.ToArray();
    }

    private static void EncodeBlock(ReadOnlySpan<byte> block, StringBuilder sb)
    {
        // Each block is a big-endian unsigned integer rendered in base58 and left-padded.
        BigInteger number = 0;
        foreach (var b in block)
        {
            number = number * 256 + b;
        }

        var target = EncodedBlockSizes[block.Length];
        var chars = new char[target];
        for (var i = target - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(number % 58)];
            number /= 58;
        }

        sb.Append(chars);
    }

    private static byte[] DecodeBlock(string block, int byteCount)
    {
        BigInteger number = 0;
        foreach (var c in block)
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0) throw new FormatException($"Invalid Monero base58 character '{c}'.");
            number = number * 58 + index;
        }

        var bytes = new byte[byteCount];
        for (var i = byteCount - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(number % 256);
            number /= 256;
        }

        if (number != 0) throw new FormatException("Monero base58 block overflow.");
        return bytes;
    }
}
