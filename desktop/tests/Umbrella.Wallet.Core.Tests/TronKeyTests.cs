using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// TRX and USDT (TRC-20) both spend from the TRON account, so the signing key must belong to the
/// exact address the wallet shows. If it didn't, a send would draw on an account the user never
/// funded — the same gate ETH, BTC/LTC and SOL already have.
/// </summary>
public sealed class TronKeyTests
{
    private const string Mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;

    [Fact]
    public void Signing_key_matches_the_displayed_tron_address()
    {
        var deriver = new HdAddressDeriver();

        var shown = deriver.DeriveReceiveAddress(Mnemonic, ChainId.Tron, 0).Address;
        var key = deriver.DeriveTronKey(Mnemonic, 0);

        // Rebuild the TRON address from the key: 0x41 ‖ keccak(uncompressed pubkey)[-20:], base58check.
        var uncompressed = key.PubKey.Decompress().ToBytes();
        var hash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(uncompressed[1..]);
        var payload = new byte[21];
        payload[0] = 0x41;
        Buffer.BlockCopy(hash, 12, payload, 1, 20);

        var checksum = System.Security.Cryptography.SHA256.HashData(
            System.Security.Cryptography.SHA256.HashData(payload))[..4];
        var full = payload.Concat(checksum).ToArray();

        Assert.Equal(shown, NBitcoin.DataEncoders.Encoders.Base58.EncodeData(full));
    }

    [Fact]
    public void Tron_key_is_deterministic_and_index_dependent()
    {
        var deriver = new HdAddressDeriver();

        Assert.Equal(
            deriver.DeriveTronKey(Mnemonic).ToHex(),
            deriver.DeriveTronKey(Mnemonic).ToHex());

        Assert.NotEqual(
            deriver.DeriveTronKey(Mnemonic, 0).ToHex(),
            deriver.DeriveTronKey(Mnemonic, 1).ToHex());
    }
}
