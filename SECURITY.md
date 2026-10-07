# Security Policy

Phobia is a self-custody wallet. A bug here can cost somebody everything they hold, so this page is
specific rather than reassuring.

The technical detail — threat model, cryptography, what is deliberately not protected — is in
**[docs/security-model.md](docs/security-model.md)**.

## Reporting a vulnerability

**Report privately:**
**[github.com/thefear078/Phobia-Wallet/security/advisories/new](https://github.com/thefear078/Phobia-Wallet/security/advisories/new)**

Please do **not** open a public issue for anything that could put funds at risk. A public report on a
wallet is a race between the fix and whoever reads it first.

If GitHub advisories are not available to you, contact [t.me/PhobiaStat](https://t.me/PhobiaStat)
and ask for a private channel. Do not put details in a public chat.

### What to include

- What you found, and why it matters.
- Steps to reproduce, or a proof of concept.
- Affected version — the installer version, or the commit if you built it.
- Your OS.
- Whether you believe it is already being exploited.

### What to expect

| | |
|---|---|
| Acknowledgement | within **72 hours** |
| Initial assessment | within **7 days** |
| Fix for a critical issue | as fast as it can be done correctly — days, not weeks |
| Credit | yes, by name or handle, unless you prefer otherwise |
| Disclosure | coordinated. We will agree a date with you; we will not sit on it indefinitely. |

This is a small project. The timelines above are commitments, not an SLA backed by a team — if
something slips, you will hear why rather than nothing.

## Scope

### In scope

Anything that could lose, expose, or lock up funds:

- Key derivation producing a wrong or unspendable address
- Vault encryption weaknesses — KDF parameters, cipher misuse, key handling in memory
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
  detection and education — spam-token folding, address-poisoning warnings — but a user who types
  their seed into a website cannot be saved by the wallet.
- Third-party explorer or RPC outages and rate limits.
- The fact that Bitcoin, Ethereum and other transparent chains are public ledgers. That is the chain's
  design, not our bug. Privacy Radar tells you what a send reveals.
- Zcash shielded addresses. Not implemented, and listed as transparent-only in the wallet.
- Missing features, UI preferences, and "you should use X instead".

## Bug bounty

There is no funded bounty programme yet. Being straight about that rather than implying one:

- **Critical** findings — anything that lets an attacker take funds or extract a seed — will be
  rewarded from project funds, and credited publicly.
- All valid findings get credit and a fix.
- If you would like to see a funded programme, [sponsorship](https://github.com/sponsors/thefear078)
  is what would pay for it.

## Supported versions

| Version | Supported |
|---|---|
| 4.10.0 betas (Phobia **Beta 1**) | ✅ |
| 4.9.x | ✅ |
| 4.8.x | ⚠️ critical fixes only |
| < 4.8 | ❌ |

Always run the latest release. The wallet looks for one by itself (shortly after start, then twice a
day), downloads it and keeps it only if its SHA-256 matches **both** the release's `SHA256SUMS` file and
GitHub's own digest for that file — a disagreement between the two means one was changed after the
release, and the file is deleted. Only a version newer than the one running is ever offered, and only
from this project's own release downloads.

It still never installs anything by itself. Replacing the program always takes your click, because a
wallet that can silently replace its own binary is a wallet with a very attractive update channel. Both
the check and the download can be turned off in Settings → Updates, and both go through Tor when Tor is
on. A matching checksum proves the file is the one on the release page, not who built it — for that,
verify the build attestation as below.

## Verifying what you run

Four independent checks — checksum, signature, build attestation, building it yourself — each with the
exact commands, are in **[docs/BUILD_VERIFY.md](docs/BUILD_VERIFY.md)**. The signing keys' public
fingerprints:

| Platform | Signer | Fingerprint |
|---|---|---|
| Windows (Authenticode) | the fear (thefear078) | SHA-1 `89C2D871C195D57C14D1911B6C47629DE052C553` |
| Android APK | the fear (thefear078) | SHA-256 `C3:80:0E:C6:34:F3:C1:6C:84:4E:62:0B:BB:92:28:81:B6:34:C0:2B:17:40:68:D4:80:F8:9A:DA:A8:95:72:D1` |

A file signed by anything else is not an official build. The Windows certificate is self-signed until a
CA-issued one replaces it, so Windows still shows an unknown publisher; the thumbprint is what to check.

## What we do on our side

| | |
|---|---|
| Static analysis | CodeQL on every push |
| Secret scanning | enabled, with push protection; gitleaks over the whole history every week |
| Dependency alerts | Dependabot, with security updates |
| Vulnerable packages | build fails on a known-vulnerable dependency |
| Supply chain of the build itself | every GitHub Action pinned to a commit SHA; OpenSSF Scorecard published weekly |
| Bundled Tor / Monero | pinned versions, checked against the projects' signed sums on every PR (`supply-chain` job) |
| Branch protection | `main` requires all checks green; no force-push, no deletion |
| Release tags | immutable: a ruleset forbids moving or deleting `v*` tags; a published release is never rebuilt |
| Tests | about 1,850 offline tests plus rendered-screen tests, required before merge; the Monero service's login is tested against the real `monero-wallet-rpc` in CI |

No external audit has been performed. Status and future links live in
**[AUDIT_STATUS.md](AUDIT_STATUS.md)**. When an audit exists, it will be linked there and here with the
full report, including findings — not a badge alone.

## Security update process

When a vulnerability is confirmed:

1. **Within 72 hours:** acknowledge receipt.  
2. **Within 7 days:** assessment and a fix timeline.  
3. **Fix released** under coordinated disclosure.  
4. **CVE** requested when severity is High or Critical and appropriate.  
5. **Credit** publicly unless the reporter asks otherwise.

## Release integrity

Each release should include:

| Asset | Status |
|---|---|
| `SHA256SUMS-<label>.txt` | ✅ shipped |
| Build attestation over every artifact **and** the sums file | ✅ keyless, via GitHub OIDC (since 4.8.0) |
| Authenticode signature on every Windows executable | ✅ from the release after 2026-10-07 (self-signed, see above) |
| APK signed with one stable key | ✅ from the release after 2026-10-07 |
| SBOM (SPDX) `PhobiaWallet-<label>-sbom.spdx.json` | ✅ from the release after 2026-10-07, covered by the sums and the attestation |

Verify an attestation with the GitHub CLI:

```bash
gh attestation verify PhobiaWallet-Setup-<version>.exe --repo thefear078/Phobia-Wallet
```

It checks, against a public transparency log rather than against anything we say, that those exact
bytes came out of this repository's release workflow at a specific commit. Releases published before
4.8.0 have checksums only — an attestation cannot be added to a build after the fact, and claiming
otherwise would defeat the point. The same goes for signatures and SBOMs on older releases.

## Historical vulnerabilities

Full log: **[SECURITY/VULNERABILITY_HISTORY.md](SECURITY/VULNERABILITY_HISTORY.md)**.  
Process: **[SECURITY/COORDINATED_DISCLOSURE.md](SECURITY/COORDINATED_DISCLOSURE.md)**.

| Date | ID | Severity | Fixed in |
|---|---|---|---|
| 2026-10-07 | PHB-2026-01 — local Monero service had no login | **High** | next release (#143) |
| 2026-10-07 | PHB-2026-02 — Tor circuit isolation ineffective | Medium | next release (#143) |
| 2026-10-07 | PHB-2026-03 — token ticker impersonation valued | Medium | next release (#143) |
| 2026-10-07 | PHB-2026-04 — Android kill-switch reset with Orbot | Medium | next release (#143) |
| 2026-10-07 | PHB-2026-05…07 — price-list fingerprint, `socks4://` local DNS, fiat field currency | Low | next release (#143) |

All found by the project's own review, none reported from outside; details and affected versions in
the full log. Disclosed reports will be added the same way, with links to advisories.

## Bug bounty

**Currently unfunded.** There is no paid bounty programme yet. Responsible disclosure is still
welcome under the process above; researchers will be credited publicly unless they ask otherwise.

The project is **MIT-licensed** so independent review and patches are welcome ([LICENSE_CHANGE.md](LICENSE_CHANGE.md)).
An external audit remains planned (roadmap **R.5** / [AUDIT_STATUS.md](AUDIT_STATUS.md)) — until then, do
not describe the wallet as audited.

When funding exists, the intended structure is:

| Severity | Example | Target reward | Acknowledgement |
|---|---|---|---|
| Critical | Loss or theft of funds / seed | TBD (aim: meaningful fixed range) | ≤ 72 hours |
| High | Privacy bypass of kill-switch / Tor-only, vault crypto flaw | TBD | ≤ 72 hours |
| Medium | Non-fund UX / honesty bugs that mislead about risk | Credit | ≤ 7 days |

This table is a **promise of structure**, not a funded programme. It will not be described as “active
bounty” until rewards are actually available.

---

📖 Documentation hub: [docs/INDEX.md](docs/INDEX.md) · Support: [.github/SUPPORT.md](.github/SUPPORT.md)

## Our promises

These are the things that would make everything else on this page untrue, so they are stated plainly:

- Your seed and keys never leave your device. There is no server that could receive them.
- No telemetry, no analytics, no crash reporting, no advertising. Ever.
- Phobia takes **no cut** of your transfers.
- We cannot freeze, seize, or recover your funds — and neither can anyone else holding this software.
- If we ever find that one of these was broken, we will say so publicly, including how long it was
  broken and what we know about the impact.
