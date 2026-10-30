# Security Policy

Phobia is a self-custody wallet. A bug here can cost somebody everything they hold, so this page is
specific rather than reassuring.

- How Phobia meets the Altumptic Product Security Baseline, with the proof for each point and the
  exceptions that remain: **[docs/PSB.md](docs/PSB.md)**.
- Threat model, cryptography, what is deliberately not protected: **[docs/security-model.md](docs/security-model.md)**
  and **[THREAT_MODEL.md](THREAT_MODEL.md)**.

## Reporting a vulnerability

**Report privately:**
**[github.com/thefear078/Phobia-Wallet/security/advisories/new](https://github.com/thefear078/Phobia-Wallet/security/advisories/new)**

Please do **not** open a public issue for anything that could put funds at risk. A public report on a
wallet is a race between the fix and whoever reads it first.

If GitHub advisories are not available to you, contact [t.me/PhobiaStat](https://t.me/PhobiaStat)
and ask for a private channel. Do not put details in a public chat. The same contacts are in
[`.well-known/security.txt`](.well-known/security.txt). There is no security email address yet.

### What to include

- What you found, and why it matters.
- Steps to reproduce, or a proof of concept.
- Affected version: the installer version, or the commit if you built it.
- Your OS.
- Whether you believe it is already being exploited.

### What to expect

| | |
|---|---|
| Acknowledgement | within **72 hours** |
| Initial assessment | within **7 days** |
| Fix for a critical issue | as fast as it can be done correctly: days, not weeks |
| Credit | yes, by name or handle, unless you prefer otherwise |
| Disclosure | coordinated. We agree a date with you and do not sit on it indefinitely. |

This is a one-maintainer project. The timelines above are commitments, not an SLA backed by a team; if
something slips, you will hear why rather than nothing.

### Safe harbour

If you research in good faith within this policy, using your own wallets and funds, touching no one
else's data or money, not disrupting services you do not own, and reporting privately, the project will
not take legal action against you or ask anyone else to. We will work with you on the fix and on the
disclosure.

## Scope

### In scope

Anything that could lose, expose, or lock up funds:

- Key derivation producing a wrong or unspendable address
- Vault encryption weaknesses: KDF parameters, cipher misuse, key handling in memory
- Transaction construction: wrong recipient, wrong amount, wrong change, wrong fee
- Signing flaws, nonce reuse, key material leaking anywhere
- Privacy leaks: anything that bypasses `PublicHttp`, defeats the Tor kill-switch, or sends data we do
  not document as leaving the device
- Seed or key material reaching disk, logs, the clipboard, or a crash dump unintentionally
- Screen-capture protection being bypassed on seed/key screens
- Supply-chain problems: a dependency, the build scripts, or the release pipeline
- Spoofing the wallet's identity or update path

### Out of scope

- Attacks requiring malware **already running as the user**. No desktop wallet defends against this,
  and we say so in [docs/security-model.md](docs/security-model.md) rather than implying otherwise.
- Physical access to an **unlocked** machine.
- The user voluntarily giving away their 24 words (phishing sites, fake support). We defend by
  detection and education (spam-token folding, address-poisoning warnings), but a user who types
  their seed into a website cannot be saved by the wallet.
- Third-party explorer or RPC outages and rate limits.
- The fact that Bitcoin, Ethereum and other transparent chains are public ledgers. That is the chain's
  design, not our bug. Privacy Radar tells you what a send reveals.
- Zcash shielded addresses. Not implemented, and listed as transparent-only in the wallet.
- Missing features, UI preferences, and "you should use X instead".

## Bug bounty

There is **no funded bounty programme**, and none is implied:

- Every valid finding gets a fix and public credit.
- A **critical** finding, one that lets an attacker take funds or extract a seed, is rewarded from
  project funds, decided case by case. There is no fixed amount.
- [Sponsorship](https://github.com/sponsors/thefear078) is what would pay for a real programme.

## Supported versions

| Version | Supported |
|---|---|
| 4.10.0 betas (Phobia **Beta**) | ✅ |
| 4.9.x | ✅ |
| 4.8.x | ⚠️ critical fixes only |
| < 4.8 | ❌ |

## Updates

Always run the latest release. The wallet looks for one shortly after start and then twice a day, and
offers only a version newer than the one running, only from this project's own release page.

From the next Beta on, before anything is downloaded, the release's checksum list must carry the
project's signature, checked against a key built into the wallet ([keys/](keys/README.md)). A release
page that somebody else filled, with both file and list replaced, is refused on every platform. The
file must then match that list **and** GitHub's own digest. On Windows it must also carry an intact
Authenticode signature by the project's pinned certificate. A file that fails any check is deleted, never
run. Android installs an APK only over the same signing key.

The wallet still never installs anything by itself. Replacing the program always takes your click,
because a wallet that can silently replace its own binary is a wallet with a very attractive update
channel. The check and the download can each be turned off in Settings → Updates, and both go through
Tor when Tor is on.

## Verifying what you run

Four independent checks (checksum, signatures, build attestation, building it yourself), each with the
exact commands, are in **[docs/BUILD_VERIFY.md](docs/BUILD_VERIFY.md)**. The public keys and
certificates are in **[keys/](keys/README.md)**.

| What | Signer | Fingerprint |
|---|---|---|
| Windows programs (Authenticode) | the fear (thefear078) | SHA-1 `89C2D871C195D57C14D1911B6C47629DE052C553` |
| Checksum list (`SHA256SUMS-<label>.txt.sig`), from the next Beta | the same RSA-4096 key | [`keys/phobia-release-signing.pem`](keys/phobia-release-signing.pem) |
| Android APK | the fear (thefear078) | SHA-256 `C3:80:0E:C6:34:F3:C1:6C:84:4E:62:0B:BB:92:28:81:B6:34:C0:2B:17:40:68:D4:80:F8:9A:DA:A8:95:72:D1` |

A file signed by anything else is not an official build. The Windows certificate is self-signed until a
CA-issued one replaces it, so Windows still shows an unknown publisher; the thumbprint is what to check.

## Release integrity

| Asset | Since |
|---|---|
| `SHA256SUMS-<label>.txt`, checked against every file before publishing | every release |
| Build attestation (Sigstore) over every file **and** the checksum list | 4.8.0 |
| Authenticode signature on every Windows program; APK signed with one stable key | 8 October 2026 |
| SBOM (SPDX) `PhobiaWallet-<label>-sbom.spdx.json` | 8 October 2026 |
| Signed checksum list `SHA256SUMS-<label>.txt.sig` | the next Beta |

```bash
gh attestation verify PhobiaWallet-Setup-Beta.exe --repo thefear078/Phobia-Wallet
```

That checks, against a public transparency log rather than against anything we say, that those exact
bytes came out of this repository's release workflow at a specific commit. Older releases have only
what existed when they were built: a signature or attestation cannot be added afterwards, and claiming
otherwise would defeat the point.

## What we do on our side

| | |
|---|---|
| Static analysis | **CodeQL** (C#, extended security queries, and the workflows themselves) and **Semgrep** (C#, secrets, GitHub Actions rules) on every push and pull request |
| Secret scanning | enabled, with push protection; gitleaks over the whole history every week |
| Dependencies | Dependabot security updates; the build fails on a known-vulnerable package; dependency review fails on high severity; lock files |
| Build supply chain | every GitHub Action pinned to a commit SHA; OpenSSF Scorecard weekly; the wallet's own assemblies rebuild byte for byte |
| Bundled Tor and Monero | pinned versions, checked against the projects' signed hashes on every pull request |
| Branch protection | `main` requires every check green; no force-push, no deletion |
| Release tags | immutable: a ruleset forbids moving or deleting `v*` tags; a published release is never rebuilt |
| Tests | about 2,210 offline tests plus rendered-screen tests, required before merge, including fail-closed and tampering cases; coverage published weekly ([docs/testing.md](docs/testing.md)) |
| Self-review from an attacker's side | secrets in logs, memory, files at rest, local services: **[docs/SECURITY_REVIEW.md](docs/SECURITY_REVIEW.md)** |

No external audit has been performed ([AUDIT_STATUS.md](AUDIT_STATUS.md)). When one exists, it will be
linked there and here with the full report, findings included, not as a badge alone.

## When a vulnerability is confirmed

1. **Within 72 hours:** acknowledge receipt.
2. **Within 7 days:** assessment and a fix timeline.
3. **Fix released** under coordinated disclosure, with a **Security** section in the release notes.
4. **CVE** requested when severity is High or Critical and appropriate.
5. **Credit** publicly unless the reporter asks otherwise.

## Historical vulnerabilities

Full log: **[SECURITY/VULNERABILITY_HISTORY.md](SECURITY/VULNERABILITY_HISTORY.md)**.
Process: **[SECURITY/COORDINATED_DISCLOSURE.md](SECURITY/COORDINATED_DISCLOSURE.md)**.

| Date | ID | Severity | Fixed in |
|---|---|---|---|
| 2026-10-30 | PHB-2026-11: the duress password's decoy listed the real wallet's history | **High** | the next Beta |
| 2026-10-28 | PHB-2026-10: an update was trusted on hashes from the release page alone | Medium | the next Beta |
| 2026-10-07 | PHB-2026-01: local Monero service had no login | **High** | 4.10.0-beta.3, 2026-10-07 (#143) |
| 2026-10-07 | PHB-2026-02: Tor circuit isolation ineffective | Medium | 4.10.0-beta.3, 2026-10-07 (#143) |
| 2026-10-07 | PHB-2026-03: token ticker impersonation valued | Medium | 4.10.0-beta.3, 2026-10-07 (#143) |
| 2026-10-07 | PHB-2026-04: Android kill-switch reset with Orbot | Medium | 4.10.0-beta.3, 2026-10-07 (#143) |
| 2026-10-07 | PHB-2026-05…07: price-list fingerprint, `socks4://` local DNS, fiat field currency | Low | 4.10.0-beta.3, 2026-10-07 (#143) |
| 2026-10-07 | PHB-2026-08: balance chart named the coins held | Low | 4.10.0-beta.3, 2026-10-07 (#145) |
| 2026-10-07 | PHB-2026-09: Linux Tor shipped without its libraries (could not start) | Low | 4.10.0-beta.3, 2026-10-07 (#145) |

All found by the project's own review, none reported from outside; details and affected versions in
the full log.

## Our promises

These are the things that would make everything else on this page untrue, so they are stated plainly:

- Your seed and keys never leave your device. There is no server that could receive them.
- No telemetry, no analytics, no crash reporting, no advertising. Ever.
- Phobia takes **no cut** of your transfers.
- We cannot freeze, seize, or recover your funds, and neither can anyone else holding this software.
- If we ever find that one of these was broken, we will say so publicly, including how long it was
  broken and what we know about the impact.

---

📖 Documentation hub: [docs/INDEX.md](docs/INDEX.md) · Support: [.github/SUPPORT.md](.github/SUPPORT.md)
