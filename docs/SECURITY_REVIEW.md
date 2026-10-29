# Security self-review

*Reviewed 2026-10-09 against `main` (Phobia Wallet Beta, 4.10.0-beta.5; still current for 4.10.0-beta.6), and again
on 2026-10-29 against the Altumptic security baseline ([§7 below](#7-against-the-altumptic-baseline-2026-10-29), [PSB.md](PSB.md)). Not an external audit — there has
been none yet ([AUDIT_STATUS.md](../AUDIT_STATUS.md)). It is the code read from an attacker's side, with
what was looked at, how, and what came of it, so anyone can repeat it.*

The questions a wallet has to answer before anybody should trust it, one by one.

## 1. Can a secret reach a log, the console or another process?

**Looked at:** every write to standard output, standard error, debug/trace output and log files
(`Console.*`, `Debug.*`, `Trace.*`, `ILogger`, `Android.Util.Log`, `File.Append*`, `StreamWriter`); crash
handlers; the arguments of every program the wallet starts.

**Found:**

- The wallet writes **no logs at all** and has **no crash reporter** — nothing to leak, and nothing sent
  anywhere when it fails.
- The only log lines are the Android **self-test** build's (CI only, `PHOBIA_SELFTEST`), which print paths
  and the words OK / FAIL — no key, phrase or address.
- `monero-wallet-rpc` runs at `--log-level=0`, and the Monero keys it restores from travel in the body of a
  loopback request behind a random login — **never on the command line**, where any account on the
  computer could read them from the process list. Tor's command line holds a config path and a process id.

**Verdict:** nothing to fix.

## 2. Is memory cleared after secrets are used?

**Looked at:** the vault's decrypt path, the session's lifetime of the phrase and passwords, key
derivation and every signer (`ZeroMemory` / `SensitiveBytes.Clear` call sites).

**Found:**

- The vault's decrypted bytes and the AES key derived from the password are **wiped in a `finally`**,
  whatever happens (`EncryptedFileSeedVault`).
- Raw private-key buffers in the signers (Nano, NEAR, Stellar, Polkadot sr25519, BIP39 seed bytes) are
  wiped after use; exchange API secrets are dropped on lock; the BIP32 account-key cache is cleared on lock.
- **Limitation, stated rather than hidden:** while the wallet is unlocked the recovery phrase — and the vault
  password kept so one password opens every wallet — are .NET `string`s. A managed string cannot be
  overwritten in place: the runtime may also have copied it during garbage collection. Both are dropped
  when the wallet locks (auto-lock on idle and on minimise, `Ctrl+L`), and become unreachable, but the bytes
  stay in freed memory until reused. Someone able to read this process's memory while it runs can read the
  phrase; that is in the [threat model](../THREAT_MODEL.md) as out of scope (malware on the machine), and it
  is the same for every .NET or JVM wallet. Moving the phrase to a pinned, wipeable byte buffer end to end
  is the next step (it touches every deriver).

## 3. Are the files on disk protected from other users of the computer?

**Looked at:** where data lives (`AppPaths`), how every file is written (`AtomicFile`), and what each file
holds.

| File | Content | At rest |
|---|---|---|
| `vault.json`, `wallets/*.vault.json` | recovery phrase | **encrypted** — Argon2id (64 MiB, 4 passes) → AES-256-GCM |
| transaction notes, exchange API keys, address book, explorer key | your notes and names; read-only keys | **encrypted** — HKDF-SHA256 from the phrase, one context per store → AES-256-GCM (`SealedFile`) |
| watch-only wallet description | the extended public keys it watches | **encrypted** — HKDF-SHA256 from a random key in the vault → AES-256-GCM |
| panic-password check | an Argon2id hash, or random bytes when none is set | not reversible |
| watched addresses, activity log, kept transaction history, swaps, settings, cached balances, wallet index | addresses and labels; public transaction ids and amounts | plain text, owner-only — exception E-1 in [PSB.md](PSB.md#12-exceptions), until 2027-01-31 |

**Found — and fixed in this review:** on **Linux** (and macOS) the data folder was created with the default
umask: `0755` for the folder, `0644` for files. Any other account on the same computer could read the plain
text files — the address book, the watched addresses, the activity log. The vault stayed encrypted, but
those files say a great deal about the person. Now:

- the data folder is set to **`0700`** at every start (so a folder an older version made is tightened too),
  and every file the wallet writes is created **`0600`** — owner only from the moment it exists, not
  changed afterwards (`AppPaths.RestrictToOwner`, `AtomicFile`; `DataFolderPermissionTests`, run in CI on
  Linux). On 2026-10-29 the last four writers that made their own temporary files (the vault, the wallet
  index, the removed-wallet index and the address index) were moved onto `AtomicFile` too; the `0700`
  folder had kept other accounts out of them meanwhile.

**Windows:** a data folder under `%APPDATA%` is private to your account by its ACL. A folder **beside the
program** (portable, or installed to another drive such as `D:\Phobia`) inherits that drive's ACL, which on
many machines lets other local accounts read it. On a computer shared with other people, keep Phobia in
your user folder. **Android:** the app's private storage, closed to other apps by the system.

## 4. Can anything on the computer talk to the wallet's local services?

- **Monero service** (`monero-wallet-rpc`): listens on `127.0.0.1` only, behind a random login written to a
  file only this account can read — a web page (cross-site POST) or another account cannot tell it to
  send. A copy left by a crash is ended; a port already taken is refused rather than handed the keys
  (fixed in 2026-10, [vulnerability history](../SECURITY/VULNERABILITY_HISTORY.md)).
- **Tor**: SOCKS on loopback, its own data directory, dies with the wallet (`__OwningControllerProcess`).
  No control port is opened.

## 5. What does the network learn?

One HTTP client for everything (`PublicHttp`); with **Tor-only** on, a request that cannot go through Tor is
refused before a socket opens (tested with a listener that must see no connection). Each purpose — chain
data, broadcasts, prices, swaps, exchange accounts, updates — gets its own Tor circuit, and names are
resolved by Tor, never by this computer's DNS (checked on a real SOCKS5 handshake). Every server the code
can contact is declared with what it learns, and a test fails the build if one is missing
(`NetworkCounterpartyTests`); the list is in Settings → Privacy. What Tor cannot hide: the addresses
themselves — [PRIVACY.md](../PRIVACY.md).

## 6. Supply chain

CodeQL (C#, and the workflows) and Semgrep on every push and pull request; Dependabot alerts and security
updates; dependency review on pull requests; the build fails on a known-vulnerable package; secret scanning
with push protection plus gitleaks over the whole history; every GitHub Action pinned to a commit; OpenSSF
Scorecard. Tor and Monero are fetched at pinned versions and checked against the projects' signed hash
lists. Releases are signed, attested (keyless, Sigstore) and carry an SBOM; tags cannot be moved —
[BUILD_VERIFY.md](BUILD_VERIFY.md).

## 7. Against the Altumptic baseline (2026-10-29)

**Looked at:** every cryptographic call, the update path end to end, how links reach the system, the
transport settings, and which layer can reach the network.

**Found, and fixed in the same change:**

- **Updates were trusted on hashes from the release page alone.** The checksum list and the file came from
  the same page, so whoever could replace both could ship an "update" the wallet accepted, on Linux and
  Android in particular, where no other signature was checked. The list is now signed in the release
  workflow and the wallet refuses a release whose list its pinned key does not verify; on Windows the
  program must also carry the project's Authenticode signature. Recorded as PHB-2026-10 in the
  [vulnerability history](../SECURITY/VULNERABILITY_HISTORY.md).
- **The panic-password check used PBKDF2** in the unreleased branch; it is Argon2id now, before it ever shipped.
- **Sealed stores derived their key with one SHA-256** of the phrase and a name. Safe with a 24-word
  phrase, but not the baseline's KDF: now HKDF-SHA256 with the store name also bound in as associated
  data; old files open and are rewritten.
- **Links were checked by prefix** before being handed to the system. They are now parsed: absolute
  `https://`, a host, no user name or password, no control characters (`SafeHttpsUrlTests`).
- **TLS versions were left to the operating system.** Now TLS 1.2 or 1.3 only, on the one client every
  request uses.
- **No test held the line that signing code never touches the network.** `SigningPathIsolationTests` now
  fails the build if Core ever references a networking library.

**Verdict:** fixed; the deviations that remain have end dates in [PSB.md §12](PSB.md#12-exceptions).

## Open items

| | |
|---|---|
| The phrase as a wipeable byte buffer end to end (see 2) | planned |
| Seal the plain-text history files (see 3) | by 2027-01-31 — exception E-1 |
| A Windows certificate a certificate authority vouches for | the maintainer's application — [CODE_SIGNING.md](CODE_SIGNING.md) |
| An external audit | not funded yet — [AUDIT_STATUS.md](../AUDIT_STATUS.md) |

Found something this review missed? Report it privately: [SECURITY.md](../SECURITY.md).
