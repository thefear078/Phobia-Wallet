/**
 * High-level seed phrase flows: create / import / reveal / remove.
 * Mnemonic lives only in memory and the encrypted IndexedDB vault.
 * Everything is scoped to the authenticated account (userId) so multiple
 * accounts on one device never see each other's seed.
 */

import { api } from "@/lib/api/client";
import {
  generateMnemonicPhrase,
  validateMnemonicPhrase,
  deriveAddress,
  DERIVABLE_CHAINS,
} from "./walletCore";
import {
  encryptSeed,
  decryptSeed,
  loadVault,
  hasVault,
  clearVault,
  loadLegacyVault,
  hasLegacyVault,
  migrateLegacyVault,
} from "./vault";

const SEED_SKIP_KEY = "umbra.seed.skipped.v2";
const SEED_LABEL = "Umbrella Seed";

function skipKey(userId: string): string {
  return `${SEED_SKIP_KEY}:${userId}`;
}

export function hasSkippedSeedSetup(userId: string): boolean {
  if (typeof window === "undefined" || !userId) return true;
  try {
    return window.localStorage.getItem(skipKey(userId)) === "1";
  } catch {
    return true;
  }
}

export function markSeedSetupSkipped(userId: string) {
  try {
    window.localStorage.setItem(skipKey(userId), "1");
  } catch {
    /* ignore */
  }
}

export function clearSeedSetupSkipped(userId: string) {
  try {
    window.localStorage.removeItem(skipKey(userId));
  } catch {
    /* ignore */
  }
}

export { validateMnemonicPhrase };

/** Account-scoped check — a legacy (unclaimed) vault does NOT count. */
export function hasSeedVault(userId: string): Promise<boolean> {
  return hasVault(userId);
}

/** An old single-slot vault exists on this device and can be claimed with its password. */
export function hasUnclaimedDeviceVault(): Promise<boolean> {
  return hasLegacyVault();
}

/** New wallets use a 24-word (256-bit) phrase. Import still accepts 12/15/18/21/24. */
export function generateSeedPhrase(): string {
  return generateMnemonicPhrase(256);
}

/** Link derived receive-addresses (ETH, BTC, SOL, TRON) as watch-only so balances show up. */
export async function linkDerivedAddresses(mnemonic: string): Promise<void> {
  for (const chain of DERIVABLE_CHAINS) {
    try {
      const address = deriveAddress(mnemonic, chain);
      await api.linkWallet({ chain, address, label: SEED_LABEL, watchOnly: true });
    } catch {
      // already linked or backend rejected — non-fatal for onboarding
    }
  }
}

/** Encrypt + persist a freshly confirmed mnemonic, then link its addresses. */
export async function saveSeedPhrase(
  mnemonic: string,
  password: string,
  userId: string,
): Promise<void> {
  if (!validateMnemonicPhrase(mnemonic)) {
    throw new Error("Invalid seed phrase");
  }
  await encryptSeed(mnemonic.normalize("NFKD").trim(), password, userId);
  clearSeedSetupSkipped(userId);
  await linkDerivedAddresses(mnemonic);
}

/** Import an existing phrase typed by the user. */
export async function importSeedPhrase(
  rawPhrase: string,
  password: string,
  userId: string,
): Promise<void> {
  const mnemonic = rawPhrase.normalize("NFKD").trim().replace(/\s+/g, " ").toLowerCase();
  if (!validateMnemonicPhrase(mnemonic)) {
    throw new Error("Phrase failed BIP39 validation — check the words and their order");
  }
  await saveSeedPhrase(mnemonic, password, userId);
}

/**
 * Claim the pre-scoping device vault for this account. The password proves
 * ownership: only a successful decrypt migrates the record.
 */
export async function unlockDeviceVault(password: string, userId: string): Promise<void> {
  const blob = await loadLegacyVault();
  if (!blob) throw new Error("No unclaimed seed phrase on this device");
  let mnemonic: string;
  try {
    mnemonic = await decryptSeed(blob, password);
  } catch {
    throw new Error("Incorrect password");
  }
  await migrateLegacyVault(userId);
  clearSeedSetupSkipped(userId);
  await linkDerivedAddresses(mnemonic);
}

/** Decrypt the stored phrase; throws when the password is wrong or vault is empty. */
export async function revealSeedPhrase(password: string, userId: string): Promise<string> {
  const blob = await loadVault(userId);
  if (!blob) throw new Error("No seed phrase stored on this device");
  try {
    return await decryptSeed(blob, password);
  } catch {
    throw new Error("Incorrect password");
  }
}

/** Remove this account's encrypted vault from the device only (funds stay on-chain). */
export async function removeSeedFromDevice(userId: string): Promise<void> {
  await clearVault(userId);
}
