# Swap

Swap any coin the open wallet holds for any coin it can receive, each on its own network. The coins
you get always arrive at **your own address in this wallet** — never at an exchange account.

<!-- TOC -->
- [The three routes](#the-three-routes)
- [Step by step](#step-by-step)
- [Following a swap](#following-a-swap)
- [What can be swapped](#what-can-be-swapped)
- [Privacy](#privacy)
- [When something goes wrong](#when-something-goes-wrong)
<!-- /TOC -->

---

## The three routes

The wallet picks the route by **trust**, tries them in this order, and names the one it chose before
you pay:

| Route | Who holds your coins during the swap | Notes |
|---|---|---|
| **THORChain** | Nobody — the network itself swaps and pays out | Bitcoin, Litecoin, Dogecoin, Bitcoin Cash, Ethereum and Cosmos can pay; many coins can be received |
| **NEAR Intents** (1Click) | A smart contract, which refunds you if the swap cannot be filled | Adds a 0.25% fee (no partner key); covers Solana, TRON and its USDT, XRP, Cardano, Zcash, Stellar, TON, BNB, Avalanche, Polygon, the Ethereum networks and their stablecoins |
| **Exolix** | An exchange, for the minutes the swap takes | Used **only** for Monero, Nano and Decred, which nothing decentralised reaches; the screen shows it in a warning colour |

When one route cannot quote a pair, the next is asked. When none can, the screen shows each route's
own reason.

## Step by step

1. Open **Swap** in the sidebar.
2. **You pay:** pick the coin. The picker shows the coin and its network ("USDT · TRON") and only
   what this wallet can pay with. The wallet's name is at the top right; the balance is under the
   amount — click it to use the whole balance.
3. **You receive:** pick the coin to get.
4. Type the amount and press **Get quote**. The review shows what you get, the route, the fees, the
   expected time and the minimum you will accept.
5. Press **Confirm**:
   - A **THORChain** swap from BTC, LTC, DOGE, BCH or ETH is signed and sent right in the Swap screen.
   - **Every other route** opens the payment on the **Send** screen, already filled in — coin, deposit
     address, amount and memo — under a banner that says what it buys and through whom. You review it
     and confirm with your password like any send.
6. The rate is checked again at the moment you confirm. **A swap is final once it is paid.**

The note at the top of Swap explaining the routes can be closed with ✕;
**Settings → Appearance → Interface → Closed notes** brings it back.

## Following a swap

After paying, a card on the Swap screen follows the swap until it is **done, refunded or failed**, and
the swap appears in **Activity** with a link to the route's own tracking page. You can close the card;
the swap goes on without it.

## What can be swapped

Bitcoin, Litecoin, Dogecoin, Bitcoin Cash, Zcash, Ethereum and its networks (Arbitrum, Base,
Optimism), BNB Chain, Avalanche, Polygon, TRON and its USDT, Solana and its USDC, XRP, Cardano, Stellar,
TON and its USDT, Cosmos Hub, NEAR, Monero, Nano and Decred.

Limits of this build:

- **Polkadot** has no route that answers right now.
- **Decred** can be received from a swap but cannot pay for one (the wallet does not send Decred yet).

## Privacy

Every route learns the **address you pay from and the address you receive at** — it needs both to do
the swap. Tor hides your IP from it; it does not hide the addresses. Both NEAR Intents and Exolix are in
**Settings → Privacy → who this wallet talks to**.

## When something goes wrong

| What you see | What it means |
|---|---|
| "No route swaps X for Y right now" | None of the three quoted the pair. Try again later or a different pair. |
| The quote changed at Confirm | Prices moved; the new figure is what you will get. |
| The swap card says **refunded** | The route could not fill it and sent your coins back to the paying address. |
| Exolix shows in a warning colour | That route holds your coins for the minutes of the swap. It is used only where no decentralised route exists. |

---

📖 [User guides](README.md) · [Documentation Index](../INDEX.md)
