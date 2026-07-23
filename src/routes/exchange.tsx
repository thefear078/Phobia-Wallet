import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { AppShell } from "@/components/AppShell";
import { ArrowDownUp, TrendingUp, TrendingDown } from "lucide-react";
import { useMemo, useState } from "react";
import { useConvert, useMarketPrices } from "@/lib/market/prices";

export const Route = createFileRoute("/exchange")({
  validateSearch: (search: Record<string, unknown>) => ({
    asset: typeof search.asset === "string" ? search.asset : undefined,
    side: search.side === "sell" ? ("sell" as const) : ("buy" as const),
  }),
  head: () => ({
    meta: [
      { title: "Exchange — Umbrella Wallet" },
      { name: "description", content: "Rates and swaps via P2P." },
    ],
  }),
  component: ExchangePage,
});

const SYMBOLS = ["BTC", "ETH", "USDT", "USDC", "SOL", "TON", "XMR", "BNB", "MATIC"] as const;

function ExchangePage() {
  const navigate = useNavigate();
  const { prices, isLoading } = useMarketPrices();
  const [from, setFrom] = useState("USDT");
  const [to, setTo] = useState("BTC");
  const [amount, setAmount] = useState("100");

  const amountNum = Number.parseFloat(amount) || 0;
  const convert = useConvert(from, to, amountNum);

  const coinList = useMemo(() => {
    return SYMBOLS.map((symbol) => {
      const row = prices[symbol];
      return {
        symbol,
        usd: row?.usd ?? 0,
        change24h: row?.change24h ?? 0,
      };
    }).filter((c) => c.usd > 0 || isLoading);
  }, [prices, isLoading]);

  const received = convert.data?.result;
  const rate = convert.data?.rate ?? 0;

  const goToP2p = () => {
    void navigate({
      to: "/p2p",
      search: { asset: to, side: "buy" },
    });
  };

  return (
    <AppShell>
      <header className="px-5 pt-6 pb-4">
        <h1 className="text-2xl font-bold">Exchange</h1>
        <p className="text-sm text-muted-foreground">Live rates · convert · trade on P2P</p>
      </header>

      <section className="px-5 mb-6">
        <h2 className="text-base font-semibold mb-3">Markets</h2>
        <div className="rounded-2xl bg-card divide-y divide-border">
          {isLoading && coinList.length === 0 ? (
            <div className="p-4 text-sm text-muted-foreground">Loading prices…</div>
          ) : (
            coinList.map((c) => {
              const up = c.change24h >= 0;
              return (
                <button
                  key={c.symbol}
                  type="button"
                  onClick={() => {
                    setTo(c.symbol);
                    if (from === c.symbol) setFrom("USDT");
                  }}
                  className="w-full flex items-center justify-between p-3.5 text-left hover:bg-secondary/40 transition"
                >
                  <span className="font-semibold">{c.symbol}</span>
                  <div className="text-right">
                    <div className="font-semibold tabular-nums">
                      $
                      {c.usd.toLocaleString("en-US", {
                        maximumFractionDigits: c.usd >= 1 ? 2 : 4,
                      })}
                    </div>
                    <div
                      className={`text-xs flex items-center justify-end gap-1 ${
                        up ? "text-[color:var(--success)]" : "text-destructive"
                      }`}
                    >
                      {up ? (
                        <TrendingUp className="h-3 w-3" />
                      ) : (
                        <TrendingDown className="h-3 w-3" />
                      )}
                      {up ? "+" : ""}
                      {c.change24h.toFixed(2)}%
                    </div>
                  </div>
                </button>
              );
            })
          )}
        </div>
      </section>

      <section className="px-5 space-y-2 relative">
        <div className="rounded-2xl bg-card p-4">
          <div className="flex justify-between text-xs text-muted-foreground mb-1">
            <span>You pay</span>
            <span>
              $
              {(prices[from]?.usd ?? 0).toLocaleString("en-US", {
                maximumFractionDigits: 2,
              })}
            </span>
          </div>
          <div className="flex items-center gap-3">
            <input
              type="number"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className="bg-transparent text-2xl font-bold flex-1 outline-none w-0"
            />
            <select
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="bg-secondary rounded-xl px-3 py-2 text-sm font-semibold outline-none"
            >
              {SYMBOLS.map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex justify-center -my-3.5 relative z-10">
          <button
            type="button"
            onClick={() => {
              setFrom(to);
              setTo(from);
            }}
            className="h-10 w-10 rounded-full bg-primary text-primary-foreground flex items-center justify-center border-4 border-background"
            aria-label="Swap pair"
          >
            <ArrowDownUp className="h-4 w-4" />
          </button>
        </div>

        <div className="rounded-2xl bg-card p-4">
          <div className="flex justify-between text-xs text-muted-foreground mb-1">
            <span>You receive</span>
            <span>
              1 {from} ≈ {rate > 0 ? rate.toFixed(6) : "…"} {to}
            </span>
          </div>
          <div className="flex items-center gap-3">
            <div className="text-2xl font-bold flex-1 truncate tabular-nums">
              {convert.isFetching && received == null
                ? "…"
                : (received ?? 0).toLocaleString("en-US", { maximumFractionDigits: 8 })}
            </div>
            <select
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className="bg-secondary rounded-xl px-3 py-2 text-sm font-semibold outline-none"
            >
              {SYMBOLS.map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </div>
        </div>

        <button
          type="button"
          onClick={goToP2p}
          className="w-full mt-3 py-3.5 rounded-2xl font-semibold text-primary-foreground shadow-[var(--shadow-glow)]"
          style={{ background: "var(--gradient-primary)" }}
        >
          Trade on P2P
        </button>
      </section>
    </AppShell>
  );
}
