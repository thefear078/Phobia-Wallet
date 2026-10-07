# Threat model

Who this wallet defends against, how, and — the part that matters more — where the defence ends.

A threat model that only lists wins is marketing. Each vector below states the attacker, what the
wallet actually does, and what remains true afterwards. Where the honest answer is "this does not
help", it says so.

**Scope.** Phobia is a wallet for Windows and Linux, .NET 8 / Avalonia, with an Android beta built from
the same code. There is no server, no account and no backend: nothing about a user exists anywhere
except on their own device. Where the phone differs — Android may stop its Tor in the background (see
[Vector 4](#vector-4--the-network-operator-isp-café-wi-fi-hostile-country)) — the vector says so.

## Contents

- [Trust boundaries](#trust-boundaries)
- [Vector 1 — Malware running as the user](#vector-1--malware-running-as-the-user-infostealer-keylogger-rat)
- [Vector 2 — Someone at the keyboard](#vector-2--someone-at-the-keyboard-of-an-unlocked-or-locked-machine)
- [Vector 2b — Someone standing over you](#vector-2b--someone-standing-over-you-while-you-unlock-it)
- [Vector 3 — Offline brute force of `vault.json`](#vector-3--offline-brute-force-of-a-stolen-vaultjson)
- [Vector 4 — The network operator](#vector-4--the-network-operator-isp-café-wi-fi-hostile-country)
- [Vector 5 — The block explorer or RPC node](#vector-5--the-block-explorer-or-rpc-node)
- [Vector 6 — Chain analysis](#vector-6--chain-analysis-of-your-own-transactions)
- [Vector 7 — Compromised release binary](#vector-7--a-compromised-release-binary-supply-chain)
- [Vector 8 — Telemetry and metadata leakage](#vector-8--telemetry-and-metadata-leakage-from-the-app-itself)
- [Vector 9 — Data left after wipe](#vector-9--data-left-behind-after-delete-everything)
- [Vector 10 — Swap routes and staking servers](#vector-10--swap-routes-and-staking-servers)
- [What is deliberately out of scope](#what-is-deliberately-out-of-scope)
- [Reporting a vulnerability](#reporting-a-vulnerability)

---

## Trust boundaries

| Trusted | Why |
|---|---|
| The local OS kernel and filesystem | A desktop app runs inside it. If this is compromised, nothing below matters. |
| The CPU's RNG via `RandomNumberGenerator` | Seed entropy comes from the OS CSPRNG, not a language RNG. |
| The bundled Tor binary and `monero-wallet-rpc` | Shipped with the build; verified by release checksums. |

| **Not** trusted | Consequence |
|---|---|
| The network | Everything goes over TLS; Tor is available for every request and enforced by a kill-switch. |
| Every block explorer and RPC node | They are named, listed, and replaceable by the user. |
| Price feeds | Never given an address; treated as a coin-name oracle only. |
| Any exchange the user connects | Opt-in, read-only keys, stored encrypted. |
| The settings file | Re-validated on load, never trusted because it was written earlier. |

---

## Vector 1 — Malware running as the user (infostealer, keylogger, RAT)

**Attacker.** Code already executing with the user's privileges on the same machine.

**What the wallet does.** The vault is Argon2id (m = 64 MiB, t = 4, p = 2) → AES-256-GCM with versioned
associated data. Keys are zeroed after signing. The seed screen sets `WDA_EXCLUDEFROMCAPTURE`, so the
window renders black to screenshots and screen sharing. The clipboard is cleared 45 seconds after an
address is copied, and only if it still holds exactly what the wallet put there. Auto-lock defaults to
five minutes.

**Where the defence ends.** It does not hold. An attacker at this level can:

- capture the vault password with a keylogger while you type it;
- read the clipboard before the clear timer;
- view the screen via remote desktop / RAT even when capture APIs are blocked for screenshots;
- scrape process memory while the wallet is unlocked — and the unlocked mnemonic is a .NET `string`,
  which cannot be reliably zeroed (immutable strings, GC moves, `SecureString` is a no-op outside
  Windows).

**No desktop wallet survives a fully compromised user session.** Full-disk encryption helps a stolen
powered-off disk; it does not help malware already running as you.

**Mitigation (partial today, stronger next):**

- **H.1 ✅** — Bitcoin **PSBT** export/import so spends can be signed on a hardware wallet or
  air-gapped machine; Phobia only signs coins its own scan found. Guide:
  [docs/HARDWARE_WALLETS.md](docs/HARDWARE_WALLETS.md).
- **H.2 🏆 next** — USB Ledger / Trezor so the seed never decrypts on this PC for spends.
- **H.3** — Multisig 2-of-3 for amounts that justify operational complexity.
- **Best-effort memory hygiene** — `SensitiveBytes` zeroes key material buffers after use. It does
  **not** make an unlocked .NET `string` mnemonic safe against a memory scraper.

Until H.2 ships, residual risk for **hot-wallet spends on this PC** stays **HIGH**. Duress/decoy
passwords defend against **coercion**, not malware.

**Residual risk: HIGH** for unlocked hot wallet on a compromised PC. **Lower** when signing only via
hardware / PSBT and the seed is never unlocked here for spends.

---

## Vector 2 — Someone at the keyboard of an unlocked or locked machine

**Attacker.** A person with physical access and time — a border official, a flatmate, a thief.

**What the wallet does.** Failed unlocks are counted and persisted: three free, then an exponential
backoff (5 s → 10 s → 20 s → … capped at 15 minutes). Persisted, because a counter that resets when the
window is closed stops nobody. Capped, because a forgotten password must not hold the owner's own
funds hostage.

**Where the defence ends.** Anyone who can edit the settings file resets the counter, and anyone who
can copy `vault.json` attacks it offline where no UI rule applies (see Vector 3). The throttle raises
the cost of guessing at the keyboard and nothing more.

**Residual risk: MEDIUM**, dominated by password strength.

---

## Vector 2b — Someone standing over you while you unlock it

**Attacker.** A person who can compel the password: a border official, a robbery, anybody the owner
cannot simply refuse.

**What the wallet does.** A **duress password** opens a different, real wallet — its own seed, its own
addresses, its own history. The vault file holds two slots and is the same size whether one wallet is
stored or two, the unused slot is random bytes, and the real slot's position is chosen at random, so
the file cannot be used to argue that something else is in there. A wrong password and an empty slot
fail identically, and removing a decoy leaves a file indistinguishable from one that never had one.

**Where the defence ends.** It does not hide that OTHER wallets exist on the machine — a coercer who
looks at the data directory sees the list. It does nothing about being watched while typing, or about
somebody who compares a copy of the file taken before and after (a slot that was noise and is now
ciphertext proves a wallet was added). And changing the vault password rewrites the file, which
removes the decoy; the app says so beside the feature.

**Residual risk: MEDIUM.** The wallet can make the money unreachable with the password that was
given. It cannot make a determined person stop asking.

---

## Vector 3 — Offline brute force of a stolen `vault.json`

**Attacker.** Someone who copied the vault file — from a backup, a stolen disk, a synced folder.

**What the wallet does.** Argon2id with 64 MiB of memory per guess, 4 iterations, 2 lanes. Memory-hard
by design: GPU and ASIC parallelism buys far less than it does against PBKDF2 or bcrypt. The vault file
is a fixed-size binary with two slots; its parameters are not read from the file at all, so a tampered
vault cannot ask for a trivially cheap derivation. (Older JSON vaults are still read, with their
parameters range-checked, and are rewritten in the new format the first time they are opened.)

**Where the defence ends.** At the password. A short or reused password falls regardless of the KDF,
and no parameter choice fixes that. The wallet enforces a minimum length and shows a strength meter;
it cannot enforce a good password.

**Residual risk: LOW for a strong passphrase, HIGH for a weak one.**

---

## Vector 4 — The network operator (ISP, café Wi-Fi, hostile country)

**Attacker.** Anyone who can watch or modify traffic between the machine and the internet.

**What the wallet does.** Every request goes over TLS with certificate validation; the validation
callback is never overridden, and a test fails the build if anyone adds one. Tor ships inside the
build as a child process on its own port, separate from any Tor Browser. The **kill-switch is
fail-closed**: with Tor-only on and Tor down, the shared HTTP client refuses connections in its
`ConnectCallback`, before DNS and before a socket. There is no fallback to clearnet — that silent
fallback is a classic wallet vulnerability and this one does not have it.

Requests are also **split across separate Tor circuits by purpose**. On one circuit a single exit
relay sees the wallet ask an explorer "what is the balance of bc1q…" and then, minutes later, hand
over a transaction spending it — and can tie the two together by timing alone. Chain data, broadcasts,
prices, swap quotes, an exchange account, PayJoin and maintenance traffic each get their own circuit,
so the relay that saw an address is not the relay that receives the spend. Tor keys a circuit on the
SOCKS5 username/password, so this needs no extra dependency.

Until 4.10.0-beta.2 this was claimed and not true: the per-purpose label was written into the proxy
URI, which .NET's SOCKS client ignores, so every request reached Tor with no credentials and shared
one circuit. `SocksHandshakeTests` now runs a SOCKS5 server on loopback and reads the username and the
destination type off the wire — including that the destination goes to Tor as a **name**, never as an
address this machine looked up in its own DNS.

A circuit per *server* as well (Tor Browser's per-site isolation) was built and measured, and not
shipped: a fresh wallet asks some thirty servers at once, and with a circuit to build for each, Tor
left the Bitcoin-family balance scans unfinished after four minutes. So within one purpose, one exit at
a time sees the lookups the wallet sends to different explorers; two explorers that pooled their logs
could join them on that exit and the minute.

Every per-purpose client is built from the same proxy and kill-switch state as the shared one and torn
down whenever that state changes. A cached client outliving the kill-switch being armed would be a
hole in the kill-switch itself — worse than not isolating — so `TorStreamIsolationTests` pins it, and
the assertion was verified by removing the teardown and watching the test fail.

**On Android** (from Beta 2) the APK carries the same Tor, run from the native-library folder, with the
same switch, kill-switch and per-purpose circuits; CI's `device-check` job proves on an emulator that it
starts and bootstraps. Android may stop it while the app is in the background: the wallet starts it again
on return, and until then requests are refused, never direct. Orbot's SOCKS port (`127.0.0.1:9050`) as
the custom proxy remains an alternative, with the kill-switch kept armed across restarts while it is set.

A custom proxy typed as `socks4://` is used as SOCKS4a and `socks5h://` as SOCKS5: plain SOCKS4 makes
.NET resolve the server's name in this machine's DNS first, which would name every explorer the wallet
uses to whoever runs that DNS.

**Where the defence ends.** Traffic timing and volume are still observable. A `.onion` node removes
the exit node; a clearnet node over Tor does not. Isolation splits *who sees what*; it does not hide
that a Tor user is doing something.

**Residual risk: LOW with Tor on, HIGH with it off** — which the Security Center says out loud rather
than scoring generously.

---

## Vector 5 — The block explorer or RPC node

**Attacker.** Whoever answers "what is the balance of this address".

**What they learn.** The connecting IP (an exit node with Tor on), that it belongs to a wallet,
roughly which blocks it asked for, when it is online, and which connection a transaction entered the
network through. Crucially: **your addresses**, and the ability to tie every address asked about in one
session to one person.

**What the wallet does.** Names every one of them in Settings, with who runs it and what it learns.
Lets the user point any chain — Bitcoin, Litecoin, Bitcoin Cash, Dogecoin, Ethereum, Solana, TON,
Tron, Cardano, Monero — at a different company or at their own node. A server the user chose is never
silently replaced by the default. The catalog cannot fall behind the code: the build fails if a host
appears in the source without appearing in the list.

**Where the defence ends.** Tor hides the IP. **It does not un-send the address.** The only complete
answer is running your own node, which the wallet supports and cannot do for you.

**Residual risk: MEDIUM with a public node, LOW with your own.**

---

## Vector 6 — Chain analysis of your own transactions

**Attacker.** Anyone reading the public ledger afterwards — which is everyone, forever.

**What the wallet does.** A fresh receive address per payment on BTC, LTC, BCH and DOGE, offered
exactly where the wallet also scans and can spend across every address it issued. Coin control on the
UTXO chains, so a spend need not join coins from different parts of a life. Privacy Radar reads the
pending transaction offline and says what it would reveal — chiefly which addresses it links. Change
always goes to a freshly derived internal address.

**Where the defence ends.** A transparent chain publishes the amount and both addresses permanently.
A spend that joins two addresses cannot be un-joined. Whoever you paid knows you paid them. Phobia
has **no CoinJoin, no PayJoin, no Dandelion++ and no Taproot** — these are real gaps, listed in the
roadmap rather than implied away.

**Residual risk: HIGH on transparent chains. Monero is the answer the wallet actually has.**

---

## Vector 7 — A compromised release binary (supply chain)

**Attacker.** Someone who replaces the download, or compromises the build.

**What the wallet does.** Releases are built in public CI from a tagged commit. A per-version
`SHA256SUMS-<label>.txt` is generated from the attached artifacts, self-verified before publishing,
and refuses to publish if empty. The artifact set is checked for completeness and for strays, so a
partial release cannot yield a manifest that silently omits a file. Dependencies are pinned, scanned
by Dependabot, and the source is analysed by CodeQL on every PR. Since 2026-10-07 also:

- **Signatures.** Every Windows executable and the installer carry the author's Authenticode signature,
  timestamped, and the APK is signed with one stable key; the release workflow refuses a signature from
  any other certificate. Fingerprints in [SECURITY.md](SECURITY.md#verifying-what-you-run).
- **Provenance.** Every file and the sums file carry a keyless build attestation (since 4.8.0), checked
  against the public Sigstore log rather than against this repository, and each release ships an SBOM.
- **Reproducible core.** The managed assemblies — the code that derives keys, signs and routes — are
  built twice in CI from different paths and must be byte-identical; anyone can build the tag and compare.
- **The build itself.** Every GitHub Action is pinned to a commit SHA, release tags are immutable (a
  ruleset forbids moving or deleting them), and a published release is never rebuilt. Bundled Tor and
  Monero are checked against their projects' signed sums on every PR.

**Where the defence ends.** The Windows certificate is **self-signed**: it proves a file is unchanged
since the release workflow signed it, not who the publisher is, and Windows still warns. The installer,
apphost and APK are not bit-for-bit reproducible. Signing keys live in the repository's encrypted
secrets, so whoever controls the GitHub account controls the signatures — the attestation then still
names the workflow and commit, and an immutable tag makes a quiet swap visible. A CA-issued certificate
and an external audit are on the roadmap.

**Residual risk: LOW–MEDIUM** for a user who checks the signature or the attestation; **MEDIUM** for one
who checks nothing.

---

## Vector 8 — Telemetry and metadata leakage from the app itself

**Attacker.** The wallet's own developers, present or future.

**What the wallet does.** There is no analytics SDK, no crash reporter, no account, and **no
application log at all** — a log is the easiest place for an address or an amount to end up sitting in
the clear. This is enforced rather than promised: the build fails if any of nineteen analytics or
crash-reporting packages is referenced, if any of their call shapes appears in the source, or if
anything writes a log file.

**Where the defence ends.** The bundled Monero daemon writes its own log, which is its business, and a
future maintainer can always change the rules. The test makes that a deliberate act with a visible
diff rather than an accident.

**Residual risk: LOW.**

---

## Vector 9 — Data left behind after "delete everything"

**Attacker.** Whoever examines the machine afterwards.

**What the wallet does.** The wipe removes the encrypted seed, every additional wallet, settings and
profile images, watch addresses, exchange keys, the address book, private transaction notes, the
activity log, cached balances and prices, and the Monero wallet. A test scans the source for every
path written under the data directory and fails the build if the wiper does not handle it.

The address book is now encrypted at rest under a seed-derived key, and an existing plaintext book is
migrated and the readable copy deleted. **Until 4.7 it was plaintext and survived the wipe** — the list
of who somebody pays, left behind by the act of erasing the wallet.

**Where the defence ends.** Files are deleted, not shredded; on an SSD, recovery of unlinked blocks is
a question for the drive's firmware, not for this program. Full-disk encryption is the correct answer
and is the operating system's job.

**Residual risk: LOW after 4.7, and it was not before.**

---

## Vector 10 — Swap routes and staking servers

**Attacker.** A swap route (THORChain, NEAR Intents, Exolix) that quotes or settles dishonestly, or a
server that builds a staking transaction other than the one asked for (TronGrid builds TRON's).

**What they could try.** Quote one figure and pay out less; keep coins paid to a custodial route;
return a TRON transaction that votes for someone else, freezes more, or sends TRX away, hoping it is
signed unread.

**What the wallet does.**
- Swaps go to the user's own receiving address, never an exchange account. The quote is read again at
  Confirm and carries a minimum; every payment except an in-wallet THORChain deposit goes through the
  ordinary Send review and password. Routes are tried by trust and the custodial one (Exolix) is used
  only for Monero, Nano and Decred, which nothing decentralised reaches, and is shown in a warning
  colour before anything is paid.
- TRON staking: the wallet decodes the protobuf TronGrid returned and refuses to sign unless it is
  exactly one contract of the expected type, from this wallet, for the amount, resource and Super
  Representative chosen, and the id it signs is the SHA-256 of those bytes.
- Solana and Cosmos staking transactions are built on this machine; the Cosmos node only simulates one
  for its gas, and the account's sequence is checked again just before signing.

**Where the defence ends.** A custodial route that keeps the coins it was paid cannot be stopped by the
wallet — which is why it is named and avoided wherever possible. A Cosmos validator that misbehaves can
be slashed, and delegators lose a small share with it.

**Residual risk: LOW for THORChain, NEAR Intents and staking; MEDIUM for the minutes a custodial swap
holds the coins.**

---

## What is deliberately out of scope

- **Recovering a lost seed phrase.** There is no path. That is the same property that means nobody can
  freeze the funds either.
- **Protecting against the recipient.** Whoever you pay knows they were paid by you.
- **Making a transparent chain private.** It cannot be done from the wallet side.
- **Defending a rooted or malware-infected machine.** See Vector 1.
- **Hiding that this computer holds a wallet at all.** A duress password hides which wallet the
  password opens, not the fact that the program is installed (see Vector 2b).

---

## Reporting a vulnerability

See [SECURITY.md](SECURITY.md). The rules the code is held to are in [MANIFESTO.md](MANIFESTO.md);
the implementation detail is in [docs/security-model.md](docs/security-model.md).

---

📖 Back to [Documentation Index](docs/INDEX.md) · [README](README.md)

