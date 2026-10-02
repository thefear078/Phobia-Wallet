using System.Numerics;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Swapping every coin the wallet holds: the catalog, how a pair is routed, and the two deposit-address
/// providers' answers, read from responses captured from the live services (1click.chaindefuser.com and
/// exolix.com, 2026-10-01/02).
///
/// The routing rule is the one that matters: the most trusted route that trades both sides wins, so an
/// exchange that holds the user's coins is only ever chosen when nothing decentralised can do the swap.
/// </summary>
public sealed class SwapRoutesTests
{
    private static SwapAsset A(string key) => SwapCatalog.Find(key) ?? throw new KeyNotFoundException(key);

    [Fact]
    public void Every_key_is_unique_and_names_a_coin_the_wallet_has_an_account_for()
    {
        Assert.Equal(SwapCatalog.All.Count, SwapCatalog.All.Select(a => a.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var chains = ChainCatalog.All.Select(c => c.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(SwapCatalog.All, a => Assert.Contains(a.AddressSymbol, chains));
    }

    [Fact]
    public void A_native_coin_pays_through_a_send_path_that_exists()
    {
        foreach (var asset in SwapCatalog.All.Where(a => a.SendKey is not null))
        {
            Assert.True(MainViewModel.SendableSymbols.Contains(asset.SendKey!) || asset.SendKey is "TRX" or "USDT" or "XMR",
                $"{asset.Key} pays with send key {asset.SendKey}, which the Send screen does not handle");
        }
    }

    [Fact]
    public void A_pair_goes_to_the_most_trusted_route_that_trades_both_sides()
    {
        Assert.Equal([SwapProvider.Thorchain, SwapProvider.NearIntents, SwapProvider.Exolix], SwapCatalog.RoutesFor(A("BTC"), A("ETH")));
        // Monero: only an exchange reaches it, in either direction.
        Assert.Equal([SwapProvider.Exolix], SwapCatalog.RoutesFor(A("BTC"), A("XMR")));
        Assert.Equal([SwapProvider.Exolix], SwapCatalog.RoutesFor(A("XMR"), A("BTC")));
        // THORChain trades Solana, but this wallet does not write a Solana THORChain memo: never paid via THORChain.
        Assert.DoesNotContain(SwapProvider.Thorchain, SwapCatalog.RoutesFor(A("SOL"), A("TRX")));
        Assert.Equal(SwapProvider.NearIntents, SwapCatalog.RoutesFor(A("SOL"), A("TRX"))[0]);
        // ...but it delivers TRON USDT from Bitcoin.
        Assert.Equal(SwapProvider.Thorchain, SwapCatalog.RoutesFor(A("BTC"), A("USDT@TRON"))[0]);
        // The Cosmos Hub pays a THORChain vault with the transaction memo.
        Assert.Equal(SwapProvider.Thorchain, SwapCatalog.RoutesFor(A("ATOM"), A("BTC"))[0]);
        Assert.Empty(SwapCatalog.RoutesFor(A("BTC"), A("BTC")));
    }

    [Fact]
    public void Every_coin_the_wallet_receives_has_some_route_from_bitcoin()
    {
        foreach (var asset in SwapCatalog.All.Where(a => a.Key != "BTC"))
            Assert.True(SwapCatalog.CanSwap(A("BTC"), asset), $"nothing swaps BTC for {asset.Key}");
    }

    [Fact]
    public void Thorchain_ids_are_pools_that_trade_today()
    {
        // The Available pools on 2026-10-01 (thorchain/pools via rest.cosmos.directory), for the chains used here.
        string[] pools =
        [
            "AVAX.AVAX", "BASE.ETH", "BASE.USDC-0X833589FCD6EDB6E08F4C7C32D4F71B54BDA02913", "BCH.BCH", "BSC.BNB",
            "BSC.USDT-0X55D398326F99059FF775485246999027B3197955", "BTC.BTC", "DOGE.DOGE", "ETH.ETH",
            "ETH.USDC-0XA0B86991C6218B36C1D19D4A2E9EB0CE3606EB48", "ETH.USDT-0XDAC17F958D2EE523A2206206994597C13D831EC7",
            "GAIA.ATOM", "LTC.LTC", "SOL.SOL", "TRON.TRX", "TRON.USDT-TR7NHQJEKQXGTCI8Q8ZY4PL8OTSZGJLJ6T", "XRP.XRP",
        ];
        Assert.All(SwapCatalog.All.Where(a => a.Thor is not null), a => Assert.Contains(a.Thor!, pools));
    }

    [Fact]
    public void Near_intents_tokens_resolve_from_the_live_list_shape()
    {
        const string list = """
            [{"assetId":"nep141:btc.omft.near","decimals":8,"blockchain":"btc","symbol":"BTC","price":85000},
             {"assetId":"nep245:v2_1.omni.hot.tg:1117_","decimals":9,"blockchain":"ton","symbol":"GRAM","price":3.1},
             {"assetId":"nep141:tron-d28a265909efecdcee7c5028585214ea0b96f015.omft.near","decimals":6,"blockchain":"tron","symbol":"USDT","contractAddress":"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"},
             {"assetId":"nep245:v2_1.omni.hot.tg:56_2CMMyVTGZkeyNZTSvS5sarzfir6g","decimals":18,"blockchain":"bsc","symbol":"USDT"}]
            """;
        var map = NearIntentsSwapClient.ParseTokens(list);
        Assert.Equal(new IntentsToken("nep141:btc.omft.near", 8), map[("btc", "BTC")]);
        Assert.Equal(9, map[("ton", "GRAM")].Decimals);       // Toncoin is listed as GRAM
        Assert.Equal(18, map[("bsc", "USDT")].Decimals);      // BEP-20 USDT has 18 decimals, not 6
        Assert.Equal(("ton", "GRAM"), A("TON").Intents);
    }

    [Fact]
    public void A_dry_near_intents_quote_is_read_in_the_coins_own_units()
    {
        // 1click.chaindefuser.com, BTC → TRX, dry, 2026-10-02 (signature dropped).
        const string body = """
            {"quote":{"amountIn":"100000","amountInFormatted":"0.001","amountInUsd":"85.856000000000","minAmountIn":"100000",
             "amountOut":"256187229","amountOutFormatted":"256.187229","amountOutUsd":"85.580112409137","minAmountOut":"253625356",
             "timeEstimate":860,"refundFee":"1900","withdrawFee":"290000"},"correlationId":"42e54541-5571-4a8f-b37b-75aa0738cbf6"}
            """;
        var (quote, error) = NearIntentsSwapClient.ParseQuote(body, 8, 6);
        Assert.Null(error);
        Assert.NotNull(quote);
        Assert.Equal(0.001m, quote!.AmountIn);
        Assert.Equal(256.187229m, quote.AmountOut);
        Assert.Equal(253.625356m, quote.MinAmountOut);
        Assert.Equal(860, quote.TimeEstimateSeconds);
        Assert.Null(quote.DepositAddress);   // a dry quote issues no address to pay
        Assert.True(quote.AmountInUsd - quote.AmountOutUsd > 0);
    }

    [Fact]
    public void A_real_near_intents_quote_carries_the_address_and_memo_to_pay()
    {
        const string body = """
            {"quote":{"depositAddress":"GDWQ5BHHXGBGRQIZZQG5XF3OSNZOEQYBXZPLHGFTRWZB6YBL4Q2JH7AU","depositMemo":"1111111",
             "amountIn":"1000000000","amountOut":"1170","minAmountOut":"1158","timeEstimate":120,
             "deadline":"2026-10-02T15:00:00Z"},"correlationId":"x"}
            """;
        var (quote, _) = NearIntentsSwapClient.ParseQuote(body, 7, 8);
        Assert.Equal("GDWQ5BHHXGBGRQIZZQG5XF3OSNZOEQYBXZPLHGFTRWZB6YBL4Q2JH7AU", quote!.DepositAddress);
        Assert.Equal("1111111", quote.DepositMemo);
        Assert.Equal(100m, quote.AmountIn);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero), quote.Deadline);
    }

    [Fact]
    public void A_declined_near_intents_quote_says_why()
    {
        var (quote, error) = NearIntentsSwapClient.ParseQuote(
            """{"message":"Temporary swap limits: minimum swap amount is $100","correlationId":"y"}""", 6, 8);
        Assert.Null(quote);
        Assert.Equal("Temporary swap limits: minimum swap amount is $100", error);
    }

    [Theory]
    [InlineData("0.001", 8, "100000")]
    [InlineData("1.5", 18, "1500000000000000000")]
    [InlineData("0.1234567", 6, "123456")]   // beyond the coin's precision is cut, never rounded up
    public void Amounts_go_to_the_smallest_unit_exactly(string amount, int decimals, string expected)
    {
        Assert.Equal(BigInteger.Parse(expected), NearIntentsSwapClient.ToSmallest(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), decimals));
    }

