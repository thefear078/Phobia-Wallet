using NBitcoin;
using NBitcoin.Crypto;
using Umbrella.Wallet.Core.Chains;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Decred's transaction format and signature hash, checked against transactions the network accepted.
///
/// Each fixture is a real mainnet transaction (taken from dcrdata on 2026-10-08) with the coins it spent.
/// If the signature hash computed here were wrong in a single byte, the signatures the network verified
/// would not verify here — so passing means this code hashes exactly what dcrd hashes. The second
/// transaction has nine inputs, which exercises the witness hash's "empty script for every other input"
/// rule that a one-input transaction cannot.
/// </summary>
public sealed class DecredTransactionTests
{
    public sealed record Spent(string TxId, uint Vout, byte Tree, string PkScript, long Atoms, uint Height, uint BlockIndex);

    public sealed record Fixture(string TxId, string Raw, Spent[] Inputs);

    public static readonly Fixture[] Fixtures =
    [
        new(
            "188388b7100980876adcffc3caafcc80b87addd52f849ee878fe3a116ac52b58",
            "01000000016efbbea68b9a8ea3dbd366a88e5e7454a954f5f96c2ce7afc1ab6febd8f417520000000000ffffffff02000000" +
            "00000000000000226a2081dc2aeaea2f6633a29e9bd2f8fc7b4eaa80cf4ac00804f523657ae2f313332508ef030000000000" +
            "00001976a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac00000000000000000144f90300000000001e1b11000a" +
            "0000006a47304402200cacb1f40816a339a60a33ccbad13f2a105c124580a5f7d12c9f98c681c02ca7022071228ab3863d26" +
            "b65441f986e8b9e73180d5078bd1fbc0e23721b8f5ec2f6cbb0121024503ca517c6253ba7e742315a37078326cbcbaf4e61f" +
            "5c1b11028a26d694fa08",
            [
                new("5217f4d8eb6fabc1afe72c6cf9f554a954745e8ea866d3dba38e9a8ba6befb6e", 0, 0, "76a9148bfc00b1db91f28b5de3e404e83a4d45c625ff6c88ac", 260420, 1121054, 10),
            ]),
        new(
            "7bbfab9507a700f68444fb5c763e48c41a8156fc274c7d902d1d31269e2d153f",
            "010000000940051eca6d24e17d9c47aaef17a1879975dda6001b32ff9f53c99a2bde36e4260b00000000ffffffff49485a92" +
            "cc6b20ffd7839b8f54d8ba15268d8906144686fb4cb47db8b3c682900000000000ffffffffbd0d8cd2c3421992ab3b737466" +
            "8b17e528834b30fea17098ae56bdffb84e39951600000000ffffffffbd0d8cd2c3421992ab3b7374668b17e528834b30fea1" +
            "7098ae56bdffb84e39951700000000ffffffffbd0d8cd2c3421992ab3b7374668b17e528834b30fea17098ae56bdffb84e39" +
            "951800000000ffffffffbd0d8cd2c3421992ab3b7374668b17e528834b30fea17098ae56bdffb84e39953300000000ffffff" +
            "ffa3fea6d39b1bb3f5eebdfdae0f9589eba68d21edc6abf3df592a964800b761a30600000000ffffffffba77e679bb190051" +
            "e29c21a1fb88c1e0880a29000a8a1f0cd9de76ce7f42b3c30100000000ffffffff1c7d7d25e712fbc5d08abb4576e3438461" +
            "029da7f9f8ed65965caae0845ef2cf0100000000ffffffff12e96f42760000000000001976a9141db3d24c4d7cbec6c9a0f5" +
            "96811656a1985a626888aca2a993b90100000000001976a914c9c94478b4b035960907780044cb2e445bf676d688ac61eaf9" +
            "be0100000000001976a9141915bd0ad0b34d840643ae9b176c2c7d5e6cdfb588ac871555330200000000001976a914f979d6" +
            "1104eb23515644f448ed4d0b28053e1f2d88ac4bc451560200000000001976a9147bbfd3ec7d20eb5f257a05789e38bf0073" +
            "149f8888acd28cde650200000000001976a9145a4cb679eee3f31b1327251cb38920ce0d4f91d888ac9e70cc6d0200000000" +
            "001976a914004c7257c8f4c38307fe137394554ca05f93047388ac00e6b7780200000000001976a9144cf6deac2d03def39b" +
            "c30cdcfd044cd4fe0754e388ac9b2aab9a0200000000001976a9145c70ffa1d2183a2998bece13ab09dd889490714188ac00" +
            "0000000400000000001976a91403b9f3b73dbec699eb2fd4075fdb789f127512c388ac000000000400000000001976a9141f" +
            "af11300a4698f0c044eb890b96b214490f7f8988ac000000000400000000001976a9147360858779d653b403e031f3ccabe4" +
            "2420394fd888ac000000000400000000001976a9147b79780928e5c1300418dab8cd3111b96b84e23e88ac00000000040000" +
            "0000001976a9148ac5603530102142dc3e15c11229bbb9565d88d488ac000000000400000000001976a914ce3bb8142c4335" +
            "3a9f61c34fccc407aa633fe5e088ac000000000400000000001976a914d63c719f85c7de899108842affd87fd47d11986488" +
            "ac000000000400000000001976a914d93eb22088f59ebd3a72b86987e85edea285bec588ac000000000400000000001976a9" +
            "14fd67352540bc1158afa36f00f6a0d1cea48e3f1e88ac000000000000000009cb79427604000000151d11000b0000006a47" +
            "30440220494843ea5c14a7ea29f5e975f8646dd688a40ae5ca455b6d3e462284e2530ca302202a5f520aaee024d619ddf7bf" +
            "c18cd0e9b90737aabd7003833b96abbcfe15b53a0121036f6638f0f1aa3a74c8fde1f9757a77223372e575625c6f8338ebb6" +
            "39f58ce08fb496de6506000000731e11000c0000006b483045022100da85a8d80d4acb99eb27a90e00a1b78ce86d6c39339a" +
            "3ade4d24d8170029c2b2022026323e77bb74cd19654d10deef7dc1635c648758c9f0744599226bed8c8474b6012102c13416" +
            "241f68262c308ab14818e9bc4c9561cca44761b1e5132386f7e061e30284b393b905000000731e11000a0000006a47304402" +
            "20032e00d5bd63a5502c33e4f21cfdb60d32da172150b473d4453e2d5af5ad0cfb022002ae07741858fee6c283dcd4369f6e" +
            "66e8a8524e80bf1e6afa29dc1de9fa1041012102b6c0893eff07089aea783502167e34254bff090e86d8854c33f3f2243dbe" +
            "e3922dce515606000000731e11000a0000006b4830450221008c08ebabc9c1b4e9603bee688c21e0e1a40412314904496588" +
            "a056856f523d7102203399d32344fbd8760cc94e22a27c2425b412380fa6f329c40e7865199656620601210383e6d715d316" +
            "28df977e7332103981aa85f833507b1c5ef3a66940c0f876e459e2efb77806000000731e11000a0000006b48304502210088" +
            "52841abf0c80933acdfe97a173d7476ccd250dbf4e2e0cbb84e2471afebab302205d61f92155f7f09df0837ad4e9ed1249b8" +
            "7f0a08387a5af212f9fd1a0c9741a0012102d58988f7c0b0f0c91195967eda138e38bd6b20845506c68d2137bdf53c1d3df3" +
            "7d34ab9a06000000731e11000a0000006a47304402207c88669d57c8b0f119e03df8b9e8a1054621e5ad46b3783d0a8fb89a" +
            "74de416002200397a5425aaccb7b10bb43439a34a966c2b82f6d1ba2661012b3ea2b73dfc95a012103745df07bf9cc64b10d" +
            "e075c11394650d70b9d7baec581ed44f20e5683cde7dca807acc6d06000000691e11000a0000006b48304502210092a9debd" +
            "178a9c360717d68975d50da5e27d61236292087d66a083d260a8043f0220209b8115a39753fee2f32eefc47d55aedf62231d" +
            "98fe95e5c461abfdab33dfe9012103c6dda77e75b8fadc3e06390a4fadf72a35a55d442a987e3739521828b4eac617691f55" +
            "3306000000121d1100090000006b483045022100f90d349399788b62bb92ecf9cc1fe8ef72d01ba005dab852cd4b0f069ca1" +
            "2fd2022005a0bc0c5da25f5d2e81b774c33b82427aaf0bb600b3faf0f78190ab3251ffa901210241cfcf1962e9c9f6a800f1" +
            "21191af34857272b3ea6f02954a1b1a610db2803a643f4f9be050000001c1d1100030000006b483045022100dab4baeb9030" +
            "b9ff70f0b99f7a223ad6ca381e5dcb950c4958601978970fd4c7022021b94a7d665cd22cb4b68a784f57f5b85fec5d891a1f" +
            "6d576c26e83bf2e70a1f012103e16768381b2e03ee068fe9686a31e5402c8fdbcb405b8b3aeb2248b08e6a8b6d",
            [
                new("26e436de2b9ac9539fff321b00a6dd759987a117efaa479c7de1246dca1e0540", 11, 0, "76a914128523e3682390ea48267a85268f2f1dbdbf71ef88ac", 19163937227, 1121557, 11),
                new("9082c6b3b87db44cfb86461406898d2615bad8548f9b83d7ff206bcc925a4849", 0, 0, "76a91437019f0d08aafbd488860fcb5b3bec0bf51b7bec88ac", 27478890164, 1121907, 12),
                new("95394eb8ffbd56ae9870a1fe304b8328e5178b6674733bab921942c3d28c0dbd", 22, 0, "76a9149b540edb56f3d9284bf046b4cf4464ede6739e3788ac", 24588301188, 1121907, 10),
                new("95394eb8ffbd56ae9870a1fe304b8328e5178b6674733bab921942c3d28c0dbd", 23, 0, "76a914efdab10fba99a665d72fbbe01b399e8533eebce088ac", 27218005549, 1121907, 10),
                new("95394eb8ffbd56ae9870a1fe304b8328e5178b6674733bab921942c3d28c0dbd", 24, 0, "76a914a14c17c403a787111b73ee1c0207f960cbc7437e88ac", 27795124194, 1121907, 10),
                new("95394eb8ffbd56ae9870a1fe304b8328e5178b6674733bab921942c3d28c0dbd", 51, 0, "76a914a3f4463ddaaee15769b55326b64f41efe58e55f088ac", 28364715133, 1121907, 10),
                new("a361b70048962a59dff3abc6ed218da6eb89950faefdbdeef5b31b9bd3a6fea3", 6, 0, "76a9145a2b29ceff46e6da9e578c01557d7e05b62a81c388ac", 27611921024, 1121897, 10),
                new("c3b3427fce76ded90c1f8a0a00290a88e0c188fba1219ce2510019bb79e677ba", 1, 0, "76a91470638e56280023809a1d54f525a2c849735edfcb88ac", 26631020393, 1121554, 9),
                new("cff25e84e0aa5c9665edf8f9a79d02618443e37645bb8ad0c5fb12e7257d7d1c", 1, 0, "76a9140f6dd90100ec5c92b4bce1d45f47fd34f90c9e4e88ac", 24678888515, 1121564, 3),
            ]),
    ];

