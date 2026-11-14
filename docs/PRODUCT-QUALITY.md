# Product quality: what people feel, what makes it worth choosing, and the bar to reach

**Status date:** 2026-11-14 · **Owner:** the maintainer · **Review:** monthly, and after every release  
**Related:** [ROADMAP.md](ROADMAP.md) (the backlog and its IDs) · [PSB.md](PSB.md) (the security baseline) · [STANDARDS.md](STANDARDS.md) (how claims are written)

This document says where Phobia Wallet stands as a product, not only as code. It lists what a person
notices first, what makes the wallet worth choosing over others, and the measurable bar that the next
releases must meet. Every claim here has a proof or is marked as open. Items refer to roadmap IDs.

---

## 1. Where the product stands

### 1.1 Strengths, with proof

| Strength | Proof | Why it matters to a person |
|---|---|---|
| Security claims are checked, not promised | [PSB.md](PSB.md) maps each requirement to a test, a signature or an attestation. Network isolation is a CI job (`NetworkIsolationTests`). | A reader can verify the claim without trusting the project. |
| Transaction formats match reference implementations | Test vectors from Zcash (ZIP-243 and ZIP-244), Stellar, XRP, Polkadot, Nano and Decred. | A payment built by this wallet is accepted by the network, not only by the wallet itself. |
| Features most wallets do not have | Duress and panic passwords, watch-only wallets with PSBT signing, private broadcast for Bitcoin, Privacy Radar, the "what leaked" report after a send. | These answer real questions: who is watching, what a payment reveals, what happens under pressure. |
| Limits are written next to features | Privacy Radar and the send report state what is not protected. First-run statements say the same. | Users are not surprised later, and the text is honest in six languages. |
| Broad test base | 2,211 offline tests and 69 live checks against real explorers, nodes and GitHub (2026-11-08). | Regressions in the send path are caught before release. |
| Supply chain | Build attestations, SBOM, pinned Tor and Monero, immutable release tags, CodeQL and Semgrep with zero open alerts (2026-10-29). | Protects against a tampered download, which is a common attack on wallets. |

### 1.2 What a person notices first, ranked

| Rank | Observation | Roadmap | State on 2026-11-14 |
|---|---|---|---|
| 1 | Unlock is slow, and the window stalls after the password. | N.3 | Not measured yet. The Argon2id cost (64 MiB, four passes, two lanes) is deliberate and takes a second or two by itself. The rest of the unlock is what must be measured. |
| 2 | ZEC sends fail on the public build. | N.0 | Fixed in PR #169 (v5 transactions, NU7 branch id). No release yet. |
| 3 | No hardware wallet. Serious users ask for it first. | P2.1 (H.2) | Not started. Needs physical devices to test on. |
| 4 | No audit. The wallet says so, which is honest, and it still weighs on trust. | R.3 | `AUDIT:NONE`. |
| 5 | Balances come from public servers. When a server is silent, the wallet says "could not be read". | §7 of ROADMAP | Honest behaviour. It is also inconvenient, and it is the first thing a user sees on a bad day. |
| 6 | The Windows publisher is shown as unknown. | R.1 | Self-signed certificate. A certificate from a certificate authority is needed. |
| 7 | The documentation is long, and a newcomer cannot find the main point quickly. | this file, ROADMAP §1 | Improved on 2026-11-08. The README should link here for newcomers. |

### 1.3 Blockers before anything else

These block the next merge and the next release. They come first in the plan.

| ID | Blocker | Next step | Proof when done |
|---|---|---|---|
| Q.1 | Three open review threads on PR #169 (Bugbot): a High finding that legacy plaintext files are taken over by whichever secret opens first, a Medium finding that watch-only UTXO addresses show no history, and a Medium finding that restore leaves the watch list inconsistent. | The maintainer chooses the migration policy for the legacy files: option A (drop them after upgrade, no takeover) or option B (keep the takeover, which can expose the real wallet's records to the decoy). Option A is recommended. Nothing is decided until the maintainer answers. Fix the two Medium findings. Re-run the full offline suite. | All threads resolved; a test for each finding; PR merged. |
| Q.2 | ZEC sends fail on the public build. | Merge #169, then tag the next Beta. | The release page lists the new Beta; a live ZEC send is recorded in the release notes with its date. |
| Q.3 | Unlock is not measured. | Time each step of unlock in a Release build with a scratch vault (N.3). | A table of timings in the PR; the targets in §4 are met or a dated plan exists. |

