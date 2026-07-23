import { Link, useRouterState } from "@tanstack/react-router";
import {
  Wallet,
  ArrowLeftRight,
  BarChart3,
  Users,
  Settings,
  ArrowUpRight,
  ArrowDownLeft,
  Shield,
} from "lucide-react";
import type { ReactNode } from "react";
import { useT } from "@/lib/i18n";
import { GraniteCredit } from "@/components/GraniteCredit";
import { TorStatus } from "@/components/TorStatus";
import { UmbrellaLogo } from "@/components/UmbrellaLogo";
import { useSession } from "@/lib/authStore";
import { ThemeToggleButton } from "@/components/ThemeToggle";

const DESKTOP_NAV = [
  { to: "/", labelKey: "wallet" as const, icon: Wallet },
  { to: "/exchange", labelKey: "exchange" as const, icon: ArrowLeftRight },
  { to: "/p2p", labelKey: "p2p" as const, icon: Users },
  { to: "/stats", labelKey: "stats" as const, icon: BarChart3 },
  { to: "/settings", labelKey: "profile" as const, icon: Settings },
];

export function AppShell({ children }: { children: ReactNode }) {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const t = useT();
  const session = useSession();

  const tabs = DESKTOP_NAV.map((item) => ({
    ...item,
    label: t[item.labelKey],
  }));

  return (
    <div className="app-scroll relative z-10 min-h-screen text-foreground flex justify-center md:items-center md:p-6">
      <div
        className="w-full max-w-md md:max-w-[1180px] md:h-[min(820px,calc(100vh-3rem))] min-h-screen md:min-h-0 flex flex-col md:overflow-hidden md:rounded-[10px] bg-background relative"
        style={{ boxShadow: "var(--shadow-window)" }}
      >
        {/* Desktop title bar */}
        <div className="hidden md:flex items-center justify-between h-9 pl-4 hairline-b bg-surface select-none shrink-0">
          <div className="flex items-center gap-3">
            <UmbrellaLogo className="h-3.5 w-3.5" />
            <span className="text-[11px] tracking-[0.28em] uppercase text-muted-foreground">
              Umbra <span className="text-foreground/70 ml-2">— Vault</span>
            </span>
          </div>
          <div className="flex items-center gap-3 pr-3">
            <TorStatus />
            <ThemeToggleButton />
          </div>
        </div>

        <div className="flex flex-1 min-h-0">
          {/* Desktop sidebar */}
          <aside className="hidden md:flex w-[220px] shrink-0 hairline-r bg-surface-2 flex-col">
            <div className="px-5 pt-6 pb-4">
              <div className="eyebrow">Wallet</div>
              <div className="mt-2">
                <div className="font-serif text-[22px] leading-none truncate">
                  {session.name || "Umbra User"}
                </div>
                <div className="mt-1.5 text-[11px] text-muted-foreground font-mono truncate">
                  {session.email || session.username || "local vault"}
                </div>
              </div>
            </div>

            <nav className="px-3 mt-2 space-y-0.5 flex-1">
              {tabs.map(({ to, label, icon: Icon }) => {
                const active = to === "/" ? pathname === "/" : pathname.startsWith(to);
                return (
                  <Link
                    key={to}
                    to={to}
                    className={`w-full flex items-center gap-3 px-3 h-9 rounded-sm text-[13px] transition-colors ${
                      active
                        ? "bg-elevated text-foreground"
                        : "text-muted-foreground hover:text-foreground hover:bg-elevated/60"
                    }`}
                  >
                    <span
                      className={`w-1 h-4 -ml-3 rounded-full ${active ? "bg-foreground" : "bg-transparent"}`}
                    />
                    <Icon className="h-[15px] w-[15px]" strokeWidth={1.5} />
                    <span>{label}</span>
                  </Link>
                );
              })}
            </nav>

            <div className="px-5 py-4 hairline-t space-y-3">
              <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                <span className="w-1.5 h-1.5 rounded-full bg-[color:var(--positive)]" />
                <span>Privacy · seed on-device</span>
              </div>
              <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                <Shield className="h-3.5 w-3.5" strokeWidth={1.5} />
                <span>Non-custodial · no telemetry</span>
              </div>
              <GraniteCredit compact className="opacity-70 justify-start" />
            </div>
          </aside>

          {/* Main column */}
          <div className="flex-1 flex flex-col min-w-0 min-h-0 relative pb-24 md:pb-0">
            <main className="flex-1 md:overflow-y-auto relative z-[1]">{children}</main>

            {/* Mobile footer meta */}
            <div className="md:hidden px-5 pb-2 pt-3 flex items-center justify-between gap-3">
              <GraniteCredit compact className="opacity-80" />
              <div className="flex items-center gap-3 shrink-0">
                <span className="text-[11px] text-muted-foreground tabular-nums">v1.0</span>
                <TorStatus />
              </div>
            </div>

            {/* Desktop status bar */}
            <div className="hidden md:flex sticky bottom-0 h-7 hairline-t bg-surface items-center justify-between px-5 text-[10.5px] text-muted-foreground font-mono shrink-0">
              <div className="flex items-center gap-4">
                <span className="flex items-center gap-1.5">
                  <span className="w-1.5 h-1.5 rounded-full bg-[color:var(--positive)]" />
                  Online
                </span>
                <span className="flex items-center gap-1">
                  <ArrowDownLeft className="h-3 w-3" /> Receive
                </span>
                <span className="flex items-center gap-1">
                  <ArrowUpRight className="h-3 w-3" /> Send
                </span>
              </div>
              <span>Umbra · signed build</span>
            </div>
          </div>
        </div>

        {/* Mobile bottom nav */}
        <nav
          className="md:hidden fixed bottom-0 left-1/2 -translate-x-1/2 w-full max-w-md border-t border-border bg-popover/95 backdrop-blur-xl tg-bottom-nav z-20"
          style={{ paddingBottom: "max(0px, env(safe-area-inset-bottom))" }}
        >
          <ul className="grid grid-cols-5">
            {tabs.map(({ to, label, icon: Icon }) => {
              const active = to === "/" ? pathname === "/" : pathname.startsWith(to);
              return (
                <li key={to}>
                  <Link
                    to={to}
                    className={`flex flex-col items-center gap-1 py-2.5 text-[11px] font-medium transition-colors ${
                      active ? "text-primary" : "text-muted-foreground hover:text-foreground"
                    }`}
                  >
                    <Icon className="h-5 w-5" strokeWidth={active ? 2.4 : 1.8} />
                    <span>{label}</span>
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
      </div>
    </div>
  );
}
