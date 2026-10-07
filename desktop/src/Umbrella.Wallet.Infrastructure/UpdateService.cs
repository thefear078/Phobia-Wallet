using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>How this copy of the wallet was installed, which decides what an update can do for it.</summary>
public enum InstallKind
{
    /// <summary>Installed by the Windows setup program: the new setup upgrades it in place.</summary>
    WindowsInstaller,

    /// <summary>The single-file portable exe: the new exe replaces this one.</summary>
    WindowsPortable,

    /// <summary>A plain folder on Windows (unpacked by hand): the new setup is downloaded and verified,
    /// and the person installs it.</summary>
    WindowsFolder,

    /// <summary>The Linux tarball: the new one is downloaded, verified, and on the click unpacked over this
    /// folder (the data folder is not in it) and started.</summary>
    Linux,

    /// <summary>The Android app: the check says a newer APK is out and opens its download; Android itself
    /// installs it, and only over a copy signed with the same key.</summary>
    Android,
}

/// <summary>One file attached to a release, with the SHA-256 GitHub computed for it.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>
/// A release version as the project tags it: three numbers and, for a beta, a pre-release label —
/// <c>4.10.0</c>, <c>4.10.0-beta.1</c>. Ordered by Semantic Versioning: the numbers first; at equal
/// numbers a pre-release comes BEFORE the release (4.10.0-beta.2 &lt; 4.10.0); labels compare
/// part by part, numeric parts as numbers (beta.10 &gt; beta.9).
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string Pre = "")
    : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Pre.Length > 0;

    /// <summary>"4.10.0-beta.1" — the version itself, as VERSION and the tags carry it.</summary>
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Pre}" : $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// What the release's files are named with: "Beta-1" for 4.10.0-beta.1 — a beta is called by its
    /// beta number, the way people say it — and the plain version ("4.10.0") for a full release.
    /// </summary>
    public string FileLabel =>
        Pre.StartsWith("beta.", StringComparison.OrdinalIgnoreCase) && int.TryParse(Pre["beta.".Length..], out var n)
            ? $"Beta-{n}"
            : ToString();

    public int CompareTo(ReleaseVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;

        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = int.TryParse(a[i], out var ai);
            var bn = int.TryParse(b[i], out var bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) > 0;
    public static bool operator <(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) < 0;
    public static bool operator >=(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) >= 0;
    public static bool operator <=(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) <= 0;
}

