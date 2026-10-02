namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>Who carries out a swap.</summary>
public enum SwapProvider
{
    /// <summary>THORChain: a decentralised network. The coins go to a vault with a memo and the network
    /// pays out; nobody holds them in between. No account, no key, no extra fee.</summary>
    Thorchain,

    /// <summary>NEAR Intents (the 1Click API): coins go to a one-time deposit address, solvers fill the
    /// swap through a smart contract, and a failed swap is refunded to the paying address. Without a
    /// partner key it adds a 0.25% platform fee.</summary>
    NearIntents,

    /// <summary>Exolix: an instant exchange. It holds the coins for the minutes the swap takes — the one
    /// custodial route, used only for the coins no decentralised route reaches (Monero, Nano, Decred).</summary>
    Exolix,
}

/// <summary>
/// One coin on one network, as the swap screen offers it, with what each provider calls it.
/// </summary>
/// <param name="Key">The swap screen's identity for it: "USDT@TRON", "ETH@ARB", "BTC".</param>
/// <param name="Symbol">The coin: "USDT".</param>
/// <param name="Network">The network it is on, as the wallet names it: "TRON", "Arbitrum One".</param>
/// <param name="AddressSymbol">The wallet account whose address receives it (and pays it, and gets a
/// refund): "TRX" for a TRON token, "ETH" for anything on an EVM network.</param>
/// <param name="SendKey">The Send screen's key for paying with it, when that is fixed (native coins);
/// null for a token, whose key the Send picker builds from the held row.</param>
/// <param name="Contract">A token's contract, mint or jetton master; null for a native coin.</param>
/// <param name="Thor">THORChain's asset id, when THORChain trades it.</param>
/// <param name="Intents">NEAR Intents' (blockchain, symbol), resolved to an asset id from its token list.</param>
/// <param name="Exolix">Exolix's (coin, network) codes.</param>
public sealed record SwapAsset(
    string Key,
    string Symbol,
    string Network,
    string AddressSymbol,
    string? SendKey,
    string? Contract,
    string? Thor,
    (string Blockchain, string Symbol)? Intents,
    (string Coin, string Network)? Exolix)
{
    /// <summary>"USDT · TRON" — the coin and where it lives, as both pickers show it.</summary>
    public string Display => $"{Symbol} · {Network}";
}

