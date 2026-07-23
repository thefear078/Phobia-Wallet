namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// Whether the core can derive receive addresses for a chain.
/// </summary>
public enum ChainSupportLevel
{
    /// <summary>HD receive-address derivation AND balance sync are implemented.</summary>
    Supported,

    /// <summary>
    /// A real, restorable receive address is derived, but the balance cannot be read from a
    /// public explorer. Monero is the case: amounts are hidden on-chain, so seeing your balance
    /// requires scanning with the view key against a node — not something a public API exposes.
    /// </summary>
    ReceiveOnly,

    /// <summary>Listed for product planning; derivation is not implemented.</summary>
    Planned,
}
