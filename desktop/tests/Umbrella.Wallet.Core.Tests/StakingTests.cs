using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Staking from the wallet: TRON's checker against transactions TronGrid really built, Solana's stake
/// instructions byte for byte, Cosmos staking messages against the send path's own encoding. The live
/// side was checked read-only: a whole Solana stake transaction passed a validator's simulation, Cosmos
/// delegate / claim / undelegate passed the Hub's simulation, and these TRON bytes came from TronGrid.
/// </summary>
public sealed class StakingTests
{
    // ---------------- TRON ----------------

    private const string Owner = "TU3kjFuhtEo42tsCBtfYUAZxoqQ4yuSLQ5";
    private const string Witness = "TJvaAeFb8Lykt9RQcVyyTFN2iDvGMuyD4M";

    /// <summary>TronGrid's freezebalancev2 for 1 TRX of energy from <see cref="Owner"/>, and its txID.</summary>
    private const string FreezeHex =
        "0a02d739220824845a7be2657feb40c8a5dae78f345a59083612550a34747970652e676f6f676c65617069732e636f6d2f70726f746f" +
        "636f6c2e467265657a6542616c616e63655632436f6e7472616374121d0a1541c64e69acde1c7b16c2a3efcdbbdaa96c3644c2b310c0843d18017081e2d6e78f34";
    private const string FreezeId = "901f320660c170df95deb1fff73d4af04dfad22d4ed84f0571b6d1a49bcc647a";

    /// <summary>TronGrid's votewitnessaccount: one vote from <see cref="Owner"/> for <see cref="Witness"/>.</summary>
    private const string VoteHex =
        "0a02d739220824845a7be2657feb40c8a5dae78f345a6a080412660a30747970652e676f6f676c65617069732e636f6d2f70726f746f63" +
        "6f6c2e566f74655769746e657373436f6e747261637412320a1541c64e69acde1c7b16c2a3efcdbbdaa96c3644c2b312190a154162398d516b555ac" +
        "64af24416e05c199c01823048100170c3e2d6e78f34";

    [Fact]
    public void A_real_freeze_is_read_back_exactly_and_its_id_is_its_hash()
    {
        var raw = Convert.FromHexString(FreezeHex);
        Assert.Equal(FreezeId, TronStaking.TxId(raw));

        var owner = TronStakingClient.Address21(Owner);
        Assert.Null(TronStaking.CheckFreeze(raw, owner, 1_000_000, TronResource.Energy));

        // Anything else asked of the same bytes is refused.
        Assert.NotNull(TronStaking.CheckFreeze(raw, owner, 2_000_000, TronResource.Energy));
        Assert.NotNull(TronStaking.CheckFreeze(raw, owner, 1_000_000, TronResource.Bandwidth));
        Assert.NotNull(TronStaking.CheckFreeze(raw, TronStakingClient.Address21(Witness), 1_000_000, TronResource.Energy));
        Assert.NotNull(TronStaking.CheckUnfreeze(raw, owner, 1_000_000, TronResource.Energy));   // a freeze is not an unfreeze
        Assert.NotNull(TronStaking.CheckVote(raw, owner, TronStakingClient.Address21(Witness), 1));
    }