/// <summary>A published release: its version, what changed, and its files.</summary>
public sealed record ReleaseInfo(
    ReleaseVersion Version, string Tag, string Notes, DateTimeOffset? Published, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>What a check found.</summary>
public sealed record UpdateCheckResult(bool Available, ReleaseInfo? Release, string? Error);

/// <summary>A downloaded update that matched both published checksums, and where it is.</summary>
public sealed record VerifiedUpdate(ReleaseInfo Release, InstallKind Kind, string FilePath, string Sha256);

/// <summary>
/// Finding, downloading and verifying a newer release — the "updates arrive by themselves" half of the
/// wallet, done so that it cannot be turned against the person using it.
///
/// <list type="bullet">
/// <item>Every request goes through <see cref="PublicHttp"/>, so over Tor when Tor is on, and never
/// around the kill-switch. The check sends nothing but the request itself: no identifier, no version.</item>
/// <item>Only a release NEWER than the running one is ever offered — an old release with a known bug
/// cannot be pushed back onto somebody as an "update".</item>
/// <item>A file is accepted only from this project's own release downloads, under the exact name the
/// release process gives it, and only when its SHA-256 matches BOTH the release's checksum manifest and
/// the digest GitHub reports for it. A file that fails is deleted, never run.</item>
/// <item>Nothing here touches the data folder or the vault: an update replaces the program, not the wallet.</item>
/// </list>
///
/// What a checksum cannot prove is WHO built the file — both lists come from the same release page.
/// That is what the build attestations are for (<c>gh attestation verify</c>), and the wallet says so
/// rather than presenting a matching hash as more than it is.
/// </summary>
public static class UpdateService
{
    public const string ReleasesUrl = "https://github.com/thefear078/Phobia-Wallet/releases";
    private const string LatestApi = "https://api.github.com/repos/thefear078/Phobia-Wallet/releases/latest";

    /// <summary>The newest releases including pre-releases — what a beta copy reads, since "latest"
    /// on GitHub never names a pre-release.</summary>
    private const string RecentApi = "https://api.github.com/repos/thefear078/Phobia-Wallet/releases?per_page=20";
    private const string LatestPage = "https://github.com/thefear078/Phobia-Wallet/releases/latest";
    private const string DownloadPrefix = "https://github.com/thefear078/Phobia-Wallet/releases/download/";

    /// <summary>Pre-rename download URLs still resolve (GitHub redirect). Accept both so a redirected
    /// response is not refused as "not from this project".</summary>
    private const string LegacyDownloadPrefix = "https://github.com/thefear078/UmbrellaWallet/releases/download/";

    /// <summary>Where GitHub serves release files from after its redirect. A download that ends anywhere
    /// else is refused, whatever its hash.</summary>
    private static readonly string[] DownloadHosts =
    [
        "https://github.com/",
        "https://objects.githubusercontent.com/",
        "https://release-assets.githubusercontent.com/",
    ];

    /// <summary>No release file is anywhere near this; a response that is, is not one.</summary>
    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Maintenance);

    // --- checking ----------------------------------------------------------------------------------

    /// <summary>
    /// Asks GitHub for the latest release and compares it with <paramref name="current"/>. The release
    /// API comes first because it carries the notes and per-file digests; when it is unavailable (it
    /// rate-limits, and a Tor exit is shared by many people) the public release page's redirect still
    /// names the version, and the files are then checked against the manifest alone.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(string current, CancellationToken ct = default)
    {
        if (!TryParseVersion(current, out var running))
            return new UpdateCheckResult(false, null, "Could not read this app's own version.");

        // A beta reads the beta channel: every recent release, pre-releases included, the newest wins —
        // so a beta moves to the next beta, and to the full release once it is out. A full release
        // never sees a pre-release.
        if (running.IsPrerelease)
        {
            try
            {
                using var res = await Http.GetAsync(RecentApi, ct);
                if (res.IsSuccessStatusCode)
                {
                    var newest = NewestRelease(await res.Content.ReadAsStringAsync(ct), includePrereleases: true);
                    if (newest is not null) return new UpdateCheckResult(newest.Version > running, newest, null);
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // the stable check below still runs
            }
        }

        string? apiError = null;
        try
        {
            using var res = await Http.GetAsync(LatestApi, ct);
            if (res.IsSuccessStatusCode)
            {
                var release = ParseRelease(await res.Content.ReadAsStringAsync(ct));
                if (release is not null) return new UpdateCheckResult(release.Version > running, release, null);
                apiError = "GitHub's answer could not be read.";
            }
            else
            {
                apiError = $"GitHub answered {(int)res.StatusCode}.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            apiError = ex.Message;
        }

        try
        {
            using var res = await Http.GetAsync(LatestPage, ct);
            var landed = res.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
            if (res.IsSuccessStatusCode && TagFromReleaseUrl(landed) is { } tag && TryParseVersion(tag, out var latest))
            {
                var release = new ReleaseInfo(latest, tag, string.Empty, null, []);
                return new UpdateCheckResult(latest > running, release, null);
            }

            return new UpdateCheckResult(false, null, $"Could not read the latest release ({apiError}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(false, null, $"Could not reach GitHub: {ex.Message}");
        }
    }

    /// <summary>The newest usable release in a GitHub list answer (<c>/releases</c>), pre-releases
    /// included only when asked for. Drafts never.</summary>
    public static ReleaseInfo? NewestRelease(string json, bool includePrereleases)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            ReleaseInfo? best = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var release = ParseRelease(item.GetRawText(), includePrereleases);
                if (release is not null && (best is null || release.Version > best.Version)) best = release;
            }
            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The release in a GitHub API answer, or null when it is not a usable one. A draft is never offered;
    /// a pre-release only to a copy on the beta channel (<paramref name="allowPrerelease"/>).
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json, bool allowPrerelease = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (!allowPrerelease && root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.GetString() is not { Length: > 0 } tag) return null;
            if (!TryParseVersion(tag, out var version)) return null;

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
            DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.TryGetDateTimeOffset(out var at)
                ? at
                : null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                    var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                    var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
                    var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                        ? digest["sha256:".Length..].ToLowerInvariant()
                        : null;
                    assets.Add(new ReleaseAsset(name, url, size, sha));
                }
            }

            return new ReleaseInfo(version, tag, notes, published, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v4.9.0", "4.9.0" or "4.10.0-beta.1": exactly three numbers, optionally a pre-release
    /// label of letters, digits, dots and hyphens; anything else is refused.</summary>
    public static bool TryParseVersion(string? text, out ReleaseVersion version)
    {
        version = default;
        var t = (text ?? string.Empty).Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var plus = t.IndexOf('+');   // build metadata ("+sha") never orders anything
        if (plus >= 0) t = t[..plus];
        var dash = t.IndexOf('-');
        var core = dash >= 0 ? t[..dash] : t;
        var pre = dash >= 0 ? t[(dash + 1)..] : string.Empty;
        if (dash >= 0 && (pre.Length == 0 || !pre.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))) return false;
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) || major < 0 || minor < 0 || patch < 0) return false;
        version = new ReleaseVersion(major, minor, patch, pre);
        return true;
    }

    /// <summary>The tag at the end of a release page URL (<c>…/releases/tag/v4.9.0</c>), or null.</summary>
    public static string? TagFromReleaseUrl(string url)
    {
        const string marker = "/releases/tag/";
        var i = url.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var tag = Uri.UnescapeDataString(url[(i + marker.Length)..]).Trim('/');
        return tag.Length == 0 || tag.Contains('/') ? null : tag;
    }

    // --- which file ----------------------------------------------------------------------------------

    /// <summary>How the running copy was installed.</summary>
    public static InstallKind DetectInstallKind()
    {
        if (OperatingSystem.IsAndroid()) return InstallKind.Android;
        if (!OperatingSystem.IsWindows()) return InstallKind.Linux;

        // The setup program leaves its uninstaller beside the app.
        var dir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(dir, "unins000.exe"))) return InstallKind.WindowsInstaller;

        // A single-file bundle has no assembly on disk to point at.
