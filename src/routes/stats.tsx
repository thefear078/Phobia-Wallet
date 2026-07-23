import { createFileRoute } from "@tanstack/react-router";
import { AppShell } from "@/components/AppShell";
import { TrendingUp, TrendingDown, AlertCircle } from "lucide-react";
import { useMemo, useState } from "react";
import { usePortfolio } from "@/hooks/usePortfolio";
import { useMarketPrices, useSparkline } from "@/lib/market/prices";
import { PriceChart } from "@/components/PriceChart";
import { LinkAccountsPrompt } from "@/components/LinkAccountsPrompt";

export const Route = createFileRoute("/stats")({
  head: () => ({ meta: [{ title: "Stats — Umbrella Wallet" }] }),
  component: StatsPage,
});

type Fiat = "USD" | "UAH" | "EUR";

const CHART_SYMBOLS = ["BTC", "ETH", "SOL", "XMR", "USDT"];

function formatFiat(v: number, fiat: Fiat): string {
  const symbol = fiat === "USD" ? "$" : fiat === "EUR" ? "€" : "₴";
  if (v >= 1000) return `${symbol}${v.toLocaleString("en-US", { maximumFractionDigits: 0 })}`;
  return `${symbol}${v.toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
}

function SparklineRow({
  symbol,
  change24h,
  color,
}: {
  symbol: string;
  change24h: number;
  color: string;
}) {
  const spark = useSparkline(symbol);
  const up = change24h >= 0;
  return (
    <div className="flex items-center justify-between rounded-2xl bg-card p-3.5 gap-3">
      <div className="min-w-0">
        <div className="font-semibold text-sm">{symbol}</div>
        <div className={`text-xs ${up ? "text-[color:var(--success)]" : "text-destructive"}`}>
          {up ? "+" : ""}
          {change24h.toFixed(2)}% 24h
        </div>
      </div>
      <PriceChart
        data={spark.data?.prices ?? []}
        color={up ? "var(--success)" : "var(--destructive)"}
        width={120}
        height={40}
      />
      <span className="sr-only" style={{ color }}>
        {symbol}
      </span>
    </div>
  );
}

function StatsPage() {
  const [fiat, setFiat] = useState<Fiat>("USD");
  const { assets, totalUsd, totalUah, avgChange, hasLinks, isLoading, uahPerUsd } = usePortfolio();
  const { prices, isLoading: marketLoading } = useMarketPrices();

  const totalEur = useMemo(() => {
    const usdtEur = prices.USDT?.eur;
    if (usdtEur && usdtEur > 0) return totalUsd * usdtEur;
    return totalUsd * 0.92;
  }, [prices.USDT?.eur, totalUsd]);

  const totalDisplay = fiat === "USD" ? totalUsd : fiat === "UAH" ? totalUah : totalEur;

  const sorted = [...assets].sort((a, b) => b.valueUsd - a.valueUsd);

  const { topGainer, topLoser } = useMemo(() => {
    if (assets.length === 0) return { topGainer: null, topLoser: null };
    const byChange = [...assets].sort((a, b) => b.change24h - a.change24h);
    return { topGainer: byChange[0], topLoser: byChange[byChange.length - 1] };
  }, [assets]);

  const pieGradient = useMemo(() => {
    if (sorted.length === 0 || totalUsd <= 0) return "var(--secondary)";
    let acc = 0;
    const stops: string[] = [];
    for (const a of sorted) {
      const pct = (a.valueUsd / totalUsd) * 100;
      const start = acc;
      acc += pct;
      stops.push(`${a.color} ${start.toFixed(2)}% ${acc.toFixed(2)}%`);
    }
    return `conic-gradient(${stops.join(", ")})`;
  }, [sorted, totalUsd]);

  return (
    <AppShell>
      <header className="px-5 pt-6 pb-4">
        <h1 className="text-2xl font-bold">Stats</h1>
        <p className="text-sm text-muted-foreground">Portfolio and market in real time</p>
      </header>

      {!hasLinks && !isLoading && (
        <section className="px-5 mb-4">
          <LinkAccountsPrompt compact />
        </section>
      )}

      <section className="px-5">
        <div
          className="rounded-3xl p-5"
          style={{ background: "var(--gradient-card)", boxShadow: "var(--shadow-card)" }}
        >
          <div className="flex items-center justify-between">
            <div className="text-xs text-muted-foreground">Portfolio value</div>
            <div className="flex gap-1">
              {(["USD", "UAH", "EUR"] as const).map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setFiat(c)}
                  className={`px-2 py-0.5 rounded-md text-[10px] font-semibold ${
                    fiat === c
                      ? "bg-primary text-primary-foreground"
                      : "bg-secondary text-muted-foreground"
                  }`}
                >
                  {c}
                </button>
              ))}
            </div>
          </div>
          <div className="text-3xl font-bold mt-1 tabular-nums">
            {isLoading ? "…" : formatFiat(totalDisplay, fiat)}
          </div>
          <div
            className={`text-sm mt-1 ${avgChange >= 0 ? "text-[color:var(--success)]" : "text-destructive"}`}
          >
            {avgChange >= 0 ? "+" : ""}
            {avgChange.toFixed(2)}% in 24h
            {fiat === "UAH" && (
              <span className="text-muted-foreground text-xs ml-2">
                · {uahPerUsd.toFixed(2)} UAH/USD
              </span>
            )}
          </div>
        </div>
      </section>

      <section className="px-5 mt-6">
        <h2 className="text-base font-semibold mb-3">Holdings movers</h2>
        <div className="grid grid-cols-2 gap-3">
          <div className="rounded-2xl bg-card p-3">
            <div className="text-[11px] text-muted-foreground mb-1">Top gainer</div>
            {topGainer ? (
              <>
                <div className="font-semibold">{topGainer.symbol}</div>
                <div className="text-sm text-[color:var(--success)] flex items-center gap-1">
                  <TrendingUp className="h-3.5 w-3.5" />+{topGainer.change24h.toFixed(2)}%
                </div>
              </>
            ) : (
              <div className="text-sm text-muted-foreground">—</div>
            )}
          </div>
          <div className="rounded-2xl bg-card p-3">
            <div className="text-[11px] text-muted-foreground mb-1">Top loser</div>
            {topLoser ? (
              <>
                <div className="font-semibold">{topLoser.symbol}</div>
                <div className="text-sm text-destructive flex items-center gap-1">
                  <TrendingDown className="h-3.5 w-3.5" />
                  {topLoser.change24h.toFixed(2)}%
                </div>
              </>
            ) : (
              <div className="text-sm text-muted-foreground">—</div>
            )}
          </div>
        </div>
      </section>

      <section className="px-5 mt-6">
        <h2 className="text-base font-semibold mb-3">Asset allocation</h2>
        {sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground flex items-center gap-2">
            <AlertCircle className="h-4 w-4" /> No data
          </p>
        ) : (
          <div className="rounded-2xl bg-card p-4 space-y-4">
            <div
              className="mx-auto h-28 w-28 rounded-full"
              style={{ background: pieGradient }}
              role="img"
              aria-label="Portfolio allocation pie chart"
            />
            <div className="space-y-3">
              {sorted.map((a) => {
                const value =
                  fiat === "USD"
                    ? a.valueUsd
                    : fiat === "UAH"
                      ? a.valueUah
                      : a.valueUsd * (prices.USDT?.eur || 0.92);
                const pct = totalUsd > 0 ? (a.valueUsd / totalUsd) * 100 : 0;
                return (
                  <div key={a.symbol}>
                    <div className="flex justify-between text-sm">
                      <span className="font-medium flex items-center gap-2">
                        <span
                          className="h-2 w-2 rounded-full shrink-0"
                          style={{ background: a.color }}
                        />
                        {a.symbol}
                      </span>
                      <span className="text-muted-foreground tabular-nums">
                        {pct.toFixed(1)}% · {formatFiat(value, fiat)}
                      </span>
                    </div>
                    <div className="h-1.5 rounded-full bg-secondary mt-1.5 overflow-hidden">
                      <div
                        className="h-full rounded-full"
                        style={{ width: `${pct}%`, background: a.color }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        )}
      </section>

      <section className="px-5 mt-6">
        <h2 className="text-base font-semibold mb-3">7-day charts</h2>
        <div className="space-y-2">
          {(sorted.length ? sorted.map((a) => a.symbol) : CHART_SYMBOLS).slice(0, 5).map((sym) => (
            <SparklineRow
              key={sym}
              symbol={sym}
              change24h={
                prices[sym]?.change24h ?? assets.find((a) => a.symbol === sym)?.change24h ?? 0
              }
              color={assets.find((a) => a.symbol === sym)?.color ?? "var(--primary)"}
            />
          ))}
        </div>
      </section>

      <section className="px-5 mt-6 pb-8">
        <h2 className="text-base font-semibold mb-3">Market</h2>
        <div className="grid grid-cols-2 gap-3">
          {marketLoading && Object.keys(prices).length === 0 ? (
            <div className="col-span-2 text-sm text-muted-foreground">Loading…</div>
          ) : (
            Object.entries(prices)
              .slice(0, 6)
              .map(([symbol, row]) => {
                const up = row.change24h >= 0;
                return (
                  <div key={symbol} className="rounded-2xl bg-card p-3 text-left">
                    <span className="text-sm font-semibold">{symbol}</span>
                    <div className="text-lg font-bold mt-2 tabular-nums">
                      ${row.usd.toLocaleString("en-US")}
                    </div>
                    <div
                      className={`text-xs flex items-center gap-1 ${
                        up ? "text-[color:var(--success)]" : "text-destructive"
                      }`}
                    >
                      {up ? (
                        <TrendingUp className="h-3 w-3" />
                      ) : (
                        <TrendingDown className="h-3 w-3" />
                      )}
                      {up ? "+" : ""}
                      {row.change24h.toFixed(2)}%
                    </div>
                  </div>
                );
              })
          )}
        </div>
      </section>
    </AppShell>
  );
}
