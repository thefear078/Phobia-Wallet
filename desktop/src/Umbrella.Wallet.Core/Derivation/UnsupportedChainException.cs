using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// Thrown when address derivation is requested for a planned/unsupported chain.
/// </summary>
public sealed class UnsupportedChainException : NotSupportedException
{
    public ChainId ChainId { get; }

    public UnsupportedChainException(ChainId chainId)
        : base($"Receive-address derivation for {chainId} is planned but not supported in this MVP.")
    {
        ChainId = chainId;
    }
}
