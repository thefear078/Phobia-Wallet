using System.Globalization;
using System.Text;
using System.Text.Json;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Seed;

/// <summary>Why a phrase is not a Monero seed — so the import screen can say which thing to fix.</summary>
public enum MoneroSeedProblem
{
    None,
    /// <summary>Not 25 words (a Monero seed always has 25).</summary>
    WordCount,
    /// <summary>25 words, but not all from one Monero wordlist.</summary>
    UnknownWords,
    /// <summary>Every word is a Monero word, but the 25th (the checksum) does not match — a word is mistyped.</summary>
    Checksum,
}

/// <summary>A decoded Monero seed: its language, the phrase written out in full, and the account it holds.</summary>
public sealed record MoneroSeed(string Language, string Normalized, MoneroWallet Wallet);

/// <summary>
/// Monero's own 25-word recovery phrase — the one the Monero GUI and CLI, Feather, Cake Wallet and
/// MyMonero show — in every language Monero ships: Chinese (simplified), English, Dutch, French, Spanish,
/// German, Italian, Portuguese, Japanese, Russian, Esperanto and Lojban.
///
/// The rules are Monero's (src/mnemonics/electrum-words.cpp), not an approximation of them:
/// <list type="bullet">
/// <item>24 words carry the 32-byte key, three words to every 4 bytes; the 25th is a checksum — the word at
///   <c>crc32(first N letters of each of the 24 words) mod 24</c>, N being the list's unique-prefix length.</item>
/// <item>A word is identified by its first N letters, so abbreviations Monero accepts are accepted here.</item>
/// <item>The secret spend key is the 32 bytes reduced mod ℓ; the secret view key is
///   <c>Keccak-256(the 32 bytes)</c> reduced mod ℓ.</item>
/// </list>
/// The wordlists are embedded byte-for-byte from Monero's source (their hashes are pinned by a test), and
/// every language is pinned to an independent implementation's keys and address.
///
/// Forgiving where it cannot change the answer: case is ignored, numbers and punctuation around words are
/// dropped, Chinese may be written with or without spaces, and a Spanish or German word typed without its
/// accent is still found (in those lists no two words differ by an accent alone).
/// </summary>
public static class MoneroMnemonic
{
    public const int WordCount = 25;

    private sealed record Wordlist(
        string Language,
        int Prefix,
        string[] Words,
        Dictionary<string, int> ByPrefix,
        Dictionary<string, int>? ByFoldedPrefix);

    private static readonly Lazy<IReadOnlyList<Wordlist>> Lists = new(Load);

    /// <summary>The languages recognised, in the order Monero tries them.</summary>
    public static IReadOnlyList<string> Languages => Lists.Value.Select(l => l.Language).ToList();

    /// <summary>A language's words as Monero ships them — for the wordlist integrity test.</summary>
    public static IReadOnlyList<string> WordsOf(string language) =>
        Lists.Value.First(l => l.Language == language).Words;

    /// <summary>The unique-prefix length Monero uses for a language.</summary>
    public static int PrefixOf(string language) => Lists.Value.First(l => l.Language == language).Prefix;

    /// <summary>True when the phrase is a valid 25-word Monero seed in any language.</summary>
    public static bool IsMoneroMnemonic(string? phrase) => Recognize(phrase, out _, out _) == MoneroSeedProblem.None;

    /// <summary>Checks a phrase without deriving keys — cheap enough to run on every keystroke.</summary>
    public static MoneroSeedProblem Check(string? phrase) => Recognize(phrase, out _, out _);

    /// <summary>Decodes a Monero seed to its account. Returns false (and why) when it is not one.</summary>
    public static bool TryDecode(string? phrase, out MoneroSeed? seed, out MoneroSeedProblem problem)
    {
        seed = null;
        problem = Recognize(phrase, out var list, out var indices);
        if (problem != MoneroSeedProblem.None) return false;

        var key = new byte[32];
        if (!IndicesToBytes(indices, list!.Words.Length, key))
        {
            // Three words that no 4 bytes could have produced: Monero rejects these too.
            problem = MoneroSeedProblem.UnknownWords;
            return false;
        }

        seed = new MoneroSeed(
            list.Language,
            string.Join(' ', indices.Select(i => list.Words[i])),
            KeysFrom(key));
        Array.Clear(key);
        return true;
    }

