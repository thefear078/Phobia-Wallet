using NBitcoin;

namespace Umbrella.Wallet.Core.Seed;

/// <summary>
/// Generates and validates 24-word BIP39 English mnemonics.
/// Seed material stays on the calling side; this type does not persist secrets.
/// </summary>
public sealed class Bip39MnemonicService
{
    /// <summary>New wallets are generated with the strongest length (256-bit entropy).</summary>
    public const int GeneratedWordCount = 24;

    /// <summary>Every valid BIP39 length. Import must accept all of them, not only 24.</summary>
    private static readonly int[] AllowedWordCounts = [12, 15, 18, 21, 24];

    /// <summary>
    /// Creates a new cryptographically random 24-word English mnemonic.
    /// </summary>
    public string Generate()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        return mnemonic.ToString();
    }

    /// <summary>
    /// Validates a candidate mnemonic: English BIP39 wordlist, a valid word count
    /// (12/15/18/21/24), and a valid checksum.
    /// </summary>
    public MnemonicValidationResult Validate(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            return MnemonicValidationResult.Fail("Recovery phrase is required.");
        }

        var words = mnemonic
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!AllowedWordCounts.Contains(words.Length))
        {
            return MnemonicValidationResult.Fail(
                $"A recovery phrase has 12, 15, 18, 21, or 24 words — found {words.Length}.");
        }

        var normalized = string.Join(' ', words);

        try
        {
            var parsed = new Mnemonic(normalized, Wordlist.English);
            if (!parsed.IsValidChecksum)
            {
                return MnemonicValidationResult.Fail(
                    "Recovery phrase checksum is invalid — check the words and their order.");
            }

            return MnemonicValidationResult.Success(string.Join(' ', parsed.Words));
        }
        catch (Exception ex)
        {
            return MnemonicValidationResult.Fail($"Recovery phrase is invalid: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a validated mnemonic into an NBitcoin <see cref="Mnemonic"/>.
    /// </summary>
    internal static Mnemonic ParseValidated(string normalizedMnemonic) =>
        new(normalizedMnemonic, Wordlist.English);
}