/// <summary>
/// Every coin and network the wallet can swap, and how a pair is routed.
///
/// <para>The order of preference is the order of trust: THORChain first (nobody holds the coins), then
/// NEAR Intents (a contract holds them, refunds to the payer), then Exolix (an exchange holds them for
/// the minutes of the swap). A pair uses the first route that trades both sides — so Exolix is only ever
/// the route for Monero, Nano and Decred, which nothing decentralised reaches.</para>
///
/// <para>Paying through THORChain needs a deposit this wallet can build with a memo: Bitcoin, Litecoin,
/// Dogecoin and Bitcoin Cash carry it as an OP_RETURN, Ethereum as router calldata, Cosmos as the
/// transaction memo. Other THORChain chains (XRP, TRON, Solana) want the memo in places this wallet's
/// senders do not write yet, so they are paid through NEAR Intents instead and only RECEIVED from
/// THORChain.</para>
/// </summary>
public static class SwapCatalog
{
    public static readonly IReadOnlyList<SwapAsset> All =
    [
        // --- Bitcoin family -----------------------------------------------------------------------
        new("BTC", "BTC", "Bitcoin", "BTC", "BTC", null, "BTC.BTC", ("btc", "BTC"), ("BTC", "BTC")),
        new("LTC", "LTC", "Litecoin", "LTC", "LTC", null, "LTC.LTC", ("ltc", "LTC"), ("LTC", "LTC")),
        new("DOGE", "DOGE", "Dogecoin", "DOGE", "DOGE", null, "DOGE.DOGE", ("doge", "DOGE"), ("DOGE", "DOGE")),
        new("BCH", "BCH", "Bitcoin Cash", "BCH", "BCH", null, "BCH.BCH", ("bch", "BCH"), ("BCH", "BCH")),
        new("ZEC", "ZEC", "Zcash", "ZEC", "ZEC", null, null, ("zec", "ZEC"), ("ZEC", "ZEC")),

        // --- Ethereum and the EVM networks (one 0x address) ------------------------------------------
        new("ETH", "ETH", "Ethereum", "ETH", "ETH", null, "ETH.ETH", ("eth", "ETH"), ("ETH", "ETH")),
        new("USDT@ETH", "USDT", "Ethereum", "ETH", null, "0xdac17f958d2ee523a2206206994597c13d831ec7",
            "ETH.USDT-0XDAC17F958D2EE523A2206206994597C13D831EC7", ("eth", "USDT"), ("USDT", "ETH")),
        new("USDC@ETH", "USDC", "Ethereum", "ETH", null, "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48",
            "ETH.USDC-0XA0B86991C6218B36C1D19D4A2E9EB0CE3606EB48", ("eth", "USDC"), ("USDC", "ETH")),
        new("ETH@ARB", "ETH", "Arbitrum One", "ETH", "ARB", null, null, ("arb", "ETH"), ("ETH", "ARBITRUM")),
        new("ETH@BASE", "ETH", "Base", "ETH", "BASE", null, "BASE.ETH", ("base", "ETH"), ("ETH", "BASE")),
        new("ETH@OP", "ETH", "Optimism", "ETH", "OP", null, null, ("op", "ETH"), ("ETH", "OPTIMISM")),
        new("BNB", "BNB", "BSC", "ETH", "BNB", null, "BSC.BNB", ("bsc", "BNB"), ("BNB", "BSC")),
        new("USDT@BSC", "USDT", "BSC", "ETH", null, "0x55d398326f99059ff775485246999027b3197955",
            "BSC.USDT-0X55D398326F99059FF775485246999027B3197955", ("bsc", "USDT"), ("USDT", "BSC")),
        new("AVAX", "AVAX", "Avalanche", "ETH", "AVAX", null, "AVAX.AVAX", ("avax", "AVAX"), ("AVAX", "AVAXC")),
        new("MATIC", "POL", "Polygon", "ETH", "MATIC", null, null, ("pol", "POL"), ("POL", "MATIC")),
        new("USDC@BASE", "USDC", "Base", "ETH", null, "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913",
            "BASE.USDC-0X833589FCD6EDB6E08F4C7C32D4F71B54BDA02913", ("base", "USDC"), null),
        new("USDC@ARB", "USDC", "Arbitrum One", "ETH", null, "0xaf88d065e77c8cc2239327c5edb3a432268e5831",
            null, ("arb", "USDC"), null),
        new("USDT@POL", "USDT", "Polygon", "ETH", null, "0xc2132d05d31c914a87c6611c10748aeb04b58e8f",
            null, ("pol", "USDT"), ("USDT", "MATIC")),

        // --- TRON ----------------------------------------------------------------------------------
        new("TRX", "TRX", "TRON", "TRX", "TRX", null, "TRON.TRX", ("tron", "TRX"), ("TRX", "TRX")),
        // TRON's USDT: the Send screen's "USDT" key is TRC-20 USDT on this wallet's TRON address.
        new("USDT@TRON", "USDT", "TRON", "TRX", "USDT", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            "TRON.USDT-TR7NHQJEKQXGTCI8Q8ZY4PL8OTSZGJLJ6T", ("tron", "USDT"), ("USDT", "TRX")),

        // --- Account chains ---------------------------------------------------------------------------
        new("SOL", "SOL", "Solana", "SOL", "SOL", null, "SOL.SOL", ("sol", "SOL"), ("SOL", "SOL")),
        new("USDC@SOL", "USDC", "Solana", "SOL", null, "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v",
            null, ("sol", "USDC"), ("USDC", "SOL")),
        new("XRP", "XRP", "XRP Ledger", "XRP", "XRP", null, "XRP.XRP", ("xrp", "XRP"), ("XRP", "XRP")),
        new("ADA", "ADA", "Cardano", "ADA", "ADA", null, null, ("cardano", "ADA"), ("ADA", "ADA")),
        new("XLM", "XLM", "Stellar", "XLM", "XLM", null, null, ("stellar", "XLM"), ("XLM", "XLM")),
        // Toncoin trades as GRAM on NEAR Intents since TON's 2026 rename.
        new("TON", "TON", "TON", "TON", "TON", null, null, ("ton", "GRAM"), ("TON", "TON")),
        new("USDT@TON", "USDT", "TON", "TON", null, "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs",
            null, ("ton", "USDT"), null),
        new("ATOM", "ATOM", "Cosmos Hub", "ATOM", "ATOM", null, "GAIA.ATOM", null, ("ATOM", "ATOM")),
        new("NEAR", "NEAR", "NEAR Protocol", "NEAR", "NEAR", null, null, null, ("NEAR", "NEAR")),
        new("DOT", "DOT", "Polkadot", "DOT", "DOT", null, null, null, ("DOT", "DOT")),

        // --- Reached only through an exchange -------------------------------------------------------
        new("XMR", "XMR", "Monero", "XMR", "XMR", null, null, null, ("XMR", "XMR")),
        new("XNO", "XNO", "Nano", "XNO", "XNO", null, null, null, ("XNO", "NANO")),
        new("DCR", "DCR", "Decred", "DCR", null, null, null, null, ("DCR", "DCR")),
    ];

    private static readonly Dictionary<string, SwapAsset> ByKey =
        All.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

    public static SwapAsset? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var asset) ? asset : null;

    /// <summary>Coins this wallet pays a THORChain deposit with itself: memo as OP_RETURN (UTXO), router
    /// calldata (Ethereum) or the transaction memo (Cosmos Hub).</summary>
    public static readonly IReadOnlySet<string> ThorPayable =
        new HashSet<string>(["BTC", "LTC", "DOGE", "BCH", "ETH", "ATOM"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The routes that can carry <paramref name="from"/> → <paramref name="to"/>, most trusted first.
    /// Empty when nothing trades the pair.
    /// </summary>
    public static IReadOnlyList<SwapProvider> RoutesFor(SwapAsset from, SwapAsset to)
    {
        var routes = new List<SwapProvider>();
        if (string.Equals(from.Key, to.Key, StringComparison.OrdinalIgnoreCase)) return routes;
        if (from.Thor is not null && to.Thor is not null && ThorPayable.Contains(from.Key)) routes.Add(SwapProvider.Thorchain);
        if (from.Intents is not null && to.Intents is not null) routes.Add(SwapProvider.NearIntents);
        if (from.Exolix is not null && to.Exolix is not null) routes.Add(SwapProvider.Exolix);
        return routes;
    }

    /// <summary>Whether anything can carry the pair.</summary>
    public static bool CanSwap(SwapAsset from, SwapAsset to) => RoutesFor(from, to).Count > 0;
}
