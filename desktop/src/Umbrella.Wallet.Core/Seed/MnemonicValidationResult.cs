namespace Umbrella.Wallet.Core.Seed;

/// <summary>
/// Outcome of BIP39 mnemonic validation for the 24-word wallet MVP.
/// </summary>
public sealed record MnemonicValidationResult(
    bool IsValid,
    string? NormalizedMnemonic,
    string? Error)
{
    public static MnemonicValidationResult Success(string normalized) =>
        new(true, normalized, null);

    public static MnemonicValidationResult Fail(string error) =>
        new(false, null, error);
}
