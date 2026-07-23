/**
 * Encrypted seed vault — client-only, IndexedDB. Never send mnemonic to server.
 */

import { argon2id } from "hash-wasm";

const DB_NAME = "umbra-vault";
const STORE = "vault";
/** Pre-account-scoping records lived under this single key. */
const LEGACY_VAULT_KEY = "primary";

/** Vaults are scoped per account so a second login on the same device never sees another user's seed. */
function vaultKey(userId: string): string {
  if (!userId) throw new Error("Vault access requires an authenticated user");
  return `seed:${userId}`;
}

export type EncryptedBlob = {
  ciphertext: string;
  iv: string;
  salt: string;
  version: 1;
};

type VaultRecord = EncryptedBlob;

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => {
      req.result.createObjectStore(STORE);
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error("IndexedDB open failed"));
  });
}

function idbGet<T>(key: string): Promise<T | undefined> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(STORE, "readonly");
        const req = tx.objectStore(STORE).get(key);
        req.onsuccess = () => resolve(req.result as T | undefined);
        req.onerror = () => reject(req.error);
      }),
  );
}

function idbPut(key: string, value: VaultRecord): Promise<void> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(STORE, "readwrite");
        tx.objectStore(STORE).put(value, key);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      }),
  );
}

function idbDelete(key: string): Promise<void> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(STORE, "readwrite");
        tx.objectStore(STORE).delete(key);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      }),
  );
}

function toBase64(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes));
}

function fromBase64(b64: string) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

async function deriveKey(password: string, salt: Uint8Array): Promise<CryptoKey> {
  const hashHex = await argon2id({
    password,
    salt,
    parallelism: 1,
    iterations: 3,
    memorySize: 64 * 1024,
    hashLength: 32,
    outputType: "hex",
  });
  const keyBytes = new Uint8Array(32);
  for (let i = 0; i < 32; i++) {
    keyBytes[i] = Number.parseInt(hashHex.slice(i * 2, i * 2 + 2), 16);
  }
  return crypto.subtle.importKey("raw", keyBytes, { name: "AES-GCM" }, false, [
    "encrypt",
    "decrypt",
  ]);
}

export async function encryptSeed(
  mnemonic: string,
  password: string,
  userId: string,
): Promise<EncryptedBlob> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveKey(password, salt);
  const encoded = new TextEncoder().encode(mnemonic.normalize("NFKD"));
  const ciphertext = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, encoded);
  const blob: EncryptedBlob = {
    ciphertext: toBase64(new Uint8Array(ciphertext)),
    iv: toBase64(iv),
    salt: toBase64(salt),
    version: 1,
  };
  await saveVault(userId, blob);
  return blob;
}

export async function decryptSeed(blob: EncryptedBlob, password: string): Promise<string> {
  const salt = fromBase64(blob.salt);
  const iv = fromBase64(blob.iv);
  const key = await deriveKey(password, salt);
  const plain = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv },
    key,
    fromBase64(blob.ciphertext),
  );
  return new TextDecoder().decode(plain);
}

export async function saveVault(userId: string, blob: EncryptedBlob): Promise<void> {
  await idbPut(vaultKey(userId), blob);
}

export async function loadVault(userId: string): Promise<EncryptedBlob | null> {
  const row = await idbGet<EncryptedBlob>(vaultKey(userId));
  return row ?? null;
}

export async function hasVault(userId: string): Promise<boolean> {
  if (typeof indexedDB === "undefined" || !userId) return false;
  return Boolean(await loadVault(userId));
}

export async function clearVault(userId: string): Promise<void> {
  if (typeof indexedDB === "undefined") return;
  await idbDelete(vaultKey(userId));
}

/** Legacy single-slot vault (created before per-account scoping). */
export async function loadLegacyVault(): Promise<EncryptedBlob | null> {
  if (typeof indexedDB === "undefined") return null;
  const row = await idbGet<EncryptedBlob>(LEGACY_VAULT_KEY);
  return row ?? null;
}

export async function hasLegacyVault(): Promise<boolean> {
  return Boolean(await loadLegacyVault());
}

/**
 * Claim the legacy vault for `userId`. Ownership must be proven first by
 * successfully decrypting it with the user's encryption password.
 */
export async function migrateLegacyVault(userId: string): Promise<void> {
  const blob = await loadLegacyVault();
  if (!blob) return;
  await idbPut(vaultKey(userId), blob);
  await idbDelete(LEGACY_VAULT_KEY);
}