---

## 2. Altumptic standard: gap to close

[PSB.md](PSB.md) is the conformance page. The table below lists what is open against the standard as a
product, not only as code.

| Requirement (PSB section) | State | Action | Roadmap | Due |
|---|---|---|---|---|
| Independent review of cryptography and key handling (PSB §7: a second reviewer for security-sensitive changes; §9: evidence pack) | `AUDIT:NONE` | Write the scope of an external review of the key, derivation, signing and unlock code. Request two quotes. | R.3 | Scope by 2026-12-31 |
| Reproducible installers (§3.1, §4.2) | Managed assemblies are byte-identical; installers and the APK are not | Publish which files reproduce, and make the installer reproducible or document why it cannot be. | R.4 | 2027-Q1 |
| Certificate from a certificate authority for Windows (§3.2) | Self-signed | Order a certificate for an individual developer. | R.1 | Order by 2026-12-15 |
| Security email and a security.txt on a domain (§8) | Email not decided; security.txt only in the repository | Decide the email. Publish security.txt on a domain the project controls. | R.7, R.8 | 2026-12-31 |
| Limits named next to features (§0 P6) | Done for Privacy Radar, the send report and the first run | Check every new feature against this rule before merge (STANDARDS §2). | ongoing | every change |
| Monthly documentation review (ROADMAP M.6) | Done on 2026-11-08 | Repeat monthly; record the date at the top of each document. | M.6 | 2026-12-08 |
| Altumptic site describes Phobia accurately | Site (`OUTR/src/content.ts`) calls the wallet "анонімний". The standard forbids this claim without proof. | Change the wording on the site to match STANDARDS §2 and §10. This is outside this repository. | — | 2026-11-30 |

---

## 3. What makes people choose it

Ranked by how much they would matter to a careful user, and by how few wallets offer them. A feature is
listed here only if it exists, or if the work is scheduled. Each one needs its claim written under §5.

### 3.1 Verification on every screen (exists partly, make it live)

**What exists:** Privacy Radar and its score in the Security Center; the send report that names the
IP, the kill-switch, the input linkage and the change; the first-run statements.

**What to build:** a line under each action, for example "this payment goes through Tor, the explorer sees
these addresses, the broadcast goes to node X". The information already exists in the send path.

**Why it is unusual:** most wallets say "private" in general. Few say, for this payment, who sees what.

**Roadmap:** new item Q.4. **Test:** the line is generated from the same state the send gate uses
(`SendTransportGate`), so it cannot disagree with the gate.

### 3.2 Cold signing with a phone (new)

**What exists:** PSBT export, review and signing (H.1); watch-only wallets (H.5); the Android APK with Tor
and Monero.

**What to build:** a flow where the computer holds no keys. It builds the payment as a PSBT, shows it as a
QR code, the phone reviews and signs it, and the signed result comes back by QR.

**Why it matters:** a computer that is compromised cannot sign. This is the practical answer to "malware
on my PC", and it does not need a hardware wallet.

**Risks:** the phone becomes the place where keys live, so its signing screen must show the amount, the
address and the fee on its own. This is a large job; it needs a threat model of its own before code.

**Roadmap:** new item P2.6, after Q.1 to Q.3. **Test:** a PSBT built on the computer is signed on the phone
and finalised on the computer, and a tampered PSBT is refused by the phone.

### 3.3 Recovery drill (partly exists)

**What exists:** the phrase check on paper (P1.8).

**What to build:** a practice run that restores a test wallet from the phrase alone, on a machine with no
network, and shows the result in plain words.

**Why it matters:** most people never test their backup. A drill makes the backup real.

**Roadmap:** new item Q.5.

### 3.4 Honest swaps (exists)

**What exists:** swaps paid as quoted, followed to the end, with a price limit (N.9).

**Claim rule:** say what the route charges, what arrives and when a refund starts. Do not say "best rate"
unless a test compares rates across routes.

### 3.5 Things not to build for numbers

- New coins added only for the count. Every coin must meet the full cycle in ROADMAP §4.
- Features that need a server the project runs. The project runs no servers (LOCAL-FIRST).
- Anything described as "anonymous" or "untraceable" (STANDARDS §2).

---

## 4. The quality bar

Each target says where it is measured. Where a target cannot be measured in CI, the device check is named.

