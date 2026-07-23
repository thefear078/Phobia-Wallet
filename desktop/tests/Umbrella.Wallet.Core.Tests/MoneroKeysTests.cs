using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Hard gate for Monero. XMR only ships if every one of these passes, because a wrong address
/// silently burns funds. Each primitive is pinned to a value published outside this codebase.
/// </summary>
public sealed class MoneroKeysTests
{
    /// <summary>
    /// The Monero project's own published donation address. Decoding it with our base58 and
    /// re-deriving its Keccak checksum proves base58, the 69-byte layout, the mainnet prefix
    /// and the checksum rule are all exactly Monero's.
    /// </summary>
    private const string PublishedMoneroDonationAddress =
        "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";

    [Fact]
    public void Keccak256_matches_the_published_empty_string_vector()
    {
        // Keccak-256(""), NOT SHA3-256 (which would be a7ffc6f8bf1ed766...).
        Assert.Equal(
            "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470",
            Convert.ToHexString(MoneroKeys.Keccak256([])).ToLowerInvariant());
    }

    [Fact]
    public void ScalarMultBase_of_one_is_the_ed25519_basepoint()
    {
        var one = new byte[32];
        one[0] = 1;
        Assert.Equal(
            "5866666666666666666666666666666666666666666666666666666666666666",
            Convert.ToHexString(MoneroKeys.ScalarMultBase(one)).ToLowerInvariant());
    }

    [Fact]
    public void Decodes_a_real_published_mainnet_address_and_its_checksum()
    {
        var ok = MoneroKeys.TryDecodeAddress(
            PublishedMoneroDonationAddress, out var prefix, out var spend, out var view);

        Assert.True(ok, "Failed to decode/verify a known-good Monero mainnet address.");
        Assert.Equal(MoneroKeys.MainnetPrefix, prefix);
        Assert.Equal(32, spend.Length);
        Assert.Equal(32, view.Length);
    }

    [Fact]
    public void Base58_round_trips_the_published_address_byte_for_byte()
    {
        var raw = MoneroKeys.DecodeBase58(PublishedMoneroDonationAddress);
        Assert.Equal(69, raw.Length);
        Assert.Equal(PublishedMoneroDonationAddress, MoneroKeys.EncodeBase58(raw));
    }

    [Fact]
    public void Generated_wallet_looks_exactly_like_a_mainnet_account()
    {
        var seed = new byte[64];
        for (var i = 0; i < seed.Length; i++) seed[i] = (byte)i;

        var wallet = MoneroKeys.FromSeed(seed);

        // Mainnet primary addresses are 95 chars and begin with '4'.
        Assert.Equal(95, wallet.Address.Length);
        Assert.StartsWith("4", wallet.Address, StringComparison.Ordinal);

        // It must decode back to exactly the public keys we derived.
        Assert.True(MoneroKeys.TryDecodeAddress(wallet.Address, out var prefix, out var spend, out var view));
        Assert.Equal(MoneroKeys.MainnetPrefix, prefix);
        Assert.Equal(wallet.PublicSpendKeyHex, Convert.ToHexString(spend).ToLowerInvariant());
        Assert.Equal(wallet.PublicViewKeyHex, Convert.ToHexString(view).ToLowerInvariant());
    }

    [Fact]
    public void View_key_is_the_reduced_keccak_of_the_spend_key()
    {
        var seed = new byte[64];
        Random.Shared.NextBytes(seed);
        var wallet = MoneroKeys.FromSeed(seed);

        var spend = Convert.FromHexString(wallet.SecretSpendKeyHex);
        var expectedView = MoneroKeys.ScReduce32(MoneroKeys.Keccak256(spend));
        Assert.Equal(wallet.SecretViewKeyHex, Convert.ToHexString(expectedView).ToLowerInvariant());
    }

    [Fact]
    public void Secret_scalars_are_reduced_below_the_group_order()
    {
        var seed = new byte[64];
        Random.Shared.NextBytes(seed);
        var wallet = MoneroKeys.FromSeed(seed);

        foreach (var hex in new[] { wallet.SecretSpendKeyHex, wallet.SecretViewKeyHex })
        {
            var scalar = Convert.FromHexString(hex);
            // sc_reduce32 output is idempotent under a second reduction.
            Assert.Equal(hex, Convert.ToHexString(MoneroKeys.ScReduce32(scalar)).ToLowerInvariant());
        }
    }

    [Fact]
    public void Derivation_is_deterministic_and_seed_dependent()
    {
        var a = new byte[64];
        var b = new byte[64];
        Random.Shared.NextBytes(a);
        Array.Copy(a, b, 64);
        b[0] ^= 0xFF;

        Assert.Equal(MoneroKeys.FromSeed(a).Address, MoneroKeys.FromSeed(a).Address);
        Assert.NotEqual(MoneroKeys.FromSeed(a).Address, MoneroKeys.FromSeed(b).Address);
    }

    [Fact]
    public void Rejects_a_corrupted_address()
    {
        var corrupted = PublishedMoneroDonationAddress[..90] + "aaaaa";
        Assert.False(MoneroKeys.TryDecodeAddress(corrupted, out _, out _, out _));
    }
}
