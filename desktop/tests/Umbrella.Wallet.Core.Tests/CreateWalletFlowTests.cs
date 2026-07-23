using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Covers the exact sequence the Create-wallet button runs, after it was reported as
/// "no phrase is generated". The generator and vault were fine; the form swallowed
/// validation errors into the title bar. These tests pin the flow itself.
/// </summary>
public sealed class CreateWalletFlowTests : IDisposable
{
    private const string Password = "a-very-long-vault-password";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"umbrella-create-flow-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateWallet_ProducesA24WordPhrase_AndPersistsIt()
    {
        var mnemonics = new Bip39MnemonicService();
        var vault = new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json"));

        var phrase = mnemonics.Generate();
        await vault.CreateAsync(phrase, Password);

        Assert.Equal(24, phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(mnemonics.Validate(phrase).IsValid);
        Assert.Equal(phrase, await vault.UnlockAsync(Password));
    }

    [Fact]
    public void Generate_ProducesADifferentPhraseEveryTime()
    {
        var mnemonics = new Bip39MnemonicService();
        var phrases = Enumerable.Range(0, 16).Select(_ => mnemonics.Generate()).ToList();

        Assert.Equal(phrases.Count, phrases.Distinct().Count());
    }

    /// <summary>
    /// The vault throws below 12 chars. The form must reject at the same boundary, otherwise
    /// the click dies inside the vault and looks like a dead button.
    /// </summary>
    [Fact]
    public async Task CreateWallet_ShortPassword_IsRejectedByTheVault()
    {
        var vault = new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => vault.CreateAsync(new Bip39MnemonicService().Generate(), "short"));
        Assert.False(vault.Exists);
    }

    [Fact]
    public async Task AfterCreate_EverySupportedChainDerivesARealAddress()
    {
        var vault = new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json"));
        var phrase = new Bip39MnemonicService().Generate();
        await vault.CreateAsync(phrase, Password);

        var mnemonic = await vault.UnlockAsync(Password);
        var deriver = new HdAddressDeriver();

        foreach (var chain in ChainCatalog.Supported)
        {
            var account = deriver.DeriveReceiveAddress(mnemonic, chain.Id);

            Assert.False(string.IsNullOrWhiteSpace(account.Address));
            Assert.DoesNotContain("Unlock", account.Address, StringComparison.Ordinal);
            Assert.DoesNotContain("Adapter", account.Address, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Monero has no BIP44 path and its own 25-word seed, so a BIP39-derived XMR address would
    /// not be restorable in any real Monero wallet. It must refuse rather than invent one.
    /// </summary>
    [Fact]
    public async Task PlannedChains_RefuseToDeriveInsteadOfInventingAnAddress()
    {
        var vault = new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json"));
        var phrase = new Bip39MnemonicService().Generate();
        await vault.CreateAsync(phrase, Password);

        var mnemonic = await vault.UnlockAsync(Password);
        var deriver = new HdAddressDeriver();

        Assert.Contains(ChainCatalog.Planned, c => c.Id == ChainId.Ton);
        foreach (var chain in ChainCatalog.Planned)
        {
            Assert.Throws<UnsupportedChainException>(
                () => deriver.DeriveReceiveAddress(mnemonic, chain.Id));
        }

        // Monero is receive-only: it MUST produce a real 95-char mainnet address, not a stub.
        var monero = deriver.DeriveReceiveAddress(mnemonic, ChainId.Xmr);
        Assert.Equal(95, monero.Address.Length);
        Assert.StartsWith("4", monero.Address, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
