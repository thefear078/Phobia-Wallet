using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The paths other wallets use, searched for money an imported phrase holds where this wallet does not
/// look. Pinned to the published addresses of the BIP39 test phrase, and to this wallet's own
/// derivation where a path coincides with one it already makes, so a wrong path cannot pass as "no
/// funds there".
/// </summary>
public class AlternativeAccountsTests
{
    private const string Phrase =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly IReadOnlyList<AlternativeAccount> Accounts =
        new HdAddressDeriver().DeriveAlternativeAccounts(Phrase, passphrase: "");

    private static string At(string path) => Accounts.Single(a => a.Path == path).Address;

    [Fact]
    public void LegacyAndNestedSegwitBitcoinMatchThePublishedAddresses()
    {
        Assert.Equal("1LqBGSKuX5yYUonjxT5qGfpUsXKYYWeabA", At("m/44'/0'/0'/0/0"));   // BIP44
        Assert.Equal("37VucYSaXLCAsxYyAPfbSi9eh4iEcbShgf", At("m/49'/0'/0'/0/0"));   // BIP49
    }

    [Fact]
    public void MetaMasksSecondAccountIsTheStandardPathsNextIndex()
    {
        var deriver = new HdAddressDeriver();
        Assert.Equal("0x6Fac4D18c912343BF86fa7049364Dd4E424Ab9C0", At("m/44'/60'/0'/0/1"));
        Assert.Equal(deriver.DeriveReceiveAddress(Phrase, ChainId.Eth, 1, "").Address, At("m/44'/60'/0'/0/1"));
        Assert.Equal(deriver.DeriveReceiveAddress(Phrase, ChainId.Tron, 1, "").Address, At("m/44'/195'/0'/0/1"));
    }

    [Fact]
    public void NothingDuplicatesTheAddressesTheWalletAlreadyUses()
    {
        var deriver = new HdAddressDeriver();
        var own = new[] { ChainId.Eth, ChainId.Tron, ChainId.Sol, ChainId.Btc, ChainId.Ltc }
            .Select(c => deriver.DeriveReceiveAddress(Phrase, c, 0, "").Address)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Accounts, a => own.Contains(a.Address));
        Assert.Equal(Accounts.Count, Accounts.Select(a => a.Address).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryEntryNamesWhereItComesFrom()
    {
        Assert.All(Accounts, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Origin));
            Assert.True(a.Account >= 1);
            Assert.StartsWith("m/", a.Path);
        });
    }
}
