using System.Buffers.Binary;
using Umbrella.Wallet.Core.Codecs;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// One input of a Decred transaction. Unlike Bitcoin, the input carries a "fraud proof" in its witness:
/// the amount, block height and position-in-block of the coin it spends. dcrd's mempool compares all
/// three with its own record of that coin and refuses the transaction if any differs.
/// </summary>
/// <param name="PrevHash">The spent transaction's id in wire order (the reverse of how explorers print it).</param>
/// <param name="Tree">0 for the regular transaction tree, 1 for the stake tree.</param>
public sealed record DecredTxIn(
    byte[] PrevHash,
    uint PrevIndex,
    byte Tree,
    uint Sequence,
    long ValueIn,
    uint BlockHeight,
    uint BlockIndex,
    byte[] SignatureScript);

/// <summary>One output: an amount in atoms and the script it pays to (script version 0 for every standard one).</summary>
public sealed record DecredTxOut(long Value, ushort ScriptVersion, byte[] PkScript);

/// <summary>A Decred transaction as dcrd's <c>wire.MsgTx</c> holds it.</summary>
public sealed record DecredTx(
    ushort Version,
    IReadOnlyList<DecredTxIn> Inputs,
    IReadOnlyList<DecredTxOut> Outputs,
    uint LockTime,
    uint Expiry);

/// <summary>
/// Decred's transaction format and signature hash, written from dcrd's <c>wire/msgtx.go</c> and
/// <c>txscript/sighash.go</c> and checked against real mainnet transactions (<c>DecredTransactionTests</c>):
/// a signature this code computes the hash for verifies against the key that signed it on the network.
///
/// Decred looks like Bitcoin and is not. The transaction is split into a prefix (inputs' outpoints and
/// sequences, outputs, lock time, expiry) and a witness (each input's fraud proof and signature script);
/// the transaction id is BLAKE-256 of the prefix alone, so it is fixed before anything is signed. The
/// signature hash is BLAKE-256 over the hash type, the prefix hash and a witness hash in which only the
/// input being signed carries a script.
/// </summary>
public static class DecredTransactions
{
    /// <summary>The transaction version every regular Decred transaction uses.</summary>
    public const ushort TxVersion = 1;

    /// <summary>SIGHASH_ALL: the signature commits to every input and output.</summary>
    public const uint SigHashAll = 1;

    /// <summary>dcrd's default minimum relay fee: 0.0001 DCR per 1000 bytes. Also the base of its dust rule.</summary>
    public const long MinRelayFeePerKb = 10_000;

    /// <summary>
    /// The fee rate this wallet pays: one and a half times the relay minimum, so a transaction is never
    /// a byte away from being refused. About 0.00004 DCR for a one-coin payment.
    /// </summary>
    public const long FeePerKb = 15_000;

    /// <summary>A sequence of all ones: final, no relative lock.</summary>
    public const uint FinalSequence = 0xFFFF_FFFF;

    // wire.TxSerializeType, in the top 16 bits of the serialised version.
    private const uint SerializeFull = 0;
    private const uint SerializeNoWitness = 1;

    // txscript.SigHashSerializeType: what the signature hash's two halves are serialised as.
    private const uint SigHashSerializePrefix = 1;
    private const uint SigHashSerializeWitness = 3;

    /// <summary>The whole transaction, prefix and witness, as it is broadcast.</summary>
    public static byte[] Serialize(DecredTx tx)
    {
        var w = new Writer();
        w.UInt32(tx.Version | (SerializeFull << 16));
        WritePrefix(w, tx);
        WriteWitness(w, tx);
        return w.ToArray();
    }

    /// <summary>The prefix serialisation the transaction id is the hash of.</summary>
    public static byte[] SerializePrefix(DecredTx tx)
    {
        var w = new Writer();
        w.UInt32(tx.Version | (SerializeNoWitness << 16));
        WritePrefix(w, tx);
        return w.ToArray();
    }

