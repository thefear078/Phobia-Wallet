using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// Finds out which of Bitcoin's and Litecoin's servers are answering BEFORE any address is sent to one.
///
/// A scan asks its first server and waits: eight seconds direct, fifteen through Tor, before it gives
/// up on a server that hangs and moves to the next. Measured through Tor on 2026-10-10, the first
/// Bitcoin server hung on two circuits out of three while two others answered in one or two seconds —
/// so the first Bitcoin balance after unlock cost fifteen seconds of waiting on a server that was
/// never going to answer.
///
/// The question asked here names nothing of the user's — the height of the chain's newest block — and
/// goes out over the same route and circuit as the scan that follows, so its answer is about the
/// servers as THAT scan will find them. Every server is asked at once; one that has not answered a
/// little after the first one did is put at the back of the queue for a few minutes
/// (<see cref="ExplorerHttp.Bench"/>). It learns nothing: no address, no balance, not even which coin
/// the wallet holds, since every installation asks the same questions.
///
/// A server the user chose is the only one asked for its chain, as everywhere: there is nothing to
/// order, and it is not probed.
/// </summary>
public static class ExplorerWarmUp
{
    /// <summary>The longest the check waits when nothing answers at all.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(6);

    /// <summary>The least the others are given after the first answer.</summary>
    public static readonly TimeSpan MinGrace = TimeSpan.FromSeconds(2);

    /// <summary>How long one check stands — a little under the bench, so a server still hanging is
    /// found again before it returns to the front.</summary>
    public static readonly TimeSpan GoodFor = TimeSpan.FromMinutes(4);

    private static readonly SemaphoreSlim Once = new(1, 1);
    private static DateTimeOffset _last = DateTimeOffset.MinValue;
    private static string _route = string.Empty;

    /// <summary>One address-free question per server the scans would otherwise ask blind.</summary>
    public static IReadOnlyList<string> Questions()
    {
        var questions = new List<string>();
        foreach (var symbol in new[] { "BTC", "LTC" })
        {
            if (ChainEndpoints.OverrideFor(symbol) is not null) continue;   // the user's server, alone
            questions.AddRange(EsploraUtxoExplorer.BasesFor(symbol).Select(b => b + "/blocks/tip/height"));
        }

        // One host serves Bitcoin, Litecoin and Dogecoin; one question tells for all three.
        if (questions.Count > 0) questions.Add(BitcoreUtxoExplorer.BaseFor("BTC") + "/block/tip");
        return questions;
    }

    /// <summary>
    /// Runs the check unless one made over the same route still stands. Never throws: a check that
    /// could not run leaves the servers in their usual order.
    /// </summary>
    public static async Task EnsureAsync(CancellationToken ct)
    {
        if (PublicHttp.RequireProxy && string.IsNullOrEmpty(PublicHttp.ActiveProxy)) return;   // nothing may leave yet
        var route = PublicHttp.ActiveProxy ?? "direct";
        if (route == _route && DateTimeOffset.UtcNow - _last < GoodFor) return;

        try
        {
            await Once.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (route == _route && DateTimeOffset.UtcNow - _last < GoodFor) return;
            var questions = Questions();
            if (questions.Count > 1)
            {
                var http = PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
                await RunAsync(
                    async (url, c) =>
                    {
                        using var res = await http.GetAsync(url, c).ConfigureAwait(false);
                        return res.IsSuccessStatusCode;
                    },
                    questions, Patience, MinGrace, url => ExplorerHttp.Bench(url), ct).ConfigureAwait(false);
            }

            _route = route;
            _last = DateTimeOffset.UtcNow;
        }
        catch
        {
            // the caller's cancel, or no network: the scan finds out for itself, as it always did
        }
        finally
        {
            Once.Release();
        }
    }

    /// <summary>Forgets the last check (tests, and a change of route).</summary>
    public static void Forget()
    {
        _last = DateTimeOffset.MinValue;
        _route = string.Empty;
    }

    /// <summary>
    /// Asks every server at once and benches the ones that have not answered by a little after the
    /// first answer: three times as long as that answer took, at least <paramref name="minGrace"/>, at
    /// most <paramref name="patience"/> in all. When nothing answers, nothing is benched — that says
    /// something about the network, not about one server against another. Returns what was benched.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RunAsync(
        Func<string, CancellationToken, Task<bool>> ask, IReadOnlyList<string> urls,
        TimeSpan patience, TimeSpan minGrace, Action<string> bench, CancellationToken ct)
    {
        if (urls.Count < 2) return [];

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = DateTimeOffset.UtcNow;
        var asks = urls.Select(async url =>
        {
            try { return (Url: url, Ok: await ask(url, stop.Token).ConfigureAwait(false)); }
            catch { return (Url: url, Ok: false); }
        }).ToList();

        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = asks.ToList();
        var deadline = started + patience;
        while (pending.Count > 0)
        {
            var left = deadline - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) break;
            var timer = Task.Delay(left, ct);
            var done = await Task.WhenAny(pending.Cast<Task>().Append(timer)).ConfigureAwait(false);
            if (done == timer)
            {
                ct.ThrowIfCancellationRequested();
                break;
            }

            var finished = pending.First(p => p == done);
            pending.Remove(finished);
            var (url, ok) = await finished.ConfigureAwait(false);
            if (!ok) continue;
            if (answered.Count == 0)
            {
                // The first answer sets how long the rest are worth waiting for.
                var took = DateTimeOffset.UtcNow - started;
                var grace = TimeSpan.FromTicks(Math.Max(minGrace.Ticks, took.Ticks * 3));
                if (started + grace < deadline) deadline = started + grace;
            }
            answered.Add(url);
        }

        stop.Cancel();
        if (answered.Count == 0) return [];

        var benched = urls.Where(u => !answered.Contains(u)).ToList();
        foreach (var url in benched) bench(url);
        return benched;
    }
}
