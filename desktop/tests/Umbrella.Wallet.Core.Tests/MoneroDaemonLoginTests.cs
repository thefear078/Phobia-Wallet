using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The login, against the real monero-wallet-rpc.
///
/// <see cref="MoneroRpcLoginTests"/> proves the wallet answers an epee-shaped Digest challenge. These
/// prove the two things only the real daemon can: that started with neither <c>--rpc-login</c> nor
/// <c>--disable-rpc-login</c> it writes its login where <see cref="MoneroRpcService.LoginFileIn"/> says,
/// and that the wallet's client gets in with it — and nothing gets in without it.
///
/// The daemon is not part of this repository (it is fetched and signature-checked at build time), and
/// some machines' antivirus blocks it, so the tests run where <c>PHOBIA_MONERO_RPC</c> names the program:
/// the CI supply-chain job, right after it has fetched and verified it. Elsewhere they return at once.
/// </summary>
[Trait("Category", "MoneroDaemon")]
public sealed class MoneroDaemonLoginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-xmrd-{Guid.NewGuid():N}");
    private Process? _daemon;

    private static string? Daemon => Environment.GetEnvironmentVariable("PHOBIA_MONERO_RPC");

    [Fact]
    public async Task The_daemon_writes_its_login_and_the_wallet_gets_in_with_it_alone()
    {
        if (string.IsNullOrWhiteSpace(Daemon)) return;   // not on this machine; CI runs it
        Assert.True(File.Exists(Daemon), $"PHOBIA_MONERO_RPC names {Daemon}, which does not exist");

        var port = FreePort();
        var login = await Start(port);

        using var body = MoneroRpcService.RequestBody("get_version", new { });
        using var http = MoneroRpcService.CreateRpcClient(login);
        using var ok = await http.PostAsync($"http://127.0.0.1:{port}/json_rpc", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("\"version\"", await ok.Content.ReadAsStringAsync());

        // What a web page, or another account, gets: no login, no answer.
        using var anonymousBody = MoneroRpcService.RequestBody("get_version", new { });
        using var anonymous = MoneroRpcService.CreateRpcClient(new NetworkCredential());
        using var refused = await anonymous.PostAsync($"http://127.0.0.1:{port}/json_rpc", anonymousBody);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private async Task<NetworkCredential> Start(int port)
    {
        Directory.CreateDirectory(_dir);
        var start = new ProcessStartInfo
        {
            FileName = Daemon!,
            WorkingDirectory = _dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // The same switches MoneroRpcService passes, with a node nobody answers on: the RPC server
        // comes up without one, which is all this needs.
        start.ArgumentList.Add("--rpc-bind-ip=127.0.0.1");
        start.ArgumentList.Add($"--rpc-bind-port={port}");
        start.ArgumentList.Add($"--wallet-dir={_dir}");
        start.ArgumentList.Add("--daemon-address=127.0.0.1:1");
        start.ArgumentList.Add("--log-level=0");
        _daemon = Process.Start(start)!;
        _daemon.BeginOutputReadLine();
        _daemon.BeginErrorReadLine();

        var file = MoneroRpcService.LoginFileIn(_dir, port);
        for (var i = 0; i < 240; i++)
        {
            Assert.False(_daemon.HasExited, "monero-wallet-rpc exited during start-up");
            if (File.Exists(file))
            {
                try
                {
                    if (MoneroRpcService.ParseLogin(await File.ReadAllTextAsync(file)) is { } login && await Listening(port))
                        return login;
                }
                catch (IOException)
                {
                    // still being written
                }
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"no login file at {file} after two minutes");
    }

    private static async Task<bool> Listening(int port)
    {
        try
        {
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        try { if (_daemon is { HasExited: false }) { _daemon.Kill(entireProcessTree: true); _daemon.WaitForExit(5000); } }
        catch { /* best effort */ }
        _daemon?.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* a locked log is not worth a failure */ }
    }
}
