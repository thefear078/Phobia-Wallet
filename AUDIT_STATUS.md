# Audit status

**Last updated:** 2026-10-08

## External security audit

**No independent third-party security audit has been performed yet.**

When one is completed, this file will link:

- auditor name and scope;
- date and commit/tag audited;
- full report (including findings), not only a marketing badge;
- which items were fixed and which remain accepted risk.

Until then, do **not** describe Phobia as “audited” in README, store listings, or press.

## What exists today (not a substitute for an audit)

| Control | Where |
|---|---|
| Offline unit / integration tests (about 1,850) | CI `desktop` job |
| Rendered-screen tests: every desktop and phone section drawn headless with the real styles | `desktop/tests/Umbrella.Wallet.UiTests` |
| Monero service login checked against the real `monero-wallet-rpc` | CI `supply-chain` job |
| Bundled Tor and Monero checked against the projects' signed sums | CI `supply-chain` job |
| Reproducible build check (two paths, byte-identical assemblies) | CI `reproducible-build` job |
| Signed-transaction vectors: every send path, swap and staking encodings | `desktop/tests` |
| CodeQL | `.github/workflows/codeql.yml` |
| gitleaks / Dependabot / vulnerable-package gate | Security workflows |
| Every GitHub Action pinned to a commit SHA; OpenSSF Scorecard weekly | `.github/workflows/*.yml`, `scorecard.yml` |
| Signed releases (Authenticode, stable APK key), build attestations, SBOM, immutable tags | [docs/BUILD_VERIFY.md](docs/BUILD_VERIFY.md) |
| Public threat model & privacy docs | [THREAT_MODEL.md](THREAT_MODEL.md), [PRIVACY.md](PRIVACY.md) |
| Coordinated vulnerability disclosure | [SECURITY.md](SECURITY.md) |

## Internal reviews

| Date | Scope | Result |
|---|---|---|
| 2026-10-07 | Beta readiness: network privacy checked on the wire (SOCKS handshakes, DNS), the local Monero service, Android kill-switch, prices and currency, release pipeline | Seven issues, one High — all fixed, listed in [SECURITY/VULNERABILITY_HISTORY.md](SECURITY/VULNERABILITY_HISTORY.md) |

An internal review is the author checking their own work. It finds real bugs (the table above is the
proof) but it is not independent, and it is not called an audit anywhere.

## Planned

See [docs/ROADMAP.md](docs/ROADMAP.md) item **R.5** (external security audit) and a CA-issued Windows code-signing certificate (**R.2**).

## Contact

Security reports: [GitHub Security Advisories](https://github.com/thefear078/Phobia-Wallet/security/advisories/new)  
Author: [CONTACT.md](CONTACT.md)

---

📖 Back to [Documentation Index](docs/INDEX.md)

