using System.Diagnostics;
using Umbrella.Wallet.Core.Chains;
using System.Net.Http.Json;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record MoneroBalance(decimal Total, decimal Unlocked, ulong ScannedHeight, ulong ChainHeight)
{
    public bool Synced => ChainHeight > 0 && ScannedHeight + 2 >= ChainHeight;

    public int PercentSynced => ChainHeight == 0
        ? 0
        : (int)Math.Clamp(ScannedHeight * 100.0 / ChainHeight, 0, 100);
}

/// <summary>
/// How a Monero send ended. <paramref name="Unclear"/> means the wallet was asked to relay and never
/// answered: the transaction may be on the network, so it must not be offered as a retry.
/// </summary>
public sealed record MoneroSendResult(bool Ok, string? TxHash, decimal FeeXmr, string? Error, bool Unclear = false);

/// <summary>
/// Drives the bundled <c>monero-wallet-rpc</c> so Monero is a first-class coin — real balance and
/// real sending, not just an address.
///
/// Monero amounts are hidden on-chain, so a balance only exists after scanning with the view key,
/// and spending requires RingCT + Bulletproofs. Re-implementing that would be reckless, so we run
/// Monero's own audited binary locally and speak JSON-RPC to it. The wallet is restored from the
/// keys Umbrella already derives; those keys never leave this machine, and the daemon we point at
/// only ever sees encrypted-by-design Monero traffic (through Tor when Tor is on).
/// </summary>
public sealed class MoneroRpcService : IDisposable
{
    /// <summary>Private port so it cannot collide with a user's own monero-wallet-rpc.</summary>
    private const int RpcPort = 18099;

    /// <summary>1 XMR = 10^12 piconero.</summary>
    private const decimal Piconero = 1_000_000_000_000m;

    /// <summary>
    /// Where a wallet derived from the recovery phrase starts scanning when it is first created on a
    /// device. It used to be "the last ~30 days", which made a restore lose money: on a new PC, or after
    /// a wipe, Monero received more than a month earlier was never found and could not be spent.
    ///
    /// The account comes from Umbrella's own derivation ("umbrella-monero-v1"), which first shipped on
    /// 2026-07-23 — no coin can have been sent to it before that. Block 3,700,000 was mined on
    /// 2026-06-19 (the same hash and time on two independent public nodes), a month earlier, so scanning
    /// from it finds everything the account has ever received. The first scan is longer; nothing is missed.
    /// </summary>
    public const ulong DerivedAccountFloorHeight = 3_700_000;

    /// <summary>Written beside a wallet file this build created, recording the height it scans from.
    /// A wallet without it was created by an older build that scanned only the last ~30 days.</summary>
    public const string ScanFromSuffix = ".umbrella-scan-from";

    /// <summary>
    /// True when a wallet file exists that was created before scans started at the full range — so it
    /// may be missing older funds and must be restored again. Pure, so the rule is testable.
    /// </summary>
    public static bool NeedsFullRestore(string walletFile) =>
        File.Exists(walletFile) && !File.Exists(walletFile + ScanFromSuffix);

    /// <summary>
    /// The remote node this wallet asks about the chain — the single most consequential setting on
    /// Monero, and one this service used to make silently. Set it before <see cref="StartAsync"/>;
    /// empty means the catalog's default.
    ///
    /// The node never sees the keys, the balance, the addresses or the amounts. It does see the IP
    /// that connected (an exit node, with Tor on), that the IP belongs to a Monero wallet, roughly
    /// which blocks were asked for, and which connection a transaction entered the network through.
    /// </summary>
    public string NodeAddress { get; set; } = string.Empty;

    /// <summary>The node the running daemon is actually talking to, so the UI can name it rather than
    /// implying the chosen one was reached.</summary>
    public string ActiveNode { get; private set; } = string.Empty;

