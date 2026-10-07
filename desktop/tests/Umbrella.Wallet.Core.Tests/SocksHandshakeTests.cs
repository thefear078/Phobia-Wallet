using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// What the wallet actually says to its SOCKS5 proxy, read off the wire.
///
/// <see cref="TorStreamIsolationTests"/> proves that each purpose gets its own client object. That is
/// necessary and not sufficient: Tor puts two streams on different circuits only when their SOCKS5
/// username/password differ, so a client that hands Tor no credentials at all shares one circuit with
/// every other client, however many objects there are. These tests stand up a SOCKS5 server on
/// loopback, let the real handler talk to it, and record the handshake — the username offered, and
/// whether the destination went out as a NAME (resolved by Tor, at the exit) or as an address this
/// machine looked up itself, which would have put the explorer's name on the local network in clear.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class SocksHandshakeTests : IDisposable
{
    public SocksHandshakeTests() => TestDataIsolation.GoOffline();

    public void Dispose() => TestDataIsolation.GoOffline();

    [Fact]
    public async Task A_purpose_client_hands_tor_its_own_username()
    {
        using var socks = new FakeSocks5();
        Route(socks);

        var host = Host("explorer");
        await Probe(PublicHttp.NetworkPurpose.ChainData, $"http://{host}/");

        var hello = Assert.Single(socks.For(host));
        Assert.NotNull(hello.User);
        Assert.Contains("chaindata", hello.User!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Address_lookups_and_broadcasts_go_out_under_different_usernames()
    {
        // The separation the whole per-purpose design is for: the exit that saw the wallet ask about an
        // address must not be the exit that sees the transaction spending it.
        using var socks = new FakeSocks5();
        Route(socks);

        var host = Host("explorer");
        await Probe(PublicHttp.NetworkPurpose.ChainData, $"http://{host}/");
        await Probe(PublicHttp.NetworkPurpose.Broadcast, $"http://{host}/");

        var users = socks.For(host).Select(h => h.User).ToList();
        Assert.Equal(2, users.Count);
        Assert.All(users, Assert.NotNull);
        Assert.NotEqual(users[0], users[1]);
    }

    [Fact]
    public async Task One_purpose_keeps_one_circuit_whatever_it_asks()
    {
        // Deliberately per purpose, not per server or per request. A circuit for every explorer was
        // tried: a fresh wallet asks some thirty servers at once, and with that many circuits to build
        // Tor left the Bitcoin-family balance scans unfinished after four minutes (one circuit per
        // purpose read them all). A fresh circuit per request would be worse still.
        using var socks = new FakeSocks5();
        Route(socks);

        var (one, other) = (Host("one-explorer"), Host("other-explorer"));
        await Probe(PublicHttp.NetworkPurpose.ChainData, $"http://{one}/a");
        await Probe(PublicHttp.NetworkPurpose.ChainData, $"http://{one}/b");
        await Probe(PublicHttp.NetworkPurpose.ChainData, $"http://{other}/");

        var seen = socks.For(one).Concat(socks.For(other)).ToList();
        Assert.Equal(3, seen.Count);
        Assert.Single(seen.Select(h => h.User).Distinct());
    }

    [Fact]
    public async Task Every_purpose_has_a_username_of_its_own()
    {
        using var socks = new FakeSocks5();
        Route(socks);

        var host = Host("explorer");
        foreach (var purpose in Enum.GetValues<PublicHttp.NetworkPurpose>())
            await Probe(purpose, $"http://{host}/");

        var users = socks.For(host).Select(h => h.User).ToList();
        Assert.Equal(Enum.GetValues<PublicHttp.NetworkPurpose>().Length, users.Count);
        Assert.All(users, Assert.NotNull);
        Assert.Equal(users.Count, users.Distinct().Count());
    }

    [Fact]
    public async Task The_destination_goes_to_tor_as_a_name_not_as_a_local_lookup()
    {
        // ATYP 3 = domain name: Tor resolves it at the exit. An IPv4/IPv6 ATYP here would mean this
        // machine had already asked its own DNS server where the explorer lives.
        using var socks = new FakeSocks5();
        Route(socks);

        var host = Host("prices");
        await Probe(PublicHttp.NetworkPurpose.Prices, $"http://{host}/");

        var hello = Assert.Single(socks.For(host));
        Assert.Equal(3, hello.AddressType);
    }

    // --- the proxy string a user types ---------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1:9050", "socks5://127.0.0.1:9050")]
    [InlineData("socks5://127.0.0.1:9150", "socks5://127.0.0.1:9150")]
    // "socks5h" is how curl and Tor's own docs spell "resolve the name at the proxy". .NET does not
    // know the spelling and refused every request; its socks5 already sends the name, so they are one.
    [InlineData("socks5h://127.0.0.1:9050", "socks5://127.0.0.1:9050")]
    // Plain SOCKS4 cannot carry a name, so .NET resolves the explorer on THIS machine first — a DNS
    // query in clear for every server the wallet uses. SOCKS4a carries the name; Tor speaks it.
    [InlineData("socks4://127.0.0.1:9050", "socks4a://127.0.0.1:9050")]
    [InlineData("socks4a://127.0.0.1:9050", "socks4a://127.0.0.1:9050")]
    [InlineData(" SOCKS5://Localhost:9050 ", "socks5://localhost:9050")]
    public void A_proxy_is_normalised_to_one_that_resolves_names_remotely(string typed, string expected)
    {
        Assert.Equal(expected, PublicHttp.NormalizeProxy(typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://127.0.0.1:8080")]   // not a SOCKS proxy: the wallet's isolation needs SOCKS
    [InlineData("https://127.0.0.1:8080")]
    [InlineData("socks5://127.0.0.1")]      // no port
    [InlineData("socks5://:9050")]
    [InlineData("not a proxy")]
    public void Anything_else_is_refused_rather_than_guessed(string typed)
    {
        Assert.Null(PublicHttp.NormalizeProxy(typed));
    }

    // --- helpers -----------------------------------------------------------------------------------

    /// <summary>A destination no other test uses: view models built by other tests in this process can
    /// still be refreshing in the background through whatever proxy is set, so a test reads only the
    /// handshakes for its own hosts.</summary>
    private static string Host(string name) => $"{name}-{Guid.NewGuid():N}.example";

    private static void Route(FakeSocks5 socks)
    {
        PublicHttp.SetRequireProxy(false);
        PublicHttp.SetProxy(socks.Uri);
    }

    /// <summary>One request through the purpose's client. The fake proxy refuses the CONNECT, so the
    /// request fails — what matters is the handshake it recorded before refusing.</summary>
    private static async Task Probe(PublicHttp.NetworkPurpose purpose, string url)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { using var _ = await PublicHttp.For(purpose).GetAsync(url, cts.Token); }
        catch (HttpRequestException) { /* expected: the fake proxy refuses every CONNECT */ }
    }

    private sealed record Handshake(string? User, int AddressType, string Host);

    /// <summary>
    /// The smallest SOCKS5 server that can tell us what a client offered (RFC 1928 + RFC 1929): it
    /// accepts username/password when offered, records the CONNECT, and then refuses it.
    /// </summary>
    private sealed class FakeSocks5 : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<Handshake> _handshakes = new();

        public FakeSocks5()
        {
            _listener.Start();
            _ = AcceptLoop();
        }

        public string Uri => $"socks5://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public IReadOnlyList<Handshake> For(string host) =>
            _handshakes.Where(h => string.Equals(h.Host, host, StringComparison.OrdinalIgnoreCase)).ToArray();

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                _ = Serve(client);
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var s = client.GetStream();
                    var head = await Read(s, 2);                       // VER, NMETHODS
                    var methods = await Read(s, head[1]);
                    string? user = null;
                    if (methods.Contains((byte)0x02))
                    {
                        await s.WriteAsync(new byte[] { 0x05, 0x02 });
                        var ver = await Read(s, 2);                    // 0x01, ULEN
                        user = Encoding.UTF8.GetString(await Read(s, ver[1]));
                        var plen = await Read(s, 1);
                        await Read(s, plen[0]);
                        await s.WriteAsync(new byte[] { 0x01, 0x00 });
                    }
                    else
                    {
                        await s.WriteAsync(new byte[] { 0x05, 0x00 });
                    }

                    var req = await Read(s, 4);                        // VER, CMD, RSV, ATYP
                    var host = req[3] switch
                    {
                        0x01 => new IPAddress(await Read(s, 4)).ToString(),
                        0x04 => new IPAddress(await Read(s, 16)).ToString(),
                        0x03 => Encoding.ASCII.GetString(await Read(s, (await Read(s, 1))[0])),
                        _ => "?",
                    };
                    await Read(s, 2);                                  // port
                    _handshakes.Enqueue(new Handshake(user, req[3], host));

                    // 0x05 = connection refused: the client stops here, no HTTP is attempted.
                    await s.WriteAsync(new byte[] { 0x05, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
                }
                catch
                {
                    // a client that hung up mid-handshake records nothing
                }
            }
        }

        private static async Task<byte[]> Read(NetworkStream s, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = await s.ReadAsync(buffer.AsMemory(read, count - read));
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
