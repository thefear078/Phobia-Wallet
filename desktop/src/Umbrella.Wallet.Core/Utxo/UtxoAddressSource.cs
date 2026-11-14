using NBitcoin;
using NBitcoin.Altcoins;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Watch;

namespace Umbrella.Wallet.Core.Utxo;

/// <summary>
/// Where a UTXO scan gets its addresses: the leaf at (change, index) on one branch. A wallet with a
/// phrase derives them from it; a watch-only wallet (roadmap H.5) from an account's extended public
/// key. The scan itself is the same walk either way, so a watched account is found exactly as fully as
/// one with its phrase on the machine.
/// </summary>
public interface IUtxoAddressSource
{
    ChainId Chain { get; }

    /// <summary>True when the BIP86 Taproot branches are walked beside the default ones.</summary>
    bool ScansTaproot { get; }

    (UtxoDerivationPath Path, string Address) At(uint change, uint index, UtxoScriptKind kind);
}

/// <summary>The addresses of a wallet with its phrase: what every scan did before watch-only wallets.</summary>
public sealed class SeedAddressSource(HdAddressDeriver deriver, string mnemonic, ChainId chain) : IUtxoAddressSource
{
    public ChainId Chain => chain;

    public bool ScansTaproot => UtxoAccountScanner.ScansTaproot(chain);

    public (UtxoDerivationPath Path, string Address) At(uint change, uint index, UtxoScriptKind kind)
    {
        var account = deriver.DeriveBitcoinLikeAt(mnemonic, chain, change, index, kind: kind);
        return (account.Path, account.Address);
    }
}

/// <summary>
/// The addresses of one watched account, from its extended public key: <c>key/change/index</c>, in the
/// address type the description named. Nothing here can sign — an extended public key cannot.
/// </summary>
public sealed class XpubAddressSource : IUtxoAddressSource
{
    private readonly ExtPubKey _account;
    private readonly ExtPubKey[] _branches;
    private readonly Network _network;
    private readonly ScriptPubKeyType _type;

    public XpubAddressSource(WatchXpub watched)
    {
        Watched = watched;
        Chain = watched.Chain;
        _account = WatchSpecParser.ToExtPubKey(watched.ExtPubKey);
        _branches = [_account.Derive(0), _account.Derive(1)];
        _network = NetworkFor(watched.Chain);
        _type = TypeFor(watched.Script);
    }

    public WatchXpub Watched { get; }

    public ChainId Chain { get; }

    /// <summary>A watched account is one address type: a Taproot account is its own description.</summary>
    public bool ScansTaproot => false;

    public (UtxoDerivationPath Path, string Address) At(uint change, uint index, UtxoScriptKind kind)
    {
        if (kind != UtxoScriptKind.Default || change > 1) throw new ArgumentOutOfRangeException(nameof(kind));
        var key = _branches[change].Derive(index).PubKey;
        return (new UtxoDerivationPath(Chain, change, index), key.GetAddress(_type, _network).ToString());
    }

    /// <summary>The public key at (change, index), for a PSBT that names it.</summary>
    public PubKey PubKeyAt(uint change, uint index) => _branches[change].Derive(index).PubKey;

    /// <summary>The scriptPubKey at (change, index).</summary>
    public Script ScriptAt(uint change, uint index) => PubKeyAt(change, index).GetAddress(_type, _network).ScriptPubKey;

    public static Network NetworkFor(ChainId chain) => chain switch
    {
        ChainId.Btc => Network.Main,
        ChainId.Ltc => Litecoin.Instance.Mainnet,
        ChainId.Doge => Dogecoin.Instance.Mainnet,
        ChainId.Bch => BCash.Instance.Mainnet,
        _ => throw new UnsupportedChainException(chain),
    };

    public static ScriptPubKeyType TypeFor(WatchScript script) => script switch
    {
        WatchScript.Legacy => ScriptPubKeyType.Legacy,
        WatchScript.NestedSegwit => ScriptPubKeyType.SegwitP2SH,
        WatchScript.Segwit => ScriptPubKeyType.Segwit,
        WatchScript.Taproot => ScriptPubKeyType.TaprootBIP86,
        _ => throw new ArgumentOutOfRangeException(nameof(script)),
    };
}
