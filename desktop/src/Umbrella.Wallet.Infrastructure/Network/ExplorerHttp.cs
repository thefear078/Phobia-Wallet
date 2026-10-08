using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Requests to the free public explorers, made the way a guest should make them: only a few at a time
/// to any one host, spaced out where the host asks for it, and when a server answers "not now" — 429,
/// a 5xx, a timeout — the wallet waits and asks again instead of turning a busy moment into an
/// unreadable balance on the user's screen. A definite answer (200, 400, 404) is returned at once.
///
/// The HttpClient still comes from <see cref="PublicHttp"/>: Tor, the proxy and the kill-switch apply
/// exactly as before. This only decides WHEN a request is sent, never HOW.
/// </summary>
public static class ExplorerHttp
{
    private static readonly ConcurrentDictionary<string, HostGate> Gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The longest the wallet will wait before asking again, whatever Retry-After says.
    /// An hour-long ban is not something to sit through; the scan reports "unknown" instead.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long one request to an explorer may take before it counts as "not answering": eight seconds
    /// direct, fifteen through Tor or a proxy (a circuit adds a few seconds of its own). An address lookup
    /// is answered in well under a second by a healthy server. The client's own limit is twenty seconds,
    /// and a server that hangs (mempool.space took ten-plus seconds a request one evening, Cloudflare sat
    /// on litecoinspace for twenty before a 522) held a whole scan back that long, request after request,
    /// before the next server was tried. A timed-out server is benched like a refusing one.
    /// </summary>
    public static TimeSpan AttemptDeadline =>
        PublicHttp.ActiveProxy is null ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(15);

    /// <summary>
    /// GET <paramref name="url"/>, trying up to <paramref name="attempts"/> times while the answer is
    /// transient. Returns the last response (success or not) for the caller to judge; throws only
    /// when no response ever came back, or on the caller's own cancel.
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient http, string url, CancellationToken ct, int attempts = 3)
    {
        var gate = GateFor(url);
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            TimeSpan wait;

            await gate.EnterAsync(ct).ConfigureAwait(false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(AttemptDeadline);
                response = await http.GetAsync(url, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < attempts && IsTransient(ex))
            {
                // No answer yet — reset or timed out. Worth one more try after a pause. A refused
                // connection or an unknown host is an answer, and is not retried.
            }
            finally
            {
                gate.Exit();
            }

            if (response is not null)
            {
                if (response.IsSuccessStatusCode || !IsTransient(response.StatusCode) || attempt >= attempts)
                    return response;

                wait = Backoff(attempt, response.Headers.RetryAfter);
                response.Dispose();
            }
            else
            {
                wait = Backoff(attempt, null);
            }

            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    // --- Which servers are refusing right now ---------------------------------------------------------

    private static readonly ConcurrentDictionary<string, DateTimeOffset> Benched = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a server that said "too many requests" or did not answer is left alone, unless
    /// it said for how long itself.</summary>
    public static readonly TimeSpan BenchFor = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Records that <paramref name="url"/>'s server refused (429/5xx) or did not answer in time, so every
    /// explorer asks its other servers first for a while. Without this each request — six at a time in a
    /// scan, and again on every refresh — went to the refusing server first and waited on it: a fresh
    /// wallet's Bitcoin scan took over four minutes while mempool.space answered in 40 ms.
    /// </summary>
    public static void Bench(string url, TimeSpan? retryAfter = null)
    {
        var host = HostOf(url);
        var until = DateTimeOffset.UtcNow + (retryAfter is { } r && r > TimeSpan.Zero && r < TimeSpan.FromHours(1) ? r : BenchFor);
        Benched.AddOrUpdate(host, until, (_, old) => old > until ? old : until);
    }

    /// <summary>True while the server behind <paramref name="url"/> is benched.</summary>
    public static bool IsBenched(string url) =>
        Benched.TryGetValue(HostOf(url), out var until) && until > DateTimeOffset.UtcNow;

    /// <summary>Clears every bench (tests, and a change of route such as switching Tor on).</summary>
    public static void ForgetBenches() => Benched.Clear();

    /// <summary>The servers in the order to ask them: the ones not benched first, each group in its
    /// original order — so the user's or the shipped preference still decides among healthy servers.</summary>
    public static IReadOnlyList<string> HealthyFirst(IReadOnlyList<string> urls) =>
        urls.Where(u => !IsBenched(u)).Concat(urls.Where(IsBenched)).ToList();

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    /// <summary>Answers that mean "not now" rather than "no".</summary>
    public static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// True for a failure worth another try: a timeout, a dropped connection, or a "not now" status.
    /// A refused connection, an unknown host or the kill-switch blocking a request are definite — the
    /// same request a second later gets the same answer, so it is not worth the wait.
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        if (ex is TimeoutException or TaskCanceledException) return true;
        if (ex is not HttpRequestException http) return false;
        if (http.StatusCode is { } code) return IsTransient(code);

        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socket)
            {
                return socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted
                    or SocketError.TimedOut or SocketError.TryAgain or SocketError.NetworkReset;
            }

            if (inner is IOException or TimeoutException) return true;   // connection dropped mid-answer
        }

        return false;
    }

    /// <summary>How long to wait before attempt <paramref name="attempt"/> + 1: what the server asked
    /// for when it said, otherwise 0.8 s, 1.6 s, 3.2 s… with a little jitter so parallel probes do not
    /// all come back at the same instant — never more than <see cref="MaxWait"/>.</summary>
    public static TimeSpan Backoff(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        TimeSpan wait;
        if (retryAfter?.Delta is { } delta) wait = delta;
        else if (retryAfter?.Date is { } date) wait = date - DateTimeOffset.UtcNow;
        else wait = TimeSpan.FromMilliseconds(800 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));

        if (wait < TimeSpan.FromMilliseconds(200)) wait = TimeSpan.FromMilliseconds(200);
        return wait > MaxWait ? MaxWait : wait;
    }

    /// <summary>
    /// How politely each host is treated. BlockCypher's keyless tier allows three requests a second,
    /// so it gets one at a time, spaced; public explorers get four in flight. A node on this machine —
    /// the user's own — is nobody else's server and is not throttled.
    /// </summary>
    private static HostGate GateFor(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        return Gates.GetOrAdd(host, h =>
            IsLocal(uri) ? new HostGate(64, TimeSpan.Zero)
            : h.EndsWith("blockcypher.com", StringComparison.OrdinalIgnoreCase) ? new HostGate(1, TimeSpan.FromMilliseconds(400))
            : new HostGate(4, TimeSpan.Zero));
    }

    private static bool IsLocal(Uri? uri) =>
        uri is not null && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    private sealed class HostGate(int maxInFlight, TimeSpan spacing)
    {
        private readonly SemaphoreSlim _slots = new(maxInFlight, maxInFlight);
        private readonly SemaphoreSlim _pace = new(1, 1);
        private DateTimeOffset _next = DateTimeOffset.MinValue;

        public async Task EnterAsync(CancellationToken ct)
        {
            await _slots.WaitAsync(ct).ConfigureAwait(false);
            if (spacing <= TimeSpan.Zero) return;

            try
            {
                await _pace.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    if (_next > now) await Task.Delay(_next - now, ct).ConfigureAwait(false);
                    _next = DateTimeOffset.UtcNow + spacing;
                }
                finally
                {
                    _pace.Release();
                }
            }
            catch
            {
                _slots.Release();   // never leak a slot to a cancel
                throw;
            }
        }

        public void Exit() => _slots.Release();
    }
}
