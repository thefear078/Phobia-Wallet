# Phobia Wallet — roadmap

**Product version:** 4.10.0-beta.6, shown to people as “Beta” (betas are not numbered in the app, the release titles or these documents).  
**Next Beta:** the code on branch `claude/roadmap-p2`, in pull requests [#167](https://github.com/thefear078/Phobia-Wallet/pull/167) and [#169](https://github.com/thefear078/Phobia-Wallet/pull/169) (#169 is stacked on #167).  
**Status date:** 2026-11-08. Change this line whenever a status in this file changes.  
**Standard:** Altumptic Product Security Baseline v1.0, class A (custody) — [PSB.md](PSB.md).  
**Owner:** the maintainer. **Review:** at every release, and once a month (see [STANDARDS.md](STANDARDS.md)).

Legend: ✅ done · 🟡 partly done · ⏳ in progress or next · 📅 planned · ❌ not planned.

Shipped work is listed here in one line each, with where it is proved. The full entries live in [CHANGELOG.md](../CHANGELOG.md). Where this file and another document disagree, this file is right and the other document needs an update.

---

## 1. Now

In order. Each item says why it is here and what finishes it.

| ID | Item | Why now | Next step | Status |
|---|---|---|---|---|
| **N.0** | **Zcash NU7 is active.** Block 3,543,405 has passed (the chain was at 3,552,733 on 2026-11-08). NU7 refuses v4 transactions, so **ZEC sends from Beta 6 fail.** | Users cannot send ZEC from the public build. | Merge #167, then #169. Tag the next Beta. | ⏳ code ready in #169 (v5, ZIP-244); no release yet |
| **N.1** | Merge pull requests #167 and #169 | Both carry this branch's work. | The maintainer merges (automated merges are blocked for this project). | ⏳ both open; #169 CI 11/11 green |
| **N.2** | First real run of the `sign-checksums` release job | Tested only locally, against Beta 6's list. | Watch the job on the next tag. Then run `scripts/verify-published-release.sh` on the published files. | ⏳ |
| **N.3** | Unlock is slow and the window stalls after the password | Reported on the maintainer's PC, 2026-11-08. Not measured yet. | Time each step of `SetUnlocked` in a Release build against a scratch vault. Move synchronous work off the UI thread. Targets: the window responds within 1 s of the key derivation; the full view is ready within 3 s on a typical desktop. | 📅 not measured |
| **N.5** | Three open review threads on #169 (one High): legacy plaintext files are taken over by the first secret that opens, watch-only UTXO history is empty, and restore leaves the watch list inconsistent | Blocks the merge under the branch rule. Details and steps: [PRODUCT-QUALITY.md §1.3](PRODUCT-QUALITY.md#13-blockers-before-anything-else) (Q.1). | ⏳ open |
| **N.4** | Set the panic password again after updating | The Argon2id check replaced PBKDF2 in the October build. | Put it in the release notes of the next Beta. | ⏳ |

---

## 2. Principles

Phobia follows the Altumptic PSB principles (PSB §0). For this product they mean:

- **Fund safety first.** A wrong balance on screen, a send that can pay twice, or a key in a log is a bug of the highest class.
- **The user verifies; the user does not have to trust.** Every claim points to a proof: a test, a signature, an attestation or a command.
- **Fail closed.** A network or verification error never becomes a zero balance, a clearnet fallback or an unsigned update.
- **Name the limits.** Each feature says what it does not protect, next to the feature.

Feature rule: no new feature before the open P0 and P1 items are closed, unless the user asks for it. The working rules are in [WORKFLOW.md](WORKFLOW.md).

---

## 3. Release gate

A release is announced only when every line below holds. The checklist with commands is [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

1. The offline suite is green. The Live suite has been run, and each failure is explained (a rate limit is named as one).
2. The same version everywhere: `VERSION`, the tag, the installer, the README and the CHANGELOG heading.
3. The checksum list is signed (`SHA256SUMS-<label>.txt.sig`). Attestations and the SBOM are attached. `scripts/verify-published-release.sh` passes on the published files.
4. The release notes have a **Security** section, even when it says there are no security-relevant changes.
5. The coin matrix (§6) matches the code (`CapabilityMatrixTests`).
6. Every server the code can contact is listed with what it learns (`NetworkCounterpartyTests`).
7. No PSB exception (§8) is past its date.

---

## 4. Open work

### P0 — fund safety and honest state

| ID | Item | Status and next step |
|---|---|---|
| P0.1 | Zcash NU7 release | See N.0. |
| P0.2 | Unlock responsiveness | See N.3. |

### P1 — user-facing gaps

All P1 items from the 2026 backlog are closed (§5). Open items that users will notice:

| ID | Item | Status and next step |
|---|---|---|
| P1.1 | NEAR staking on the Staking screen (N.16) | 🟡 The network half is done and pinned to three mainnet transactions. The screen, the copy and a real-funds check remain. |
| P1.2 | Staking on Cardano, Polkadot (nomination pools) and TON (nominator pools) (N.16) | 📅 In that order. Each ships after a real-funds check. ETH only through a pool the user picks. |
| P1.3 | THORChain refund detail and retry for a failed broadcast (N.9) | 🟡 Paid as quoted and followed to the end is done. The remaining parts are the refund detail and the retry. |
| P1.4 | Load older history where a source has a cursor (N.13) | 📅 |
| P1.5 | Electrum-server option for BTC and LTC, and a documented self-hosted setup | 📅 |
| P1.6 | Settings, the wallet list and the address counters sealed (PSB exception E-1) | 📅 Review by 2027-04-30. Moving the counters is a change of its own, because they set how far each wallet is scanned for funds. |

### P2 — privacy and hardware

| ID | Item | Status and next step |
|---|---|---|
| P2.1 | Ledger and Trezor: signing on the device (H.2) | 📅 Design in [HARDWARE_WALLETS.md](HARDWARE_WALLETS.md). Needs the devices to test on; nothing ships on emulator evidence alone. Order: Bitcoin, Ethereum family, then the other coins as each device's app allows. |
| P2.6 | Cold signing with a phone: the computer builds a PSBT, the phone reviews and signs it through QR | 📅 Design direction in [PRODUCT-QUALITY.md §3.2](PRODUCT-QUALITY.md#32-cold-signing-with-a-phone-new). Threat model first; no code before it. |
| P2.2 | Receiving a PayJoin (BIP-78) | 📅 Needs an always-reachable endpoint. Sending is done (P2.2 in §5). |
| P2.3 | CoinJoin (P2.3) | 📅 |
| P2.4 | Silent Payments for BTC (P2.5) | 📅 |
| P2.5 | Zcash shielded: receive, balance, send (P2.6) | 📅 Needs ZIP-32 derivation, a light-wallet server or full node for scanning, the note-commitment tree, and proofs built on the device. Plan: bundle the Zcash project's Rust library per platform, fetched and verified like Tor and Monero. Until then the wallet says “transparent only” wherever a ZEC address is shown. |

### P3 — platforms and maintainability

| ID | Item | Status and next step |
|---|---|---|
| H.3 | Multisig, 2-of-3 | 📅 Long term. |
| H.4 | Android | 🟡 Beta APK with Tor and Monero, signed with one stable key. Not yet on Play or F-Droid. |
| H.6 | macOS, Apple silicon and Intel | 📅 Needs a Developer ID (US$99 a year), notarisation, Tor and Monero fetched and verified for macOS, and a Mac to test on. Not offered for download until it is signed, notarised and has passed the isolation check (P0.8 pattern) on that platform. |
| H.7 | iOS | 📅 After macOS and after Android leaves beta. Tor and Monero must be linked in as libraries, because iOS cannot run bundled programs. |
| M.1 | Split `MainViewModel` into feature views | 🟡 Partial classes exist. This file keeps growing, so the split should follow the next large feature. |
| M.2 | Design system and WCAG AA on the critical screens | ⏳ |
| M.3 | Price alerts | 📅 |
| M.4 | Read-only exchange connectors, on request | 📅 |
| M.5 | NFT or staking in the main navigation | ❌ Not before a full model and a spam policy exist. |
| M.6 | Documentation review | ⏳ Monthly. The standard is in [STANDARDS.md](STANDARDS.md). |

### R — release trust and compliance

| ID | Item | Status and next step |
|---|---|---|
| R.1 | Windows certificate from a certificate authority (SmartScreen) | 📅 The current certificate is self-signed, so Windows shows an unknown publisher. The pipeline already signs with it. A CA certificate for an individual developer is the next step; the maintainer applies. |
| R.2 | EV signing and Microsoft Store (R.6, R.7) | 📅 Optional. Without EV, GitHub side-loading stays the main channel. |
| R.3 | External security audit (R.5) | 📅 AUDIT:NONE. Until one is done, nothing is described as audited. |
| R.4 | Reproducible installers (R.1 in the old numbering) | 📅 Managed assemblies are byte-identical (CI). Installers, the apphost and the APK are not yet. |
| R.5 | Linux package signing with PGP (L.6) | ⏳ Needed for Flatpak, Snap and distribution repositories. |
| R.6 | Store compliance (L.4, L.5, L.7) | 📅 L.4 at store submission. L.5 and L.7 after legal review. See §10. |
| R.7 | Security email address (PSB E-2) | Decision for the maintainer. Due 2026-12-31. The maintainer's personal address is not published anywhere. |
| R.8 | `security.txt` on a website (PSB E-3) | 📅 Due 2026-12-31. The file exists in the repository. |
| R.9 | MD5 in the Monero daemon's login (PSB E-4) | Upstream behaviour. Review 2027-04-30. |

---

## 5. Done

Each line names what is proved and where. Dates are when the work landed in a merged change or in the next Beta.

| Area | What | Proved by | Since |
|---|---|---|---|
| Vault | Argon2id 64 MiB, AES-256-GCM, two-slot deniable file for every wallet | `EncryptedFileSeedVault`, `DuressWalletScenarioTests` | 4.x |
| Restore | Restore from the phrase alone, funds found on address 15 and spent (P0.0) | `RestoreFromSeedProofTests` | 2026-09 |
| History | Each transaction judged against the wallet's whole address set (P0.1) | history tests for BTC, LTC, BCH | 2026-10-02 |
| Fail-closed | Balances read as unknown, never zero (P0.6); send gate at review and confirm (P0.7); isolation CI (P0.8) | `SendTransportGate`, `NetworkIsolationTests`, `KillSwitchTests` | 2026-09 to 2026-10-07 |
| Supply chain | Tor and Monero pinned to their signed sums (P0.2); checksum CI (P0.3); one capability matrix (P0.4); zkSync sends with the chain's own gas (P0.5) | `check-pinned-binaries.sh`, `verify-published-release.sh`, `CapabilityMatrixTests` | 2026-09 to 2026-10 |
| Duress | Decoy password (P1.1); hidden-wallet unlock (P1.2); panic password (P1.3) | `DuressWalletScenarioTests`, `PanicPasswordTests` | 2026-10-16 |
| Honesty | Phrase dry-run on paper (P1.8); Privacy Radar limits (P1.11); “what leaked?” after a send (P1.12); send needs the password (P1.21) | `PhraseCheckTests`, `PrivacyScoreInspectorTests`, `SendLeakReportTests` | 2026-10 |
| Activity | One screen with filters and honest coverage (P1.10); address book with confirmation (P1.9); connection chip (P1.6); simulation before confirm (P1.4) | `SendSafetyIntegrationTests` | 2026-10 |
| Self-verify | Watch-only xpub export (P1.20); [VERIFY_YOUR_WALLET.md](VERIFY_YOUR_WALLET.md) | `AccountXpubTests` | 2026-10 |
| Bitcoin | Taproot find, show and spend (P2.1); PayJoin sender (P2.2); PSBT export, review and sign (H.1); private broadcast (P2.4) | BIP-86 vectors; `PayjoinSenderTests` | 2026-09 to 2026-10-28 |
| Tokens | Any ERC-20 (N.1); any TRC-20 (N.2); jettons and SPL, both token programs (N.3) | per-chain send tests and live validator checks | 2026-09 |
| Chains | XRP (N.4), Stellar (N.5), Cosmos (N.6), NEAR (N.7), Polkadot (N.8) | each pinned to its reference SDK's vectors | 2026-09 |
| Swaps | Paid as quoted, followed to the end, price limit on THORChain (N.9) | `ThorchainQuoteLiveTests`, swap follow tests | 2026-10-10 to 2026-10-16 |
| Zcash | Transparent send with ZIP-243 (N.10); v5 with ZIP-244 and NU7 branch id in #169 | ZIP-243 and ZIP-244 vectors; `ZcashV5Tests`; `ZcashConsensusLiveTests` | 2026-10-14, 2026-10-29 |
| Nano, Decred | Nano receive, balance and send (N.11); Decred receive, balance and send (N.12) | `NanoBlockTests`, `DecredTransactionTests`, live send checks | 2026-10-08 |
| History | Depth per source (N.13); Base, Arbitrum, Linea, BNB Chain, Polygon (N.14); tokens on EVM networks (N.15); your own explorer key (N.15) | `EvmTokenHistoryLiveTests`, `ExplorerKeyTests` | 2026-10-10 to 2026-10-28 |
| Staking | TRON, Solana, Cosmos (2026-10-02); yield from the network's own figures (2026-10-10); NEAR pool calls (N.16, network half) | `NearStakingTests` | 2026-10-28 |
| Watch-only | Seedless wallets from descriptors, SLIP-132 keys and addresses (H.5), in #169 | `WatchPsbtTests`, `AccountXpubTests` | 2026-10-28 |
| Security baseline | PSB class A (R.11); signed checksum list (R.10, #169); records sealed per wallet and secret (R.12, PHB-2026-11, #169) | [PSB.md](PSB.md), `ReleaseSignatureTests`, `WalletDataSealingTests`, `DuressRecordsTests` | 2026-10-29 to 2026-10-30 |
| Release trust | Attestations (R.3); SBOM (R.4); immutable tags (R.8); pinned Actions and Scorecard (R.9); managed assemblies reproducible (R.1) | `reproducible-build` CI job; `verify-published-release.sh` | 2026-10-07 |
| Android | Beta APK with Tor and Monero in the package (H.4) | emulator CI job | 2026-10-02 |
| First run | Not a financial institution, no anonymity claim, age gate, terms acceptance (L.1, L.2, L.3, L.8) | UI strings in six languages; wording test | 2026-09 |

---

## 6. Coin matrix (summary)

This table is checked by `CapabilityMatrixTests`: a ✅ in Send must match a sendable coin in the code. Full detail per network: [12-coins-and-chains.md](12-coins-and-chains.md).

| Symbol | Receive | Balance | Send | History | Note |
|---|:---:|:---:|:---:|:---:|---|
| BTC | ✅ | ✅ | ✅ | ✅ | coin control; restored Taproot found and spent |
| LTC | ✅ | ✅ | ✅ | ✅ | |
| BCH | ✅ | ✅ | ✅ | ✅ | |
| DOGE | ✅ | ✅ | ✅ | ✅ | |
| ETH | ✅ | ✅ | ✅ | ✅ | any held ERC-20 |
| OP | ✅ | ✅ | ✅ | ✅ | |
| Arb / Base / Linea | ✅ | ✅ | ✅ | ✅ | history while the wallet holds ETH there |
| zkSync Era | ✅ | ✅ | ✅ | ✅ | gas from the chain's own estimate |
| TRX | ✅ | ✅ | ✅ | ✅ | any held TRC-20 |
| SOL | ✅ | ✅ | ✅ | ✅ | SPL tokens, both token programs |
| TON | ✅ | ✅ | ✅ | ✅ | jettons |
| ADA | ✅ | ✅ | ✅ | ✅ | |
| XMR | ✅ | ✅ | ✅ | ✅ | Monero full wallet, through the local monero-wallet-rpc |
| AVAX | ✅ | ✅ | ✅ | ✅ | |
| BNB / MATIC | ✅ | ✅ | ✅ | ✅ | history while the wallet holds the coin |
| FTM / CRO | ✅ | ✅ | ✅ | ❌ | no keyless history source; the explorer key covers it |
| ZEC | ✅ | ✅ | ✅ | ✅ | transparent only; Beta 6 sends are refused after NU7, fixed in #169 (N.0) |
| XRP | ✅ | ✅ | ✅ | ✅ | history shows the amount delivered |
| XLM | ✅ | ✅ | ✅ | ✅ | |
| ATOM | ✅ | ✅ | ✅ | ✅ | staked ATOM is not in the balance |
| NEAR | ✅ | ✅ | ✅ | ✅ | implicit accounts |
| DOT | ✅ | ✅ | ✅ | ✅ | send from Asset Hub |
| XNO | ✅ | ✅ | ✅ | ✅ | |
| DCR | ✅ | ✅ | ✅ | ✅ | |

---

## 7. Known limits

These are stated in the app and in the README. They stay until they are fixed.

- **No external security audit.** See [AUDIT_STATUS.md](../AUDIT_STATUS.md).
- **Windows publisher is unverified.** The certificate is the maintainer's own (R.1).
- **Transparent chains are public.** Tor hides the IP address, not the ledger. Privacy Radar shows what a send reveals.
- **Zcash is transparent only** until P2.5.
- **Dandelion++ cannot be done from a wallet.** Bitcoin Core never adopted it. The wallet's private broadcast is the part it can do (P2.4 in §5).
- **Some history cannot be read without a key.** Fantom and Cronos need an explorer key of your own.
- **The seed is on a general-purpose computer** until P2.1 (hardware) ships. Watch-only wallets (H.5) keep the seed off the computer for viewing and for preparing payments.
- **Monero** needs the monero-wallet-rpc program on the computer, and antivirus can remove it. Restore the file, or add an exclusion yourself; the wallet never changes security settings for you.

---

## 8. Altumptic PSB exceptions

Each exception has an end date and a compensating control. An exception past its date is a defect. The full table with controls is in [PSB.md §12](PSB.md#12-exceptions).

| ID | Deviation | Ends | Owner | Status |
|---|---|---|---|---|
| E-1 | Settings, the wallet list and the address counters are plain text (owner-only) | 2027-04-30 | maintainer | Narrowed 2026-10-30; history and activity are sealed |
| E-2 | No dedicated security email address | 2026-12-31 | maintainer | Decision open (R.7) |
| E-3 | `security.txt` is in the repository, not on a website | 2026-12-31 | maintainer | Open (R.8) |
| E-4 | Monero daemon's HTTP Digest login uses MD5 | 2027-04-30 | upstream | Review |

---

## 9. Milestones

| Date | What happened |
|---|---|
| 2026-09-15 | Consolidated this roadmap from the older documents |
| 2026-09-20 | P0 fund-safety items closed |
| 2026-10-01 | Beta 1: the Phobia name, new design, Market and Activity rebuilt |
| 2026-10-07 | Readiness audit: circuit isolation made real, Monero login, Orbot on Android |
| 2026-10-15 | Beta 6 published (the last public build) |
| 2026-10-16 | Every P1 item closed (duress, panic, phrase check, price limit) |
| 2026-10-28 | Watch-only wallets, private broadcast, your own explorer key, NEAR pool calls |
| 2026-10-29 | PSB class A assessment; signed checksum list in #169; Zcash v5 ready for NU7 |
| 2026-10-30 | Records sealed per wallet and secret (PHB-2026-11); build installed on the maintainer's PC |
| 2026-11-08 | Zcash NU7 active on mainnet; this roadmap and the standard rewritten; N.0–N.4 opened |

---

## 10. Store and legal

Store listings are a separate track. The documents are in the repository: [`APP_STORE_NOTES.md`](../APP_STORE_NOTES.md), [`GEO_BLOCKING.md`](../GEO_BLOCKING.md), [`LEGAL/README.md`](../LEGAL/README.md).

Rules that apply to every store, and to this README and the site:

- The wallet is **non-custodial software**. It is not a financial institution, and it says so at first run.
- **Do not claim** “fully private”, “anonymous”, “untraceable” or “secure from government”. For a transparent chain, say what the chain reveals.
- Monero may be described as a privacy coin, honestly. Bitcoin and Ether are never described as anonymous.
- A **KYC-less on-ramp** is not added to the core wallet without a separate legal decision.
- Keep a side-load channel (GitHub Releases) that does not depend on any store.

| Item | Status |
|---|---|
| Terms, Privacy Policy, store notes, geo policy, contacts | ✅ in the repository |
| First-run statements and age gate | ✅ in the app |
| Google Play business verification and listing copy (L.4) | 📅 At submission |
| Geo-blocking (L.5, L.7) | 📅 After legal review. Not confused with Tor censorship. |
| Linux package signing (L.6) | ⏳ |

---

## 11. Deliberately not planned

- Advertising, telemetry, analytics
- Custody of user funds
- Growth features that weaken security
- KYC or an on-ramp inside the core wallet without a legal decision
- A web or backend component as part of the product
- Forks or rebrands without permission (see [LICENSE](../LICENSE))
- Claims of anonymity or full privacy without the limits written next to them

---

## 12. Sources of truth

| Document | Role |
|---|---|
| **This file** | The backlog: what is open, what is done, what comes next |
| [STANDARDS.md](STANDARDS.md) | How every document in this repository is written and kept up to date |
| [PSB.md](PSB.md) | Conformance with the Altumptic baseline, with proofs and exceptions |
| [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) | What must hold before a release is announced |
| [WORKFLOW.md](WORKFLOW.md) | How work is done, reviewed and merged |
| [12-coins-and-chains.md](12-coins-and-chains.md) | Detail for each network |
| [CHANGELOG.md](../CHANGELOG.md) | What has shipped, release by release |
| [THREAT_MODEL.md](../THREAT_MODEL.md), [security-model.md](security-model.md), [SECURITY_REVIEW.md](SECURITY_REVIEW.md) | What is defended, and where the defence ends |
| [BUILD_VERIFY.md](BUILD_VERIFY.md), [VERIFY_YOUR_WALLET.md](VERIFY_YOUR_WALLET.md) | How a user checks a build and the wallet's behaviour |
| [HARDWARE_WALLETS.md](HARDWARE_WALLETS.md) | Design for P2.1 |
| [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) | The public-beta gate, as it stands |
| [INDEX.md](INDEX.md) | The documentation hub |
| [`../MANIFESTO.md`](../MANIFESTO.md) | The rules the backlog must follow |
| [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md), [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md) | Earlier planning notes. Kept as history; this file is newer where they differ. |

---

📖 Back to [Documentation Index](INDEX.md)
