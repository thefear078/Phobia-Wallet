using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Umbrella.Wallet.App;

/// <summary>
/// A sender's error, in the wallet's language.
///
/// The senders say what went wrong in English, and say it precisely — the server they asked, the figure
/// a contract reported, the transaction id to look up. That stays the record (logs, tests, support), and
/// the senders are not touched: this is the one place it is turned into what the user reads.
///
/// The messages people meet — a wrong address, their own address, the amount, too little money, a
/// network that did not answer, an account not yet activated — are translated whole, the values they
/// carry kept ("senderr.*": the English entry IS the pattern a message is matched against). The rare,
/// technical rest get a sentence in the wallet's language saying what kind of trouble it is
/// ("senderr.cat.*"), with the original line under it. English is shown as it is.
/// </summary>
public static class SendErrorText
{
    /// <summary>The messages translated whole; the English text of each is matched with {0}… as wildcards.</summary>
    public static readonly string[] ExactKeys =
    [
        "senderr.amountPositive",
        "senderr.amountAboveZero",
        "senderr.amountDecimals",
        "senderr.amountDecimalsCoin",
        "senderr.amountNear",
        "senderr.adaMin",
        "senderr.ownAddress",
        "senderr.ownNear",
        "senderr.ownAddressFee",
        "senderr.invalidSolana",
        "senderr.invalidTon",
        "senderr.invalidCardano",
        "senderr.invalidTron",
        "senderr.invalidXrp",
        "senderr.invalidStellar",
        "senderr.invalidCosmos",
        "senderr.invalidNear",
        "senderr.invalidPolkadot",
        "senderr.invalidNano",
        "senderr.invalidDecred",
        "senderr.invalidEvm",
        "senderr.invalidGeneric",
        "senderr.xAddress",
        "senderr.muxed",
        "senderr.tokenAccount",
        "senderr.notWallet",
        "senderr.programAddress",
        "senderr.tokenContractEvm",
        "senderr.tokenContractTron",
        "senderr.unreachableRpcs",
        "senderr.tonUnreachable",
        "senderr.cardanoUnreachable",
        "senderr.noAnswerCosmos",
        "senderr.noAnswerNear",
        "senderr.noAnswerPolkadot",
        "senderr.noAnswerSolana",
        "senderr.noAnswerXrp",
        "senderr.solanaNoBlockhash",
        "senderr.zcashRateLimited",
        "senderr.tronNoTx",
        "senderr.nanoNoAnswer",
        "senderr.nanoCannotRead",
        "senderr.nanoUnreadable",
        "senderr.nanoNoAnswerDetail",
        "senderr.xlmUnfunded",
        "senderr.xrpInactive",
        "senderr.nearNoAccount",
        "senderr.cosmosEmpty",
        "senderr.dotNone",
        "senderr.adaNone",
        "senderr.nearCreate",
        "senderr.xrpNoSend",
        "senderr.xrpAuth",
        "senderr.insufficientToken",
        "senderr.insufficientSymbol",
        "senderr.notEnoughXno",
        "senderr.decimalsUnknown",
        "senderr.zecUpgrade",
        "senderr.unconfirmedCoin",
        "senderr.cosmosCongested",
        "senderr.zecT1",
        "senderr.dcrDs",
        "senderr.nodeRefused",
        "senderr.blockhashExpired",
        "senderr.unclearLanded",
        "senderr.differentId",
        "senderr.keyMismatch",
        "senderr.readBalance",
        "senderr.readSolBalance",
        "senderr.readAccount",
        "senderr.readAccountKey",
        "senderr.readNonce",
        "senderr.readFee",
        "senderr.readGasPrice",
        "senderr.readReserve",
        "senderr.readExistential",
        "senderr.readRent",
        "senderr.readDestRent",
        "senderr.checkDestination",
        "senderr.checkDestinationExists",
        "senderr.checkRecipientAccount",
        "senderr.findOwnAccount",
        "senderr.readCoins",
        "senderr.readCoinSource",
        "senderr.readDecred",
        "senderr.hostAccountData",
        "senderr.hostDestination",
        "senderr.btcUnclear",
        "senderr.adaUnclear",
        "senderr.atomUnclear",
        "senderr.dcrUnclear",
        "senderr.ethDifferent",
        "senderr.ethUnclear",
        "senderr.xmrUnclear",
        "senderr.nearUnclear",
        "senderr.dotUnclear",
        "senderr.solUnclear",
        "senderr.xlmDifferent",
        "senderr.xlmUnclear",
        "senderr.tonUnclear",
        "senderr.trxUnclear",
        "senderr.xrpUnclear",
        "senderr.zecUnclear"
    ];

