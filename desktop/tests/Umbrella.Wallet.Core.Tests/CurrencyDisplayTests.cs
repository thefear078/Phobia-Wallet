using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Money on screen in the currency it says it is in.
///
/// Reported from a real wallet (2026-10-07): with CNY chosen, 104 USDT read "¥104,32" and BTC "¥83 910" —
/// dollar figures under the yuan's sign. The rate had failed to load (Tor still starting, the
/// kill-switch holding), the failure was taken as a rate of 1.0, and it was never asked for again. The
/// coin chart, meanwhile, labelled its axis in dollars beside a list in yuan.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class CurrencyDisplayTests : IDisposable
{
    private readonly decimal _rate = Fx.Rate;
    private readonly string _symbol = Fx.Symbol;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-fx-{Guid.NewGuid():N}");

    public CurrencyDisplayTests() => Fx.SetLanguage("en");

    public void Dispose()
    {
        Fx.Use("USD", 1m);
        (Fx.Rate, Fx.Symbol) = (_rate, _symbol);
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void Without_a_rate_the_figures_stay_in_dollars_and_say_so()
    {
        Fx.Use("CNY", null);

        Assert.Equal("USD", Fx.Code);
        Assert.Equal("$104.32", Fx.Money(104.32));
    }

    [Fact]
    public void With_a_rate_symbol_and_amount_change_together()
    {
        Fx.Use("CNY", 7.1m);

        Assert.Equal("CNY", Fx.Code);
        Assert.Equal("¥710.00", Fx.Money(100));
    }

    [Fact]
    public void A_currency_the_wallet_does_not_list_is_shown_in_dollars()
    {
        Fx.Use("XYZ", 3m);
        Assert.Equal("USD", Fx.Code);
        Assert.Equal(1m, Fx.Rate);
    }

    [Fact]
    public void The_chart_labels_its_prices_in_the_shown_currency()
    {
        Fx.Use("CNY", 7m);
        Assert.Equal("700.00", MainViewModel.FormatPrice(100));
        Assert.Equal("0.7000", MainViewModel.FormatPrice(0.1));
        Assert.Equal("587,377", MainViewModel.FormatPrice(83_911));   // whole units past 100 000
        Assert.Equal("—", MainViewModel.FormatPrice(double.NaN));
    }

    [Fact]
    public void Saved_rates_are_there_at_the_next_start_and_a_stale_week_is_not()
    {
        var path = Path.Combine(_dir, "fx-rates.json");
        new FxRateCache(path).Save(new Dictionary<string, decimal> { ["CNY"] = 7.12m, ["UAH"] = 41.3m });

        var reopened = new FxRateCache(path);
        Assert.Equal(7.12m, reopened.RateFor("CNY"));
        Assert.Equal(1m, reopened.RateFor("USD"));
        Assert.Null(reopened.RateFor("EUR"));

        var old = DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeSeconds();
        File.WriteAllText(path, "{\"At\":" + old + ",\"Rates\":{\"CNY\":7.12}}");
        Assert.Null(new FxRateCache(path).RateFor("CNY"));
    }

    [Fact]
    public void A_rates_answer_is_read_and_a_broken_one_is_nothing_not_one()
    {
        var rates = PublicMarketRatesClient.ParseFiatRates(
            """{"result":"success","base_code":"USD","rates":{"USD":1,"CNY":7.1203,"UAH":41.28,"BAD":0,"EUR":0.92}}""");
        Assert.NotNull(rates);
        Assert.Equal(7.1203m, rates!["CNY"]);
        Assert.False(rates.ContainsKey("BAD"));

        Assert.Null(PublicMarketRatesClient.ParseFiatRates("""{"result":"error","error-type":"quota-reached"}"""));
        Assert.Null(PublicMarketRatesClient.ParseFiatRates("<html>blocked</html>"));
    }

    [Fact]
    public void The_day_s_move_is_now_against_a_day_ago_not_today_times_the_average()
    {
        // $110 now after +10%: it was $100, so the move is +$10 and +10% — not +$11.
        var (pct, move) = MainViewModel.Move24h([(110, 10)]);
        Assert.Equal(10, pct, 6);
        Assert.Equal(10, move, 6);

        // One coin up 10%, one down 10%, $110 and $90 now: they were $100 each, so the wallet is flat.
        (pct, move) = MainViewModel.Move24h([(110, 10), (90, -10)]);
        Assert.Equal(0, pct, 6);
        Assert.Equal(0, move, 6);

        Assert.Equal((0d, 0d), MainViewModel.Move24h([]));
    }

    // --- tokens are priced on their contract, not their ticker ------------------------------------------

    [Theory]
    [InlineData("TRON", "USDT", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", TokenIdentityVerdict.Genuine)]
    [InlineData("TRON", "USDT", "TXyz1111111111111111111111111111aa", TokenIdentityVerdict.Impersonation)]
    // Base58: case is part of a TRON address.
    [InlineData("TRON", "USDT", "tr7nhqjekqxgtci8q8zy4pl8otszgjlj6t", TokenIdentityVerdict.Impersonation)]
    [InlineData("Ethereum", "USDC", "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", TokenIdentityVerdict.Genuine)]
    [InlineData("Ethereum", "USDC", "0x0000000000000000000000000000000000000001", TokenIdentityVerdict.Impersonation)]
    [InlineData("Ethereum", "ETH", "0x0000000000000000000000000000000000000002", TokenIdentityVerdict.Impersonation)]
    [InlineData("TRON", "TRX", "TAbc", TokenIdentityVerdict.Impersonation)]
    [InlineData("Ethereum", "BTC", "0x0000000000000000000000000000000000000003", TokenIdentityVerdict.Impersonation)]
    [InlineData("Ethereum", "LINK", "0x514910771af9ca656af840dff83e8264ecf986ca", TokenIdentityVerdict.Unknown)]
    [InlineData("TON", "USDT", "0:b113a994b5024a16719f69139328eb759596c38a25f59028b146fecdc3621dfe", TokenIdentityVerdict.Unknown)]
    public void A_ticker_is_trusted_only_on_its_real_contract(string network, string symbol, string contract,
        TokenIdentityVerdict expected)
    {
        Assert.Equal(expected, TokenIdentity.Judge(network, symbol, contract));
    }
}
