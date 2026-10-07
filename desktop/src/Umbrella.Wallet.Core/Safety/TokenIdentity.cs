namespace Umbrella.Wallet.Core.Safety;

/// <summary>What the wallet can say about a token's identity from its network, ticker and contract.</summary>
public enum TokenIdentityVerdict
{
    /// <summary>The contract IS the real token of that ticker on that network.</summary>
    Genuine,
    /// <summary>The ticker belongs to money the wallet knows the real contract of — and this is another
    /// contract. A fake "USDT" airdropped to millions of addresses is the scam this exists for.</summary>
    Impersonation,
    /// <summary>A ticker the wallet keeps no contract for; nothing either way.</summary>
    Unknown,
}

/// <summary>
/// The real contracts of the tickers fake tokens copy most.
///
/// A token's ticker is a string anybody can set. The wallet priced tokens by ticker, so a worthless
/// token calling itself "USDT" was valued at a dollar a unit, never flagged as spam (a priced token is
/// exempt), and a million of them appeared as a million dollars — the lure of the fake-balance scam. A
/// ticker listed here is priced only on its real contract; on any other it is an impersonation, worth
/// nothing and folded away. So is a token taking the network's own coin's ticker (an ERC-20 "ETH", a
/// TRC-20 "TRX"), or bitcoin's anywhere — the real coin is never a token. Tickers not listed keep the
/// old rule; add a contract only after checking it on the network's own explorer.
/// </summary>
public static class TokenIdentity
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> Canonical =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TRON"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["USDT"] = ["TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"],
                ["USDC"] = ["TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8"],
            },
            ["Ethereum"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["USDT"] = ["0xdac17f958d2ee523a2206206994597c13d831ec7"],
                ["USDC"] = ["0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48"],
                ["DAI"] = ["0x6b175474e89094c44da98b954eedeac495271d0f"],
                ["WETH"] = ["0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2"],
                ["WBTC"] = ["0x2260fac5e5542a773aa44fbcfedf7c193bc2c599"],
            },
        };

    /// <summary>Each network's own coin: a token by that ticker on it is never the coin.</summary>
    private static readonly Dictionary<string, string> NativeTicker = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRON"] = "TRX",
        ["Ethereum"] = "ETH",
        ["TON"] = "TON",
        ["Solana"] = "SOL",
    };

    public static TokenIdentityVerdict Judge(string network, string symbol, string contract)
    {
        var ticker = (symbol ?? string.Empty).Trim();
        if (ticker.Length == 0) return TokenIdentityVerdict.Unknown;

        if (NativeTicker.TryGetValue(network ?? string.Empty, out var native) &&
            string.Equals(ticker, native, StringComparison.OrdinalIgnoreCase))
            return TokenIdentityVerdict.Impersonation;
        if (string.Equals(ticker, "BTC", StringComparison.OrdinalIgnoreCase))
            return TokenIdentityVerdict.Impersonation;

        if (Canonical.TryGetValue(network ?? string.Empty, out var tickers) &&
            tickers.TryGetValue(ticker, out var real))
        {
            // Hex (0x…) contracts compare without case — the case is only EIP-55's checksum. Base58 ones
            // (TRON) compare exactly: there, case is part of the address.
            var c0 = (contract ?? string.Empty).Trim();
            return real.Any(c => string.Equals(c, c0,
                    c.StartsWith("0x", StringComparison.Ordinal) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                ? TokenIdentityVerdict.Genuine
                : TokenIdentityVerdict.Impersonation;
        }

        return TokenIdentityVerdict.Unknown;
    }
}
