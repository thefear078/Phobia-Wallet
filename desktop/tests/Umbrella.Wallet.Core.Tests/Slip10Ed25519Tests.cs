using System.Globalization;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Validates the ed25519 derivation (Solana) against the OFFICIAL SLIP-0010 test vectors:
/// https://github.com/satoshilabs/slips/blob/master/slip-0010.md#test-vector-1-for-ed25519
///
/// Getting a receive address wrong strands funds, so this checks both the SLIP-0010 key
/// schedule and BouncyCastle's ed25519 public-key generation, byte for byte.
/// </summary>
public sealed class Slip10Ed25519Tests
{
    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex);

    private static string HexOf(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    // Test vector 1 seed.
    private static readonly byte[] Seed1 = Hex("000102030405060708090a0b0c0d0e0f");

    [Theory]
    // path (unhardened numbers), expected private scalar, expected public key (no 0x00 prefix)
    [InlineData(new uint[] { }, "2b4be7f19ee27bbf30c667b642d5f4aa69fd169872f8fc3059c08ebae2eb19e7",
        "a4b2856bfec510abab89753fac1ac0e1112364e7d250545963f135f2a33188ed")]
    [InlineData(new uint[] { 0 }, "68e0fe46dfb67e368c75379acec591dad19df3cde26e63b93a8e704f1dade7a3",
        "8c8a13df77a28f3445213a0f432fde644acaa215fc72dcdf300d5efaa85d350c")]
    [InlineData(new uint[] { 0, 1 }, "b1d0bad404bf35da785a64ca1ac54b2617211d2777696fbffaf208f746ae84f2",
        "1932a5270f335bed617d5b935c80aedb1a35bd9fc1e31acafd5372c30f5c1187")]
    public void Slip10_Vector1(uint[] path, string expectedPriv, string expectedPub)
    {
        var priv = Slip10Ed25519.DerivePrivateKey(Seed1, path);
        Assert.Equal(expectedPriv, HexOf(priv));

        var pub = Slip10Ed25519.PublicKey(priv);
        Assert.Equal(expectedPub, HexOf(pub));
    }

    /// <summary>Solana address is deterministic and base58 for a fixed mnemonic.</summary>
    [Fact]
    public void Solana_DerivesStableBase58Address()
    {
        const string mnemonic =
            "abandon abandon abandon abandon abandon abandon abandon abandon " +
            "abandon abandon abandon abandon abandon abandon abandon abandon " +
            "abandon abandon abandon abandon abandon abandon abandon art";

        var deriver = new HdAddressDeriver(new Bip39MnemonicService());
        var first = deriver.DeriveReceiveAddress(mnemonic, ChainId.Sol);
        var again = deriver.DeriveReceiveAddress(mnemonic, ChainId.Sol);

        Assert.Equal(first.Address, again.Address);
        Assert.Equal("m/44'/501'/0'/0'", first.DerivationPath);
        // base58 Solana addresses are 32–44 chars and contain no 0, O, I, or l.
        Assert.InRange(first.Address.Length, 32, 44);
        Assert.DoesNotContain(first.Address, c => c is '0' or 'O' or 'I' or 'l');
    }
}
