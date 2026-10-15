using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Solana history used to be the twelve newest transactions from one server, with nobody named on the
/// other side. These pin the parts that decide what is fetched and what a row says.
/// </summary>
public sealed class SolanaHistoryTests
{
    private const string Signatures = """
        {"jsonrpc":"2.0","id":1,"result":[
          {"signature":"sigNew","slot":5,"err":null,"blockTime":1700000400},
          {"signature":"sigFailed","slot":4,"err":{"InstructionError":[0,"Custom"]},"blockTime":1700000300},
          {"signature":"sigKnown","slot":3,"err":null,"blockTime":1700000200},
          {"signature":"sigOld","slot":2,"err":null,"blockTime":1700000100}
        ]}
        """;

    [Fact]
    public void Only_successful_transactions_not_read_yet_are_fetched()
    {
        var wanted = OnChainHistoryClient.SolanaSignaturesToRead(
            Signatures, new HashSet<string> { "sigKnown" }, maxNew: 40);

        // Not the failed one (it moved nothing but a fee) and not the one already kept.
        Assert.Equal(new[] { "sigNew", "sigOld" }, wanted);
    }

    [Fact]
    public void One_read_fetches_at_most_its_limit_newest_first()
    {
        var wanted = OnChainHistoryClient.SolanaSignaturesToRead(Signatures, known: null, maxNew: 2);

        Assert.Equal(new[] { "sigNew", "sigKnown" }, wanted);
    }

    [Fact]
    public void An_error_page_is_not_an_empty_history()
    {
        // Null sends the reader to the next server; an empty list would have been "this wallet has no
        // transactions", which is how a rate limit used to read.
        Assert.Null(OnChainHistoryClient.SolanaSignaturesToRead(
            """{"jsonrpc":"2.0","error":{"code":429,"message":"Too many requests"},"id":1}""", null, 40));
        Assert.Empty(OnChainHistoryClient.SolanaSignaturesToRead("""{"jsonrpc":"2.0","id":1,"result":[]}""", null, 40)!);
    }

    [Fact]
    public void A_row_names_the_other_side_of_the_transfer()
    {
        // A small send from the wallet (the fee payer), and its earlier receipt from the same counterparty
        // (who paid that fee). Made-up accounts and amounts, in the shape the node answers with.
        const string me = "MEsolWallet";
        const string json = """
            [
              {"jsonrpc":"2.0","id":0,"result":{"blockTime":1700000200,
                "meta":{"err":null,"fee":5000,"preBalances":[2000000,9000000,1],"postBalances":[1993500,9001500,1]},
                "transaction":{"message":{"accountKeys":["MEsolWallet","OTHERsol","11111111111111111111111111111111"]},"signatures":["sigSend"]}}},
              {"jsonrpc":"2.0","id":1,"result":{"blockTime":1700000100,
                "meta":{"err":null,"fee":5000,"preBalances":[11005000,0,1],"postBalances":[9000000,2000000,1]},
                "transaction":{"message":{"accountKeys":["OTHERsol","MEsolWallet","11111111111111111111111111111111"]},"signatures":["sigReceive"]}}}
            ]
            """;

        var rows = OnChainHistoryClient.ParseSolanaTransactions(json, me);

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Sent", "0.0000015", "OTHERsol"), (rows[0].Kind, rows[0].Amount, rows[0].Counterparty));
        Assert.Equal(("Received", "0.002", "OTHERsol"), (rows[1].Kind, rows[1].Amount, rows[1].Counterparty));
    }
}
