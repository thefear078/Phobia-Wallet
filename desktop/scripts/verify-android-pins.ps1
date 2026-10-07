<#
.SYNOPSIS
    Checks every Tor and Monero archive the APK bundles against its project's signed sums file.

.DESCRIPTION
    fetch-android-helpers.sh downloads the Android builds of Tor and monero-wallet-rpc and checks each
    against a SHA-256 pinned in that script. A pin only ever agrees with itself; this proves each one is
    what the Tor Project and the Monero project published, signature and all — the same check
    fetch-tor.ps1 and fetch-monero.ps1 make for the desktop builds. The pins are read from the bash
    script itself, so there is one list and it cannot drift from what is checked here.

    Run by CI's supply-chain job:  ./desktop/scripts/verify-android-pins.ps1 -RequireSignature
#>
param([switch]$RequireSignature)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SupplyChain.ps1')

$script = Get-Content -Raw (Join-Path $PSScriptRoot 'fetch-android-helpers.sh')
$torVersion = [regex]::Match($script, 'TOR_VERSION="([^"]+)"').Groups[1].Value
$moneroVersion = [regex]::Match($script, 'MONERO_VERSION="([^"]+)"').Groups[1].Value
if (-not $torVersion -or -not $moneroVersion) { throw 'Could not read the pinned versions from fetch-android-helpers.sh' }

$pins = @()
foreach ($m in [regex]::Matches($script, 'tor_arch="([^"]+)"; tor_sha="([0-9a-f]{64})"')) {
    $pins += [pscustomobject]@{ What = "Tor $torVersion ($($m.Groups[1].Value))"; Project = 'tor'
        File = "tor-expert-bundle-android-$($m.Groups[1].Value)-$torVersion.tar.gz"; Sha = $m.Groups[2].Value }
}
foreach ($m in [regex]::Matches($script, 'monero_arch="([^"]+)"; monero_sha="([0-9a-f]{64})"')) {
    $pins += [pscustomobject]@{ What = "Monero $moneroVersion ($($m.Groups[1].Value))"; Project = 'monero'
        File = "monero-android-$($m.Groups[1].Value)-$moneroVersion.tar.bz2"; Sha = $m.Groups[2].Value }
}
if (($pins | Where-Object Project -eq 'tor').Count -lt 2 -or ($pins | Where-Object Project -eq 'monero').Count -lt 2) {
    throw "Expected at least two Tor and two Monero pins in fetch-android-helpers.sh, found $($pins.Count)."
}

$torSums = "https://dist.torproject.org/torbrowser/$torVersion/sha256sums-unsigned-build.txt"
# PUBLIC key fingerprints (the same ones fetch-tor.ps1 and fetch-monero.ps1 pin), named so that
# .gitleaks.toml's allowlist recognises them as such.
$torSigningKeyFingerprint = 'EF6E286DDA85EA2A4BA7DE684E2C6E8793298290'
$moneroSigningKeyFingerprint = '81AC591FE9C4B65C5806AFC3F0AF4D462A0BDF92'

foreach ($pin in $pins) {
    Write-Host "== $($pin.What): $($pin.File)"
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("phobia-pin-" + [guid]::NewGuid().ToString('N'))
    if ($pin.Project -eq 'tor') {
        Assert-PinnedBySignedSums -SumsUrl $torSums -SignatureUrl "$torSums.asc" `
            -FileName $pin.File -ExpectedSha256 $pin.Sha -KeyFingerprint $torSigningKeyFingerprint `
            -KeyUrls @((Join-Path $PSScriptRoot "keys/torbrowser-$torSigningKeyFingerprint.asc"),
                'https://openpgpkey.torproject.org/.well-known/openpgpkey/torproject.org/hu/kounek7zrdx745qydx6p59t9mqjpuhdf?l=torbrowser') `
            -WorkDir $work -Required:$RequireSignature -What $pin.What
    }
    else {
        Assert-PinnedBySignedSums -SumsUrl 'https://www.getmonero.org/downloads/hashes.txt' `
            -FileName $pin.File -ExpectedSha256 $pin.Sha -KeyFingerprint $moneroSigningKeyFingerprint `
            -KeyUrls @('https://raw.githubusercontent.com/monero-project/monero/master/utils/gpg_keys/binaryfate.asc') `
            -WorkDir $work -Required:$RequireSignature -What $pin.What
    }
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "OK - all $($pins.Count) Android pins are in their projects' signed sums."
