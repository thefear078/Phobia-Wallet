using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// History for Nano, Decred, Dogecoin and transparent Zcash, read from answers shaped like the real ones
/// (captured from rpc.nano.to, dcrdata, BlockCypher, Blockchair and 3xpl on 2026-10-01, trimmed).
///
/// The UTXO rule is the one that matters: a transaction's effect on the WALLET is what it paid to the
/// wallet's addresses minus what it spent from them. Read per address, a payment with change looks like
/// a send of everything plus an unrelated receive.
/// </summary>
public sealed class CoinHistoryTests
{
    [Fact]
    public void Nano_lists_confirmed_sends_and_receives_in_xno()
    {
        const string json = """
            {"account":"nano_1natrium1o3z5519ifou7xii8crpxpk8y65qmkih8e8bpsjri651oza8imdd","history":[
             {"type":"receive","account":"nano_3utf4adfpnzka4ghsx835waja4k1iygt1a4dghogduwkthewfzincrujxrzq","amount":"10000000000000000000000000000","local_timestamp":"1789466566","height":"3223","hash":"6D7FC58CAC10E970E71FE266D6648B4DF563040037B4EB6BA45C28596CF515E1","confirmed":"true"},
             {"type":"send","account":"nano_1uw3f4f1sgpm7sd1jptri4xphdchhaa6pw67aagygzferhfxrr69nkrmgiw4","amount":"50000000000000000000000000000000","local_timestamp":"1789083743","height":"3222","hash":"CC3E0F8CEE2325D61F717A9897A2B9461C9B8F16FE5FE5568B2A513057E7F892","confirmed":"true"},
             {"type":"receive","account":"nano_3gfn","amount":"4000000000000000000000000","local_timestamp":"1789045888","hash":"D21C","confirmed":"false"}
            ]}
            """;

        var rows = CoinHistoryClient.ParseNano(json);

        Assert.Equal(2, rows.Count);   // the unconfirmed block is not money that moved yet
        Assert.Equal(("Received", "XNO", "0.01"), (rows[0].Kind, rows[0].Asset, rows[0].Amount));
        Assert.Equal(1789466566000, rows[0].UnixMs);
        Assert.Equal("https://nanolooker.com/block/6D7FC58CAC10E970E71FE266D6648B4DF563040037B4EB6BA45C28596CF515E1", rows[0].Explorer);
        Assert.Equal(("Sent", "50"), (rows[1].Kind, rows[1].Amount));
        Assert.StartsWith("nano_1uw3", rows[1].Counterparty);
    }

    private const string DcrMe = "DsiNpSKgyuRWn8csUwaz1G31c5PZUT8zAnT";
    private const string DcrChange = "DsChangeAddressOfThisWallet00000000";

    [Fact]
    public void Decred_nets_change_out_of_a_send_and_names_the_recipient()
    {
        // 1.8617067 DCR in from this wallet; 1.0 to someone else, 0.8616805 back to the wallet's change.
        var json = """
            {"totalItems":2,"from":0,"to":2,"items":[
             {"txid":"d277","vin":[{"addr":"@ME@","valueSat":186170670,"value":1.8617067}],
              "vout":[{"value":1.0,"n":0,"scriptPubKey":{"addresses":["DsbyrRecipient"],"type":"pubkeyhash"}},
                      {"value":0.8616805,"n":1,"scriptPubKey":{"addresses":["@CHANGE@"],"type":"pubkeyhash"}},
                      {"value":0,"n":2,"scriptPubKey":{"type":"nulldata"}}],
              "time":1790881467,"blocktime":1790881467},
             {"txid":"e721","vin":[{"addr":"DsbjkSomeoneElse","value":2.5}],
              "vout":[{"value":1.8617067,"n":0,"scriptPubKey":{"addresses":["@ME@"]}},{"value":0.6382,"n":1,"scriptPubKey":{"addresses":["DsbjkSomeoneElse"]}}],
              "time":1790800000}
            ]}
            """.Replace("@ME@", DcrMe).Replace("@CHANGE@", DcrChange);

        var rows = CoinHistoryClient.ParseInsight(json, [DcrMe, DcrChange], "DCR", h => $"https://dcrdata.decred.org/tx/{h}");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Sent", "DCR", "1.0000262"), (rows[0].Kind, rows[0].Asset, rows[0].Amount));   // 1.0 + the fee
        Assert.Equal("DsbyrRecipient", rows[0].Counterparty);
        Assert.Equal(1790881467000, rows[0].UnixMs);
        Assert.Equal(("Received", "1.8617067", "DsbjkSomeoneElse"), (rows[1].Kind, rows[1].Amount, rows[1].Counterparty));
        Assert.Equal("https://dcrdata.decred.org/tx/e721", rows[1].Explorer);
    }

