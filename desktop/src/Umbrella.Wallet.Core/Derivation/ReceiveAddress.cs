using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// A deterministic public receive address derived from a mnemonic.
/// </summary>
public sealed record ReceiveAddress(
    ChainId Chain,
    string Address,
    string DerivationPath,
    uint AddressIndex);
