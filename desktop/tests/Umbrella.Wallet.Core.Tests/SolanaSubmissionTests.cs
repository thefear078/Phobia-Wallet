using System.Text.Json;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// What a Solana send does when the servers misbehave — the part that decides whether the wallet says
/// "sent", "nothing was sent" or "check before sending again", and each of those is a statement about
/// somebody's money.
///
/// On 2026-10-10 a send ended at "Could not fetch a recent blockhash" because the one server the quote
/// came from had gone quiet, while the other two listed servers were answering. The network here is a
/// script, so every case is exact: which server answers what, and in which order.
/// </summary>
public sealed class SolanaSubmissionTests
{
    private const string A = "https://a.example";
    private const string B = "https://b.example";
    private const string C = "https://c.example";

    private static readonly byte[] PrivateKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static byte[] PublicKey()
    {
        var pub = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(PrivateKey, 0, pub, 0);
        return pub;
    }

    private static readonly byte[] Destination = Enumerable.Repeat((byte)0x22, 32).ToArray();
    private static readonly string Blockhash = Encoders.Base58.EncodeData(Enumerable.Repeat((byte)0x33, 32).ToArray());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement BlockhashAnswer(ulong lastValid = 1_000) =>
        Json($$$"""{"context":{"slot":1},"value":{"blockhash":"{{{Blockhash}}}","lastValidBlockHeight":{{{lastValid}}}}}""");

    private static JsonElement Status(string? level, string err = "null") => Json(level is null
        ? """{"context":{"slot":1},"value":[null]}"""
        : $$$"""{"context":{"slot":1},"value":[{"confirmationStatus":"{{{level}}}","err":{{{err}}}}]}""");

    /// <summary>A scripted network: what each server answers to each method, and everything it was asked.</summary>
    private sealed class Script
    {
        public readonly List<(string Server, string Method, bool Broadcast, string? Payload)> Calls = [];
        private readonly Dictionary<(string Server, string Method), Func<(JsonElement?, string?)>> _answers = [];

        public Script On(string server, string method, Func<(JsonElement?, string?)> answer)
        {
            _answers[(server, method)] = answer;
            return this;
        }

        public Script On(string server, string method, JsonElement result) => On(server, method, () => (result, null));

        public SolanaSubmission.Rpc Rpc => (server, request, _, broadcast) =>
        {
            var root = JsonSerializer.SerializeToElement(request);
            var method = root.GetProperty("method").GetString()!;
            var payload = method == "sendTransaction" ? root.GetProperty("params")[0].GetString() : null;
            lock (Calls) Calls.Add((server, method, broadcast, payload));
            // Unscripted = the server did not answer.
            return Task.FromResult(_answers.TryGetValue((server, method), out var answer) ? answer() : (null, null));
        };

        public IEnumerable<string> ServersAsked(string method) =>
            Calls.Where(c => c.Method == method).Select(c => c.Server);
    }

    private static Task<SolSendOutcome> Run(Script script, params string[] servers) =>
        SolanaSubmission.RunAsync(servers, script.Rpc, (_, _) => Task.CompletedTask, PublicKey(),
            [SolanaTransactionSender.SystemTransfer(PublicKey(), Destination, 1_000_000)], PrivateKey, CancellationToken.None);

    private static JsonElement Id(Script script) =>
        JsonSerializer.SerializeToElement(SignatureOf(script.Calls.First(c => c.Method == "sendTransaction").Payload!));

    /// <summary>The transaction id is its first signature: 64 bytes after the one-byte signature count.</summary>
    private static string SignatureOf(string base64Transaction) =>
        Encoders.Base58.EncodeData(Convert.FromBase64String(base64Transaction).AsSpan(1, 64).ToArray());

    [Fact]
    public async Task A_server_that_gives_no_blockhash_is_passed_over_and_the_next_one_takes_the_send()
    {
        var script = new Script()
            .On(B, "getLatestBlockhash", BlockhashAnswer())
            .On(B, "getSignatureStatuses", Status("confirmed"));
        script.On(B, "sendTransaction", () => (Id(script), null));

        var outcome = await Run(script, A, B, C);

        Assert.Equal(SolSubmitOutcome.Included, outcome.Outcome);
        Assert.Equal(new[] { A, B }, script.ServersAsked("getLatestBlockhash"));   // A asked first, C never needed
        var submit = Assert.Single(script.Calls, c => c.Method == "sendTransaction");
        Assert.Equal(B, submit.Server);                                             // the one that answered
        Assert.True(submit.Broadcast);                                              // on the broadcast circuit
        Assert.Equal(SignatureOf(submit.Payload!), outcome.Signature);
    }

