# Bundled monero-wallet-rpc

Monero is the one coin whose balance cannot be read from a public explorer — amounts are hidden
on-chain, so a balance only exists after scanning with your view key, and spending requires
RingCT + Bulletproofs. Re-implementing that would be reckless, so Umbrella ships **Monero's own
audited wallet daemon** and drives it over JSON-RPC (`MoneroRpcService`).

## Contents (not committed)

| File                    | Purpose                                     |
| ----------------------- | ------------------------------------------- |
| `monero-wallet-rpc.exe` | Official Monero wallet daemon (~39 MB)      |

Git-ignored like the Tor binaries — see `.gitignore`. The build copies whatever is here into the
output and the installer packages it.

## Fetching it

```powershell
pwsh desktop/scripts/fetch-monero.ps1
```

Downloads the official CLI bundle from `downloads.getmonero.org`, extracts just
`monero-wallet-rpc.exe`, and verifies it runs.

Verified with **Monero 'Fluorine Fermi' v0.18.5.1**.

## How it is used

1. The wallet is restored with `generate_from_keys` from the address + secret spend/view keys that
   `MoneroKeys` derives from the vault seed. **The keys never leave this machine** — they go to a
   local process on `127.0.0.1:18099` only.
2. It syncs against a public remote node (and through Tor when Tor is on, via `--proxy`).
3. `get_balance` gives the real balance; `transfer` builds, signs and relays a real transaction.

This also independently validates our own derivation: the daemon reconstructs **exactly the same
primary address** that `MoneroKeys` produces from the same keys.

## Licence

Monero is distributed under the BSD 3-clause licence by The Monero Project, redistributed
unmodified.
