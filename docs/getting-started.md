# Getting started

Install Phobia Wallet, verify the download, create or import a wallet, and make a first small send
safely.

<!-- TOC -->
- [1. Download](#1-download)
- [2. Verify the file](#2-verify-the-file)
- [3. Install or run portable](#3-install-or-run-portable)
- [4. First launch](#4-first-launch)
- [5. Create or import a wallet](#5-create-or-import-a-wallet)
- [6. Turn on Tor (recommended)](#6-turn-on-tor-recommended)
- [7. Receive and send](#7-receive-and-send)
- [8. Backup](#8-backup)
- [9. Next reading](#9-next-reading)
<!-- /TOC -->

---

## 1. Download

The current build is the **Beta**, a GitHub *pre-release*: get it from its
**[release page](https://github.com/thefear078/Phobia-Wallet/releases/tag/v4.10.0-beta.5)**. (GitHub's "Latest" label points at the last full release, 4.9.0, until a
full 4.10.0 is out.)

| Platform | File |
|---|---|
| Windows installer | `PhobiaWallet-Setup-Beta.exe` |
| Windows portable | `PhobiaWallet-Beta-win-x64-portable.exe` — one file, no install |
| Linux | `PhobiaWallet-Beta-linux-x64.tar.gz` — Tor and Monero included |
| Android | `PhobiaWallet-Beta-android.apk` — Android 7.0+, see the [Android guide](guides/android.md) |

Also download **`SHA256SUMS-Beta.txt`** from the same release. A full release names its files by
version instead (`PhobiaWallet-Setup-4.10.0.exe`, `SHA256SUMS-4.10.0.txt`). Files with a number in the
name (`…-Beta-5…`) are the same bytes, kept for older copies' updaters — you can ignore them.

## 2. Verify the file

Do not run an unsigned copy you cannot check.

```powershell
# Windows PowerShell — compare to the matching line in SHA256SUMS-Beta.txt
Get-FileHash .\PhobiaWallet-Setup-Beta.exe -Algorithm SHA256
```

```bash
# Linux / macOS
sha256sum -c SHA256SUMS-Beta.txt --ignore-missing
# and who built it — every file carries a GitHub build attestation
gh attestation verify PhobiaWallet-Beta-linux-x64.tar.gz --repo thefear078/Phobia-Wallet
```

Details: [SECURITY.md — Verifying what you run](../SECURITY.md#verifying-what-you-run).

## 3. Install or run portable

- **Installer:** choose a folder; wallet data lives under `%APPDATA%\UmbrellaWallet` (Windows).
- **Portable:** run next to its folder; data typically stays in `data/` beside the binary — useful when
  you do not want an installed footprint.

Linux: unpack the tarball and run `./phobia-wallet`; `./install-desktop-entry.sh` adds it to your
application menu. (Releases before 4.10.0 named the program `Umbrella.Wallet.App`.)

Android: open the APK on the phone and allow your browser or file manager to install it once
(Android asks). The [Android guide](guides/android.md) walks through it, and through what the phone
app does not do yet.

## 4. First launch

You will set a **vault password** (unlocks the encrypted vault on this device). This is **not** your
24-word recovery phrase.

Read and accept the terms when prompted (see [TERMS_OF_SERVICE.md](../TERMS_OF_SERVICE.md)). You must
be **18+**.

## 5. Create or import a wallet

**Create:** the app generates a BIP39 phrase with the OS CSPRNG. Write all words on **paper**. Confirm
the random word checks. Anyone with those words has the funds; if you lose them and the device, nobody
can recover them — including us.

**Import:** paste a BIP39 phrase from another wallet. Addresses will match the original derivation
paths Phobia supports. Two other formats import as single-coin wallets:

- a **TON phrase** (Telegram Wallet, Tonkeeper, TON Space — 24 words) → a Toncoin-only wallet;
- a **Monero seed** (Monero GUI, Feather, Cake Wallet, MyMonero — 25 words, in any of Monero's 12
  languages, Chinese included) → a Monero-only wallet. Give the day the wallet was made (or its restore
  height) in "Scan from" so the first scan does not read the whole chain.

## 6. Turn on Tor (recommended)

Before you unlock (or immediately after), open **Settings → Privacy** and enable Tor. Optional:
**Tor-only kill-switch** so clearnet is refused if Tor is down; with it on, the wallet starts Tor by
itself at every launch.

Tor hides your **IP** from explorers. It does **not** make Bitcoin/Ethereum private. See
[PRIVACY.md](../PRIVACY.md).

## 7. Receive and send

1. **Receive:** pick the coin, confirm the **network** under the name, show QR / address. On UTXO
   chains, prefer a fresh address when the wallet offers one.
2. **Send a tiny test amount first.** Blockchain transfers are final.
3. The amount box takes the **coin**, and says so on its right-hand side. The switch under it changes
   the box to your display currency; the line beside the switch always shows the same amount the other
   way round. Look at the unit before you look at the number.
4. **Continue** opens the review over the page: check the amount, the whole destination, the fee, and
   any warning in amber — a fee larger than the amount usually means the amount is in the wrong unit.
   Type your password and confirm.
5. The receipt says how it ended (*confirmed*, *waiting for a block*, or *no clear answer — check before
   sending again*) and can be copied as text or saved as a picture.

Every coin in the wallet sends.

## 8. Backup

Export an **encrypted** backup from Settings and store it offline. Prefer verifying the backup before
you rely on it. The paper seed remains the ultimate recovery path.

## 9. Next reading

| Topic | Document |
|---|---|
| Swap any coin for any coin | [guides/swap.md](guides/swap.md) |
| Stake TRX, SOL and ATOM | [guides/staking.md](guides/staking.md) |
| Watch addresses, connect exchanges | [guides/connect.md](guides/connect.md) |
| Several wallets, Settings, the interface | [guides/wallets-and-settings.md](guides/wallets-and-settings.md) |
| The whole wallet in Ukrainian | [guides/user-guide-uk.md](guides/user-guide-uk.md) |
| Philosophy | [MANIFESTO.md](../MANIFESTO.md) |
| Threats | [THREAT_MODEL.md](../THREAT_MODEL.md) |
| Problems | [troubleshooting.md](troubleshooting.md) |
| Full doc map | [INDEX.md](INDEX.md) |

---

📖 Back to [Documentation Index](INDEX.md)