    // The daemon is on loopback. A default HttpClient honours the system proxy, which can
    // route or block 127.0.0.1 — and would send local RPC through Tor once Tor is on.
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromMinutes(3),
    };
    private Process? _process;
    private readonly object _gate = new();
    private string? _walletName;

    public static string ExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "monero",
            OperatingSystem.IsWindows() ? "monero-wallet-rpc.exe" : "monero-wallet-rpc");

    public static bool IsBundlePresent => File.Exists(ExecutablePath);

    public bool IsRunning
    {
        get { lock (_gate) { return _process is { HasExited: false }; } }
    }

    // Monero's scan cache can reach hundreds of MB, so it must not default to the system drive.
    private static string WalletDirectory => AppPaths.MoneroDirectory;

    /// <summary>
    /// Boots monero-wallet-rpc and restores the account from its keys. Safe to call repeatedly —
    /// if the wallet already exists on disk it is simply opened.
    /// </summary>
    /// <param name="scanFrom">The block to scan from when the wallet is first created here. Null means
    /// <see cref="DerivedAccountFloorHeight"/> — right for the account derived from the recovery phrase.
    /// An imported Monero seed passes its own (0 when its age is unknown).</param>
    public async Task<(bool Ok, string Message)> StartAsync(
        string address,
        string secretSpendKey,
        string secretViewKey,
        string walletPassword,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        ulong? scanFrom = null)
    {
        if (!IsBundlePresent)
        {
            return (false, "Bundled monero-wallet-rpc is missing from this build.");
        }

        if (!IsRunning)
        {
            var started = await LaunchAsync(progress, ct);
            if (!started.Ok) return started;
        }

        // Deterministic wallet file name per account, so re-opening finds the same one.
        _walletName = "umbrella-" + address[..12].ToLowerInvariant();
        var walletFile = Path.Combine(WalletDirectory, _walletName);

        // A wallet an older build created scans only from ~30 days before it was made. It is moved aside —
        // renamed, never deleted, since it holds this device's transaction keys — and restored again from
        // the same keys over the full range, once.
        if (NeedsFullRestore(walletFile))
        {
            progress?.Report("Re-reading the full Monero history for this account…");
            await CallAsync("close_wallet", new { }, ct);
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var file in new[] { walletFile, walletFile + ".keys" })
            {
                if (File.Exists(file)) File.Move(file, $"{file}.before-full-scan-{stamp}");
            }
        }

        if (File.Exists(walletFile))
        {
            progress?.Report("Opening Monero wallet…");
            var opened = await CallAsync("open_wallet", new
            {
                filename = _walletName,
                password = walletPassword,
            }, ct);
            if (opened.Error is not null && !opened.Error.Contains("already open", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Could not open the Monero wallet: {opened.Error}");
            }
        }
        else
        {
            progress?.Report("Restoring Monero wallet from keys…");
            var restoreHeight = scanFrom ?? DerivedAccountFloorHeight;

            var created = await CallAsync("generate_from_keys", new
            {
                restore_height = restoreHeight,
                filename = _walletName,
                address,
                spendkey = secretSpendKey,
                viewkey = secretViewKey,
                password = walletPassword,
                autosave_current = true,
            }, ct);
            if (created.Error is not null)
            {
                return (false, $"Could not restore the Monero wallet: {created.Error}");
            }

            File.WriteAllText(walletFile + ScanFromSuffix,
                restoreHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        progress?.Report("Scanning the Monero chain…");
        return (true, "Monero wallet ready");
    }

    /// <summary>Balance plus scan progress, so the UI can say "still syncing" instead of a wrong 0.</summary>
    public async Task<MoneroBalance?> GetBalanceAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return null;

        var balance = await CallAsync("get_balance", new { account_index = 0 }, ct);
        if (balance.Result is null) return null;

        var total = balance.Result.Value.TryGetProperty("balance", out var b) ? b.GetUInt64() : 0;
        var unlocked = balance.Result.Value.TryGetProperty("unlocked_balance", out var u) ? u.GetUInt64() : 0;

        var heights = await CallAsync("get_height", new { }, ct);
        var scanned = heights.Result?.TryGetProperty("height", out var hh) == true ? hh.GetUInt64() : 0;
        var chain = await GetChainHeightAsync(ct);

        return new MoneroBalance(total / Piconero, unlocked / Piconero, scanned, chain);
    }

    /// <summary>Builds, signs and relays a real Monero transaction through the local RPC wallet.</summary>
    /// <param name="feeAddress">Optional developer-fee recipient, sent as a second destination in
    /// the SAME transaction (one network fee). The caller must have validated it — an invalid
    /// address would make the whole transfer fail, so pass null rather than risk the user's send.</param>
    /// <param name="feeAmount">Developer fee, on top of <paramref name="amountXmr"/>.</param>
    public async Task<MoneroSendResult> SendAsync(
        string toAddress, decimal amountXmr,
        string? feeAddress = null, decimal feeAmount = 0m, CancellationToken ct = default)
    {
        if (!IsRunning)
        {
            return new MoneroSendResult(false, null, 0, "Monero wallet is not running.");
        }

        var piconero = (ulong)(amountXmr * Piconero);
        var destinations = new List<object> { new { amount = piconero, address = toAddress } };
        if (!string.IsNullOrWhiteSpace(feeAddress) && feeAmount > 0)
        {
            var feePico = (ulong)(feeAmount * Piconero);
            if (feePico > 0) destinations.Add(new { amount = feePico, address = feeAddress });
        }

        var response = await CallAsync("transfer", new
        {
            destinations = destinations.ToArray(),
            account_index = 0,
            priority = 1,
            get_tx_key = true,
        }, ct);

        if (response.Error is not null)
        {
            // The wallet refuses on its own terms before relaying anything (not enough money, a bad
            // address, no unlocked outputs). Anything else — a dead socket, a crash after relaying — is
            // settled against the wallet's own record of what it sent.
            var refused = new[] { "not enough", "no unlocked", "invalid address", "failed to parse", "transaction too large", "daemon is busy" }
                .Any(e => response.Error.Contains(e, StringComparison.OrdinalIgnoreCase));
            return refused
                ? new MoneroSendResult(false, null, 0, response.Error)
                : await SettleAsync(toAddress, piconero, $"The Monero wallet's answer was unclear: {response.Error}", ct);
        }

        var hash = response.Result?.TryGetProperty("tx_hash", out var th) == true ? th.GetString() : null;
        var fee = response.Result?.TryGetProperty("fee", out var f) == true ? f.GetUInt64() / Piconero : 0m;
        return hash is null
            ? new MoneroSendResult(false, null, 0, "The wallet did not return a transaction hash.")
            : new MoneroSendResult(true, hash, fee, null);
    }

    /// <summary>
    /// Asks the wallet what it has actually sent. A relayed transfer shows up in its own outgoing list
    /// (pool included) with the destination and amount it was built for, so an answer lost on the way
    /// back can still be settled instead of being reported as a failure to try again.
    /// </summary>
    private async Task<MoneroSendResult> SettleAsync(string toAddress, ulong piconero, string why, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : 5), ct);
            var (result, error) = await CallAsync("get_transfers", new { @out = true, pending = true, pool = true, account_index = 0 }, ct);
            if (error is not null || result is not { } transfers) continue;

            foreach (var list in new[] { "out", "pending", "pool" })
            {
                if (!transfers.TryGetProperty(list, out var entries) || entries.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("destinations", out var destinations) || destinations.ValueKind != JsonValueKind.Array) continue;
                    foreach (var destination in destinations.EnumerateArray())
                    {
                        var address = destination.TryGetProperty("address", out var a) ? a.GetString() : null;
                        var amount = destination.TryGetProperty("amount", out var m) && m.TryGetUInt64(out var v) ? v : 0;
                        if (address != toAddress || amount != piconero) continue;

                        var hash = entry.TryGetProperty("txid", out var t) ? t.GetString() : null;
                        var fee = entry.TryGetProperty("fee", out var f) && f.TryGetUInt64(out var fv) ? fv / Piconero : 0m;
                        return new MoneroSendResult(true, hash, fee, null);
                    }
                }
            }
        }

        return new MoneroSendResult(false, null, 0,
            $"{why} The transfer may already have been relayed — check your Monero transfers before sending again, " +
            "because a second send would spend different outputs and pay twice.",
            Unclear: true);
    }

    private async Task<(bool Ok, string Message)> LaunchAsync(IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(WalletDirectory);
        Stop();

        // Route through Tor when it's on, so the remote node never sees the real IP.
        var proxy = PublicHttp.ActiveProxy;

        // Fail-closed, exactly like the clearnet kill-switch: in Tor-only mode we must NOT start the
        // daemon without a proxy, or it would connect straight to a public node and leak the real IP —
        // the precise de-anonymisation the kill-switch exists to prevent. XMR is the privacy coin; hold
        // this line rather than quietly exposing the user.
        if (PublicHttp.RequireProxy && string.IsNullOrWhiteSpace(proxy))
        {
            return (false, "Tor-only mode is on but Tor is not connected — the Monero node connection is " +
                "blocked (starting it would expose your IP). Connect Tor, or turn Tor-only mode off.");
        }

        // The user's choice, or the catalog default. Never a silent substitution: if the chosen node
        // cannot be used under the current network settings, this refuses and says why rather than
        // connecting the wallet to a different stranger's machine.
        var chosen = MoneroNodeCatalog.Resolve(
            NodeAddress,
            torConnected: !string.IsNullOrWhiteSpace(proxy),
            killSwitchArmed: PublicHttp.RequireProxy);

        if (chosen is null)
        {
            return MoneroNode.TryParse(NodeAddress, out var wanted) && wanted.IsOnion
                ? (false, "That Monero node is a .onion address, which only works over Tor. " +
                          "Turn Tor on, or choose a different node.")
                : (false, "Every Monero node is blocked right now: Tor-only mode is on and Tor is not " +
                          "connected. Connect Tor, or turn Tor-only mode off.");
        }

        var node = chosen.Address;
        ActiveNode = node;

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            // Must be a writable directory. The daemon drops its log beside the working
            // directory, so pointing this at the install folder made it fail outright under
            // Program Files, where the app has no write access.
            WorkingDirectory = WalletDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--rpc-bind-ip=127.0.0.1");
        startInfo.ArgumentList.Add($"--rpc-bind-port={RpcPort}");
        startInfo.ArgumentList.Add("--disable-rpc-login");
        startInfo.ArgumentList.Add($"--wallet-dir={WalletDirectory}");
        startInfo.ArgumentList.Add($"--daemon-address={node}");
        startInfo.ArgumentList.Add($"--log-file={Path.Combine(WalletDirectory, "monero-wallet-rpc.log")}");
        startInfo.ArgumentList.Add("--log-level=0");
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            // monero-wallet-rpc wants host:port, not a socks5:// URI.
            var uri = new Uri(proxy);
            startInfo.ArgumentList.Add($"--proxy={uri.Host}:{uri.Port}");
        }

        if (!File.Exists(ExecutablePath))
        {
            return (false,
                "monero-wallet-rpc.exe is missing from this build. Reinstall Umbrella, or run " +
                "scripts/fetch-monero.ps1 to stage it.");
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // Keep the daemon's own words: a generic timeout tells the user nothing actionable.
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return (false, $"Could not start monero-wallet-rpc: {ex.Message}");
        }

        lock (_gate) { _process = process; }

        progress?.Report("Starting Monero wallet service…");

        // 90 s, not 20: the daemon is a 39 MB unsigned binary, and on first run an antivirus
        // scan alone can outlast a short timeout on a cold disk.
        const int attempts = 180;
        for (var i = 0; i < attempts; i++)
        {
            if (process.HasExited)
            {
                return (false, $"monero-wallet-rpc exited during startup. {Tail(output)}");
            }

            await Task.Delay(500, ct);
            var probe = await CallAsync("get_version", new { }, ct);
            if (probe.Result is not null) return (true, "Monero service ready");

            if (i % 20 == 19)
            {
                progress?.Report($"Still starting the Monero service… ({(i + 1) / 2} s)");
            }
        }

        Stop();
        return (false, $"monero-wallet-rpc did not become ready in 90 s. {Tail(output)}");
    }

    /// <summary>Last few lines the daemon printed, for a message the user can act on.</summary>
    private static string Tail(System.Text.StringBuilder output)
    {
        var lines = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return "The daemon printed nothing.";
        return string.Join(" | ", lines.TakeLast(3));
    }

    private async Task<ulong> GetChainHeightAsync(CancellationToken ct)
    {
        var response = await CallAsync("get_height", new { }, ct);
        return response.Result?.TryGetProperty("height", out var h) == true ? h.GetUInt64() : 0;
    }

    /// <summary>
    /// A JSON-RPC request body with its length stated. Monero's HTTP server (epee — the same code in
    /// monerod and monero-wallet-rpc) does not read a chunked body: it answers "Invalid Request", and
    /// <c>PostAsJsonAsync</c> sends exactly that, because JSON content has no length until it is written.
    /// Every call went out that way until this was found by asking two public Monero nodes the same
    /// question both ways; a buffered string carries a Content-Length and is read.
    /// </summary>
    public static HttpContent RequestBody(string method, object parameters) =>
        new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = "0", method, @params = parameters }),
            System.Text.Encoding.UTF8,
            "application/json");

    private async Task<(JsonElement? Result, string? Error)> CallAsync(
        string method, object parameters, CancellationToken ct)
    {
        try
        {
            using var body = RequestBody(method, parameters);
            using var res = await _http.PostAsync($"http://127.0.0.1:{RpcPort}/json_rpc", body, ct);
            if (!res.IsSuccessStatusCode) return (null, $"RPC HTTP {(int)res.StatusCode}");

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "RPC error";
                return (null, message);
            }

            return doc.RootElement.TryGetProperty("result", out var result)
                ? (result.Clone(), null)
                : (null, "RPC returned no result");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
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
            catch { /* best effort */ }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
