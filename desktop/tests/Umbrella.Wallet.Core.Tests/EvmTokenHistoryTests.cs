using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Token history on the EVM networks (roadmap N.15): the transfers of the tokens Holdings shows, read
/// per network — an Etherscan-shaped <c>tokentx</c> list where one answers without a key, 3xpl's ERC-20
/// module otherwise. Fixtures keep the shape of real answers (2026-10-16) with made-up addresses and ids.
/// </summary>
public sealed class EvmTokenHistoryTests
{
    private const string Me = "0x1111111111111111111111111111111111111111";
    private const string Other = "0x2222222222222222222222222222222222222222";
    private const string UsdcArb = "0xaf88d065e77c8cc2239327c5edb3a432268e5831";
    private const string FakeUsdt = "0x9999999999999999999999999999999999999999";

    private static readonly IReadOnlyList<OnChainHistoryClient.HeldToken> Held =
    [
        new("0xAF88d065e77c8cC2239327C5EDb3A432268e5831", "USDC", 6),
    ];

    // ---- Etherscan-shaped tokentx -------------------------------------------------------------------------

    private static string Tokentx(params string[] items) => $$"""{"status":"1","message":"OK","result":[{{string.Join(',', items)}}]}""";

    private static string Item(string hash, string from, string to, string value, string contract, string symbol = "USDC", string decimals = "6") =>
        $$"""{"hash":"{{hash}}","timeStamp":"1792123835","from":"{{from}}","to":"{{to}}","value":"{{value}}","contractAddress":"{{contract}}","tokenSymbol":"{{symbol}}","tokenDecimal":"{{decimals}}"}""";

    [Fact]
    public void A_transfer_in_and_out_are_rows_with_the_other_side_named()
    {
        var json = Tokentx(
            Item("0xaa01", Other, Me, "12500000", UsdcArb),
            Item("0xaa02", Me, Other, "3000000", UsdcArb));

        var rows = OnChainHistoryClient.ParseEvmTokenTx(json, Me, Held, "https://arbiscan.io/tx/");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Received", "USDC", "12.5", Other, "https://arbiscan.io/tx/0xaa01"),
            (rows[0].Kind, rows[0].Asset, rows[0].Amount, rows[0].Counterparty, rows[0].Explorer));
        Assert.Equal(("Sent", "3", Other), (rows[1].Kind, rows[1].Amount, rows[1].Counterparty));
        Assert.Equal(1792123835000, rows[0].UnixMs);
    }

    [Fact]
    public void A_look_alike_token_never_becomes_a_row()
    {
        // Address poisoning's tool: a token calling itself USDT, on its own contract, "sent" to the wallet.
        var json = Tokentx(Item("0xbb01", Other, Me, "1000000000000", FakeUsdt, "USDT"));

        Assert.Empty(OnChainHistoryClient.ParseEvmTokenTx(json, Me, Held, "https://arbiscan.io/tx/"));
    }

    [Fact]
    public void The_decimals_are_the_wallets_not_the_answers()
    {
        // An answer claiming 18 decimals for a 6-decimal token would shrink the amount a trillion times.
        var json = Tokentx(Item("0xcc01", Other, Me, "2000000", UsdcArb, decimals: "18"));

        Assert.Equal("2", OnChainHistoryClient.ParseEvmTokenTx(json, Me, Held, "x/").Single().Amount);
    }

    [Fact]
    public void A_transfer_to_oneself_or_between_others_moves_nothing_here()
    {
        var json = Tokentx(
            Item("0xdd01", Me, Me, "5000000", UsdcArb),
            Item("0xdd02", Other, "0x3333333333333333333333333333333333333333", "5000000", UsdcArb));

        Assert.Empty(OnChainHistoryClient.ParseEvmTokenTx(json, Me, Held, "x/"));
    }

    [Fact]
    public void A_refusal_in_the_envelope_is_no_rows()
    {
        Assert.Empty(OnChainHistoryClient.ParseEvmTokenTx("""{"status":"0","message":"NOTOK","result":"Max rate limit reached"}""", Me, Held, "x/"));
    }

    // ---- 3xpl's ERC-20 module -----------------------------------------------------------------------------

    private static string Events(params string[] items) =>
        """{"data":{"events":{"arbitrum-one-erc-20":[""" + string.Join(',', items) + """]}},"context":{"code":200}}""";

    // The slash is escaped in the real answer ("arbitrum-one-erc-20\/0x…"), so the fixture escapes it too.
    private static string Event(string tx, string effect, string contract, bool failed = false) =>
        $$"""{"block":515358246,"transaction":"{{tx}}","sort_key":0,"time":"2026-10-16T11:13:12Z","currency":"arbitrum-one-erc-20\/{{contract}}","effect":"{{effect}}","failed":{{(failed ? "true" : "false")}},"extra":null,"extra_indexed":null}""";

    [Fact]
    public void Events_become_one_row_per_transaction_and_token()
    {
        var json = Events(
            Event("0xee01", "+7000000", UsdcArb),
            Event("0xee02", "-1500000", UsdcArb),
            Event("0xee02", "-500000", UsdcArb));

        var rows = OnChainHistoryClient.ParseEvmTokenEvents(json, "arbitrum-one-erc-20", Held, "https://arbiscan.io/tx/");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Received", "7"), (rows[0].Kind, rows[0].Amount));
        Assert.Equal(("Sent", "2", "https://arbiscan.io/tx/0xee02"), (rows[1].Kind, rows[1].Amount, rows[1].Explorer));
        Assert.Equal(string.Empty, rows[1].Counterparty);   // the events do not name it; nothing is guessed
        Assert.Equal(DateTimeOffset.Parse("2026-10-16T11:13:12Z").ToUnixTimeMilliseconds(), rows[0].UnixMs);
    }

    [Fact]
    public void Failed_unknown_and_net_zero_events_are_left_out()
    {
        var json = Events(
            Event("0xff01", "+9000000", UsdcArb, failed: true),
            Event("0xff02", "+9000000", FakeUsdt),
            Event("0xff03", "+4000000", UsdcArb),
            Event("0xff03", "-4000000", UsdcArb));

        Assert.Empty(OnChainHistoryClient.ParseEvmTokenEvents(json, "arbitrum-one-erc-20", Held, "x/"));
    }

    [Fact]
    public void Another_modules_answer_is_not_read_as_this_one()
    {
        Assert.Empty(OnChainHistoryClient.ParseEvmTokenEvents(Events(Event("0x01", "+1", UsdcArb)), "polygon-erc-20", Held, "x/"));
    }

    // ---- coverage ------------------------------------------------------------------------------------------

    [Fact]
    public void Every_stablecoin_network_has_a_token_history_but_bnb_chain()
    {
        // BNB Chain: 3xpl keeps no BEP-20 module and BscScan wants a key. The Activity screen names those
        // tokens as not read — this pins that it is the only network left out.
        var without = PublicChainBalanceClient.EvmStablecoins
            .Select(t => t.Network)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !OnChainHistoryClient.HasEvmTokenHistory(n))
            .ToList();

        Assert.Equal(["BSC"], without);
        Assert.True(OnChainHistoryClient.HasEvmTokenHistory("Ethereum"));
    }

    [Fact]
    public void Each_token_source_links_to_the_explorer_a_send_there_is_linked_to()
    {
        foreach (var (network, _, _, explorer) in OnChainHistoryClient.EvmTokenHistory)
        {
            var chain = EthTransactionSender.Chains.Values.Single(c => c.Name.Equals(network, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("https://" + chain.ExplorerTx, explorer);
        }
    }
}
