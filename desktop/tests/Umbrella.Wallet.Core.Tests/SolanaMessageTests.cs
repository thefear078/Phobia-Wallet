using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Solana transfer message layout. A malformed message would either be rejected by the
/// cluster or — worse — move the wrong amount, so the byte layout is asserted explicitly.
/// </summary>
public sealed class SolanaMessageTests
{
    private static byte[] Key(byte fill)
    {
        var key = new byte[32];
        Array.Fill(key, fill);
        return key;
    }

    [Fact]
    public void Transfer_message_has_the_exact_legacy_layout()
    {
        var from = Key(0x11);
        var to = Key(0x22);
        var blockhash = Key(0x33);
        const ulong lamports = 1_000_000_000; // 1 SOL

        var message = SolanaTransactionSender.BuildTransferMessage(from, to, lamports, blockhash);

        var i = 0;
        Assert.Equal(1, message[i++]);   // required signatures
        Assert.Equal(0, message[i++]);   // readonly signed
        Assert.Equal(1, message[i++]);   // readonly unsigned (system program)

        Assert.Equal(3, message[i++]);   // account count
        Assert.Equal(from, message[i..(i + 32)]); i += 32;
        Assert.Equal(to, message[i..(i + 32)]); i += 32;
        Assert.Equal(new byte[32], message[i..(i + 32)]); i += 32;   // System Program = all zero

        Assert.Equal(blockhash, message[i..(i + 32)]); i += 32;

        Assert.Equal(1, message[i++]);   // one instruction
        Assert.Equal(2, message[i++]);   // program id index → system program
        Assert.Equal(2, message[i++]);   // account count
        Assert.Equal(0, message[i++]);   // from index
        Assert.Equal(1, message[i++]);   // to index
        Assert.Equal(12, message[i++]);  // data length

        Assert.Equal(2u, BitConverter.ToUInt32(message, i)); i += 4;      // System instruction 2 = Transfer
        Assert.Equal(lamports, BitConverter.ToUInt64(message, i)); i += 8;

        Assert.Equal(message.Length, i);
    }

    /// <summary>The signature we attach must verify against the sending account's public key.</summary>
    [Fact]
    public void Signature_verifies_against_the_wallets_own_solana_key()
    {
        const string mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;
        var deriver = new HdAddressDeriver();

        var priv = deriver.DeriveSolanaPrivateKey(mnemonic);
        var shownAddress = deriver.DeriveReceiveAddress(mnemonic, ChainId.Sol).Address;

        var pub = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(priv, 0, pub, 0);

        // The signing key must correspond to the address the wallet shows for receiving.
        Assert.Equal(shownAddress, Encoders.Base58.EncodeData(pub));

        var message = SolanaTransactionSender.BuildTransferMessage(pub, Key(0x44), 12_345, Key(0x55));
        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(priv, 0, message, 0, message.Length, signature, 0);

        Assert.True(Ed25519.Verify(signature, 0, pub, 0, message, 0, message.Length));
    }
}