    public static TheoryData<int> FixtureIndexes() => new() { 0, 1 };

    [Theory]
    [MemberData(nameof(FixtureIndexes))]
    public void A_real_transaction_reads_and_writes_back_byte_for_byte(int which)
    {
        var fixture = Fixtures[which];
        var raw = Convert.FromHexString(fixture.Raw);

        var tx = DecredTransactions.Parse(raw);

        Assert.Equal(fixture.Inputs.Length, tx.Inputs.Count);
        Assert.Equal(raw, DecredTransactions.Serialize(tx));
        Assert.Equal(fixture.TxId, DecredTransactions.TxId(tx));
    }

    [Theory]
    [MemberData(nameof(FixtureIndexes))]
    public void Every_signature_the_network_accepted_verifies_against_this_signature_hash(int which)
    {
        var fixture = Fixtures[which];
        var tx = DecredTransactions.Parse(Convert.FromHexString(fixture.Raw));

        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            var spent = fixture.Inputs[i];
            var pkScript = Convert.FromHexString(spent.PkScript);
            var pushed = DecredTransactions.ReadSignatureScript(tx.Inputs[i].SignatureScript);
            Assert.NotNull(pushed);
            var (signature, publicKey) = pushed.Value;

            // The key behind the signature is the one the spent output paid.
            Assert.Equal(pkScript, DecredTransactions.PayToPubKeyHash(DecredAddress.Hash160(publicKey)));
            Assert.Equal((byte)DecredTransactions.SigHashAll, signature[^1]);

            var hash = DecredTransactions.SignatureHash(tx, i, pkScript);
            var verified = new PubKey(publicKey).Verify(new uint256(hash), ECDSASignature.FromDER(signature[..^1]));
            Assert.True(verified, $"input {i} of {fixture.TxId}");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureIndexes))]
    public void The_fraud_proof_is_the_spent_coins_amount_height_and_position(int which)
    {
        // dcrd's mempool refuses an input whose witness disagrees with its record of the coin, so the
        // wallet fills these from the explorer's block height and block index — the same figures the
        // wallets that made these transactions wrote.
        var fixture = Fixtures[which];
        var tx = DecredTransactions.Parse(Convert.FromHexString(fixture.Raw));

        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            var input = tx.Inputs[i];
            var spent = fixture.Inputs[i];
            Assert.Equal(spent.Atoms, input.ValueIn);
            Assert.Equal(spent.Height, input.BlockHeight);
            Assert.Equal(spent.BlockIndex, input.BlockIndex);
            Assert.Equal(spent.Tree, input.Tree);
            Assert.Equal(spent.Vout, input.PrevIndex);
            Assert.Equal(spent.TxId, Convert.ToHexString(input.PrevHash.Reverse().ToArray()).ToLowerInvariant());
        }
    }

    [Fact]
    public void A_signature_stops_verifying_when_an_output_changes()
    {
        var fixture = Fixtures[0];
        var tx = DecredTransactions.Parse(Convert.FromHexString(fixture.Raw));
        var pkScript = Convert.FromHexString(fixture.Inputs[0].PkScript);
        var (signature, publicKey) = DecredTransactions.ReadSignatureScript(tx.Inputs[0].SignatureScript)!.Value;

        var outputs = tx.Outputs.ToList();
        outputs[^1] = outputs[^1] with { Value = outputs[^1].Value - 1 };
        var tampered = tx with { Outputs = outputs };

        var hash = DecredTransactions.SignatureHash(tampered, 0, pkScript);
        Assert.False(new PubKey(publicKey).Verify(new uint256(hash), ECDSASignature.FromDER(signature[..^1])));
    }

    [Fact]
    public void Dust_and_sizes_follow_dcrd()
    {
        // mempool.isDust for a pay-to-pubkey-hash output at the 0.0001 DCR/kB relay fee.
        Assert.Equal(6030, DecredTransactions.DustThreshold(25));
        Assert.Equal(36, DecredTransactions.OutputSize(25));
        Assert.Equal(166, DecredTransactions.P2pkhInputSize);
        // One input paying an address and change back: 12 + 2 + 1 + 166 + 72.
        Assert.Equal(253, DecredTransactions.EstimateSize(1, [25, 25]));
        Assert.Equal(253 * DecredTransactions.FeePerKb / 1000, DecredTransactions.FeeFor(253));

        // The real one-input transaction above is no larger than the estimate for its shape.
        var tx = DecredTransactions.Parse(Convert.FromHexString(Fixtures[0].Raw));
        Assert.True(Convert.FromHexString(Fixtures[0].Raw).Length <=
                    DecredTransactions.EstimateSize(tx.Inputs.Count, tx.Outputs.Select(o => o.PkScript.Length)));
    }

    [Fact]
    public void Addresses_become_the_scripts_that_pay_them()
    {
        // The output of the first fixture paid this address with this script.
        Assert.Equal("76a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac",
            Convert.ToHexString(DecredTransactions.ScriptFor("DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi")!).ToLowerInvariant());
        Assert.Null(DecredTransactions.ScriptFor("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"));   // Bitcoin, not Decred
        Assert.Null(DecredTransactions.ScriptFor(""));
    }
}
