using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// ATOM and DOT history, read from answers shaped like the real ones (taken on 2026-10-08 from
/// CryptoCrew's Cosmos Hub archive node and from Statescan for a real Polkadot account).
/// </summary>
public sealed class CosmosPolkadotHistoryTests
{
    private const string Me = "cosmos19rl4cm2hmr8afy4kldpxz3fka4jguq0auqdal4";

    private static string MultiSendSpam()
    {
        // The real one paid 2000 addresses 1 uatom each, all of them starting "cosmos19r…" like this
        // wallet's own — address poisoning — with a memo advertising a "claim" site.
        var outputs = string.Join(",", Enumerable.Range(0, 11)
            .Select(i => $$$"""{"address":"cosmos19r7q{{{i:D2}}}fhndultexylzv3n5vjgn3gyl0y2v3kkjc","coins":[{"denom":"uatom","amount":"1"}]}""")
            .Append($$$"""{"address":"{{{Me}}}","coins":[{"denom":"uatom","amount":"1"}]}"""));
        return $$$"""
            {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgMultiSend",
              "inputs":[{"address":"cosmos1lgars47nzhfzt0mqsjrdzrx2zcg7c2skxcayt2","coins":[{"denom":"uatom","amount":"12"}]}],
              "outputs":[{{{outputs}}}]}],
              "memo":"You have been allocated 50,000 $ATOM Staking Rewards, Claim now at …"}}
            """;
    }

    private static string CosmosPage() => $$$"""
        {"txs":[
          {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgSend","from_address":"{{{Me}}}","to_address":"cosmos1q3ttkjvntv0qw38q2vseenhrcu694wn7qzn3p0","amount":[{"denom":"uatom","amount":"644816"}]}]}},
          {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgSend","from_address":"cosmos1gugn5lxuvllks27qrlj053ca8tqw973n09l77c","to_address":"{{{Me}}}","amount":[{"denom":"uatom","amount":"645415"}]}]}},
          {{{MultiSendSpam()}}},
          {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgSend","from_address":"cosmos16a87dpmkutsfx7cddxt749urv62nkcajmr8nw2","to_address":"{{{Me}}}","amount":[{"denom":"uatom","amount":"1"}]}]}},
          {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgSend","from_address":"cosmos1gugn5lxuvllks27qrlj053ca8tqw973n09l77c","to_address":"{{{Me}}}","amount":[{"denom":"uatom","amount":"5000000"}]}]}},
          {"body":{"messages":[{"@type":"/ibc.applications.transfer.v1.MsgTransfer","sender":"{{{Me}}}","receiver":"osmo1xyz","token":{"denom":"uatom","amount":"2500000"}}]}},
          {"body":{"messages":[{"@type":"/cosmos.bank.v1beta1.MsgSend","from_address":"{{{Me}}}","to_address":"cosmos1q3ttkjvntv0qw38q2vseenhrcu694wn7qzn3p0","amount":[{"denom":"ibc/27394FB092D2ECCD56123C74F36E4C1F926001CEADA9CA97EA622B25F41E5EB2","amount":"10"}]}]}}
        ],
        "tx_responses":[
          {"txhash":"6E044ACF51516D2AFB021ACD37BA64337FCCD7DC2A59E4A4FD4BE2BCC3CD8905","height":"28373891","code":0,"timestamp":"2025-11-11T19:49:28Z"},
          {"txhash":"834B7CD186DB3548CB97D752212E4B1AF2D7A72BED59C69F783FE8F3B50C2CBF","height":"28373889","code":0,"timestamp":"2025-11-11T19:49:19Z"},
          {"txhash":"5A98EAB9D2A66CC20A5573F63FBF3C421EB75BF7B39BB961D174C91CE439A922","height":"28708703","code":0,"timestamp":"2025-12-04T19:45:04Z"},
          {"txhash":"78BD2994878567275AEE86A1401671DF4AF20BC925A769B1D9148C3C96B70340","height":"27764687","code":0,"timestamp":"2025-10-01T01:17:05Z"},
          {"txhash":"FA11ED0000000000000000000000000000000000000000000000000000000000","height":"27000000","code":5,"timestamp":"2025-09-01T00:00:00Z"},
          {"txhash":"1BC0000000000000000000000000000000000000000000000000000000000000","height":"27000001","code":0,"timestamp":"2025-09-02T00:00:00Z"},
          {"txhash":"70CE000000000000000000000000000000000000000000000000000000000000","height":"27000002","code":0,"timestamp":"2025-09-03T00:00:00Z"}
        ],
        "total":"7"}
        """;

    [Fact]
    public void Atom_sends_and_receipts_are_read_with_the_link_the_send_screen_stores()
    {
        var rows = AccountHistoryClient.ParseCosmos(CosmosPage(), Me);

        var sent = Assert.Single(rows, r => r.Hash.StartsWith("6E044ACF", StringComparison.Ordinal));
        Assert.Equal("Sent", sent.Kind);
        Assert.Equal("ATOM", sent.Asset);
        Assert.Equal("0.644816", sent.Amount);
        Assert.Equal("cosmos1q3ttkjvntv0qw38q2vseenhrcu694wn7qzn3p0", sent.Counterparty);
        Assert.Equal(DateTimeOffset.Parse("2025-11-11T19:49:28Z").ToUnixTimeMilliseconds(), sent.UnixMs);
        Assert.Equal($"https://www.mintscan.io/cosmos/tx/{sent.Hash}", sent.Explorer);

        var received = Assert.Single(rows, r => r.Hash.StartsWith("834B7CD1", StringComparison.Ordinal));
        Assert.Equal("Received", received.Kind);
        Assert.Equal("0.645415", received.Amount);
        Assert.Equal("cosmos1gugn5lxuvllks27qrlj053ca8tqw973n09l77c", received.Counterparty);

        var ibc = Assert.Single(rows, r => r.Hash.StartsWith("1BC0", StringComparison.Ordinal));
        Assert.Equal("Sent", ibc.Kind);
        Assert.Equal("2.5", ibc.Amount);
        Assert.Equal("osmo1xyz", ibc.Counterparty);
    }

