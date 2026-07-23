# CoinJoin (Wasabi / WabiSabi)

This module defines types and stubs for Bitcoin CoinJoin via the public Wasabi coordinator.

## Why stubs?

Full WabiSabi requires:

- Blind signature credential issuance (amount + vsize)
- Ownership proofs for UTXOs
- Anonymous Tor transport to the `.onion` coordinator
- Transaction signing coordination across peers

Those pieces live in [Wasabi Wallet](https://github.com/WalletWasabi/WalletWasabi) / BTCPayServer plugins — not a thin browser client.

## Intended integration

1. Run Tor (or Tor Browser) so `WASABI_COORDINATOR` is reachable.
2. Proxy coordinator HTTP through SOCKS5 (`socks5://127.0.0.1:9050`).
3. Replace stubs in `coinjoin.ts` with calls to a local wasabi-backend or BTCPayServer CoinJoin API.
4. Keep UTXO selection and signing on-device; never send spend keys to Umbra servers.

## Public coordinator

```
http://wasabiukrxmkdgve5kynjztuovbg43uxcbcxn6y2okcrsg7gb6jdmbad.onion
```
