import { createHash, randomBytes, randomUUID } from "crypto";
import * as argon2 from "argon2";

const ARGON2_OPTS = { type: argon2.argon2id } as const;

export async function hashPassword(password: string): Promise<string> {
  return argon2.hash(password, ARGON2_OPTS);
}

export async function verifyPassword(hash: string, password: string): Promise<boolean> {
  try {
    return await argon2.verify(hash, password);
  } catch {
    return false;
  }
}

/** Argon2id hash of refresh token — never store plaintext tokens. */
export async function hashRefreshToken(token: string): Promise<string> {
  return argon2.hash(token, ARGON2_OPTS);
}

export async function verifyRefreshToken(hash: string, token: string): Promise<boolean> {
  try {
    return await argon2.verify(hash, token);
  } catch {
    return false;
  }
}

/** Legacy SHA-256 lookup (demo store / migration compat). */
export function hashToken(token: string): string {
  return createHash("sha256").update(token).digest("hex");
}

export function generateToken(bytes = 32): string {
  return randomBytes(bytes).toString("hex");
}

export function newTokenId(): string {
  return randomUUID();
}
