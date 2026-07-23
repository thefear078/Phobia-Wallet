import { useEffect, useState } from "react";
import { toast } from "sonner";
import { checkTorConnection } from "@/lib/wallet/tor";

const POLL_MS = 60_000;

export function TorStatus() {
  const [viaTor, setViaTor] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;

    const run = async () => {
      const ok = await checkTorConnection();
      if (!cancelled) setViaTor(ok);
    };

    void run();
    const id = window.setInterval(() => void run(), POLL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, []);

  const active = viaTor === true;
  const label = viaTor == null ? "…" : active ? "Tor" : "Clear";

  return (
    <button
      type="button"
      onClick={() =>
        toast.message("For maximum privacy, open Umbra via Tor Browser", {
          description: "Use the .onion mirror or Tor Browser with SOCKS5.",
        })
      }
      className="inline-flex items-center gap-1.5 text-[14px] text-muted-foreground hover:text-foreground transition-colors"
      title="Tor connection status"
    >
      <span
        className={`h-2 w-2 rounded-full shrink-0 ${
          viaTor == null
            ? "bg-muted-foreground/50"
            : active
              ? "bg-[color:var(--success)]"
              : "bg-muted-foreground"
        }`}
        aria-hidden
      />
      <span className="font-medium tabular-nums">{label}</span>
    </button>
  );
}
