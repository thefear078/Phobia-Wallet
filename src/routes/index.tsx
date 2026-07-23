import { createFileRoute, Link } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { AppShell } from "@/components/AppShell";
import { api } from "@/lib/api/client";
import {
  ArrowDownLeft,
  ArrowUpRight,
  ArrowLeftRight,
  Eye,
  EyeOff,
  QrCode,
  Bell,
  Copy,
  Check,
  AlertCircle,
  RefreshCw,
} from "lucide-react";
import { useMemo, useState } from "react";
import { toast } from "sonner";
import { useProfile } from "@/lib/profileStore";
import { useT } from "@/lib/i18n";
import { useSession } from "@/lib/authStore";
import { UmbrellaLogo } from "@/components/UmbrellaLogo";
import { ThemeToggleButton } from "@/components/ThemeToggle";
import { usePortfolio } from "@/hooks/usePortfolio";
import { LinkAccountsPrompt } from "@/components/LinkAccountsPrompt";
import { WalletSheets } from "@/components/wallet/WalletSheets";
import { PriceChart } from "@/components/PriceChart";
import { useSparkline } from "@/lib/market/prices";
import { hapticImpact, isTelegramMiniApp } from "@/lib/telegram/telegramApp";

export const Route = createFileRoute("/")({
  component: Index,
});

type SheetType = null | "deposit" | "withdraw" | "scan" | "notif";

function formatMoney(n: number) {
  const [main, cents] = n
    .toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    .split(".");
  return { main, cents: cents ?? "00" };
}