    /// <summary>What kind of trouble a message is, for the ones not translated whole; first match wins.</summary>
    private static readonly (string Category, Regex Pattern)[] Categories =
    [
        ("unclear", Rx(@"on an explorer before sending again|Could not tell whether|Check the account before sending again")),
        ("own", Rx(@"this wallet's own")),
        ("address", Rx(@"not a valid|\bis not a (?:[\w-]+ )*(?:address|account)\b|not supported yet|address of \d+ characters|not a wallet|belongs to a program|token account|jetton-wallet address")),
        ("funds", Rx(@"Insufficient|Not enough|\bneed (?:about )?[\d.]+|No spendable|holds no|nothing to send")),
        ("amount", Rx(@"\bamount\b|decimal places|minimum")),
        ("refused", Rx(@"refused|rejected|refusing")),
        ("network", Rx(@"unreachable|answered|did not answer|did not say|did not return|no answer|Could not (?:read|check|get|fetch|find|reach)|rate-limit|explorer|node|server")),
    ];

    private static readonly Regex NothingSent = Rx(@"\bnothing (?:was )?(?:signed or )?(?:sent|signed)\b");

    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    private static Regex Rx(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    /// <summary>The English entry of a key as a pattern: its text literally, each {n} any run of characters.</summary>
    public static Regex PatternFor(string key) => Patterns.GetOrAdd(key, k =>
    {
        var english = Loc.Instance.InEnglish(k);
        var parts = Regex.Split(english, @"(\{\d\})");
        var body = string.Concat(parts.Select(part =>
            Regex.IsMatch(part, @"^\{\d\}$") ? $"(?<p{part[1]}>.*?)" : Regex.Escape(part)));
        return new Regex("^" + body + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    });

    /// <summary>The category a message falls in ("failed" when nothing more specific fits).</summary>
    public static string CategoryOf(string english)
    {
        foreach (var (category, pattern) in Categories)
            if (pattern.IsMatch(english)) return category;
        return "failed";
    }

    /// <summary>The key of the whole translation of a message, or null when it has none.</summary>
    public static string? ExactKeyFor(string english)
    {
        var text = english.Trim();
        return ExactKeys.FirstOrDefault(k => PatternFor(k).IsMatch(text));
    }

    /// <summary>
    /// A message translated whole when it can be, and otherwise as the sender wrote it — for a place that
    /// already says, in the wallet's language, what kind of trouble it is (the receipt of an unclear send).
    /// </summary>
    public static string LocalizeWhole(string? english)
    {
        if (string.IsNullOrWhiteSpace(english) || Loc.Instance.CurrentCode == "en") return english ?? string.Empty;
        var text = english.Trim();
        foreach (var key in ExactKeys)
        {
            var match = PatternFor(key).Match(text);
            if (!match.Success) continue;
            var values = Enumerable.Range(0, 3)
                .Select(i => (object)(match.Groups["p" + i].Success ? match.Groups["p" + i].Value : string.Empty)).ToArray();
            return string.Format(CultureInfo.InvariantCulture, Loc.Instance[key], values);
        }

        return english;
    }

    /// <summary>What to show for a sender's error in the wallet's current language.</summary>
    public static string Localize(string? english)
    {
        if (string.IsNullOrWhiteSpace(english)) return english ?? string.Empty;
        if (Loc.Instance.CurrentCode == "en") return english;

        var text = english.Trim();
        foreach (var key in ExactKeys)
        {
            var match = PatternFor(key).Match(text);
            if (!match.Success) continue;
            var values = Enumerable.Range(0, 3)
                .Select(i => (object)(match.Groups["p" + i].Success ? match.Groups["p" + i].Value : string.Empty)).ToArray();
            return string.Format(CultureInfo.InvariantCulture, Loc.Instance[key], values);
        }

        var category = CategoryOf(text);
        var headline = Loc.Instance["senderr.cat." + category];
        if (category != "unclear" && NothingSent.IsMatch(text)) headline += " " + Loc.Instance["senderr.cat.nothingSent"];
        return headline + "\n" + text;
    }
}
