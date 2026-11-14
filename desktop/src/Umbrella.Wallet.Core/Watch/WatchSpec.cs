using System.Text.Json;
using System.Text.Json.Serialization;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Watch;

/// <summary>Which addresses an account-level extended public key makes.</summary>
public enum WatchScript
{
    /// <summary>P2PKH, <c>1…</c> / <c>L…</c> / <c>D…</c> (BIP44, <c>pkh()</c>).</summary>
    Legacy,

    /// <summary>P2WPKH inside P2SH, <c>3…</c> (BIP49, <c>sh(wpkh())</c>).</summary>
    NestedSegwit,

    /// <summary>Native SegWit P2WPKH, <c>bc1q…</c> (BIP84, <c>wpkh()</c>).</summary>
    Segwit,

    /// <summary>Taproot P2TR key path, <c>bc1p…</c> (BIP86, <c>tr()</c>).</summary>
    Taproot,
}

/// <summary>
/// One watched HD account: its account-level extended public key and the address type it makes. The
/// key origin (master fingerprint and account path) is kept when it was given, because a hardware
/// wallet signing a PSBT made from this account needs both to recognise its own keys.
/// </summary>
public sealed record WatchXpub(
    ChainId Chain,
    string ExtPubKey,
    WatchScript Script,
    string? Fingerprint = null,
    string? AccountPath = null);

/// <summary>One watched single address, with the ticker of its network.</summary>
public sealed record WatchAddressEntry(string Symbol, string Address);

/// <summary>
/// What a seedless watch-only wallet watches (roadmap H.5): HD accounts by extended public key, and
/// single addresses on any network. Nothing in it can sign. It is kept sealed beside the wallet's vault
/// (<c>WatchWalletFile</c>), because an xpub names every address an account will ever use.
/// </summary>
public sealed record WatchSpec(IReadOnlyList<WatchXpub> Xpubs, IReadOnlyList<WatchAddressEntry> Addresses)
{
    public static WatchSpec Empty { get; } = new([], []);

    public bool IsEmpty => Xpubs.Count == 0 && Addresses.Count == 0;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(new Stored(Xpubs.ToList(), Addresses.ToList()), Json);

    public static WatchSpec FromJson(string json)
    {
        var stored = JsonSerializer.Deserialize<Stored>(json, Json)
                     ?? throw new InvalidDataException("Empty watch-only wallet description.");
        return new WatchSpec(stored.Xpubs ?? [], stored.Addresses ?? []);
    }

    private sealed record Stored(List<WatchXpub>? Xpubs, List<WatchAddressEntry>? Addresses);
}
