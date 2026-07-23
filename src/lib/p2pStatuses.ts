/** Client mirror of backend P2P active statuses. */
export const P2P_TERMINAL_STATUSES = new Set(["completed", "cancelled"]);

export const P2P_ACTIVE_STATUSES = new Set([
  "created",
  "awaiting_fiat_payment",
  "fiat_payment_confirmed",
  "crypto_sent",
  "disputed",
]);

export function isActiveOrderStatus(status: string): boolean {
  return P2P_ACTIVE_STATUSES.has(status) && !P2P_TERMINAL_STATUSES.has(status);
}
