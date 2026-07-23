import { createFileRoute, Link } from "@tanstack/react-router";
import type { ReactNode } from "react";

export const Route = createFileRoute("/help")({
  component: HelpPage,
});

function HelpPage() {
  return (
    <AppShellLite>
      <h1 className="text-2xl font-bold">Help & FAQ</h1>
      <div className="mt-6 space-y-4 text-sm">
        <Faq
          q="Why can't Umbrella return my funds?"
          a="Umbrella does not hold your assets — settlement is directly between you and the counterparty."
        />
        <Faq
          q="What if the counterparty does not pay?"
          a="Open a dispute in the P2P deal, keep the tx hash / bank reference, and email support@umbra.wallet."
        />
        <Faq
          q="WalletConnect dropped during signing?"
          a="Reconnect in Settings → Wallet. The transaction is not signed until you confirm it in your wallet."
        />
        <Faq q="Bank revoked access?" a="Link Monobank again via personal token in settings." />
      </div>
      <p className="mt-8 text-muted-foreground text-xs">Support: support@umbra.wallet</p>
      <div className="mt-4 flex flex-wrap gap-3 text-xs">
        <Link to="/legal/privacy" className="text-primary">
          Privacy
        </Link>
        <Link to="/legal/terms" className="text-primary">
          Terms
        </Link>
        <Link to="/legal/agreement" className="text-primary">
          User agreement
        </Link>
        <Link to="/legal/rules" className="text-primary">
          Rules
        </Link>
      </div>
    </AppShellLite>
  );
}

function Faq({ q, a }: { q: string; a: string }) {
  return (
    <div className="rounded-2xl bg-card p-4 border border-border">
      <div className="font-semibold">{q}</div>
      <p className="mt-2 text-muted-foreground leading-relaxed">{a}</p>
    </div>
  );
}

function AppShellLite({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-background px-5 py-8 max-w-md mx-auto text-foreground">
      <Link to="/settings" className="text-sm text-muted-foreground">
        ← Settings
      </Link>
      {children}
    </div>
  );
}
