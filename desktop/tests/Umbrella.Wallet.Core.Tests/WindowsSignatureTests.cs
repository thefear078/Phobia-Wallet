using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// An update runs on Windows only when it carries an intact Authenticode signature by the project's own,
/// pinned certificate (Altumptic PSB §5.2). Each way that can fail is tried on a real file.
/// </summary>
public sealed class WindowsSignatureTests : IDisposable
{
    /// <summary>A published, project-signed installer, when this machine has a copy of one (the
    /// release folder used to verify Beta 6). Absent in CI, where the other cases still run.</summary>
    private const string SignedRelease = @"D:\umbrella-dist\v4.10.0-beta.6\PhobiaWallet-Setup-Beta.exe";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"umbrella-sig-{Guid.NewGuid():N}");

    public WindowsSignatureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void The_projects_signed_installer_is_accepted()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SignedRelease)) return;

        Assert.Equal(WindowsSignature.Verdict.SignedByProject, WindowsSignature.Check(SignedRelease));
        Assert.Null(UpdateService.SignatureProblem(InstallKind.WindowsInstaller, SignedRelease));
    }

    [Fact]
    public void One_changed_byte_is_refused_as_tampered()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SignedRelease)) return;
        var copy = Path.Combine(_dir, "setup.exe");
        var bytes = File.ReadAllBytes(SignedRelease);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);

        Assert.Equal(WindowsSignature.Verdict.Tampered, WindowsSignature.Check(copy));
        Assert.NotNull(UpdateService.SignatureProblem(InstallKind.WindowsInstaller, copy));
    }

    [Fact]
    public void A_file_signed_by_someone_else_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        // The .NET runtime's own library: validly signed — by Microsoft, not by this project.
        var microsoft = typeof(object).Assembly.Location;

        Assert.Equal(WindowsSignature.Verdict.SignedBySomeoneElse, WindowsSignature.Check(microsoft));
    }

    [Fact]
    public void An_unsigned_program_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        // This wallet's own assembly as built for the tests: never signed.
        var unsigned = typeof(UpdateService).Assembly.Location;

        Assert.Equal(WindowsSignature.Verdict.NotSigned, WindowsSignature.Check(unsigned));
        Assert.NotNull(UpdateService.SignatureProblem(InstallKind.WindowsPortable, unsigned));
    }

    [Fact]
    public void The_tarball_and_the_apk_are_not_judged_by_a_windows_signature()
    {
        Assert.Null(UpdateService.SignatureProblem(InstallKind.Linux, "anything"));
        Assert.Null(UpdateService.SignatureProblem(InstallKind.Android, "anything"));
    }
}
