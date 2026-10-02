# Connect

**Connect** shows money the wallet does not hold the keys to: other addresses you want to watch, and
exchange accounts read through a **read-only** API key. Nothing on this screen can move funds — there
is no code that could.

At the top: how many addresses are watched, how many exchanges are connected, and what all of it is
worth. **Addresses** and **Exchanges** are separate tabs.

<!-- TOC -->
- [Watch an address](#watch-an-address)
- [Connect an exchange](#connect-an-exchange)
- [Where to create a read-only key](#where-to-create-a-read-only-key)
- [Privacy](#privacy)
<!-- /TOC -->

---

## Watch an address

1. Open **Discover → Connect**, tab **Addresses**.
2. Paste the **public address** — from MetaMask, a Ledger, an explorer, a cold wallet. The network is
   recognised from the address itself (0x… is Ethereum, bc1… Bitcoin, T… TRON); pick it from the list
   only when the shape is ambiguous.
3. Give it a label if you like ("Cold storage", "Ledger") and press **Link address**.

The address appears in the list with what it holds now and its value, and its coins join your total and
your assets as *watched*. TRON and Ethereum addresses also show their tokens. **Remove** takes it out
again. Never paste a private key or a recovery phrase here — the field only ever needs the public
address.

## Connect an exchange

1. Tab **Exchanges**. Pick the exchange from the tiles: Binance, Bybit, OKX, Kraken, KuCoin, Gate.io,
   MEXC, Bitget or Telegram CryptoBot.
2. On the exchange, create an API key with **read / view permission only** — never trading, never
   withdrawal. The hint under the tiles says where.
3. Paste the key (and the secret; OKX, KuCoin and Bitget also ask for the passphrase you set when
   creating it; CryptoBot uses a single token) and press **Connect exchange**.

The wallet checks the key against the exchange before saving it, encrypts it on this computer with a key
derived from your recovery phrase, and sends it only to that exchange. The balances join your total.
**Disconnect** removes the key.

## Where to create a read-only key

| Exchange | Where | Permission to tick |
|---|---|---|
| Binance | API Management → Create API | Enable Reading — nothing else |
| Bybit | API → Create New Key → System-generated | Read-Only |
| OKX | API → Create API key | Read — and note the passphrase |
| Kraken | Settings → API → Create key | Query Funds |
| KuCoin | API Management → Create API | General (read) — and note the passphrase |
| Gate.io | API Management → Create API key | Read only |
| MEXC | API Management → Create | Account read |
| Bitget | API Management → Create | Read-only — and note the passphrase |
| Telegram CryptoBot | @CryptoBot → Crypto Pay → Create App | The app's API token |

## Privacy

A watched address is read from the same public servers as your own — they learn the address, Tor hides
your IP. An exchange already knows who you are; the wallet tells it nothing beyond the key's own
requests for balances.

---

📖 [User guides](README.md) · [Documentation Index](../INDEX.md)
