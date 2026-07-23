/**
 * Non-custodial P2P order status machine.
 * Umbrella coordinates matchmaking + public settlement proofs only — no escrow.
 */

export const P2P_TERMINAL_STATUSES = new Set(["completed", "cancelled"]);

export const P2P_ACTIVE_STATUSES = new Set([
  "created",
  "awaiting_fiat_payment",
  "fiat_payment_confirmed",
  "crypto_sent",
  "disputed",
]);

export const P2P_ORDER_TRANSITIONS: Record<string, string[]> = {
  created: ["awaiting_fiat_payment", "disputed", "cancelled"],
  awaiting_fiat_payment: ["fiat_payment_confirmed", "disputed", "cancelled"],
  fiat_payment_confirmed: ["crypto_sent", "disputed"],
  crypto_sent: ["completed", "disputed"],
  completed: [],
  disputed: ["cancelled", "completed"],
  cancelled: [],
};

export const P2P_CANCELLABLE_STATUSES = new Set(["created", "awaiting_fiat_payment"]);

export function canTransition(from: string, to: string): boolean {
  return (P2P_ORDER_TRANSITIONS[from] ?? []).includes(to);
}

export function isActiveOrderStatus(status: string): boolean {
  return P2P_ACTIVE_STATUSES.has(status) && !P2P_TERMINAL_STATUSES.has(status);
}

export function kycLevelValue(level: string | null | undefined): number {
  const map: Record<string, number> = {
    none: 0,
    basic: 1,
    standard: 2,
    advanced: 3,
  };
  return map[(level ?? "none").toLowerCase()] ?? 0;
}

export function hasKycLevel(
  record: { status: string; level: string } | null | undefined,
  minLevel: number,
): boolean {
  if (!record || record.status !== "approved") return false;
  return kycLevelValue(record.level) >= minLevel;
}
