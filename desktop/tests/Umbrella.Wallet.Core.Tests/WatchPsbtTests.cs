using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Psbt;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Core.Watch;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The unsigned PSBT of a watch-only wallet (roadmap H.5), checked the way it will be used: a signer
/// that holds only the account key — as a hardware wallet does — finds its coins by the fingerprint
/// and paths the PSBT names, signs them, and the result finalises into a valid transaction.
/// </summary>
public sealed class WatchPsbtTests
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Destination = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    private static (ExtKey Master, HDFingerprint Fingerprint) Signer()
    {
        var master = new Mnemonic(Phrase).DeriveExtKey();
        return (master, master.GetPublicKey().GetHDFingerPrint());
    }

    private static XpubAddressSource Watched(string accountPath, WatchScript script, bool withOrigin = true)
    {
        var (master, fp) = Signer();
        var xpub = master.Derive(new KeyPath(accountPath)).Neuter().ToString(Network.Main);
        return new XpubAddressSource(new WatchXpub(ChainId.Btc, xpub, script,
            withOrigin ? fp.ToString() : null, withOrigin ? accountPath : null));
    }

    private static List<OwnedUtxo> Coins(XpubAddressSource source) =>
    [
        new(new UtxoDerivationPath(ChainId.Btc, 0, 3), source.At(0, 3, UtxoScriptKind.Default).Address, new string('1', 64), 0, 60_000, true),
        new(new UtxoDerivationPath(ChainId.Btc, 1, 1), source.At(1, 1, UtxoScriptKind.Default).Address, new string('2', 64), 1, 30_000, true),
    ];

    [Theory]
    [InlineData("84'/0'/0'", WatchScript.Segwit, ScriptPubKeyType.Segwit)]
    [InlineData("86'/0'/0'", WatchScript.Taproot, ScriptPubKeyType.TaprootBIP86)]
    [InlineData("49'/0'/0'", WatchScript.NestedSegwit, ScriptPubKeyType.SegwitP2SH)]
    public void The_key_holder_recognises_signs_and_finalises_it(string accountPath, WatchScript script, ScriptPubKeyType type)
    {
        var source = Watched(accountPath, script);
        var coins = Coins(source);

        var result = WatchPsbt.Build(source, coins, Destination, 70_000, 5, changeIndex: 2);
        Assert.Null(result.Error);
        var psbt = result.Psbt!;

        // The change goes back to the account's own change branch, at the index asked for.
        Assert.Contains(psbt.Outputs, o => o.ScriptPubKey == source.ScriptAt(1, 2) && o.Value.Satoshi == result.ChangeSat);
        Assert.Equal(90_000 - 70_000 - result.ChangeSat, result.FeeSat);

        // The signer knows only its account key and where it sits; the PSBT tells it which coins are its.
        var (master, fp) = Signer();
        psbt.SignAll(type, master.Derive(new KeyPath(accountPath)), new RootedKeyPath(fp, new KeyPath(accountPath)));
        Assert.True(psbt.TryFinalize(out var errors), string.Join("; ", errors ?? []));

        var tx = psbt.ExtractTransaction();
        var verifier = Network.Main.CreateTransactionBuilder();
        verifier.AddCoins(psbt.Inputs.Select(i => i.GetCoin()!));
        Assert.True(verifier.Verify(tx, out var verifyErrors), string.Join("; ", verifyErrors.Select(e => e.ToString())));
    }

    [Fact]
    public void Without_the_key_origin_the_signer_cannot_find_its_coins()
    {
        // Why the screen asks for [fingerprint/path]: the PSBT is correct, but nothing tells a hardware
        // wallet which of its keys these coins belong to.
        var source = Watched("84'/0'/0'", WatchScript.Segwit, withOrigin: false);
        var psbt = WatchPsbt.Build(source, Coins(source), Destination, 70_000, 5, 2).Psbt!;

        var (master, fp) = Signer();
        psbt.SignAll(ScriptPubKeyType.Segwit, master.Derive(new KeyPath("84'/0'/0'")), new RootedKeyPath(fp, new KeyPath("84'/0'/0'")));

        Assert.False(psbt.TryFinalize(out _));
        Assert.Null(WatchPsbt.Origin(source.Watched));
    }

    [Theory]
    [InlineData("not-an-address", 70_000, 5, "address")]
    [InlineData("ltc1qg82tthhymvqkhpd7vmsss0ef4fqmvqn5p8p3dy", 70_000, 5, "address")]
    [InlineData(Destination, 0, 5, "amount")]
    [InlineData(Destination, 95_000, 5, "Not enough")]
    [InlineData(Destination, 70_000, 0, "fee")]
    public void What_cannot_be_paid_is_refused_with_a_reason(string to, long amount, double rate, string reason)
    {
        var source = Watched("84'/0'/0'", WatchScript.Segwit);

        var result = WatchPsbt.Build(source, Coins(source), to, amount, rate, 2);

        Assert.Null(result.Psbt);
        Assert.Contains(reason, result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
