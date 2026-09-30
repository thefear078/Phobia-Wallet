# 07 — Network Fees & Costs

Phobia Wallet is **non-custodial**. Users pay only **blockchain network fees** (miner/validator
costs). There are no subscriptions and no hidden platform charges in the product documentation.

## What you pay when sending

| Chain | Typical cost driver |
|-------|---------------------|
| BTC / LTC | Miner fee (vbytes × fee rate) |
| ETH | Gas (base + priority) |
| SOL | Small fixed lamport fee |
| XMR | Dynamic fee chosen by `monero-wallet-rpc` |
| TRX / USDT-TRC20 | Energy or burned TRX (see below) |

All sends show an estimated network fee in the review step before you confirm.

## Why TRC-20 (USDT on Tron) can be expensive

This is **not** a wallet-specific charge — it is how the Tron network prices smart-contract calls:

- USDT transfers consume **Energy**.
- Without staked TRX, the network **burns TRX** instead.

| Scenario | Approx. cost |
|----------|----------------|
| USDT to an address that already holds USDT | ~13 TRX burned |
| USDT to a fresh address | ~27–30 TRX burned |
| Wallet with enough staked TRX | Energy only (no burn) |

**Mitigations:** stake TRX for Energy, use ERC-20 USDT on Ethereum, or use USDC on Solana for
lower fees.

## Exchange quotes

The web Exchange view shows rates from public market APIs. Any service line shown in the UI is
displayed **before** you confirm — there are no post-confirm surprises.

## Legal note

Nothing in this document is financial or tax advice. You are solely responsible for your
transactions and their outcomes.
