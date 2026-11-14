using System.Numerics;
using System.Security.Cryptography;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// NEAR staking (roadmap N.16): calls to a validator's staking pool. Pinned to three real mainnet
/// transactions read from the network on 2026-10-28 — a deposit_and_stake, an unstake and a
/// withdraw_all to astro-stakers.poolv1.near: rebuilt from their fields, each gives the hash the network
/// recorded, and each one's own signature verifies over the bytes rebuilt here. (The reference block hash
/// is not in the RPC's view of a transaction; it was recovered as the block whose hash reproduces the
/// recorded transaction hash.)
/// </summary>
public sealed class NearStakingTests
{
    public static TheoryData<string, string, ulong, string, string, ulong, string, string, string, string> RealCalls() => new()
    {
        {
            "nkdiamond.near", "6jS67jdjHHqWTq1ySNmkKjvh34KLmyTaDpyaz2fjX2qZ", 204716358000013, "CYfvR1gK2yGLMxB587w13nxqoyPHwSfSRSvW8drZu5Ca",
            NearStaking.DepositAndStake, 29_000_000_000_000, "566000000000000000000000000",
            "Cda3nXJtYrGycJX2cnHpJiqcWtcdmBriq315LxEvcLkE",
            "28AEmqRcKqWP8jVKC1qHQjZac23qnvABobDvrnnS5VjYqNL6Prr6xUXwF6o8w7bsu93cSfL9FC2zsCyeuYmv722i", "{}"
        },
        {
            "nicklesshots.near", "EsdwViwP5aeqZMtLwmxTCD4KdhX7CRvAZu9CUKC4GG7z", 219862137000095, "7suDWRNHoYHWJ7HaEaJMJAXHunhuNzNUfjNeLBFu7miK",
            NearStaking.Unstake, 28_000_000_000_000, "66644760199905062436",
            "E8wZECoobtR3JmgSSzkPye3xNxaK4infkM5pEJwYZH2G",
            "632dVZzTtXvEHPv1AS5YSYf5rpfMYbSUzNHGPuJDbujtUsDh4SppttyZ3CrbHve7mEfxtjNgDidpcjvCBWDp1o4s", "{\"amount\":\"66644760199905062436\"}"
        },
        {
            "8ebe571bdb371f98442402b3870bd29a1c289a0a7fbae89ac21d81c8826438be", "AcDEQ27ZCUF8JuP3nKFJKDMfsxGA8rhB7JyJVQivjNM3", 133705114000007, "BdSVpMucM8ytjtfWNyfu4jDzh8KBfBTU8SmJtLJK7UQp",
            NearStaking.WithdrawAll, 50_000_000_000_000, "0",
            "7NfXtCQrh9tA4eVofAwqYQUvzjZES1KvtAb2riSpDA3N",
            "3sUh6TJY6V2wxUspLwuowys72wwhy6ZSuMBfEvdPmYdpgk9pYtbLvcsooU8DY1B6dWpFT7DVR6yae6tbHgLxdwWq", "{}"
        },
    };

    [Theory]
    [MemberData(nameof(RealCalls))]
    public void A_real_pool_call_is_rebuilt_byte_for_byte(
        string signer, string publicKey, ulong nonce, string blockHash, string method, ulong gas, string yocto,
        string expectedHash, string signature, string expectedArgs)
    {
        var call = NearStaking.Call(signer, Encoders.Base58.DecodeData(publicKey), nonce, Encoders.Base58.DecodeData(blockHash),
            "astro-stakers.poolv1.near", method, BigInteger.Parse(yocto), gas);

        Assert.Equal(expectedArgs, call.ArgsJson);
        Assert.Equal(expectedHash, NearTransactions.Hash(call));

        // The signer's own signature verifies over the bytes built here: they are what was signed.
        var digest = SHA256.HashData(NearTransactions.StakeTransactionBytes(call));
        Assert.True(Ed25519.Verify(Encoders.Base58.DecodeData(signature), 0, Encoders.Base58.DecodeData(publicKey), 0, digest, 0, digest.Length));
    }

    private static NearStakeCall Valid(string method = NearStaking.DepositAndStake) =>
        NearStaking.Call("alice.near", new byte[32], 1, new byte[32], "astro-stakers.poolv1.near", method, BigInteger.Pow(10, 24));

    [Theory]
    [InlineData("alice.near")]
    [InlineData("poolv1.near")]
    [InlineData("evil.poolv1.near.attacker.near")]
    public void Only_a_staking_pool_receives_a_call(string receiver)
    {
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid() with { PoolId = receiver }));
    }

    [Fact]
    public void Nothing_but_the_four_pool_calls_can_be_expressed()
    {
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid() with { Method = "ft_transfer" }));
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid() with { ArgsJson = "{\"receiver_id\":\"evil.near\"}" }));
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid(NearStaking.Unstake) with { DepositYocto = BigInteger.One }));
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid(NearStaking.Unstake) with { ArgsJson = "{\"amount\":\"1\",\"x\":1}" }));
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid() with { DepositYocto = BigInteger.Zero }));
        Assert.Throws<ArgumentException>(() => NearTransactions.StakeTransactionBytes(Valid() with { Gas = NearTransactions.MaxGas + 1 }));
        Assert.Throws<ArgumentException>(() => NearStaking.Call("alice.near", new byte[32], 1, new byte[32], "astro-stakers.poolv1.near", "storage_withdraw", BigInteger.One));
    }

    [Fact]
    public void A_seed_that_is_not_the_signers_is_refused()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        Assert.Throws<InvalidOperationException>(() => NearTransactions.Sign(Valid(), seed));
    }
}
