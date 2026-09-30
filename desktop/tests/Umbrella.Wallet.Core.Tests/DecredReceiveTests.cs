using System.Text;
using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Codecs;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Decred receive and balance, against vectors from three independent places:
/// <list type="bullet">
/// <item>BLAKE-256 against the digests published for it (the SHA-3 submission and its reference code);</item>
/// <item>address encoding against Decred's own dcrd (<c>txscript/stdaddr/address_test.go</c>);</item>
/// <item>phrase → key → address against Trust Wallet Core's Decred tests.</item>
/// </list>
/// Decred's hash is BLAKE-256 where Bitcoin's is SHA-256, in both the key hash and the checksum, so a
/// wrong step here produces a well-formed address nobody holds the key to.
/// </summary>
public sealed class DecredReceiveTests
{
    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    [Theory]
    [InlineData("", "716f6e863f744b9ac22c97ec7b76ea5f5908bc5b2f67c61510bfc4751384ea7a")]
    [InlineData("The quick brown fox jumps over the lazy dog", "7576698ee9cad30173080678e5965916adbb11cb5245d386bf1ffda1cb26c9d7")]
    public void Blake256_matches_the_published_digests_of_text(string text, string digest) =>
        Assert.Equal(digest, Hex(Blake256.Hash(Encoding.ASCII.GetBytes(text))));

    [Fact]
    public void Blake256_matches_the_specifications_one_block_vector()
    {
        // The BLAKE paper's own examples: one zero byte, and 72 zero bytes (two blocks, the second
        // holding message bits AND the padding).
        Assert.Equal("0ce8d4ef4dd7cd8d62dfded9d4edb0a774ae6a41929a74da23109e8f11139c87", Hex(Blake256.Hash(new byte[1])));
        Assert.Equal("d419bad32d504fb7d44d460c42c5593fe544fa4c135dec31e21bd9abdcc22d41", Hex(Blake256.Hash(new byte[72])));
    }

    [Theory]
    // dcrd: a public-key hash and the address it must encode to.
    [InlineData("2789d58cfa0957d206f025c2af056fc8a77cebb0", "DsUZxxoHJSty8DCfwfartwTYbuhmVct7tJu")]
    [InlineData("229ebac30efd6a69eec9c1a48e048b7c975c25f2", "DsU7xcg53nxaKLLcAUSKyRndjG78Z2VZnX9")]
    public void A_key_hash_encodes_exactly_as_dcrd_does(string hash160, string address)
    {
        Assert.Equal(address, DecredAddress.FromPublicKeyHash(Convert.FromHexString(hash160)));
        var decoded = DecredAddress.TryDecode(address);
        Assert.NotNull(decoded);
        Assert.Equal(DecredAddressKind.PublicKeyHash, decoded.Value.Kind);
        Assert.Equal(hash160, Hex(decoded.Value.Hash));
    }

    [Fact]
    public void A_public_key_hashes_with_blake256_then_ripemd160()
    {
        // Trust Wallet Core: the secp256k1 generator point's compressed key.
        var pub = Convert.FromHexString("0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798");
        Assert.Equal("DsmcYVbP1Nmag2H4AS17UTvmWXmGeA7nLDx", DecredAddress.FromPublicKey(pub));
    }

    [Theory]
    // Trust Wallet Core: the same phrase at m/44'/42'/0'/0/0, without and with a BIP39 passphrase.
    [InlineData("", "DsVMHD5D86dpRnt2GPZvv4bYUJZg6B9Pzqa")]
    [InlineData("TREZOR", "DsksmLD2wDoA8g8QfFvm99ASg8KsZL8eJFd")]
    public void The_wallets_deriver_reproduces_trust_wallets_addresses(string passphrase, string address)
    {
        const string phrase = "ripple scissors kick mammal hire column oak again sun offer wealth tomorrow wagon turn fatal";
        var derived = new Umbrella.Wallet.Core.Derivation.HdAddressDeriver()
            .DeriveReceiveAddress(phrase, ChainId.Dcr, 0, passphrase);

        Assert.Equal(address, derived.Address);
        Assert.Equal("m/44'/42'/0'/0/0", derived.DerivationPath);
    }

    [Fact]
    public void A_script_hash_address_is_recognised()
    {
        // Trust Wallet Core: this Dc… address locks to a914 f5916158e3e2c4551c1796708db8367207ed13bb 87.
        var decoded = DecredAddress.TryDecode("Dcur2mcGjmENx4DhNqDctW5wJCVyT3Qeqkx");
        Assert.NotNull(decoded);
        Assert.Equal(DecredAddressKind.ScriptHash, decoded.Value.Kind);
        Assert.Equal("f5916158e3e2c4551c1796708db8367207ed13bb", Hex(decoded.Value.Hash));
    }

    [Theory]
    [InlineData("DsUZxxoHlSty8DCfwfartwTYbuhmVct7tJu")]   // dcrd: 'l' is not base58
    [InlineData("DsUZxxoHJSty8DCfwfartwTYbuhmVct7tJv")]   // last character changed: checksum fails
    [InlineData("1BoatSLRHtKNngkdXEeobR76b53LETtpyT")]     // a Bitcoin address
    [InlineData("t3gQDEavk5VzAAHK8TrQu2BWDLxEiF1unBm")]    // a Zcash address (Trust Wallet's invalid case)
    [InlineData("rnBFvgZphmN39GWzUJeUitaP22Fr9be75H")]     // an XRP address
    [InlineData("")]
    public void Anything_else_is_refused(string address) => Assert.Null(DecredAddress.TryDecode(address));

    [Fact]
    public void A_dcrdata_total_is_the_unspent_amount_and_a_bad_answer_is_unknown()
    {
        using var ok = JsonDocument.Parse("""{"address":"DsUZxxoHJSty8DCfwfartwTYbuhmVct7tJu","dcr_spent":12.5,"dcr_unspent":0.75}""");
        Assert.Equal(0.75m, DecredAddress.ParseTotals(ok.RootElement));

        using var bad = JsonDocument.Parse("""{"message":"invalid address"}""");
        Assert.Null(DecredAddress.ParseTotals(bad.RootElement));
    }
}
