# Privacy

There is no privacy policy in the usual sense, because there is nothing to have a policy about: no
server, no account, no database, no analytics. This document says what that means concretely, and
where the wallet's privacy stops.

---

## What never leaves your machine

- Your 24-word recovery phrase
- Every private key, including Monero's spend key
- Your vault password
- Your private transaction notes
- Your address book (encrypted at rest since 4.7)
- Your settings, themes, profile images
- Your activity log, and the transaction history each wallet has read

There is no telemetry, no crash reporting, no analytics SDK and no account. **This is enforced by the
build**, not promised: the test suite fails if any of nineteen analytics or crash-reporting packages is
referenced, if their API shapes appear in the source, or if anything writes an application log.

There is no application log at all. A log is the easiest place for an address or an amount to end up
sitting in the clear, so the wallet does not keep one.

---

## What does leave your machine, and to whom

A wallet cannot read a blockchain by itself. It has to ask a server — and on a public chain, asking
means saying your address.

| What is sent | To whom | Why |
|---|---|---|
| Your wallet addresses | Block explorers and RPC nodes | To read balances and history |
| A signed transaction | The same | To broadcast it |
| Your Cosmos, Polkadot and NEAR addresses | CryptoCrew's Cosmos Hub archive node; Statescan (Polkadot); NearBlocks | To read their history: an ordinary node of those chains keeps no complete per-account index, so an indexer is asked |
| One fixed list of coin symbols, the same for every wallet | CoinGecko, Binance; KuCoin and Bybit for what those refuse | To price coins. The list does not depend on what you hold, so the request says nothing about you |
| The coin a chart is for | Binance, then KuCoin, Bybit or CoinGecko | To draw the chart you open. The Home balance chart asks for the whole market list, in the same order from every wallet — never for "the coins this wallet holds" |
| Nothing but the request | open.er-api.com | Currency rates (USD → EUR, UAH, …) when you show a currency other than dollars, at most once an hour; kept on disk so figures appear at once |
| The address you pay from and the address you receive at | The swap route you confirm: THORChain, NEAR Intents (1Click) or Exolix | To carry out a swap — only when you swap |
| Your TRON, Solana or Cosmos address, and the staking transaction | TronGrid, the Solana RPC node, the Cosmos Hub REST node (the same servers that read your balance) | To read your stake and broadcast a staking action — only when you open Staking or stake |
| Your read-only API key's requests | The exchange it belongs to, and nobody else | To read that account's balances — only when you connect an exchange |
| Nothing identifying | GitHub | Shortly after start and twice a day, to look for a new release (Settings → Updates can turn this off); and when a release is downloaded |

**Tor hides your IP from these servers. It does not un-send the address.** That sentence is the whole
honest summary of wallet privacy on a transparent chain.

The full list — every server, who runs it, why it is contacted, and what it learns — is in
**Settings → Privacy**, sorted so the ones handed your actual addresses come first. It cannot fall
behind the code: the build fails if a host appears in the source without appearing in the list.

---

## What is never sent

- Your IP or location **as data**. It is visible to whoever answers a request, like any internet
  connection — which is what Tor is for. It is never *transmitted as a field*.
- Your device model, OS version, hardware identifiers, screen size or locale
- Your email, name or any identifier — none is ever asked for
- Your balances, as a figure. A server sees the address and can look the balance up itself; the wallet
  never reports one.
- Timestamps of anything, anywhere

---

## What you can change

- **Tor**, bundled and one switch. The kill-switch is fail-closed: with Tor-only on and Tor down, a
  request that would go over clearnet does not go at all. Over Tor, requests are split across
  **separate circuits by purpose** — the exit relay that saw your address is not the one that receives
  your transaction, and the one that prices coins sees neither. Each purpose reaches Tor under its own
  SOCKS username, and a test reads that username off a real SOCKS5 handshake (before 2026-10-07 it was
  not sent, and everything shared one circuit — see the
  [vulnerability history](SECURITY/VULNERABILITY_HISTORY.md)). Requests of one purpose share a circuit.
- **Tor on Android too**: the APK carries the same Tor, so the phone has the same switch,
  kill-switch and circuits.
- **A SOCKS5 proxy** of your own instead — Orbot (`127.0.0.1:9050`) on Android, for example — with the
  kill-switch kept armed while it is set. The destination always goes to the proxy as a name, so this machine's
  DNS never hears which servers the wallet uses.
