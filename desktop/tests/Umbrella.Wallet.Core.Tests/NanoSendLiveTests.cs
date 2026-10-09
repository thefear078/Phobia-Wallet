using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A real node judges blocks this code built. Each is an OPEN block for a throwaway key that "receives" a
/// real mainnet send that was never meant for it, so none can move money. For an open block the node
/// checks the work, then the signature, and only then whether the payment is the account's to take — so
/// the good block must fail as "Unreceivable", and the same block with one signature bit flipped, or a
/// nonce that does no work, must fail on exactly that. (A send building on a missing previous block is no
/// test: the node answers "Gap previous" before it looks at the signature at all.)
/// <c>Category=Live</c>: run by hand, never in CI.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class NanoSendLiveTests
{
    /// <summary>A mainnet send (to the Natrium donation account) — the link of block 6D7FC58C….</summary>
    private const string SomeoneElsesSend = "7A47335F0EDB165766036B4CFCE09F53F28CC8AC279E218719886E485383E39D";

    [Fact]
    public async Task A_node_accepts_this_codes_signature_and_work_and_refuses_only_what_is_not_ours()
    {
        ChainEndpoints.ClearAll();
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);

        var key = RandomNumberGenerator.GetBytes(32);
        var publicKey = NanoAccounts.PublicKey(key);
        var work = NanoBlocks.GenerateWork(publicKey, NanoBlocks.ReceiveThreshold);
        var block = NanoBlocks.Build(key, new byte[32], NanoSender.FallbackRepresentative, new BigInteger(1000),
            Convert.FromHexString(SomeoneElsesSend), work);
        var badSignature = (byte[])block.Signature.Clone();
        badSignature[5] ^= 1;

        Assert.Equal("Unreceivable", await ProcessAsync(block));
        Assert.Equal("Bad signature", await ProcessAsync(block with { Signature = badSignature }));
        Assert.Contains("work", await ProcessAsync(block with { Work = 1 }), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ProcessAsync(NanoStateBlock block)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var res = await http.PostAsJsonAsync("https://rpc.nano.to", NanoBlocks.ProcessRequest(block, "open"));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Null(NanoBlocks.ParseProcessed(doc.RootElement, out var error));
        return error ?? "";
    }
}
