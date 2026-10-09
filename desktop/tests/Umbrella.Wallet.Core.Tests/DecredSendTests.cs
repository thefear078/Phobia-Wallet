using System.Text.Json;
using NBitcoin;
using NBitcoin.Crypto;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Decred sending (roadmap N.12), around the signature hash that <see cref="DecredTransactionTests"/>
/// pins to transactions the network accepted: the key belongs to the address shown, the coins and the
/// fee are chosen before anything is signed, every input repeats its coin's amount, height and
/// position, and the bytes the wallet would publish carry signatures that verify.
/// </summary>
public sealed class DecredSendTests
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly byte[] Script = DecredTransactions.PayToPubKeyHash(new byte[20]);

    private static DecredUtxo Coin(long atoms, uint height = 1_000_000, string? txId = null, uint index = 0) =>
        new(txId ?? new string('a', 63) + (atoms % 10), index, atoms, height, BlockIndex: 3, Tree: 0);

    [Fact]
    public void The_signing_key_belongs_to_the_address_shown()
    {
        var deriver = new HdAddressDeriver();
        var shown = deriver.DeriveReceiveAddress(Mnemonic, ChainId.Dcr);
        using var key = deriver.DeriveDecredKey(Mnemonic);

        Assert.Equal(shown.Address, DecredAddress.FromPublicKey(key.PubKey.ToBytes()));
        Assert.Equal("m/44'/42'/0'/0/0", shown.DerivationPath);
    }

    [Fact]
    public void Coins_are_chosen_largest_first_with_the_fee_and_change_worked_out_together()
    {
        var coins = new[] { Coin(50_000), Coin(2_000_000), Coin(300_000) };

        var (plan, error) = DecredSendRules.Plan(coins, 1_000_000, Script, Script);

        Assert.Null(error);
        Assert.NotNull(plan);
        Assert.Single(plan.Inputs);
        Assert.Equal(2_000_000, plan.Inputs[0].Atoms);
        Assert.Equal(DecredTransactions.FeeFor(DecredTransactions.EstimateSize(1, [25, 25])), plan.FeeAtoms);
        Assert.Equal(plan.TotalInputAtoms, plan.AmountAtoms + plan.FeeAtoms + plan.ChangeAtoms);
        Assert.False(plan.ChangeSweptToFee);
    }

    [Fact]
    public void Change_too_small_to_be_an_output_goes_to_the_fee_rather_than_becoming_dust()
    {
        var oneOutputFee = DecredTransactions.FeeFor(DecredTransactions.EstimateSize(1, [25]));
        var coins = new[] { Coin(1_000_000 + oneOutputFee + 100) };

        var (plan, _) = DecredSendRules.Plan(coins, 1_000_000, Script, Script);

        Assert.NotNull(plan);
        Assert.True(plan.ChangeSweptToFee);
        Assert.Equal(0, plan.ChangeAtoms);
        Assert.Equal(oneOutputFee + 100, plan.FeeAtoms);
    }

    [Fact]
    public void Refusals_are_made_before_anything_is_signed()
    {
        Assert.Contains("dust limit", DecredSendRules.Plan([Coin(1_000_000)], 6_000, Script, Script).Error);
        Assert.Contains("unconfirmed", DecredSendRules.Plan([Coin(1_000_000, height: 0)], 10_000, Script, Script).Error);
        Assert.Contains("no confirmed Decred", DecredSendRules.Plan([], 10_000, Script, Script).Error);
        Assert.Contains("network fee", DecredSendRules.Plan([Coin(1_000_000)], 1_000_000, Script, Script).Error);
        Assert.Contains("Not enough DCR", DecredSendRules.Plan([Coin(10_000)], 1_000_000, Script, Script).Error);
    }

    [Fact]
    public void Amounts_never_round_away_an_atom()
    {
        Assert.True(DecredSendRules.TryToAtoms(0.00000001m, out var one));
        Assert.Equal(1, one);
        Assert.False(DecredSendRules.TryToAtoms(0.000000001m, out _));
        Assert.False(DecredSendRules.TryToAtoms(0m, out _));
        Assert.Equal(0.002578m, DecredSendRules.ToDcr(257_800));
    }

    [Fact]
    public void The_bytes_a_send_publishes_are_signed_by_the_wallet_and_repeat_each_coins_fraud_proof()
    {
        var deriver = new HdAddressDeriver();
        using var key = deriver.DeriveDecredKey(Mnemonic);
        var from = DecredAddress.FromPublicKey(key.PubKey.ToBytes());
        var fromScript = DecredTransactions.ScriptFor(from)!;
        var toScript = DecredTransactions.ScriptFor("DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi")!;

        var inputs = new[]
        {
            new DecredUtxo("188388b7100980876adcffc3caafcc80b87addd52f849ee878fe3a116ac52b58", 1, 257_800, 1_121_916, 2, 0),
            new DecredUtxo("5217f4d8eb6fabc1afe72c6cf9f554a954745e8ea866d3dba38e9a8ba6befb6e", 0, 260_420, 1_121_054, 10, 0),
        };
        var quote = new DcrSendQuote(from, "DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi", 0.004m, 400_000, 6_000, 0.00006m,
            ChangeAtoms: 257_800 + 260_420 - 400_000 - 6_000, inputs, fromScript, toScript, ChangeSweptToFee: false);

        var (raw, txId) = DecredTransactionSender.Build(quote, key);
        var tx = DecredTransactions.Parse(raw);

        Assert.Equal(DecredTransactions.TxId(tx), txId);
        Assert.Equal(raw, DecredTransactions.Serialize(tx));
        Assert.Equal(2, tx.Outputs.Count);
        Assert.Equal(400_000, tx.Outputs[0].Value);
        Assert.Equal(toScript, tx.Outputs[0].PkScript);
        Assert.Equal(quote.ChangeAtoms, tx.Outputs[1].Value);
        Assert.Equal(fromScript, tx.Outputs[1].PkScript);
        Assert.Equal(tx.Inputs.Sum(i => i.ValueIn), tx.Outputs.Sum(o => o.Value) + quote.FeeAtoms);
        Assert.True(raw.Length <= DecredTransactions.EstimateSize(2, [25, 25]));

        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            var input = tx.Inputs[i];
            Assert.Equal(inputs[i].Atoms, input.ValueIn);
            Assert.Equal(inputs[i].Height, input.BlockHeight);
            Assert.Equal(inputs[i].BlockIndex, input.BlockIndex);
            Assert.Equal(DecredTransactions.FinalSequence, input.Sequence);

            var (signature, publicKey) = DecredTransactions.ReadSignatureScript(input.SignatureScript)!.Value;
            Assert.Equal(key.PubKey.ToBytes(), publicKey);
            var hash = new uint256(DecredTransactions.SignatureHash(tx, i, fromScript));
            Assert.True(key.PubKey.Verify(hash, ECDSASignature.FromDER(signature[..^1])));
        }
    }

    [Fact]
    public void A_send_without_change_has_one_output()
    {
        using var key = new HdAddressDeriver().DeriveDecredKey(Mnemonic);
        var from = DecredAddress.FromPublicKey(key.PubKey.ToBytes());
        var fromScript = DecredTransactions.ScriptFor(from)!;
        var quote = new DcrSendQuote(from, "DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi", 0.0025m, 250_000, 7_800, 0.000078m,
            ChangeAtoms: 0, [Coin(257_800)], fromScript, DecredTransactions.ScriptFor("DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi")!,
            ChangeSweptToFee: true);

        var tx = DecredTransactions.Parse(DecredTransactionSender.Build(quote, key).Raw);

        Assert.Single(tx.Outputs);
    }

    [Fact]
    public void Only_confirmed_coins_paying_this_address_are_read_from_the_explorer()
    {
        // The shape dcrdata's /insight/api/addr/{address}/utxo answered with on 2026-10-08.
        var ours = "76a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac";
        var json = $$"""
            [
              {"address":"DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi","txid":"188388b7100980876adcffc3caafcc80b87addd52f849ee878fe3a116ac52b58","vout":1,"scriptPubKey":"{{ours}}","height":1121916,"amount":0.002578,"satoshis":257800,"confirmations":7},
              {"address":"DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi","txid":"{{new string('b', 64)}}","vout":0,"scriptPubKey":"{{ours}}","height":0,"amount":0.01,"satoshis":1000000,"confirmations":0},
              {"address":"DsRTa49EFdRBukVF6oMRLcFaNKYo49SCnAi","txid":"{{new string('c', 64)}}","vout":2,"scriptPubKey":"bb76a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac","height":1121900,"amount":5,"satoshis":500000000,"confirmations":20}
            ]
            """;
        using var doc = JsonDocument.Parse(json);

        var coins = DecredTransactionSender.ParseCoins(doc.RootElement, Convert.FromHexString(ours));

        Assert.NotNull(coins);
        Assert.Equal(2, coins.Count);                                   // the stake-tagged output is not ours to spend this way
        Assert.Equal(257_800, coins[0].Atoms);
        Assert.True(coins[0].Confirmed);
        Assert.False(coins[1].Confirmed);
        Assert.Null(DecredTransactionSender.ParseCoins(JsonDocument.Parse("""{"error":"x"}""").RootElement, Script));
    }

    [Fact]
    public void A_coins_height_and_position_come_from_its_own_transaction_and_must_agree_with_it()
    {
        var ours = Convert.FromHexString("76a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac");
        var coin = new DecredUtxo("188388b7100980876adcffc3caafcc80b87addd52f849ee878fe3a116ac52b58", 1, 257_800, 1_121_916, 0, 0);
        // Trimmed from dcrdata's /api/tx/{id} for that transaction.
        const string tx = """
            {"txid":"188388b7100980876adcffc3caafcc80b87addd52f849ee878fe3a116ac52b58","tree":0,"type":"regular",
             "vout":[{"value":0,"n":0,"scriptPubKey":{"hex":"6a2081dc"}},
                     {"value":0.002578,"n":1,"scriptPubKey":{"hex":"76a914056bd6aa7cc7288ebbc69560aab571ea1f80434888ac"}}],
             "block":{"blockheight":1121916,"blockindex":2}}
            """;

        var (proven, error) = DecredTransactionSender.ReadOrigin(JsonDocument.Parse(tx).RootElement, coin, ours);
        Assert.Null(error);
        Assert.Equal(1_121_916u, proven!.Height);
        Assert.Equal(2u, proven.BlockIndex);

        // An explorer whose two answers disagree about the coin is not signed for.
        var (_, lie) = DecredTransactionSender.ReadOrigin(JsonDocument.Parse(tx).RootElement, coin with { Atoms = 999_999 }, ours);
        Assert.Contains("contradicts", lie);
        var (_, stake) = DecredTransactionSender.ReadOrigin(JsonDocument.Parse(tx.Replace("\"tree\":0", "\"tree\":1")).RootElement, coin, ours);
        Assert.Contains("staking", stake);
        var (_, mempool) = DecredTransactionSender.ReadOrigin(
            JsonDocument.Parse("""{"tree":0,"vout":[],"block":{"blockheight":0,"blockindex":0}}""").RootElement, coin, ours);
        Assert.Contains("not in a block", mempool);
    }

    [Theory]
    [InlineData(true, "{\"txid\":\"ab\"}", UtxoBroadcastAnswer.Accepted)]
    [InlineData(false, "rejected transaction ab: already have transaction ab", UtxoBroadcastAnswer.Accepted)]
    [InlineData(false, "failed to deserialize tx: unexpected EOF", UtxoBroadcastAnswer.Rejected)]
    [InlineData(false, "bad fraud check value in (expected 5, given 4) for txIn 0", UtxoBroadcastAnswer.Rejected)]
    [InlineData(false, "transaction ab has 10 fees which is under the required amount of 2500", UtxoBroadcastAnswer.Rejected)]
    [InlineData(false, "output cd:0 already spent by transaction ef in the memory pool", UtxoBroadcastAnswer.Unclear)]
    // What mainnet answered on 2026-10-08 to a payment signed with the wrong key (DecredSendLiveTests).
    [InlineData(false, "SendRawTransaction failed: \"-1: rejected transaction 306a7fcf: failed to validate input 306a7fcf:0 which references output c4bc2eb0:0", UtxoBroadcastAnswer.Rejected)]
    [InlineData(false, "", UtxoBroadcastAnswer.Unclear)]
    public void What_dcrd_answers_through_dcrdata_is_read_for_what_it_means(bool ok, string body, UtxoBroadcastAnswer expected) =>
        Assert.Equal(expected, UtxoBroadcast.Classify(ok, body));
}
