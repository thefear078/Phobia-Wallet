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
        // A Tor this wallet left behind — a crash, a forced close, an older version that had no way to
        // tie Tor to the wallet — still holds the port and the data folder, and a new one then exits at
        // once. That was "Tor exited before it finished bootstrapping" on every launch, which for a
        // Tor-only wallet means no balance, no price, nothing.
        StopLeftoverTor();
        BootstrapPercent = 0;
        _lastLogProblem = null;

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
        // Tor exits by itself (within seconds) once this process is gone, so a wallet that dies without
        // reaching Stop() — killed, crashed, closed by an update — leaves no Tor behind. The same option
        // Tor Browser uses for its own Tor.
        startInfo.ArgumentList.Add("__OwningControllerProcess");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var bootstrapped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            if (e.Data.Contains("[err]", StringComparison.Ordinal) || e.Data.Contains("[warn]", StringComparison.Ordinal))
                _lastLogProblem = e.Data;
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
            return (false, "Tor exited before it finished bootstrapping" +
                           (ProblemFromLog(_lastLogProblem) is { } why ? $": {why}" : "."));
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

    /// <summary>The last warning or error Tor logged, to say why it stopped.</summary>
    private volatile string? _lastLogProblem;

    private static string PidFilePath => Path.Combine(DataDirectory, "tor.pid");

    /// <summary>
    /// Ends a Tor this wallet started earlier and did not stop: the one named in its pid file, and —
    /// for a Tor from a version that wrote no pid file — any running copy of this wallet's own bundled
    /// tor.exe. A Tor the user runs themselves (Tor Browser, a system Tor) is a different program file
    /// and is never touched.
    /// </summary>
    private static void StopLeftoverTor()
    {
        var leftovers = new List<Process>();
        try
        {
            if (File.Exists(PidFilePath) &&
                int.TryParse(File.ReadAllText(PidFilePath).Trim(), out var pid) && pid != Environment.ProcessId)
            {
                var p = Process.GetProcessById(pid);
                if (p.ProcessName.Equals("tor", StringComparison.OrdinalIgnoreCase)) leftovers.Add(p);
                else p.Dispose();
            }
        }
        catch
        {
            // No such process any more — nothing to stop.
        }

        foreach (var p in Process.GetProcessesByName("tor"))
        {
            if (leftovers.Any(l => l.Id == p.Id)) { p.Dispose(); continue; }
            try
            {
                if (string.Equals(p.MainModule?.FileName, TorExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    leftovers.Add(p);
                    continue;
                }
            }
            catch
            {
                // Another user's process, or one that just exited.
            }

            p.Dispose();
        }

        foreach (var p in leftovers)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
            }
            catch
            {
                // Best effort; if it survives, the start below reports why it could not run.
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    /// <summary>Tor's own words for why it could not run, without the timestamp and log level.</summary>
    public static string? ProblemFromLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var end = line.IndexOf("] ", StringComparison.Ordinal);
        var text = (end >= 0 ? line[(end + 2)..] : line).Trim();
        return text.Length > 160 ? text[..160] + "…" : text;
    }

    private static string BuildTorrc(string torDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SocksPort 127.0.0.1:{SocksPort}");
        sb.AppendLine($"DataDirectory {DataDirectory}");
        sb.AppendLine($"PidFile {PidFilePath}");
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
