import { createFileRoute } from "@tanstack/react-router";
import { AppShell } from "@/components/AppShell";
import { Star, Shield, Filter, Plus, Pencil, Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { P2pOrderSheet } from "@/components/P2pOrderSheet";
import { P2pOfferSheet } from "@/components/P2pOfferSheet";
import { api } from "@/lib/api/client";
import type { OfferRow, P2pOrderRow } from "@/components/p2pTypes";
import { useP2pOrderStream } from "@/hooks/useP2pOrderStream";
import { isActiveOrderStatus } from "@/lib/p2pStatuses";

export const Route = createFileRoute("/p2p")({
  validateSearch: (search: Record<string, unknown>): { asset?: string; side?: "buy" | "sell" } => ({
    asset: typeof search.asset === "string" ? search.asset : undefined,
    side: search.side === "sell" ? "sell" : search.side === "buy" ? "buy" : undefined,
  }),
  head: () => ({
    meta: [
      { title: "P2P exchange — Umbrella Wallet" },
      { name: "description", content: "Non-custodial matchmaking." },
    ],
  }),
  component: P2PPage,
});

const ASSETS = ["BTC", "ETH", "USDT"] as const;
const QUOTES = ["UAH", "USD", "EUR", "USDT", "USDC"] as const;
const METHODS = ["Monobank", "PrivatBank", "PUMB", "Wise", "Revolut"] as const;

const ORDER_STATUS_LABELS: Record<string, string> = {
  created: "Created",
  awaiting_fiat_payment: "Fiat payment",
  fiat_payment_confirmed: "Fiat confirmed",
  crypto_sent: "Crypto sent",
  completed: "Completed",
  cancelled: "Cancelled",
  disputed: "Dispute",
};

type Tab = "market" | "orders" | "myoffers";

function P2PPage() {
  const qc = useQueryClient();
  const search = Route.useSearch();
  const [tab, setTab] = useState<Tab>("market");
  const [side, setSide] = useState<"buy" | "sell">(search.side ?? "buy");
  const [asset, setAsset] = useState<string>(search.asset ?? "USDT");
  const [fiat, setFiat] = useState<string>("");
  const [method, setMethod] = useState<string>("");
  const [deal, setDeal] = useState<OfferRow | null>(null);
  const [activeOrder, setActiveOrder] = useState<P2pOrderRow | null>(null);
  const [offerSheetOpen, setOfferSheetOpen] = useState(false);
  const [editOffer, setEditOffer] = useState<OfferRow | null>(null);

  const invalidateOrders = useCallback(() => {
    void qc.invalidateQueries({ queryKey: ["p2p-orders"] });
  }, [qc]);

  const invalidateOffers = useCallback(() => {
    void qc.invalidateQueries({ queryKey: ["p2p-offers"] });
    void qc.invalidateQueries({ queryKey: ["p2p-my-offers"] });
  }, [qc]);

  useP2pOrderStream(
    useCallback(() => invalidateOrders(), [invalidateOrders]),
    tab === "orders",
  );

  useEffect(() => {
    if (search.asset) setAsset(search.asset);
    if (search.side) setSide(search.side);
  }, [search.asset, search.side]);

  const {
    data: offers = [],
    isError,
    refetch,
    isFetching,
  } = useQuery({
    queryKey: ["p2p-offers", asset, side, fiat, method],
    queryFn: () =>
      api.p2pOffers({
        asset,
        // User intent "buy" → merchant offers that sell (and vice versa)
        side: side === "buy" ? "sell" : "buy",
        ...(fiat ? { fiat } : {}),
        ...(method ? { method } : {}),
      }),
    retry: 1,
    staleTime: 30_000,
    enabled: tab === "market",
  });

  const { data: myOrders = [], isLoading: ordersLoading } = useQuery({
    queryKey: ["p2p-orders"],
    queryFn: () => api.listP2pOrders(),
    retry: 1,
    refetchInterval: tab === "orders" ? 5000 : false,
    enabled: tab === "orders",
  });

  const { data: myOffers = [], isLoading: myOffersLoading } = useQuery({
    queryKey: ["p2p-my-offers"],
    queryFn: () => api.myP2pOffers(),
    retry: 1,
    enabled: tab === "myoffers",
  });

  const deleteOffer = async (offerId: string) => {
    try {
      await api.deleteP2pOffer(offerId);
      toast.success("Offer unpublished");
      invalidateOffers();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Delete error");
    }
  };

  return (
    <AppShell>
      <header className="px-5 pt-6 pb-4 flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-bold">P2P Market</h1>
          <p className="text-sm text-muted-foreground">
            Direct settlement — Umbrella only coordinates
          </p>
        </div>
        <button
          onClick={() => {
            setEditOffer(null);
            setOfferSheetOpen(true);
          }}
          className="mt-1 h-10 w-10 rounded-full bg-primary text-primary-foreground flex items-center justify-center shadow"
          aria-label="Create offer"
        >
          <Plus className="h-5 w-5" />
        </button>
      </header>

      <div className="px-5">
        <div className="grid grid-cols-3 rounded-2xl bg-secondary p-1">
          {(
            [
              ["market", "Offers"],
              ["orders", "My deals"],
              ["myoffers", "My offers"],
            ] as const
          ).map(([t, label]) => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={`py-2.5 rounded-xl text-xs font-semibold transition ${
                tab === t ? "bg-card text-foreground shadow" : "text-muted-foreground"
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {tab === "market" && (
        <>
          <div className="px-5 mt-4">
            <div className="grid grid-cols-2 rounded-2xl bg-secondary p-1">
              {(["buy", "sell"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => setSide(s)}
                  className={`py-2 rounded-xl text-sm font-semibold transition ${
                    side === s ? "bg-card text-foreground shadow" : "text-muted-foreground"
                  }`}
                >
                  {s === "buy" ? "Buy" : "Sell"}
                </button>
              ))}
            </div>
          </div>

          <div className="px-5 mt-3 flex gap-2 overflow-x-auto pb-1">
            {ASSETS.map((a) => (
              <button
                key={a}
                onClick={() => setAsset(a)}
                className={`px-4 py-1.5 rounded-full text-sm font-medium whitespace-nowrap ${
                  asset === a
                    ? "bg-primary text-primary-foreground"
                    : "bg-secondary text-muted-foreground"
                }`}
              >
                {a}
              </button>
            ))}
          </div>

          <div className="px-5 mt-2 flex flex-wrap gap-2">
            <select
              value={fiat}
              onChange={(e) => setFiat(e.target.value)}
              className="bg-secondary rounded-xl px-3 py-1.5 text-xs outline-none"
            >
              <option value="">All quotes</option>
              {QUOTES.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
            <select
              value={method}
              onChange={(e) => setMethod(e.target.value)}
              className="bg-secondary rounded-xl px-3 py-1.5 text-xs outline-none"
            >
              <option value="">All methods</option>
              {METHODS.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
            <button
              onClick={() => void refetch()}
              className="ml-auto h-9 w-9 rounded-full bg-secondary flex items-center justify-center"
            >
              <Filter className={`h-4 w-4 ${isFetching ? "animate-pulse" : ""}`} />
            </button>
          </div>

          {isError && (
            <p className="px-5 mt-4 text-sm text-destructive">
              API unavailable. Start the backend.
            </p>
          )}

          <section className="px-5 mt-4 space-y-3 pb-8">
            {offers.length === 0 && !isFetching && (
              <div className="text-center text-muted-foreground text-sm py-10">
                No offers for {asset}
              </div>
            )}
            {offers.map((o) => (
              <OfferCard
                key={o.id}
                offer={o}
                side={side}
                onTrade={() => {
                  setActiveOrder(null);
                  setDeal(o);
                }}
              />
            ))}
          </section>
        </>
      )}

      {tab === "orders" && (
        <section className="px-5 mt-4 pb-8 space-y-3">
          {ordersLoading && <p className="text-sm text-muted-foreground">Loading…</p>}
          {!ordersLoading && myOrders.length === 0 && (
            <p className="text-sm text-muted-foreground text-center py-8">
              No deals. Pick an offer on the market to start.
            </p>
          )}
          {(myOrders as P2pOrderRow[]).map((order) => (
            <button
              key={order.id}
              onClick={() => {
                setDeal(null);
                setActiveOrder(order);
              }}
              className="w-full rounded-2xl bg-card p-4 text-left"
            >
              <div className="flex justify-between items-center text-sm">
                <span className="font-semibold">
                  {order.offer
                    ? `${order.offer.asset}/${order.offer.fiat}`
                    : `#${order.id.slice(0, 8)}`}
                </span>
                <span
                  className={`text-xs px-2 py-0.5 rounded-md ${
                    isActiveOrderStatus(order.status)
                      ? "bg-primary/15 text-primary"
                      : order.status === "completed"
                        ? "bg-[color:var(--success)]/15 text-[color:var(--success)]"
                        : "bg-secondary text-muted-foreground"
                  }`}
                >
                  {ORDER_STATUS_LABELS[order.status] ?? order.status}
                </span>
              </div>
              <div className="mt-1.5 flex justify-between text-xs text-muted-foreground">
                <span>
                  {order.amount.toLocaleString("en-US")} {order.offer?.fiat ?? ""}
                  {order.offer ? ` · ${order.offer.merchant}` : ""}
                </span>
                <span>
                  {new Date(order.updatedAt ?? order.createdAt ?? Date.now()).toLocaleString(
                    "uk-UA",
                    {
                      day: "2-digit",
                      month: "2-digit",
                      hour: "2-digit",
                      minute: "2-digit",
                    },
                  )}
                </span>
              </div>
            </button>
          ))}
        </section>
      )}

      {tab === "myoffers" && (
        <section className="px-5 mt-4 pb-8 space-y-3">
          {myOffersLoading && <p className="text-sm text-muted-foreground">Loading…</p>}
          {!myOffersLoading && myOffers.length === 0 && (
            <div className="text-center text-muted-foreground text-sm py-8 space-y-3">
              <p>You have no offers yet.</p>
              <button
                onClick={() => {
                  setEditOffer(null);
                  setOfferSheetOpen(true);
                }}
                className="px-5 py-2.5 rounded-xl bg-primary text-primary-foreground text-sm font-semibold"
              >
                Create offer
              </button>
            </div>
          )}
          {(myOffers as OfferRow[]).map((o) => (
            <div key={o.id} className="rounded-2xl bg-card p-4">
              <div className="flex items-center justify-between">
                <div className="text-sm font-semibold">
                  {o.side === "sell" ? "Sell" : "Buy"} {o.asset}/{o.fiat}
                </div>
                <span
                  className={`text-xs px-2 py-0.5 rounded-md ${
                    o.status === "active"
                      ? "bg-[color:var(--success)]/15 text-[color:var(--success)]"
                      : "bg-secondary text-muted-foreground"
                  }`}
                >
                  {o.status === "active" ? "Active" : "In deal"}
                </span>
              </div>
              <div className="mt-1 text-lg font-bold">
                {o.price} <span className="text-xs text-muted-foreground">{o.fiat}</span>
              </div>
              <div className="mt-1 text-xs text-muted-foreground">
                Limit: {o.min ?? 0}–{(o.max ?? 0).toLocaleString("en-US")} {o.fiat}
              </div>
              <div className="mt-2 flex flex-wrap gap-1.5">
                {o.methods.map((m) => (
                  <span
                    key={m}
                    className="text-[11px] px-2 py-0.5 rounded-md bg-secondary text-muted-foreground capitalize"
                  >
                    {m}
                  </span>
                ))}
              </div>
              <div className="mt-3 flex gap-2">
                <button
                  onClick={() => {
                    setEditOffer(o);
                    setOfferSheetOpen(true);
                  }}
                  className="flex-1 py-2 rounded-xl bg-secondary text-xs font-semibold flex items-center justify-center gap-1.5"
                >
                  <Pencil className="h-3.5 w-3.5" />
                  Edit
                </button>
                <button
                  onClick={() => void deleteOffer(o.id)}
                  className="flex-1 py-2 rounded-xl bg-destructive/10 text-destructive text-xs font-semibold flex items-center justify-center gap-1.5"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                  Unpublish
                </button>
              </div>
            </div>
          ))}
        </section>
      )}

      <P2pOrderSheet
        offer={deal}
        side={side}
        existingOrder={activeOrder}
        onClose={() => {
          setDeal(null);
          setActiveOrder(null);
        }}
        onOrderUpdated={invalidateOrders}
      />

      <P2pOfferSheet
        open={offerSheetOpen}
        editOffer={editOffer}
        onClose={() => {
          setOfferSheetOpen(false);
          setEditOffer(null);
        }}
        onSaved={invalidateOffers}
      />
    </AppShell>
  );
}

