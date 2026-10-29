# Phobia Wallet — unified roadmap

**Product version:** see [`VERSION`](../VERSION) (currently **4.10.0-beta.6**, shown as “Beta”; betas are not numbered for people).  
**Consolidation date:** 2026-09-15 · **last status pass:** 2026-10-29 (the Altumptic security baseline, class A — [PSB.md](PSB.md): signed checksum lists the updater requires, R.10; H.5 seedless watch-only wallets, N.15 explorer key of your own, P2.4 private broadcast, N.16 NEAR staking calls; before that 2026-10-16 — P1.3 panic password, P1.8 check of the written-down phrase, N.9 price limit on THORChain swaps and the THORChain nodes replaced after Nine Realms went away, N.15 token history on the EVM networks, M.2 marked done; before that 2026-10-10 — swap payments held to their quote and followed to the end, balances that keep up with a send, staking yield computed from the networks, history for Base / Arbitrum / Linea / BNB Chain / Polygon, and the hardware, watch-only, macOS, iOS and shielded-Zcash backlog written out in full — H.2, H.5–H.7, P2.6, N.13–N.16; before that 2026-10-07 — readiness audit: Tor circuit isolation made real, Monero service login, Orbot on Android, swap claims re-checked live; before that 2026-09-20: P0.0, P0.2–P0.4, P0.6–P0.8, P1.1, P1.2, P1.4–P1.7, P1.9–P1.13 and L.1/L.2/L.3/L.8 closed).  
**Purpose:** one document for “what remains to do” — compiled from README, CHANGELOG, `SECURE_ANON_ROADMAP`, `CLAUDE_IMPLEMENTATION_ROADMAP_UK`, `12-coins-and-chains`, `PRIVACY`, `THREAT_MODEL`, `security-model`, `BUILD_VERIFY`, and notes on alignment with the manifesto philosophy.

Legend: ✅ done · 🟡 partial · ⏳ next · 📅 planned · ❌ not planned / blocked.

Long technical implementation phases remain in [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).  
Privacy details — in [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md).  
Network matrix — in [`12-coins-and-chains.md`](12-coins-and-chains.md).  
How to verify yourself — [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md).

---

## 1. Primary rule

Fund safety, correct balances, and **UI honesty** matter more than coin count or screen count.  
Do not announce “ready / full / private” until the end-to-end scenario works and is covered by tests.

Philosophy ([`MANIFESTO.md`](../MANIFESTO.md)): the user must **verify**, not **trust**. A promise without a self-verification mechanism and without text stating “what is not protected” is marketing fog — even when the code is solid.

**Release gate (short):** green `dotnet test` + `Release` build · same version everywhere · Windows/Linux artifacts + `SHA256SUMS` · send-path has vectors · coin limitations visible in UI · fail-closed: network error ≠ zero balance.

**Philosophy coverage (audit orientation):** ~85% — gaps are specifically in communicating limits, fail-closed for balance/send, and “verify yourself.”

---

## 2. Already done (do not re-list as “todo”)

