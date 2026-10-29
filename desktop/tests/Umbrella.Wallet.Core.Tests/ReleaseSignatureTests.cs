using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Umbrella.Wallet.Infrastructure;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The updater believes a hash only from a checksum list the project signed (Altumptic PSB §3.1, §5.2).
/// The fixture is Beta 6's published <c>SHA256SUMS-Beta.txt</c>, signed with the real release key by
/// <c>desktop/scripts/sign-checksums.ps1</c> — so the key pinned in the wallet is proved to be the key
/// the release workflow signs with, not just a key that signs something.
/// </summary>
public sealed class ReleaseSignatureTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "release", name);

    private static byte[] Sums => File.ReadAllBytes(Fixture("SHA256SUMS-Beta.txt"));
    private static byte[] Signature => File.ReadAllBytes(Fixture("SHA256SUMS-Beta.txt.sig"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "desktop", "src"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void A_list_the_project_signed_is_accepted()
    {
        Assert.True(ReleaseSignature.Verify(Sums, Signature));
        Assert.Null(UpdateService.SumsSignatureProblem(Sums, Signature));
    }

    [Fact]
    public void One_changed_byte_in_the_list_is_refused()
    {
        var sums = Sums;
        sums[10] ^= 1;   // inside the first hash: a different file would now "match"

        Assert.False(ReleaseSignature.Verify(sums, Signature));
        Assert.Contains("not signed by this project", UpdateService.SumsSignatureProblem(sums, Signature));
    }

    [Fact]
    public void A_list_signed_by_any_other_key_is_refused()
    {
        using var other = RSA.Create(4096);
        var forged = other.SignData(Sums, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.False(ReleaseSignature.Verify(Sums, forged));
        // ...while that same key is accepted when it is the one pinned: the check is the key, not luck.
        Assert.True(ReleaseSignature.Verify(Sums, forged, [other.ExportSubjectPublicKeyInfoPem()]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public void A_missing_or_absurd_signature_is_refused(int length)
    {
        var signature = new byte[length];
        Assert.False(ReleaseSignature.Verify(Sums, signature));
        Assert.NotNull(UpdateService.SumsSignatureProblem(Sums, length == 0 ? null : signature));
    }

    [Fact]
    public void A_list_without_a_signature_is_refused_before_anything_is_fetched()
    {
        Assert.Contains("carries no signature", UpdateService.SumsSignatureProblem(Sums, null));
    }

    /// <summary>The key in the wallet, the key file people are told to verify with, and the key inside
    /// the pinned Windows certificate are one and the same.</summary>
    [Fact]
    public void The_pinned_key_is_the_published_key_and_the_certificates_key()
    {
        var root = RepoRoot();
        var published = File.ReadAllText(Path.Combine(root, "keys", "phobia-release-signing.pem"));
        Assert.Equal(Normalise(published), Normalise(Assert.Single(ReleaseSignature.PinnedPublicKeys)));

        using var cert = new X509Certificate2(File.ReadAllBytes(Path.Combine(root, "keys", "phobia-codesign.cer")));
        Assert.Equal(WindowsSignature.PinnedCertificates[0], Convert.ToHexString(SHA256.HashData(cert.RawData)));
        using var certKey = cert.GetRSAPublicKey()!;
        Assert.Equal(Normalise(published), Normalise(certKey.ExportSubjectPublicKeyInfoPem()));

        static string Normalise(string pem) => pem.Replace("\r", "").Trim();
    }

    /// <summary>The workflow is the other half of the contract: it must sign the very list the wallet
    /// fetches, check it with the published key, and attach the signature under the name looked for.</summary>
    [Fact]
    public void The_release_workflow_signs_the_list_the_wallet_checks()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("sign-checksums.ps1 -File \"SHA256SUMS-${{ steps.v.outputs.label }}.txt\" -PublicKey keys/phobia-release-signing.pem", workflow);
        Assert.Contains("files: SHA256SUMS-${{ steps.v.outputs.label }}.txt.sig", workflow);
        Assert.Equal("SHA256SUMS-Beta.txt.sig",
            ReleaseSignature.NameFor(UpdateService.SumsNameFor(new ReleaseVersion(4, 10, 0, "beta.7"))));
    }

    /// <summary>PSB §2.1: TLS 1.2 or 1.3 only, set on the client every request goes through.</summary>
    [Fact]
    public void Every_https_connection_is_tls_1_2_or_1_3()
    {
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, PublicHttp.AllowedTls);

        using var client = PublicHttp.CreateProbeClient(null, requireProxy: false);
        var handler = (SocketsHttpHandler)typeof(HttpMessageInvoker)
            .GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        Assert.Equal(PublicHttp.AllowedTls, handler.SslOptions.EnabledSslProtocols);
    }
}
