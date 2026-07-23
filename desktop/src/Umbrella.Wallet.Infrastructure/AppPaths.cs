namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Resolves where Umbrella keeps everything it writes: the encrypted vault, watch-only
/// addresses, Tor's data directory and Monero's blockchain scan cache.
///
/// The default is a <c>data</c> folder next to the executable, so the wallet is genuinely
/// portable and — importantly — its data lands on whatever drive the app was installed to
/// rather than filling up the system drive. Monero's scan cache alone can reach hundreds of
/// megabytes, which is exactly the kind of thing that should not silently eat C:.
///
/// Order of preference:
///   1. <c>UMBRELLA_DATA_DIR</c> environment variable (explicit override)
///   2. <c>&lt;app directory&gt;/data</c> when that location is writable (portable / installed to D:)
///   3. <c>%APPDATA%/UmbrellaWallet</c> (e.g. installed under Program Files, which is read-only)
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> Root = new(Resolve);

    /// <summary>Absolute path to the directory holding all Umbrella data.</summary>
    public static string DataRoot => Root.Value;

    public static string VaultFile => Path.Combine(DataRoot, "vault.json");
    public static string WatchAddressesFile => Path.Combine(DataRoot, "watch-addresses.json");
    public static string TorDirectory => Path.Combine(DataRoot, "tor");
    public static string MoneroDirectory => Path.Combine(DataRoot, "monero");

    /// <summary>The pre-1.1 locations, kept so existing installs can be migrated off C:.</summary>
    private static string LegacyRoamingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmbrellaWallet");

    private static string LegacyLocalRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmbrellaWallet");

    private static string Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable("UMBRELLA_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Directory.CreateDirectory(overridden);
            return overridden;
        }

        var beside = Path.Combine(AppContext.BaseDirectory, "data");
        if (IsWritable(beside))
        {
            return beside;
        }

        var fallback = LegacyRoamingRoot;
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            // Program Files and similar are read-only for a normal user.
            return false;
        }
    }

    /// <summary>
    /// Moves data written by earlier versions (under %APPDATA% / %LOCALAPPDATA% on the system
    /// drive) into <see cref="DataRoot"/>. Runs once at startup and never overwrites: if a file
    /// already exists at the destination the legacy copy is left alone rather than risking a
    /// vault being clobbered.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);

            MoveFileIfPresent(Path.Combine(LegacyRoamingRoot, "vault.json"), VaultFile);
            MoveFileIfPresent(Path.Combine(LegacyLocalRoot, "watch-addresses.json"), WatchAddressesFile);
            MoveDirectoryIfPresent(Path.Combine(LegacyLocalRoot, "tor"), TorDirectory);
            MoveDirectoryIfPresent(Path.Combine(LegacyLocalRoot, "monero"), MoneroDirectory);

            RemoveIfEmpty(LegacyRoamingRoot);
            RemoveIfEmpty(LegacyLocalRoot);
        }
        catch
        {
            // Migration is best-effort: a failure here must never stop the wallet from opening.
        }
    }

    private static void MoveFileIfPresent(string from, string to)
    {
        if (!File.Exists(from) || File.Exists(to)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
    }

    private static void MoveDirectoryIfPresent(string from, string to)
    {
        if (!Directory.Exists(from) || Directory.Exists(to)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
    }

    private static void RemoveIfEmpty(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
