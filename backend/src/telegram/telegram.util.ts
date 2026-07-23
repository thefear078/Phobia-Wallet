import { createHash, createHmac, timingSafeEqual } from "crypto";
import { UnauthorizedException } from "@nestjs/common";

export type TelegramInitUser = {
  id: number;
  username?: string;
  first_name?: string;
  last_name?: string;
};

const AUTH_MAX_AGE_SECONDS = 3600;

export function verifyTelegramInitData(initData: string, botToken: string): TelegramInitUser {
  const params = new URLSearchParams(initData);
  const hash = params.get("hash");
  const authDateRaw = params.get("auth_date");
  const userRaw = params.get("user");

  if (!hash || !authDateRaw || !userRaw) {
    throw new UnauthorizedException("Invalid Telegram initData payload");
  }

  const authDate = Number(authDateRaw);
  if (!Number.isFinite(authDate)) {
    throw new UnauthorizedException("Invalid Telegram auth date");
  }

  const now = Math.floor(Date.now() / 1000);
  if (now - authDate > AUTH_MAX_AGE_SECONDS) {
    throw new UnauthorizedException("Telegram auth request expired");
  }

  const lines: string[] = [];
  for (const [key, value] of params.entries()) {
    if (key !== "hash") lines.push(`${key}=${value}`);
  }
  lines.sort((a, b) => a.localeCompare(b));
  const dataCheckString = lines.join("\n");

  const secret = createHash("sha256").update(botToken).digest();
  const signature = createHmac("sha256", secret).update(dataCheckString).digest("hex");
  const signatureBytes = Buffer.from(signature, "hex");
  const hashBytes = Buffer.from(hash, "hex");
  const signatureOk =
    signatureBytes.length === hashBytes.length && timingSafeEqual(signatureBytes, hashBytes);
  if (!signatureOk) {
    throw new UnauthorizedException("Telegram signature mismatch");
  }

  let user: TelegramInitUser;
  try {
    user = JSON.parse(userRaw) as TelegramInitUser;
  } catch {
    throw new UnauthorizedException("Invalid Telegram user payload");
  }

  if (!user?.id) {
    throw new UnauthorizedException("Telegram user is missing");
  }

  return user;
}

export function verifyTelegramLoginWidget(initData: string, botToken: string): TelegramInitUser {
  const params = new URLSearchParams(initData);
  const hash = params.get("hash");
  const authDateRaw = params.get("auth_date");
  const idRaw = params.get("id");

  if (!hash || !authDateRaw || !idRaw) {
    throw new UnauthorizedException("Invalid Telegram login widget payload");
  }

  const authDate = Number(authDateRaw);
  if (!Number.isFinite(authDate)) {
    throw new UnauthorizedException("Invalid Telegram auth date");
  }

  const now = Math.floor(Date.now() / 1000);
  if (now - authDate > AUTH_MAX_AGE_SECONDS) {
    throw new UnauthorizedException("Telegram auth request expired");
  }

  const lines: string[] = [];
  for (const [key, value] of params.entries()) {
    if (key !== "hash") lines.push(`${key}=${value}`);
  }
  lines.sort((a, b) => a.localeCompare(b));
  const dataCheckString = lines.join("\n");

  const secret = createHash("sha256").update(botToken).digest();
  const signature = createHmac("sha256", secret).update(dataCheckString).digest("hex");
  const signatureBytes = Buffer.from(signature, "hex");
  const hashBytes = Buffer.from(hash, "hex");
  const signatureOk =
    signatureBytes.length === hashBytes.length && timingSafeEqual(signatureBytes, hashBytes);
  if (!signatureOk) {
    throw new UnauthorizedException("Telegram signature mismatch");
  }

  const id = Number(idRaw);
  if (!Number.isFinite(id)) {
    throw new UnauthorizedException("Telegram user is missing");
  }

  return {
    id,
    username: params.get("username") ?? undefined,
    first_name: params.get("first_name") ?? undefined,
    last_name: params.get("last_name") ?? undefined,
  };
}

export function resolveTelegramUser(initData: string, botToken: string): TelegramInitUser {
  const params = new URLSearchParams(initData);
  if (params.has("user")) {
    return verifyTelegramInitData(initData, botToken);
  }
  if (params.has("id")) {
    return verifyTelegramLoginWidget(initData, botToken);
  }
  throw new UnauthorizedException("Invalid Telegram auth payload");
}

/** Demo-only: parse user from initData without HMAC (when bot token absent). */
export function parseTelegramInitDataUnsafe(initData: string): TelegramInitUser {
  if (!initData?.trim()) {
    return { id: 999_000_001, username: "demo_user", first_name: "Demo" };
  }
  const params = new URLSearchParams(initData);
  const userRaw = params.get("user");
  if (!userRaw) {
    return { id: 999_000_001, username: "demo_user", first_name: "Demo" };
  }
  try {
    const user = JSON.parse(userRaw) as TelegramInitUser;
    if (!user?.id) throw new Error("missing id");
    return user;
  } catch {
    throw new UnauthorizedException("Invalid Telegram user payload");
  }
}