#pragma warning disable IL3000 // Location is empty in a single-file app — which is exactly the signal.
        var single = string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
#pragma warning restore IL3000
        return single ? InstallKind.WindowsPortable : InstallKind.WindowsFolder;
    }

    /// <summary>The exact file name the release process gives the asset for this kind of install.</summary>
    public static string AssetNameFor(InstallKind kind, ReleaseVersion version) => NameFor("PhobiaWallet", kind, version);

    /// <summary>The same file under its name from before the rename (releases from 4.10.0 attach both;
    /// earlier ones only this).</summary>
    public static string LegacyAssetNameFor(InstallKind kind, ReleaseVersion version) => NameFor("UmbrellaWallet", kind, version);

    private static string NameFor(string product, InstallKind kind, ReleaseVersion version)
    {
        var v = version.FileLabel;
        return kind switch
        {
            InstallKind.WindowsPortable => $"{product}-{v}-win-x64-portable.exe",
            InstallKind.Linux => $"{product}-{v}-linux-x64.tar.gz",
            InstallKind.Android => $"{product}-{v}-android.apk",
            _ => $"{product}-Setup-{v}.exe",
        };
    }

    /// <summary>The checksum manifest's name for a version.</summary>
    public static string SumsNameFor(ReleaseVersion version) => $"SHA256SUMS-{version.FileLabel}.txt";

    /// <summary>The download URL for a file of a release — only ever this project's own.</summary>
    public static string DownloadUrl(ReleaseInfo release, string fileName) =>
        $"{DownloadPrefix}{Uri.EscapeDataString(release.Tag)}/{Uri.EscapeDataString(fileName)}";

    /// <summary>True for a URL this project's releases are actually served from.</summary>
    public static bool IsTrustedDownload(Uri? url) =>
        url is not null && url.Scheme == Uri.UriSchemeHttps &&
        DownloadHosts.Any(h => url.AbsoluteUri.StartsWith(h, StringComparison.OrdinalIgnoreCase)) &&
        (!url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         url.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase) ||
         url.AbsoluteUri.StartsWith(LegacyDownloadPrefix, StringComparison.OrdinalIgnoreCase));

    // --- checksums -----------------------------------------------------------------------------------

    /// <summary>
    /// A <c>sha256sum</c> manifest as name → lower-case hash. A line that is not a 64-hex hash and a
    /// name is ignored; a name listed twice with two different hashes makes the whole manifest unusable,
    /// because there is then no way to say which one the release meant.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[64..].TrimStart().TrimStart('*').Trim();
            if (name.Length == 0) continue;

            hash = hash.ToLowerInvariant();
            if (sums.TryGetValue(name, out var existing) && existing != hash) return null;
            sums[name] = hash;
        }

        return sums;
    }

    /// <summary>
    /// The hash a file must have, or why there is none to trust. The manifest must list it; when GitHub
    /// also reports a digest for it, the two must agree — a disagreement means one of them was changed
    /// after the release was made, and neither is used.
    /// </summary>
    public static (string? Hash, string? Error) ExpectedHash(
        IReadOnlyDictionary<string, string> sums, string fileName, string? githubDigest)
    {
        if (!sums.TryGetValue(fileName, out var listed))
            return (null, $"The release's checksum list does not name {fileName}.");
        if (githubDigest is not null && !githubDigest.Equals(listed, StringComparison.OrdinalIgnoreCase))
            return (null, "The release's checksum list and GitHub's own record of the file disagree. Nothing was installed.");
        return (listed, null);
    }

    /// <summary>
    /// True when a release found by a later check replaces an update already downloaded and waiting —
    /// a different version than the file on disk. The waiting file is then dropped, so the banner and
    /// the file it installs can never name two different versions.
    /// </summary>
    public static bool Supersedes(ReleaseInfo latest, VerifiedUpdate? waiting) =>
        waiting is not null && waiting.Release.Version != latest.Version;

    // --- downloading ---------------------------------------------------------------------------------

    /// <summary>
    /// Downloads the file for <paramref name="kind"/> into <paramref name="directory"/> and keeps it only
    /// if its SHA-256 matches. <paramref name="progress"/> gets 0..1. A file that fails is deleted.
    /// </summary>
    public static async Task<(VerifiedUpdate? Update, string? Error)> DownloadAsync(
        ReleaseInfo release, InstallKind kind, string directory,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var fileName = AssetNameFor(kind, release.Version);
        var asset = release.Assets.FirstOrDefault(a => a.Name == fileName);
        if (asset is null && release.Assets.FirstOrDefault(a => a.Name == LegacyAssetNameFor(kind, release.Version)) is { } legacy)
        {
            // A release from before the rename carries only the Umbrella-era name.
            fileName = legacy.Name;
            asset = legacy;
        }
        if (release.Assets.Count > 0 && asset is null)
            return (null, $"The release has no {fileName} to download.");

        // The manifest first: without it there is nothing to check the file against, so nothing is fetched.
        string sumsText;
        try
        {
            var sumsUrl = DownloadUrl(release, SumsNameFor(release.Version));
            using var res = await Http.GetAsync(sumsUrl, ct);
            if (!res.IsSuccessStatusCode || !IsTrustedDownload(res.RequestMessage?.RequestUri))
                return (null, $"Could not read the release's checksum list ({(int)res.StatusCode}).");
            sumsText = await res.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, $"Could not read the release's checksum list: {ex.Message}");
        }

        var sums = ParseSums(sumsText);
        if (sums is null) return (null, "The release's checksum list names one file twice with different hashes.");

        var (expected, hashError) = ExpectedHash(sums, fileName, asset?.Sha256);
        if (expected is null) return (null, hashError);

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        var partial = target + ".part";

        try
        {
            using var res = await Http.GetAsync(DownloadUrl(release, fileName), HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return (null, $"The download failed ({(int)res.StatusCode}).");
            if (!IsTrustedDownload(res.RequestMessage?.RequestUri))
                return (null, "The download was redirected somewhere this project does not publish from. Nothing was saved.");

            var total = res.Content.Headers.ContentLength ?? asset?.Size ?? 0;
            if (total > MaxDownloadBytes) return (null, "The download is far larger than any release. Nothing was saved.");

            await using (var body = await res.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                while (true)
                {
                    // A stalled connection gets a minute per chunk, not forever.
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stall.CancelAfter(TimeSpan.FromSeconds(60));
                    var n = await body.ReadAsync(buffer, stall.Token);
                    if (n == 0) break;

                    read += n;
                    if (read > MaxDownloadBytes) throw new InvalidDataException("The download is far larger than any release.");
                    sha.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
                }

                var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (actual != expected)
                {
                    file.Close();
                    TryDelete(partial);
                    return (null, "The downloaded file does not match the release's checksum. It was deleted and nothing was installed.");
                }

                file.Close();
                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target);
                progress?.Report(1.0);
                return (new VerifiedUpdate(release, kind, target, actual), null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            TryDelete(partial);
            return (null, $"The download did not finish: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-hashes a file already on disk. Used right before a downloaded update is run, so a file swapped
    /// in the download folder after it was verified is still caught.
    /// </summary>
    public static bool StillMatches(VerifiedUpdate update)
    {
        try
        {
            using var stream = File.OpenRead(update.FilePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return hash == update.Sha256;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // --- installing ----------------------------------------------------------------------------------

    /// <summary>
    /// Hands the verified update over. For the setup program that means starting it (the wallet then
    /// closes so its files can be replaced); for the portable exe it means putting the new exe where the
    /// old one was and starting it. Returns false with a reason when it could not — the old copy is then
    /// left exactly as it was.
    /// </summary>
    public static (bool Started, string? Error) Install(VerifiedUpdate update)
    {
        if (!StillMatches(update))
            return (false, "The downloaded file changed after it was checked. It was not run.");

        try
        {
            switch (update.Kind)
            {
                case InstallKind.WindowsInstaller:
                case InstallKind.WindowsFolder:
                {
                    var args = update.Kind == InstallKind.WindowsFolder
                        // Point the setup at this folder, so the update lands where the wallet already lives.
                        ? $"/DIR=\"{AppContext.BaseDirectory.TrimEnd('\\', '/')}\""
                        : string.Empty;
                    Process.Start(new ProcessStartInfo(update.FilePath, args) { UseShellExecute = true });
                    return (true, null);
                }

                case InstallKind.WindowsPortable:
                    return ReplacePortable(update.FilePath);

                case InstallKind.Linux:
                {
                    var folder = AppContext.BaseDirectory;
                    var program = UnpackOver(update.FilePath, folder);
                    Process.Start(new ProcessStartInfo(program) { UseShellExecute = false, ArgumentList = { "--after-update", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) } });
                    return (true, null);
                }

                default:
                    return (false, "This kind of install is updated outside the wallet.");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Swaps the running portable exe for the new one. Windows lets a running exe be renamed but not
    /// overwritten, so the old one steps aside as <c>.old</c> (removed on the next start), the new one
    /// takes its name, and it is started. If any step fails, the old exe is put back.
    /// </summary>
    private static (bool Started, string? Error) ReplacePortable(string newExe)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return (false, "Could not tell where this copy of the wallet is.");

        // The new copy waits for this one to exit before it starts (Program.WaitForThePreviousCopy):
        // until then this process still holds Tor's port and the data folder.
        return SwapExecutable(current, newExe, path => Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = $"--after-update {Environment.ProcessId}",
        }));
    }

    /// <summary>
    /// The swap itself, with the start step passed in so the roll-back can be tested: when the copy or
    /// the start fails, <paramref name="current"/> is the old program again and nothing is left behind
    /// under <c>.old</c>. Throws the original failure after restoring.
    /// </summary>
    public static (bool Started, string? Error) SwapExecutable(string current, string newExe, Action<string> start)
    {
        var old = current + ".old";
        TryDelete(old);
        File.Move(current, old);
        try
        {
            File.Copy(newExe, current);
            start(current);
            return (true, null);
        }
        catch
        {
            // Whatever failed — the copy or the start — the working exe goes back where it was, so the
            // copy that is running now is still the one that starts next time.
            TryDelete(current);
            File.Move(old, current);
            throw;
        }
    }

    /// <summary>
    /// Linux: unpacks a verified release tarball over <paramref name="appFolder"/> and returns the path of
    /// the new program. Every file is first written beside its target under a temporary name and then
    /// renamed over it - a rename is atomic, and Linux allows it while the old program (and its Tor) still
    /// run from the old file. The tarball holds the program, Tor and Monero and nothing else: the data
    /// folder is not in it and is never touched. Entries that would land outside the folder are refused by
    /// the tar reader itself.
    /// </summary>
    public static string UnpackOver(string tarball, string appFolder)
    {
        var unpacked = Path.Combine(Path.GetDirectoryName(tarball)!, "unpacked");
        if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
        Directory.CreateDirectory(unpacked);
        try
        {
            using (var file = File.OpenRead(tarball))
            using (var gz = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress))
            {
                System.Formats.Tar.TarFile.ExtractToDirectory(gz, unpacked, overwriteFiles: true);
            }

            // The tarball holds one folder, phobia-wallet-<version>-linux-x64, with the program in it.
            var root = Directory.GetDirectories(unpacked).SingleOrDefault(d => File.Exists(Path.Combine(d, LinuxProgram)))
                ?? throw new InvalidDataException($"The release tarball has no {LinuxProgram} in it.");

            foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(appFolder, Path.GetRelativePath(root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var staged = target + ".new";
                File.Copy(source, staged, overwrite: true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, File.GetUnixFileMode(source));
                File.Move(staged, target, overwrite: true);
            }

            return Path.Combine(appFolder, LinuxProgram);
        }
        finally
        {
            try { Directory.Delete(unpacked, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The Linux build's program file (publish-linux.sh names it).</summary>
    public const string LinuxProgram = "phobia-wallet";

    /// <summary>Removes what a previous portable update left behind. Safe to call on every start.</summary>
    public static void CleanUpAfterUpdate()
    {
        var current = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(current)) TryDelete(current + ".old");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Still in use or already gone; it is only a leftover.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
