using System.Diagnostics;
using System.Text;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Runs the Tor client that ships inside the app (tor/tor.exe) as a child process and exposes
/// its SOCKS port. Nothing external needs to be installed — this is the "Tor built in" path.
///
/// The process is bound to the app lifetime: <see cref="Stop"/> kills it, and the port is
/// deliberately non-default (9250) so it never collides with a Tor Browser the user is running.
/// </summary>
public sealed class EmbeddedTorService : IDisposable
{
    /// <summary>Highest bootstrap percentage seen, so a timeout can say how far it got.</summary>
    private volatile int _lastBootstrapPercent;

    /// <summary>Non-default port so a user's own Tor (9050/9150) is never disturbed.</summary>
    public const int SocksPort = 9250;

    public string ProxyUri => $"socks5://127.0.0.1:{SocksPort}";

    private Process? _process;
    private readonly object _gate = new();

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>Latest bootstrap percentage parsed from Tor's log (0–100).</summary>
    public int BootstrapPercent { get; private set; }

    /// <summary>Where the bundled Tor lives once published next to the executable.</summary>
    public static string TorExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "tor",
            OperatingSystem.IsWindows() ? "tor.exe" : "tor");

    public static bool IsBundlePresent => File.Exists(TorExecutablePath);

    private static string DataDirectory => AppPaths.TorDirectory;

    /// <summary>
    /// Starts Tor and waits until it reports "Bootstrapped 100%" (or the timeout elapses).
    /// Returns (ok, message) so the UI can show exactly what happened.
    /// </summary>
    public async Task<(bool Ok, string Message)> StartAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning && BootstrapPercent >= 100)
        {
            return (true, $"Tor already running on 127.0.0.1:{SocksPort}");
        }

        if (!IsBundlePresent)
        {
            return (false, "Bundled Tor is missing from this build (tor/tor.exe not found).");
        }

        Stop();
        BootstrapPercent = 0;

        var torDir = Path.GetDirectoryName(TorExecutablePath)!;
        Directory.CreateDirectory(DataDirectory);
        var torrcPath = Path.Combine(DataDirectory, "torrc");
        await File.WriteAllTextAsync(torrcPath, BuildTorrc(torDir), cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = TorExecutablePath,
            // Writable: the install folder may be read-only under Program Files.
            WorkingDirectory = DataDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(torrcPath);

        var bootstrapped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            var percent = ParseBootstrap(e.Data);
            if (percent.HasValue) _lastBootstrapPercent = percent.Value;
            if (percent is not null)
            {
                BootstrapPercent = percent.Value;
                progress?.Report($"Tor bootstrapping… {percent.Value}%");
                if (percent.Value >= 100) bootstrapped.TrySetResult(true);
            }
        };
        process.Exited += (_, _) => bootstrapped.TrySetResult(false);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return (false, $"Could not start bundled Tor: {ex.Message}");
        }

        lock (_gate)
        {
            _process = process;
        }

        // A cold start with no cached consensus has to fetch and verify one before it can build
        // circuits — measured at ~75 s here, and slower on a poor link. 90 s was cutting it fine
        // enough to fail intermittently, which is what "Tor doesn't work" actually looked like.
        var timeout = Task.Delay(TimeSpan.FromSeconds(240), cancellationToken);
        var finished = await Task.WhenAny(bootstrapped.Task, timeout);
        if (finished != bootstrapped.Task)
        {
            var reached = _lastBootstrapPercent;
            Stop();
            return (false, reached >= 40
                // Past 40% it is fetching the consensus, so the link is up but slow or filtered.
                ? $"Tor reached {reached}% but could not finish in 4 minutes. The connection is " +
                  "very slow or Tor is being filtered on this network."
                : $"Tor stalled at {reached}% — it could not reach the Tor network at all. " +
                  "Check the connection, or whether Tor is blocked here.");
        }

        if (!bootstrapped.Task.Result)
        {
            Stop();
            return (false, "Tor exited before it finished bootstrapping.");
        }

        return (true, $"Tor ready · SOCKS5 on 127.0.0.1:{SocksPort}");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_process is null) return;
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch
            {
                // best effort — the process may already be gone
            }
            finally
            {
                _process.Dispose();
                _process = null;
                BootstrapPercent = 0;
            }
        }
    }

    private static string BuildTorrc(string torDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SocksPort 127.0.0.1:{SocksPort}");
        sb.AppendLine($"DataDirectory {DataDirectory}");
        // GeoIP files ship alongside tor.exe; without them Tor still runs but logs warnings.
        var geoip = Path.Combine(torDir, "geoip");
        var geoip6 = Path.Combine(torDir, "geoip6");
        if (File.Exists(geoip)) sb.AppendLine($"GeoIPFile {geoip}");
        if (File.Exists(geoip6)) sb.AppendLine($"GeoIPv6File {geoip6}");
        sb.AppendLine("ClientOnly 1");
        sb.AppendLine("AvoidDiskWrites 1");
        sb.AppendLine("Log notice stdout");
        return sb.ToString();
    }

    /// <summary>Extracts N from a Tor log line like "Bootstrapped 45% (requesting_descriptors)".</summary>
    internal static int? ParseBootstrap(string line)
    {
        const string marker = "Bootstrapped ";
        var idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = line[(idx + marker.Length)..];
        var pct = rest.IndexOf('%');
        if (pct <= 0) return null;
        // Tor may print "100" or "37.5"; take the integer part.
        var number = rest[..pct].Split('.')[0].Trim();
        return int.TryParse(number, out var value) ? Math.Clamp(value, 0, 100) : null;
    }

    public void Dispose() => Stop();
}
