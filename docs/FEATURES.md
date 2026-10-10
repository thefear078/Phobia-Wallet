# Feature tour

What Phobia does, screen by screen — the long version of the README's feature list. What each chain can
do is in [12-coins-and-chains.md](12-coins-and-chains.md); how it is built, in [architecture.md](architecture.md).

- [Monero, Bitcoin and USDT in one vault](#monero-bitcoin-and-usdt-in-one-vault)
- [Tor is bundled, not assumed](#tor-is-bundled-not-assumed)
- [You choose which server sees your addresses](#you-choose-which-server-sees-your-addresses)
- [Security Center](#security-center)
- [Sending safely](#sending-safely)
- [Market](#market) · [Portfolio](#portfolio) · [Activity](#activity)
- [Swaps](#swaps) · [Staking](#staking)
- [Themes and languages](#themes-and-languages)
- [What it costs](#what-it-costs)

---

## Monero, Bitcoin and USDT in one vault

Not a Monero wallet with Bitcoin bolted on, and not a Bitcoin wallet that shows XMR as "coming soon".
Eighteen chains share one encrypted vault — plus the Ethereum networks that use your `0x` address — and
every one of them can receive, show its balance, send and read its history.

Monero runs the real `monero-wallet-rpc` on your machine, over loopback, behind a random login, because no
explorer can compute a Monero balance for you. Bitcoin is a full HD wallet: native SegWit, every used
address found, coin control, Taproot coins from a restored wallet spent, PSBT and PayJoin.

## Tor is bundled, not assumed

One switch. The wallet starts its own Tor client on port 9250 (or the next free port) — separate from a Tor
Browser you may already run — and routes balance lookups, price fetches and broadcasts through it, each
kind of request on a circuit of its own, so the exit that saw your address is not the one that sees you
spend from it. Names are resolved by Tor, never by your computer's DNS.

The **kill-switch** fails closed: when Tor-only is on, a request that cannot go through Tor does not go at
all, and the wallet brings Tor up by itself at launch so that never leaves you offline. The Android app
carries the same Tor; [Orbot](https://orbot.app) works too — see [the Android guide](guides/android.md).

## You choose which server sees your addresses

A wallet cannot read a chain by itself — it has to ask somebody, and on a transparent chain asking means
*saying the address*. Tor hides your IP; it does not un-send an address. So **Settings → Privacy** names
every server the wallet talks to and what each one learns, and lets you point a chain somewhere else — a
different company, or a node you run.

Two rules are enforced rather than suggested: a `.onion` node is never used without Tor and never silently
swapped for a clearnet one, and a plain `http://` endpoint is refused — choosing your own server *for
privacy* and then sending addresses in clear would be worse than not choosing.

## Security Center

<p align="center"><img src="assets/screenshot-security-v410.png" width="80%" alt="Security Center"/></p>

It reads live settings and reports what is *actually* protecting the wallet right now. If Tor is off, it
says your IP is visible to every explorer you use. It does not flatter — and one button turns on every
protection it scores.

## Sending safely

- **Your password again** before anything is signed.
- **Privacy Radar** reads the transaction you are about to make and tells you what it would reveal
  on-chain — chiefly whether it links coins that were never connected, which is how chain analysis
  de-anonymises people. The analysis runs offline, on your own data, for you.
- **Address poisoning** — a look-alike of an address from your history, or one of your own — is flagged
  before you send; a destination on the wrong network is refused.
- **A simulation** of the balance before and after, with the network fee named.
- An answer that never arrives is settled against the chain, **never offered as a retry** — a second send
  of "the same" payment pays twice.
- A **duress password** opens a decoy wallet.

## Market

<p align="center"><img src="assets/screenshot-market-v410.png" width="80%" alt="Market chart with candles"/></p>

An exchange chart: candles or a line, a volume band, the price axis on the right with the last price
tagged, a crosshair on both axes and the hovered candle's open, high, low and close — with KuCoin and Bybit
behind Binance, so it still draws over Tor.

## Portfolio

The chart beside the balance follows the pointer: each point is what the coins you hold now were worth at
that moment, when that was, and the move since the start — real prices, not a smoothed guess. Balances
show at once from the last read and refresh in the background; a balance that could not be read says so
rather than reading zero.

## Activity

<p align="center"><img src="assets/screenshot-activity-v410.png" width="80%" alt="Activity"/></p>

Every event has its own icon, the list runs newest first under Today / Yesterday, and every transfer shows
what it is worth. History is read from the chains themselves, so transfers made before this wallet existed
show too; private notes on transactions are encrypted; the list exports to CSV. A coin whose history cannot
be read is named, never shown as "no transactions".

## Swaps

Coins swap for one another, each on its own network, and always to your own address. The route is chosen
by trust and named before you pay: **THORChain** first (nobody holds your coins), then **NEAR Intents** (a
smart contract holds them for the swap and refunds you if it cannot be filled), and the **Exolix** exchange
last — the route for Monero, Nano, Decred and NEAR, which nothing decentralised reaches. Exolix holds the
coins for the minutes of the swap, and the screen says so in a warning colour. Details:
[guides/swap.md](guides/swap.md).

## Staking

TRX, SOL and ATOM stake from the wallet — freeze and vote on TRON, a stake account on Solana, a delegation on
the Cosmos Hub — each built, checked and signed here and confirmed with your password like a send. The
transaction TronGrid builds for TRON is decoded and compared with what you asked before it is signed.
Details: [guides/staking.md](guides/staking.md).

## Themes and languages

<table>
<tr>
<td><img src="assets/screenshot-welcome-v410.png" alt="Welcome"/></td>
<td><img src="assets/screenshot-unlock-v410.png" alt="Unlock"/></td>
</tr>
<tr>
<td><img src="assets/screenshot-receive-v410.png" alt="Receive"/></td>
<td><img src="assets/screenshot-settings-v410.png" alt="Settings"/></td>
</tr>
</table>

The default look is **Phobia's midnight violet**: a near-black page, charcoal cards, one violet accent,
crystal mountains on the horizon and the large crystal on the balance card. Motion has a switch for each
piece. **15 themes** share that design in their own colours, every one checked for WCAG AA contrast:

<table>
<tr>
<td align="center"><img src="assets/theme-gold-v410.png" alt="Honey gold"/><br/><sub>Honey gold</sub></td>
<td align="center"><img src="assets/theme-uniswap-v410.png" alt="Uniswap"/><br/><sub>Uniswap · hot pink</sub></td>
</tr>
<tr>
<td align="center"><img src="assets/theme-void-v410.png" alt="Void"/><br/><sub>Void · electric OLED</sub></td>
<td align="center"><img src="assets/theme-navy-v410.png" alt="Navy"/><br/><sub>Navy · the classic blue</sub></td>
</tr>
</table>

Six languages: English, Українська, Русский, 中文, Español, Deutsch — a test fails the build if one is
missing a string. Adding a language: [localization.md](localization.md); a theme: [theming.md](theming.md).

## What it costs

**Phobia takes no cut of your transfers.** You pay the network's own miner or validator fee and nothing
else — no platform fee, no subscription, no withdrawal fee, no account. It is funded by
[GitHub Sponsors](https://github.com/sponsors/thefear078) and bounties for specific coins or features;
later, perhaps, a small spread on swaps, which are optional in a way a send is not. No advertising and no
tracking — those are the two things that would make everything else here untrue.

---

📖 Back to the [README](../README.md) · [Documentation index](INDEX.md)
