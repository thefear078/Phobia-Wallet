# Third-party notices

Phobia Wallet includes third-party software under separate licenses. Those licenses remain in force
and are **not** replaced by the Phobia [LICENSE](LICENSE).

---

## Bundled binaries (pinned + SHA-256 verified)

These executables are **not committed** to the repository. Release/build scripts download a **pinned
version** and verify SHA-256 against the values below before use. Any mismatch fails the build
(fail-closed). See `desktop/scripts/fetch-tor.ps1` and `desktop/scripts/fetch-monero.ps1`.

When bumping a pin, update version **and** hash here and in the fetch script, using the upstream
project’s own signed hashes file — never a random mirror.

### Tor Expert Bundle

| | |
|---|---|
| Component | `tor.exe` + GeoIP databases (Windows x86_64 expert bundle) |
| Version | 15.0.25 |
| Archive | `tor-expert-bundle-windows-x86_64-15.0.25.tar.gz` |
| Source URL | https://dist.torproject.org/torbrowser/15.0.25/tor-expert-bundle-windows-x86_64-15.0.25.tar.gz |
| SHA-256 | `c1988c701de2f24f7d098d1d6d7f507b58f27740d097f29c246e0524d639b252` |
| Hash source | https://dist.torproject.org/torbrowser/15.0.25/sha256sums-unsigned-build.txt (+ `.asc`) |
| Signing key | `EF6E286DDA85EA2A4BA7DE684E2C6E8793298290` — Tor Browser Developers |
| License | BSD 3-Clause (Tor Project upstream `LICENSE`) |

### Monero CLI (`monero-wallet-rpc`)

| | |
|---|---|
| Component | `monero-wallet-rpc.exe` (from the Windows x64 CLI archive) |
| Version | 0.18.5.4 |
| Archive | `monero-win-x64-v0.18.5.4.zip` |
| Source URL | https://downloads.getmonero.org/cli/monero-win-x64-v0.18.5.4.zip |
| SHA-256 | `1ccac3b476ba75bf6dfdfa848f6520a79b64cdf3457a160eaa0dad82d5a2a042` |
| Hash source | https://www.getmonero.org/downloads/hashes.txt (clearsigned) |
| Signing key | `81AC591FE9C4B65C5806AFC3F0AF4D462A0BDF92` — binaryFate ([in-repo copy](https://raw.githubusercontent.com/monero-project/monero/master/utils/gpg_keys/binaryfate.asc)) |
| License | BSD 3-Clause (Monero project upstream `LICENSE`) |

### Android builds (in the APK)

The phone app carries the same two programs, from the projects' own Android builds, as `lib*.so` in the
APK's native-library folder (the one place Android runs a program from). Staged by
`desktop/scripts/fetch-android-helpers.sh`, each archive checked against the SHA-256 below; CI's
`supply-chain` job checks every pin against the signed sums file above (`verify-android-pins.ps1`).

| Archive | In the APK as | SHA-256 |
|---|---|---|
| `tor-expert-bundle-android-aarch64-15.0.25.tar.gz` | `lib/arm64-v8a/libTor.so` | `a1a67ec77ce0a7c4fca446fd9f915fbb0302c3107c2d56ab213196187cfd894a` |
| `tor-expert-bundle-android-armv7-15.0.25.tar.gz` | `lib/armeabi-v7a/libTor.so` | `4fcdb5540f7ea2f529b9063115d66dd7dfa61e5602bfaab96a78e8046bb4b8d5` |
| `tor-expert-bundle-android-x86_64-15.0.25.tar.gz` | emulator check build only | `11dbac44e2ee29df2cd6e3a5f38164d2630b2c85342f6a041b9cb57d7012d071` |
| `monero-android-armv8-v0.18.5.4.tar.bz2` | `lib/arm64-v8a/libmonero-wallet-rpc.so` | `b1ba5efaf8d078fce26ae66a45252e4eb67a7de3068ea093c5ba6c8f7e7cb59c` |
| `monero-android-armv7-v0.18.5.4.tar.bz2` | `lib/armeabi-v7a/libmonero-wallet-rpc.so` | `58d39c1e241bde624a01d9d2402784314a769bf47f555708aa52ae52fbcfc7f8` |

Tor's archives are listed in the Tor Project's signed sums for 15.0.25 (key `EF6E286D…3298290`), Monero's in
`hashes.txt` signed by binaryFate (`81AC591F…0BDF92`). Same versions and licences as the desktop builds.

### Verifying binaries by hand

```powershell
Get-FileHash -Algorithm SHA256 .\tor-expert-bundle-windows-x86_64-15.0.25.tar.gz
Get-FileHash -Algorithm SHA256 .\monero-win-x64-v0.18.5.4.zip
```

For the strongest guarantee, verify the upstream hashes file’s PGP signature before trusting the hash.

---

## .NET / NuGet libraries (representative)

Versions move with Dependabot; this table lists **primary** runtime dependencies and typical license
families. Always confirm the license text shipped with the NuGet package for the exact version in use.

| Component | Typical license | Source |
|-----------|-----------------|--------|
| .NET runtime / BCL | MIT | https://dotnet.microsoft.com |
| Avalonia UI | MIT | https://avaloniaui.net |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| NBitcoin / NBitcoin.Altcoins | MIT | https://github.com/MetacoSA/NBitcoin |
| Nethereum | MIT | https://github.com/Nethereum/Nethereum |
| BouncyCastle.Cryptography | MIT | https://www.bouncycastle.org |
| Konscious.Security.Cryptography.Argon2 | MIT | https://github.com/kmaragon/Konscious.Security.Cryptography |
| QRCoder | MIT | https://github.com/codebude/QRCoder |
| xUnit / test SDK (tests only) | Apache-2.0 / MIT | https://xunit.net |

Full transitive graphs appear in each project’s `*.csproj` / restore graph.

---

## Attribution

Some components require attribution in documentation or an About screen. This file and the in-app
About / open-source notices (where present) satisfy that requirement for Phobia’s distribution.

If a license is missing or incorrect, report via [CONTACT.md](CONTACT.md) (never send seeds).

---

Related: [LICENSE](LICENSE) · [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) · [LEGAL/README.md](LEGAL/README.md)

---

📖 Back to [Documentation Index](docs/INDEX.md)
