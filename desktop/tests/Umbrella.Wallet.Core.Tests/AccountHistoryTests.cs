using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// History for XRP and Stellar, read from answers shaped like the real ones (captured from
/// xrplcluster.com and horizon.stellar.org, trimmed).
///
/// The rule that matters most is the XRP one: the amount shown is what the ledger says was DELIVERED.
/// A partial payment names a large Amount and delivers a sliver; a history that printed Amount would show
/// the user money that never arrived — the same trick that has emptied exchanges.
/// </summary>
public sealed class AccountHistoryTests
{
    private const string Me = "rEb8TK3gBgk5auZkwc6sHnwrGVJH8DuaLh";
    private const string Them = "rLpvuHZFE46NUyZH5XaMvmYRJZF7aory7t";

    private static string XrpAnswer(string txs) => """{"result":{"account":"@Me@","transactions":[@txs@],"validated":true,"status":"success"}}""".Replace("@Me@", Me).Replace("@txs@", txs);

    private static string XrpPayment(string from, string to, string amount, string delivered, string result = "tesSUCCESS",
        string hash = "9FAF70B356A2DA2F5340CAA878A83AB6FC7275F3BDB9D05C92DD5E65FC0197DE", string type = "Payment") => """
        {"meta":{"TransactionResult":"@result@","delivered_amount":@delivered@},
         "tx":{"TransactionType":"@type@","Account":"@from@","Destination":"@to@","Amount":@amount@,
               "hash":"@hash@","date":843725290},
         "validated":true}
        """.Replace("@result@", result).Replace("@delivered@", delivered).Replace("@type@", type).Replace("@from@", from).Replace("@to@", to).Replace("@amount@", amount).Replace("@hash@", hash);