    /// <summary>Decodes a phrase already known to be a Monero seed (the vault holds only valid ones).</summary>
    public static MoneroSeed Decode(string phrase) =>
        TryDecode(phrase, out var seed, out var problem)
            ? seed!
            : throw new ArgumentException($"Not a Monero seed ({problem}).", nameof(phrase));

    /// <summary>spend = sc_reduce32(key); view = sc_reduce32(Keccak-256(key)) — account_base::generate.</summary>
    private static MoneroWallet KeysFrom(byte[] key)
    {
        var spend = MoneroKeys.ScReduce32(key);
        var view = MoneroKeys.ScReduce32(MoneroKeys.Keccak256(key));
        var publicSpend = MoneroKeys.ScalarMultBase(spend);
        var publicView = MoneroKeys.ScalarMultBase(view);
        return new MoneroWallet(
            MoneroKeys.BuildAddress(MoneroKeys.MainnetPrefix, publicSpend, publicView),
            Convert.ToHexString(spend).ToLowerInvariant(),
            Convert.ToHexString(view).ToLowerInvariant(),
            Convert.ToHexString(publicSpend).ToLowerInvariant(),
            Convert.ToHexString(publicView).ToLowerInvariant());
    }

    private static MoneroSeedProblem Recognize(string? phrase, out Wordlist? match, out int[] indices)
    {
        match = null;
        indices = [];
        var tokens = Tokens(phrase);
        if (tokens.Count != WordCount) return MoneroSeedProblem.WordCount;

        var allWordsFound = false;
        foreach (var list in Lists.Value)
        {
            var found = new int[WordCount];
            var ok = true;
            for (var i = 0; i < WordCount && ok; i++)
            {
                ok = TryFind(list, tokens[i], out found[i]);
            }
            if (!ok) continue;

            allWordsFound = true;
            if (ChecksumMatches(list, found))
            {
                match = list;
                indices = found;   // all 25: the key is in the first 24, and the phrase keeps its checksum word
                return MoneroSeedProblem.None;
            }
        }

        return allWordsFound ? MoneroSeedProblem.Checksum : MoneroSeedProblem.UnknownWords;
    }

    private static bool TryFind(Wordlist list, string token, out int index)
    {
        var key = Prefix(token, list.Prefix);
        if (list.ByPrefix.TryGetValue(key, out index)) return true;
        return list.ByFoldedPrefix is not null && list.ByFoldedPrefix.TryGetValue(Fold(key), out index);
    }

    private static bool ChecksumMatches(Wordlist list, int[] found)
    {
        if (ChecksumIndex(list, found, list.Prefix) is var i && found[i] == found[24]) return true;
        // monero-python (and wallets built on it) computes Japanese checksums from 4 letters where Monero
        // uses 3. The checksum is not part of the key, so such a seed is still the same account.
        return list.Language == "Japanese" && found[ChecksumIndex(list, found, 4)] == found[24];
    }

    /// <summary>crc32 over the concatenated prefixes of the 24 words, as Monero spells them, mod 24.</summary>
    private static int ChecksumIndex(Wordlist list, int[] found, int prefix)
    {
        var joined = new StringBuilder();
        for (var i = 0; i < 24; i++) joined.Append(Prefix(list.Words[found[i]], prefix, lower: false));
        return (int)(Crc32(Encoding.UTF8.GetBytes(joined.ToString())) % 24);
    }

