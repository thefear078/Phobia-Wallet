using System.Formats.Tar;
using System.IO.Compression;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// On Linux an update used to stop at "downloaded and verified - unpack it over this folder yourself".
/// Now the click unpacks the verified tarball over the program's folder, file by file through a rename
/// (atomic, and allowed while the old program runs), and starts the new one. What it must never do:
/// touch the data folder, leave half-written files, or accept a tarball that is not a Phobia release.
/// </summary>
public sealed class LinuxSelfUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"phobia-selfupdate-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void The_new_release_replaces_the_program_and_its_helpers_and_leaves_the_data_alone()
    {
        var app = Folder("app");
        File.WriteAllText(Path.Combine(app, UpdateService.LinuxProgram), "old program");
        Directory.CreateDirectory(Path.Combine(app, "tor"));
        File.WriteAllText(Path.Combine(app, "tor", "tor"), "old tor");
        Directory.CreateDirectory(Path.Combine(app, "data"));
        File.WriteAllText(Path.Combine(app, "data", "vault.json"), "the user's vault");

        var tarball = Tarball("phobia-wallet-4.10.0-beta.2-linux-x64", new()
        {
            [UpdateService.LinuxProgram] = "new program",
            ["tor/tor"] = "new tor",
            ["monero/monero-wallet-rpc"] = "new monero",
        });

        var program = UpdateService.UnpackOver(tarball, app);

        Assert.Equal(Path.Combine(app, UpdateService.LinuxProgram), program);
        Assert.Equal("new program", File.ReadAllText(program));
        Assert.Equal("new tor", File.ReadAllText(Path.Combine(app, "tor", "tor")));
        Assert.Equal("new monero", File.ReadAllText(Path.Combine(app, "monero", "monero-wallet-rpc")));
        Assert.Equal("the user's vault", File.ReadAllText(Path.Combine(app, "data", "vault.json")));
        Assert.Empty(Directory.EnumerateFiles(app, "*.new", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(tarball)!, "unpacked")));
    }

    [Fact]
    public void A_tarball_without_the_program_is_refused_and_nothing_is_replaced()
    {
        var app = Folder("app2");
        File.WriteAllText(Path.Combine(app, UpdateService.LinuxProgram), "old program");
        var tarball = Tarball("something-else", new() { ["evil"] = "x" });

        Assert.Throws<InvalidDataException>(() => UpdateService.UnpackOver(tarball, app));

        Assert.Equal("old program", File.ReadAllText(Path.Combine(app, UpdateService.LinuxProgram)));
        Assert.False(File.Exists(Path.Combine(app, "evil")));
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Tarball(string top, Dictionary<string, string> files)
    {
        var dir = Folder("updates-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "release.tar.gz");
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gz, TarEntryFormat.Pax);
        foreach (var (name, text) in files)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{top}/{name}")
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            };
            tar.WriteEntry(entry);
        }
        return path;
    }
}
