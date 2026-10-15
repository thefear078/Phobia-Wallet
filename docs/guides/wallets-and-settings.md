# Wallets and settings

<!-- TOC -->
- [The home screen](#the-home-screen)
- [Several wallets](#several-wallets)
- [Removing and restoring a wallet](#removing-and-restoring-a-wallet)
- [Finding a setting](#finding-a-setting)
- [The interface](#the-interface)
- [Security switches](#security-switches)
- [Privacy switches](#privacy-switches)
- [Backup and updates](#backup-and-updates)
<!-- /TOC -->

---

## The home screen

- **Total balance** — the whole wallet, always. Picking a network above the asset list (All, Bitcoin,
  Ethereum, Solana) narrows the list, never the total. **Hide** turns every figure into dots.
- **The chart beside it** shows what the coins you hold now were worth across the window (1D, 1W, 1M,
  1Y). Move the pointer over it: each point shows its value, when it was, and the change since the start
  of the window. The last point is the live balance. It is not a record of past balances — the wallet
  keeps none — and the note under the chart says so.
- **Your assets** — biggest first; coins at zero fold behind one button, and unsolicited airdrop tokens
  (a common scam) behind another, with a warning.
- The right column holds the market, the theme swatches and what the balance is made of.

## Several wallets

A wallet is one recovery phrase. You can keep several side by side — a savings wallet, a daily one,
one imported from another app — each in its own encrypted vault on this computer.

- **Add:** Settings → Wallets → *Add another wallet* — create a new phrase or import one. A new wallet
  reuses the password you already use, so switching needs no typing.
- **Switch:** the wallet card at the top of the side menu, **Ctrl+Shift+W** for the next one, or
  Settings → Wallets → *Switch*.
- **Every wallet's balance:** Settings → Wallets → *Show every wallet's balance* lists each wallet's
  figure and their sum. A wallet that is not open is read in the background; one never read shows "—",
  not a made-up zero.
- **Name, colour and coins:** rename the open wallet, give it a colour, and choose which coins it shows.
  A wallet the app named itself ("Wallet 2") reads in your language.

## Removing and restoring a wallet

**Remove** takes two presses: the first turns the button into *Confirm removal* for six seconds, the
second takes the wallet out of the list. Its encrypted vault is **kept** on this computer under
**Removed wallets** (Settings → Wallets), where **Restore** puts it back — it opens with its own
password, as before. Only a full data wipe deletes a removed wallet's vault.

## Finding a setting

The search box at the top of Settings looks through **everything the Settings screens say** — every
title, switch and hint — in your language and in English, plus everyday words (seed, proxy, тема…).
Pick a result: its tab opens and the page scrolls to the card, which lights up for a moment.

## The interface

Settings → **Appearance**:

- **Language** (English, Українська, Русский, 中文, Español, Deutsch) and **display currency**.
- **Theme** — Phobia's midnight violet by default, Honey gold and others; it switches at once.
- **Interface:**
  - **"Back to top" button** — a round button that fades in at the bottom right once a page is scrolled
    far down; one click glides back up. Turn it off here.
  - **Closed notes** — the explanations on Swap, Buy crypto, Activity, Staking and Connect close with ✕.
    *Show again* brings every closed note back.
- **Motion** — animations, stickers and light effects, each on its own switch.
- **Lock screen**, **profile** and **sidebar** look.

## Security switches

Settings → **Security**:

- **Auto-lock** after a chosen idle time; **lock on minimise** (Privacy tab).
- **Hide balances by default.**
- **Password before every send** — on by default. Staking actions ask for it too. Switching it off
  takes the password; switching it on does not.
- **Security Center** — what is actually protecting the wallet right now, and one button for the
  recommended protection.
- **Duress password** — a second password that opens a decoy wallet.
- **PSBT** — review and sign Bitcoin transactions prepared elsewhere (hardware-wallet workflows).
- **Sign / verify a message.**

## Privacy switches

Settings → **Privacy**:

- **Tor** — the bundled Tor client; **Tor-only** refuses any request that cannot go through Tor.
- **Custom proxy** (SOCKS5), **IP version**, **which server each network is read from**, **Monero node**.
- **Who this wallet talks to** — every server, why, and what it learns.

## Backup and updates

- Settings → **Backup**: save an encrypted backup file and **check** it with its password; reveal the
  recovery phrase (password required, screen capture blocked while it is shown).
- Settings → Appearance → **Updates**: the wallet looks for a new release by itself, downloads it and
  keeps it only if its checksum matches both the release's `SHA256SUMS` and GitHub's own digest.
  Installing is always your click.

---

📖 [User guides](README.md) · [Documentation Index](../INDEX.md)
