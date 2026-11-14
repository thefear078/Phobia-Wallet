using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NBitcoin;
using NBitcoin.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Zcash sends are v5 transactions (ZIP-225) signed with the ZIP-244 signature hash: NU7, scheduled for
/// mainnet block 3,543,405, accepts v5 or v6 only (ZIP-2003), so a v4 send would be refused from then on.
///
/// Pinned two ways. Zcash's own ZIP-244 vectors: every transaction id, and every SIGHASH_ALL for a
/// transparent input. The vectors carry Sapling and Orchard parts the wallet never builds, so this test
/// computes their digests itself (ZIP-244 T.3, T.4) and hands them in. And a v5 transaction mainnet
/// accepted: rebuilt byte for byte from its parts, its id recomputed, and every one of its six
/// signatures verified over the digest computed here.
/// </summary>
public sealed class ZcashV5Tests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    public static IEnumerable<object[]> Vectors()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("zcash-zip244-vectors.json")));
        var i = 0;
        foreach (var row in doc.RootElement.GetProperty("vectors").EnumerateArray())
            yield return [i++, row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Official_vector_txid_and_sighash_all(int number, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement;
        var tx = Parsed.From(Convert.FromHexString(row[0].GetString()!));
        var amounts = row[2].EnumerateArray().Select(a => (ulong)a.GetInt64()).ToList();
        var scripts = row[3].EnumerateArray().Select(s => Convert.FromHexString(s.GetString()!)).ToList();

        Assert.Equal(ZcashTransactions.VersionV5, tx.Version);
        Assert.Equal(ZcashTransactions.VersionGroupIdV5, tx.VersionGroupId);

        var inputs = tx.Inputs.Select((x, k) => new ZcashInput(x.TxId, x.Index,
            k < amounts.Count ? amounts[k] : 0, k < scripts.Count ? scripts[k] : []) { Sequence = x.Sequence }).ToList();

        var txid = ZcashTransactions.TxIdDigestV5(inputs, tx.Outputs, tx.LockTime, tx.Expiry, tx.BranchId, tx.SaplingDigest, tx.OrchardDigest);
        Assert.True(Convert.ToHexString(txid).Equals(row[1].GetString(), StringComparison.OrdinalIgnoreCase), $"vector {number}: txid");

        if (row[4].ValueKind == JsonValueKind.Null) return;   // no transparent input to sign
        var index = row[4].GetInt32();
        var sighash = ZcashTransactions.SigHashV5(inputs, tx.Outputs, index, tx.LockTime, tx.Expiry, tx.BranchId, tx.SaplingDigest, tx.OrchardDigest);
        Assert.True(Convert.ToHexString(sighash).Equals(row[5].GetString(), StringComparison.OrdinalIgnoreCase), $"vector {number}: sighash_all");
    }

    [Fact]
    public void A_v5_transaction_mainnet_accepted_is_rebuilt_and_every_signature_verifies()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture("zcash-v5-mainnet-tx.json")));
        var raw = Convert.FromHexString(doc.RootElement.GetProperty("raw").GetString()!);
        var txid = doc.RootElement.GetProperty("txid").GetString()!;
        var spent = doc.RootElement.GetProperty("spent").EnumerateArray()
            .Select(s => (Value: (ulong)s.GetProperty("value").GetInt64(), Script: Convert.FromHexString(s.GetProperty("script_pubkey").GetString()!)))
            .ToList();

        var tx = Parsed.From(raw);
        Assert.Equal(0x37A5165Bu, tx.BranchId);   // signed under NU6.3
        Assert.Null(tx.SaplingDigest);            // transparent only: exactly what the wallet builds
        Assert.Null(tx.OrchardDigest);

        var inputs = tx.Inputs.Select((x, k) => new ZcashInput(x.TxId, x.Index, spent[k].Value, spent[k].Script) { Sequence = x.Sequence }).ToList();

        // The wallet's serializer gives back the very bytes the network took, and the same id.
        Assert.Equal(raw, ZcashTransactions.SerializeV5(inputs, tx.Outputs, tx.Inputs.Select(x => x.ScriptSig).ToList(), tx.LockTime, tx.Expiry, tx.BranchId));
        Assert.Equal(txid, ZcashTransactions.TxIdV5(inputs, tx.Outputs, tx.LockTime, tx.Expiry, tx.BranchId));

        for (var i = 0; i < inputs.Count; i++)
        {
            var script = new Script(tx.Inputs[i].ScriptSig).ToOps().ToList();
            var signature = script[0].PushData;
            var pubKey = new PubKey(script[1].PushData);
            Assert.Equal(ZcashTransactions.SigHashAll, signature[^1]);
            Assert.Equal(pubKey.Hash.ToBytes(), inputs[i].ScriptPubKey[3..23]);   // P2PKH of this key

            var digest = ZcashTransactions.SigHashV5(inputs, tx.Outputs, i, tx.LockTime, tx.Expiry, tx.BranchId);
            Assert.True(pubKey.Verify(new uint256(digest), ECDSASignature.FromDER(signature[..^1])), $"input {i}");
        }
    }

    [Fact]
    public void A_changed_amount_of_any_spent_coin_changes_every_signature_hash()
    {
        // ZIP-244 commits each input's signature to the amounts of ALL coins spent, so a signer cannot
        // be shown a smaller input than the one it is really spending.
        var a = new ZcashInput(new byte[32], 0, 100_000, Script.Empty.ToBytes());
        var b = new ZcashInput(Enumerable.Repeat((byte)1, 32).ToArray(), 1, 200_000, Script.Empty.ToBytes());
        var outputs = new List<ZcashOutput> { new(250_000, Script.Empty.ToBytes()) };

        var before = ZcashTransactions.SigHashV5([a, b], outputs, 0, 0, 3_600_000, 0x77190AD9);
        var after = ZcashTransactions.SigHashV5([a, b with { Zatoshi = 1 }], outputs, 0, 0, 3_600_000, 0x77190AD9);
        Assert.NotEqual(before, after);
    }

    /// <summary>A v5 transaction read back into its parts, with the shielded digests ZIP-244 defines.</summary>
    private sealed record Parsed(
        uint Version, uint VersionGroupId, uint BranchId, uint LockTime, uint Expiry,
        List<(byte[] TxId, uint Index, byte[] ScriptSig, byte[] Sequence)> Inputs,
        List<ZcashOutput> Outputs, byte[]? SaplingDigest, byte[]? OrchardDigest)
    {
        public static Parsed From(byte[] tx)
        {
            var r = new Reader(tx);
            uint version = r.U32(), group = r.U32(), branch = r.U32(), lockTime = r.U32(), expiry = r.U32();

            var inputs = new List<(byte[], uint, byte[], byte[])>();
            for (var n = r.Compact(); n > 0; n--)
                inputs.Add((r.Take(32), r.U32(), r.Take((int)r.Compact()), r.Take(4)));

            var outputs = new List<ZcashOutput>();
            for (var n = r.Compact(); n > 0; n--)
                outputs.Add(new ZcashOutput(BinaryPrimitives.ReadUInt64LittleEndian(r.Take(8)), r.Take((int)r.Compact())));

            // Sapling (ZIP-225): spends 96 bytes, outputs 756 bytes, then the rest of the bundle.
            var spends = Enumerable.Range(0, (int)r.Compact()).Select(_ => r.Take(96)).ToList();
            var saplingOutputs = Enumerable.Range(0, (int)r.Compact()).Select(_ => r.Take(756)).ToList();
            byte[]? sapling = null;
            if (spends.Count + saplingOutputs.Count > 0)
            {
                var valueBalance = r.Take(8);
                var anchor = spends.Count > 0 ? r.Take(32) : [];
                r.Take(192 * spends.Count + 64 * spends.Count + 192 * saplingOutputs.Count + 64);
                var spendsDigest = spends.Count == 0
                    ? Blake("ZTxIdSSpendsHash")
                    : Blake("ZTxIdSSpendsHash",
                        Blake("ZTxIdSSpendCHash", [.. spends.SelectMany(s => s[32..64])]),
                        Blake("ZTxIdSSpendNHash", [.. spends.SelectMany(s => s[..32].Concat(anchor).Concat(s[64..96]))]));
                var outputsDigest = saplingOutputs.Count == 0
                    ? Blake("ZTxIdSOutputHash")
                    : Blake("ZTxIdSOutputHash",
                        Blake("ZTxIdSOutC__Hash", [.. saplingOutputs.SelectMany(o => o[32..148])]),
                        Blake("ZTxIdSOutM__Hash", [.. saplingOutputs.SelectMany(o => o[148..660])]),
                        Blake("ZTxIdSOutN__Hash", [.. saplingOutputs.SelectMany(o => o[..32].Concat(o[660..756]))]));
                sapling = Blake("ZTxIdSaplingHash", spendsDigest, outputsDigest, valueBalance);
            }

            // Orchard: actions of 820 bytes, then flags, value balance, anchor, proofs and signatures.
            var actions = Enumerable.Range(0, (int)r.Compact()).Select(_ => r.Take(820)).ToList();
            byte[]? orchard = null;
            if (actions.Count > 0)
            {
                var flags = r.Take(1);
                var valueBalance = r.Take(8);
                var anchor = r.Take(32);
                r.Take((int)r.Compact());
                r.Take(64 * actions.Count + 64);
                orchard = Blake("ZTxIdOrchardHash",
                    Blake("ZTxIdOrcActCHash", [.. actions.SelectMany(a => a[32..64].Concat(a[96..212]))]),
                    Blake("ZTxIdOrcActMHash", [.. actions.SelectMany(a => a[212..724])]),
                    Blake("ZTxIdOrcActNHash", [.. actions.SelectMany(a => a[..32].Concat(a[64..96]).Concat(a[724..820]))]),
                    flags, valueBalance, anchor);
            }

            Assert.Equal(tx.Length, r.Position);   // nothing left over: the parse is the whole transaction
            return new Parsed(version, group, branch, lockTime, expiry, inputs, outputs, sapling, orchard);
        }

        private static byte[] Blake(string personalisation, params byte[][] parts)
        {
            var person = new byte[16];
            Encoding.ASCII.GetBytes(personalisation).CopyTo(person, 0);
            var digest = new Blake2bDigest(null, 32, null, person);
            foreach (var part in parts) digest.BlockUpdate(part, 0, part.Length);
            var hash = new byte[32];
            digest.DoFinal(hash, 0);
            return hash;
        }
    }

    private sealed class Reader(byte[] bytes)
    {
        public int Position { get; private set; }

        public byte[] Take(int n)
        {
            var slice = bytes[Position..(Position + n)];
            Position += n;
            return slice;
        }

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public ulong Compact()
        {
            var first = Take(1)[0];
            return first switch
            {
                < 0xFD => first,
                0xFD => BinaryPrimitives.ReadUInt16LittleEndian(Take(2)),
                0xFE => BinaryPrimitives.ReadUInt32LittleEndian(Take(4)),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(Take(8)),
            };
        }
    }
}
