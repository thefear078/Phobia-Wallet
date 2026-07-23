import { createHmac, timingSafeEqual } from "crypto";

export function hmacSha256Hex(payload: string | Buffer, secret: string): string {
  return createHmac("sha256", secret).update(payload).digest("hex");
}

export function verifyHmacSha256Hex(
  payload: string | Buffer,
  secret: string,
  signature: string | undefined,
): boolean {
  if (!signature || !secret) return false;
  const normalized = signature
    .replace(/^sha256=/i, "")
    .trim()
    .toLowerCase();
  const expected = hmacSha256Hex(payload, secret);
  try {
    const a = Buffer.from(expected, "hex");
    const b = Buffer.from(normalized, "hex");
    if (a.length !== b.length) return false;
    return timingSafeEqual(a, b);
  } catch {
    return false;
  }
}
