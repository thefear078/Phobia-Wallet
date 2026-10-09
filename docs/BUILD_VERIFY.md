# Build & Verify Phobia Wallet

*Don't trust — verify.* You never have to take anyone's word that the file you run is the one this
repository built. Each check below is independent; the more you run, the less you are trusting.

| Check | Proves | Takes |
|---|---|---|
| [A. Checksum](#a-checksum) | the file is the one on the release page | 30 s |
| [B. Signature](#b-signature) | the file was signed with the project's own key | 30 s |
| [C. Attestation](#c-build-attestation) | the file came out of this repository's release workflow, at a named commit | 1 min |
| [D. Build it yourself](#d-build-it-yourself) | the code that runs is the code you can read | 10 min |

Releases are **immutable**: a tag is never moved and a published release is never rebuilt (a repository
ruleset forbids moving or deleting `v*` tags, and the release workflow refuses to build for a release
that already has files). If a file on a release page ever changes, that is the signal something is wrong.

---

## A. Checksum

Every release carries `SHA256SUMS-<label>.txt` (`SHA256SUMS-Beta.txt`, `SHA256SUMS-4.10.0.txt`)
listing every file on the page, the SBOM included. A beta also carries the same files under numbered
names (`PhobiaWallet-Setup-Beta-5.exe`, for older copies' updaters) with a `SHA256SUMS-Beta-5.txt` of
their own. The release workflow writes it only after checking
the set of files is complete, and checks every line against the file before publishing.

```bash
# Linux / macOS, in the folder with the download and the sums file
sha256sum -c SHA256SUMS-Beta.txt --ignore-missing
```

```powershell
# Windows: compare with the matching line of the sums file
Get-FileHash .\PhobiaWallet-Setup-Beta.exe -Algorithm SHA256
```

A mismatch means: do not run it. A match proves the file is the one on the page — not who put it there.
That is what B and C are for.

## B. Signature

The release workflow signs with two keys held only in the repository's encrypted secrets and offline by
the author, **the fear (thefear078)**. Their public fingerprints:

| Platform | Signer | Fingerprint |
|---|---|---|
| Windows (Authenticode) — `Phobia.exe`, portable exe, installer | `CN=the fear (thefear078), O=Phobia Wallet` | SHA-1 thumbprint **`89C2D871C195D57C14D1911B6C47629DE052C553`**<br>SHA-256 `A88405BED227428530E8E6A77F4D3A42C03A154A1AD2E2E0EF1C87583DEA412F` |
| Android APK (v2/v3 signature) | `CN=the fear (thefear078), O=Phobia Wallet` | SHA-256 **`C3:80:0E:C6:34:F3:C1:6C:84:4E:62:0B:BB:92:28:81:B6:34:C0:2B:17:40:68:D4:80:F8:9A:DA:A8:95:72:D1`** |

Files from releases since 2026-10-07 carry these signatures; older ones were not signed (the first
beta's APK, 2026-10-02, has a key made for that one build). Both fingerprints are also pinned in `release.yml`: a release
whose secrets hold any other key, or no key, fails instead of publishing.

**Windows** (PowerShell):

```powershell
Get-AuthenticodeSignature .\PhobiaWallet-Setup-Beta.exe |
  Format-List Status, @{n='Thumbprint';e={$_.SignerCertificate.Thumbprint}}, @{n='Timestamped';e={[bool]$_.TimeStamperCertificate}}
```

The thumbprint must be `89C2D871…C553` and the file timestamped. The certificate is **self-signed** for
now, so `Status` reads `UnknownError` ("root not trusted") and Windows SmartScreen still warns about an
unknown publisher: the signature proves the file is untouched since the release workflow signed it, but
no certificate authority vouches for the name yet. A CA-issued certificate (for an individual open-source
developer) will replace it; this page will say so, with the new thumbprint. The routes, and what the
pipeline needs then: [CODE_SIGNING.md](CODE_SIGNING.md).

**Android** (any machine with the Android SDK's build-tools):

```bash
apksigner verify --print-certs PhobiaWallet-Beta-android.apk
```

`Signer #1 certificate SHA-256 digest` must equal the fingerprint above. Android itself enforces it from
then on: an update signed with any other key will not install over the app.

## C. Build attestation

Every file and the sums file carry a keyless build attestation (GitHub OIDC → the public Sigstore
transparency log). It states that those exact bytes came out of this repository's `release.yml` at a
specific commit — checked against the log, not against anything this project says.

```bash
gh attestation verify PhobiaWallet-Setup-Beta.exe --repo thefear078/Phobia-Wallet
```

From the release after 4.10.0-beta.5 the signed attestation itself is on the release page too, as
`provenance-<label>.sigstore.json`, so it can be checked without asking GitHub for it:

```bash
gh attestation verify PhobiaWallet-Setup-Beta.exe --repo thefear078/Phobia-Wallet --bundle provenance-Beta.sigstore.json
```

Each release also carries `PhobiaWallet-<label>-sbom.spdx.json`: every package that release builds from,
with its version and licence (SPDX, from GitHub's dependency graph), covered by the same checksum and
attestation.

## D. Build it yourself

You need the .NET SDK pinned in [`global.json`](../global.json) (8.0.423, latest patch allowed).

```bash
git clone https://github.com/thefear078/Phobia-Wallet.git
cd Phobia-Wallet
git checkout v4.10.0-beta.5          # the tag on the release page — never a moving branch
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter "Category!=Live"
```

Publish exactly as the release workflow does (Windows):

```powershell
pwsh desktop/scripts/fetch-tor.ps1 -RequireSignature      # Tor, checked against the Tor Project's signed sums
pwsh desktop/scripts/fetch-monero.ps1 -RequireSignature   # Monero, checked against binaryFate's signed hashes.txt
$csproj = "desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj"
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/app
```

Linux: `desktop/scripts/publish-linux.sh`. Android: `desktop/scripts/publish-android.sh` (needs the
android workload and a JDK 17).

### What is and is not bit-for-bit reproducible

| Layer | Reproducible? | Why |
|---|---|---|
| Managed assemblies (`Umbrella.Wallet.*.dll`) | ✅ yes | Deterministic C# compilation. CI's `reproducible-build` job builds every commit twice, from two different paths, and fails if they differ. These are the files that derive keys, sign transactions and route traffic. |
| Bundled .NET runtime | ✅ with the pinned SDK | Microsoft's own binaries, copied verbatim. |
| Tor, `monero-wallet-rpc` | ✅ upstream's files | Pinned version, SHA-256 checked against the projects' **signed** sums (Tor Browser Developers `EF6E286D…3298290`, binaryFate `81AC591F…0BDF92`). See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md). |
| `Phobia.exe` apphost, single-file bundle, installer, APK | ❌ not bit-identical | Timestamps, compression and the signature itself differ between builds. |

So compare what carries the logic: build the released tag, then hash the three managed DLLs from your
build and from the released installer's app folder (the portable exe bundles them inside) — they must match.

```powershell
Get-ChildItem dist/app/Umbrella.Wallet.*.dll | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_).Hash, $_.Name }
```

## E. Prove privacy at runtime

- **Settings → Privacy → Verify Tor** asks `check.torproject.org` through the wallet's own route.
- **Tor-only (block clearnet)** is fail-closed: CI's `network-isolation` job proves no socket is opened.
- No telemetry, no crash reporting, no analytics, no accounts — see [PRIVACY.md](../PRIVACY.md) and
  [TOR.md](TOR.md).

---

📖 Back to [Documentation Index](INDEX.md) · [Verify your wallet](VERIFY_YOUR_WALLET.md) · [Security](../SECURITY.md)
