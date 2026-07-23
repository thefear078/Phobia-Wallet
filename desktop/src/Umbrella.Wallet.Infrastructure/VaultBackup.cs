using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>Everything a backup carries, so a restore rebuilds the wallet as it was.</summary>
public sealed record VaultBackupContents(
    string Vault,
    string? WatchAddresses,
    string? Exchanges);

/// <summary>
/// Export and restore of the encrypted vault.
///
/// The backup is a copy of the already-encrypted vault plus the non-secret side files — it is
/// never decrypted on the way out, so the exported file is exactly as safe as the vault itself
/// and is useless without the password. That also means a backup can only be restored with the
/// password that made it; there is no recovery path, by design.
/// </summary>
public static class VaultBackup
{
    private const string Magic = "umbrella-backup-v1";

    /// <summary>Writes a backup bundle. Returns the byte count so the UI can confirm it wrote.</summary>
    public static async Task<(bool Ok, string Message)> ExportAsync(
        string destinationPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(AppPaths.VaultFile))
            {
                return (false, "There is no vault on this PC to back up yet.");
            }

            var bundle = new Dictionary<string, string?>
            {
                ["magic"] = Magic,
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["vault"] = await File.ReadAllTextAsync(AppPaths.VaultFile, ct),
                ["watchAddresses"] = await ReadIfPresentAsync(AppPaths.WatchAddressesFile, ct),
                // Encrypted with a key derived from the seed, so it stays sealed in the backup too.
                ["exchanges"] = await ReadBytesAsBase64Async(
                    Path.Combine(AppPaths.DataRoot, "exchanges.bin"), ct),
            };

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(destinationPath, json, ct);

            var size = new FileInfo(destinationPath).Length;
            return (true, $"Backup written · {size:N0} bytes · still encrypted with your password");
        }
        catch (Exception ex)
        {
            return (false, $"Backup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores a backup over the current data directory. The existing vault is moved aside
    /// rather than deleted, so a mistaken restore is recoverable.
    /// </summary>
    public static async Task<(bool Ok, string Message)> RestoreAsync(
        string sourcePath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(sourcePath)) return (false, "That backup file does not exist.");

            var json = await File.ReadAllTextAsync(sourcePath, ct);
            Dictionary<string, string?>? bundle;
            try
            {
                bundle = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            }
            catch
            {
                return (false, "That file is not an Umbrella backup.");
            }

            if (bundle is null ||
                !bundle.TryGetValue("magic", out var magic) || magic != Magic)
            {
                return (false, "That file is not an Umbrella backup.");
            }

            if (!bundle.TryGetValue("vault", out var vault) || string.IsNullOrWhiteSpace(vault))
            {
                return (false, "The backup does not contain a vault.");
            }

            // Verify it parses as a vault before touching anything on disk.
            try
            {
                using var probe = JsonDocument.Parse(vault);
                if (!probe.RootElement.TryGetProperty("Version", out _) &&
                    !probe.RootElement.TryGetProperty("version", out _))
                {
                    return (false, "The backup's vault looks corrupt.");
                }
            }
            catch
            {
                return (false, "The backup's vault looks corrupt.");
            }

            Directory.CreateDirectory(AppPaths.DataRoot);

            if (File.Exists(AppPaths.VaultFile))
            {
                var aside = $"{AppPaths.VaultFile}.replaced-{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Move(AppPaths.VaultFile, aside);
            }

            await File.WriteAllTextAsync(AppPaths.VaultFile, vault, ct);

            if (bundle.TryGetValue("watchAddresses", out var watch) && !string.IsNullOrWhiteSpace(watch))
            {
                await File.WriteAllTextAsync(AppPaths.WatchAddressesFile, watch, ct);
            }

            if (bundle.TryGetValue("exchanges", out var exchanges) && !string.IsNullOrWhiteSpace(exchanges))
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(AppPaths.DataRoot, "exchanges.bin"), Convert.FromBase64String(exchanges), ct);
            }

            return (true, "Backup restored · unlock with the password that made it");
        }
        catch (Exception ex)
        {
            return (false, $"Restore failed: {ex.Message}");
        }
    }

    /// <summary>A default filename that sorts by date and says what it is.</summary>
    public static string SuggestedFileName() =>
        $"umbrella-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.json";

    private static async Task<string?> ReadIfPresentAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;

    private static async Task<string?> ReadBytesAsBase64Async(string path, CancellationToken ct) =>
        File.Exists(path) ? Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)) : null;
}
