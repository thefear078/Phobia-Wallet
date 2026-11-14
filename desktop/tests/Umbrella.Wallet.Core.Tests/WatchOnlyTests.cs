using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Core.Watch;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Seedless watch-only wallets (roadmap H.5): what people paste is read the way it was meant, an
/// extended public key gives exactly the addresses the phrase behind it gives, and the description is
/// sealed beside the vault.
/// </summary>
public sealed class WatchOnlyTests : IDisposable
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    // BIP-84's own test vector (mainnet): the account key of the phrase above and its first addresses.
    private const string Bip84Zpub = "zpub6rFR7y4Q2AijBEqTUquhVz398htDFrtymD9xYYfG1m4wAcvPhXNfE3EfH1r1ADqtfSdVCToUG868RvUUkgDKf31mGDtKsAYz2oz2AGutZYs";

    // BIP-44 account 0 of the same phrase, as wallets and Ian Coleman's tool show it.
    private const string Bip44Xpub = "xpub6BosfCnifzxcFwrSzQiqu2DBVTshkCXacvNsWGYJVVhhawA7d4R5WSWGFNbi8Aw6ZRc1brxMyWMzG3DSSSSoekkudhUd9yLb6qx39T9nMdj";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-watch-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static string? NoAddresses(string _) => null;

    // ---- the published vectors -----------------------------------------------------------------------

    [Fact]
    public void A_zpub_gives_bip84s_own_addresses()
    {
        var spec = WatchSpecParser.Parse(Bip84Zpub, NoAddresses);
        Assert.True(spec.Ok);
        var source = new XpubAddressSource(spec.Spec.Xpubs.Single());

        Assert.Equal("bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu", source.At(0, 0, UtxoScriptKind.Default).Address);
        Assert.Equal("bc1qnjg0jd8228aq7egyzacy8cys3knf9xvrerkf9g", source.At(0, 1, UtxoScriptKind.Default).Address);
        Assert.Equal("bc1q8c6fshw2dlwun7ekn9qwf37cu2rn755upcp6el", source.At(1, 0, UtxoScriptKind.Default).Address);
    }

    [Fact]
    public void A_legacy_descriptor_gives_bip44s_first_address()
    {
        var spec = WatchSpecParser.Parse($"pkh({Bip44Xpub})", NoAddresses);
        var source = new XpubAddressSource(spec.Spec.Xpubs.Single());

        Assert.Equal(WatchScript.Legacy, spec.Spec.Xpubs[0].Script);
        Assert.Equal("1LqBGSKuX5yYUonjxT5qGfpUsXKYYWeabA", source.At(0, 0, UtxoScriptKind.Default).Address);
    }

    [Fact]
    public void The_descriptor_checksum_is_bip380s()
    {
        Assert.Equal("89f8spxm", WatchSpecParser.Checksum("raw(deadbeef)"));
    }

    // ---- a watched account is the phrase's account -----------------------------------------------------

    public static TheoryData<ChainId, UtxoScriptKind, WatchScript> Accounts() => new()
    {
        { ChainId.Btc, UtxoScriptKind.Default, WatchScript.Segwit },
        { ChainId.Btc, UtxoScriptKind.Taproot, WatchScript.Taproot },
        { ChainId.Ltc, UtxoScriptKind.Default, WatchScript.Segwit },
        { ChainId.Doge, UtxoScriptKind.Default, WatchScript.Legacy },
        { ChainId.Bch, UtxoScriptKind.Default, WatchScript.Legacy },
    };

    [Theory]
    [MemberData(nameof(Accounts))]
    public void Watching_an_accounts_key_finds_the_addresses_its_phrase_does(ChainId chain, UtxoScriptKind kind, WatchScript script)
    {
        var deriver = new HdAddressDeriver();
        var key = deriver.DeriveAccountExtPubKey(Phrase, chain, kind);
        var watched = new XpubAddressSource(new WatchXpub(chain, key.ToString(Network.Main), script));
        var seed = new SeedAddressSource(deriver, Phrase, chain);

        for (uint change = 0; change <= 1; change++)
        for (uint index = 0; index < 4; index++)
            Assert.Equal(seed.At(change, index, kind).Address, watched.At(change, index, UtxoScriptKind.Default).Address);
    }

    // ---- what is refused, and why ------------------------------------------------------------------------

    [Fact]
    public void A_bare_xpub_is_refused_rather_than_guessed()
    {
        var result = WatchSpecParser.Parse(Bip44Xpub, NoAddresses);

        Assert.False(result.Ok);
        Assert.Equal("watch.err.bareXpub", result.Issues.Single().Code);
    }

    [Fact]
    public void An_xpub_on_a_chain_with_one_address_type_needs_only_the_chain()
    {
        var result = WatchSpecParser.Parse("DOGE:" + Bip44Xpub + "\nBCH:" + Bip44Xpub, NoAddresses);

        Assert.True(result.Ok);
        Assert.Equal([ChainId.Doge, ChainId.Bch], result.Spec.Xpubs.Select(x => x.Chain));
    }

    [Fact]
    public void A_descriptor_keeps_its_key_origin_for_signing_elsewhere()
    {
        var body = $"wpkh([73C5DA0A/84h/0h/0h]{Bip84AsXpub()}/<0;1>/*)";
        var result = WatchSpecParser.Parse(body + "#" + WatchSpecParser.Checksum(body), NoAddresses);

        var x = result.Spec.Xpubs.Single();
        Assert.Equal(("73c5da0a", "84'/0'/0'", WatchScript.Segwit), (x.Fingerprint, x.AccountPath, x.Script));
        Assert.Equal("bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu", new XpubAddressSource(x).At(0, 0, UtxoScriptKind.Default).Address);
    }

    [Fact]
    public void A_descriptor_with_a_wrong_checksum_is_refused()
    {
        var result = WatchSpecParser.Parse($"wpkh({Bip84AsXpub()})#qqqqqqqq", NoAddresses);

        Assert.Equal("watch.err.checksum", result.Issues.Single().Code);
    }

    [Theory]
    [InlineData("tpubDC8msFGeGuwnKG9Upg7DM2b4DaRqg3CUZa5g8v2SRQ6K4NSkxUgd7HsL2XVWbVm39yBA4LAxysQAm397zwQSQoQgewGiYZqrA9DsP4zbQ1M", "watch.err.testnet")]
    [InlineData("wsh(multi(2,xpubA,xpubB))", "watch.err.descriptor")]
    [InlineData("tr(xpubA,{pk(xpubB)})", "watch.err.descriptor")]
    [InlineData("zpub6rFR7y4Q2AijBEqTUquhVz398htDFrtymD9xYYfG1m4wAcvPhXNfE3EfH1r1ADqtfSdVCToUG868RvUUkgDKf31mGDtKsAYz2oz2AGutZYt", "watch.err.key")]
    public void Keys_and_scripts_this_wallet_cannot_watch_are_named(string line, string code)
    {
        Assert.Equal(code, WatchSpecParser.Parse(line, NoAddresses).Issues.Single().Code);
    }

    [Fact]
    public void Doge_has_no_segwit_to_watch()
    {
        Assert.Equal("watch.err.script", WatchSpecParser.Parse($"DOGE:{Bip84Zpub}", NoAddresses).Issues.Single().Code);
    }

    [Fact]
    public void Addresses_are_named_by_shape_or_by_the_network_written_in_front()
    {
        static string? Detect(string a) => a.StartsWith("0x") ? "ETH" : a.StartsWith('T') ? "TRX" : null;

        var result = WatchSpecParser.Parse(
            "0x000000000000000000000000000000000000dEaD\n# a comment\n\nSOL:7Vbmv1jt4vyuqBZcpYPpnVhrqVe5e6ZPb6JxDcffRHUM\nnot-an-address",
            Detect);

        Assert.Equal([("ETH", "0x000000000000000000000000000000000000dEaD"), ("SOL", "7Vbmv1jt4vyuqBZcpYPpnVhrqVe5e6ZPb6JxDcffRHUM")],
            result.Spec.Addresses.Select(a => (a.Symbol, a.Address)));
        Assert.Equal((5, "watch.err.unknown"), (result.Issues.Single().Line, result.Issues.Single().Code));
    }

    // ---- the scan over a watched account ------------------------------------------------------------------

    private sealed class FakeExplorer(Dictionary<string, long> funded) : IUtxoExplorer
    {
        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct) =>
            Task.FromResult(new AddressActivity(funded.ContainsKey(address), funded.ContainsKey(address) ? 1 : 0));

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExplorerUtxo>>(funded.TryGetValue(address, out var v)
                ? [new ExplorerUtxo(new string('a', 64), 0, v, true)]
                : []);
    }

    [Fact]
    public async Task A_watched_account_is_scanned_past_its_gap_like_a_phrase()
    {
        var source = new XpubAddressSource(WatchSpecParser.Parse(Bip84Zpub, NoAddresses).Spec.Xpubs.Single());
        var receive7 = source.At(0, 7, UtxoScriptKind.Default).Address;
        var change2 = source.At(1, 2, UtxoScriptKind.Default).Address;
        var explorer = new FakeExplorer(new() { [receive7] = 50_000, [change2] = 20_000 });

        var scan = await new UtxoAccountScanner().ScanAsync(source, explorer, UtxoScanFloors.None);

        Assert.Equal(70_000, scan.ConfirmedSat);
        Assert.False(scan.Partial);
        Assert.Equal(7u, scan.HighestUsedExternalIndex);
        Assert.Equal(2u, scan.HighestUsedInternalIndex);
    }

    // ---- the sealed description ------------------------------------------------------------------------

    [Fact]
    public void The_description_is_sealed_and_only_its_own_secret_opens_it()
    {
        Directory.CreateDirectory(_dir);
        var vault = Path.Combine(_dir, "vault.json");
        var secret = WatchWalletFile.NewSecret();
        var spec = WatchSpecParser.Parse(Bip84Zpub + "\nETH:0x000000000000000000000000000000000000dEaD", NoAddresses).Spec;

        WatchWalletFile.Save(vault, secret, spec);
        var onDisk = File.ReadAllText(WatchWalletFile.PathFor(vault));

        Assert.DoesNotContain("zpub", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("dEaD", onDisk, StringComparison.Ordinal);
        Assert.Equal(spec.ToJson(), WatchWalletFile.Load(vault, secret).ToJson());
        Assert.ThrowsAny<Exception>(() => WatchWalletFile.Load(vault, WatchWalletFile.NewSecret()));
        Assert.True(WatchWalletFile.IsWatchSecret(secret));
        Assert.False(WatchWalletFile.IsWatchSecret(Phrase));
    }

    /// <summary>BIP-84's zpub with the standard version bytes, as Bitcoin Core writes it in a descriptor.</summary>
    private static string Bip84AsXpub() => WatchSpecParser.ToExtPubKey(Bip84Zpub).ToString(Network.Main);
}
