/**
 * Monero — NOT IMPLEMENTED. Do not ship address derivation from this module.
 *
 * A previous version of this file produced addresses that LOOKED valid but were
 * not, and any XMR sent to them would have been permanently lost. It was wrong
 * in three independent ways:
 *
 *  1. It encoded with Bitcoin base58 (`@scure/base`). Monero uses its OWN base58
 *     (8-byte blocks → 11 chars each). Output was a 94-char "B…" string; real
 *     mainnet addresses are 95 chars starting with "4".
 *  2. `hashToScalar` applied ed25519 *clamping* instead of Monero's `sc_reduce32`
 *     (reduction mod the group order ℓ). A clamped scalar is not a valid Monero
 *     private key.
 *  3. `checkIncomingTx` reported `isOurs` when a keccak hash had any non-zero
 *     byte — i.e. essentially always true.
 *
 * Beyond the encoding bugs, there is a deeper problem: Monero has no BIP44 path
 * and uses its own 25-word mnemonic. Nothing derived here from a BIP39 phrase
 * would ever be restorable in monero-wallet-cli / Feather / the official GUI, so
 * even a *correctly encoded* address would still strand the user's funds.
 *
 * Correct approach (desktop client, per the brief): talk to `monero-wallet-rpc`,
 * or embed a real Monero wallet library, and use Monero's own seed. View-only
 * monitoring should use a real view key exported from that wallet.
 */

export class MoneroNotSupportedError extends Error {
  constructor(
    message = "Monero is not supported in the web client. Use the desktop client with monero-wallet-rpc.",
  ) {
    super(message);
    this.name = "MoneroNotSupportedError";
  }
}

/** @deprecated Never returns — Monero cannot be derived from a BIP39 seed. */
export function deriveMoneroKeys(_seed: Uint8Array): never {
  throw new MoneroNotSupportedError(
    "Monero keys cannot be derived from a BIP39 seed — XMR uses its own 25-word mnemonic.",
  );
}

/** @deprecated Never returns — requires Monero's own base58, not Bitcoin base58. */
export function encodeMoneroAddress(_spendPub: Uint8Array, _viewPub: Uint8Array): never {
  throw new MoneroNotSupportedError(
    "Monero address encoding requires Monero base58 (8-byte blocks) — not implemented.",
  );
}

/** @deprecated Never returns — real output scanning needs full RingCT/ECDH. */
export function checkIncomingTx(
  _viewKey: Uint8Array,
  _txPubKey: Uint8Array,
  _outputIndex: number,
): never {
  throw new MoneroNotSupportedError(
    "Monero output scanning requires full RingCT support — not implemented.",
  );
}
