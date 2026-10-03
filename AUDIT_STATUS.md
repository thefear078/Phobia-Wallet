# Audit status

**Last updated:** 2026-10-02 (Phobia Beta 1)

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
| Offline unit / integration tests (about 1,800) | CI `desktop` job |
| Reproducible build check (two paths, byte-identical assemblies) | CI `reproducible-build` job |
| Signed-transaction vectors: every send path, swap and staking encodings | `desktop/tests` |
| CodeQL | `.github/workflows/codeql.yml` |
| gitleaks / Dependabot / vulnerable-package gate | Security workflows |
| Public threat model & privacy docs | [THREAT_MODEL.md](THREAT_MODEL.md), [PRIVACY.md](PRIVACY.md) |
| Coordinated vulnerability disclosure | [SECURITY.md](SECURITY.md) |

## Planned

See [docs/ROADMAP.md](docs/ROADMAP.md) item **R.5** (external security audit) and reproducible-build attestations (**R.1**).

## Contact

Security reports: [GitHub Security Advisories](https://github.com/thefear078/Phobia-Wallet/security/advisories/new)  
Author: [CONTACT.md](CONTACT.md)

---

📖 Back to [Documentation Index](docs/INDEX.md)