- **Which server answers for each chain** — Bitcoin, Litecoin, Bitcoin Cash, Dogecoin, Ethereum,
  Solana, TON, Tron, Cardano, Monero. Pick a different company, or point the wallet at a node you run.
  A `.onion` node is accepted and will only ever be used with Tor on.
- **A fresh receive address per payment** on the UTXO chains.
- **Coin control**, so a spend need not join coins from different parts of your life.

A server you chose is never silently replaced by the default. If it stops answering, the wallet says
"unknown" and lets you decide — rerouting your addresses to the default is exactly what choosing was
meant to prevent.

---

## Where this stops

Being direct about this is the point of the document.

- **A transparent chain is public forever.** The amount and both addresses are on it. Nothing the
  wallet does changes that; Monero is the only coin here where it is not true.
- **Whoever you pay knows you paid them.** No network privacy touches this.
- **A spend links its inputs.** Two addresses joined by one transaction cannot be un-joined.
- **Malware on your machine defeats all of it.** See [THREAT_MODEL.md](THREAT_MODEL.md), Vector 1.
- **There is no CoinJoin and no Dandelion++ yet**, and PayJoin works only as the sender, with a
  receiver that offers it. Taproot is in since 4.8. Real gaps, on the roadmap, not implied away.
- **The local Monero service** (`monero-wallet-rpc`, on the desktop and on Android) listens on this device alone and
  answers only to a random login it makes at each start, in a file only your account can read. Before
  2026-10-07 it had no login.

---

## Data on disk

Everything the wallet stores lives in one directory — `data/` beside the executable, or
`%APPDATA%/UmbrellaWallet` if that is read-only (the folder kept its old name so existing wallets are
found). On Android, the app's private storage. Settings, balances, the address book, activity and the
market cache are written atomically — a crash mid-save leaves the previous copy, never half a file.

| Encrypted | In the clear |
|---|---|
| The seed vault (Argon2id → AES-256-GCM) | Settings: theme, language, currency |
| Exchange API keys | Watch-only addresses you added |
| Private transaction notes | The activity log, and each wallet's transaction history as read so far |
| The address book | Cached balances, prices and currency rates |

The items in the right-hand column are there because they are useful before the wallet is unlocked, or
because they are not secret. If that trade is wrong for you, **Settings → Danger zone** removes all of
it, and the build fails if the wallet writes a file the wipe does not handle.

### Data retention

- **We retain nothing on our servers** — there is no user database. See also the store-facing
  [PRIVACY_POLICY.md](PRIVACY_POLICY.md).
- **On your device**, data remains until you wipe it in-app or delete the application data directory:
  - Windows: `data\` next to `Phobia.exe`, or `%APPDATA%\UmbrellaWallet` when that folder is read-only
  - Linux: `data/` next to the program, or `~/.config/UmbrellaWallet` when that folder is read-only
  - Android: the app's private storage, removed with the app. Android backup is off, so it is never
    copied to a cloud account.
- **Encrypted backup files** you export remain until **you** delete them.

Full-disk encryption is the right answer to a stolen laptop, and it is the operating system's job
rather than this program's.

---

## Questions this document exists to answer directly

**Does the app send my IP, location or device model?** No, none is transmitted as data. Your IP is
visible to any server you connect to, as with any internet connection; Tor replaces it with an exit
node.

**Is there Google Analytics, Firebase or Sentry?** No, and the build fails if anyone adds one.

**Are my transactions, balances or contacts logged?** There is no application log. The activity list
is local and is erased by the wipe.

**Do you use third-party public RPC nodes?** Yes — every one listed by name in Settings → Privacy with
what it learns, and each chain's server replaceable by your own.

**Can I use my own node or proxy?** Yes: your own server per chain, your own Monero node, your own
SOCKS5 proxy (Orbot on Android), or the bundled Tor.

**Can a price service tell what I hold?** Not from the price list, nor from the Home balance chart:
both ask for the same fixed list from every wallet (until 2026-10-07 the balance chart asked for exactly
the coins held). Opening one coin's chart does name that coin. Over Tor it sees an exit's address, not
yours, and it never sees a wallet address.

---

📖 Back to [Documentation Index](docs/INDEX.md)