    /// <summary>
    /// words_to_bytes: every three word indices carry one little-endian 32-bit word. Done in wrapping 32-bit
    /// arithmetic as Monero does it, including the check that rejects triples no 4 bytes could produce.
    /// </summary>
    private static bool IndicesToBytes(int[] indices, int n, byte[] output)
    {
        var len = (uint)n;
        for (var i = 0; i < 8; i++)
        {
            var w1 = (uint)indices[i * 3];
            var w2 = (uint)indices[i * 3 + 1];
            var w3 = (uint)indices[i * 3 + 2];
            var value = unchecked(w1 + len * ((len - w1 + w2) % len) + len * len * ((len - w2 + w3) % len));
            if (value % len != w1) return false;
            BitConverter.TryWriteBytes(output.AsSpan(i * 4, 4), value);
            if (!BitConverter.IsLittleEndian) output.AsSpan(i * 4, 4).Reverse();
        }
        return true;
    }

    /// <summary>
    /// The words of a pasted phrase. Anything that is not a letter separates words (so "1. abbey, 2. …"
    /// works), an apostrophe stays inside a word (Lojban has them), and every Chinese character is a word
    /// on its own — Monero's Chinese list is single characters, often written with no spaces at all.
    /// </summary>
    private static List<string> Tokens(string? phrase)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(phrase)) return tokens;

        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0 && current.ToString().Any(char.IsLetter)) tokens.Add(current.ToString());
            current.Clear();
        }

        foreach (var rune in phrase.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (IsHan(rune))
            {
                Flush();
                tokens.Add(rune.ToString());
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            var inWord = Rune.IsLetter(rune)
                         || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                         || rune.Value is '\'' or '’';
            if (inWord) current.Append(rune.Value == '’' ? "'" : rune.ToString());
            else Flush();
        }
        Flush();
        return tokens;
    }

    private static bool IsHan(Rune rune) =>
        rune.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0x20000 and <= 0x2A6DF;

    /// <summary>The first <paramref name="count"/> code points (utf8prefix), lower-cased for lookups.</summary>
    private static string Prefix(string word, int count, bool lower = true)
    {
        var sb = new StringBuilder();
        foreach (var rune in word.EnumerateRunes())
        {
            if (count-- == 0) break;
            sb.Append(rune.ToString());
        }
        return lower ? sb.ToString().ToLowerInvariant() : sb.ToString();
    }

    /// <summary>Drops accents: "lápiz" → "lapiz".</summary>
    private static string Fold(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IReadOnlyList<Wordlist> Load()
    {
        using var stream = typeof(MoneroMnemonic).Assembly.GetManifestResourceStream(
                               "Umbrella.Wallet.Core.Seed.MoneroWordlists.json")
                           ?? throw new InvalidOperationException("The Monero wordlists are missing from this build.");
        using var doc = JsonDocument.Parse(stream);

        var lists = new List<Wordlist>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var language = entry.GetProperty("language").GetString()!;
            var prefix = entry.GetProperty("prefix").GetInt32();
            var words = entry.GetProperty("words").EnumerateArray().Select(w => w.GetString()!).ToArray();

            var byPrefix = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < words.Length; i++) byPrefix.Add(Prefix(words[i], prefix), i);

            lists.Add(new Wordlist(language, prefix, words, byPrefix, FoldedLookup(words, prefix, byPrefix)));
        }
        return lists;
    }

    /// <summary>
    /// A second lookup by accent-free prefix, for Latin-script lists where that is still unambiguous (Spanish
    /// and German: no two words differ by an accent alone, and no folded prefix is another word's prefix).
    /// Null for every other list, so nothing is ever guessed.
    /// </summary>
    private static Dictionary<string, int>? FoldedLookup(string[] words, int prefix, Dictionary<string, int> exact)
    {
        if (!words.All(w => w.All(c => c < 0x0250))) return null;   // Latin script only

        var folded = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < words.Length; i++)
        {
            var key = Prefix(words[i], prefix);
            var plain = Fold(key);
            if (plain == key) continue;
            if (exact.ContainsKey(plain) || !folded.TryAdd(plain, i)) return null;
        }
        return folded.Count == 0 ? null : folded;
    }

    /// <summary>CRC-32 (IEEE 802.3, the one boost::crc_32_type computes).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
