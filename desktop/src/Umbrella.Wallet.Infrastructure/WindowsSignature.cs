using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Umbrella.Wallet.Infrastructure;

/// <summary>
/// Is this Windows program signed by the project? The update's signature check (Altumptic PSB §5.2:
/// "auto-update only from a signed channel; verify before apply").
///
/// Two questions, both must be yes:
/// <list type="number">
/// <item><b>Is the signature intact?</b> Windows' own <c>WinVerifyTrust</c> checks the file's Authenticode
/// signature against its bytes. A changed byte is <c>TRUST_E_BAD_DIGEST</c>, an unsigned file
/// <c>TRUST_E_NOSIGNATURE</c> — refused. The project's certificate is self-signed, so Windows does not
/// trust its root and says so (<c>CERT_E_UNTRUSTEDROOT</c>); that answer comes only AFTER the signature
/// has been checked, and is accepted — but only together with the second question.</item>
/// <item><b>Is the signer the project?</b> The signing certificate's SHA-256 must be one pinned here. A
/// file validly signed by anybody else — a trusted publisher included — is not an update of this wallet.</item>
/// </list>
/// No revocation lookup and cache-only retrieval: checking an update makes no network request of its own.
/// </summary>
public static class WindowsSignature
{
    /// <summary>
    /// SHA-256 of the project's code-signing certificates (CN=the fear (thefear078), O=Phobia Wallet; valid
    /// to 2036). Its SHA-1 thumbprint, as SECURITY.md also gives it, is 89C2D871C195D57C14D1911B6C47629DE052C553.
    /// A new certificate is added here in a release signed with the old one, before anything is signed with it.
    /// </summary>
    public static readonly string[] PinnedCertificates =
    [
        "A88405BED227428530E8E6A77F4D3A42C03A154A1AD2E2E0EF1C87583DEA412F",
    ];

    public enum Verdict
    {
        SignedByProject,
        NotSigned,
        Tampered,
        SignedBySomeoneElse,
        Unreadable,
    }

    [SupportedOSPlatform("windows")]
    public static Verdict Check(string path)
    {
        int result;
        try
        {
            result = Verify(path);
        }
        catch
        {
            return Verdict.Unreadable;
        }

        switch ((uint)result)
        {
            case 0:                     // signed and chained to a root Windows trusts
            case CertEUntrustedRoot:    // signed; the root is the project's own self-signed certificate
            case CertEChaining:
            case CertEExpired:          // timestamped signature, certificate past its dates since
                break;
            case TrustENoSignature:
            case TrustESubjectFormUnknown:
                return Verdict.NotSigned;
            case TrustEBadDigest:
            case TrustENoSignatureOrBadDigest:
                return Verdict.Tampered;
            default:
                return Verdict.Unreadable;
        }

        try
        {
#pragma warning disable SYSLIB0057 // reading the signer of a signed file; the signature itself was verified above
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var sha256 = Convert.ToHexString(SHA256.HashData(signer.RawData));
            return PinnedCertificates.Contains(sha256, StringComparer.OrdinalIgnoreCase)
                ? Verdict.SignedByProject
                : Verdict.SignedBySomeoneElse;
        }
        catch
        {
            // Windows found an intact signature but its certificate could not be read back: not judged.
            return Verdict.Unreadable;
        }
    }

    // --- WinVerifyTrust --------------------------------------------------------------------------------

    private const uint TrustENoSignature = 0x800B0100;
    private const uint TrustESubjectFormUnknown = 0x800B0003;
    private const uint TrustEBadDigest = 0x80096010;
    private const uint TrustENoSignatureOrBadDigest = 0x80096019;   // TRUST_E_NOSIGNATURE for some formats
    private const uint CertEUntrustedRoot = 0x800B0109;
    private const uint CertEChaining = 0x800B010A;
    private const uint CertEExpired = 0x800B0101;

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid action, IntPtr data);

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const uint WtdRevocationCheckNone = 0x10;

    [SupportedOSPlatform("windows")]
    private static int Verify(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfoPtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr,
            };
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdCacheOnlyUrlRetrieval | WtdRevocationCheckNone,
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);

            // Release the state Windows kept for the check.
            data = Marshal.PtrToStructure<WinTrustData>(dataPtr);
            data.dwStateAction = WtdStateActionClose;
            Marshal.StructureToPtr(data, dataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);
            return result;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }
}
