# Pre-beta checklist

**Last verified:** 2026-10-10 · Wallet [4.10.0-beta.6](../VERSION) (Beta)  
**Repo:** [thefear078/Phobia-Wallet](https://github.com/thefear078/Phobia-Wallet)

This replaces stale audits that still claim “missing LICENSE / CODEOWNERS / CoC / Dependabot”.
Those items are **done**. What remains before a **public beta** is mostly **code** (P0 fund-safety)
and **process** (EV certificate order) — not more markdown stubs.

---

## Phases (product first, stores later)

Legal and store markdown is **already written** — leave it alone while you ship coins, unless behaviour
changes. Engineering order:

1. **Core** — remaining fund-safety polish + **H.2 hardware wallets** ([ROADMAP.md](ROADMAP.md), [HARDWARE_WALLETS.md](HARDWARE_WALLETS.md))  
2. **Coins** — [WORKFLOW.md](WORKFLOW.md) + [adding-a-chain.md](adding-a-chain.md)  
3. **Stabilize** — leftover P1 / P2  
4. **Release prep** — EV cert (≥60 days), store submission, **external audit (R.5)**

Day-to-day: [WORKFLOW.md](WORKFLOW.md). Doc links: `pwsh scripts/check-doc-links.ps1`.

---

## A. Documentation spider — DONE

- [x] [docs/INDEX.md](INDEX.md) hub + back-links on root/docs pages
- [x] [README.md](../README.md) navigation table + badges (CI, CodeQL, Security, release, **MIT**)
- [x] [getting-started.md](getting-started.md), [ROADMAP.md](ROADMAP.md), [REPO_HARDENING.md](REPO_HARDENING.md)
- [x] [CONTACT.md](../CONTACT.md) — real channels (GitHub, Telegram, TikTok, Reddit, Advisories). **No** fake `legal@…example` addresses.

## B. Legal / brand — DONE

| Item | Status | Note |
|------|--------|------|
| [LICENSE](../LICENSE) | ✅ | **MIT** (+ trademark notice). See [LICENSE_CHANGE.md](../LICENSE_CHANGE.md). |
| [LEGAL/LICENSE_SUMMARY.md](../LEGAL/LICENSE_SUMMARY.md) | ✅ | Plain English |
| [TRADEMARK_POLICY.md](../TRADEMARK_POLICY.md) | ✅ | Brand protection |
| [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | ✅ | Community / issues / Telegram |
| [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md) | ✅ | Non-custodial, 18+, liability |
| [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) | ✅ | Store formal policy |
| [APP_STORE_NOTES.md](../APP_STORE_NOTES.md) | ✅ | §5 Apple 3.1.5, §6 Play, §7 Microsoft below |
| [GEO_BLOCKING.md](../GEO_BLOCKING.md) | ✅ | Counsel review still required for enforcement |
| [AUDIT_STATUS.md](../AUDIT_STATUS.md) | ✅ | Explicitly **not audited** |
| [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | ✅ | Tor / Monero / NuGet |
| [LEGAL/](../LEGAL/README.md) | ✅ | Pointers + App Store privacy map |
| [SECURITY/](../SECURITY/README.md) | ✅ | Coordinated disclosure + history log |

## C. GitHub hardening — DONE

| Item | Status |
|------|--------|
| [.github/CODEOWNERS](../.github/CODEOWNERS) | ✅ |
| [.github/dependabot.yml](../.github/dependabot.yml) | ✅ |
| [.github/pull_request_template.md](../.github/pull_request_template.md) | ✅ |
| [.github/ISSUE_TEMPLATE/](../.github/ISSUE_TEMPLATE/) (bug, feature, security) | ✅ |
| [.github/SUPPORT.md](../.github/SUPPORT.md) | ✅ |
| [.gitleaks.toml](../.gitleaks.toml) | ✅ |
| Branch rulesets (PR + CI, no force-push/delete) | ✅ |
| Secret scanning + push protection | ✅ |
| Dependabot security updates | ✅ |
| CodeQL + Security workflows (gitleaks, dependency-review, nuget vulnerable) | ✅ |
| Private vulnerability reporting | ✅ |

Live status detail: [REPO_HARDENING.md](REPO_HARDENING.md).

## D. Store readiness — docs DONE / UI + org OPEN

| Platform | Docs | Remaining |
|----------|------|-----------|
| GitHub side-load (primary) | ✅ | Keep shipping checksums |
| Apple App Store | ✅ notes | Org Apple account + L.1–L.8 **UI** + legal entity (R.2) |
| Google Play | ✅ notes | Business verification + L.1–L.8 **UI** |
| Microsoft Store | ✅ notes (§7) | EV/OV signing (R.2/R.6); optional Store listing (R.7) |
| Linux packages | ✅ notes | PGP package signing (L.6 / R.3) |

## E. Code gates before **public** beta — OPEN

Do **not** call a public beta “fund-safe” until these are green:

| ID | Task | Status |
|----|------|--------|
| **P0.0** | Full restore proof (seed-only recover past gap) | ✅ `RestoreFromSeedProofTests` — finds **and spends** funds on issued address #15 after the local state is deleted |
| **P0.6** | Fail-closed balance (no fake `0.0000` on network error) | ✅ unread balances read “—” with a reason; the total says what it is missing |
| **P0.7** | Send-path transport gate (Tor settings enforced) | ✅ refused at Review **and** Confirm when the live route is not the chosen one |
| **P0.8** | Network isolation CI (Tor-only cannot clearnet) | ✅ own CI job; a loopback listener proves no socket is opened |
| L.1–L.3, L.8 | First-run disclaimer + 18+ + ToS/Privacy accept in UI | ✅ shipped — gates create/import/unlock, versioned acceptance, 6 languages |
| P0.1–P0.5 | HD history judged against the whole address set; Tor/Monero signed-sum pins; published-release check; one capability matrix; zkSync sends | ✅ see [ROADMAP.md](ROADMAP.md) §3 |
| Net | Tor circuits really separated per purpose, Monero service behind a login | ✅ 2026-10-07 — `SocksHandshakeTests`, `MoneroRpcLoginTests` |
| Tests | `dotnet test` green on CI | ✅ on `main` PRs — 1,860 offline tests on 2026-10-08 |

## E2. Release artifacts — check before telling anyone to download

| Item | Status |
|------|--------|
| Windows installer + portable exe, Linux tar.gz, Android APK, `SHA256SUMS-<label>.txt` on the release page | ✅ since the first beta (2026-10-02) — the sums match GitHub's own digests, every file has a build attestation; a beta also carries numbered copies for older copies' updaters, with their own sums |
| Android APK signed with a **stable** key (repository secrets `ANDROID_KEYSTORE_B64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS`) | ✅ set 2026-10-07 — every APK since carries the key in [BUILD_VERIFY.md](BUILD_VERIFY.md#b-signature); a copy from before that date is one reinstall |
| Windows code signing (SmartScreen) | 🟡 since 2026-10-07 every exe and the installer are signed and timestamped (`WINDOWS_CERT_PFX_B64` / `WINDOWS_CERT_PASSWORD`), but self-signed — SmartScreen still warns until a CA-issued certificate replaces it (R.2; the routes: [CODE_SIGNING.md](CODE_SIGNING.md)) |
| Tags point at the commit each release was built from | ✅ restored 2026-10-07 (moved to `6a533d4` on 2026-10-03; the first beta's copies attached to old release pages were deleted). A ruleset now forbids moving or deleting `v*` tags, and the release workflow refuses a release that already has files |
| SBOM on the release page | ✅ since 2026-10-07 — `PhobiaWallet-<label>-sbom.spdx.json`, in the sums and the attestation |

Track in [ROADMAP.md](ROADMAP.md) §3.

## F. Process (human) — OPEN

| Task | Owner | Note |
|------|-------|------|
| Order **OV/EV code signing** (R.2 / R.6) | Founder | Start **≥60 days** before Windows Store / SmartScreen goal |
| Confirm legal entity if submitting to Apple as org | Founder | Required by Apple 3.1.5 (i) |
| External audit (R.5) | Later | After beta stability — keep [AUDIT_STATUS.md](../AUDIT_STATUS.md) honest |

---

## Verdict

| Layer | Ready for public beta? |
|-------|-------------------------|
| Docs / legal / GitHub hardening | **Yes** |
| Philosophy consistency | **Yes** |
| Fund-safety P0 code + first-run UI | **Yes** — P0.0–P0.8 and the first-run UI are done |
| Release artifacts | **Yes for a GitHub beta** — signed (Windows self-signed), attested, with an SBOM; a CA-issued Windows certificate is still missing |
| Store submission (Apple/Play/MS) | **Not yet** — the in-app disclaimers are done; a legal entity and code signing are not |

**Private / friends beta** on GitHub Releases is fine **today** if testers accept experimental risk and never put more than they can lose — and if CONTACT / SECURITY channels are used for bugs.

---

📖 Back to [Documentation Index](INDEX.md) · Hardening: [REPO_HARDENING.md](REPO_HARDENING.md)