    [Fact]
    public void An_incoming_xrp_payment_is_read_with_its_sender_and_date()
    {
        var rows = AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Them, Me, "\"20793800\"", "\"20793800\"")), Me);

        var row = Assert.Single(rows);
        Assert.Equal("Received", row.Kind);
        Assert.Equal("XRP", row.Asset);
        Assert.Equal("20.7938", row.Amount);
        Assert.Equal(Them, row.Counterparty);
        // 843725290 seconds after 2000-01-01 is 2026-09-26.
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 8, 8, 10, TimeSpan.Zero).ToUnixTimeMilliseconds(), row.UnixMs);
        Assert.Equal("https://livenet.xrpl.org/transactions/9FAF70B356A2DA2F5340CAA878A83AB6FC7275F3BDB9D05C92DD5E65FC0197DE", row.Explorer);
    }

    [Theory]
    // API v2: tx_json without a date, the close time beside it as seconds…
    [InlineData(""" "date":843725290, """)]
    // …or only as an ISO timestamp (Clio).
    [InlineData(""" "close_time_iso":"2026-09-26T08:08:10Z", """)]
    public void A_newer_api_answer_keeps_the_transactions_time(string closeTime)
    {
        var json = """
            {"result":{"transactions":[
              {"meta":{"TransactionResult":"tesSUCCESS","delivered_amount":"1000000"},
               "tx_json":{"TransactionType":"Payment","Account":"@Them@","Destination":"@Me@","DeliverMax":"1000000"},
               @close@ "hash":"AB12","validated":true}
            ]}}
            """.Replace("@Them@", Them).Replace("@Me@", Me).Replace("@close@", closeTime);

        var row = Assert.Single(AccountHistoryClient.ParseXrp(json, Me));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 8, 8, 10, TimeSpan.Zero).ToUnixTimeMilliseconds(), row.UnixMs);
        Assert.Equal("AB12", row.Hash);
    }

    [Fact]
    public void A_partial_payment_shows_what_was_delivered_not_what_it_claimed()
    {
        // Claims 1,000,000 XRP, delivers 0.000001.
        var rows = AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Them, Me, "\"1000000000000\"", "\"1\"")), Me);

        Assert.Equal("0.000001", Assert.Single(rows).Amount);
    }

    [Fact]
    public void Failed_and_non_xrp_payments_are_left_out()
    {
        var failed = XrpPayment(Them, Me, "\"5000000\"", "\"5000000\"", result: "tecUNFUNDED_PAYMENT");
        var issued = XrpPayment(Them, Me, """{"currency":"USD","issuer":"rX","value":"5"}""",
            """{"currency":"USD","issuer":"rX","value":"5"}""");
        var trust = XrpPayment(Me, Them, "\"0\"", "\"0\"", type: "TrustSet");

        Assert.Empty(AccountHistoryClient.ParseXrp(XrpAnswer($"{failed},{issued},{trust}"), Me));
    }

    [Fact]
    public void An_outgoing_xrp_payment_names_the_recipient()
    {
        var row = Assert.Single(AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Me, Them, "\"40283187\"", "\"40283187\"")), Me));
        Assert.Equal("Sent", row.Kind);
        Assert.Equal(Them, row.Counterparty);
        Assert.Equal("40.283187", row.Amount);
    }

    private const string StellarMe = "GAHK7EEG2WWHVKDNT4CEQFZGKF2LGDSW2IVM4S5DP42RBW3K6BTODB4A";
    private const string StellarThem = "GC7YFQUTYWEZI6CE4JSOFCM6GRPXHLZOQPKFDGYO6BFTKYQ4KWU7KQI7";

    [Fact]
    public void Stellar_payments_and_the_accounts_creation_are_read()
    {
        var json = """
            {"_embedded":{"records":[
              {"type":"payment","from":"@StellarThem@","to":"@StellarMe@","amount":"12.5000000","asset_type":"native",
               "transaction_hash":"b56f891b","created_at":"2026-09-26T07:04:49Z","transaction_successful":true},
              {"type":"payment","from":"@StellarMe@","to":"@StellarThem@","amount":"3.0000000","asset_type":"credit_alphanum4",
               "asset_code":"USDC","transaction_hash":"c60b3999","created_at":"2026-09-25T00:56:54Z","transaction_successful":true},
              {"type":"create_account","funder":"@StellarThem@","account":"@StellarMe@","starting_balance":"2.0000000",
               "transaction_hash":"cf1fbbee","created_at":"2026-09-23T23:28:33Z","transaction_successful":true}
            ]}}
            """.Replace("@StellarThem@", StellarThem).Replace("@StellarMe@", StellarMe);

        var rows = AccountHistoryClient.ParseStellar(json, StellarMe);

        Assert.Equal(2, rows.Count);   // the USDC payment is not XLM and is left out
        Assert.Equal("12.5", rows[0].Amount);
        Assert.Equal("Received", rows[0].Kind);
        Assert.Equal("https://stellar.expert/explorer/public/tx/b56f891b", rows[0].Explorer);
        Assert.Equal("2", rows[1].Amount);
        Assert.Equal(StellarThem, rows[1].Counterparty);
    }

    [Fact]
    public void A_failed_stellar_payment_is_left_out()
    {
        var json = """
            {"_embedded":{"records":[
              {"type":"payment","from":"@StellarMe@","to":"@StellarThem@","amount":"1.0000000","asset_type":"native",
               "transaction_hash":"aa","created_at":"2026-09-26T07:04:49Z","transaction_successful":false}
            ]}}
            """.Replace("@StellarMe@", StellarMe).Replace("@StellarThem@", StellarThem);
        Assert.Empty(AccountHistoryClient.ParseStellar(json, StellarMe));
    }

    [Fact]
    public void History_links_are_the_links_a_send_stores_so_the_two_rows_merge()
    {
        // The Send screen stores "https://" + the explorer path (FinishSendAsync); Activity de-duplicates
        // on the whole string. A bare host here would show every payment sent from this wallet twice.
        var hash = "9FAF70B356A2DA2F5340CAA878A83AB6FC7275F3BDB9D05C92DD5E65FC0197DE";
        var sendPath = $"livenet.xrpl.org/transactions/{hash}";
        var row = Assert.Single(AccountHistoryClient.ParseXrp(XrpAnswer(XrpPayment(Them, Me, "\"1\"", "\"1\"", hash: hash)), Me));
        Assert.Equal($"https://{sendPath}", row.Explorer);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"result":{"status":"error"}}""")]
    [InlineData("""{"_embedded":{}}""")]
    public void An_unexpected_answer_is_no_rows_not_an_exception(string json)
    {
        Assert.Empty(AccountHistoryClient.ParseXrp(json, Me));
        Assert.Empty(AccountHistoryClient.ParseStellar(json, StellarMe));
        Assert.Empty(AccountHistoryClient.ParseNear(json, NearMe));
    }

    private const string NearMe = "98793cd91a3f870fb126f66285808c7e094afcfc4eda8a970f6648cdf0dbd6de";

    [Fact]
    public void Near_transfers_are_read_and_contract_calls_and_failures_are_not()
    {
        // The shape NearBlocks' txns-only answers with, trimmed. Deposits are JSON numbers — the second
        // in exponent form, as NearBlocks prints large yocto amounts.
        var json = """
            {"txns":[
              {"transaction_hash":"AAA1","signer_account_id":"alice.near","receiver_account_id":"@me@",
               "block_timestamp":"1790774824751389720","actions":[{"action":"TRANSFER","deposit":1500000000000000000000000}],
               "outcomes":{"status":true}},
              {"transaction_hash":"BBB2","signer_account_id":"@me@","receiver_account_id":"bob.near",
               "block_timestamp":"1790774822843860191","actions":[{"action":"TRANSFER","deposit":2.5e+23}],
               "outcomes":{"status":true}},
              {"transaction_hash":"CCC3","signer_account_id":"@me@","receiver_account_id":"wrap.near",
               "block_timestamp":"1790774819243183044","actions":[{"action":"FUNCTION_CALL","method":"near_deposit","deposit":0}],
               "outcomes":{"status":true}},
              {"transaction_hash":"DDD4","signer_account_id":"@me@","receiver_account_id":"bob.near",
               "block_timestamp":"1790774819243183044","actions":[{"action":"TRANSFER","deposit":1e+24}],
               "outcomes":{"status":false}}
            ]}
            """.Replace("@me@", NearMe);

        var rows = AccountHistoryClient.ParseNear(json, NearMe);

        Assert.Equal(2, rows.Count);
        Assert.Equal(("Received", "1.5", "alice.near"), (rows[0].Kind, rows[0].Amount, rows[0].Counterparty));
        Assert.Equal(("Sent", "0.25", "bob.near"), (rows[1].Kind, rows[1].Amount, rows[1].Counterparty));
        Assert.Equal("https://nearblocks.io/txns/AAA1", rows[0].Explorer);   // what a send from here stores
        Assert.Equal(1790774824751L, rows[0].UnixMs);                         // nanoseconds -> milliseconds
    }

    [Fact]
    public void A_near_transfer_too_large_for_a_decimal_in_yocto_is_still_shown()
    {
        // ~5 million NEAR is ~5 x 10^30 yocto, past decimal's ~7.9 x 10^28. Dropping the row would hide a
        // real transfer; it is scaled instead.
        var json = """
            {"txns":[{"transaction_hash":"EEE5","signer_account_id":"@me@","receiver_account_id":"exchange.near",
              "block_timestamp":"1790774824751389720","actions":[{"action":"TRANSFER","deposit":4.999999849952385e+30}],
              "outcomes":{"status":true}}]}
            """.Replace("@me@", NearMe);

        var row = Assert.Single(AccountHistoryClient.ParseNear(json, NearMe));
        Assert.StartsWith("4999999.84995", row.Amount);
    }
}
