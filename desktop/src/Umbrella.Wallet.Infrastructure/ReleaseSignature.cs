using System;
using System.Security.Cryptography;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// The release's checksum list, signed by the project (Altumptic PSB §3.1 "signed checksums", §5.2 "verify
/// before apply"). Every release attaches <c>SHA256SUMS-&lt;label&gt;.txt.sig</c>: an RSA-4096 PKCS#1 v1.5
/// signature, SHA-256, over the list's exact bytes, made in the release workflow by
/// <c>desktop/scripts/sign-checksums.ps1</c> with the key that also Authenticode-signs the Windows files.
///
/// The updater trusts a file's hash only from a list this key signed, so a release page that somebody
/// else filled — binary and checksum list together — is refused on every platform, the Linux tarball and
/// the APK included. The same check by hand:
/// <c>openssl dgst -sha256 -verify keys/phobia-release-signing.pem -signature SHA256SUMS-Beta.txt.sig SHA256SUMS-Beta.txt</c>.
/// </summary>
public static class ReleaseSignature
{
    /// <summary>
    /// The public half of the release key — also in <c>keys/phobia-release-signing.pem</c> (a test keeps the
    /// two identical) and inside the certificate <see cref="WindowsSignature.PinnedCertificates"/> pins. A new
    /// key is added here in a release signed with the old one, before anything is signed with it.
    /// </summary>
    public static readonly string[] PinnedPublicKeys =
    [
        """
        -----BEGIN PUBLIC KEY-----
        MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA5eREOysDZZ66yakS2WTV
        o51Rb06Sw+BHXHihHkjnRFLyR1goDv7yD0jRqtkoT9WRbDdTA9imXk1vLW9/Qv+6
        ohsanzm4/EP2e5z4FJw+9MgPOSIinvAC1XRKsjhNTV/br9SVY7F88bm4AL9qAgRV
        G+qPgN3rho7yEsj+yLLPLCzmJL7HXGUSTghSm4+lNlxEVz4wQID5Ht0B6uWgY3yd
        2SWblsErWoTh9o1J1vGgKSGsczEPH8ypD0No6rhnfW2lgxvVs4lpZX7XNWGBF0qe
        CHg+z7pkP8AX3J4lkMVijk8kWkpBppcOkTh5qmkqYT3JAlnu8ZJ6zVXj87sIBDWh
        wvKDlRJk1o6YQTJarEj60NvsFO1wSqKo1drpMEN8SgvKIm6Je3NnyXp9/FKRwLla
        RcjKfu6pPICpQxvxsTVP0/+Gbbzt6El5r9oOvkVTP7K8g2xx5dqBbMeM5n7hpclU
        nCiYkleexY/0UMV6DmOtVGJnvuY5LdqIe4B4LlPa8had3h/ooaRbIrPLvGTuYPZG
        qFuhNxM32sPtqgZ5Q0TMf77rMw+ehlB+5szhvV9B1ar00o9vt9+4RulGDt50var/
        Z/Zb1x/hEqFFAzTABmO6A1mnl7gE4HF0bDpDdfpsB31hSdwjWPRa8zuUGQlcnrFB
        jG6/KgDk0JwUbOQXVT4tK0sCAwEAAQ==
        -----END PUBLIC KEY-----
        """,
    ];

    /// <summary>A signature is a fixed 512 bytes for RSA-4096; anything far larger is not one.</summary>
    public const int MaxSignatureBytes = 4096;

    /// <summary>The signature file's name beside a checksum list.</summary>
    public static string NameFor(string checksumListName) => checksumListName + ".sig";

    /// <summary>True when <paramref name="signature"/> is one of the pinned keys' signature over exactly
    /// <paramref name="content"/>. Anything malformed is simply false.</summary>
    public static bool Verify(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) =>
        Verify(content, signature, PinnedPublicKeys);

    public static bool Verify(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature, string[] publicKeysPem)
    {
        if (signature.IsEmpty || signature.Length > MaxSignatureBytes) return false;
        foreach (var pem in publicKeysPem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (rsa.VerifyData(content, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return true;
            }
            catch (CryptographicException)
            {
                // a key that cannot be read verifies nothing
            }
            catch (ArgumentException)
            {
            }
        }
        return false;
    }
}