function OfferCard({
  offer,
  side,
  onTrade,
}: {
  offer: OfferRow;
  side: "buy" | "sell";
  onTrade: () => void;
}) {
  return (
    <div className="rounded-2xl bg-card p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="h-9 w-9 rounded-full bg-[image:var(--gradient-primary)] flex items-center justify-center text-primary-foreground text-sm font-bold">
            {offer.merchant[0]}
          </div>
          <div>
            <div className="flex items-center gap-1.5 text-sm font-semibold">
              {offer.merchant}
              <Shield className="h-3.5 w-3.5 text-primary" />
            </div>
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <span className="flex items-center gap-0.5">
                <Star className="h-3 w-3 fill-current text-[color:var(--success)]" />
                {offer.rating != null ? `${offer.rating}%` : "new"}
              </span>
              {offer.deals != null && <span>{offer.deals} deals</span>}
            </div>
          </div>
        </div>
        <div className="text-right">
          <div className="text-lg font-bold">
            {offer.price} <span className="text-xs text-muted-foreground">{offer.fiat}</span>
          </div>
          <div className="text-[10px] text-muted-foreground">per 1 {offer.asset}</div>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        {offer.quoteKind === "crypto" ? (
          <span className="text-[11px] px-2 py-0.5 rounded-md bg-primary/15 text-primary font-medium">
            Crypto-to-crypto · on-chain
          </span>
        ) : (
          offer.methods.map((m) => (
            <span
              key={m}
              className="text-[11px] px-2 py-0.5 rounded-md bg-secondary text-muted-foreground capitalize"
            >
              {m}
            </span>
          ))
        )}
      </div>
      <div className="mt-3 flex items-end justify-between">
        <div className="text-xs text-muted-foreground">
          Limit: {offer.min ?? 0}–{(offer.max ?? 0).toLocaleString("en-US")} {offer.fiat}
        </div>
        <button
          onClick={onTrade}
          className="px-4 py-2 rounded-xl text-sm font-semibold text-primary-foreground"
          style={{
            background: side === "buy" ? "var(--gradient-primary)" : "var(--gradient-gold)",
          }}
        >
          Trade
        </button>
      </div>
    </div>
  );
}
