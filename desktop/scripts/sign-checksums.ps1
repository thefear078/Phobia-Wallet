<#
.SYNOPSIS
    Signs a release's checksum list with the project's release key and proves the signature checks out
    against the public key published in keys/phobia-release-signing.pem.

.DESCRIPTION
    Writes "<file>.sig": an RSA-4096 PKCS#1 v1.5 signature over the file's exact bytes, SHA-256. The key
    is the one that Authenticode-signs the Windows files (WINDOWS_CERT_PFX_B64 / WINDOWS_CERT_PASSWORD),
    read into memory only. The wallet's updater refuses a release whose checksum list does not carry this
    signature, and anyone can check it by hand:

        openssl dgst -sha256 -verify phobia-release-signing.pem -signature SHA256SUMS-Beta.txt.sig SHA256SUMS-Beta.txt

    The signature is checked with the PUBLISHED key before the script ends, so a release can never carry
    a signature that the wallet or a reader would reject.

.EXAMPLE
    ./desktop/scripts/sign-checksums.ps1 -File SHA256SUMS-Beta.txt -PublicKey keys/phobia-release-signing.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $File,
    [Parameter(Mandatory)] [string] $PublicKey
)

$ErrorActionPreference = 'Stop'

if (-not $env:WINDOWS_CERT_PFX_B64) {
    # An official release always signs its checksums; a fork has no key and says so.
    if ($env:GITHUB_REPOSITORY -eq 'thefear078/Phobia-Wallet') {
        throw "WINDOWS_CERT_PFX_B64 is not set - an official release must sign its checksum list."
    }
    Write-Host "::warning::WINDOWS_CERT_PFX_B64 is not set - $File is left unsigned."
    return
}

if (-not (Test-Path $File)) { throw "Nothing to sign at $File" }
if (-not (Test-Path $PublicKey)) { throw "No published key at $PublicKey" }

$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
$pfx = [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_B64)
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx, $env:WINDOWS_CERT_PASSWORD, $flags)
try {
    if (-not $cert.HasPrivateKey) { throw "The certificate in WINDOWS_CERT_PFX_B64 has no private key." }
    if ($env:WINDOWS_CERT_THUMBPRINT -and $cert.Thumbprint -ne $env:WINDOWS_CERT_THUMBPRINT.Trim().ToUpperInvariant()) {
        throw "WINDOWS_CERT_PFX_B64 holds certificate $($cert.Thumbprint), not the published $($env:WINDOWS_CERT_THUMBPRINT)."
    }

    $content = [IO.File]::ReadAllBytes((Resolve-Path $File))
    $private = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
    try {
        $signature = $private.SignData($content,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    }
    finally { $private.Dispose() }
}
finally { $cert.Dispose() }

# Checked against the published key, not the key that just signed: what a reader and the wallet use.
$published = [System.Security.Cryptography.RSA]::Create()
try {
    $published.ImportFromPem([IO.File]::ReadAllText((Resolve-Path $PublicKey)))
    $ok = $published.VerifyData($content, $signature,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
}
finally { $published.Dispose() }
if (-not $ok) { throw "The signature over $File does not verify with $PublicKey - the key in the secret is not the published one." }

$out = (Resolve-Path $File).Path + '.sig'
[IO.File]::WriteAllBytes($out, $signature)
Write-Host "Signed $File -> $out ($($signature.Length) bytes), checked against $PublicKey"
