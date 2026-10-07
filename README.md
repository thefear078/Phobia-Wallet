<div align="center">

<img src="docs/assets/readme-banner.png" width="100%" alt="Phobia Wallet"/>

# Phobia Wallet

**A self-custody crypto wallet for Windows, Linux and Android that never asks who you are.**

No account. No email. No phone number. No KYC. No tracking. No fee on your transfers.

<sub>an independent project by <b>the fear</b></sub>

> **Umbrella Wallet is now Phobia Wallet.** Same code, same keys, same encrypted vault — a new name and
> a new look. Phobia is in **beta**, and a stable copy (4.9.0 and older) is never offered a beta by
> itself: install Beta 1 from its release page over the old copy, and it keeps the vault and settings.

<br/>

![CI](https://github.com/thefear078/Phobia-Wallet/actions/workflows/ci.yml/badge.svg)
![CodeQL](https://github.com/thefear078/Phobia-Wallet/actions/workflows/codeql.yml/badge.svg)
![Security](https://github.com/thefear078/Phobia-Wallet/actions/workflows/security.yml/badge.svg)
![Release](https://img.shields.io/github/v/release/thefear078/Phobia-Wallet?include_prereleases&label=release)
![License](https://img.shields.io/badge/license-MIT-4B3F86)
![Version](https://img.shields.io/badge/version-4.10.0--beta.1-2F6BEF)
![Tests](https://img.shields.io/badge/tests-1850%2B%20offline-7DCF8F)
![Platform fee](https://img.shields.io/badge/platform%20fee-none-7DCF8F)

![Windows](https://img.shields.io/badge/Windows-ready-4B3F86?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-ready-6E5FB8?logo=linux&logoColor=white)
![Android](https://img.shields.io/badge/Android-beta%20APK-6E5FB8?logo=android&logoColor=white)
![Tor](https://img.shields.io/badge/Tor-bundled-7D4698?logo=torproject&logoColor=white)
![Monero](https://img.shields.io/badge/Monero-full%20wallet-F26822?logo=monero&logoColor=white)

**[⬇ Download Beta 1](https://github.com/thefear078/Phobia-Wallet/releases/tag/v4.10.0-beta.1)** ·
[Verify your download](#verify-what-you-downloaded) ·
[Build it yourself](docs/building.md) ·
[Security](SECURITY.md) ·
[The rules](MANIFESTO.md) ·
[Threat model](THREAT_MODEL.md) ·
[Privacy](PRIVACY.md) ·
[**Docs index**](docs/INDEX.md) ·
[Brand kit & posters](brand/README.md)

<br/>

<img src="docs/assets/screenshot-portfolio-v410.png" width="88%" alt="Phobia Wallet portfolio"/>

</div>

---

## Documentation navigation

Official docs are one web: root philosophy/legal files + `docs/` deep-dives. **Hub:** [docs/INDEX.md](docs/INDEX.md).

```
Phobia-Wallet/
├── README.md                 ← you are here
├── MANIFESTO.md              ← core philosophy (THE RULES)
├── SECURITY.md · PRIVACY.md · THREAT_MODEL.md
├── TERMS_OF_SERVICE.md · PRIVACY_POLICY.md · TRADEMARK_POLICY.md
├── LEGAL/                    ← pointers to legal docs
├── docs/
│   ├── INDEX.md              ← documentation hub
│   ├── getting-started.md
│   ├── ROADMAP.md
│   └── …
└── .github/SUPPORT.md
```

| If you want to… | Read this first |
|---|---|
| Install and use | [Getting started](docs/getting-started.md) · [User guides](docs/guides/README.md) · [On Android](docs/guides/android.md) |
| Swap, stake, connect | [Swap](docs/guides/swap.md) · [Staking](docs/guides/staking.md) · [Connect](docs/guides/connect.md) |
| Read it in Ukrainian | [Посібник користувача](docs/guides/user-guide-uk.md) |
| Understand the philosophy | [MANIFESTO.md](MANIFESTO.md) |
| Know security guarantees | [THREAT_MODEL.md](THREAT_MODEL.md) |
| Report a vulnerability | [SECURITY.md](SECURITY.md) |
| Build from source | [Building](docs/building.md) |
| Add a new coin | [Adding a chain](docs/adding-a-chain.md) |
| See the backlog | [ROADMAP.md](docs/ROADMAP.md) |
| Legal / store | [LEGAL/](LEGAL/README.md) · [Terms](TERMS_OF_SERVICE.md) · [Trademark](TRADEMARK_POLICY.md) |

## What this is

Phobia is a wallet for Windows and Linux — with an Android beta built from the same code — that holds
your keys and nothing else of yours.

Your 24-word seed is generated on your machine, encrypted into a local vault with your password
(Argon2id → AES-256-GCM), and never leaves the device. There is no server that knows you exist. There
is no account to create, nothing to verify, and no way for us — or anyone holding this software — to
freeze, seize, or recover your funds.

It holds eighteen chains in one vault — plus the Ethereum networks that share your 0x address —
including Monero as a **full** wallet rather than a receive-only stub, and ships Tor inside the binary
so your balance lookups don't hand your IP address to a block explorer. What each chain can do today,
receive, balance or send, is in [Coins](#coins) — nothing is listed as working before it does.

## Why it exists

Most wallets ask you to trade privacy for convenience, and most of them don't say so plainly.

- Exchange wallets hold your keys and know your identity.
- Most "private" wallets do one chain well. Feather is Monero. Sparrow is Bitcoin. If you hold both,
  you run both.
- Wallets with Tor support usually make you configure it. Which means most people don't.
- Nearly every wallet takes a cut of your transfers, and many bury it.

Phobia is the wallet we wanted to exist: one vault for the coins people actually hold together,
Tor as a switch instead of a setup guide, and no cut of your money.

## What makes it different

**Monero and Bitcoin and USDT in one vault.**
Not a Monero wallet with Bitcoin bolted on, and not a Bitcoin wallet that shows XMR as "coming soon".
Monero runs the real `monero-wallet-rpc` locally over loopback, so its balance is computed on your
machine because no explorer can compute it for you.

**Tor is bundled, not assumed.**
One switch. The wallet starts its own Tor client on port 9250 (or the next free port) — separate from
a Tor Browser you may already be running — and routes every balance lookup, price fetch and broadcast
through it, each kind of request on a circuit of its own, so the exit that saw your address is not the
one that sees you spend from it. There is also a kill-switch: when it's on, a request
that cannot go through Tor does not go at all, and the wallet brings Tor up by itself on launch so that
never leaves you offline. On Android, point it at Orbot — see [Phobia on Android](docs/guides/android.md).

**The Security Center tells you the truth, not a promise.**
It reads live settings and reports what is *actually* protecting the wallet right now. If Tor is off,
it says your IP is visible to every explorer you use. It will not flatter you — and one button turns on
every protection it scores. Every send asks for your password again before anything is signed.

<div align="center">
<img src="docs/assets/screenshot-security-v410.png" width="80%" alt="Security Center"/>
</div>

**A market you can actually read.**
The coin chart is an exchange chart: candles or a line, a volume band, the price axis on the right with
the last price tagged, a crosshair on both axes and the hovered candle's open, high, low and close —
with KuCoin and Bybit behind Binance, so it still draws over Tor.

<div align="center">
<img src="docs/assets/screenshot-market-v410.png" width="80%" alt="Market chart with candles"/>
</div>

**Stake from the wallet.**
TRX, SOL and ATOM stake right here — freeze and vote on TRON, a stake account on Solana, a delegation on the
Cosmos Hub — each built, checked and signed in the wallet and confirmed with your password like a send.
The transaction TronGrid builds for TRON is decoded and compared with what you asked before it is signed.

**Your balance, point by point.**
The chart beside the balance follows the pointer: each point is what the coins you hold now were worth at
that moment, when that was, and the move since the start — real prices, not a smoothed guess. A wallet of
stablecoins draws as the flat line it is.

**Activity you can scan.**
Every event has its own icon, the list runs newest first under Today / Yesterday headings, and every
transfer shows what it is worth.

<div align="center">
<img src="docs/assets/screenshot-activity-v410.png" width="80%" alt="Activity"/>
</div>

**You choose which server sees your addresses.**
A wallet cannot read a chain by itself — it has to ask somebody, and on a transparent chain asking
means *saying the address*. Tor hides your IP; it does not un-send an address. So Settings → Privacy
names every server the wallet talks to and what each one learns, and lets you point any chain
somewhere else — a different company, or a node you run. Monero, Bitcoin, Litecoin, Bitcoin Cash,
Dogecoin, Ethereum, Solana, TON, Tron, Cardano, XRP, Stellar, Cosmos, NEAR and Polkadot.

Two rules are enforced rather than suggested: a `.onion` node is never used without Tor and is never
silently swapped for a clearnet one, and a plain `http://` endpoint is refused outright — choosing
your own server *for privacy* and then sending addresses in clear would be worse than not choosing.

**Privacy Radar.**
Before you send, the wallet reads the transaction you are about to make and tells you what it would
reveal on-chain — chiefly whether it links coins that were previously unconnected, which is how chain
analysis de-anonymises people. The analysis runs offline, on your own data, for you.

**No cut of your transfers.**
You pay the network's miner/validator fee and nothing else. See [What this costs](#what-this-costs).

## Screenshots

| Welcome | Unlock |
|---|---|
| <img src="docs/assets/screenshot-welcome-v410.png" alt="Welcome"/> | <img src="docs/assets/screenshot-unlock-v410.png" alt="Unlock"/> |
| **Receive** | **Settings** |
| <img src="docs/assets/screenshot-receive-v410.png" alt="Receive"/> | <img src="docs/assets/screenshot-settings-v410.png" alt="Settings"/> |

The default look is **Phobia's midnight violet** — bold and quiet: a near-black page, charcoal cards,
one violet accent, clean type, crystal mountains on the horizon and the large crystal on the balance card.
Motion is Phobia's own and each piece has a switch: crystals floating up behind the page, glints
twinkling across it, a sweep of light over the balance card. **15 themes**
share that design in their own colours — the glow behind the page, the mountains and both logos take the
theme's hue — and the gold that was Umbrella's default is still there as **Honey gold**:

| Honey gold · light on black | Uniswap · hot pink |
|---|---|
| <img src="docs/assets/theme-gold-v410.png" alt="Honey gold theme"/> | <img src="docs/assets/theme-uniswap-v410.png" alt="Uniswap theme"/> |
| **Void · electric OLED** | **Navy · the classic blue** |
| <img src="docs/assets/theme-void-v410.png" alt="Void theme"/> | <img src="docs/assets/theme-navy-v410.png" alt="Navy theme"/> |

## Coins

Every row in the wallet prints the network under the coin name, because sending on the wrong network
is the most common way people lose money.

| Coin | Receive | Balance | Send | History | Swap | Notes |
|---|:---:|:---:|:---:|:---:|:---:|---|
| Bitcoin (BTC) | ✅ | ✅ | ✅ | ✅ | ✅ | BIP84 native SegWit, full HD scan |
| Ethereum (ETH) | ✅ | ✅ | ✅ | ✅ | ✅ | + every ERC-20 at the same address — **and now sendable**, fee in ETH |
| Litecoin (LTC) | ✅ | ✅ | ✅ | ✅ | ✅ | BIP84, full HD scan |
| Dogecoin (DOGE) | ✅ | ✅ | ✅ | ✅ | ✅ | real UTXO spend |
| Bitcoin Cash (BCH) | ✅ | ✅ | ✅ | ✅ | ✅ | CashAddr, SIGHASH_FORKID |
| Monero (XMR) | ✅ | ✅ | ✅ | ✅ | ✅ | local `monero-wallet-rpc`, loopback only |
| Solana (SOL) | ✅ | ✅ | ✅ | ✅ | ✅ | SPL tokens send too, Token-2022 (PayPal USD) included when its extensions allow |
| TRON (TRX) | ✅ | ✅ | ✅ | ✅ | ✅ | + every TRC-20 at the same address |
| USDT (TRC-20) | ✅ | ✅ | ✅ | ✅ | ✅ | same address as TRX; fee paid in TRX |
| TON | ✅ | ✅ | ✅ | ✅ | 🟡 | wallet v4R2, pinned to `@ton/ton` |
| Cardano (ADA) | ✅ | ✅ | ✅ | ✅ | ✅ | CIP-1852, BIP32-Ed25519 |
| Zcash (ZEC) | ✅ | ✅ | ✅ | ✅ | ✅ | **transparent `t1…` only** — not shielded |
| XRP Ledger (XRP) | ✅ | ✅ | ✅ | ✅ | ✅ | destination tag for exchange deposits; an address becomes an account once it receives the network's reserve |
| Stellar (XLM) | ✅ | ✅ | ✅ | ✅ | ✅ | SEP-0005, restores in LOBSTR / Solar / Ledger; memo for exchange deposits |
| Cosmos Hub (ATOM) | ✅ | ✅ | ✅ | — | ✅ | memo for exchange deposits; the balance is *available* ATOM — staked ATOM is not counted |
| NEAR Protocol (NEAR) | ✅ | ✅ | ✅ | ✅ | ✅ | from your implicit account, to any `.near` name or implicit account; swaps through the Exolix exchange |
| Polkadot (DOT) | ✅ | ✅ | ✅ | — | — | sr25519, same account as Polkadot.js / Nova; balance adds Asset Hub + relay; sends from Asset Hub; no route trades native DOT, so no swap |
| Nano (XNO) | ✅ | ✅ | ✅ | ✅ | ✅ | restores in Ledger / Trust / Nault (BIP39); no fee — a send pockets what it needs first, with proof of work computed on your device |
| Decred (DCR) | ✅ | ✅ | — | ✅ | ✅ | the BIP44 account Trust Wallet / Ledger / Exodus use (Decrediton derives differently); bought through a swap, sending is not here yet |
| Linea (ETH) | ✅ | ✅ | ✅ | 🟡 | — | same `0x` as mainnet |
| zkSync Era (ETH) | ✅ | ✅ | ✅ | 🟡 | — | the gas comes from zkSync's own estimate, not Ethereum's 21,000 |

Plus the native coin of every major EVM network at the same `0x` address (BNB, MATIC, AVAX, FTM, CRO,
and ETH on Arbitrum / Optimism / Base), and NFTs listed by name and count with **no image fetch**, so
viewing them never leaks your IP.

Coins swap for one another, each on its own network, and always to your own address. The route is chosen
by trust and named before you pay: **THORChain** first (nobody holds your coins), then **NEAR Intents** (a
smart contract holds them for the swap and refunds you if it cannot be filled; +0.25% without a partner
key), and the **Exolix** exchange last — the route for Monero, Nano, Decred and NEAR, which nothing
decentralised reaches, and otherwise only when the other two cannot quote the pair. It holds the coins for
the minutes of the swap, and the screen says so in a warning colour. 🟡 in the table: a route exists but
did not quote that coin when this was written.

## Download

<img src="docs/assets/logo-phobia.png" width="64" align="left" alt="" hspace="14"/>

The current build is **Beta 1** — its [release page](https://github.com/thefear078/Phobia-Wallet/releases/tag/v4.10.0-beta.1) has every file below. It is a
pre-release, so GitHub does not label it “Latest”; older builds are on the
[releases page](https://github.com/thefear078/Phobia-Wallet/releases).
This is the icon you will see once it is installed.

<br clear="left"/>

| | |
|---|---|
| **Windows installer** | [`PhobiaWallet-Setup-Beta-1.exe`](https://github.com/thefear078/Phobia-Wallet/releases/download/v4.10.0-beta.1/PhobiaWallet-Setup-Beta-1.exe) |
| **Windows portable** | [`PhobiaWallet-Beta-1-win-x64-portable.exe`](https://github.com/thefear078/Phobia-Wallet/releases/download/v4.10.0-beta.1/PhobiaWallet-Beta-1-win-x64-portable.exe) — one file, no install, leaves nothing behind |
| **Linux** | [`PhobiaWallet-Beta-1-linux-x64.tar.gz`](https://github.com/thefear078/Phobia-Wallet/releases/download/v4.10.0-beta.1/PhobiaWallet-Beta-1-linux-x64.tar.gz) |
| **Android** | [`PhobiaWallet-Beta-1-android.apk`](https://github.com/thefear078/Phobia-Wallet/releases/download/v4.10.0-beta.1/PhobiaWallet-Beta-1-android.apk) — Android 7.0 or newer; [how to install](docs/guides/android.md) |
| **Checksums** | [`SHA256SUMS-Beta-1.txt`](https://github.com/thefear078/Phobia-Wallet/releases/download/v4.10.0-beta.1/SHA256SUMS-Beta-1.txt) |

Portable mode matters if you don't want the wallet to be installed on the machine at all: it runs
from the file you downloaded and keeps its data next to it.

**On a phone:** the Android app is the same wallet — the same keys, signing code, coins and screens —
laid out for a phone. From Beta 2 the APK carries Tor and the Monero service too — the projects' own
Android builds, checked against their signed sums — so the phone has the same Tor switch, kill-switch and
Monero balance as the desktop ([Orbot](https://orbot.app) works as well). The
[Android guide](docs/guides/android.md) says how, and what else differs.

<img src="docs/assets/screenshot-android-home-v410.png" width="32%" alt="Phobia on Android — home"/> <img src="docs/assets/screenshot-android-assets-v410.png" width="32%" alt="Phobia on Android — assets and market"/> <img src="docs/assets/screenshot-android-quick-v410.png" width="32%" alt="Phobia on Android — quick actions"/>

### Verify what you downloaded

Every release ships `SHA256SUMS-<version>.txt`. Check it before you run anything:

```bash
# Linux / macOS — run in the folder with the download and the sums file
sha256sum -c SHA256SUMS-Beta-1.txt
```

```powershell
# Windows PowerShell — compare against the matching line in the sums file
Get-FileHash .\PhobiaWallet-Setup-Beta-1.exe -Algorithm SHA256
```

If the hash does not match, do not run it. From Beta 2 every Windows executable and the APK are also
signed by the author — Windows thumbprint `89C2D871C195D57C14D1911B6C47629DE052C553`, Android
certificate SHA-256 `C3:80:0E:…:95:72:D1` (in full in [SECURITY.md](SECURITY.md#verifying-what-you-run)).
The Windows certificate is self-signed for now, so Windows still names no publisher: check the thumbprint.
Every file also has a build attestation (`gh attestation verify <file> --repo thefear078/Phobia-Wallet`)
and each release an SBOM. All four checks, and building it yourself — the managed assemblies that derive
keys and sign come out byte-identical — are in **[docs/BUILD_VERIFY.md](docs/BUILD_VERIFY.md)**.

## Get started

1. **Create** a new wallet, or **import** any BIP39 phrase (12/15/18/21/24 words) from another wallet.
   The derived addresses will match the original exactly.
2. Write the 24 words on **paper**. The wallet then asks you for three of them at random — not
   theatre, it is the only way to catch a phrase you wrote down wrong while it still costs nothing.
3. Turn **Tor** on in Settings *before* you unlock, if you don't want your addresses queried over
   clearnet even once.
4. Send a **small test amount** to any new address first. A blockchain transfer is final.

While your seed phrase or Monero keys are on screen, the window is excluded from screenshots and
screen sharing on Windows, and from screenshots, recording and the recent-apps preview on Android. Linux
has no such switch for an app to throw, and the Security Center says so. A camera pointed at the screen
still works everywhere, so reveal them alone.

## Architecture

```mermaid
flowchart TB
    subgraph UI["Umbrella.Wallet.App — Avalonia UI"]
        V["Views (XAML)"]
        VM["MainViewModel (partial classes)"]
        LOC["Localization · 6 languages"]
        THEME["Theming · 15 themes"]
    end

    subgraph CORE["Umbrella.Wallet.Core — pure, offline, testable"]
        SEED["BIP39 seed"]
        DERIVE["Derivation<br/>BIP32 · SLIP-0010 · BIP32-Ed25519"]
        UTXO["UTXO scan + spend planner"]
        SAFETY["Safety<br/>Privacy Radar · spam · address checks"]
        AMOUNT["AmountInput<br/>locale-safe parsing"]
    end

    subgraph INFRA["Umbrella.Wallet.Infrastructure — I/O"]
        VAULT["Encrypted vault<br/>Argon2id → AES-256-GCM"]
        HTTP["PublicHttp"]
        TOR["Bundled Tor client :9250"]
        XMRRPC["monero-wallet-rpc<br/>loopback only"]
        SIGN["Signers<br/>NBitcoin · Nethereum · BouncyCastle"]
    end

    EXPL["Public explorers / RPCs"]

    V --> VM
    VM --> CORE
    VM --> INFRA
    HTTP -->|"when Tor is on"| TOR --> EXPL
    HTTP -.->|"direct, unless kill-switch"| EXPL
    INFRA --> XMRRPC
    VAULT -->|"decrypted only in memory"| SEED
    SEED --> DERIVE --> SIGN

    classDef secret fill:#2E1013,stroke:#EE3244,color:#fff
    class SEED,VAULT,SIGN secret
```

**The rule the layout enforces:** `Core` never touches the network, so every piece of money logic —
derivation, UTXO selection, fee maths, amount parsing, privacy analysis — is testable offline against
known vectors. Anything that talks to the outside world lives in `Infrastructure` and goes through one
Tor-aware HTTP client.

### What leaves your device

| Never leaves | Leaves (to public explorers / RPCs) |
|---|---|
| Your seed phrase | The addresses you look up |
| Every private key | Transactions you broadcast |
| Your vault password | Coin prices you fetch |
| Your private transaction notes | *(all of it behind Tor when Tor is on)* |

There is no account, no email, no telemetry and no analytics of any kind.

## Repository layout

```
desktop/
  src/
    Umbrella.Wallet.Core/            pure logic — no network, no UI
      Chains/                        chain catalog, address validation
      Derivation/                    BIP32 / SLIP-0010 / Ed25519 derivation
      Utxo/                          HD scan, spend planner, fee levels
      Safety/                        Privacy Radar, spam detection, token identity
      Amounts/                       locale-safe amount parsing
      Chart/                         candle aggregation
    Umbrella.Wallet.Infrastructure/  everything that does I/O
      Network/                       explorers, RPC, senders, Tor
      EncryptedFileSeedVault.cs      Argon2id → AES-256-GCM vault
      AtomicFile.cs                  every save is all-or-nothing
    Umbrella.Wallet.App/             Avalonia UI, desktop and phone layouts
      Views/                         XAML
      ViewModels/                    MainViewModel, split by feature
      Localization.cs                every user-facing string, 6 languages
      Theming.cs                     15 themes
      GuideContent.cs                the in-app guide
    Umbrella.Wallet.Android/         the Android head (hosts the App project)
  tests/
    Umbrella.Wallet.Core.Tests/      about 1,850 offline tests
    Umbrella.Wallet.UiTests/         every screen drawn headless, desktop and phone
  installer/                         Inno Setup script
  scripts/                           release, signing, Tor/Monero staging
docs/                                everything in this README's Docs section
```

## Build it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Nothing else.

```bash
git clone https://github.com/thefear078/Phobia-Wallet.git
cd Phobia-Wallet
dotnet build desktop/Umbrella.Wallet.sln -c Release
```

```bash
# run it
dotnet run --project desktop/src/Umbrella.Wallet.App
```

```bash
# the full offline test suite — no network required
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter "Category!=Live"
```

Producing installers, the portable build and checksums is documented in
**[docs/building.md](docs/building.md)**.

## Tests

More than 1,850 offline tests, run on every push by [CI](.github/workflows/ci.yml). They are not there
for a badge — several classes of them exist because the alternative is losing money:

- **Derivation** is pinned byte-for-byte to official test vectors and to the reference libraries
  (`@ton/ton` for TON, `cardano-serialization-lib` for ADA). An address the wallet shows you is an
  address the original wallet would show.
- **Amount parsing** is pinned because `"0,5"` parsed naively reads as `5` — ten times the amount.
- **Spend planning** is pinned so a partial network scan can never quietly lower a balance, and a fee
  level can never drop below the relay floor and strand a transaction.
- **Localization parity** fails if any language is missing a key, so a feature cannot silently render
  in English inside a translated wallet.
- **Theme contrast** fails if body text, muted text, or a button label falls below WCAG AA.
- **Signing** for every new format — Taproot, PSBT, PayJoin, Stellar — is pinned to transactions the
  reference implementations build (BIP test vectors, NBitcoin, the Stellar Go SDK), never to the
  wallet's own output.
- **Busy explorers** are pinned too: a timeout is not a cancel, a 429 is waited out, and an unread
  balance is never shown as zero.
- **Privacy on the wire**: a SOCKS5 server on loopback reads each request's circuit label off the
  handshake, a listener proves the kill-switch opens no socket, and the Monero service's login is
  checked against the real `monero-wallet-rpc`.
- **Screens as drawn**: every section, desktop and phone, is rendered headless with the real styles, and
  a screen that shows an object's type name instead of words fails the build.

`--filter "Category!=Live"` excludes the handful of tests that hit real explorers, so the default run
is fully offline and deterministic.

## Diagnostics

| Symptom | Where to look |
|---|---|
| Balance is slow or missing | Security Center → is Tor on? Tor adds latency by design |
| Monero balance stuck at "Scanning…" | It is a real wallet syncing, not an API call — it takes time |
| A coin shows "Not ready" | That chain's adapter is not implemented; it is never a fake address |
| Send fails with "not synced" | A partial scan refuses to spend rather than risk a wrong balance |
| An unknown token appeared | Suspected spam airdrops are folded away — see the notice above Holdings |

More in **[docs/troubleshooting.md](docs/troubleshooting.md)**.

## What this costs

**Phobia takes no cut of your transfers.** You pay the network's own miner/validator fee and nothing
else — no platform fee, no subscription, no withdrawal fee, no account fee. A wallet that holds your
own keys should not charge you for touching your own money.

It is funded instead by:

- **[GitHub Sponsors](https://github.com/sponsors/thefear078)**
- **Bounties** — anyone can fund a specific coin or feature
- **A small swap spread**, later and only on swaps, which are optional in a way a send is not

There is no advertising and no tracking, and there will not be. Those are the two things that would
make everything else on this page untrue.

## Security

The short version:

- Seed generated with the OS CSPRNG, 256-bit entropy, BIP39.
- Vault: Argon2id (m=64 MiB, t=4, p=2) → AES-256-GCM with versioned associated data.
- Keys are decrypted into memory only for the moment they are used, then zeroed.
- Seed and key screens set `WDA_EXCLUDEFROMCAPTURE` on Windows and `FLAG_SECURE` on Android, so
  screenshots and screen sharing see nothing.
- Auto-lock on idle and on minimise; `Ctrl+L` locks instantly.
- Every network call goes through one Tor-aware client; the kill-switch makes "no Tor" mean "no
  request", not "quietly direct". Over Tor, each purpose (address lookups, broadcasts, prices, swaps…)
  gets its own circuit, and names are resolved by Tor, never by this machine's DNS.
- The local Monero service runs with a random login only this user can read, so neither a web page nor
  another account on the machine can ask it to send.

The long version, including the threat model and what Phobia explicitly does **not** protect you
from, is in **[SECURITY.md](SECURITY.md)**.

Found a vulnerability? [Report it privately](https://github.com/thefear078/Phobia-Wallet/security/advisories/new).
Please don't open a public issue for anything that could put funds at risk.

## Roadmap

Being honest about what exists and what doesn't. Full backlog (coins, security, UX, hardware):
**[docs/ROADMAP.md](docs/ROADMAP.md)**.

| | |
|---|---|
| ✅ Shipped | 18 chains, Tor + kill-switch with per-purpose circuits, Monero full wallet, swaps, Security Center, Privacy Radar, CSV export, encrypted notes, themes, 6 languages · duress password · transaction simulation · Tor/Monero pinned to upstream's signed sums · one capability matrix · any held ERC-20 / TRC-20 / jetton · restored Taproot found and spent · PayJoin when a payment link offers it · PSBT export, review and signing · keyless release attestations · signed Windows and Android builds, SBOM, immutable release tags |
| 🔜 Next | **Seedless watch-only mode** · Ledger / Trezor |
| 🧪 Beta | **Android** (APK with its own Tor and Monero service from Beta 2; checks for updates, Android installs them) |
| 🗓 Planned | Tor bundled on Android · Decred sending · reproducible builds · external security audit |
| ❌ Not planned | Any advertising · any telemetry · custody of your funds · venture funding |

## Documentation

| | |
|---|---|
| [docs/INDEX.md](docs/INDEX.md) | **Central documentation hub** — start here |
| [docs/getting-started.md](docs/getting-started.md) | Install, verify, first wallet |
| [docs/guides/](docs/guides/README.md) | **User guides** — Android, swap, staking, connect, wallets and settings, and a full guide in Ukrainian |
| [MANIFESTO.md](MANIFESTO.md) | The rules this wallet is held to — **do not dilute this tone** |
| [THREAT_MODEL.md](THREAT_MODEL.md) | Attack vectors: what is defended, and where the defence ends |
| [PRIVACY.md](PRIVACY.md) | Technical privacy: what leaves this machine |
| [PRIVACY_POLICY.md](PRIVACY_POLICY.md) | **Formal privacy policy** (App Store / Play) |
| [TERMS_OF_SERVICE.md](TERMS_OF_SERVICE.md) | **Terms of use** (non-custodial, 18+, liability) |
| [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) | Name and logo protection |
| [APP_STORE_NOTES.md](APP_STORE_NOTES.md) | Approved store wording |
| [GEO_BLOCKING.md](GEO_BLOCKING.md) | Restricted jurisdictions (store compliance) |
| [AUDIT_STATUS.md](AUDIT_STATUS.md) | External audit status — none yet |
| [CONTACT.md](CONTACT.md) | GitHub · Telegram · TikTok · Reddit |
| [LEGAL/README.md](LEGAL/README.md) | Legal document map |
| [docs/README.md](docs/README.md) | Engineering docs index |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Single backlog (coins, security, store/legal) |
| [docs/architecture.md](docs/architecture.md) | How the layers fit together and why |
| [docs/building.md](docs/building.md) | Build, run, test, package installers |
| [docs/security-model.md](docs/security-model.md) | Threat model, crypto choices, what is not protected |
| [docs/forking.md](docs/forking.md) | Fork it: what to change, what not to, licence limits |
| [docs/adding-a-chain.md](docs/adding-a-chain.md) | Add a coin end to end, with the safety gates |
| [docs/localization.md](docs/localization.md) | Add or fix a language |
| [docs/theming.md](docs/theming.md) | Add a theme that passes the contrast tests |
| [docs/testing.md](docs/testing.md) | What the suite covers and how to extend it |
| [docs/troubleshooting.md](docs/troubleshooting.md) | Diagnosing a misbehaving wallet |
| [docs/PRE_BETA_CHECKLIST.md](docs/PRE_BETA_CHECKLIST.md) | Public beta gate (what is left) |
| [docs/WORKFLOW.md](docs/WORKFLOW.md) | How to add coins and keep ROADMAP honest |
| [docs/REPO_HARDENING.md](docs/REPO_HARDENING.md) | Live GitHub security / legal status |
| [CHANGELOG.md](CHANGELOG.md) | Every release |
| [.github/SUPPORT.md](.github/SUPPORT.md) | How to get help (never share seeds) |

## The honest part

- A blockchain transfer is **final**. Nobody can reverse it — not us, not a support desk.
- If you lose the 24 words **and** the vault password, the funds are gone. That is what self-custody
  means, and it is the trade you are making for nobody being able to freeze them.
- Tor hides your IP from explorers. It does not make a transparent chain private: Bitcoin, Ethereum
  and the rest are public ledgers, and Privacy Radar exists to tell you what yours reveals.
- Zcash here is transparent addresses only. It is listed that way rather than implying shielded
  privacy the wallet does not provide.
- No external security audit has been done yet — see [AUDIT_STATUS.md](AUDIT_STATUS.md). Tests and
  public CI are evidence, not a substitute.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md), and
[docs/forking.md](docs/forking.md) if you are building on top of this.

Anything touching the send path, the vault, or key derivation needs tests. That is not bureaucracy:
those are the paths where a bug costs somebody their money.

## License

**MIT** for the code — see [LICENSE](LICENSE) and [LICENSE_CHANGE.md](LICENSE_CHANGE.md). Brand names
and logos remain under [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md). Forking guide:
[docs/forking.md](docs/forking.md). Summary: [LEGAL/LICENSE_SUMMARY.md](LEGAL/LICENSE_SUMMARY.md).
Third-party: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

---

## Who makes this

<div align="center">

<img src="docs/assets/logo-thefear-ghost.png" width="128" alt="the fear"/>

### the fear

<sub>An independent developer. No company, no investors, no board to answer to —<br/>
which is exactly why there is no tracking and no cut of your transfers.</sub>

<br/>

<a href="https://t.me/PhobiaStat">
  <img src="docs/assets/logo-telegram-channel.png" width="72" alt="Phobia Wallet on Telegram"/>
</a>

**[t.me/PhobiaStat](https://t.me/PhobiaStat)** ·
[GitHub](https://github.com/thefear078/Phobia-Wallet) ·
[TikTok @thefear078](https://www.tiktok.com/@thefear078) ·
[Reddit u/Particular_Lime_7004](https://www.reddit.com/user/Particular_Lime_7004)

<sub>Releases, security notes and contact — see [CONTACT.md](CONTACT.md).<br/>
Nobody there will ever ask you for your seed phrase.</sub>

<br/>

[💖 Sponsor this work](https://github.com/sponsors/thefear078) ·
[Terms](TERMS_OF_SERVICE.md) ·
[Privacy Policy](PRIVACY_POLICY.md) ·
[Audit status](AUDIT_STATUS.md)

<br/>
<br/>

<sub>© 2026 Phobia Wallet · by <b>the fear</b></sub>

</div>
