using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The local Monero service must not answer anyone who asks.
///
/// It used to run with <c>--disable-rpc-login</c> on a fixed loopback port, so anything that could reach
/// 127.0.0.1:18099 could call <c>sweep_all</c> on the open wallet — a web page included, because a
/// browser sends a "simple" cross-site POST to a loopback address without asking and Monero's server
/// reads a JSON body whatever its content type. Now the daemon makes its own random login (written to a
/// file only this user can read) and the wallet answers its HTTP Digest challenge.
///
/// The real daemon cannot run in this suite, so the challenge below is the one Monero's HTTP server
/// (epee, <c>contrib/epee/src/net/http_auth.cpp</c>) sends: two <c>WWW-authenticate</c> headers, MD5 and
/// MD5-sess, realm "monero-rpc", qop "auth". The fake server checks the client's answer the way epee
/// does — by recomputing it — so a client that merely sent something would fail.
/// </summary>
public sealed class MoneroRpcLoginTests
{
    [Theory]
    [InlineData("MD5", "MD5-sess")]
    [InlineData("MD5-sess", "MD5")]
    public async Task The_wallet_answers_the_daemons_digest_challenge(string first, string second)
    {
        using var daemon = new FakeEpee("monero", "s3cr3t+/base64=", first, second);
        using var http = MoneroRpcService.CreateRpcClient(new NetworkCredential("monero", "s3cr3t+/base64="));
        using var body = MoneroRpcService.RequestBody("get_version", new { });

        using var res = await http.PostAsync(daemon.Url, body);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"version\"", await res.Content.ReadAsStringAsync());
        Assert.True(daemon.Authenticated);
    }

    [Fact]
    public async Task Without_the_login_the_daemon_answers_nothing_but_401()
    {
        // What a web page, or another account on the machine, gets.
        using var daemon = new FakeEpee("monero", "s3cr3t", "MD5", "MD5-sess");
        using var http = MoneroRpcService.CreateRpcClient(new NetworkCredential());
        using var body = MoneroRpcService.RequestBody("sweep_all", new { address = "4..." });

        using var res = await http.PostAsync(daemon.Url, body);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.False(daemon.Authenticated);
    }

    [Fact]
    public async Task A_wrong_login_is_refused_too()
    {
        using var daemon = new FakeEpee("monero", "s3cr3t", "MD5", "MD5-sess");
        using var http = MoneroRpcService.CreateRpcClient(new NetworkCredential("monero", "guess"));
        using var body = MoneroRpcService.RequestBody("get_version", new { });

        using var res = await http.PostAsync(daemon.Url, body);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.False(daemon.Authenticated);
    }

    [Theory]
    [InlineData("monero:Q2hhbmdlZE9uRWFjaFN0YXJ0==\n", "monero", "Q2hhbmdlZE9uRWFjaFN0YXJ0==")]
    [InlineData("monero:a+b/c=", "monero", "a+b/c=")]
    public void The_login_file_is_read_as_user_and_password(string text, string user, string password)
    {
        var login = MoneroRpcService.ParseLogin(text);
        Assert.NotNull(login);
        Assert.Equal(user, login!.UserName);
        Assert.Equal(password, login.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("monero")]
    [InlineData(":password")]
    [InlineData("monero:")]
    [InlineData("monero:a\nother:b")]
    public void Anything_else_is_not_a_login(string? text)
    {
        Assert.Null(MoneroRpcService.ParseLogin(text));
    }

    [Fact]
    public void The_service_is_never_started_with_its_login_switched_off()
    {
        // Pinned in the source, because the daemon cannot run here: an ArgumentList line that turns the
        // login off would reopen the hole without any other test noticing.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "desktop", "src", "Umbrella.Wallet.Infrastructure",
            "Network", "MoneroRpcService.cs"));
        Assert.DoesNotMatch(new Regex(@"ArgumentList\.Add\([^)]*disable-rpc-login"), source);
        Assert.DoesNotMatch(new Regex(@"ArgumentList\.Add\([^)]*--rpc-login"), source);   // never on a command line
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }

    /// <summary>A loopback HTTP server that challenges and checks exactly as Monero's epee does.</summary>
    private sealed class FakeEpee : IDisposable
    {
        private const string Realm = "monero-rpc";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly string _user, _password;
        private readonly string[] _algorithms;
        private readonly string _nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        public FakeEpee(string user, string password, params string[] algorithms)
        {
            (_user, _password, _algorithms) = (user, password, algorithms);
            _listener.Start();
            _ = AcceptLoop();
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/json_rpc";

        public bool Authenticated { get; private set; }

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
                    var stream = client.GetStream();
                    while (true)
                    {
                        var (method, uri, headers) = await ReadRequest(stream);
                        if (method is null) return;

                        if (headers.TryGetValue("authorization", out var auth) && Valid(auth, method, uri!))
                        {
                            Authenticated = true;
                            const string json = """{"id":"0","jsonrpc":"2.0","result":{"release":true,"version":65562}}""";
                            await Write(stream, $"HTTP/1.1 200 Ok\r\nContent-Type: application/json\r\nContent-Length: {json.Length}\r\n\r\n{json}");
                            continue;
                        }

                        var challenge = new StringBuilder("HTTP/1.1 401 Unauthorized\r\n");
                        foreach (var algorithm in _algorithms)
                        {
                            challenge.Append($"WWW-authenticate: Digest qop=\"auth\",algorithm={algorithm},realm=\"{Realm}\",nonce=\"{_nonce}\",stale=false\r\n");
                        }
                        challenge.Append("Content-Type: text/html\r\nContent-Length: 0\r\n\r\n");
                        await Write(stream, challenge.ToString());
                    }
                }
                catch
                {
                    // the client hung up
                }
            }
        }

        private bool Valid(string authorization, string method, string uri)
        {
            if (!authorization.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)) return false;
            var f = Regex.Matches(authorization, @"(\w+)=(?:""([^""]*)""|([^,\s]+))")
                .ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(),
                              m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            if (f.GetValueOrDefault("username") != _user || f.GetValueOrDefault("realm") != Realm ||
                f.GetValueOrDefault("nonce") != _nonce || f.GetValueOrDefault("qop") != "auth")
                return false;

            var (nc, cnonce) = (f.GetValueOrDefault("nc", ""), f.GetValueOrDefault("cnonce", ""));
            var algorithm = f.GetValueOrDefault("algorithm", "MD5");
            var ha1 = Md5($"{_user}:{Realm}:{_password}");
            if (algorithm.Equals("MD5-sess", StringComparison.OrdinalIgnoreCase)) ha1 = Md5($"{ha1}:{_nonce}:{cnonce}");
            var ha2 = Md5($"{method}:{f.GetValueOrDefault("uri", uri)}");
            var expected = Md5($"{ha1}:{_nonce}:{nc}:{cnonce}:auth:{ha2}");
            return string.Equals(f.GetValueOrDefault("response"), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string Md5(string s) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

        private static async Task<(string? Method, string? Uri, Dictionary<string, string> Headers)> ReadRequest(NetworkStream s)
        {
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await s.ReadAsync(one) == 0) return (null, null, new());
                head.Append((char)one[0]);
            }

            var lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var parts = lines[0].Split(' ');
            var headers = lines.Skip(1)
                .Select(l => l.Split(':', 2))
                .Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First()[1].Trim());

            if (headers.TryGetValue("content-length", out var len) && int.TryParse(len, out var n) && n > 0)
            {
                var body = new byte[n];
                var read = 0;
                while (read < n)
                {
                    var got = await s.ReadAsync(body.AsMemory(read, n - read));
                    if (got == 0) break;
                    read += got;
                }
            }
            return (parts[0], parts[1], headers);
        }

        private static Task Write(NetworkStream s, string text) => s.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
