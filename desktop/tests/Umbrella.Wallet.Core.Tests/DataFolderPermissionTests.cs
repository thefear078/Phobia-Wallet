using Umbrella.Wallet.Infrastructure;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// On Linux and macOS the wallet's folder and files are its owner's alone. The default umask made them
/// 0755 / 0644: any other account on the computer could read the address book, the watched addresses,
/// the activity log and the settings. CI runs these on Linux; on Windows (profile ACLs) they return.
/// </summary>
public sealed class DataFolderPermissionTests
{
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void A_file_the_wallet_writes_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("phobia-perm-").FullName;
        try
        {
            var file = Path.Combine(dir, "address-book.json");
            AtomicFile.WriteAllText(file, "[]");
            Assert.Equal(OwnerOnlyFile, File.GetUnixFileMode(file));

            AtomicFile.WriteAllText(file, "[1]");          // a rewrite keeps it so
            Assert.Equal(OwnerOnlyFile, File.GetUnixFileMode(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_data_folder_is_closed_to_other_accounts_even_when_an_older_build_made_it_open()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("phobia-perm-").FullName;
        try
        {
            File.SetUnixFileMode(dir, OwnerOnlyFolder | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                      | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);   // 0755

            AppPaths.RestrictToOwner(dir);

            Assert.Equal(OwnerOnlyFolder, File.GetUnixFileMode(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
