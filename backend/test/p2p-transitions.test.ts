import {
  P2P_CANCELLABLE_STATUSES,
  P2P_ORDER_TRANSITIONS,
  canTransition,
} from "../src/p2p/p2p-transitions";

function assert(condition: boolean, msg: string) {
  if (!condition) throw new Error(msg);
}

assert(canTransition("created", "awaiting_fiat_payment"), "created → awaiting_fiat_payment");
assert(canTransition("awaiting_fiat_payment", "fiat_payment_confirmed"), "awaiting → fiat confirmed");
assert(canTransition("fiat_payment_confirmed", "crypto_sent"), "fiat confirmed → crypto_sent");
assert(canTransition("crypto_sent", "completed"), "crypto_sent → completed");
assert(canTransition("created", "disputed"), "created → disputed");
assert(canTransition("awaiting_fiat_payment", "cancelled"), "awaiting → cancelled");
assert(!canTransition("created", "completed"), "no skip to completed");
assert(!canTransition("fiat_payment_confirmed", "cancelled"), "no cancel after fiat confirmed");
assert(P2P_CANCELLABLE_STATUSES.has("created"), "created is cancellable");
assert(!JSON.stringify(P2P_ORDER_TRANSITIONS).includes("escrow"), "no escrow transitions");

console.log("p2p transitions OK");
