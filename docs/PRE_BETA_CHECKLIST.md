# Public-beta checklist

**Status date:** 2026-11-08 · **Public build:** 4.10.0-beta.6 (“Beta”, 2026-10-15) · **Next Beta:** code in PR [#167](https://github.com/thefear078/Phobia-Wallet/pull/167) and [#169](https://github.com/thefear078/Phobia-Wallet/pull/169)  
**Repository:** [thefear078/Phobia-Wallet](https://github.com/thefear078/Phobia-Wallet)

This is the gate for telling people to download a beta. Each line says what is true today and what proves it.
The rows use the IDs of [ROADMAP.md](ROADMAP.md). The standard is [PSB.md](PSB.md).

---

## Verdict

| Layer | State on 2026-11-08 |
|---|---|
| Documents, legal text, GitHub hardening | ✅ ready |
| First-run statements, age gate, terms acceptance | ✅ in the app |
| Fund-safety code (P0.0–P0.8) | ✅ in the public build |
| Zcash sends | ❌ **the public Beta 6 cannot send ZEC.** NU7 is active; the fix is in #169 and needs a release (N.0) |
| Unlock responsiveness | ❌ **slow and stalls after the password** on the maintainer's PC. Not measured yet (N.3) |
| Release artifacts | ✅ for the GitHub channel: checksums, attestations, SBOM. The Windows signature is self-signed |
| Signed checksum list | 🟡 in #169; the first run of the signing job happens on the next tag (N.2) |
| Store submission | ❌ not yet: no legal entity, no certificate from a certificate authority, no store listing copy |

**GitHub beta, for testers who accept experimental risk:** usable for everything except ZEC sends, until the
next Beta is released. Testers keep only what they can afford to lose, and report anything that touches funds
through [SECURITY.md](../SECURITY.md).

---

## A. Documents — done

| Item | Status |
|---|---|
| [INDEX.md](INDEX.md) hub, links from the root and docs pages | ✅ the link check passes |
| [README.md](../README.md): badges, results with dates, honest part, roadmap summary | ✅ |
| [ROADMAP.md](ROADMAP.md), [STANDARDS.md](STANDARDS.md), [WORKFLOW.md](WORKFLOW.md) | ✅ rewritten 2026-11-08 |
| [PSB.md](PSB.md), [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md), [security.txt](../.well-known/security.txt) | ✅ 2026-10-29 |
| [CONTACT.md](../CONTACT.md): real channels only, no invented addresses | ✅ |

## B. Legal and store documents — done

| Item | Status | Note |
|---|---|---|
| [LICENSE](../LICENSE) | ✅ | MIT, with the trademark notice; see [LICENSE_CHANGE.md](../LICENSE_CHANGE.md) |
| [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md), [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | ✅ | |
| [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md), [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) | ✅ | non-custodial, 18+, liability |
| [APP_STORE_NOTES.md](../APP_STORE_NOTES.md) | ✅ | Apple 3.1.5, Google Play, Microsoft |
| [GEO_BLOCKING.md](../GEO_BLOCKING.md) | ✅ | enforcement waits for legal review (R.6) |
| [AUDIT_STATUS.md](../AUDIT_STATUS.md) | ✅ | states plainly: not audited |
| [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | ✅ | Tor and Monero versions match the fetch scripts (`check-pinned-binaries.sh`) |
| [SECURITY/](../SECURITY/README.md) | ✅ | disclosure process and history |

## C. GitHub hardening — done

| Item | Status |
|---|---|
| CODEOWNERS, Dependabot, pull request template, issue templates, SUPPORT | ✅ |
| Gitleaks configuration, secret scanning with push protection | ✅ |
| Branch rulesets: pull request and CI required; no force-push or deletion; `v*` tags immutable | ✅ |
| CodeQL, Semgrep, dependency review, vulnerable-package check | ✅ |
| Private vulnerability reporting | ✅ |
| Live status | [REPO_HARDENING.md](REPO_HARDENING.md) |

## D. Store readiness — open

| Platform | Documents | Still needed |
|---|---|---|
| GitHub Releases (primary) | ✅ | a signed checksum list on every release (#169, N.2) |
| Apple App Store | ✅ notes | an organisation account and a legal entity; the first-run statements are done (R.1, R.6) |
| Google Play | ✅ notes | business verification; listing copy at submission (R.6, L.4) |
| Microsoft Store | ✅ notes | a certificate from a certificate authority, or EV (R.1, R.2) |
| Linux packages | ✅ notes | PGP package signing (R.5) |

## E. Code gates for a public beta

| ID | Gate | State |
|---|---|---|
| P0.0 | Restore from the phrase alone, funds found and spent past the gap | ✅ `RestoreFromSeedProofTests` |
| P0.6 | Unreadable balances never shown as zero | ✅ `BalanceReadout`: “—” with a reason |
| P0.7 | Send path must match the Tor settings | ✅ refused at review and at confirm (`SendTransportGate`) |
| P0.8 | Tor-only mode opens no clearnet socket | ✅ `NetworkIsolationTests` in its own CI job |
| L.1, L.2, L.3, L.8 | First-run statements, age gate, terms acceptance | ✅ in the app, six languages |
| Tor circuits per purpose | Each purpose on its own circuit, names resolved by Tor | ✅ 2026-10-07 |
| Monero service | Behind a random login the account alone can read | ✅ 2026-10-07 (`MoneroRpcLoginTests`) |
| Offline tests | Green on every pull request | ✅ 2,211 offline tests on 2026-11-08 |
| **N.0** | Zcash sends work after NU7 | ❌ in the public build; ✅ in #169 (needs merge and release) |
| **N.3** | Unlock responsive | ❌ not measured yet |

## F. Release artifacts

| Item | State |
|---|---|
| Windows installer, portable exe, Linux tarball, Android APK, `SHA256SUMS-<label>.txt` | ✅ on every release since 2026-10-02 |
| Build attestations and SBOM | ✅ since 2026-10-07 |
| Checksum list signed with the project key (`.sig`) | 🟡 in #169; first run on the next tag (R.10, N.2) |
| Android APK signed with one stable key | ✅ since 2026-10-07 (see [BUILD_VERIFY.md](BUILD_VERIFY.md)) |
| Windows certificate | 🟡 self-signed since 2026-10-07; SmartScreen warns until a certificate authority issues one (R.1) |
| Tags point at the commit each release was built from | ✅ tags are immutable; the release workflow refuses to add files to a published release |

## G. Process — open, for the maintainer

| Task | ID | Note |
|---|---|---|
| Merge #167, then #169 | N.1 | automated merges are not used for this project |
| Order a certificate from a certificate authority | R.1 | at least 60 days before any Store or SmartScreen goal |
| Legal entity, if Apple is to be submitted as an organisation | R.6 | required by Apple 3.1.5 (i) |
| Security email address | R.7 | decision; due 2026-12-31 (PSB E-2) |
| External audit | R.3 | after beta stability; [AUDIT_STATUS.md](../AUDIT_STATUS.md) stays honest until then |

---

📖 Back to [Documentation Index](INDEX.md) · Hardening: [REPO_HARDENING.md](REPO_HARDENING.md) · Standard: [STANDARDS.md](STANDARDS.md)
