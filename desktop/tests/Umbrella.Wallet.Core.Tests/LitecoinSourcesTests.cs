using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Litecoin had one source, litecoinspace, for its balance, its fee, its broadcast and its history. In
/// October 2026 it stopped answering address lookups (Cloudflare 522/502 after twenty seconds each) and
/// every Litecoin balance read "unknown". It now has Dogecoin's arrangement - Bitcore, then BlockCypher,
/// with litecoinspace behind them - while a server the user chose stays the only one asked.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class LitecoinSourcesTests : IDisposable
{
    private readonly string? _suiteOverride = ChainEndpoints.OverrideFor("LTC");

    public void Dispose() => ChainEndpoints.SetOverride("LTC", _suiteOverride);

    [Fact]
    public void With_no_choice_made_three_sources_stand_in_for_each_other()
    {
        ChainEndpoints.SetOverride("LTC", null);

        Assert.IsType<FailoverUtxoExplorer>(FailoverUtxoExplorer.ForLitecoin());
        Assert.Equal("ltc", BlockCypherUtxoExplorer.CoinFor("LTC"));
        Assert.Equal("https://api.bitcore.io/api/LTC/mainnet", BitcoreUtxoExplorer.BaseFor("LTC"));
        // Fee and broadcast: BlockCypher (with Bitcore behind it in the sender), not litecoinspace.
        Assert.Equal("https://api.blockcypher.com/v1/ltc/main", BitcoinTransactionSender.ExplorerFor("LTC"));
    }

    [Fact]
    public void A_server_the_user_chose_is_the_only_one_asked()
    {
        ChainEndpoints.SetOverride("LTC", "https://ltc.example.org/api");

        Assert.IsType<EsploraUtxoExplorer>(FailoverUtxoExplorer.ForLitecoin());
        Assert.Equal("https://ltc.example.org/api", BitcoinTransactionSender.ExplorerFor("LTC"));
    }

    [Fact]
    public void Bitcoin_has_bitcore_behind_its_esploras_unless_the_user_chose_a_server()
    {
        var suiteBtc = ChainEndpoints.OverrideFor("BTC");
        try
        {
            ChainEndpoints.SetOverride("BTC", null);
            Assert.IsType<FailoverUtxoExplorer>(FailoverUtxoExplorer.ForBitcoin());
            Assert.Equal("https://api.bitcore.io/api/BTC/mainnet", BitcoreUtxoExplorer.BaseFor("BTC"));

            ChainEndpoints.SetOverride("BTC", "https://btc.example.org/api");
            Assert.IsType<EsploraUtxoExplorer>(FailoverUtxoExplorer.ForBitcoin());
        }
        finally
        {
            ChainEndpoints.SetOverride("BTC", suiteBtc);
        }
    }

    [Fact]
    public void Litecoin_history_is_read_from_blockcypher_refs()
    {
        var page = """
            {"txrefs":[
              {"tx_hash":"aa","tx_input_n":-1,"tx_output_n":0,"value":150000000,"confirmed":"2026-10-07T10:00:00Z"},
              {"tx_hash":"bb","tx_input_n":0,"tx_output_n":-1,"value":50000000,"confirmed":"2026-10-07T11:00:00Z"}
            ]}
            """;

        var rows = CoinHistoryClient.ParseBlockcypherRefs([page], "LTC", h => $"https://live.blockcypher.com/ltc/tx/{h}");

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Kind == "Received" && r.Amount == "1.5" && r.Asset == "LTC");
        Assert.Contains(rows, r => r.Kind == "Sent" && r.Amount == "0.5");
    }
}
