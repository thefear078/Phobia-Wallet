using System.Net;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Private broadcast (roadmap P2.4): a signed transaction handed to ONE randomly chosen node of the
/// coin's own network, over a Tor circuit of its own — instead of to the explorer that has just been
/// asked about the same addresses.
///
/// Dandelion++ itself (BIP-156) was never adopted by Bitcoin Core: no node runs its stem phase, and a
/// wallet cannot do it alone. What a wallet can do is the part that matters to it: the first machine
/// to see the transaction is not one that already knows which addresses are the user's, and it sees
/// a Tor exit, not the user. That machine then relays it like any other — the explorer learns of it
/// from the network, as it learns of everybody's.
///
/// The node is reached by name through the proxy (a DNS seed resolved at the Tor exit, which hands
/// out a random reachable node), so nothing is looked up on this machine. A fresh SOCKS identity per
/// broadcast keeps it off every other circuit.
/// </summary>
public static class PeerRelay
{
    /// <summary>How long a relay may take before the explorer is used instead.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(45);

    /// <summary>The DNS seeds of a network: each answers with random reachable nodes.</summary>
    public static IReadOnlyList<string> SeedsFor(NBitcoin.Network network) =>
        network.DNSSeeds.Select(s => s.Host).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();

    /// <summary>
    /// The user agent sent in the handshake: the reference client's own, so the relay does not stand
    /// out among the node's peers as the one library nobody else runs.
    /// </summary>
    public static string UserAgentFor(NBitcoin.Network network) => network.NetworkSet.CryptoCode switch
    {
        "LTC" => "/LitecoinCore:0.21.4/",
        "DOGE" => "/Shibetoshi:1.14.9/",
        _ => "/Satoshi:28.1.0/",
    };

    /// <summary>
    /// Hands <paramref name="tx"/> to one node of <paramref name="network"/>: version handshake, the
    /// transaction, then a ping — a node reads its messages in order, so its pong says the transaction
    /// was read. True when that much happened; whether the network then carries it is for the caller to
    /// check. Through <paramref name="socks"/> when one is given (the only way the wallet calls it); a
    /// test may give a node directly.
    /// </summary>
    public static async Task<bool> TryRelayAsync(
        NBitcoin.Network network, Transaction tx, EndPoint target, EndPoint? socks, CancellationToken ct) =>
        (await RelayAsync(network, tx, target, socks, ct)).Ok;

    /// <summary>The same, with the user agent the node announced in its handshake (for the live test).</summary>
    public static async Task<(bool Ok, string? PeerAgent)> RelayAsync(
        NBitcoin.Network network, Transaction tx, EndPoint target, EndPoint? socks, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Deadline);
        try
        {
            var parameters = new NodeConnectionParameters
            {
                UserAgent = UserAgentFor(network),
                IsRelay = false,                    // nothing is asked of it but this one transaction
                ConnectCancellation = deadline.Token,
            };
            if (socks is not null)
            {
                var identity = "phobia-relay-" + Guid.NewGuid().ToString("N")[..12];
                parameters.TemplateBehaviors.Add(new SocksSettingsBehavior(
                    socks, onlyForOnionHosts: false, new NetworkCredential(identity, identity), streamIsolation: true));
            }

            using var node = await Node.ConnectAsync(network, target, parameters).WaitAsync(deadline.Token);
            await node.VersionHandshakeAsync(deadline.Token);
            await node.SendMessageAsync(new TxPayload(tx));
            await Task.Run(() => node.PingPong(deadline.Token), deadline.Token);
            var agent = node.PeerVersion?.UserAgent;
            node.DisconnectAsync("done");
            return (true, agent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// One attempt through the proxy at a random seed of the network — then another seed, once, if the
    /// first did not answer. The caller falls back to its explorer whatever happens here.
    /// </summary>
    public static async Task<bool> RelayThroughProxyAsync(NBitcoin.Network network, Transaction tx, Uri socksProxy, CancellationToken ct)
    {
        var socks = new DnsEndPoint(socksProxy.Host, socksProxy.Port);
        // Which node is told first is the privacy this buys, so it is chosen with the system's
        // cryptographic generator rather than a predictable one.
        var seeds = SeedsFor(network).OrderBy(_ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)).Take(2).ToList();
        foreach (var seed in seeds)
        {
            if (await TryRelayAsync(network, tx, new DnsEndPoint(seed, network.DefaultPort), socks, ct)) return true;
        }

        return false;
    }
}
