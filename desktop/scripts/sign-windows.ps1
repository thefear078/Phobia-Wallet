<#
.SYNOPSIS
    Authenticode-signs release executables with the certificate in WINDOWS_CERT_PFX_B64 /
    WINDOWS_CERT_PASSWORD, timestamps them, and proves each one carries that signature.

.DESCRIPTION
    Used by the release workflow; runs anywhere with PowerShell 7 on Windows. The certificate is read
    from the environment into memory only (EphemeralKeySet): nothing touches a certificate store or
    the disk. Every file is SHA-256 signed with an RFC 3161 timestamp, so the signature stays valid
    after the certificate expires.

    A self-signed certificate ("the fear (thefear078)") is not trusted by Windows, so the signature's
    status reads "UnknownError" (untrusted root) rather than "Valid". It still proves the file is the one
    this release built: anyone can compare the signer's thumbprint with the one published in
    docs/BUILD_VERIFY.md. A file is accepted only when its signer IS this certificate and it is
    timestamped; any other status (no signature, hash mismatch) fails the release.

.EXAMPLE
    ./desktop/scripts/sign-windows.ps1 -Files dist/app/Phobia.exe, dist/portable/Phobia.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Files
)

$ErrorActionPreference = 'Stop'

if (-not $env:WINDOWS_CERT_PFX_B64) {
    # An official release is never unsigned; a fork's or a local build may be.
    if ($env:GITHUB_REPOSITORY -eq 'thefear078/Phobia-Wallet') {
        throw "WINDOWS_CERT_PFX_B64 is not set - an official release must be signed."
    }
    Write-Host "::warning::WINDOWS_CERT_PFX_B64 is not set - these executables are left unsigned."
    return
}

$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
$bytes = [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_B64)
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes, $env:WINDOWS_CERT_PASSWORD, $flags)
if (-not $cert.HasPrivateKey) { throw "The certificate in WINDOWS_CERT_PFX_B64 has no private key." }
# The secret must hold the certificate whose thumbprint is published, not merely some certificate.
if ($env:WINDOWS_CERT_THUMBPRINT -and $cert.Thumbprint -ne $env:WINDOWS_CERT_THUMBPRINT.Trim().ToUpperInvariant()) {
    throw "WINDOWS_CERT_PFX_B64 holds certificate $($cert.Thumbprint), not the published $($env:WINDOWS_CERT_THUMBPRINT)."
}
Write-Host "Signing as: $($cert.Subject)  thumbprint $($cert.Thumbprint)  valid to $($cert.NotAfter.ToString('yyyy-MM-dd'))"

# Two independent RFC 3161 services: a timestamp that could not be had is retried on the other rather
# than shipping a signature that dies with the certificate.
$timestampers = @('http://timestamp.digicert.com', 'http://timestamp.sectigo.com')

foreach ($file in $Files) {
    if (-not (Test-Path $file)) { throw "Nothing to sign at $file" }
    $signed = $false
    foreach ($tsa in $timestampers) {
        try {
            $null = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $tsa
            $check = Get-AuthenticodeSignature -FilePath $file
            if ($check.SignerCertificate -and $check.SignerCertificate.Thumbprint -eq $cert.Thumbprint -and $check.TimeStamperCertificate) {
                # Valid = a CA-issued certificate; UnknownError = this one is self-signed (untrusted root).
                if ($check.Status -in 'Valid', 'UnknownError') { $signed = $true; break }
            }
            Write-Host "  $tsa : status $($check.Status) - trying the next timestamp service"
        }
        catch {
            Write-Host "  $tsa : $($_.Exception.Message) - trying the next timestamp service"
        }
    }
    if (-not $signed) { throw "Could not sign and timestamp $file" }
    Write-Host "  signed + timestamped: $file"
}
