import type { OfferRow, P2pOrderRow, QuoteKind } from "./p2pTypes";
import { useCallback, useEffect, useState } from "react";
import { toast } from "sonner";
import { Check, Copy, ShieldAlert, ShieldCheck, TriangleAlert } from "lucide-react";
import { ActionSheet } from "@/components/ActionSheet";
import { api } from "@/lib/api/client";
import { useSession } from "@/lib/authStore";
import { isDemoMode } from "@/lib/demoMode";
import { useP2pOrderStream, type P2pOrderStatusEvent } from "@/hooks/useP2pOrderStream";
import { isActiveOrderStatus } from "@/lib/p2pStatuses";

export type { P2pOrderRow } from "./p2pTypes";

const EVM_TX_HASH = /^0x[a-fA-F0-9]{64}$/;

const CANCELLABLE = new Set(["created", "awaiting_fiat_payment"]);

/** Step labels adapt to the quote kind: fiat pays by bank, crypto pays on-chain. */
function steps(quoteKind: QuoteKind, quote: string) {
  const payLabel = quoteKind === "crypto" ? `Send ${quote}` : "Fiat payment";
  const payDoneLabel = quoteKind === "crypto" ? `${quote} sent` : "Fiat confirmed";
  return [
    { key: "created", label: "Created" },
    { key: "awaiting_fiat_payment", label: payLabel },
    { key: "fiat_payment_confirmed", label: payDoneLabel },
    { key: "crypto_sent", label: "Crypto sent" },
    { key: "completed", label: "Completed" },
  ] as const;
}

function statusLabel(status: string, quoteKind: QuoteKind, quote: string): string {
  const cryptoQuote = quoteKind === "crypto";
  const map: Record<string, string> = {
    created: "Deal created",
    awaiting_fiat_payment: cryptoQuote ? `Awaiting ${quote} transfer` : "Awaiting fiat payment",
    fiat_payment_confirmed: cryptoQuote ? `${quote} transfer confirmed` : "Fiat payment confirmed",
    crypto_sent: "Crypto sent",
    completed: "Deal completed",
    cancelled: "Deal cancelled",
    disputed: "Dispute opened",
  };
  return map[status] ?? status;
}

type Props = {
  offer: OfferRow | null;
  side: "buy" | "sell";
  onClose: () => void;
  onOrderUpdated?: () => void;
  existingOrder?: P2pOrderRow | null;
};

