using System.Text;
using System.Text.RegularExpressions;
using NBitcoin;
using NBitcoin.DataEncoders;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Watch;

/// <summary>A line the parser could not use, by its 1-based number, with a code the screen translates.</summary>
public sealed record WatchParseIssue(int Line, string Code, string Text);

/// <summary>What a pasted watch-only description became.</summary>
public sealed record WatchParseResult(WatchSpec Spec, IReadOnlyList<WatchParseIssue> Issues)
{
    public bool Ok => Issues.Count == 0 && !Spec.IsEmpty;
}

/// <summary>
/// Reads what people paste to watch a wallet (roadmap H.5), one item a line:
/// <list type="bullet">
/// <item>an output descriptor — <c>wpkh([d34db33f/84h/0h/0h]xpub…/&lt;0;1&gt;/*)</c>, <c>sh(wpkh(…))</c>,
/// <c>pkh(…)</c>, <c>tr(…)</c>, checksum checked when present;</item>
/// <item>an extended public key whose prefix says its address type (SLIP-132: <c>zpub</c>, <c>ypub</c>,
/// <c>Ltub</c>, <c>Mtub</c>, <c>dgub</c>);</item>
/// <item>an address, its network recognised from its shape;</item>
/// <item>any of these with the network in front — <c>LTC:zpub…</c>, <c>BCH:xpub…</c>, <c>SOL:…</c> —
/// where the shape alone cannot say.</item>
/// </list>
/// A bare <c>xpub</c> is refused rather than guessed: it does not say which addresses it makes, and
/// watching the wrong kind shows an empty wallet that is not empty.
/// </summary>
public static class WatchSpecParser
{
    private const uint XpubVersion = 0x0488B21E;
    private const uint YpubVersion = 0x049D7CB2;
    private const uint ZpubVersion = 0x04B24746;
    private const uint LtubVersion = 0x019DA462;
    private const uint MtubVersion = 0x01B26EF6;
    private const uint DgubVersion = 0x02FACAFD;
    private const uint MultisigYpub = 0x0295B43F;
    private const uint MultisigZpub = 0x02AA7ED3;
    private const uint TpubVersion = 0x043587CF;
    private const uint UpubVersion = 0x044A5262;
    private const uint VpubVersion = 0x045F1CF6;

    private static readonly string[] HdChains = ["BTC", "LTC", "DOGE", "BCH"];

    /// <param name="text">The pasted lines.</param>
    /// <param name="detectSymbol">Names an address's network from its shape, or null when the shape is
    /// ambiguous or not an address at all.</param>
    public static WatchParseResult Parse(string? text, Func<string, string?> detectSymbol)
    {
        var xpubs = new List<WatchXpub>();
        var addresses = new List<WatchAddressEntry>();
        var issues = new List<WatchParseIssue>();
        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var number = i + 1;

            // "LTC:zpub…" / "SOL:addr" — a network named in front. A descriptor never has a colon
            // before its first parenthesis; a bitcoin: URI is not a watch item.
            string? named = null;
            var colon = line.IndexOf(':');
            var paren = line.IndexOf('(');
            if (colon is > 1 and <= 6 && (paren < 0 || colon < paren))
            {
                named = line[..colon].Trim().ToUpperInvariant();
                line = line[(colon + 1)..].Trim();
            }

            if (line.Contains('('))
            {
                var parsed = ParseDescriptor(line, named);
                if (parsed.Issue is { } code) issues.Add(new WatchParseIssue(number, code, lines[i].Trim()));
                else Add(xpubs, parsed.Xpub!);
                continue;
            }

            if (LooksLikeExtendedKey(line))
            {
                var parsed = ParseBareKey(line, named);
                if (parsed.Issue is { } code) issues.Add(new WatchParseIssue(number, code, lines[i].Trim()));
                else Add(xpubs, parsed.Xpub!);
                continue;
            }

            var symbol = named ?? detectSymbol(line);
            if (symbol is null)
            {
                issues.Add(new WatchParseIssue(number, "watch.err.unknown", lines[i].Trim()));
                continue;
            }

            if (!addresses.Any(a => a.Symbol == symbol && a.Address.Equals(line, StringComparison.OrdinalIgnoreCase)))
                addresses.Add(new WatchAddressEntry(symbol, line));
        }

