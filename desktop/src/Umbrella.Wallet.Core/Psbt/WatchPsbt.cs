using NBitcoin;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Core.Watch;

namespace Umbrella.Wallet.Core.Psbt;

/// <summary>
/// A payment from a watched account as an UNSIGNED PSBT (roadmap H.5): built from the coins the
/// wallet's own scan found, with change to the account's next unused change address, for signing in
/// the wallet that holds the keys.
///
/// Every input and the change output are named by master fingerprint and full BIP32 path when the
/// account was described with its key origin (<c>[fingerprint/path]xpub…</c>) — that is what a hardware
/// wallet or Sparrow needs to recognise them as its own. Without the origin the PSBT is still correct,
/// and the screen says the signer may not recognise it.
/// </summary>
public static class WatchPsbt
{
    public sealed record Result(PSBT? Psbt, long FeeSat, long ChangeSat, string? Error);

    public static Result Build(
        XpubAddressSource source,
        IReadOnlyList<OwnedUtxo> coins,
        string destination,
        long amountSat,
        double feeRateSatPerVByte,
        uint changeIndex)
    {
        try
        {
            var network = XpubAddressSource.NetworkFor(source.Chain);
            BitcoinAddress to;
            try { to = BitcoinAddress.Create(destination.Trim(), network); }
            catch (FormatException) { return new Result(null, 0, 0, "That is not an address on this network."); }

            if (amountSat <= 0) return new Result(null, 0, 0, "The amount must be more than zero.");
            if (feeRateSatPerVByte <= 0) return new Result(null, 0, 0, "The fee rate must be more than zero.");
            if (coins.Count == 0) return new Result(null, 0, 0, "This account has no coins to spend yet.");

            var spendable = coins.Select(u =>
            {
                var coin = new Coin(
                    new OutPoint(uint256.Parse(u.TxId), (uint)u.Vout),
                    new TxOut(Money.Satoshis(u.ValueSat), source.ScriptAt(u.Path.Change, u.Path.Index)));
                // P2SH-wrapped SegWit spends need the P2WPKH script it wraps, or no signer can sign it.
                return source.Watched.Script == WatchScript.NestedSegwit
                    ? coin.ToScriptCoin(source.PubKeyAt(u.Path.Change, u.Path.Index).GetScriptPubKey(ScriptPubKeyType.Segwit))
                    : (ICoin)coin;
            }).ToList();
            var changeScript = source.ScriptAt(1, changeIndex);

            var builder = network.CreateTransactionBuilder();
            builder.AddCoins(spendable);
            builder.Send(to, Money.Satoshis(amountSat));
            builder.SetChange(changeScript);
            builder.SendEstimatedFees(new FeeRate(Money.Satoshis((decimal)(feeRateSatPerVByte * 1000))));

            PSBT psbt;
            try { psbt = builder.BuildPSBT(sign: false); }
            catch (NotEnoughFundsException) { return new Result(null, 0, 0, "Not enough in this account for the amount and its fee."); }

            psbt.TryGetFee(out var fee);
            var changeSat = psbt.Outputs.Where(o => o.ScriptPubKey == changeScript).Sum(o => o.Value.Satoshi);

            if (Origin(source.Watched) is { } origin)
            {
                foreach (var input in psbt.Inputs)
                {
                    var used = coins.First(u => uint256.Parse(u.TxId) == input.PrevOut.Hash && (uint)u.Vout == input.PrevOut.N);
                    Name(input, source, origin, used.Path.Change, used.Path.Index);
                }

                foreach (var output in psbt.Outputs.Where(o => o.ScriptPubKey == changeScript))
                    Name(output, source, origin, 1, changeIndex);
            }

            return new Result(psbt, fee?.Satoshi ?? 0, changeSat, null);
        }
        catch (Exception ex)
        {
            return new Result(null, 0, 0, $"The PSBT could not be made: {ex.Message}");
        }
    }

    /// <summary>The master fingerprint and account path the description named, when it named them.</summary>
    public static (HDFingerprint Fingerprint, KeyPath Account)? Origin(WatchXpub watched)
    {
        if (watched.Fingerprint is null || watched.AccountPath is null) return null;
        try
        {
            return (HDFingerprint.Parse(watched.Fingerprint), KeyPath.Parse(watched.AccountPath));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void Name(PSBTCoin coin, XpubAddressSource source, (HDFingerprint Fingerprint, KeyPath Account) origin, uint change, uint index)
    {
        var pubKey = source.PubKeyAt(change, index);
        var rooted = new RootedKeyPath(origin.Fingerprint, origin.Account.Derive(change).Derive(index));
        if (source.Watched.Script == WatchScript.Taproot)
        {
            // BIP-371: a key-path coin is named by its x-only internal key, under the Taproot field.
            var internalKey = pubKey.TaprootInternalKey;
            coin.TaprootInternalKey = internalKey;
            coin.HDTaprootKeyPaths[new TaprootPubKey(internalKey.ToBytes())] = new TaprootKeyPath(rooted);
        }
        else
        {
            coin.AddKeyPath(pubKey, rooted);
        }
    }
}
