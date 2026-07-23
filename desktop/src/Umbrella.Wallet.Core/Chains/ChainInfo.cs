namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Metadata for a chain exposed by the wallet core.
/// </summary>
public sealed record ChainInfo(
    ChainId Id,
    string Symbol,
    string Name,
    ChainSupportLevel Support,
    string? DerivationScheme,
    string? ReceivePathTemplate);
