using System.Numerics;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Nano state blocks. The key, the signature and the work are pinned to the signed block in the Nano
/// documentation (docs.nano.org → Integration guides → Key management); the hash to a real mainnet block
/// read back from a node. A block that differs in one byte from what a node computes is refused, so
/// nothing here is checked against this code's own output alone.
/// </summary>
public sealed class NanoBlockTests
{
    private const string PrivateKey = "781186FB9EF17DB6E3D1056550D9FAE5D5BBADA6A6BC370E4CBB938B1DC71DA3";
    private const string PublicKey = "3068BB1CA04525BB0E416C485FE6A67FD52540227D267CC8B6E8DA958A7FA039";
    private const string Account = "nano_1e5aqegc1jb7qe964u4adzmcezyo6o146zb8hm6dft8tkp79za3sxwjym5rx";
    private const string Previous = "92BA74A7D6DC7557F3EDA95ADC6341D51AC777A0A6FF0688A5C492AB2B2CB40D";
    private const string Link = "5C2FBB148E006A8E8BA7A75DD86C9FE00C83F5FFDBFD76EAA09531071436B6AF";
    private const string LinkAsAccount = "nano_1q3hqecaw15cjt7thbtxu3pbzr1eihtzzpzxguoc37bj1wc5ffoh7w74gi6p";
    private const string BlockHash = "BB569136FA05F8CBF65CEF2EDE368475B289C4477342976556BA4C0DDF216E45";
    private const string Signature =
        "74BCC59DBA39A1E34A5F75F96D6DE9154E3477AAD7DE30EA563DFCFE501A804228008F98DDF4A15FD35705102785C50EF76732C3A74B0FEC5B0DD67B574A5900";
    private const string Work = "fbffed7c73b61367";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void The_documented_key_is_the_documented_account()
    {
        var publicKey = NanoAccounts.PublicKey(Hex(PrivateKey));
        Assert.Equal(PublicKey, Convert.ToHexString(publicKey));
        Assert.Equal(Account, NanoAccounts.Address(publicKey));
    }

    /// <summary>
    /// The hash, against a real mainnet block (a receive on the Natrium donation account, height 3223,
    /// read with <c>block_info</c>): the node calls it 6D7FC58C…15E1. The documentation's key-management
    /// example cannot serve here — its hash, signature and work agree with one another, but its fields
    /// hash to something else (checked independently with Python's hashlib), so they were edited after
    /// the block was made.
    /// </summary>
    [Fact]
    public void The_block_hash_matches_a_real_mainnet_block()
    {
        var hash = NanoBlocks.Hash(
            NanoAccounts.TryDecode("nano_1natrium1o3z5519ifou7xii8crpxpk8y65qmkih8e8bpsjri651oza8imdd")!,
            Hex("CC3E0F8CEE2325D61F717A9897A2B9461C9B8F16FE5FE5568B2A513057E7F892"),
            NanoAccounts.TryDecode("nano_1x7biz69cem95oo7gxkrw6kzhfywq4x5dupw4z1bdzkb74dk9kpxwzjbdhhs")!,
            BigInteger.Parse("310784522967173701685653362791790"),
            Hex("7A47335F0EDB165766036B4CFCE09F53F28CC8AC279E218719886E485383E39D"));

        Assert.Equal("6D7FC58CAC10E970E71FE266D6648B4DF563040037B4EB6BA45C28596CF515E1", Convert.ToHexString(hash));

        // Its work, a receive's, against its root (the previous block).
        Assert.True(NanoBlocks.IsWorkValid(NanoBlocks.ParseWork("73a8c38fd16b4f5d"),
            Hex("CC3E0F8CEE2325D61F717A9897A2B9461C9B8F16FE5FE5568B2A513057E7F892"), NanoBlocks.ReceiveThreshold));
    }

    [Fact]
    public void A_sends_link_is_the_recipients_public_key()
    {
        Assert.Equal(Link, Convert.ToHexString(NanoAccounts.TryDecode(LinkAsAccount)!));
    }

    [Fact]
    public void The_signature_matches_the_documentation_byte_for_byte()
    {
        // ed25519 signatures are deterministic: the same key and hash give exactly these 64 bytes.
        var signature = NanoBlocks.Sign(Hex(PrivateKey), Hex(BlockHash));
        Assert.Equal(Signature, Convert.ToHexString(signature));
    }

    [Fact]
    public void The_documented_work_is_valid_for_its_root_and_a_changed_root_is_not()
    {
        var work = NanoBlocks.ParseWork(Work);
        Assert.True(NanoBlocks.IsWorkValid(work, Hex(Previous), NanoBlocks.ReceiveThreshold));

        var other = Hex(Previous);
        other[0] ^= 1;
        Assert.False(NanoBlocks.IsWorkValid(work, other, NanoBlocks.ReceiveThreshold));
        Assert.Equal(Work, NanoBlocks.WorkHex(work));
    }

    [Fact]
    public void Generated_work_reaches_the_threshold_it_was_asked_for()
    {
        // A low threshold (one hash in 256 on average), so the test is instant; the search is the same.
        const ulong easy = 0xff00000000000000;
        var root = Hex(Previous);
        var work = NanoBlocks.GenerateWork(root, easy);
        Assert.True(NanoBlocks.IsWorkValid(work, root, easy));
    }

    [Fact]
    public void The_unrolled_work_hash_agrees_with_blake2b_on_random_inputs()
    {
        var rng = new Random(20261001);
        for (var i = 0; i < 2000; i++)
        {
            var root = new byte[32];
            rng.NextBytes(root);
            var nonce = (ulong)rng.NextInt64() ^ ((ulong)rng.Next() << 40);
            var fast = NanoBlocks.FastWorkValue(nonce,
                BitConverter.ToUInt64(root, 0), BitConverter.ToUInt64(root, 8),
                BitConverter.ToUInt64(root, 16), BitConverter.ToUInt64(root, 24));
            Assert.Equal(NanoBlocks.WorkValue(nonce, root), fast);
        }
    }

    [Fact]
    public void The_unrolled_work_hash_reads_the_documented_work_as_the_node_does()
    {
        var root = Hex(Previous);
        var nonce = NanoBlocks.ParseWork(Work);
        Assert.Equal(NanoBlocks.WorkValue(nonce, root), NanoBlocks.FastWorkValue(nonce,
            BitConverter.ToUInt64(root, 0), BitConverter.ToUInt64(root, 8),
            BitConverter.ToUInt64(root, 16), BitConverter.ToUInt64(root, 24)));
    }

    [Fact]
    public void A_balance_beyond_128_bits_is_refused()
    {
        var key = new byte[32];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NanoBlocks.Hash(key, key, key, BigInteger.Pow(2, 128), key));
    }
}
