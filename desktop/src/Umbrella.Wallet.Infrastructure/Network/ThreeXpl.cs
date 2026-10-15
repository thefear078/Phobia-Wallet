namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>
/// 3xpl's keyless API, asked the way it will answer.
///
/// It turns away requests that arrive closer together than about half a second ("429 Too Many
/// Requests") — measured 2026-10-10: of five sent at once, two were refused; the same five a little
/// apart were all answered. The wallet reads several networks from it in one refresh, so every request
/// to it goes through here: one at a time, spaced, and asked once more when it is refused all the same
/// (another user of the same Tor exit may have used up the moment).
/// </summary>
public static class ThreeXpl
{
    public const string Root = "https://sandbox-api.3xpl.com";

    /// <summary>The least time between two requests.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(1_100);

    /// <summary>How long a refused request waits before it is asked again.</summary>
    public static readonly TimeSpan RefusedWait = TimeSpan.FromSeconds(3);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>The body of a successful answer; null when it was refused, failed, or was no answer.</summary>
    public static Task<string?> GetAsync(HttpClient http, string url, CancellationToken ct) =>
        GetAsync(async (u, c) =>
        {
            using var res = await http.GetAsync(u, c);
            return ((int)res.StatusCode, res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(c) : null);
        }, url, Spacing, RefusedWait, ct);

    /// <summary>The same, over any way of asking — which is what the tests drive.</summary>
    public static async Task<string?> GetAsync(
        Func<string, CancellationToken, Task<(int Status, string? Body)>> ask, string url,
        TimeSpan spacing, TimeSpan refusedWait, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Gate.WaitAsync(ct);
            int status;
            try
            {
                var wait = _last + (attempt == 0 ? spacing : refusedWait) - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                string? body;
                try
                {
                    (status, body) = await ask(url, ct);
                }
                finally
                {
                    _last = DateTimeOffset.UtcNow;
                }

                if (status is >= 200 and < 300) return body;
            }
            finally
            {
                Gate.Release();
            }

            if (status != 429) return null;   // refused for a reason asking again will not change
        }

        return null;
    }
}