| Area | Target | Measured by | Today |
|---|---|---|---|
| Unlock, from Enter to a responsive window | window responds within 1 s after key derivation; full view within 3 s on a typical desktop | timing in a Release build against a scratch vault, recorded in the PR (Q.3) | not measured |
| Derivation itself | a deliberate cost; not a target to remove | the Argon2id parameters are in the source and in PSB | 64 MiB, four passes, two lanes |
| Cold start to the first screen | under 3 s on a typical desktop | timing in the UI harness (`Umbrella.Wallet.UiTests`) | not measured |
| Parsers and attack surface | fuzzing for PSBT, BIP21, address decoding and Zcash transactions, run in CI | a fuzz project and the OpenSSF Scorecard "Fuzzing" check | Scorecard Fuzzing: 0 |
| Accessibility | WCAG 2.2 AA on the home, send, receive and security screens | contrast and focus checks in the UI harness; a manual pass per release | not done (M.2) |
| Onboarding | from install to a first test send in at most three steps, with a small test amount suggested | a scripted walk-through in the UI harness | three steps exist; the test amount is suggested in text |
| Localisation | every user-visible string in six languages | the existing localisation test | done |
| Honesty of claims | no absolute claim in UI or store text | the existing wording test (`L.2`) plus review against STANDARDS §2 | done for the words the test knows |
| Release | signed checksum list, attestation, SBOM, Security section in the notes | `verify-published-release.sh` and RELEASE_CHECKLIST | done, except the first run of the signing job (N.2) |

---

## 5. Claim rules for this product

These apply to the README, the site, the release notes and the first-run text.

1. A claim is a sentence a reader could check. Each one points at its proof (STANDARDS §3).
2. Use the PSB labels only where the proof is linked (STANDARDS §4).
3. Do not use the words forbidden by STANDARDS §2. For the wallet, the most likely slips are "private", "anonymous",
   "secure" and "audited".
4. A feature that is not finished is described as planned, with its roadmap ID.
5. A quality target is a claim too. Publish it only with its measurement.

---

## 6. Plan

Dates are targets. Each item has an ID in ROADMAP §1 or §4, and this document is updated when one closes.

### 30 days (to 2026-12-14)

| Item | Owner | Done when |
|---|---|---|
| Q.1: resolve the three review threads, fix the two Medium findings, merge #169 | maintainer, with a fix PR | merged; all threads resolved |
| Q.2: tag the next Beta with ZEC v5 | maintainer | the release page lists it; notes carry a Security section |
| Q.3 / N.3: measure unlock, move synchronous work off the UI thread | maintainer | a table in the PR; targets met or a dated plan |
| N.4: panic password note in the release notes | maintainer | in the notes |
| M.6: first monthly documentation review | maintainer | date recorded at the top of each document |

### 60 days (to 2027-01-14)

| Item | Done when |
|---|---|
| Q.4: live route line under each send and receive action | built from the send gate's state; test shows they agree |
| Q.5: recovery drill with a test wallet and no network | scripted; result shown in plain words |
| Fuzzing for PSBT, BIP21, address decoding and Zcash transactions | fuzz project in CI; Scorecard Fuzzing check no longer 0 |
| R.7, R.8: security email decided; security.txt on a domain | both public; PSB E-2 and E-3 closed |
| R.4 first step: which installer files reproduce, and what is missing | published in BUILD_VERIFY |

### 90 days (to 2027-02-14)

| Item | Done when |
|---|---|
| R.3: scope of an external review of key handling sent to two firms | scope and quotes in the repository; status changes to `AUDIT:PLANNED` |
| R.1: Windows certificate from a certificate authority | signed release built with it; SmartScreen warning check recorded |
| P2.6: cold signing with a phone, threat model first | threat model merged; prototype behind a flag; not announced until tested on devices |
| WCAG 2.2 AA on the four main screens | checked in the UI harness and by hand |

---

## 7. What this document does not show

- The unlock timings above are targets. Nothing in this document is a measurement yet.
- The phone-signing flow is a design direction, not a design. It needs its own threat model.
- The competitor comparisons are the maintainer's reading of public features. They are not a survey, and
  they must be re-checked before any public comparison is written.
- This review covered the roadmap, the standard and the public documents. It did not re-read THREAT_MODEL,
  12-coins-and-chains, BUILD_VERIFY or HARDWARE_WALLETS in full. Those need the monthly review (M.6).

---

📖 Back to [Documentation Index](INDEX.md)