    [Fact]
    public void Dogecoin_refs_from_two_of_the_wallets_addresses_net_into_one_transaction()
    {
        // tx "aa": 100 DOGE spent from address 1, 30 DOGE change back to address 2 → a 70 DOGE send.
        // tx "bb": 5 DOGE received on address 1.
        const string first = """
            {"address":"D1","txrefs":[
              {"tx_hash":"aa","tx_input_n":0,"tx_output_n":-1,"value":10000000000,"confirmed":"2026-09-22T19:10:21Z"},
              {"tx_hash":"bb","tx_input_n":-1,"tx_output_n":0,"value":500000000,"confirmed":"2026-08-10T20:44:57Z"}]}
            """;
        const string second = """
            {"address":"D2","txrefs":[
              {"tx_hash":"aa","tx_input_n":-1,"tx_output_n":1,"value":3000000000,"confirmed":"2026-09-22T19:10:21Z"}]}
            """;

        var rows = CoinHistoryClient.ParseBlockcypherRefs([first, second], "DOGE", h => $"https://live.blockcypher.com/doge/tx/{h}");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Sent", "70", "aa"), (rows[0].Kind, rows[0].Amount, rows[0].Hash));
        Assert.Equal(("Received", "5", "bb"), (rows[1].Kind, rows[1].Amount, rows[1].Hash));
        Assert.Equal(DateTimeOffset.Parse("2026-09-22T19:10:21Z").ToUnixTimeMilliseconds(), rows[0].UnixMs);
    }

    [Fact]
    public void Zcash_reads_blockchair_balance_changes_and_3xpl_events()
    {
        const string blockchair = """
            {"data":{"t1MKn34KBa8Xh4g8qU8psibBXvURafphVn7":{"address":{"type":"pubkeyhash"},"transactions":[
              {"block_id":3502914,"hash":"a466","time":"2026-10-01 19:10:26","balance_change":125000000},
              {"block_id":3502909,"hash":"21d5","time":"2026-10-01 19:04:41","balance_change":-40000000}]}},
             "context":{"code":200}}
            """;
        var rows = CoinHistoryClient.ParseBlockchairZcash(blockchair, "t1MKn34KBa8Xh4g8qU8psibBXvURafphVn7");
        Assert.Equal(2, rows.Count);
        Assert.Equal(("Received", "ZEC", "1.25"), (rows[0].Kind, rows[0].Asset, rows[0].Amount));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 19, 10, 26, TimeSpan.Zero).ToUnixTimeMilliseconds(), rows[0].UnixMs);
        Assert.Equal(("Sent", "0.4"), (rows[1].Kind, rows[1].Amount));
        Assert.Equal("https://blockchair.com/zcash/transaction/21d5", rows[1].Explorer);

        // 3xpl: two events in one transaction (an input and the change) add up to one row.
        const string xpl = """
            {"data":{"events":{"zcash-main":[
              {"block":3502914,"transaction":"a466","time":"2026-10-01T19:10:26.000000Z","currency":"zcash","effect":"-100000000","failed":false},
              {"block":3502914,"transaction":"a466","time":"2026-10-01T19:10:26.000000Z","currency":"zcash","effect":"+60000000","failed":false},
              {"block":3502909,"transaction":"21d5","time":"2026-10-01T19:04:41.000000Z","currency":"zcash","effect":"+125000000","failed":false}]}}}
            """;
        var events = CoinHistoryClient.Parse3xplZcash(xpl);
        Assert.Equal(2, events.Count);
        Assert.Equal(("Sent", "0.4", "a466"), (events[0].Kind, events[0].Amount, events[0].Hash));
        Assert.Equal(("Received", "1.25"), (events[1].Kind, events[1].Amount));
    }

    [Theory]
    [InlineData("DOGE")]
    [InlineData("ZEC")]
    [InlineData("XNO")]
    [InlineData("DCR")]
    public void The_coins_with_a_reader_are_no_longer_named_as_unread(string symbol)
    {
        Assert.True(ChainCatalog.All.Single(c => c.Symbol == symbol).HasHistory);
        Assert.DoesNotContain(symbol, HistoryCoverage.WithoutHistory([symbol]));
    }
}
