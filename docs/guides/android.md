# Phobia on Android

The Android app is the same wallet as the desktop — the same keys, the same signing code, the same
coins and the same screens — laid out for a phone. A wallet made on the phone restores on the desktop
from its recovery phrase, and the other way round.

It is a **beta**, installed from the release page (an APK), not from a store.

<!-- TOC -->
- [Install](#install)
- [The home screen](#the-home-screen)
- [Moving around](#moving-around)
- [What the phone does not do yet](#what-the-phone-does-not-do-yet)
- [Security on a phone](#security-on-a-phone)
- [Updating](#updating)
- [Questions](#questions)
<!-- /TOC -->

---

<img src="../assets/screenshot-android-home-v410.png" width="32%" alt="Home"/> <img src="../assets/screenshot-android-assets-v410.png" width="32%" alt="Assets and market"/> <img src="../assets/screenshot-android-quick-v410.png" width="32%" alt="Quick actions"/>

## Install

1. On the phone, open the **[Beta 1 release page](https://github.com/thefear078/Phobia-Wallet/releases/tag/v4.10.0-beta.1)**
   and download **`PhobiaWallet-Beta-1-android.apk`**. Android 7.0 or newer, 64-bit or 32-bit ARM —
   practically every phone from the last eight years.
2. Check the file before you open it. Its SHA-256 must match the `PhobiaWallet-Beta-1-android.apk` line
   of `SHA256SUMS-Beta-1.txt` from the same release. Easiest on a computer:
   ```bash
   sha256sum -c SHA256SUMS-Beta-1.txt --ignore-missing
   gh attestation verify PhobiaWallet-Beta-1-android.apk --repo thefear078/Phobia-Wallet
   ```
   then copy the checked file to the phone.
3. Open the APK. Android asks once whether your browser or file manager may install apps — allow it
   for that app, install, and you can switch the permission off again afterwards.
4. Open **Phobia**, set a vault password, and create a wallet or import your recovery phrase — exactly
   as in [Getting started](../getting-started.md#4-first-launch).

Phobia asks for one permission: the network. No contacts, no storage, no camera, no location.

## The home screen

From top to bottom:

- **The wallet's name** — tap it to switch wallets or add one. Beside it, the scan button opens Receive
  with its QR codes, and the bell opens Activity.
- **Total balance** with its 24-hour move, in percent and in money. The eye hides the figures. When
  something new happened since you last opened Activity, a note says so — tap it to read.
- **Portfolio** — what today's coins were worth over the last day, week, month or year. Drag a finger
  along the line to read the value at any moment; the last point is the balance above it.
- **Receive, Send, Swap, Buy.**
- **My assets** — the open wallet's coins with their 24-hour move and a 7-day line. *See all* shows the
  whole list; the chips narrow it to one network. Tap a coin for its details.
- **Market overview** — Bitcoin, Ethereum, Litecoin and Dogecoin; tap one for its chart.

## Moving around

- The bottom bar: **Home**, **Activity**, the **crystal** in the middle, **Explore** (Discover: Market,
  Buy, P2P, NFTs, news and more) and **Settings**.
- The **crystal** opens quick actions: Receive, Send, Swap, Staking, Market, Connect, and Lock vault.
- The phone's **back** gesture closes an open panel, then returns to Home; at Home it leaves the app.
- Every page — Send, Swap, Staking, Connect, Security Center, Settings — is the desktop's page, with
  the same checks and the same password before anything is signed. The guides for
  [Swap](swap.md), [Staking](staking.md), [Connect](connect.md) and
  [Wallets and settings](wallets-and-settings.md) apply as written.

The phone starts in the **Ice** theme (Phobia blue). Settings → Appearance has all the others.

## What the phone does not do yet

- **No Tor.** Tor is bundled with the Windows and Linux builds only. On the phone every chain is read
  directly, so the servers it asks see your IP address — Settings says so at the top. If that matters
  for a payment, make it from the desktop with Tor on.
- **No Monero wallet service.** Monero balances and sends need Monero's own wallet program, which ships
  with the desktop builds. On the phone XMR has its address; its balance and sends need the desktop for
  now.
- **No automatic updates.** Install a newer APK over the old one (see below).
- **Not in a store.** Only the release page carries the APK. A "Phobia" in any app store is not this
  one.

## Security on a phone

- The seed and keys are encrypted with your vault password (Argon2id → AES-256-GCM), exactly as on
  the desktop, and stay in the app's private storage.
- **Android's backup is off** for Phobia: the vault never goes to a cloud backup. Your recovery phrase
  is the backup — write it on paper.
- **Seed and Monero key screens are protected**: no screenshots, no screen recording, and the
  recent-apps preview is blank while they are open.
- A phone is lost or stolen more easily than a computer. Use a screen lock, keep a strong vault
  password, and keep large amounts on a hardware wallet.

## Updating

Download the newer APK from its release page, check it the same way, and open it: Android installs it
over the old one and the wallet keeps its data.

Android only installs an update that is **signed with the same key** as the copy on the phone. If
Android says the app "conflicts with an existing package", the new APK was signed with a different key:
make sure your recovery phrase is written down, uninstall Phobia, install the new APK and import the
phrase. The release notes say when that is needed.

## Questions

**Is the phone app a different wallet?** No. Same recovery phrase, same addresses, same coins. Import the
phrase on the phone and you see the desktop's wallet.

**Can I use the phone and the desktop at the same time?** Yes — both read the same chains. Each keeps its
own vault, settings and notes.

**Why is the APK so big?** It carries the .NET runtime and both ARM builds, and nothing is trimmed: the
wallet reads its own files with code that trimming would break.

---

📖 [User guides](README.md) · [Documentation Index](../INDEX.md)