    /// <summary>The transaction id as explorers print it: BLAKE-256 of the prefix, byte-reversed hex.</summary>
    public static string TxId(DecredTx tx)
    {
        var hash = Blake256.Hash(SerializePrefix(tx));
        Array.Reverse(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The 32 bytes input <paramref name="index"/> signs, for SIGHASH_ALL: BLAKE-256 of the hash type,
    /// the prefix hash and the witness hash, where the witness hash commits to <paramref name="signScript"/>
    /// (the spent output's script) for this input and to an empty script for every other one.
    /// </summary>
    public static byte[] SignatureHash(DecredTx tx, int index, byte[] signScript, uint hashType = SigHashAll)
    {
        if (hashType != SigHashAll)
            throw new NotSupportedException("Only SIGHASH_ALL is built here.");
        if (index < 0 || index >= tx.Inputs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        // SIGHASH_ALL commits to the prefix exactly as serialised: the same bytes the id hashes.
        var prefix = new Writer();
        prefix.UInt32(tx.Version | (SigHashSerializePrefix << 16));
        WritePrefix(prefix, tx);
        var prefixHash = Blake256.Hash(prefix.ToArray());

        var witness = new Writer();
        witness.UInt32(tx.Version | (SigHashSerializeWitness << 16));
        witness.VarInt((ulong)tx.Inputs.Count);
        for (var i = 0; i < tx.Inputs.Count; i++)
            witness.VarBytes(i == index ? signScript : []);
        var witnessHash = Blake256.Hash(witness.ToArray());

        var final = new byte[4 + 32 + 32];
        BinaryPrimitives.WriteUInt32LittleEndian(final, hashType);
        prefixHash.CopyTo(final, 4);
        witnessHash.CopyTo(final, 36);
        return Blake256.Hash(final);
    }

    /// <summary>Reads a fully serialised transaction (prefix and witness). Throws on anything malformed.</summary>
    public static DecredTx Parse(ReadOnlySpan<byte> raw)
    {
        var r = new Reader(raw.ToArray());
        var version = r.UInt32();
        if (version >> 16 != SerializeFull)
            throw new FormatException("Only a full (prefix and witness) serialisation can be read.");

        var inputCount = checked((int)r.VarInt());
        var prefixes = new List<(byte[] Hash, uint Index, byte Tree, uint Sequence)>(inputCount);
        for (var i = 0; i < inputCount; i++)
            prefixes.Add((r.Bytes(32), r.UInt32(), r.Byte(), r.UInt32()));

        var outputCount = checked((int)r.VarInt());
        var outputs = new List<DecredTxOut>(outputCount);
        for (var i = 0; i < outputCount; i++)
            outputs.Add(new DecredTxOut(r.Int64(), r.UInt16(), r.VarBytes()));

        var lockTime = r.UInt32();
        var expiry = r.UInt32();

        var witnessCount = checked((int)r.VarInt());
        if (witnessCount != inputCount)
            throw new FormatException("The witness does not cover every input.");

        var inputs = new List<DecredTxIn>(inputCount);
        for (var i = 0; i < inputCount; i++)
        {
            var (hash, index, tree, sequence) = prefixes[i];
            inputs.Add(new DecredTxIn(hash, index, tree, sequence, r.Int64(), r.UInt32(), r.UInt32(), r.VarBytes()));
        }

        if (!r.AtEnd) throw new FormatException("Bytes left over after the transaction.");
        return new DecredTx((ushort)(version & 0xFFFF), inputs, outputs, lockTime, expiry);
    }

    /// <summary>OP_DUP OP_HASH160 &lt;20&gt; OP_EQUALVERIFY OP_CHECKSIG — the same opcodes as Bitcoin.</summary>
    public static byte[] PayToPubKeyHash(ReadOnlySpan<byte> hash160)
    {
        if (hash160.Length != 20) throw new ArgumentException("A public-key hash is 20 bytes.", nameof(hash160));
        return [0x76, 0xA9, 0x14, .. hash160, 0x88, 0xAC];
    }

    /// <summary>OP_HASH160 &lt;20&gt; OP_EQUAL.</summary>
    public static byte[] PayToScriptHash(ReadOnlySpan<byte> hash160)
    {
        if (hash160.Length != 20) throw new ArgumentException("A script hash is 20 bytes.", nameof(hash160));
        return [0xA9, 0x14, .. hash160, 0x87];
    }

    /// <summary>The output script a mainnet address is paid with, or null when it is not one.</summary>
    public static byte[]? ScriptFor(string? address) => DecredAddress.TryDecode(address) switch
    {
        { Kind: DecredAddressKind.PublicKeyHash } d => PayToPubKeyHash(d.Hash),
        { Kind: DecredAddressKind.ScriptHash } d => PayToScriptHash(d.Hash),
        _ => null,
    };

    /// <summary>&lt;DER signature + hash type&gt; &lt;compressed public key&gt;: what spends a pay-to-pubkey-hash output.</summary>
    public static byte[] SignatureScript(ReadOnlySpan<byte> derSignature, ReadOnlySpan<byte> compressedPublicKey, byte hashType = (byte)SigHashAll)
    {
        if (compressedPublicKey.Length != 33) throw new ArgumentException("A compressed public key is 33 bytes.", nameof(compressedPublicKey));
        var signature = derSignature.Length + 1;
        if (signature > 75) throw new ArgumentException("A DER signature is at most 72 bytes.", nameof(derSignature));
        return [(byte)signature, .. derSignature, hashType, 33, .. compressedPublicKey];
    }

    /// <summary>The signature (with its hash type byte) and public key a pay-to-pubkey-hash signature script pushes.</summary>
    public static (byte[] Signature, byte[] PublicKey)? ReadSignatureScript(ReadOnlySpan<byte> script)
    {
        if (script.Length < 2) return null;
        var sigLength = script[0];
        if (sigLength is < 9 or > 73 || script.Length < 2 + sigLength) return null;
        if (script.Length != 1 + sigLength + 1 + script[1 + sigLength]) return null;
        var keyLength = script[1 + sigLength];
        if (keyLength is not (33 or 65)) return null;
        return (script.Slice(1, sigLength).ToArray(), script.Slice(2 + sigLength, keyLength).ToArray());
    }

    // dcrwallet's size estimates (wallet/txsizes): the largest a standard signature can make an input,
    // so a fee priced on them is never short.
    /// <summary>A pay-to-pubkey-hash input: outpoint 37, sequence 4, fraud proof 16, script length 1, script 108.</summary>
    public const int P2pkhInputSize = 32 + 4 + 1 + 4 + 8 + 4 + 4 + 1 + 108;

    /// <summary>The size of an output paying to a script of <paramref name="scriptLength"/> bytes.</summary>
    public static int OutputSize(int scriptLength) => 8 + 2 + VarIntSize((ulong)scriptLength) + scriptLength;

    /// <summary>Serialised size of a pay-to-pubkey-hash spend with these outputs (upper bound).</summary>
    public static int EstimateSize(int inputs, IEnumerable<int> outputScriptLengths)
    {
        var outputs = outputScriptLengths.ToList();
        // Version 4, lock time 4, expiry 4; input count twice (prefix and witness); output count.
        return 12 + 2 * VarIntSize((ulong)inputs) + VarIntSize((ulong)outputs.Count)
            + inputs * P2pkhInputSize + outputs.Sum(OutputSize);
    }

    /// <summary>The fee for a transaction of <paramref name="size"/> bytes at <see cref="FeePerKb"/>.</summary>
    public static long FeeFor(int size) => size * FeePerKb / 1000;

    /// <summary>
    /// dcrd's dust rule (<c>mempool.isDust</c>): an output is dust when what it is worth is less than
    /// three times the relay fee for its own bytes plus the 165 it costs to spend. A standard
    /// pay-to-pubkey-hash output lands on 6030 atoms. A node refuses a transaction with a dust output.
    /// </summary>
    public static long DustThreshold(int scriptLength)
    {
        var total = OutputSize(scriptLength) + 165;
        // amount * 1000 / (3 * total) < relayFee  <=>  amount < ceil(3 * total * relayFee / 1000)
        return (3L * total * MinRelayFeePerKb + 999) / 1000;
    }

    private static void WritePrefix(Writer w, DecredTx tx)
    {
        w.VarInt((ulong)tx.Inputs.Count);
        foreach (var input in tx.Inputs)
        {
            if (input.PrevHash.Length != 32) throw new ArgumentException("An outpoint hash is 32 bytes.");
            w.Bytes(input.PrevHash);
            w.UInt32(input.PrevIndex);
            w.Byte(input.Tree);
            w.UInt32(input.Sequence);
        }

        w.VarInt((ulong)tx.Outputs.Count);
        foreach (var output in tx.Outputs)
        {
            w.Int64(output.Value);
            w.UInt16(output.ScriptVersion);
            w.VarBytes(output.PkScript);
        }

        w.UInt32(tx.LockTime);
        w.UInt32(tx.Expiry);
    }

    private static void WriteWitness(Writer w, DecredTx tx)
    {
        w.VarInt((ulong)tx.Inputs.Count);
        foreach (var input in tx.Inputs)
        {
            w.Int64(input.ValueIn);
            w.UInt32(input.BlockHeight);
            w.UInt32(input.BlockIndex);
            w.VarBytes(input.SignatureScript);
        }
    }

    private static int VarIntSize(ulong value) => value switch
    {
        < 0xFD => 1,
        <= 0xFFFF => 3,
        <= 0xFFFF_FFFF => 5,
        _ => 9,
    };

    private sealed class Writer
    {
        private readonly List<byte> _bytes = new(256);

        public void Byte(byte b) => _bytes.Add(b);
        public void Bytes(ReadOnlySpan<byte> b) { foreach (var x in b) _bytes.Add(x); }

        public void UInt16(ushort v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(s, v); Bytes(s); }
        public void UInt32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); Bytes(s); }
        public void Int64(long v) { Span<byte> s = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(s, v); Bytes(s); }

        // Bitcoin's CompactSize, which dcrd's WriteVarInt also writes.
        public void VarInt(ulong v)
        {
            switch (v)
            {
                case < 0xFD: Byte((byte)v); break;
                case <= 0xFFFF: Byte(0xFD); UInt16((ushort)v); break;
                case <= 0xFFFF_FFFF: Byte(0xFE); UInt32((uint)v); break;
                default:
                    Byte(0xFF);
                    Span<byte> s = stackalloc byte[8];
                    BinaryPrimitives.WriteUInt64LittleEndian(s, v);
                    Bytes(s);
                    break;
            }
        }

        public void VarBytes(ReadOnlySpan<byte> b) { VarInt((ulong)b.Length); Bytes(b); }

        public byte[] ToArray() => [.. _bytes];
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public bool AtEnd => _at == data.Length;

        public byte[] Bytes(int n)
        {
            if (n < 0 || _at + n > data.Length) throw new FormatException("The transaction ends early.");
            var b = data.AsSpan(_at, n).ToArray();
            _at += n;
            return b;
        }

        public byte Byte() => Bytes(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Bytes(8));

        public ulong VarInt()
        {
            var first = Byte();
            return first switch
            {
                0xFD => UInt16(),
                0xFE => UInt32(),
                0xFF => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8)),
                _ => first,
            };
        }

        public byte[] VarBytes() => Bytes(checked((int)VarInt()));
    }
}