    [Fact]
    public void A_real_vote_is_one_ballot_for_the_chosen_representative()
    {
        var raw = Convert.FromHexString(VoteHex);
        var owner = TronStakingClient.Address21(Owner);
        var witness = TronStakingClient.Address21(Witness);

        Assert.Null(TronStaking.CheckVote(raw, owner, witness, 1));
        Assert.NotNull(TronStaking.CheckVote(raw, owner, witness, 2));
        Assert.NotNull(TronStaking.CheckVote(raw, owner, owner, 1));
        Assert.NotNull(TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawBalance, owner));
    }

    [Fact]
    public void A_transaction_with_two_contracts_is_never_what_was_asked_for()
    {
        var raw = Convert.FromHexString(FreezeHex);
        var contract = raw.AsSpan(raw.Length - 7 - 0x59 - 2, 0x59 + 2).ToArray();   // field 11 as it stands
        Assert.Equal(0x5A, contract[0]);
        var doubled = raw.Take(raw.Length - 7).Concat(contract).Concat(raw.Skip(raw.Length - 7)).ToArray();

        Assert.Null(TronStaking.ReadContract(doubled));
        Assert.NotNull(TronStaking.CheckFreeze(doubled, TronStakingClient.Address21(Owner), 1_000_000, TronResource.Energy));
    }

    [Fact]
    public void An_owner_only_contract_is_checked_for_its_type_and_owner()
    {
        var owner = TronStakingClient.Address21(Owner);
        var value = Proto(1, owner);                                   // WithdrawBalanceContract { owner_address }
        var any = Concat(Proto(1, "type.googleapis.com/protocol.WithdrawBalanceContract"u8.ToArray()), Proto(2, value));
        var contract = Concat([0x08, (byte)TronStaking.WithdrawBalance], Proto(2, any));
        var raw = Proto(11, contract);

        Assert.Null(TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawBalance, owner));
        Assert.NotNull(TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawExpireUnfreeze, owner));
        Assert.NotNull(TronStaking.CheckOwnerOnly(raw, TronStaking.WithdrawBalance, TronStakingClient.Address21(Witness)));
    }

    [Fact]
    public void A_position_reads_frozen_votes_rewards_and_the_unfreeze_queue()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        using var account = JsonDocument.Parse("""
            {"frozenV2":[{"amount":5000000},{"type":"ENERGY","amount":100000000},{"type":"TRON_POWER"}],
             "votes":[{"vote_address":"TJvaAeFb8Lykt9RQcVyyTFN2iDvGMuyD4M","vote_count":105}],
             "unfrozenV2":[{"type":"ENERGY","unfreeze_amount":3000000,"unfreeze_expire_time":1799000000000},
                           {"unfreeze_amount":7000000,"unfreeze_expire_time":1801000000000}]}
            """);
        using var reward = JsonDocument.Parse("""{"reward":1234567}""");

        var p = TronStakingClient.ParsePosition(account.RootElement, reward.RootElement, now);

        Assert.Equal(105_000_000, p.FrozenSun);
        Assert.Equal(105, p.VotesCast);
        Assert.Equal(Witness, p.VotedFor);
        Assert.Equal(1_234_567, p.RewardSun);
        Assert.Equal(3_000_000, p.WithdrawableSun);
        Assert.Equal(7_000_000, p.UnfreezingSun);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_801_000_000_000), p.NextUnfreeze);
    }

    [Fact]
    public void Super_representatives_are_the_elected_ones_most_voted_first()
    {
        using var doc = JsonDocument.Parse("""
            {"witnesses":[
              {"address":"TU3kjFuhtEo42tsCBtfYUAZxoqQ4yuSLQ5","voteCount":5,"url":"https://b.example","isJobs":true},
              {"address":"TJvaAeFb8Lykt9RQcVyyTFN2iDvGMuyD4M","voteCount":9,"url":"https://a.example","isJobs":true},
              {"address":"TLyqzVGLV1srkB7dToTAEqgDSfPtXRJZYH","voteCount":99,"url":"https://c.example"}]}
            """);
        var list = TronStakingClient.ParseWitnesses(doc.RootElement);

        Assert.Equal(2, list.Count);   // the candidate that produces no blocks is left out
        Assert.Equal(Witness, list[0].Address);
        Assert.Equal(1, list[0].Rank);
    }

    // ---------------- Solana ----------------

    /// <summary>solana-web3.js's own vector for createWithSeed.</summary>
    [Fact]
    public void A_seeded_address_is_the_one_the_system_program_derives()
    {
        var zero = new byte[32];
        var address = SolanaStake.AddressWithSeed(zero, "limber chicken: 4/45", zero);
        Assert.Equal("9h1HyLCW5dZnBVap8C5egQ9Z6pHyjsh5MNy83iPqqRuq", SolanaKeys.Encode(address));
    }

    [Fact]
    public void A_new_stake_is_create_initialize_delegate_signed_by_the_wallet_alone()
    {
        SolanaKeys.TryDecode("5tzFkiKscXHK5ZXCGbXZxdw7gTjjD1mBwuoFbhUvuAi9", out var wallet);
        SolanaKeys.TryDecode("CcaHc2L43ZWjwCHART3oZoJvHLAe9hzT2DJNUpBzoTN1", out var vote);
        var ixs = SolanaStake.StakeInstructions(wallet, 3, vote, 1_000_000_000, 2_282_880);
        var stake = SolanaStake.StakeAccountFor(wallet, 3);

        Assert.Equal(3, ixs.Count);

        var create = ixs[0];
        Assert.Equal(new byte[32], create.ProgramId);
        var seed = "phobia-stake-3"u8.ToArray();
        Assert.Equal(4 + 32 + 8 + seed.Length + 8 + 8 + 32, create.Data.Length);
        Assert.Equal(3u, BitConverter.ToUInt32(create.Data, 0));
        Assert.Equal((ulong)seed.Length, BitConverter.ToUInt64(create.Data, 36));
        Assert.Equal(1_002_282_880UL, BitConverter.ToUInt64(create.Data, 44 + seed.Length));
        Assert.Equal(SolanaStake.AccountSpace, BitConverter.ToUInt64(create.Data, 52 + seed.Length));
        Assert.Equal(stake, create.Accounts[1].Key);

        var init = ixs[1];
        Assert.Equal(SolanaStake.ProgramId, init.ProgramId);
        Assert.Equal(4 + 32 + 32 + 8 + 8 + 32, init.Data.Length);
        Assert.Equal(wallet, init.Data[4..36]);    // staker
        Assert.Equal(wallet, init.Data[36..68]);   // withdrawer
        Assert.All(init.Data[68..], b => Assert.Equal(0, b));   // no lockup

        var delegate_ = ixs[2];
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, delegate_.Data);
        Assert.Equal(6, delegate_.Accounts.Count);
        Assert.Equal(vote, delegate_.Accounts[1].Key);
        Assert.True(delegate_.Accounts[5].IsSigner);

        // One signer: the wallet pays, is the seed's base, and is the staker.
        var message = SolanaMessage.Compile(wallet, ixs, new byte[32]);
        Assert.Equal(1, message[0]);
    }

    [Fact]
    public void Undelegating_and_withdrawing_are_the_stake_programs_instructions_5_and_4()
    {
        var wallet = new byte[32]; wallet[0] = 7;
        var stake = new byte[32]; stake[0] = 9;

        var deactivate = SolanaStake.Deactivate(stake, wallet);
        Assert.Equal(new byte[] { 5, 0, 0, 0 }, deactivate.Data);
        Assert.Equal(3, deactivate.Accounts.Count);

        var withdraw = SolanaStake.Withdraw(stake, wallet, wallet, 123_456);
        Assert.Equal(4u, BitConverter.ToUInt32(withdraw.Data, 0));
        Assert.Equal(123_456UL, BitConverter.ToUInt64(withdraw.Data, 4));
        Assert.Equal(5, withdraw.Accounts.Count);
        Assert.True(withdraw.Accounts[4].IsSigner);
    }

    [Theory]
    [InlineData("delegated", 500, "18446744073709551615", 510, SolanaStakeState.Active)]
    [InlineData("delegated", 510, "18446744073709551615", 510, SolanaStakeState.Activating)]
    [InlineData("delegated", 500, "510", 510, SolanaStakeState.Deactivating)]
    [InlineData("delegated", 500, "509", 510, SolanaStakeState.Inactive)]
    [InlineData("initialized", 0, "0", 510, SolanaStakeState.Initialized)]
    public void A_stake_account_is_placed_against_the_current_epoch(
        string type, int activation, string deactivation, int epoch, SolanaStakeState expected)
    {
        const string wallet = "5tzFkiKscXHK5ZXCGbXZxdw7gTjjD1mBwuoFbhUvuAi9";
        var json = """
            {"lamports":1002282880,"data":{"parsed":{"type":"TYPE","info":{
              "meta":{"rentExemptReserve":"2282880","authorized":{"staker":"WALLET","withdrawer":"WALLET"}},
              "stake":{"delegation":{"voter":"CcaHc2L43ZWjwCHART3oZoJvHLAe9hzT2DJNUpBzoTN1","stake":"1000000000",
                "activationEpoch":"ACTIVATION","deactivationEpoch":"DEACTIVATION"}}}}}}
            """.Replace("TYPE", type).Replace("WALLET", wallet).Replace("DEACTIVATION", deactivation)
            .Replace("ACTIVATION", activation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var doc = JsonDocument.Parse(json);
        var account = SolanaStake.Parse("stake1", doc.RootElement, (ulong)epoch, wallet);

        Assert.NotNull(account);
        Assert.Equal(expected, account!.State);
        Assert.Equal(expected is SolanaStakeState.Initialized or SolanaStakeState.Inactive, account.CanWithdraw);
        Assert.Null(SolanaStake.Parse("stake1", doc.RootElement, (ulong)epoch, "SomeoneElse11111111111111111111111111111111"));
    }

    // ---------------- Cosmos Hub ----------------

    /// <summary>A real, checksummed address: a fresh key's.</summary>
    private static readonly string Delegator = CosmosHub.AddressFromAccountId(new Key().PubKey.Compress().Hash.ToBytes());
    private const string Validator = "cosmosvaloper15gyzcp2kas2yntv9k3p0zm4k8y6e89ecmkeqee";

    [Fact]
    public void A_transfer_built_here_is_byte_identical_to_the_send_paths()
    {
        var pub = new Key().PubKey.Compress().ToBytes();
        var send = new CosmosSend(Delegator, Delegator, 12345, CosmosHub.Denom, "memo", 99, pub, 7, 3, 5000, 200000, "cosmoshub-4");
        var tx = new CosmosTx(Delegator, [CosmosStaking.Send(Delegator, Delegator, 12345)], "memo", 99, pub, 7, 3, 5000, 200000, "cosmoshub-4");

        Assert.Equal(CosmosTransactions.BodyBytes(send), CosmosStaking.BodyBytes(tx));
        Assert.Equal(CosmosTransactions.AuthInfoBytes(send), CosmosStaking.AuthInfoBytes(tx));
        Assert.Equal(CosmosTransactions.SimulationBytes(send), CosmosStaking.SimulationBytes(tx));
    }

    [Fact]
    public void Staking_messages_carry_their_type_and_refuse_wrong_addresses()
    {
        Assert.Equal(CosmosStaking.MsgDelegateType, CosmosStaking.Delegate(Delegator, Validator, 1).TypeUrl);
        Assert.Equal(CosmosStaking.MsgUndelegateType, CosmosStaking.Undelegate(Delegator, Validator, 1).TypeUrl);
        Assert.Equal(CosmosStaking.MsgWithdrawRewardType, CosmosStaking.WithdrawReward(Delegator, Validator).TypeUrl);
        Assert.Throws<ArgumentException>(() => CosmosStaking.Delegate(Validator, Delegator, 1));
        Assert.Throws<ArgumentException>(() => CosmosStaking.Delegate(Delegator, Validator, 0));
        Assert.True(CosmosStaking.IsValidValidator(Validator));
        Assert.False(CosmosStaking.IsValidValidator(Delegator));
    }

    [Fact]
    public void A_signed_staking_transaction_verifies_against_the_key()
    {
        var key = new Key();
        var pub = key.PubKey.Compress().ToBytes();
        var address = CosmosHub.AddressFromAccountId(key.PubKey.Compress().Hash.ToBytes());
        var tx = new CosmosTx(address, [CosmosStaking.Delegate(address, Validator, 1_000_000)], "", 100, pub, 1, 0, 5000, 250000, "cosmoshub-4");

        var (bytes, hash) = CosmosStaking.Sign(tx, key);

        Assert.Equal(64, hash.Length);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), hash);
        Assert.Throws<InvalidOperationException>(() => CosmosStaking.Sign(tx with { Signer = Delegator }, key));
    }

    [Fact]
    public void Delegations_rewards_and_unbonding_are_read_as_the_node_reports_them()
    {
        using var delegations = JsonDocument.Parse("""
            {"delegation_responses":[{"delegation":{"delegator_address":"DELEGATOR","validator_address":"VALIDATOR","shares":"428420179.000000000000000000"},
              "balance":{"denom":"uatom","amount":"428420179"}}]}
            """.Replace("DELEGATOR", Delegator).Replace("VALIDATOR", Validator));
        using var rewards = JsonDocument.Parse("""
            {"rewards":[{"validator_address":"VALIDATOR","reward":[{"denom":"uatom","amount":"126.741065423154114229"}]}],
             "total":[{"denom":"uatom","amount":"126.741065423154114229"}]}
            """.Replace("DELEGATOR", Delegator).Replace("VALIDATOR", Validator));
        using var unbonding = JsonDocument.Parse("""
            {"unbonding_responses":[{"validator_address":"VALIDATOR","entries":[{"completion_time":"2026-10-23T10:00:00Z","balance":"5000000"}]}]}
            """.Replace("DELEGATOR", Delegator).Replace("VALIDATOR", Validator));
        using var validators = JsonDocument.Parse("""
            {"validators":[
              {"operator_address":"VALIDATOR","jailed":false,"description":{"moniker":"Kiln"},"commission":{"commission_rates":{"rate":"0.080000000000000000"}},"tokens":"21900000000000"},
              {"operator_address":"VALIDATOR","jailed":true,"description":{"moniker":"Jailed"},"commission":{"commission_rates":{"rate":"0.05"}},"tokens":"1"}]}
            """.Replace("DELEGATOR", Delegator).Replace("VALIDATOR", Validator));

        var d = Assert.Single(CosmosStaking.ParseDelegations(delegations.RootElement, rewards.RootElement));
        Assert.Equal(428.420179m, d.StakedAtom);
        Assert.Equal(0.000126741065423154114229m, d.RewardAtom);
        var u = Assert.Single(CosmosStaking.ParseUnbonding(unbonding.RootElement));
        Assert.Equal(5m, u.Atom);
        var v = Assert.Single(CosmosStaking.ParseValidators(validators.RootElement));
        Assert.Equal("Kiln", v.Moniker);
        Assert.Equal(0.08m, v.Commission);
    }

    private static byte[] Proto(int field, byte[] value)
    {
        var key = (byte)((field << 3) | 2);
        return [key, (byte)value.Length, .. value];
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
