using System.Net;
using System.Text.Json;
using Umbrella.Wallet.Core.Utxo;
using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Dogecoin's second explorer (BitPay's Bitcore), for when BlockCypher's 200 requests an hour run out.
/// The shape is a real answer's; what matters is that a coin already being spent is never offered again
/// and a coin still in the mempool is marked unconfirmed.
/// </summary>
public class BitcoreUtxoExplorerTests
{
    private const string Txid = "484c94145e97c0b2dd96912c1f056c9e05c8d358ab96ea429c51039d75d0d3c4";

    [Fact]
    public void UnspentCoinsAreReadAndSpentOnesAreNot()
    {
        var json = $$"""
            [
              {"chain":"DOGE","mintIndex":0,"spentTxid":"","mintTxid":"{{Txid}}","mintHeight":6385234,"spentHeight":-2,"value":951727163,"confirmations":-1},
              {"chain":"DOGE","mintIndex":1,"spentTxid":"","mintTxid":"{{Txid}}","mintHeight":-1,"spentHeight":-2,"value":5000000,"confirmations":-1},
              {"chain":"DOGE","mintIndex":2,"spentTxid":"","mintTxid":"{{Txid}}","mintHeight":6385234,"spentHeight":-1,"value":7000000,"confirmations":-1},
              {"chain":"DOGE","mintIndex":3,"spentTxid":"{{Txid}}","mintTxid":"{{Txid}}","mintHeight":6385234,"spentHeight":6385300,"value":9000000,"confirmations":-1},
              {"chain":"DOGE","mintIndex":4,"spentTxid":"","mintTxid":"short","mintHeight":6385234,"spentHeight":-2,"value":1}
            ]
            """;

        using var doc = JsonDocument.Parse(json);
        var utxos = BitcoreUtxoExplorer.ParseUtxos(doc.RootElement);

        Assert.Equal(2, utxos.Count);
        Assert.Equal(951_727_163L, utxos[0].ValueSat);
        Assert.True(utxos[0].Confirmed);
        Assert.Equal(1, utxos[1].Vout);
        Assert.False(utxos[1].Confirmed);   // still in the mempool
    }

    [Fact]
    public void AnErrorIsNotAnEmptyWallet()
    {
        // Bitcore answers an error as an object or a string. Read as "no coins", that would tell someone
        // their Dogecoin is gone; it must throw, which the scanner reports as "unknown".
        using var doc = JsonDocument.Parse("""{"error":"Too many requests"}""");
        Assert.Throws<InvalidDataException>(() => BitcoreUtxoExplorer.ParseUtxos(doc.RootElement));
    }

    [Fact]
    public void AServerTheUserChoseIsTheOnlyOneAsked()
    {
        // The suite points every chain at a closed local port through the same override a user sets in
        // Settings — so here Dogecoin must go to that server alone, never quietly to Bitcore as well.
        Assert.NotNull(Umbrella.Wallet.Core.Safety.ChainEndpoints.OverrideFor("DOGE"));
        Assert.IsType<BlockCypherUtxoExplorer>(FailoverUtxoExplorer.ForDogecoin());
        Assert.Equal("https://api.bitcore.io/api/DOGE/mainnet", BitcoreUtxoExplorer.BaseFor("doge"));
    }
    [Fact]
    public async Task ARefusingServerIsSkippedAndTheNextOneAnswers()
    {
        var refusing = new FakeExplorer(new HttpRequestException("Limits reached", null, HttpStatusCode.TooManyRequests));
        var answering = new FakeExplorer(null);
        var explorer = new FailoverUtxoExplorer(
            ($"https://refusing-{Guid.NewGuid():N}.test", refusing),
            ($"https://answering-{Guid.NewGuid():N}.test", answering));

        var utxos = await explorer.GetUtxosAsync("D-address", CancellationToken.None);

        Assert.Single(utxos);
        Assert.Equal(1, refusing.Calls);
        Assert.Equal(1, answering.Calls);
    }

    [Fact]
    public async Task WhenEveryServerFailsTheAnswerIsUnknownNotEmpty()
    {
        var explorer = new FailoverUtxoExplorer(
            ($"https://a-{Guid.NewGuid():N}.test", new FakeExplorer(new HttpRequestException("down"))),
            ($"https://b-{Guid.NewGuid():N}.test", new FakeExplorer(new HttpRequestException("also down"))));

        await Assert.ThrowsAsync<HttpRequestException>(() => explorer.GetUtxosAsync("D-address", CancellationToken.None));
    }

    private sealed class FakeExplorer(Exception? failure) : IUtxoExplorer
    {
        public int Calls { get; private set; }

        public Task<AddressActivity> GetActivityAsync(string address, CancellationToken ct)
        {
            Calls++;
            return failure is null ? Task.FromResult(new AddressActivity(true, 1)) : Task.FromException<AddressActivity>(failure);
        }

        public Task<IReadOnlyList<ExplorerUtxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            Calls++;
            return failure is null
                ? Task.FromResult<IReadOnlyList<ExplorerUtxo>>([new ExplorerUtxo(Txid, 0, 100_000_000, true)])
                : Task.FromException<IReadOnlyList<ExplorerUtxo>>(failure);
        }
    }
}
