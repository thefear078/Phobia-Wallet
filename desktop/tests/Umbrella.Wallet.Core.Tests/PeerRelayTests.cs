using System.Net;
using System.Net.Sockets;
using NBitcoin;
using NBitcoin.Protocol;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Private broadcast (roadmap P2.4): the transaction is handed to one node of the coin's own network.
/// Proved against a real Bitcoin P2P node on this machine (NBitcoin's node server, regtest): the
/// handshake completes, the node receives exactly this transaction, and its pong says it was read.
/// </summary>
public sealed class PeerRelayTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static Transaction SomeTransaction()
    {
        var key = new Key();
        var tx = Network.RegTest.CreateTransaction();
        tx.Inputs.Add(new OutPoint(uint256.One, 0));
        tx.Outputs.Add(Money.Coins(0.001m), key.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit));
        return tx;
    }

    [Fact]
    public async Task The_node_receives_exactly_this_transaction()
    {
        var port = FreePort();
        using var server = new NodeServer(Network.RegTest, internalPort: port)
        {
            AllowLocalPeers = true,
            LocalEndpoint = new IPEndPoint(IPAddress.Loopback, port),
            ExternalEndpoint = new IPEndPoint(IPAddress.Loopback, port),
        };
        var received = new TaskCompletionSource<uint256>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += (_, e) =>
        {
            if (e.Message.Payload is TxPayload p) received.TrySetResult(p.Object.GetHash());
        };
        server.Listen();

        var tx = SomeTransaction();
        var ok = await PeerRelay.TryRelayAsync(Network.RegTest, tx, new IPEndPoint(IPAddress.Loopback, port), socks: null, CancellationToken.None);

        Assert.True(ok);
        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(tx.GetHash(), got);
    }

    [Fact]
    public async Task Nobody_listening_is_a_failed_relay_not_an_exception()
    {
        var ok = await PeerRelay.TryRelayAsync(Network.RegTest, SomeTransaction(),
            new IPEndPoint(IPAddress.Loopback, FreePort()), socks: null, CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public void Each_network_has_seeds_and_speaks_as_its_reference_client()
    {
        foreach (var network in new[] { Network.Main, NBitcoin.Altcoins.Litecoin.Instance.Mainnet, NBitcoin.Altcoins.Dogecoin.Instance.Mainnet })
        {
            Assert.NotEmpty(PeerRelay.SeedsFor(network));
            Assert.DoesNotContain("NBitcoin", PeerRelay.UserAgentFor(network), StringComparison.Ordinal);
        }
    }

    // ---- what the post-send report says about it ------------------------------------------------------

    [Theory]
    [InlineData(true, "firstHopPeer", true)]
    [InlineData(false, "firstHopExplorer", false)]
    public void The_report_names_who_saw_the_payment_first(bool relayed, string code, bool isProtected)
    {
        var findings = Umbrella.Wallet.Core.Safety.SendLeakReport.Build(new Umbrella.Wallet.Core.Safety.SendLeakSignals(
            RoutedThroughTor: true, KillSwitchArmed: true, InputAddressCount: 1, ChainHidesAmounts: false,
            FreshChangeUsed: true, CoinControlUsed: false, RelayedThroughPeer: relayed));

        var line = Assert.Single(findings, f => f.Code.StartsWith("firstHop", StringComparison.Ordinal));
        Assert.Equal((code, isProtected), (line.Code, line.IsProtected));
        Assert.Equal(!relayed, Umbrella.Wallet.Core.Safety.SendLeakReport.HasAvoidableExposure(findings));
    }

    [Fact]
    public void Where_the_question_does_not_arise_the_report_says_nothing_about_it()
    {
        var findings = Umbrella.Wallet.Core.Safety.SendLeakReport.Build(new Umbrella.Wallet.Core.Safety.SendLeakSignals(
            RoutedThroughTor: false, KillSwitchArmed: false, InputAddressCount: 1, ChainHidesAmounts: false,
            FreshChangeUsed: true, CoinControlUsed: false));

        Assert.DoesNotContain(findings, f => f.Code.StartsWith("firstHop", StringComparison.Ordinal));
    }
}

/// <summary>
/// The relay against the real Bitcoin network: a node found through a DNS seed completes the handshake
/// with the relay's version message and reads a transaction. The transaction is a historic, long
/// confirmed one (the 10,000 BTC pizza), so handing it over again changes nothing anywhere.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveNetworkCollection.Name)]
public sealed class PeerRelayLiveTests
{
    [Fact]
    public async Task A_real_node_takes_the_relays_handshake_and_transaction()
    {
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetProxy(null);
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetRequireProxy(false);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var hex = await http.GetStringAsync("https://mempool.space/api/tx/a1075db55d416d3ca199f55b6084e2115b9345e16c5cf302fc80e9d5fbf5d48d/hex");
        var tx = Transaction.Parse(hex.Trim(), Network.Main);

        var tried = new List<string>();
        foreach (var seed in PeerRelay.SeedsFor(Network.Main).Take(4))
        {
            tried.Add(seed);
            var (ok, agent) = await PeerRelay.RelayAsync(Network.Main, tx, new DnsEndPoint(seed, 8333), socks: null, CancellationToken.None);
            if (!ok) continue;
            // A real node answered: its own client named itself in the handshake.
            Assert.False(string.IsNullOrWhiteSpace(agent));
            Assert.StartsWith("/", agent, StringComparison.Ordinal);
            return;
        }

        Assert.Fail("No node took the relay: " + string.Join(", ", tried));
    }

    /// <summary>
    /// The path the wallet really takes: through Tor, a DNS seed resolved at the exit, a fresh circuit.
    /// Runs when <c>PHOBIA_TEST_SOCKS</c> names a Tor SOCKS port (socks5://127.0.0.1:9050).
    /// </summary>
    [Fact]
    public async Task Through_tor_a_random_node_takes_it()
    {
        var socks = Environment.GetEnvironmentVariable("PHOBIA_TEST_SOCKS");
        if (string.IsNullOrWhiteSpace(socks)) return;
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetProxy(null);
        Umbrella.Wallet.Infrastructure.Network.PublicHttp.SetRequireProxy(false);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var hex = await http.GetStringAsync("https://mempool.space/api/tx/a1075db55d416d3ca199f55b6084e2115b9345e16c5cf302fc80e9d5fbf5d48d/hex");
        var tx = Transaction.Parse(hex.Trim(), Network.Main);

        Assert.True(await PeerRelay.RelayThroughProxyAsync(Network.Main, tx, new Uri(socks), CancellationToken.None));
    }
}