using System.Numerics;
using Nethereum.Signer;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the ETH signer to the OFFICIAL EIP-155 example transaction
/// (https://eips.ethereum.org/EIPS/eip-155, "Example" section) byte-for-byte.
/// A wrong signature would burn gas or strand funds, so this is a hard gate:
/// if this test fails, sending must not ship.
/// </summary>
public sealed class EthSignerTests
{
    [Fact]
    public void Signer_matches_the_official_eip155_example_byte_for_byte()
    {
        var privateKey = Convert.FromHexString(
            "4646464646464646464646464646464646464646464646464646464646464646");

        var signed = EthTransactionSender.SignTransfer(
            privateKey,
            to: "0x3535353535353535353535353535353535353535",
            amountWei: BigInteger.Parse("1000000000000000000"),   // 1 ETH
            nonce: 9,
            gasPriceWei: BigInteger.Parse("20000000000"),          // 20 gwei
            gasLimit: 21000);

        const string expected =
            "f86c098504a817c800825208943535353535353535353535353535353535353535880de0b6b3a76400008025a0" +
            "28ef61340bd939bc2195fe537567866003e1a15d3c71ff63e1590620aa636276a0" +
            "67cbe9d8997f761aecb703304b3800ccf555c9f3dc64214b297fb1966a3b6d83";

        Assert.Equal(expected, signed.ToLowerInvariant());
    }

    /// <summary>
    /// The exported private key must correspond to the SAME address the wallet displays for
    /// receiving — otherwise a send would spend from a different account than the user funded.
    /// </summary>
    [Fact]
    public void Derived_private_key_matches_the_displayed_receive_address()
    {
        const string mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;
        var deriver = new HdAddressDeriver();

        var shown = deriver.DeriveReceiveAddress(mnemonic, ChainId.Eth, 0).Address;
        var priv = deriver.DeriveEthereumPrivateKey(mnemonic, 0);
        var fromKey = new EthECKey(priv, true).GetPublicAddress();

        Assert.Equal(shown, fromKey);
    }
}