    [Fact]
    public async Task No_blockhash_from_anyone_means_nothing_was_signed_or_sent()
    {
        var script = new Script();

        var outcome = await Run(script, A, B, C);

        Assert.Equal(SolSubmitOutcome.Rejected, outcome.Outcome);
        Assert.Null(outcome.Signature);
        Assert.Contains("nothing was signed or sent", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(script.Calls, c => c.Method == "sendTransaction");
        Assert.Equal(new[] { A, B, C }, script.ServersAsked("getLatestBlockhash"));   // every one got its chance
    }

    [Fact]
    public async Task A_preflight_refusal_ends_it_with_the_nodes_reason_and_no_transaction_id()
    {
        var script = new Script()
            .On(A, "getLatestBlockhash", BlockhashAnswer())
            .On(A, "sendTransaction", () => (null, "Transaction simulation failed: insufficient lamports"));

        var outcome = await Run(script, A, B);

        Assert.Equal(SolSubmitOutcome.Rejected, outcome.Outcome);
        Assert.Null(outcome.Signature);
        Assert.Contains("insufficient lamports", outcome.Message);
        // A refusal is an answer. It is not shopped around to a server that might be more lenient.
        Assert.Single(script.Calls, c => c.Method == "sendTransaction");
    }

    [Fact]
    public async Task The_status_is_read_from_another_server_when_the_submitting_one_goes_quiet()
    {
        var script = new Script()
            .On(A, "getLatestBlockhash", BlockhashAnswer())
            .On(B, "getSignatureStatuses", Status("finalized"));
        script.On(A, "sendTransaction", () => (Id(script), null));

        var outcome = await Run(script, A, B);

        Assert.Equal(SolSubmitOutcome.Included, outcome.Outcome);
        Assert.Equal(new[] { A, B }, script.ServersAsked("getSignatureStatuses").Take(2));
    }

    [Fact]
    public async Task A_transaction_seen_nowhere_is_handed_to_the_other_servers_as_the_same_bytes()
    {
        var looks = 0;
        var script = new Script()
            .On(A, "getLatestBlockhash", BlockhashAnswer())
            .On(A, "getBlockHeight", Json("10"));
        script.On(A, "sendTransaction", () => (Id(script), null));
        // Unseen until after the other servers have been given it, then confirmed.
        script.On(A, "getSignatureStatuses", () =>
            (++looks > SolanaSubmission.RebroadcastAfterAttempts + 1 ? Status("confirmed") : Status(null), null));

        var outcome = await Run(script, A, B, C);

        Assert.Equal(SolSubmitOutcome.Included, outcome.Outcome);
        var submits = script.Calls.Where(c => c.Method == "sendTransaction").ToList();
        Assert.Equal(new[] { A, B, C }, submits.Select(c => c.Server));
        // One signature: however many servers carry it, the network can include it once.
        Assert.Single(submits.Select(c => c.Payload).Distinct());
        Assert.All(submits, c => Assert.True(c.Broadcast));
    }

    [Fact]
    public async Task Included_but_failed_is_reported_as_a_failure_that_cost_the_fee()
    {
        var script = new Script()
            .On(A, "getLatestBlockhash", BlockhashAnswer())
            .On(A, "getSignatureStatuses", Status("confirmed", """{"InstructionError":[0,{"Custom":1}]}"""));
        script.On(A, "sendTransaction", () => (Id(script), null));

        var outcome = await Run(script, A);

        Assert.Equal(SolSubmitOutcome.FailedFeeCharged, outcome.Outcome);
        Assert.Contains("only the fee was charged", outcome.Message);
    }

    [Fact]
    public async Task Past_the_last_valid_height_and_still_unseen_is_final_nothing_was_sent()
    {
        var script = new Script()
            .On(A, "getLatestBlockhash", BlockhashAnswer(lastValid: 500))
            .On(A, "getSignatureStatuses", Status(null))
            .On(A, "getBlockHeight", Json("501"));
        script.On(A, "sendTransaction", () => (Id(script), null));

        var outcome = await Run(script, A);

        Assert.Equal(SolSubmitOutcome.Rejected, outcome.Outcome);
        Assert.NotNull(outcome.Signature);   // the id it would have had, for the record
        Assert.Contains("Nothing was sent", outcome.Message);
    }

    [Fact]
    public async Task Silence_after_the_submit_is_never_called_a_failure_or_offered_as_a_retry()
    {
        // The blockhash came, the submit got no answer, and nobody answers anything afterwards. The
        // transaction may be on its way: the only honest outcome is "unknown", with its id.
        var script = new Script().On(A, "getLatestBlockhash", BlockhashAnswer());

        var outcome = await Run(script, A, B);

        Assert.Equal(SolSubmitOutcome.Unknown, outcome.Outcome);
        Assert.NotNull(outcome.Signature);
        Assert.Contains("before sending again", outcome.Message);
        Assert.Equal(SolanaSubmission.FollowAttempts * 2,
            script.Calls.Count(c => c.Method == "getSignatureStatuses"));   // each look asked both servers
    }
}