        return new WatchParseResult(new WatchSpec(xpubs, addresses), issues);
    }

    private static void Add(List<WatchXpub> list, WatchXpub x)
    {
        if (!list.Any(o => o.Chain == x.Chain && o.Script == x.Script && o.ExtPubKey == x.ExtPubKey)) list.Add(x);
    }

    private static bool LooksLikeExtendedKey(string s) =>
        s.Length is >= 100 and <= 120 && Regex.IsMatch(s, "^[1-9A-HJ-NP-Za-km-z]+$") &&
        (s.StartsWith("xpub") || s.StartsWith("ypub") || s.StartsWith("zpub") || s.StartsWith("Ltub") ||
         s.StartsWith("Mtub") || s.StartsWith("dgub") || s.StartsWith("tpub") || s.StartsWith("upub") ||
         s.StartsWith("vpub") || s.StartsWith("Ypub") || s.StartsWith("Zpub"));

    // ---- extended keys ------------------------------------------------------------------------------

    /// <summary>The 78-byte payload of a base58check extended key, or null when it does not decode.</summary>
    public static byte[]? DecodeExtended(string key)
    {
        try
        {
            var data = Encoders.Base58Check.DecodeData(key);
            return data.Length == 78 ? data : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static uint VersionOf(byte[] data) =>
        (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

    /// <summary>The key itself, whatever its version bytes said about the address type.</summary>
    public static ExtPubKey ToExtPubKey(string key)
    {
        var data = DecodeExtended(key) ?? throw new FormatException("Not an extended public key.");
        return new ExtPubKey(data[4..]);
    }

    private static (WatchXpub? Xpub, string? Issue) ParseBareKey(string key, string? named)
    {
        var data = DecodeExtended(key);
        if (data is null) return (null, "watch.err.key");
        var version = VersionOf(data);
        if (version is TpubVersion or UpubVersion or VpubVersion) return (null, "watch.err.testnet");
        if (version is MultisigYpub or MultisigZpub) return (null, "watch.err.multisig");

        (string Chain, WatchScript Script)? implied = version switch
        {
            YpubVersion => ("BTC", WatchScript.NestedSegwit),
            ZpubVersion => ("BTC", WatchScript.Segwit),
            LtubVersion => ("LTC", WatchScript.Legacy),
            MtubVersion => ("LTC", WatchScript.NestedSegwit),
            DgubVersion => ("DOGE", WatchScript.Legacy),
            _ => null,
        };

        if (version == XpubVersion)
        {
            // An xpub says nothing about its addresses — except on the chains that have only one kind.
            if (named is "DOGE" or "BCH") implied = (named, WatchScript.Legacy);
            else return (null, "watch.err.bareXpub");
        }

        if (implied is null) return (null, "watch.err.key");
        var chain = named ?? implied.Value.Chain;
        // A named network keeps the key's address type: "LTC:zpub…" is Litecoin native SegWit.
        return Build(chain, key, implied.Value.Script, null, null);
    }

    private static (WatchXpub? Xpub, string? Issue) Build(string chain, string key, WatchScript script, string? fp, string? path)
    {
        if (!HdChains.Contains(chain)) return (null, "watch.err.chain");
        var supported = chain switch
        {
            "BTC" => true,
            "LTC" => script != WatchScript.Taproot,
            _ => script == WatchScript.Legacy,   // DOGE and BCH have no SegWit
        };
        if (!supported) return (null, "watch.err.script");
        var id = chain switch { "BTC" => ChainId.Btc, "LTC" => ChainId.Ltc, "DOGE" => ChainId.Doge, _ => ChainId.Bch };
        return (new WatchXpub(id, key, script, fp, path), null);
    }

    // ---- descriptors --------------------------------------------------------------------------------

    private static readonly Regex KeyExpression = new(
        @"^(?:\[(?<fp>[0-9a-fA-F]{8})(?<path>(?:/[0-9]+['hH]?)*)\])?(?<key>[1-9A-HJ-NP-Za-km-z]{100,120})(?<suffix>(?:/[^)]*)?)$",
        RegexOptions.Compiled);

    private static (WatchXpub? Xpub, string? Issue) ParseDescriptor(string descriptor, string? named)
    {
        var body = descriptor;
        var hash = descriptor.IndexOf('#');
        if (hash >= 0)
        {
            body = descriptor[..hash];
            if (Checksum(body) != descriptor[(hash + 1)..]) return (null, "watch.err.checksum");
        }

        WatchScript script;
        string inner;
        if (Unwrap(body, "sh(wpkh(", "))") is { } nested) { script = WatchScript.NestedSegwit; inner = nested; }
        else if (Unwrap(body, "wpkh(", ")") is { } wpkh) { script = WatchScript.Segwit; inner = wpkh; }
        else if (Unwrap(body, "pkh(", ")") is { } pkh) { script = WatchScript.Legacy; inner = pkh; }
        else if (Unwrap(body, "tr(", ")") is { } tr && !tr.Contains(',')) { script = WatchScript.Taproot; inner = tr; }
        else return (null, "watch.err.descriptor");

        var m = KeyExpression.Match(inner);
        if (!m.Success) return (null, "watch.err.descriptor");

        // The receive and change branches below the key — the standard layout, written any of the
        // usual ways. Anything else (a single branch of hardened children, a fixed child) is refused.
        var suffix = m.Groups["suffix"].Value;
        if (suffix is not ("" or "/0/*" or "/1/*" or "/<0;1>/*")) return (null, "watch.err.descriptor");

        var key = m.Groups["key"].Value;
        var data = DecodeExtended(key);
        if (data is null) return (null, "watch.err.key");
        var version = VersionOf(data);
        if (version is TpubVersion or UpubVersion or VpubVersion) return (null, "watch.err.testnet");

        var chain = named ?? version switch
        {
            LtubVersion or MtubVersion => "LTC",
            DgubVersion => "DOGE",
            _ => "BTC",
        };

        var fp = m.Groups["fp"].Success ? m.Groups["fp"].Value.ToLowerInvariant() : null;
        var path = m.Groups["path"].Success && m.Groups["path"].Value.Length > 0
            ? m.Groups["path"].Value[1..].Replace('h', '\'').Replace('H', '\'')
            : null;
        return Build(chain, key, script, fp, path);
    }

    private static string? Unwrap(string s, string open, string close) =>
        s.StartsWith(open, StringComparison.Ordinal) && s.EndsWith(close, StringComparison.Ordinal)
            ? s[open.Length..^close.Length]
            : null;

    // ---- BIP-380 checksum ---------------------------------------------------------------------------

    private const string InputCharset =
        "0123456789()[],'/*abcdefgh@:$%{}IJKLMNOPQRSTUVWXYZ&+-.;<=>?!^_|~ijklmnopqrstuvwxyzABCDEFGH`#\"\\ ";

    private const string ChecksumCharset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    private static ulong PolyMod(ulong c, int val)
    {
        var c0 = (byte)(c >> 35);
        c = ((c & 0x7ffffffffUL) << 5) ^ (ulong)val;
        if ((c0 & 1) != 0) c ^= 0xf5dee51989UL;
        if ((c0 & 2) != 0) c ^= 0xa9fdca3312UL;
        if ((c0 & 4) != 0) c ^= 0x1bab10e32dUL;
        if ((c0 & 8) != 0) c ^= 0x3706b1677aUL;
        if ((c0 & 16) != 0) c ^= 0x644d626ffdUL;
        return c;
    }

    /// <summary>The BIP-380 descriptor checksum of <paramref name="descriptor"/> (without its <c>#</c>
    /// part), or null when it holds a character descriptors cannot.</summary>
    public static string? Checksum(string descriptor)
    {
        ulong c = 1;
        int cls = 0, clsCount = 0;
        foreach (var ch in descriptor)
        {
            var pos = InputCharset.IndexOf(ch);
            if (pos < 0) return null;
            c = PolyMod(c, pos & 31);
            cls = cls * 3 + (pos >> 5);
            if (++clsCount == 3)
            {
                c = PolyMod(c, cls);
                cls = 0;
                clsCount = 0;
            }
        }

        if (clsCount > 0) c = PolyMod(c, cls);
        for (var j = 0; j < 8; j++) c = PolyMod(c, 0);
        c ^= 1;

        var sb = new StringBuilder(8);
        for (var j = 0; j < 8; j++) sb.Append(ChecksumCharset[(int)((c >> (5 * (7 - j))) & 31)]);
        return sb.ToString();
    }
}
