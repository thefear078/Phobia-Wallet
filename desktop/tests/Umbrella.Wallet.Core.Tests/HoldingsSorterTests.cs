using Umbrella.Wallet.App;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Pins the Holdings ordering: value descending, 24h change descending, name A→Z, and an unknown /
/// "Default" sort puts held coins first by value and keeps the catalog order for the rest (stable).
/// </summary>
public sealed class HoldingsSorterTests
{
    private static HoldingRowViewModel Row(string symbol, double value, double change) =>
        new(symbol, symbol, "chain", Price: 1, Amount: value, Value: value, Change24h: change,
            Address: "addr", SupportStatus: "Ready");

    private static readonly HoldingRowViewModel[] Sample =
    [
        Row("BTC", value: 100, change: -2),
        Row("ETH", value: 300, change: 5),
        Row("ADA", value: 50, change: 1),
    ];

    [Fact]
    public void By_value_is_biggest_first()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByValue).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ETH", "BTC", "ADA" }, order);
    }

    [Fact]
    public void By_change_is_best_mover_first()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByChange).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ETH", "ADA", "BTC" }, order);
    }

    [Fact]
    public void By_name_is_alphabetical()
    {
        var order = HoldingsSorter.Order(Sample, HoldingsSorter.ByName).Select(r => r.Symbol).ToArray();
        Assert.Equal(new[] { "ADA", "BTC", "ETH" }, order);
    }

    [Fact]
    public void Default_puts_what_you_hold_first_and_keeps_the_catalog_order_for_the_rest()
    {
        HoldingRowViewModel[] rows =
        [
            Row("BTC", value: 0, change: 0),
            Row("ETH", value: 0, change: 0),
            Row("USDT", value: 8000, change: 0),
            Row("LTC", value: 0, change: 0),
            Row("XRP", value: 3000, change: 0),
        ];
        var expected = new[] { "USDT", "XRP", "BTC", "ETH", "LTC" };
        Assert.Equal(expected, HoldingsSorter.Order(rows, HoldingsSorter.Default).Select(r => r.Symbol).ToArray());
        Assert.Equal(expected, HoldingsSorter.Order(rows, "whatever").Select(r => r.Symbol).ToArray());
        Assert.Equal(expected, HoldingsSorter.Order(rows, null).Select(r => r.Symbol).ToArray());
    }
}
