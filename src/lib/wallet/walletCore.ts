/**
 * BIP39/BIP44 derivation — mnemonic never leaves this module except via vault encrypt.
 */

import { HDKey } from "@scure/bip32";
import {
  generateMnemonic as bip39GenerateMnemonic,
  mnemonicToSeedSync,
  validateMnemonic as bip39ValidateMnemonic,
} from "@scure/bip39";
import { wordlist } from "@scure/bip39/wordlists/english";
import { base58 } from "@scure/base";
import { keccak_256 } from "@noble/hashes/sha3.js";
import { sha256, sha512 } from "@noble/hashes/sha2.js";
import { hmac } from "@noble/hashes/hmac.js";
import { ripemd160 } from "@noble/hashes/legacy.js";
import { secp256k1 } from "@noble/curves/secp256k1.js";
import { ed25519 } from "@noble/curves/ed25519.js";

/** Thrown when a chain has no safe derivation path (e.g. Monero from BIP39). */
export class UnsupportedChainError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "UnsupportedChainError";
  }
}

export type WalletChain = "ethereum" | "bitcoin" | "solana" | "tron" | "monero";

/**
 * Chains for which a fresh wallet derives a receive address.
 *
 * Monero is deliberately EXCLUDED: it has no BIP44 path, uses its own base58 and
 * its own 25-word seed, so nothing we derive from a BIP39 phrase would be
 * restorable in a real Monero wallet. Showing such an address would silently
 * burn funds. XMR support belongs in the desktop client via monero-wallet-rpc.
 */
export const DERIVABLE_CHAINS: WalletChain[] = ["ethereum", "bitcoin", "solana", "tron"];

export function generateMnemonic(strength: 128 | 256 = 128): string {
  return generateMnemonicPhrase(strength);
}

export function validateMnemonic(mnemonic: string): boolean {
  return validateMnemonicPhrase(mnemonic);
}

export function generateMnemonicPhrase(strength: 128 | 256 = 128): string {
  return bip39GenerateMnemonic(wordlist, strength);
}

export function validateMnemonicPhrase(mnemonic: string): boolean {
  return bip39ValidateMnemonic(mnemonic.normalize("NFKD"), wordlist);
}

function seedFromMnemonic(mnemonic: string): Uint8Array {
  if (!validateMnemonicPhrase(mnemonic)) {
    throw new Error("Invalid mnemonic phrase");
  }
  return mnemonicToSeedSync(mnemonic.normalize("NFKD"));
}

function ethAddressFromPublicKey(compressedPub: Uint8Array): string {
  // Decompress the public key point — getPublicKey() expects a *private* key.
  const uncompressed = secp256k1.Point.fromBytes(compressedPub).toBytes(false);
  const hash = keccak_256(uncompressed.slice(1));
  const addr = hash.slice(-20);
  return `0x${[...addr].map((b) => b.toString(16).padStart(2, "0")).join("")}`;
}

function btcP2pkhAddress(pubkey: Uint8Array): string {
  const h160 = ripemd160(sha256(pubkey));
  const versioned = new Uint8Array(1 + h160.length);
  versioned[0] = 0x00;
  versioned.set(h160, 1);
  const checksum = sha256(sha256(versioned)).slice(0, 4);
  const payload = new Uint8Array(versioned.length + 4);
  payload.set(versioned);
  payload.set(checksum, versioned.length);
  return base58.encode(payload);
}

const HARDENED_OFFSET = 0x80000000;

/**
 * SLIP-0010 ed25519 master + hardened child derivation.
 * ed25519 supports hardened derivation only, so every path segment is hardened.
 */
function slip10Ed25519(seed: Uint8Array, path: number[]): Uint8Array {
  let I = hmac(sha512, new TextEncoder().encode("ed25519 seed"), seed);
  let key = I.slice(0, 32);
  let chainCode = I.slice(32);
  for (const idx of path) {
    const hardIdx = (idx | HARDENED_OFFSET) >>> 0;
    const data = new Uint8Array(1 + 32 + 4);
    data[0] = 0x00;
    data.set(key, 1);
    data[33] = (hardIdx >>> 24) & 0xff;
    data[34] = (hardIdx >>> 16) & 0xff;
    data[35] = (hardIdx >>> 8) & 0xff;
    data[36] = hardIdx & 0xff;
    I = hmac(sha512, chainCode, data);
    key = I.slice(0, 32);
    chainCode = I.slice(32);
  }
  return key;
}

/** Real Solana address: ed25519 pubkey (base58) from SLIP-0010 path m/44'/501'/0'/0'. */
function solanaAddress(seed: Uint8Array, index: number): string {
  const priv = slip10Ed25519(seed, [44, 501, 0, index]);
  const pub = ed25519.getPublicKey(priv);
  return base58.encode(pub);
}

/** TRON address: base58check(0x41 ‖ keccak256(uncompressedPub[1:])[-20:]) from m/44'/195'/0'/0/index. */
function tronAddress(compressedPub: Uint8Array): string {
  const uncompressed = secp256k1.Point.fromBytes(compressedPub).toBytes(false);
  const hash = keccak_256(uncompressed.slice(1));
  const addr20 = hash.slice(-20);
  const versioned = new Uint8Array(21);
  versioned[0] = 0x41;
  versioned.set(addr20, 1);
  const checksum = sha256(sha256(versioned)).slice(0, 4);
  const payload = new Uint8Array(25);
  payload.set(versioned);
  payload.set(checksum, 21);
  return base58.encode(payload);
}

export function deriveAddress(mnemonic: string, chain: WalletChain, index = 0): string {
  const seed = seedFromMnemonic(mnemonic);
  const root = HDKey.fromMasterSeed(seed);

  switch (chain) {
    case "ethereum": {
      const child = root.derive(`m/44'/60'/0'/0/${index}`);
      if (!child.publicKey) throw new Error("Failed to derive Ethereum key");
      return ethAddressFromPublicKey(child.publicKey);
    }
    case "bitcoin": {
      const child = root.derive(`m/44'/0'/0'/0/${index}`);
      if (!child.publicKey) throw new Error("Failed to derive Bitcoin key");
      return btcP2pkhAddress(child.publicKey);
    }
    case "solana": {
      return solanaAddress(seed, index);
    }
    case "tron": {
      const child = root.derive(`m/44'/195'/0'/0/${index}`);
      if (!child.publicKey) throw new Error("Failed to derive TRON key");
      return tronAddress(child.publicKey);
    }
    case "monero": {
      // Fail loudly rather than hand back an address that cannot receive funds.
      // The previous implementation encoded with Bitcoin base58 (Monero uses its
      // own 8-byte-block base58) and clamped instead of sc_reduce32 — it produced
      // a 94-char "B..." string; real mainnet addresses are 95 chars starting "4".
      void index;
      throw new UnsupportedChainError(
        "Monero addresses cannot be derived from a BIP39 phrase. XMR requires its own " +
          "25-word seed and monero-wallet-rpc — use the desktop client.",
      );
    }
    default: {
      const _exhaustive: never = chain;
      return _exhaustive;
    }
  }
}