    [Fact]
    public void Poisoning_airdrops_failed_transactions_and_other_tokens_are_left_out()
    {
        var rows = AccountHistoryClient.ParseCosmos(CosmosPage(), Me);

        Assert.DoesNotContain(rows, r => r.Hash.StartsWith("5A98", StringComparison.Ordinal));   // 1 uatom among twelve
        Assert.DoesNotContain(rows, r => r.Hash.StartsWith("FA11ED", StringComparison.Ordinal)); // code 5: failed
        Assert.DoesNotContain(rows, r => r.Hash.StartsWith("70CE", StringComparison.Ordinal));   // an IBC token, not ATOM
        // A single tiny transfer to this address alone is not a mass airdrop: it stays.
        var tiny = Assert.Single(rows, r => r.Hash.StartsWith("78BD2994", StringComparison.Ordinal));
        Assert.Equal("0.000001", tiny.Amount);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void An_answer_that_is_not_a_transaction_page_is_no_history() =>
        Assert.Empty(AccountHistoryClient.ParseCosmos("""{"code":3,"message":"pruned"}""", Me));

    private const string Dot = "15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5";

    // Statescan's Asset Hub transfers for that account (trimmed), and the matching extrinsics.
    private const string DotTransfers = """
        {"items":[
          {"indexer":{"blockHeight":19952625,"blockTime":1787850432000,"eventIndex":5,"extrinsicIndex":2},"from":"15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5","to":"13rF2eVNskZuALw4DDtZqUqJytvNqoVFEfT12mKM7hKFGWjr","balance":"20138353636","isSigned":true,"isNativeAsset":true},
          {"indexer":{"blockHeight":19946602,"blockTime":1787837256000,"eventIndex":3,"extrinsicIndex":2},"from":"13oYgShqHzpsbxFtpv8KMzhxHKft5q7NqQEiK3nwAhGKVTBp","to":"15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5","balance":"30155893800","isSigned":true,"isNativeAsset":true},
          {"indexer":{"blockHeight":19946000,"blockTime":1787830000000,"eventIndex":4,"extrinsicIndex":3},"from":"13oYgShqHzpsbxFtpv8KMzhxHKft5q7NqQEiK3nwAhGKVTBp","to":"15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5","balance":"5000000","isSigned":true,"isNativeAsset":false,"assetId":1984},
          {"indexer":{"blockHeight":19945000,"blockTime":1787820000000,"eventIndex":4,"extrinsicIndex":3},"from":"15oF4uVJwmo4TdGW7VfQxNLavjCXviqxT9S1MgbjMNHr6Sp5","to":"11WUfznvuSt23qLC4pbEUuCxmznNKwAiuaC4PhURrFyBb2D","balance":"0","isSigned":true,"isNativeAsset":true}
        ],"page":0,"pageSize":25,"total":4}
        """;

    private const string DotExtrinsics = """
        {"items":[
          {"hash":"0x062446f22efae4c9e33a08a04dd9d9c1d2a64b7733fd741a44665344118d1113","indexer":{"blockHeight":19952625,"blockTime":1787850432000,"extrinsicIndex":2},"section":"balances","method":"transferAll","isSuccess":true}
        ],"page":0,"pageSize":25,"total":1}
        """;

    [Fact]
    public void Dot_transfers_are_read_and_a_send_links_by_the_hash_it_was_submitted_under()
    {
        var hashes = AccountHistoryClient.ParseStatescanHashes(DotExtrinsics);
        var rows = AccountHistoryClient.ParsePolkadot(DotTransfers, Dot, hashes, "https://assethub-polkadot.subscan.io");

        Assert.Equal(2, rows.Count);   // USDT on Asset Hub and a transfer of nothing are left out

        var sent = Assert.Single(rows, r => r.Kind == "Sent");
        Assert.Equal("DOT", sent.Asset);
        Assert.Equal("2.0138353636", sent.Amount);
        Assert.Equal("13rF2eVNskZuALw4DDtZqUqJytvNqoVFEfT12mKM7hKFGWjr", sent.Counterparty);
        Assert.Equal(1787850432000, sent.UnixMs);
        Assert.Equal("https://assethub-polkadot.subscan.io/extrinsic/0x062446f22efae4c9e33a08a04dd9d9c1d2a64b7733fd741a44665344118d1113", sent.Explorer);

        var received = Assert.Single(rows, r => r.Kind == "Received");
        Assert.Equal("3.01558938", received.Amount);
        Assert.Equal("https://assethub-polkadot.subscan.io/extrinsic/19946602-2", received.Explorer);
    }
}