| Area | Status |
|---|---|
| Non-custodial vault (Argon2id + AES-256-GCM), BIP39, screenshot-guard seed | ✅ |
| Tor + kill-switch fail-closed, Verify Tor, custom SOCKS5 | ✅ |
| Per-network RPC/explorer selection, list of “who can see addresses” | ✅ (4.7) |
| Monero full wallet; BTC/LTC/BCH/DOGE HD scan + fresh receive | ✅ |
| ETH + L2 (Arbitrum/Base/Optimism/Linea/zkSync Era) send | ✅ |
| THORChain swaps, Security Center, Privacy Radar, coin control (BTC/LTC/DOGE) | ✅ (Radar exists; **limit explanations — see P1.11**) |
| Address poisoning / EIP-55 / reuse warnings | ✅ |
| Desktop Win/Linux, themes, 6 languages, encrypted backup | ✅ |
| SHA256SUMS on releases | ✅ |
| Monero 25-word seeds in all 12 Monero languages (Chinese included) → Monero-only wallet, optional scan-from | ✅ (4.10) |
| Monero service reachable (RPC bodies with a stated length) and restore over the account's whole life | ✅ (4.10) |
| Rebrand Umbrella → **Phobia**: vector crystal logos + icons, midnight-violet default theme, every theme adapted, Phobia file names (+ legacy copies) | ✅ (4.10) |
| Swap any coin for any coin (THORChain → NEAR Intents → Exolix, route named before paying), Nano sends, restorable wallet removal | ✅ (2026-10-02) — every coin but Polkadot, which no route trades on its own network (re-checked live 2026-10-07; DOT and TON removed from Exolix, which lists neither natively) |
| Tor circuits per purpose, actually on the wire (the label used to sit where .NET never reads it — every request shared one circuit); names always resolved by the proxy (`socks5h` accepted, `socks4` used as 4a). Per-server circuits measured and dropped — too slow over Tor for the balance scans | ✅ (2026-10-07) |
| Local Monero service behind a random login file (it ran with `--disable-rpc-login` on a fixed port — reachable by a web page's cross-site POST); leftover daemon ended, a squatted port refused | ✅ (2026-10-07) |
| Android: Tor through Orbot as the custom proxy, kill-switch kept across restarts while it is set | ✅ (2026-10-07) |
| Balance chart follows the pointer (real worth per point, 1% scale floor), Settings search in every language over every card, “Back to top”, notes that close, wallet total never narrowed by the asset filter | ✅ (2026-10-02) |
| Staking from the wallet: TRON (freeze, vote, claim, unfreeze, withdraw — TronGrid's bytes checked before signing), Solana (seed stake accounts: stake, undelegate, withdraw), Cosmos Hub (delegate, claim, undelegate); Connect rebuilt; unlock and refresh no longer block the UI | ✅ (2026-10-02) |

---

## 3. Priority queue (what to do next)

### P0 — fund safety and honesty (fail-closed)

| # | Task | Source | Status |
|---|---|---|---|
| **P0.0** | **Full restore proof (critical).** Test: new wallet → ≥20 receive addresses → funds to address **#15** → delete local state → restore **from seed only** + BIP44/gap limit → full balance found. Without state — full HD scan only. Without this, self-custody is fiction. | Audit vs MANIFESTO §6 | ✅ (`RestoreFromSeedProofTests`, BTC/LTC/BCH/DOGE: 20 issued, funds on #15, state deleted, rediscovered **and spent**; the past-gap limit is pinned too) |
| P0.1 | Close HD UTXO edge-case gaps (gap scan, change not lost in UI) — after / together with P0.0 | Claude §3 | ✅ The last §3 gaps: **history** was judged one address at a time, so change back to the wallet counted as "sent" (a 0.001 BTC payment showed as 0.00999) and a spend funded only by change never appeared — now every transaction is judged against the wallet's whole address set (receive + change, SegWit + Taproot) and the used change addresses are queried too, for BTC, LTC and BCH; the old behaviour is kept as a failing counter-example in the tests. **Previous addresses** on Receive now show what each holds from the last complete scan ("not checked yet" otherwise). Gap scan, partial-never-zero and change to a fresh internal address were already in place |
| P0.2 | Pin + SHA/PGP verification for **Tor** and **monero-wallet-rpc** in fetch scripts (fail-closed) | Claude §4.2, BUILD_VERIFY | ✅ the fetch scripts verify each project's SIGNED sums file (Tor `.asc`, Monero clearsigned) against a pinned key fingerprint before trusting the hash; release builds pass `-RequireSignature`; `scripts/check-pinned-binaries.sh` keeps the scripts and THIRD_PARTY_NOTICES from drifting (they had: the pinned Tor version was gone from the mirror) |
| P0.3 | CI: verify `SHA256SUMS` ↔ attached artifacts byte-for-byte | Claude §4.1 | ✅ the release job already self-verifies before publishing; `scripts/verify-published-release.sh` now checks what the page **serves afterwards**, including that no attached artifact is missing from the manifest |
| P0.4 | Single machine-readable **capability matrix** → UI + README + tests (no discrepancies) | Claude §5.1, coins doc | ✅ `SendableSymbols` is the one source the picker, the send guard, the coins doc, the README table and §4 below are all checked against (`CapabilityMatrixTests`) |
| P0.5 | zkSync Era **send** or honestly leave Receive-only with gas explanation (not “Ready”) | coins / CHANGELOG 4.7 | ✅ **Sends.** It was honestly receive-only while the wallet signed every transfer with a flat 21,000 gas; now the gas limit is the chain's own `eth_estimateGas` answer (+20%, never below the intrinsic 21,000), which is what zkSync needed all along — a live check reads 178,472 there against 21,000 on Ethereum. The same change makes every other EVM network price its own gas instead of assuming Ethereum's |
| **P0.6** | **Fail-closed balance.** If all RPC/nodes are unreachable (e.g. 3+ attempts) → UI: “Balance unavailable (network error)” + Refresh. **Do not** show cache as the current balance; cache only with an explicit “last synced …” / “unknown” label, never as a live `0.0000` caused by an error. Offline test mandatory. | MANIFESTO §4, audit | ✅ (`BalanceReadout`: unread → “—” + reason, cache marked “last known”, unread rows excluded from the total and counted out loud) |
| **P0.7** | **Send-path transport gate.** Before Send/Review, verify that the actual path (Tor / Clearnet / Custom) matches the user’s settings (+ kill-switch). Violation → **FAIL**, send blocked; no silent clearnet fallback. | MANIFESTO §3–4, audit | ✅ (`SendTransportGate` at Review **and** Confirm: Tor on-but-down, routed elsewhere, unapplied proxy or armed kill-switch with no proxy all refuse) |
| **P0.8** | **Network isolation CI.** Run in a sandbox / firewall without clearnet: with Tor-only enabled, the wallet process **must not** attempt clearnet connections. Without this, the kill-switch is not proven in production. | Audit | ✅ (`Category=Isolation` CI job: a loopback listener proves no socket is opened at all, with the kill-switch off as the counter-proof; plus a scan for any HttpClient built outside `PublicHttp`) |
| **H.2** | **Hardware wallets (Ledger/Trezor)** — sign on device; seed never on PC for spends. Interim: PSBT (**H.1** ✅). See [HARDWARE_WALLETS.md](HARDWARE_WALLETS.md). | Threat model Vector 1 | 🏆 **next** |

### P1 — privacy, trust, core UX (UI honesty)

| # | Task | Source | Status |
|---|---|---|---|
| P1.1 | **Duress / decoy password** (second password → decoy vault) | README, SECURE 4.4–4.5, MANIFESTO | ✅ the vault is now the two-slot deniable file for **every** wallet (so setting one does not change the file's shape); Settings → Security sets or removes a decoy, and never reports whether one exists |
| P1.2 | Restore/finish **hidden wallet** unlock UI (BIP39 passphrase), if the product promises it | SECURE 4.4 | ✅ the derivation, scan and spend paths already honoured a passphrase; the unlock screen had no field to type one into, so the feature existed and nobody could use it. Folded behind “Advanced”, and pinned by a test |
| P1.3 | **Panic / duress wipe** with an explicit trigger | SECURE 4.5 | ✅ 2026-10-16: a **panic password** in Settings → Security, beside the duress one. Typed on the unlock screen, it silently runs the same wipe as “Delete everything” and the app opens on its first-run screen; the recovery phrase restores everything. Deniable the same way: a salted PBKDF2 check in a file every install has, random bytes of the same size when none is set, never reported or logged. It cannot misfire on a wallet: refused if it opens any vault on the device (either slot, removed wallets included), and every password chosen later is refused if it is the panic password; checked before the unlock throttle. `PanicPasswordTests`, mutation-checked |
| P1.4 | Transaction **simulation** before Confirm (what exactly changes on-chain) | README Next | ✅ already shipped — the Send review renders `SendSimulation` rows (what leaves, what returns as change, what the fee costs) |
| P1.5 | One-switch private send — bring UX to “one toggle = full checklist” on all UTXO chains | README / CHANGELOG 4.7 | ✅ the three UTXO chain lists (balance scan, fee selector, coin control + private-send plan) had drifted and left Bitcoin Cash out of coin control and out of the linkage checklist; they are now one list |
| P1.6 | Connection status in primary UI: Tor / Direct / Custom / Offline | Claude §7 | ✅ a chip in the sidebar (and in the top/bottom nav) reading TOR / PROXY / DIRECT / BLOCKED from the same live state the send gate uses — amber when Tor is on in Settings but is not carrying the traffic |
| P1.7 | Mark screenshot-guard honestly as **Windows-only** until Linux exists | Claude §7 | ✅ already honest — the Security Center says capture blocking is a Windows feature and is unavailable elsewhere, and does not score it |
| P1.8 | Verify backup (without revealing seed) + guided restore dry-run | Claude §6.5 | ✅ 2026-10-16: besides the encrypted backup file’s check, Settings → Backup now checks **the phrase written on paper** — the dry run of a phrase restore: type it, and the wallet says “restores this wallet — all N words match”, a word count that differs, or which positions do not match. The phrase is never shown; what was typed is cleared once compared and on lock. Numbered, comma-separated and mixed-case copies are read as people make them (`PhraseCheckTests`) |
| P1.9 | Address book: local, with format-check, confirm on first send | Claude §6.3 | ✅ already shipped — local encrypted book on the Send screen, wrong-network shape check, and a first-time-to-this-address confirmation (`SendSafetyIntegrationTests`) |
| P1.10 | Consolidate Activity into one screen + filters + honest partial-history labels | Claude §6.4 | ✅ one screen with filters, a last-synced line, and a note naming the held coins whose history this build does not read — computed from the capability catalog, so it cannot claim coverage the code lacks |
| **P1.11** | **Privacy Radar — limits under every status.** Mandatory text: *what is protected* and *what is not* (e.g. “Tor hides your IP, but the selected explorer sees your addresses”). Difference: “Tor works” ≠ “IP is hidden from whoever already received your address.” Without this, Radar violates MANIFESTO §1–2. | MANIFESTO, audit | ✅ every Privacy Radar finding renders its limit underneath (`PrivacyScoreFinding.LimitCode`), so no status can appear as a bare reassurance |
| **P1.12** | **“What leaked?” after send.** Short report: IP hidden yes/no · addresses seen by Node X · broadcast via Tor/Direct · coin control / fresh change on or off. The user sees the privacy cost of that operation. | MANIFESTO §1–2, audit | ✅ `SendLeakReport` renders under the send result: IP, kill-switch, ledger, input linkage **with the count**, change freshness, coin control, and the one nobody can fix (the recipient knows) |
| **P1.13** | **Duress test scenario.** QA scenario: “inspector coerces” → decoy vault opens, real funds remain inaccessible with the decoy password. Without this, P1.1–P1.3 are features, not verified solutions. | MANIFESTO intro, audit | ✅ `DuressWalletScenarioTests` — coercion opens the decoy, the real phrase stays unreachable, the two wallets share no address, the file's size is identical with and without a decoy, and removal is as deniable as adding |
| P1.21 | Password again before every send; one-click recommended protection in the Security Center | user, 2026-10 | ✅ on by default; the vault is opened with the typed password and must give back this wallet's phrase (a duress password does not confirm a send from the real wallet); scored in the Security Center |
| **P1.20** | **Self-verify mode** (long, but required by philosophy). CLI or Debug panel: verify no clearnet in wallet connections; export xpub → balance in a third-party scanner; Tor via `curl --socks5-hostname`; cross-check with `VERIFY_YOUR_WALLET.md`. | MANIFESTO, audit | ✅ watch-only account xpub export in Settings → Security (pinned by a test that the addresses it yields are the wallet's own, external **and** change), and [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) written in full: balance from a third party, live route, download, build, counterparties — and what none of it proves |

### P2 — on-chain privacy “heavy artillery”

| # | Task | Source | Status |
|---|---|---|---|
| P2.1 | **Taproot (BIP-341/86)** — scanner `m/86'` + key-path spend, not derivation only | PRIVACY, coins | ✅ **Find, show, spend.** Addresses pinned to all three BIP-86 vectors; BTC scans `m/86'` beside `m/84'` (external + internal, same gap/partial rules); key-path inputs signed and consensus-verified, alone or mixed with SegWit; change returns to the inputs' own branch on its own index counter; restored Taproot history is read. **Receive stays BIP-84** — the wallet does not hand out `bc1p…` addresses, so nothing is issued that the old scan could miss. Cost, stated: an empty BTC scan now probes 80 addresses instead of 40. Verify a first spend with a small amount |
| P2.2 | **PayJoin (BIP-78)** | PRIVACY, threat model | ✅ **Sender side.** A pasted `bitcoin:` link with `pj=` (HTTPS or `.onion` only) is used: the signed original goes to the receiver on its own Tor circuit, and its proposal is signed only after the full BIP-78 sender checklist plus an independent "your cost did not rise beyond the stated offer" check; output substitution is always refused. On any failure the reviewed payment is broadcast instead, and if even that fails the wallet says the receiver holds a signed copy rather than offering a retry that could pay twice. Every checklist rule has its own adversarial test against a simulated receiver with real keys. **Not done:** receiving a PayJoin (needs an always-reachable endpoint), and no send against a live receiver yet — make the first one small |
| P2.3 | **CoinJoin** (after PayJoin) | PRIVACY | 📅 |
| P2.4 | **Dandelion++** (broadcast timing privacy) | coins roadmap | 🟡 2026-10-28: **private broadcast**, the part a wallet can do alone. With Tor or a proxy on, a BTC / LTC / DOGE payment is first handed to one random node of its network over its own Tor circuit; explorers are only asked afterwards whether the network carried it (same signed bytes, one txid). On by default, in Settings → Privacy. Dandelion++ itself (BIP-156) was never adopted by Bitcoin Core: no node runs its stem phase, so it cannot be done from the wallet |
| P2.5 | **Silent Payments** (BTC) | coins long-term | 📅 |
| P2.6 | Zcash **shielded** (z-addresses, Sapling / Orchard): receive, balance, send | coins / CHANGELOG | 📅 **Not a switch on the existing send.** A transparent send is a Bitcoin-shaped signature, which is why N.10 could be written and pinned here. A shielded one needs (1) ZIP-32 key derivation for Sapling and Orchard; (2) scanning every block's compact outputs with the viewing key to find the wallet's own notes — through a light-wallet server (`lightwalletd`) or a full node, because no public explorer can see them; (3) the note-commitment tree and its witnesses kept in step with the chain; (4) zero-knowledge proofs (Groth16 for Sapling, Halo 2 for Orchard) built on the device. None of that exists in .NET: every wallet that does it links the Zcash project's Rust libraries (`librustzcash`, `zcash_client_backend`). Plan: bundle those as a native library per platform behind a narrow interface, fetched and verified the way Tor and Monero are (P0.2); unified addresses (ZIP-316); receive + balance first, send once the published test vectors and a testnet round trip pass. Until then the wallet says “transparent only” wherever a ZEC address is shown |

### P2 — tokens and networks (full cycle or do not add)

Rule: new network/token = derive + validate + balance + send + fee + history/status + tests + matrix row.

| # | Task | Status |
|---|---|---|
| N.1 | Send **any ERC-20** (not only native ETH / separate USDT) | ✅ the picker lists every held ERC-20 and routes on the **contract**, never the ticker; the amount is scaled by the decimals that contract reports (a row without them is refused, not assumed to be 18), the token balance is read from the contract at quote time rather than from a cached row, and the review says the fee comes out of ETH. Verify a first send with a small amount |
| N.2 | Send **any TRC-20** (not only USDT) | ✅ the same shape as N.1 — routed by contract, scaled by the decimals the contract reports, balance read from the contract with `triggerconstantcontract`. USDT is now just the TRC-20 whose contract was already known, and it gained the exact-scaling refusal the old path lacked (it multiplied through `Math.Pow` and truncated). Fee in TRX, said before Confirm |
| N.3 | Send **any SPL** / full Jetton send (Jetton balances already exist) | ✅ **Jetton send done** — TEP-74 body pinned cell-hash-for-cell-hash against `@ton/core`, message addressed to the sender's own jetton wallet with TON attached for gas, and a row whose jetton-wallet address is unknown still says “Receive only”. 🟡 **SPL balances done** — `getTokenAccountsByOwner` under both the Token and Token-2022 programs (PayPal USD is on the latter), summed per mint, decimals from the chain; eleven major mints named after an on-chain check of each, every other mint shown by address as unverified and folded with suspected spam; a failed read keeps the previous rows instead of dropping real holdings. ✅ **SPL send done** — TransferChecked between associated token accounts (derivation pinned to the Solana SDK's program-address vectors and to mainnet's own accounts), with an idempotent create of the recipient's account when it has none, whose rent the review states; mint program and decimals read from the chain; a token account or program address as the destination is refused; a mainnet validator runs the whole transaction in a live check. ✅ **Token-2022 too** — the account is derived with the mint's own program, and a mint is sent only when its extensions cannot change what a plain transfer does; a transfer fee, a transfer hook, a paused or non-transferable token, accounts that start frozen or a scaled display amount are refused in plain words, and a row stays "Receive only" for them. A token whose issuer can freeze or seize it (PayPal USD can) says so on the review. Checked live: a mainnet validator runs a PYUSD transfer that also creates the recipient's account |
| N.4 | **XRP** | ✅ **Receive, balance and send.** `m/44'/144'/0'/0/0`, the path Xaman, Ledger and Trust use — the key pinned to xrpl.js's own `fromMnemonic` test, the encoding to XRPL's documented example. Balance from `account_info` at the last *validated* ledger through a user-choosable server (XRPL Labs cluster, or Ripple's); an address the ledger does not have yet is a real zero, anything else is unknown. ✅ **Send:** a plain XRP Payment with an optional destination tag, byte-for-byte what xrpl.js signs (its own wallet tests); sequence from the open ledger, reserve and fee from the server; a destination that requires a tag, refuses XRP or only takes approved senders is stopped before signing, and a new one needs the base reserve; spendable leaves the account's reserve behind; submitted once with `fail_hard`, final only in a validated ledger, and an unclear answer settled by the payment's last ledger — never offered as a retry |
| N.5 | **Stellar (XLM)** | ✅ **Receive, balance and send.** SEP-0005 (SLIP-0010 ed25519, `m/44'/148'/0'`) pinned to the SEP's own three vectors; StrKey `G…` with its CRC16 checked, so a typo'd Stellar address is refused on paste. Balance from Horizon through a choosable server (SDF, LOBSTR); a 404 is an unfunded address — a real zero — anything else is unknown. ✅ **Send:** one native Payment, or CreateAccount for an address that is not an account yet, with a text or ID memo and a five-minute time window — byte-for-byte what the Stellar Go SDK builds (its `TestPayment` / `TestCreateAccount` envelopes). Spendable leaves the reserve (2 + subentries + sponsoring − sponsored) × 0.5 XLM and offer liabilities behind; the fee bid follows `/fee_stats`, capped at 0.001 XLM. Submitted once: an unclear answer is checked against Horizon by hash and never offered as a retry. Muxed `M…` addresses are refused for now |
| N.6 | **Cosmos (ATOM)** / IBC | ✅ **Receive, available balance and send.** `m/44'/118'/0'/0/0` pinned to cosmjs's own wallet test (key and address); the address checksum is verified by the shared BIP-173 codec, so a typo'd or wrong-prefix (e.g. `osmo1…`) address is refused. Balance: the bank module's uatom through a choosable server (PublicNode, Keplr) — **staked ATOM is not counted**, and the chain's note says so. ✅ **Send:** one bank MsgSend with an optional memo, byte-for-byte what cosmjs signs (its own three signing vectors); the node must say it is on `cosmoshub-4`; account number and sequence from the auth module, gas from the node's own simulation, fee from the fee market with room to move; a timeout height 50 blocks out settles an unclear broadcast, which is never offered as a retry. **IBC off**. ✅ **History (2026-10-08)** from an archive node (see the matrix) |
| N.7 | **NEAR** | ✅ **Receive, balance and send.** SLIP-0010 ed25519 `m/44'/397'/0'` pinned to near-seed-phrase's own parse test; the address is the implicit account (hex of the key), so nothing needs registering to receive. Balance: `view_account` at final finality through a choosable RPC (NEAR Foundation, FastNEAR — Lava's endpoint was found discontinued); `UNKNOWN_ACCOUNT` is a real zero; yocto amounts beyond `decimal` are split with BigInteger so a large holder never reads "unknown". Named `.near` accounts and staked NEAR are not shown. ✅ **Send:** one Transfer from the implicit account, byte-for-byte what near-api-js builds (its own signed-transfer test); nonce and block hash from `view_access_key`; a named destination must exist, a new implicit one needs ≥ 0.002 NEAR for its storage, a non-existent `0x…` one is refused; spendable leaves the account's storage behind; submitted once, an unclear answer checked by hash and never offered as a retry |
| N.8 | **Polkadot (DOT)** | ✅ **Receive, balance and send.** substrate-bip39 (PBKDF2 over the phrase's *entropy*) → schnorrkel sr25519 expansion → ristretto255 public key → SS58 prefix 0 — the scheme of Polkadot.js, Talisman, SubWallet and Nova, not BIP-44. Ristretto255 written from RFC 9496 and checked against its 16 published encodings; the whole pipeline against `subkey`'s documented output (phrase → mini secret → key → address); SS58 against Polkadot.js's encode tests. Balance straight from chain storage (`System.Account`, SCALE) at the finalized head of **Asset Hub and the relay chain, added** — checked live: since the 2025 migration the treasury holds 24.3 M DOT on Asset Hub vs 2.7 k on the relay, so a relay-only reader would show most people ~0. Either read failing makes the balance unknown. Both chains' servers are choosable. ✅ **Send:** `Balances.transfer_keep_alive` from Asset Hub as a signed v4 extrinsic. The pallet and call indices, the argument types and the whole transaction-extension list come from the **running runtime's metadata** (parsed in full, V14) and must match what the wallet knows how to fill in, or it refuses to sign — the check caught `EthSetOrigin` on its first live run. sr25519 signing is Merlin/STROBE/Keccak written from the reference code, pinned to the Go port's fixed transcripts and to signatures Polkadot.js made (and one it must reject); every signature is verified before use. Before broadcast the node validates the signed transfer (`TaggedTransactionQueue_validate_transaction`) — in a live check it accepted our signature and encoding and stopped only at "cannot pay" for an empty key. Existential deposit, nonce and fee from the chain; submitted once; followed through finalized blocks, success read from the block's events; a 64-block era settles an unclear outcome, never offered as a retry |
| N.9 | THORChain expansion / swap reliability (expiry, slippage, refund, failed broadcast) | 🟡 **Paid as quoted, followed to the end** (2026-10-10). A swap's payment cannot be made for another amount than its quote — a smaller one is held by the route until its deadline and only then refunded, which is what a live test ran into; “Max” on the Swap screen leaves what the payment's own fee needs; a quote for more than can be paid is refused before any route is asked. A paid swap is kept on the device and followed — every few seconds at first, then every half minute — across restarts and locks; its Activity row stays *pending* until the other coin is delivered, then says *confirmed*, *refunded* or *failed*, and a short payment is announced with what arrived, what was quoted and when the refund starts where the route reports them (NEAR Intents does). **2026-10-16 — a price limit.** THORChain quotes were taken without one, so the memo carried no limit and a swap filled at whatever the pool gave when the deposit was seen; they are now asked with a 3% tolerance, a fill below it is refunded instead, and the quote shows “At least …” (THORChain’s limit, NEAR Intents’ minimum). Checked live, memos still fit an 80-byte OP_RETURN. The same day every THORNode the wallet asked was found gone (Nine Realms no longer resolves, Liquify’s old host fails TLS, cosmos.directory answers 503) along with the swap tracker: now Liquify’s gateway, THORWallet’s node, then cosmos.directory, and links to thorchain.net; `ThorchainQuoteLiveTests` asks every node for a quote with a limit. Next: the refund detail for THORChain and the fixed-rate routes, a retry for a failed broadcast |
| N.10 | **Zcash (ZEC) transparent send** | ✅ **Receive, balance and send — the last chain in the catalogue that could take money and not move it.** ⚠ 2026-10-14: the upgrade table lacked NU6.3 (block 3,428,143, July 2026), so sends signed since then were refused by the network — nothing spent; fixed, and the live guard reads Zebra now instead of the deprecated zcashd. ⚠ 2026-10-29: that guard caught the next one eight days ahead — Zebra scheduled **NU7** for block 3,543,405 (about 6 November 2026), and NU7 accepts only v5 or v6 (ZIP-2003). Sends are now **v5 transactions (ZIP-225) signed with the ZIP-244 digest**, valid since NU5 and under NU7, pinned to all ten of Zcash's ZIP-244 vectors and to a v5 transaction mainnet accepted (rebuilt byte for byte, its six signatures verified); NU7's branch id is in the table, and a node decodes the new form. Until then it was a v4 (Sapling) transparent transaction signed with the **ZIP-243** digest: BLAKE2b-256 personalised with the consensus branch id in force at the height it expires under, so the signature cannot be replayed onto another Zcash fork. The digest is pinned to **Zcash's own `sighash.json`** — 25 of its vectors, every hash type including ANYONECANPAY/NONE/SINGLE, with the shielded digests supplied — after the first run caught that Zcash prints a uint256 reversed. v4 was confirmed still valid by sampling the live chain: recent blocks carry v4 beside v5 and v6, and a mainnet node decodes a transaction built exactly as a send is and stops only at the invented coin it spends (a live check that spends nothing). It is signed for the upgrade in force at the next block and expires before the one after, as zcashd does, so it can never outlive the rules it was signed under. Addresses are decoded by their **two version bytes**, not the `t1`/`t3` letters, so another chain's Base58Check address is refused by name and a shielded `z…`/`u1…` one is told plainly that this wallet holds transparent Zcash only. The fee is **ZIP-317**'s conventional fee, computed together with coin selection because it depends on how many coins fund the spend; change below Zcash's own 54-zatoshi dust rule goes to the fee rather than becoming an output no node would accept. Unconfirmed coins are never spent. The signing key is checked against the reviewed address before anything is signed. The transaction id is computed **before** broadcast, so an answer that never arrives is settled against the chain and shown as Pending — never as a retry that would pay twice |
| N.11 | **Nano (XNO) receive, balance and send** | ✅ **Receive and balance.** SLIP-0010 at `m/44'/165'/0'` (the path Ledger, Trust Wallet and Nault's BIP39 mode use), then Nano's ed25519 with BLAKE2b-512 in place of SHA-512 — pinned stage by stage (seed → private key → public key → address) to the Nano documentation's own test vector. Addresses decoded with their 40-bit BLAKE2b checksum, `xrb_` accepted as the same account. Balance from a choosable node RPC (Nano.to, NanoSLO, SomeNano) with failover; raw amounts (10^30 per XNO, beyond `decimal`) scaled as big integers; the balance shown is pocketed + receivable, because receivable XNO is the account's money. ✅ **Send (Beta 1):** a send first pockets the receivable payments it needs (receive blocks), then publishes a state send block — hash pinned to a mainnet block, signature to the Nano documentation's, proof of work computed locally and checked against BLAKE2b (`NanoBlockTests`). Verify a first send with a small amount |
| N.12 | **Decred (DCR) receive, balance and send** | ✅ **Receive and balance.** BIP44 `m/44'/42'/0'/0/0` — the account Trust Wallet, Ledger and Exodus derive (Decrediton uses its own seed scheme and will not show it; the chain note says so). A Bitcoin-shaped address with Decred's own hashing: RIPEMD-160 of **BLAKE-256** for the key hash and double BLAKE-256 for the checksum. BLAKE-256 (the SHA-3 finalist, 14 rounds — not BLAKE2) is written from the specification and pinned to its published digests; addresses to dcrd's own `stdaddr` vectors and to Trust Wallet Core's phrase → address vectors (with and without a passphrase); P2SH `Dc…` recognised. Balance from dcrdata's address totals (unspent DCR), choosable; an error is unknown, never 0. ✅ **Send (2026-10-08):** Decred's transaction format (prefix + witness, id = BLAKE-256 of the prefix) and signature hash written from dcrd's `wire` and `txscript` and checked against mainnet: the signatures of a one-input and a nine-input transaction verify against the hashes computed here (`DecredTransactionTests`). Each input repeats its coin's amount, block height and position (dcrd's fraud proof), read from the coin's own transaction and cross-checked; a payment signed with a stranger's key is refused by mainnet at the signature and nowhere earlier (`DecredSendLiveTests`). Coins and broadcast through dcrdata, with Bison Explorer (a community dcrdata) behind it |
| N.13 | **History depth** | ✅ 2026-10-10: every source is asked for what it hands out in one request — TRON 200, Stellar 200, Ethereum-family explorers / TON / XRP / Polkadot 100, Cosmos and BlockCypher 50 (they were 25–30) — no extra calls, no more addresses told to anybody. What is read is kept on the device and added to at every read (the beta of 10 October). Next: “load older” where a source has a cursor |
| N.14 | **History on Base, Arbitrum, Linea, BNB Chain, Polygon** | ✅ 2026-10-10: read from 3xpl's keyless API — one request a network, a thousand balance changes back, only while the wallet holds something there; requests to it are queued a second apart, because it refuses bursts. Rows carry the amount and the direction; this source does not name the other side. Ethereum itself falls back to it when Blockscout does not answer. ❌ **Fantom, Cronos**: no source answers without a key (checked 2026-10-10) — the Activity screen names them as gaps |
| N.15 | **Token history on the EVM side networks** (USDT / USDC on Base, Arbitrum, BNB Chain …), and an **optional explorer key of your own** — a free Etherscan-family key entered in Settings, used for history only — to cover Fantom and Cronos and to name the other side of a transfer | 🟡 2026-10-16: **token history done** for the tokens Holdings shows — Ethereum (Blockscout, 3xpl as fallback), Optimism (its own Blockscout), Avalanche (Routescan), Arbitrum One, Base and Polygon (3xpl’s ERC-20 module) — their contracts only, so an airdropped look-alike never becomes a row, and the wallet’s own decimals rather than the answer’s. BNB Chain has no keyless source (3xpl keeps no BEP-20 module, BscScan wants a key): its tokens are named on the Activity screen as not read. Checked live (`EvmTokenHistoryLiveTests`); Base answered nothing that day because 3xpl’s Base module was returning 500. ✅ 2026-10-28: **the explorer key of your own** — a free Etherscan V2 key in Settings → Privacy, checked before it is kept, sealed per wallet, used only where no keyless source answers (native history on Fantom and Cronos, BNB Chain tokens). Its live test runs only when a key is provided; the project has none to test with |
| N.16 | **Staking on more networks.** Today TRON (votes), Solana (stake accounts) and Cosmos Hub (delegation) are real: positions read from the chain, validators listed, every action signed on the device, and — since 2026-10-10 — the yearly yield computed from the network's own figures instead of a typed-in “~6–7%”. The other networks on the Staking screen are information only, and say so | 🟡 2026-10-28: **NEAR, the network half** — the four staking-pool calls, pinned to three real mainnet transactions, and a client that reads pools, positions and yield; not on the Staking screen yet. Then, in this order: Cardano delegation, Polkadot nomination pools, TON nominator pools; ETH only through a pool the user picks, never a default. Each ships only after a real-funds check |

### P2 — hardware, platforms, release trust

| # | Task | Status |
|---|---|---|
| H.1 | Bitcoin **PSBT** export/import + watch-only xpub | ✅ **PSBT both ways.** Export: the reviewed payment as an unsigned PSBT naming the master fingerprint and every BIP32 path — BIP-371 Taproot fields filled by hand, because NBitcoin's `AddKeyPath` leaves a Taproot coin unnamed — with the change index reserved as for a real send. Import (base64, hex or `.psbt`): reviewed line by line with the cost to this wallet, and only coins the wallet's **own scan** found are signed, at the value read from the chain; a PSBT that misstates one of them, or spends from one of its addresses a coin the scan cannot see, is refused. Completed PSBTs broadcast through the same route gate. The xpub export now includes the Taproot account the balance counts. **Not done:** a seedless watch-only wallet (import an xpub, sign elsewhere) — the app is built around an unlocked seed, and that mode is the same work H.2 needs, so they go together. Signing needs a synced wallet: this is not an air-gapped signer |
| **H.2** | **Ledger / Trezor** — sign on the device, no seed in Phobia | 🏆 **P0 next** — design in [HARDWARE_WALLETS.md](HARDWARE_WALLETS.md); H.1 PSBT is the interim path. Order: (1) **H.5** seedless watch-only wallets, which the device flow stands on; (2) Bitcoin through the devices' own protocols — Ledger over HID / APDU (the Bitcoin app's PSBT flow and wallet-policy registration), Trezor through its bridge — with the receive address confirmed **on the device's screen**; (3) the Ethereum family (EIP-1559 transactions, EIP-712 clear signing); (4) the other coins as each device's app allows. Needs the physical devices to test on: nothing ships on emulator evidence alone |
| H.3 | **Multisig** 2-of-3 | 📅 long |
| H.4 | **Android** (separate mobile threat model + UX, not a desktop copy) | 🟡 **Beta APK** — the same view models, signing code and pages in a phone layout (`Views/MobileShell`), built in CI and attached to the release with its checksum and attestation. Seed and key screens are FLAG_SECURE, Android backup is off. Tor through **Orbot** (custom proxy `127.0.0.1:9050`, kill-switch kept across restarts — beta.3). **Signed with **one stable key** from Beta 3 (fingerprint in [BUILD_VERIFY.md](BUILD_VERIFY.md#b-signature); Beta 1 → Beta 3 needs one reinstall, later updates install over). **Tor and monero-wallet-rpc in the APK** from Beta 3 (the projects' own Android builds, pinned to their signed sums, run from the native-library folder; Tor restarted when Android stops it in the background; an emulator job proves Tor bootstraps). The app looks for updates and opens the new APK's download. **Not yet:** Play Store / F-Droid |
| **H.5** | **Seedless watch-only wallets** — a wallet made from an xpub, a descriptor or a public address, with no phrase on the machine: balances, history, fresh receive addresses, unsigned PSBT export, and nothing that can sign | ✅ 2026-10-28, in the next Beta: a wallet from descriptors (BIP-380 checksum checked), SLIP-132 keys or addresses, with no phrase on the machine; the description sealed beside a vault under the same password (duress and panic included); the same gap scan a seed restore gets; payments as unsigned PSBTs to sign elsewhere; all 14 signing commands refuse it by type. A bare `xpub` is refused rather than guessed |
| **H.6** | **macOS build** (Apple silicon and Intel) | 📅 The code is Avalonia / .NET 8 and already runs on Windows and Linux; the Windows-only parts (the screenshot guard, the installer) already switch themselves off or have a Linux counterpart. Missing is everything around the code: a `.app` bundle and `.dmg`; Tor and `monero-wallet-rpc` for macOS, fetched and verified like the others (P0.2); **Developer ID signing and notarisation** — a paid Apple developer account (US$99 a year), without which Gatekeeper refuses the app; a `macos` job in `release.yml` with the same checksum, attestation and SBOM steps; and a Mac to test on. Not offered as a download until it is signed, notarised and has passed the network-isolation check (P0.8) there |
| **H.7** | **iOS build** | 📅 **after macOS, and after Android is out of beta.** More than a port: iOS does not let an app start another program, so the bundled `tor` and `monero-wallet-rpc` processes cannot exist there — Tor has to be linked in as a library and Monero used through its wallet library, and the kill-switch proven again on that stack (P0.8). Then the Avalonia iOS head with the phone layout Android already uses, the vault key in the Keychain / Secure Enclave, TestFlight before any listing. The App Store's rules for wallets (§9: an organisation account, licensing questions for swaps in some countries) decide whether swaps ship there at all |
| R.1 | **Reproducible builds** + published attestations (honestly: .NET single-file installer is not bit-identical) | ✅ the managed assemblies — the code that derives keys, signs and routes — are byte-identical across builds (CI `reproducible-build`); attestations ✅ (R.3). The apphost, installer and APK are not bit-identical; [BUILD_VERIFY.md](BUILD_VERIFY.md#what-is-and-is-not-bit-for-bit-reproducible) says which files to compare |
| R.2 | **Code signing OV/EV (SmartScreen)** | 🟡 **the pipeline is done** (2026-10-07): `release.yml` signs `Phobia.exe`, the portable exe and the installer (Authenticode SHA-256, RFC 3161 timestamp) and refuses a signature from any other certificate. The certificate is the author's own, self-signed — it proves the file is untouched, not who the publisher is, so SmartScreen still warns. **Next:** a CA-issued certificate for an individual open-source developer (no legal entity needed), swapped in through the same two secrets; EV/Store later (R.6) |
| R.3 | Sign releases with **GPG / Sigstore** | ✅ since 4.8.0 — every artifact **and** the sums file carry a keyless build attestation (GitHub OIDC → public transparency log), verified with `gh attestation verify`. Earlier releases have checksums only, and the docs say so. From the next Beta also a detached signature over the sums file (R.10) |
| R.4 | SBOM / provenance as a release asset | ✅ since 2026-10-07: `PhobiaWallet-<label>-sbom.spdx.json` (SPDX, from GitHub's dependency graph) on every release, in the sums file and the attestation; provenance is R.3 |
| R.5 | External **security audit** — status in [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | 📅 Planned |
| **R.6** | **EV Code Signing** (~$500–800/year) or OV for Windows build — without this SmartScreen / Store block unsigned exe | ⏳ |
| **R.7** | **Microsoft Store** (optional): separate Store build / Microsoft signature | 📅 |
| R.8 | **Immutable releases** | ✅ 2026-10-07: the tags force-moved on 2026-10-03 are back on their original commits; a repository ruleset forbids moving or deleting `v*` tags; `release.yml` refuses to build into a release that already has files |
| R.9 | **Build supply chain** | ✅ 2026-10-07: every GitHub Action pinned to a commit SHA (Dependabot updates them); OpenSSF Scorecard weekly; bundled Tor and Monero checked against their signed sums on every PR |
| R.10 | **Updates the wallet can attribute** (Altumptic PSB §3.1, §5.2) | ✅ 2026-10-29, from the next Beta: the release workflow signs `SHA256SUMS-<label>.txt` (RSA-4096, the Windows key; public key in [`keys/`](../keys/README.md)) and the updater refuses a release whose list its pinned key does not verify, before downloading; on Windows the program must also carry the pinned Authenticode certificate. Closes PHB-2026-10 |
| R.11 | **Altumptic Product Security Baseline, class A** | ✅ self-assessed 2026-10-29 — [PSB.md](PSB.md). Open exceptions, each with an end date: E-1 plain-text history files (2027-01-31), E-2 no security email (2026-12-31), E-3 `security.txt` not on a website (2026-12-31), E-4 MD5 in the Monero daemon's Digest login (review 2027-04-30) |
| R.12 | **Seal the history files** (PSB exception E-1) — activity log, kept history, swaps, cached balances, watched addresses, wallet index: sealed under a key from the vault, read after unlock | 📅 **by 2027-01-31** |

### P2 / Legal — store compliance (see §9 + root legal files)

| # | Task | Status |
|---|---|---|
| **L.0** | Legal docs in repo: TOS, Privacy Policy, APP_STORE_NOTES, GEO, CONTACT, TRADEMARK, CoC, LICENSE, LEGAL/, SECURITY/ | ✅ docs (2026-09-15) |
| **L.1** | **Apple / first-run:** screen — “we do not store keys; you are responsible for backup” | ✅ code (first-run page, gates create/import/unlock) |
| **L.2** | **No “Fully Private” claims** in UI/listing — follow APP_STORE_NOTES | ✅ UI strings audited in all 6 languages; a test refuses absolute claims |
| **L.3** | **Age gate 18+** on first launch | ✅ code (separate tick) |
| **L.4** | **Google Play / store copy:** “Not a financial service…” | ⏳ at submission |
| **L.5** | **Jurisdictional blocklist** — policy in GEO_BLOCKING.md | 📅 enforce |
| **L.6** | **Linux package signing** (PGP for Flatpak/Snap / distro repos) | ⏳ |
| **L.7** | **Geo-blocking** in store builds (IP/locale) after legal consultation | 📅 |
| **L.8** | **ToS / Privacy Policy acceptance** on first launch | ✅ code (versioned acceptance; changed wording asks again) |

### P3 — product / maintainability

| # | Task | Status |
|---|---|---|
| M.1 | Split `MainWindow` / `MainViewModel` into feature views (one screen each) | 🟡 partial classes already exist |
| M.2 | UI automation smoke: create → unlock → receive → send validate → backup → lock | ✅ `WalletJourneySmokeTests` drives the whole journey through the commands the buttons are bound to, and `Umbrella.Wallet.UiTests` renders every desktop section and the phone layout headlessly and fails on a screen that draws a type name instead of words |
| M.3 | Design system + WCAG AA on critical screens | ⏳ |
| M.4 | Price alerts | 📅 |
| M.5 | More exchange connectors (read-only) on request | 📅 |
| M.6 | NFT / Staking — **do not return** to primary nav without a full model + spam policy | ❌ / later |
| **M.7** | **Documentation audit (annual):** independent review of README, PRIVACY, THREAT_MODEL, MANIFESTO, and this ROADMAP for consistency with the code. Documents age faster than code. | Audit | 📅 recurring |

---

## 4. Coin matrix (summary)

| Symbol | Receive | Balance | Send | History | Note |
|---|:---:|:---:|:---:|:---:|---|
| BTC | ✅ | ✅ | ✅ | ✅ | coin control; restored Taproot (`m/86'`) found and spent — receive stays SegWit. Esplora instances, then Bitcore, the healthiest first (2026-10-08); a chosen server alone |
| LTC | ✅ | ✅ | ✅ | ✅ | litecoinspace, Bitcore and BlockCypher, the healthiest first; fee and broadcast through BlockCypher; history BlockCypher (2026-10-08). A chosen server alone |
| BCH | ✅ | ✅ | ✅ | ✅ | HD scan since 4.7 |
| DOGE | ✅ | ✅ | ✅ | ✅ | HD scan since 4.7; Bitcore then BlockCypher (4.10). History (2026-10-02): BlockCypher across the used addresses, netted per transaction |
| ETH | ✅ | ✅ | ✅ | ✅ | any held ERC-20 (N.1); history from Blockscout, and from 3xpl when Blockscout does not answer (2026-10-10) |
| OP | ✅ | ✅ | ✅ | ✅ | history from Blockscout's Optimism instance while the wallet holds ETH there (2026-10-08) |
| Arb / Base / Linea | ✅ | ✅ | ✅ | ✅ | history (2026-10-10) from 3xpl while the wallet holds ETH there: the amount and direction of every native transfer; this source does not name the other side. Blockscout's instances for them sit behind a Cloudflare challenge and the Etherscan family needs a key |
| zkSync Era | ✅ | ✅ | ✅ | ✅ | gas from the chain's own estimate; history from Blockscout's zkSync instance while the wallet holds ETH there (2026-10-08) |
| TRX | ✅ | ✅ | ✅ | ✅ | any held TRC-20, USDT included (N.2) |
| SOL | ✅ | ✅ | ✅ | ✅ | any SPL token, both token programs (Token-2022 when its extensions allow) |
| TON | ✅ | ✅ | ✅ | ✅ | jettons: balance ✅, send ✅ (N.3) |
| ADA | ✅ | ✅ | ✅ | ✅ | |
| XMR | ✅ | ✅ | ✅ | ✅ | full private |
| AVAX | ✅ | ✅ | ✅ | ✅ | EVM family; history from Routescan while the wallet holds AVAX (2026-10-08) |
| BNB / MATIC | ✅ | ✅ | ✅ | ✅ | EVM family; history (2026-10-10) from 3xpl while the wallet holds the coin, as for the rollups above |
| FTM / CRO | ✅ | ✅ | ✅ | ❌ | EVM family; no history source answers without an API key (3xpl has nothing for Fantom and does not know Cronos — checked 2026-10-10); the wallet's own sends still show. N.15 |
| ZEC | ✅ | ✅ | ✅ | ✅ | receive, balance and transparent send (N.10) — v5, ZIP-244 digest (v4 until 2026-10-29; NU7 refuses it), ZIP-317 fee. History (2026-10-02): Blockchair, then 3xpl |
| XRP | ✅ | ✅ | ✅ | ✅ | receive, balance and send (N.4) — a plain Payment + destination tag, pinned to xrpl.js; history shows the amount DELIVERED, never a partial payment's claimed Amount |
| XLM | ✅ | ✅ | ✅ | ✅ | receive, balance and send (N.5) — Payment / CreateAccount + memo, pinned to the Stellar Go SDK |
| ATOM | ✅ | ✅ | ✅ | ✅ | receive, available balance and send (N.6) — MsgSend + memo, pinned to cosmjs; staked ATOM not counted. History (2026-10-08) from CryptoCrew's archive node, whose index runs from the chain's start: ordinary public servers prune it (one answer in nine for an account with known sends); bank sends, multi-sends and IBC transfers out; mass micro-airdrops with a "claim" memo (address poisoning) left out |
| NEAR | ✅ | ✅ | ✅ | ✅ | receive, balance and send (N.7) — one Transfer from the implicit account, pinned to near-api-js |
| DOT | ✅ | ✅ | ✅ | ✅ | receive + balance (Asset Hub + relay), send from Asset Hub (N.8); history (2026-10-08) of DOT transfers on Asset Hub and the relay chain from Statescan — Subscan now refuses requests without a key; the wallet's own sends link by their hash |
| XNO | ✅ | ✅ | ✅ | ✅ | receive, balance, history and send (N.11) — m/44'/165'/0', ed25519-BLAKE2b; a send pockets the receivable it needs (open/receive blocks), then publishes a state send block; hash pinned to a mainnet block, signature to the Nano docs, proof of work computed locally (an unrolled BLAKE2b-64, seconds on a desktop) and accepted by a live node (NanoSendLiveTests) |
| DCR | ✅ | ✅ | ✅ | ✅ | receive, balance and send (N.12) — send: Decred's own transaction format and BLAKE-256 signature hash, checked against signatures mainnet accepted, each input's fraud proof read from the coin's own transaction (2026-10-08); history from dcrdata's Insight API, netted for change (2026-10-02) — m/44'/42'/0'/0/0, BLAKE-256 Hash160 + checksum, pinned to dcrd and Trust Wallet |

---

## 5. Open threat / privacy gaps (do not paper over)

| Gap | What to do |
|---|---|
| Malware on the user’s PC | Hardware wallet (H.1–H.2) |
| Explorer sees the session address set | Already: node choice + Tor, and the Radar now says so under every status (P1.11); next: fewer addresses per request, Silent Payments |
| UTXO linkage | PayJoin sending ✅ (P2.2) → CoinJoin; Silent Payments; the post-send report now names the count (P1.12 ✅) |
| Broadcast timing ↔ IP | Private broadcast ✅ (P2.4): with Tor on, the first hop is a random node over its own circuit. Dandelion++ itself needs node support that Bitcoin Core never added |
| Release substitution on GitHub | ✅ keyless build attestations (R.3) + the reproducible-build check; from the next Beta the updater itself refuses a release whose checksum list the project did not sign (R.10), so a user no longer has to choose to verify |
| $5 wrench | Duress / decoy password ✅ — with its limits stated in the app: it does not hide that other wallets exist on the machine, nor help against being watched typing |
| User forced to *trust* the client | ✅ watch-only xpub export + [`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md) + release attestations (R.3) |
| No external audit | Commission audit (R.5); until then do not write “audited” |
| Docs drifted from code | Annual documentation audit (M.7) |
| App Store / Play rejection | §9 compliance (L.1–L.8, wording) |
| SmartScreen “unknown publisher” | Builds are signed (R.2, self-signed); a CA certificate (R.2) and EV (R.6) remove the warning |
| Balances and history come from free public servers | Several sources per coin, the healthiest first, retries by themselves, history kept on the device; a server of your own per chain in Settings. A free server can still refuse — the wallet then says “could not be read”, never a guess. Next: an explorer key of your own (N.15), an Electrum-server option for BTC / LTC, a documented self-hosted setup |
| Zcash is transparent only — the public side of that chain | Shielded Zcash (P2.6); until then it is said wherever a ZEC address is shown |
| The seed is on the general-purpose computer | Seedless watch-only wallets (H.5), then Ledger / Trezor (H.2) |
| No build for macOS or iOS | H.6, then H.7 |

---

## 6. Execution order (user perspective + philosophy)

1. ✅ **P0.0** — Full restore proof (without this, all self-custody security is in question).  
2. ✅ **P0.6–P0.8** — fail-closed balance + send transport gate + network isolation CI.  
3. ✅ **P0.2–P0.4** — supply chain helpers + checksum CI + capability matrix.  
4. ✅ **P1.11 / P1.12** — Privacy Radar limits + “What leaked?” (UI honesty).  
5. ✅ **P1.1 / P1.13** — duress/decoy + verified “under coercion” scenario. **P1.2–P1.3** (hidden-wallet unlock UI, panic wipe) followed — see 6.  
6. ✅ **P1.2, P1.4–P1.7, P1.9, P1.10** — hidden-wallet unlock, simulation, private-send parity across UTXO chains, connection status, honest capture wording, address book, Activity coverage. ✅ **P1.3** panic password (2026-10-16) — every P1 item is closed.  
7. ✅ **P1.20** + §10 — self-verify (xpub export) and the public guide “how to check you are not being lied to.”  
8. ✅ **L.1 / L.3 / L.8** (+ L.2 wording) — disclaimers / age / ToS in the app; **L.4** is store-listing copy, written at submission.  
9. 🟡 **R.2 / R.6 / L.6** — Windows builds are signed in the pipeline (self-signed, 2026-10-07); next a CA-issued certificate, then EV, and PGP for Linux packages. R.3 attestations already cover “did this come from the project”; a CA certificate covers SmartScreen, which is a different problem. ← **next (human/process)**  
10. ✅ **N.1 / N.2** ERC-20 and TRC-20, ✅ **N.3** jettons and SPL tokens, both token programs.  
11. ✅ **P2.1** Taproot find/show/spend, ✅ **P2.2** PayJoin (sender).  
12. ✅ **H.1** PSBT export / review / sign. **H.2** Ledger/Trezor next — needs the seedless watch-only mode and the devices to test on.  
13. 🟡 **H.4** Android beta APK (side-load from the release page). Next: Tor on the phone, then L.5/L.7 store/geo compliance before any store listing.  
14. ✅ New L1s — **XRP, XLM, ATOM, NEAR, DOT receive, balance and send**, each pinned to its reference library or spec (the Stellar Go SDK, near-api-js, xrpl.js, cosmjs, and for DOT merlin + Polkadot.js signatures plus the running runtime's own metadata and validation).  
15. **R.7 / L.5** — Microsoft Store / geo-blocklist as needed.  
16. ✅ **H.5** seedless watch-only wallets (in the next Beta) → ⏳ **H.2** Ledger / Trezor on top of them.  
17. 📅 **H.6** macOS (signed and notarised), then **H.7** iOS.  
18. 📅 **P2.6** shielded Zcash; ✅ **N.15** token history on the side networks and an explorer key of your own; 🟡 **N.16** staking on more networks (NEAR network half done). **R.12** seal the history files by 2027-01-31.

Detailed PR schedule — §12 in [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md).

---

## 7. Deliberately not planned

| | |
|---|---|
| ❌ | Ads, telemetry, analytics |
| ❌ | Custody of user funds |
| ❌ | Venture / “growth” features instead of security |
| ❌ | KYC / on-ramp inside the core wallet without a separate legal decision |
| ❌ | Returning web/backend as part of the product |
| ❌ | Forks / rebrands without permission (see LICENSE) |
| ❌ | Claims of “anonymous / fully private / untraceable” without limit text (and for stores — see §9.4) |
| ❌ | KYC-less **on-ramp** (card → crypto) inside the app without a license (Play / Apple / MSB) |

---

## 8. Sources of truth (where this was compiled from)

| Document | Role |
|---|---|
| This file — **`docs/ROADMAP.md`** | Single backlog of “what remains” |
| [`../MANIFESTO.md`](../MANIFESTO.md) | Rules the backlog must align with |
| [`CLAUDE_IMPLEMENTATION_ROADMAP_UK.md`](CLAUDE_IMPLEMENTATION_ROADMAP_UK.md) | Detailed phases + DoD for implementation |
| [`SECURE_ANON_ROADMAP.md`](SECURE_ANON_ROADMAP.md) | Security/anonymity pillars + statuses |
| [`12-coins-and-chains.md`](12-coins-and-chains.md) | Full network matrix and “coming soon” |
| [`../README.md`](../README.md) | Public short roadmap |
| [`../CHANGELOG.md`](../CHANGELOG.md) | What already shipped |
| [`../PRIVACY.md`](../PRIVACY.md), [`../THREAT_MODEL.md`](../THREAT_MODEL.md) | Open gaps |
| [`BUILD_VERIFY.md`](BUILD_VERIFY.md) | Reproducible / verify builds |
| [`../TERMS_OF_SERVICE.md`](../TERMS_OF_SERVICE.md) | Store Terms |
| [`../PRIVACY_POLICY.md`](../PRIVACY_POLICY.md) | Formal privacy policy |
| [`../APP_STORE_NOTES.md`](../APP_STORE_NOTES.md) | Approved store wording (incl. Apple 3.1.5 / Play) |
| [`../GEO_BLOCKING.md`](../GEO_BLOCKING.md) | Restricted jurisdictions |
| [`../AUDIT_STATUS.md`](../AUDIT_STATUS.md) | External audit: none yet |
| [`../CONTACT.md`](../CONTACT.md) | Public contacts |
| [`../TRADEMARK_POLICY.md`](../TRADEMARK_POLICY.md) | Brand / name protection |
| [`../CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md) | Community standards |
| [`../LICENSE`](../LICENSE) | **MIT** (+ trademark notice) |
| [`../LICENSE_CHANGE.md`](../LICENSE_CHANGE.md) | Why we left Free-Use No-Derivatives |
| [`HARDWARE_WALLETS.md`](HARDWARE_WALLETS.md) | Malware residual risk + PSBT / H.2 path |
| [`../LEGAL/README.md`](../LEGAL/README.md) | Legal document map |
| [`../SECURITY/README.md`](../SECURITY/README.md) | Disclosure process folder |
| [`REPO_HARDENING.md`](REPO_HARDENING.md) | Live GitHub security / legal checklist |
| [`PRE_BETA_CHECKLIST.md`](PRE_BETA_CHECKLIST.md) | Public-beta gate (docs done; P0 code still open) |
| [`INDEX.md`](INDEX.md) | Documentation hub |

Update **this** file when priorities change; phase details belong in specialized docs, without duplicating conflicting statuses.

---

## 9. Compliance and legal safety (app stores)

Difference: **how the wallet works technically** (Tor, duress, non-custodial) ≠ **how it may be presented** in the App Store / Play / Microsoft Store. Desktop side-load (GitHub Releases) — low moderation risk; mobile stores — high.

| Platform | Moderation risk | Note |
|---|---|---|
| Apple App Store | **High** | §3.1.5 / payment apps; strict wording |
| Google Play | **Medium** | Crypto / Financial Services Policy since 2022 |
| Microsoft Store | **Medium** | signature required; without EV — mostly side-load |
| Windows/Linux side-load (GitHub) | **Low** | primary channel today |
| Flatpak / Snap / AUR | **Low** | PGP required (L.6) |

### 9.1. Platform rules (short)

| Platform | Allowed | Not allowed |
|---|---|---|
| **Apple** | Non-custodial (keys on device), access to own funds | Custody without a license; “hot” investment promises; misleading “fully private” when address clearnet leakage exists |
| **Google Play** | Non-custodial + clear disclaimer; publisher business verification | KYC-less on-ramp without a license; advertising “anonymous transactions” on transparent chains |
| **Microsoft Store** | Signed package | Unsigned exe as an “official” Store listing |
| **Linux repos** | Open-source + PGP | Unsigned packages in official repos |

### 9.2. Stores — backlog

| Requirement | Status | Comment |
|---|---|---|
| Formal TOS + Privacy Policy + App Store notes + Geo + Contact | ✅ | Root `.md` files (2026-09-15) |
| App Store: non-custodial confirmation + L.1/L.3 in **UI** | ✅ | First-run screen shipped; listing copy still to be written at submission |
| Google Play: crypto publisher rules + L.4 | ⏳ | Business verification; **no** KYC-less on-ramp in core |
| Microsoft Store: code signing | ⏳ R.2/R.6 | Without EV — keep GitHub side-load |
| Linux repos: PGP (L.6) | ⏳ | Fedora / Debian / Flathub |

### 9.3. Disclaimers (mandatory for store / public release)

| Element | ID | Status |
|---|---|---|
| On-start: not a financial institution + 18+ + non-custodial | L.1, L.3 | ✅ |
| On-start: no guarantee of anonymity (transparent chains) | L.2, text below | ✅ |
| Privacy warning before Send (address forever on-chain; RPC may see metadata) | — / P1.12 | 📅 |
| Geo-blocked list | L.5, L.7 | 📅 |
| ToS / Privacy Policy acceptance | L.8 | ✅ |

**On-start draft (EN, for implementing L.1/L.2/L.3):**

> **NOT A FINANCIAL INSTITUTION**  
> Phobia Wallet is non-custodial software. We do not hold your funds, manage your keys, or provide banking services. You are solely responsible for your recovery phrase and for complying with local law.  
>  
> **NO GUARANTEE OF ANONYMITY**  
> Privacy features (Tor, encrypted local storage) do not make a public ledger private. Blockchain analysis can still link activity on transparent chains.  
>  
> **USE AT YOUR OWN RISK**  
> Lost recovery phrases cannot be recovered by anyone. We are not liable for loss due to user error, malware, or regulatory action.

**Pre-Send draft (EN):**

> **TRANSACTION EXPOSES DATA**  
> Confirming means: your address is permanently on the chain; servers you query may learn metadata even over Tor; coin-control choices can link UTXOs publicly.

### 9.4. Wording (store / marketing)

| Do not use | Use |
|---|---|
| Fully private / anonymous / untraceable | Privacy-enhanced (non-custodial) |
| Anonymous transactions | Non-KYC self-custody; on-chain privacy varies by coin |
| Secure from government | Local encryption; user-held keys |
| No logging (as an absolute) | No cloud account; local-only storage (no telemetry) — see PRIVACY.md |

Monero may be described as a privacy coin **honestly**; Bitcoin/ETH and similar — **never** as “anonymous.”

### 9.5. Licenses and certificates

| Certificate / step | Approximate cost | Why |
|---|---|---|
| EV Code Signing (R.6) | ~$500–800/year | Windows trust / Store |
| Apple Developer / Google Play Console | annual fees | Mobile listing |
| Business verification | depends on jurisdiction | Play / Apple publisher |
| Money Transmitter / VASP | expensive | **Only** if a custodial on-ramp appears — currently **not planned** |
| BitLicense (NY etc.) | — | Non-custodial often exempt, but a **lawyer** must confirm; L.5/L.7 |

### 9.6. Practical steps before store submission

1. Legal entity / developer account (EU, Switzerland, or another jurisdiction — with a lawyer).  
2. EV code signing (R.6).  
3. Terms of Service + Privacy Policy + L.8 in the app.  
4. Screenshots for reviewers: disclaimers; proof that seed/keys never go to a server.  
5. Avoid the words in §9.4 “Do not use.”  
6. Always keep a **side-load** channel: GitHub Releases, FlatHub, AUR — independent of any store.

**Geo-blocking (L.7)** — politically and technically sensitive (PRC etc.; certain US states). Implement only after legal consultation; do not confuse with Tor censorship.

---

## 10. How to verify you are not being lied to

Written: **[`VERIFY_YOUR_WALLET.md`](VERIFY_YOUR_WALLET.md)** — the balance from a third party
(watch-only xpub, with its privacy cost stated), the live route (`curl --socks5-hostname`, and the
wallet's own connections), the download (`SHA256SUMS`), the build (reproducible-build check and the
pinned third-party binaries), and who the wallet talks to.

It ends with what none of it proves: a clean machine, a private transparent chain, or an audit that
has not happened.

**Still missing from that page, and named there rather than glossed over:** a GPG/Sigstore signature
over the checksum manifest (R.3), so a checksum proves the file matches the release page but not who
published it.

---

📖 Back to [Documentation Index](INDEX.md)

