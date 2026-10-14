using System.Text.RegularExpressions;
using Umbrella.Wallet.App;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A sender's error is read in the wallet's language.
///
/// The senders write English, and keep writing it — that is the record. What the user reads goes through
/// <see cref="SendErrorText"/>: the common messages whole, the rare ones under a sentence that says what
/// kind of trouble it is. The last test scans the senders, so a new message that nothing can place
/// fails the build instead of reaching a Ukrainian screen in English unannounced.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SendErrorTextTests : IDisposable
{
    private readonly string _before = Loc.Instance.CurrentCode;

    public void Dispose() => Loc.Instance.CurrentCode = _before;

    [Fact]
    public void A_common_message_is_translated_whole_and_keeps_its_values()
    {
        Loc.Instance.CurrentCode = "uk";

        Assert.Equal("Це не дійсна адреса Solana.", SendErrorText.Localize("That is not a valid Solana address."));
        Assert.Equal("Не вдалося прочитати баланс із api.example.org. Нічого не надіслано.",
            SendErrorText.Localize("Could not read the balance from api.example.org. Nothing was sent."));
        Assert.Equal("Недостатньо USDT: контракт показує 1.5, а ви хочете надіслати 2.",
            SendErrorText.Localize("Insufficient USDT: the contract reports 1.5, you asked to send 2."));
        Assert.Equal("Це власна адреса Stellar цього гаманця.",
            SendErrorText.Localize("That is this wallet's own Stellar address."));
    }

    [Fact]
    public void A_rare_message_gets_a_sentence_in_the_language_and_keeps_the_original_under_it()
    {
        Loc.Instance.CurrentCode = "uk";
        const string original = "Polkadot changed how transactions are built (call index). This wallet will not guess — nothing was sent.";

        var shown = SendErrorText.Localize(original);

        Assert.Equal("Переказ не вдався. Нічого не надіслано.\n" + original, shown);
    }

    [Theory]
    [InlineData("The receiver did not answer in time.", "network")]
    [InlineData("Check 5abc on an explorer before sending again, because a second send would pay twice.", "unclear")]
    [InlineData("Decred refused the transaction: bad signature", "refused")]
    [InlineData("That is not a valid Zano address.", "address")]
    [InlineData("Not enough TON: need 1.2 TON including fee.", "funds")]
    [InlineData("Something nobody has written yet.", "failed")]
    public void The_rest_is_placed_by_what_it_says(string message, string category) =>
        Assert.Equal(category, SendErrorText.CategoryOf(message));

    [Fact]
    public void In_english_the_message_is_shown_as_the_sender_wrote_it()
    {
        Loc.Instance.CurrentCode = "en";
        const string message = "That is not a valid Solana address.";

        Assert.Same(message, SendErrorText.Localize(message));
    }

    [Fact]
    public void Every_language_has_every_message_with_the_same_values()
    {
        var keys = SendErrorText.ExactKeys;
        Assert.NotEmpty(keys);
        foreach (var code in new[] { "en", "uk", "ru", "zh", "es", "de" })
        {
            Loc.Instance.CurrentCode = code;
            foreach (var key in keys)
            {
                var text = Loc.Instance[key];
                Assert.NotEqual(key, text);
                Assert.Equal(Placeholders(Loc.Instance.InEnglish(key)), Placeholders(text));
            }
        }
    }

    [Fact]
    public void No_two_whole_translations_claim_the_same_message()
    {
        foreach (var key in SendErrorText.ExactKeys)
        {
            var sample = Sample(Loc.Instance.InEnglish(key));
            var claimed = SendErrorText.ExactKeys.Where(k => SendErrorText.PatternFor(k).IsMatch(sample)).ToList();
            Assert.True(claimed.Count == 1 || claimed[0] == key, $"{key}: also matched by {string.Join(", ", claimed)}");
        }
    }

    /// <summary>
    /// Every error a sender can return is either translated whole or placed in a category more precise
    /// than "the transfer could not be made". Literal messages in an error position are found the way a
    /// reader would: <c>return (null, "…")</c>, a failed result's text, <c>Fail("…")</c>.
    /// </summary>
    [Fact]
    public void Every_message_in_the_senders_is_translated_or_placed()
    {
        var root = FindSourceRoot();
        if (root is null) return;   // run from a published copy without sources
        var network = Path.Combine(root, "Umbrella.Wallet.Infrastructure", "Network");
        var literal = @"(\$?""(?:[^""\\]|\\.)*"")";
        var positions = new[]
        {
            new Regex(@"return \(null, " + literal),
            new Regex(@"return \(null, null, null, " + literal),
            new Regex(@"Result\((?:false|Ok: false)[^;]*?, " + literal),
            new Regex(@"\bFail\(" + literal),
            new Regex(@"\?\? " + literal + @"\)"),
        };

        var unplaced = new List<string>();
        var count = 0;
        foreach (var file in Directory.GetFiles(network, "*Sender*.cs"))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (var position in positions)
                {
                    foreach (Match m in position.Matches(line))
                    {
                        var raw = m.Groups[1].Value;
                        var body = raw.StartsWith('$') ? raw[2..^1] : raw[1..^1];
                        if (body.Length < 12 || !body.Contains(' ')) continue;
                        // A message cut by a line break or a nested quote is checked by its first line.
                        var message = Regex.Replace(body, @"\{[^{}]*\}", "VALUE").Replace("\\\"", "\"");
                        count++;
                        if (SendErrorText.ExactKeyFor(message) is null && SendErrorText.CategoryOf(message) == "failed")
                            unplaced.Add($"{Path.GetFileName(file)}: {body}");
                    }
                }
            }
        }

        Assert.True(count > 100, $"only {count} messages found — the scan no longer sees the senders");
        var unknown = unplaced.Where(u => !InternalFailures.Any(f => u.Contains(f, StringComparison.Ordinal))).ToList();
        Assert.True(unknown.Count == 0,
            "translate these (Localization.cs, senderr.*) or say what kind of trouble they are (SendErrorText):\n" +
            string.Join("\n", unknown));
    }

    /// <summary>
    /// The wallet's own failures — a transaction it could not build or sign, a key that does not match,
    /// a chain whose rules changed under it. They read "The transfer could not be made. Nothing was sent."
    /// in the wallet's language, with the sender's line under it for whoever has to find out why.
    /// </summary>
    private static readonly string[] InternalFailures =
    [
        "PSBT export is for Bitcoin.", "Export failed: ", "The wallet's own Cardano address is invalid.",
        "This is a vesting or special account", "The chain has a different key on record",
        "This transaction does not belong to the unlocked wallet.", "Could not build the ",
        "comes from a staking transaction", "Signing failed: ", "THORChain returned an invalid router",
        "Could not encode the swap call", "This quote carries no contract calldata.",
        "The key does not match the account being sent from.", "Polkadot address could not be read",
        "Polkadot changed how ", "This token's details are not valid.", "as a token mint",
        "is not held by a token program", "cannot be sent from here", "This token's program could not be read.",
        "Could not prepare the TRON transaction",
    ];

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private static string Sample(string template) => Regex.Replace(template, @"\{(\d)\}", "V$1");

    private static string? FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "desktop", "src");
            if (Directory.Exists(Path.Combine(candidate, "Umbrella.Wallet.Infrastructure"))) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
