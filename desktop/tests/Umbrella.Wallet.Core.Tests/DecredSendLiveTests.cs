using System.Text.Json;
using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;
using Xunit.Abstractions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Decred sending against the real network, without spending anything. <c>Category=Live</c>: by hand only.
///
/// A coin somebody else holds is found in a recent block, the wallet prepares a payment from it (reading
/// the coin and its fraud proof the way a real send does), signs it with a key that is NOT the owner's,
/// and broadcasts. dcrd checks the bytes, that the coins exist, every input's fraud proof and the fee
/// before it runs a single signature — so a refusal that names only the signature proves everything
/// else this wallet built is what the network expects. Nothing can be spent: the signature is wrong.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class DecredSendLiveTests(ITestOutputHelper output)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    [Fact]
    public async Task The_network_takes_everything_but_a_signature_from_the_wrong_key()
    {
        var owner = await FindFundedAddressAsync();
        output.WriteLine($"coin owner: {owner}");

        using var stranger = new Key();
        var strangerAddress = DecredAddress.FromPublicKey(stranger.PubKey.ToBytes());

        var sender = new DecredTransactionSender();
        var (quote, error) = await sender.PrepareAsync(owner, strangerAddress, 0.0001m);
        Assert.True(quote is not null, error);
        output.WriteLine($"inputs {quote!.InputCount}: " + string.Join(", ",
            quote.Inputs.Select(i => $"{i.TxId[..8]}:{i.Index} {i.Atoms} at {i.Height}/{i.BlockIndex}")));
        Assert.All(quote.Inputs, i => Assert.True(i.Height > 0));

        // Signed by the stranger: the quote says the coins are the stranger's (so the wallet's own
        // "this is not your key" guard lets it through), while the signature hash still commits to the
        // owner's script — exactly what a real spend of these coins would sign.
        var forged = quote with { From = strangerAddress };
        var result = await sender.SignAndBroadcastAsync(forged, stranger);
        output.WriteLine($"ok={result.Ok} unclear={result.Unclear} error={result.Error}");

        Assert.False(result.Ok);
        Assert.False(result.Unclear);
        var said = result.Error!.ToLowerInvariant();
        Assert.DoesNotContain("deserialize", said);
        Assert.DoesNotContain("fraud", said);
        Assert.DoesNotContain("required amount", said);
        Assert.DoesNotContain("orphan", said);
        // dcrd's script check: "failed to validate input <id>:0 which references output …".
        Assert.Contains("failed to validate input", said);
    }

    private static readonly string[] Roots = ["https://dcrdata.decred.org", "https://bisonexplorer.com"];

    /// <summary>
    /// A GET of a dcrdata path from whichever of the two servers answers, the way the wallet asks:
    /// spaced, because both rate-limit a client walking blocks quickly, and on to the other on a refusal.
    /// </summary>
    private static async Task<string> GetAsync(string path)
    {
        Exception? last = null;
        foreach (var root in Roots)
        {
            try
            {
                await Task.Delay(250);
                using var res = await Http.GetAsync(root + path);
                res.EnsureSuccessStatusCode();
                return await res.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }
        }

        throw last!;
    }

    /// <summary>The address of an unspent pay-to-pubkey-hash coin worth at least 0.01 DCR in a recent block.</summary>
    private static async Task<string> FindFundedAddressAsync()
    {
        var tip = long.Parse(await GetAsync($"/api/block/best/height"));
        for (var height = tip - 6; height > tip - 60; height--)
        {
            using var block = JsonDocument.Parse(await GetAsync($"/api/block/{height}/tx"));
            if (!block.RootElement.TryGetProperty("tx", out var txs) || txs.ValueKind != JsonValueKind.Array) continue;
            foreach (var id in txs.EnumerateArray().Skip(1).Select(t => t.GetString()))
            {
                using var tx = JsonDocument.Parse(await GetAsync($"/api/tx/{id}"));
                foreach (var vout in tx.RootElement.GetProperty("vout").EnumerateArray())
                {
                    var spk = vout.GetProperty("scriptPubKey");
                    if (spk.GetProperty("type").GetString() != "pubkeyhash" || vout.GetProperty("value").GetDecimal() < 0.01m) continue;
                    var address = spk.GetProperty("addresses")[0].GetString()!;
                    using var utxos = JsonDocument.Parse(await GetAsync($"/insight/api/addr/{address}/utxo"));
                    if (utxos.RootElement.GetArrayLength() > 0) return address;
                }
            }
        }

        throw new InvalidOperationException("No funded pay-to-pubkey-hash coin found in the last blocks.");
    }
}
