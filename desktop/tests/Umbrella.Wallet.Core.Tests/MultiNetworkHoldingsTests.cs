using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A coin is one row whatever networks it sits on: USDT on TRON and on Polygon is one Tether row with
/// the total and both networks named; a token on a single network is shown as the coin, its network
/// underneath; a watched address keeps its own row.
/// </summary>
public class MultiNetworkHoldingsTests
{
    private static HoldingRowViewModel Row(string symbol, string name, string chain, double amount, string status = "Ready") =>
        new(symbol, name, chain, 1, amount, amount, 0, "addr", status);

    [Fact]
    public void ACoinOnSeveralNetworksIsOneRowWithTheTotal()
    {
        var rows = MainViewModel.AggregateAcrossNetworks(
        [
            Row("USDT", "Tether USD · TRC20", "TRON", 100),
            Row("USDT", "Tether USD · Polygon", "Polygon", 25),
            Row("BTC", "Bitcoin", "Bitcoin", 0.5),
        ]);

        var usdt = Assert.Single(rows, r => r.Symbol == "USDT");
        Assert.Equal(125, usdt.Amount);
        Assert.Equal("Tether USD", usdt.Name);
        Assert.Equal("TRON · Polygon", usdt.Networks);
        Assert.Equal("USDT · TRON · Polygon", usdt.SymbolLine);
        Assert.Single(rows, r => r.Symbol == "BTC");
    }

    [Fact]
    public void ATokenOnOneNetworkIsTheCoinWithItsNetworkUnderneath()
    {
        var usdt = Assert.Single(MainViewModel.AggregateAcrossNetworks([Row("USDT", "Tether USD · TRC20", "TRON", 10)]));
        Assert.Equal("Tether USD", usdt.Name);
        Assert.Equal("USDT · TRON", usdt.SymbolLine);
    }

    [Fact]
    public void AWatchedAddressKeepsItsOwnRow()
    {
        var rows = MainViewModel.AggregateAcrossNetworks(
        [
            Row("USDT", "Tether USD · TRC20", "TRON", 10),
            Row("USDT", "Ledger", "TRON", 5, status: "Watch"),
        ]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void EveryStablecoinNetworkIsOneTheSignerKnows()
    {
        // A token row the wallet shows must be sendable on its own network: every network in the list is
        // one the EVM signer has a chain id and servers for.
        Assert.All(PublicChainBalanceClient.EvmStablecoins, t =>
        {
            Assert.True(EthTransactionSender.Chains.ContainsKey(t.ChainKey), t.ChainKey);
            Assert.StartsWith("0x", t.Contract);
            Assert.Equal(42, t.Contract.Length);
            Assert.True(t.Decimals is 6 or 18);
        });
    }
}