export function P2pOrderSheet({ offer, side, onClose, onOrderUpdated, existingOrder }: Props) {
  const session = useSession();
  const [amount, setAmount] = useState("");
  const [order, setOrder] = useState<P2pOrderRow | null>(existingOrder ?? null);
  const [fiatRef, setFiatRef] = useState("");
  const [txHash, setTxHash] = useState("");
  const [disputeReason, setDisputeReason] = useState("");
  const [showDispute, setShowDispute] = useState(false);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const [ack, setAck] = useState(false);

  useEffect(() => {
    if (existingOrder) setOrder(existingOrder);
  }, [existingOrder]);

  const applyEvent = useCallback(
    (event: P2pOrderStatusEvent) => {
      if (!order || event.orderId !== order.id) return;
      setOrder((prev) => (prev ? { ...prev, status: event.status, amount: event.amount } : prev));
      onOrderUpdated?.();
    },
    [order, onOrderUpdated],
  );

  useP2pOrderStream(applyEvent, Boolean(order?.id) && !isDemoMode());

  // Demo mode has no SSE — poll so the simulated counterparty's steps appear.
  useEffect(() => {
    if (!order?.id || !isDemoMode() || !isActiveOrderStatus(order.status)) return;
    const id = window.setInterval(() => {
      void api
        .getP2pOrder(order.id)
        .then((o) => setOrder(o as P2pOrderRow))
        .catch(() => {});
    }, 2000);
    return () => window.clearInterval(id);
  }, [order?.id, order?.status]);

  const reset = () => {
    setAmount("");
    setOrder(null);
    setFiatRef("");
    setTxHash("");
    setDisputeReason("");
    setShowDispute(false);
    setAck(false);
    onClose();
  };

  if (!offer && !existingOrder) return null;

  const activeOffer: OfferRow | null = order?.offer ?? offer ?? null;
  const quoteKind: QuoteKind = activeOffer?.quoteKind ?? "fiat";
  const isCryptoQuote = quoteKind === "crypto";
  const quote = activeOffer?.fiat ?? (isCryptoQuote ? "USDT" : "UAH");
  const asset = activeOffer?.asset ?? "";
  const price = activeOffer?.price ?? 0;
  const min = activeOffer?.min ?? 0;
  const max = activeOffer?.max ?? Infinity;
  const isBuyer = order ? order.buyerId === session.userId : side === "buy";
  const isSeller = order ? order.sellerId === session.userId : side === "sell";
  const status = order?.status ?? "create";
  const active = order ? isActiveOrderStatus(order.status) : false;
  const cryptoAmount = order && price > 0 ? order.amount / price : null;
  const STEPS = steps(quoteKind, quote);

  const run = async (fn: () => Promise<unknown>, okMsg?: string) => {
    setBusy(true);
    try {
      const o = (await fn()) as P2pOrderRow;
      if (o && typeof o === "object" && "status" in o) {
        setOrder((prev) => ({ ...(prev ?? o), ...o }));
      }
      if (okMsg) toast.success(okMsg);
      onOrderUpdated?.();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error");
    } finally {
      setBusy(false);
    }
  };

  const createOrder = async () => {
    const v = Number.parseFloat(amount.replace(",", "."));
    if (!Number.isFinite(v) || v <= 0 || v < min || v > max) {
      toast.error(`Amount must be between ${min}–${max === Infinity ? "∞" : max} ${quote}`);
      return;
    }
    if (!ack) {
      toast.error("Please confirm you've read the safety notice");
      return;
    }
    if (!activeOffer) return;
    await run(() => api.createP2pOrder(activeOffer.id, v), "Deal created");
  };

  const submitFiat = async () => {
    const ref = fiatRef.trim();
    if (isCryptoQuote) {
      if (!EVM_TX_HASH.test(ref)) {
        toast.error("Paste the tx hash of your transfer (0x + 64 hex characters)");
        return;
      }
    } else if (ref.length < 4) {
      toast.error("Enter a bank payment reference (min. 4 characters)");
      return;
    }
    await run(
      () => api.submitP2pFiatProof(order!.id, ref),
      isCryptoQuote ? "Transfer recorded" : "Fiat confirmation saved",
    );
  };

  const submitCrypto = async () => {
    const hash = txHash.trim();
    if (!EVM_TX_HASH.test(hash)) {
      toast.error("Invalid format: EVM tx hash required (0x + 64 hex characters)");
      return;
    }
    await run(() => api.submitP2pCryptoProof(order!.id, hash), "Crypto transaction recorded");
  };

  const openDispute = async () => {
    const reason = disputeReason.trim();
    if (reason.length < 3) {
      toast.error("Describe the reason for the dispute");
      return;
    }
    await run(
      () => api.disputeP2pOrder(order!.id, reason),
      "Dispute opened — the team will review the evidence",
    );
    setShowDispute(false);
  };

  const copyValue = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      /* clipboard unavailable */
    }
  };

  const stepIndex = STEPS.findIndex((s) => s.key === status);
  const buyerPayLabel = isCryptoQuote ? quote : "fiat";

  return (
    <ActionSheet
      open={!!offer || !!existingOrder}
      onOpenChange={(v) => !v && reset()}
      title={
        order ? `Deal #${order.id.slice(0, 8)}` : `${side === "buy" ? "Buy" : "Sell"} ${asset}`
      }
      description={
        order
          ? statusLabel(status, quoteKind, quote)
          : activeOffer
            ? `${activeOffer.merchant} · ${price} ${quote} per 1 ${asset}`
            : undefined
      }
    >
      <div className="rounded-2xl bg-card p-4 space-y-3 text-sm">
        {/* Progress steps for an existing order */}
        {order && status !== "cancelled" && status !== "disputed" && (
          <div className="flex items-center gap-1 pb-1">
            {STEPS.map((s, i) => (
              <div key={s.key} className="flex-1 flex flex-col items-center gap-1">
                <div
                  className={`h-1.5 w-full rounded-full ${
                    stepIndex >= i ? "bg-primary" : "bg-secondary"
                  }`}
                />
                <span
                  className={`text-[9px] leading-tight text-center ${
                    stepIndex === i ? "text-primary font-semibold" : "text-muted-foreground"
                  }`}
                >
                  {s.label}
                </span>
              </div>
            ))}
          </div>
        )}

        {/* Deal summary */}
        {order && activeOffer && (
          <div className="rounded-xl bg-secondary/60 p-3 text-xs space-y-1">
            <div className="flex justify-between">
              <span className="text-muted-foreground">{isBuyer ? "You pay" : "Buyer pays"}</span>
              <span className="font-semibold">
                {order.amount.toLocaleString("en-US")} {quote}
              </span>
            </div>
            {cryptoAmount !== null && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">
                  {isBuyer ? "You receive" : "You send"}
                </span>
                <span className="font-semibold">
                  ≈ {cryptoAmount.toFixed(6)} {asset}
                </span>
              </div>
            )}
            <div className="flex justify-between">
              <span className="text-muted-foreground">Rate</span>
              <span>
                {price} {quote}
              </span>
            </div>
            {order.paymentMethod && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">Payment method</span>
                <span className="capitalize">{order.paymentMethod}</span>
              </div>
            )}
            {order.fiatPaymentReference && (
              <div className="flex justify-between items-center gap-2">
                <span className="text-muted-foreground">
                  {isCryptoQuote ? "Buyer tx" : "Fiat ref"}
                </span>
                <button
                  type="button"
                  onClick={() => void copyValue(order.fiatPaymentReference!)}
                  className="flex items-center gap-1 font-mono truncate max-w-[60%]"
                >
                  {copied ? (
                    <Check className="h-3 w-3 text-[color:var(--success)]" />
                  ) : (
                    <Copy className="h-3 w-3" />
                  )}
                  <span className="truncate">{order.fiatPaymentReference}</span>
                </button>
              </div>
            )}
            {order.cryptoTxHash && (
              <div className="flex justify-between items-center gap-2">
                <span className="text-muted-foreground">Tx hash</span>
                <button
                  type="button"
                  onClick={() => void copyValue(order.cryptoTxHash!)}
                  className="flex items-center gap-1 font-mono truncate max-w-[60%]"
                >
                  {copied ? (
                    <Check className="h-3 w-3 text-[color:var(--success)]" />
                  ) : (
                    <Copy className="h-3 w-3" />
                  )}
                  <span className="truncate">{order.cryptoTxHash}</span>
                </button>
              </div>
            )}
          </div>
        )}

        {/* Step 0: create order (with mandatory safety acknowledgement) */}
        {!order && activeOffer && (
          <>
            <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-3 text-xs space-y-2">
              <div className="flex items-center gap-2 font-semibold text-amber-200">
                <TriangleAlert className="h-4 w-4 shrink-0" />
                Before you trade with a stranger
              </div>
              <ul className="space-y-1 text-amber-100/90 list-disc pl-4">
                <li>
                  Umbrella holds no funds and cannot reverse a transfer — settlement is direct.
                </li>
                <li>
                  {isCryptoQuote
                    ? "Only release your crypto after the counterparty's transfer is confirmed on-chain."
                    : "Only release crypto after the money is truly in your bank and cleared — transfers can be recalled."}
                </li>
                <li>
                  Keep the whole deal inside the app. Ignore anyone asking to move to private chat.
                </li>
                <li>Keep receipts and tx hashes — you'll need them to win a dispute.</li>
              </ul>
              <label className="flex items-center gap-2 pt-1 text-amber-100 cursor-pointer">
                <input
                  type="checkbox"
                  checked={ack}
                  onChange={(e) => setAck(e.target.checked)}
                  className="h-4 w-4 accent-[color:var(--primary)]"
                />
                I understand and accept these risks
              </label>
            </div>

            <label className="text-xs text-muted-foreground">Amount ({quote})</label>
            <input
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              type="number"
              inputMode="decimal"
              min={min}
              max={max === Infinity ? undefined : max}
              className="w-full bg-secondary rounded-xl px-3 py-2.5 outline-none"
            />
            <div className="flex items-center justify-between text-[11px] text-muted-foreground">
              <span>
                Limit: {min}–{max === Infinity ? "∞" : max.toLocaleString("en-US")} {quote}
              </span>
              <div className="flex gap-1">
                {min > 0 && (
                  <button
                    type="button"
                    onClick={() => setAmount(String(min))}
                    className="px-2 py-0.5 rounded-md bg-secondary"
                  >
                    min
                  </button>
                )}
                {max !== Infinity && (
                  <button
                    type="button"
                    onClick={() => setAmount(String(max))}
                    className="px-2 py-0.5 rounded-md bg-secondary"
                  >
                    max
                  </button>
                )}
              </div>
            </div>
            {price > 0 && Number.parseFloat(amount.replace(",", ".")) > 0 && (
              <p className="text-[11px] text-muted-foreground">
                ≈ {(Number.parseFloat(amount.replace(",", ".")) / price).toFixed(6)} {asset}
              </p>
            )}
            <button
              disabled={busy || !ack}
              onClick={() => void createOrder()}
              className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-50"
            >
              Start deal
            </button>
          </>
        )}

        {/* Buyer: start payment */}
        {order && status === "created" && isBuyer && (
          <>
            <p className="text-xs text-muted-foreground">
              Step 1: confirm you're ready to pay in {buyerPayLabel}. You'll then have time to
              transfer.
            </p>
            <button
              disabled={busy}
              onClick={() => void run(() => api.startP2pFiat(order.id))}
              className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-60"
            >
              {isCryptoQuote ? `Start ${quote} transfer` : "Start fiat payment"}
            </button>
          </>
        )}

        {/* Buyer: submit payment proof */}
        {order && status === "awaiting_fiat_payment" && isBuyer && (
          <>
            <p className="text-xs text-muted-foreground">
              {isCryptoQuote ? (
                <>
                  Send {order.amount.toLocaleString("en-US")} {quote} to the seller's address, then
                  paste your transaction hash:
                </>
              ) : (
                <>
                  Transfer {order.amount.toLocaleString("en-US")} {quote} to the seller
                  {order.paymentMethod ? ` via ${order.paymentMethod}` : ""} and enter the payment
                  reference from your bank receipt:
                </>
              )}
            </p>
            <input
              value={fiatRef}
              onChange={(e) => setFiatRef(e.target.value)}
              placeholder={isCryptoQuote ? "0x…" : "Payment reference"}
              maxLength={isCryptoQuote ? 66 : 128}
              className="w-full bg-secondary rounded-xl px-3 py-2.5 outline-none font-mono text-xs"
            />
            <button
              disabled={busy}
              onClick={() => void submitFiat()}
              className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-60"
            >
              {isCryptoQuote ? `I sent ${quote} — confirm` : "I paid — confirm fiat"}
            </button>
          </>
        )}

        {/* Seller: submit crypto proof */}
        {order && status === "fiat_payment_confirmed" && isSeller && (
          <>
            <p className="text-xs text-muted-foreground">
              {isCryptoQuote ? (
                <>
                  The buyer sent {quote} (tx: {order.fiatPaymentReference ?? "—"}). Verify it
                  arrived on-chain, then send {asset} and enter your tx hash:
                </>
              ) : (
                <>
                  The buyer confirmed fiat payment (ref: {order.fiatPaymentReference ?? "—"}). Check
                  the arrival in your bank, send {asset} from your wallet, and enter the tx hash:
                </>
              )}
            </p>
            <input
              value={txHash}
              onChange={(e) => setTxHash(e.target.value)}
              placeholder="0x…"
              maxLength={66}
              className="w-full bg-secondary rounded-xl px-3 py-2.5 outline-none font-mono text-xs"
            />
            <button
              disabled={busy}
              onClick={() => void submitCrypto()}
              className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-60"
            >
              Record crypto transaction
            </button>
          </>
        )}

        {/* Buyer: confirm receipt */}
        {order && status === "crypto_sent" && isBuyer && (
          <>
            <p className="text-xs text-muted-foreground">
              The seller sent crypto. Make sure {asset} arrived in your wallet before confirming.
            </p>
            <button
              disabled={busy}
              onClick={() => void run(() => api.completeP2pOrder(order.id), "Deal completed")}
              className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-60"
            >
              Crypto received — complete deal
            </button>
          </>
        )}

        {/* Waiting for counterparty */}
        {order &&
          active &&
          status !== "disputed" &&
          ((status === "created" && !isBuyer) ||
            (status === "awaiting_fiat_payment" && !isBuyer) ||
            (status === "fiat_payment_confirmed" && !isSeller) ||
            (status === "crypto_sent" && !isBuyer)) && (
            <div className="flex items-center gap-2 rounded-xl bg-secondary/60 p-3 text-xs text-muted-foreground">
              <span className="h-2 w-2 rounded-full bg-primary animate-pulse shrink-0" />
              {status === "fiat_payment_confirmed"
                ? "Waiting for the seller to send crypto…"
                : status === "crypto_sent"
                  ? "Waiting for buyer confirmation…"
                  : status === "awaiting_fiat_payment"
                    ? isCryptoQuote
                      ? `Waiting for the buyer to send ${quote}…`
                      : "Waiting for the buyer's payment…"
                    : "Waiting for buyer action…"}
            </div>
          )}

        {/* Terminal states */}
        {order && (status === "completed" || status === "cancelled" || status === "disputed") && (
          <div className="text-center py-2 space-y-2">
            <p className="font-semibold">{statusLabel(status, quoteKind, quote)}</p>
            {status === "disputed" && order.disputeReason && (
              <p className="text-xs text-muted-foreground">Reason: {order.disputeReason}</p>
            )}
            <button onClick={reset} className="mt-1 px-6 py-2 rounded-xl bg-secondary text-sm">
              Close
            </button>
          </div>
        )}

        {/* Non-custodial reminder while a deal is live */}
        {order && active && status !== "disputed" && (
          <div className="flex items-start gap-2 rounded-xl bg-secondary/40 p-2.5 text-[11px] text-muted-foreground">
            <ShieldCheck className="h-3.5 w-3.5 shrink-0 text-primary mt-0.5" />
            <span>
              Umbrella never holds funds. Verify the transfer arrived before your next step.
            </span>
          </div>
        )}

        {/* Cancel (only before payment is confirmed) */}
        {order && CANCELLABLE.has(status) && (
          <button
            type="button"
            disabled={busy}
            onClick={() => void run(() => api.cancelP2pOrder(order.id), "Deal cancelled")}
            className="w-full py-2 text-xs text-muted-foreground"
          >
            Cancel deal
          </button>
        )}

        {/* Dispute */}
        {order && active && status !== "disputed" && !showDispute && (
          <button
            type="button"
            onClick={() => setShowDispute(true)}
            className="w-full py-2 text-xs text-destructive flex items-center justify-center gap-1"
          >
            <ShieldAlert className="h-3.5 w-3.5" />
            Open dispute
          </button>
        )}
        {order && showDispute && status !== "disputed" && (
          <div className="space-y-2">
            <textarea
              value={disputeReason}
              onChange={(e) => setDisputeReason(e.target.value)}
              placeholder="Describe the issue: what went wrong, what evidence you have…"
              maxLength={500}
              rows={3}
              className="w-full bg-secondary rounded-xl px-3 py-2.5 outline-none text-xs resize-none"
            />
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setShowDispute(false)}
                className="flex-1 py-2 rounded-xl bg-secondary text-xs"
              >
                Back
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => void openDispute()}
                className="flex-1 py-2 rounded-xl bg-destructive text-destructive-foreground text-xs font-semibold disabled:opacity-60"
              >
                Open dispute
              </button>
            </div>
          </div>
        )}
      </div>
    </ActionSheet>
  );
}