    [Fact]
    public void An_exolix_rate_and_its_limits_are_read_and_a_refusal_keeps_its_words()
    {
        // exolix.com/api/v2/rate, 2026-10-01.
        var (rate, error) = ExolixSwapClient.ParseRate(
            """{"fromAmount":100,"toAmount":0.63772547,"rate":0.00637725,"message":null,"minAmount":0.08974462,"withdrawMin":0.00000624,"maxAmount":1568.0587158203125,"priceImpact":"0"}""");
        Assert.Null(error);
        Assert.Equal(0.63772547m, rate!.ToAmount);
        Assert.Equal(0.08974462m, rate.MinAmount);

        var (none, why) = ExolixSwapClient.ParseRate("""{"error":"Such exchange pair is not available"}""");
        Assert.Null(none);
        Assert.Equal("Such exchange pair is not available", why);

        var (low, below) = ExolixSwapClient.ParseRate(
            """{"fromAmount":100,"toAmount":0,"message":"Amount to exchange is below the possible min amount to exchange","minAmount":197.341925,"maxAmount":3455397.5}""");
        Assert.Null(low);
        Assert.StartsWith("Amount to exchange is below", below);
    }

    [Fact]
    public void An_exolix_order_gives_the_deposit_address_and_memo_and_a_refusal_its_reason()
    {
        var (order, error) = ExolixSwapClient.ParseOrder("""
            {"id":"ee9f1a0bd8b8","amount":0.5,"amountTo":0.0031,"coinFrom":{"coinCode":"XMR","network":"XMR"},
             "coinTo":{"coinCode":"BTC","network":"BTC"},"depositAddress":"84ABC...","depositExtraId":null,
             "withdrawalAddress":"bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh","status":"wait"}
            """);
        Assert.Null(error);
        Assert.Equal("ee9f1a0bd8b8", order!.Id);
        Assert.Equal("84ABC...", order.DepositAddress);
        Assert.Null(order.DepositExtraId);
        Assert.Equal(0.5m, order.Amount);

        // exolix.com, POST /transactions with a malformed address, 2026-10-01.
        var (_, invalid) = ExolixSwapClient.ParseOrder("""{"error":"Invalid withdrawal address"}""");
        Assert.Equal("Invalid withdrawal address", invalid);
        var (_, fields) = ExolixSwapClient.ParseOrder("""{"errors":{"amount":"Amount is too small"}}""");
        Assert.Equal("Amount is too small", fields);
    }

    [Theory]
    [InlineData("PENDING_DEPOSIT", "swap.st.waiting", false)]
    [InlineData("wait", "swap.st.waiting", false)]
    [InlineData("KNOWN_DEPOSIT_TX", "swap.st.seen", false)]
    [InlineData("exchanging", "swap.st.processing", false)]
    [InlineData("SUCCESS", "swap.st.done", true)]
    [InlineData("success", "swap.st.done", true)]
    [InlineData("REFUNDED", "swap.st.refunded", true)]
    [InlineData("overdue", "swap.st.failed", true)]
    public void Each_routes_state_word_maps_onto_the_screens(string raw, string key, bool ended)
    {
        Assert.Equal((key, ended), MainViewModel.SwapStatusKey(raw));
    }
}