function Index() {
  const {
    assets,
    totalUsd,
    totalUah,
    avgChange,
    wallets,
    banks,
    hasLinks,
    isLoading,
    error,
    refetch,
  } = usePortfolio();
  const p = useProfile();
  const t = useT();
  const session = useSession();
  const [sheet, setSheet] = useState<SheetType>(null);
  const [hide, setHide] = useState(false);
  const [copied, setCopied] = useState(false);
  const [chainFilter, setChainFilter] = useState<string>("All");

  const btcSpark = useSparkline("BTC");

  const { data: recentOrders = [] } = useQuery({
    queryKey: ["p2p-orders", "recent"],
    queryFn: () => api.listP2pOrders(),
    retry: false,
    staleTime: 30_000,
  });

  const primaryWallet = wallets[0];
  const address = primaryWallet?.address ?? "—";
  const revokedBanks = banks.filter((b) => b.status !== "active");
  const inTg = isTelegramMiniApp();
  const money = formatMoney(totalUsd);
  const deltaUsd = totalUsd * (avgChange / 100);

  const chains = useMemo(() => {
    const set = new Set(wallets.map((w) => w.chain));
    return ["All", ...[...set].map((c) => c[0].toUpperCase() + c.slice(1))];
  }, [wallets]);

  const filteredAssets = useMemo(() => {
    if (chainFilter === "All") return assets;
    const key = chainFilter.toLowerCase();
    return assets.filter((a) => {
      const map: Record<string, string[]> = {
        ethereum: ["ETH"],
        bitcoin: ["BTC"],
        solana: ["SOL"],
        tron: ["TRX"],
        monero: ["XMR"],
        polygon: ["MATIC"],
        bsc: ["BNB"],
      };
      return (map[key] ?? [key.toUpperCase()]).includes(a.symbol);
    });
  }, [assets, chainFilter]);

  const openSheet = (s: SheetType) => {
    hapticImpact("light");
    setSheet(s);
  };

  const copy = async () => {
    if (address === "—") return;
    try {
      await navigator.clipboard.writeText(address);
    } catch {
      /* ignore */
    }
    hapticImpact("light");
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
    toast.success(t.addressCopied);
  };

  return (
    <AppShell>
      {/* Mobile header */}
      <header className="md:hidden tg-header flex items-center justify-between px-5 pb-4">
        <div className="flex items-center gap-3">
          <div className="h-10 w-10 rounded-full bg-secondary flex items-center justify-center border border-border/60">
            <UmbrellaLogo className="h-7 w-7" />
          </div>
          <div>
            <p className="text-xs text-muted-foreground">{t.greeting}</p>
            <p className="text-sm font-semibold">{session.name || "Umbra User"}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <ThemeToggleButton />
          <button
            onClick={() => openSheet("notif")}
            aria-label="Notifications"
            className="h-10 w-10 rounded-full bg-secondary flex items-center justify-center relative hover:bg-accent/20 transition"
          >
            <Bell className="h-5 w-5" />
            {p.push && <span className="absolute top-2 right-2 h-2 w-2 rounded-full bg-primary" />}
          </button>
        </div>
      </header>

      <div className="px-5 md:px-8 md:pt-8 space-y-6 md:space-y-10 pb-8">
        {inTg && (
          <div className="flex items-center gap-2 rounded-sm hairline bg-elevated/40 px-3 py-2 text-[11px] text-muted-foreground">
            <span className="h-1.5 w-1.5 rounded-full bg-[color:var(--positive)] animate-pulse" />
            Telegram Mini App · private mode
          </div>
        )}

        {error && (
          <div className="flex gap-2 rounded-sm hairline bg-destructive/10 p-3 text-xs">
            <AlertCircle className="h-4 w-4 shrink-0" />
            <span>
              Could not load balances.{" "}
              <button type="button" className="underline" onClick={() => refetch()}>
                Try again
              </button>
            </span>
          </div>
        )}

        {revokedBanks.length > 0 && (
          <div className="rounded-sm hairline border-amber-500/40 bg-amber-500/10 p-3 text-xs">
            Bank access revoked. Relink in{" "}
            <Link to="/settings" className="underline">
              settings
            </Link>
            .
          </div>
        )}

        {!hasLinks && !isLoading && <LinkAccountsPrompt compact onLinked={() => refetch()} />}

        {/* Balance hero */}
        <section>
          <div className="flex items-baseline justify-between">
            <div className="eyebrow">{t.totalBalance} · USD</div>
            <div className="flex items-center gap-3">
              <button
                onClick={() => refetch()}
                className="text-muted-foreground hover:text-foreground"
                aria-label="Refresh"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${isLoading ? "animate-spin" : ""}`} />
              </button>
              <button
                onClick={() => setHide((h) => !h)}
                className="text-[11px] text-muted-foreground hover:text-foreground flex items-center gap-1.5"
              >
                {hide ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                {hide ? "Show" : "Hide"}
              </button>
            </div>
          </div>

          <div className="mt-3 flex flex-wrap items-end gap-4 md:gap-5">
            <div className="font-serif tabular text-[48px] md:text-[72px] leading-[0.9] tracking-tight">
              {isLoading ? (
                "…"
              ) : hide ? (
                "•••••••"
              ) : (
                <>
                  <span className="text-muted-foreground/70">$</span>
                  {money.main}
                  <span className="text-[22px] md:text-[32px] text-muted-foreground/80 align-baseline">
                    .{money.cents}
                  </span>
                </>
              )}
            </div>
            <div className="pb-2 flex flex-col gap-0.5 text-[12px]">
              <span
                className={`tabular ${avgChange >= 0 ? "text-[color:var(--positive)]" : "text-destructive"}`}
              >
                {hide ? "••" : `${avgChange >= 0 ? "▲" : "▼"} ${Math.abs(avgChange).toFixed(2)}%`}
              </span>
              <span className="text-muted-foreground tabular">
                {hide
                  ? "••••"
                  : `${deltaUsd >= 0 ? "+" : ""}$${Math.abs(deltaUsd).toFixed(2)} · 24h`}
              </span>
              <span className="text-muted-foreground tabular md:hidden">
                {hide
                  ? "••••"
                  : `≈ ${totalUah.toLocaleString("uk-UA", { maximumFractionDigits: 0 })} ₴`}
              </span>
            </div>
          </div>

          <div className="mt-4 md:mt-5">
            <PriceChart
              data={btcSpark.data?.prices ?? []}
              color="currentColor"
              width={720}
              height={56}
              className="w-full max-w-full text-foreground opacity-90"
            />
          </div>

          <button
            onClick={() => void copy()}
            className="mt-3 flex items-center gap-2 text-[11px] text-muted-foreground font-mono truncate max-w-full hover:text-foreground"
          >
            {copied ? (
              <Check className="h-3.5 w-3.5 text-[color:var(--positive)]" />
            ) : (
              <Copy className="h-3.5 w-3.5" />
            )}
            {hide ? "••••••••" : address}
          </button>

          {/* Quick actions */}
          <div className="mt-5 grid grid-cols-4 gap-0 hairline rounded-md overflow-hidden">
            <button
              onClick={() => openSheet("withdraw")}
              className="group flex flex-col md:flex-row items-center justify-center gap-1.5 md:gap-2.5 h-14 text-[11px] md:text-[13px] hover:bg-elevated transition-colors"
            >
              <ArrowUpRight className="h-4 w-4 text-muted-foreground group-hover:text-foreground" />
              {t.withdraw}
            </button>
            <button
              onClick={() => openSheet("deposit")}
              className="group flex flex-col md:flex-row items-center justify-center gap-1.5 md:gap-2.5 h-14 text-[11px] md:text-[13px] hover:bg-elevated transition-colors hairline-l"
            >
              <ArrowDownLeft className="h-4 w-4 text-muted-foreground group-hover:text-foreground" />
              {t.deposit}
            </button>
            <Link
              to="/exchange"
              search={{ side: "buy", asset: undefined }}
              className="group flex flex-col md:flex-row items-center justify-center gap-1.5 md:gap-2.5 h-14 text-[11px] md:text-[13px] hover:bg-elevated transition-colors hairline-l"
            >
              <ArrowLeftRight className="h-4 w-4 text-muted-foreground group-hover:text-foreground" />
              {t.swap}
            </Link>
            <button
              onClick={() => openSheet("scan")}
              className="group flex flex-col md:flex-row items-center justify-center gap-1.5 md:gap-2.5 h-14 text-[11px] md:text-[13px] hover:bg-elevated transition-colors hairline-l"
            >
              <QrCode className="h-4 w-4 text-muted-foreground group-hover:text-foreground" />
              {t.scan}
            </button>
          </div>
        </section>

        {/* Holdings */}
        <section>
          <header className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="flex items-baseline gap-3">
              <h2 className="font-serif text-[22px] md:text-[26px]">{t.assets}</h2>
              <span className="eyebrow hidden sm:inline">{filteredAssets.length} assets</span>
            </div>
            <div className="flex hairline rounded-sm overflow-hidden self-start overflow-x-auto max-w-full">
              {chains.map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setChainFilter(c)}
                  className={`px-3 h-7 text-[11.5px] tracking-wide whitespace-nowrap ${
                    chainFilter === c
                      ? "bg-foreground text-primary-foreground"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {c}
                </button>
              ))}
            </div>
          </header>

          {filteredAssets.length === 0 && !isLoading ? (
            <p className="text-sm text-muted-foreground py-8 text-center hairline rounded-md mt-4">
              No assets — link a wallet with a balance
            </p>
          ) : (
            <>
              {/* Desktop table */}
              <div className="hidden md:block mt-5 hairline rounded-md overflow-hidden">
                <div className="grid grid-cols-[1.6fr_1fr_1fr_1fr_0.6fr] px-5 h-9 items-center bg-surface/60 eyebrow">
                  <span>Asset</span>
                  <span className="text-right">Price</span>
                  <span className="text-right">Holdings</span>
                  <span className="text-right">Value</span>
                  <span className="text-right">24h</span>
                </div>
                {filteredAssets.map((a, i) => {
                  const pct = totalUsd > 0 ? a.valueUsd / totalUsd : 0;
                  return (
                    <div
                      key={a.symbol}
                      className={`grid grid-cols-[1.6fr_1fr_1fr_1fr_0.6fr] px-5 h-16 items-center ${
                        i > 0 ? "hairline-t" : ""
                      } hover:bg-elevated/40 transition-colors`}
                    >
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 hairline rounded-full grid place-items-center bg-elevated font-mono text-[10px]">
                          {a.symbol.slice(0, 3)}
                        </div>
                        <div>
                          <div className="text-[13.5px]">{a.name}</div>
                          <div className="text-[11px] text-muted-foreground font-mono">
                            {a.symbol}
                          </div>
                        </div>
                      </div>
                      <div className="text-right tabular text-[13px]">
                        ${a.price.toLocaleString("en-US", { maximumFractionDigits: 2 })}
                      </div>
                      <div className="text-right tabular text-[13px] text-muted-foreground">
                        {hide ? "••••" : a.balance.toFixed(4)} {a.symbol}
                      </div>
                      <div className="text-right">
                        <div className="tabular text-[13.5px]">
                          {hide ? "••••" : `$${a.valueUsd.toFixed(2)}`}
                        </div>
                        <div className="mt-1 h-[2px] w-full bg-border overflow-hidden">
                          <div
                            className="h-full bg-foreground/80"
                            style={{ width: `${Math.min(100, pct * 100)}%` }}
                          />
                        </div>
                      </div>
                      <div
                        className={`text-right tabular text-[12.5px] ${
                          a.change24h > 0
                            ? "text-[color:var(--positive)]"
                            : a.change24h < 0
                              ? "text-destructive"
                              : "text-muted-foreground"
                        }`}
                      >
                        {a.change24h > 0 ? "▲" : a.change24h < 0 ? "▼" : "·"}{" "}
                        {Math.abs(a.change24h).toFixed(2)}%
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Mobile cards */}
              <div className="md:hidden mt-4 space-y-2">
                {filteredAssets.map((a) => (
                  <div
                    key={a.symbol}
                    className="flex items-center gap-3 hairline rounded-md bg-card p-3.5"
                  >
                    <div
                      className="h-10 w-10 rounded-full flex items-center justify-center text-sm font-bold text-primary-foreground"
                      style={{ background: a.color }}
                    >
                      {a.symbol[0]}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-sm">{a.symbol}</div>
                      <div className="text-xs text-muted-foreground">
                        {hide ? "••••" : a.balance.toFixed(4)}
                      </div>
                    </div>
                    <div className="text-right">
                      <div className="text-sm font-semibold tabular">
                        {hide ? "••••" : `$${a.valueUsd.toFixed(2)}`}
                      </div>
                      <div
                        className={`text-xs ${a.change24h >= 0 ? "text-[color:var(--positive)]" : "text-destructive"}`}
                      >
                        {a.change24h >= 0 ? "+" : ""}
                        {a.change24h.toFixed(2)}%
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </section>

        {/* Recent activity */}
        <section className="pb-2">
          <div className="flex items-center justify-between mb-3 md:mb-5">
            <h2 className="font-serif text-[22px] md:text-[26px]">{t.recent}</h2>
            <Link to="/p2p" className="eyebrow hover:text-foreground">
              {t.all} →
            </Link>
          </div>
          {recentOrders.length === 0 ? (
            <p className="text-sm text-muted-foreground py-6 text-center hairline rounded-md">
              No deals yet — create your first one on P2P
            </p>
          ) : (
            <div className="hairline rounded-md overflow-hidden">
              {recentOrders.slice(0, 5).map((o, i) => {
                const isBuyer = o.buyerId === session.userId;
                const Icon = isBuyer ? ArrowDownLeft : ArrowUpRight;
                const sign = isBuyer ? "+" : "-";
                const asset = o.offer?.asset ?? "";
                const when = o.updatedAt
                  ? new Date(o.updatedAt).toLocaleDateString("uk-UA", {
                      day: "numeric",
                      month: "short",
                      hour: "2-digit",
                      minute: "2-digit",
                    })
                  : "";
                return (
                  <div
                    key={o.id}
                    className={`grid grid-cols-[auto_1fr_auto] md:grid-cols-[auto_1fr_auto_auto] items-center gap-3 md:gap-4 px-4 md:px-5 h-14 ${
                      i > 0 ? "hairline-t" : ""
                    }`}
                  >
                    <div className="w-7 h-7 grid place-items-center hairline rounded-sm text-muted-foreground">
                      <Icon className="h-3.5 w-3.5" />
                    </div>
                    <div className="min-w-0">
                      <div className="text-[13px] truncate">
                        P2P · {isBuyer ? "buy" : "sell"}{" "}
                        <span className="text-muted-foreground">{asset}</span>
                      </div>
                      <div className="text-[11px] text-muted-foreground font-mono truncate">
                        {when} · {o.status}
                      </div>
                    </div>
                    <div className="text-right tabular text-[12.5px]">
                      {sign}
                      {o.amount} {asset}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </section>
      </div>

      <WalletSheets
        sheet={sheet}
        onClose={() => setSheet(null)}
        address={address}
        assets={assets}
      />
    </AppShell>
  );
}
