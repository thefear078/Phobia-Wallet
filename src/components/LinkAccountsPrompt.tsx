import { useState } from "react";
import { Wallet, Landmark, AlertCircle, RefreshCw, Globe, Link2 } from "lucide-react";
import { toast } from "sonner";
import {
  connectWalletConnect,
  connectInjectedWallet,
  getWalletConnectProjectId,
  hasInjectedWallet,
  MANUAL_CHAIN_OPTIONS,
  signLinkProof,
  validateManualAddress,
} from "@/lib/wallet/walletConnect";
import { api } from "@/lib/api/client";
import { isDemoMode } from "@/lib/demoMode";
import { skipLinkPrompt } from "@/lib/linkPromptSkip";
import { GraniteCredit } from "@/components/GraniteCredit";

type Props = {
  onLinked?: () => void;
  onSkip?: () => void;
  compact?: boolean;
};

export function LinkAccountsPrompt({ onLinked, onSkip, compact }: Props) {
  const [loading, setLoading] = useState<"wc" | "inj" | "mono" | "manual" | null>(null);
  const [monoToken, setMonoToken] = useState("");
  const [showMono, setShowMono] = useState(false);
  const [showManual, setShowManual] = useState(false);
  const [manualChain, setManualChain] = useState<string>("ethereum");
  const [manualAddress, setManualAddress] = useState("");
  const [manualLabel, setManualLabel] = useState("");

  const handleSkip = () => {
    skipLinkPrompt();
    onSkip?.();
    onLinked?.();
  };

  const linkWallet = async (
    fn: () => Promise<{ address: string; chain: string; label: string }>,
    key: "wc" | "inj",
  ) => {
    setLoading(key);
    try {
      const { address, chain, label } = await fn();
      const challenge = await api.walletChallenge();
      const signature = await signLinkProof(challenge.message, address);
      await api.linkWallet({
        chain,
        address,
        label,
        message: challenge.message,
        signature,
      });
      toast.success("Wallet linked with ownership proof");
      onLinked?.();
    } catch (e) {
      const msg = e instanceof Error ? e.message : "Connection error";
      if (msg.includes("User rejected") || msg.includes("rejected")) {
        toast.error("Connection cancelled in wallet");
      } else {
        toast.error(msg);
      }
    } finally {
      setLoading(null);
    }
  };

  const linkManual = async () => {
    setLoading("manual");
    try {
      const address = validateManualAddress(manualChain, manualAddress);
      const chain = manualChain === "other" ? "other" : manualChain;
      await api.linkWallet({
        chain,
        address,
        label: manualLabel.trim() || "Watch-only",
        watchOnly: true,
      });
      toast.success("Address added (read-only)");
      setManualAddress("");
      setShowManual(false);
      onLinked?.();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error");
    } finally {
      setLoading(null);
    }
  };

  const linkMono = async () => {
    if (!monoToken.trim()) {
      toast.error("Paste your Monobank personal token");
      return;
    }
    setLoading("mono");
    try {
      const res = await api.linkMonobank(monoToken.trim());
      toast.success(`Linked ${res.linked.length} Monobank account(s)`);
      setMonoToken("");
      setShowMono(false);
      onLinked?.();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Monobank error");
    } finally {
      setLoading(null);
    }
  };

  if (compact) {
    return (
      <div className="rounded-2xl border border-dashed border-border p-4 text-center space-y-3">
        <p className="text-sm text-muted-foreground">Link a wallet or bank for real balances</p>
        <div className="flex flex-wrap gap-2 justify-center">
          {hasInjectedWallet() && (
            <button
              onClick={() => void linkWallet(connectInjectedWallet, "inj")}
              disabled={loading === "inj"}
              className="px-4 py-2 rounded-xl bg-primary text-primary-foreground text-sm font-medium"
            >
              Browser
            </button>
          )}
          <button
            onClick={() => void linkWallet(connectWalletConnect, "wc")}
            disabled={loading === "wc"}
            className="px-4 py-2 rounded-xl bg-primary text-primary-foreground text-sm font-medium"
          >
            WalletConnect
          </button>
          <button
            onClick={() => setShowManual(true)}
            className="px-4 py-2 rounded-xl border border-border text-sm"
          >
            Address
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-[70vh] flex flex-col justify-center px-6 py-10 max-w-md mx-auto">
      <h2 className="text-2xl font-semibold tracking-tight">Almost ready! 🌑</h2>
      <p className="mt-2 text-sm text-muted-foreground leading-relaxed">
        Connect a wallet or bank — Umbrella will show balances and help with P2P. Keys stay with
        you.
      </p>

      {!getWalletConnectProjectId() && (
        <div className="mt-4 flex gap-2 rounded-xl border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-100/90">
          <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" />
          <span>
            {isDemoMode()
              ? "In demo mode, WalletConnect is simulated locally."
              : "For WalletConnect, add VITE_WALLETCONNECT_PROJECT_ID to .env (Reown Cloud)."}
          </span>
        </div>
      )}

      {hasInjectedWallet() && (
        <button
          onClick={() => void linkWallet(connectInjectedWallet, "inj")}
          disabled={loading === "inj"}
          className="mt-6 w-full flex items-center gap-3 rounded-2xl bg-card border border-border p-4 hover:bg-secondary/40 transition disabled:opacity-50"
        >
          <Globe className="h-6 w-6 text-primary" />
          <div className="text-left flex-1">
            <div className="font-semibold text-sm">Browser wallet</div>
            <div className="text-xs text-muted-foreground">MetaMask, Rabby, Coinbase Wallet…</div>
          </div>
          {loading === "inj" ? <RefreshCw className="h-4 w-4 animate-spin" /> : null}
        </button>
      )}

      <button
        onClick={() => void linkWallet(connectWalletConnect, "wc")}
        disabled={loading === "wc" || !getWalletConnectProjectId()}
        className="mt-3 w-full flex items-center gap-3 rounded-2xl bg-card border border-border p-4 hover:bg-secondary/40 transition disabled:opacity-50"
      >
        <Wallet className="h-6 w-6 text-primary" />
        <div className="text-left flex-1">
          <div className="font-semibold text-sm">WalletConnect</div>
          <div className="text-xs text-muted-foreground">Any mobile or desktop wallet with QR</div>
        </div>
        {loading === "wc" ? <RefreshCw className="h-4 w-4 animate-spin" /> : null}
      </button>

      <button
        onClick={() => setShowManual((v) => !v)}
        className="mt-3 w-full flex items-center gap-3 rounded-2xl bg-card border border-border p-4 hover:bg-secondary/40 transition"
      >
        <Link2 className="h-6 w-6 text-primary" />
        <div className="text-left flex-1">
          <div className="font-semibold text-sm">Add address manually</div>
          <div className="text-xs text-muted-foreground">
            BTC, ETH, TON, SOL, TRON and others (balance view)
          </div>
        </div>
      </button>

      {showManual && (
        <div className="mt-3 space-y-2 rounded-2xl border border-border p-4 bg-card/50">
          <select
            value={manualChain}
            onChange={(e) => setManualChain(e.target.value)}
            className="w-full h-11 rounded-xl bg-secondary px-3 text-sm"
          >
            {MANUAL_CHAIN_OPTIONS.map((c) => (
              <option key={c.id} value={c.id}>
                {c.label}
              </option>
            ))}
          </select>
          <input
            placeholder="Public wallet address"
            value={manualAddress}
            onChange={(e) => setManualAddress(e.target.value)}
            className="w-full h-11 rounded-xl bg-secondary px-3 text-sm font-mono text-xs"
          />
          <input
            placeholder="Label (optional)"
            value={manualLabel}
            onChange={(e) => setManualLabel(e.target.value)}
            className="w-full h-11 rounded-xl bg-secondary px-3 text-sm"
          />
          <button
            onClick={() => void linkManual()}
            disabled={loading === "manual"}
            className="w-full h-11 rounded-xl bg-primary text-primary-foreground font-semibold text-sm"
          >
            {loading === "manual" ? "Adding…" : "Link address"}
          </button>
        </div>
      )}

      <button
        onClick={() => setShowMono((v) => !v)}
        className="mt-3 w-full flex items-center gap-3 rounded-2xl bg-card border border-border p-4 hover:bg-secondary/40 transition"
      >
        <Landmark className="h-6 w-6 text-primary" />
        <div className="text-left flex-1">
          <div className="font-semibold text-sm">Monobank</div>
          <div className="text-xs text-muted-foreground">Personal token from the bank app</div>
        </div>
      </button>

      {showMono && (
        <div className="mt-3 space-y-2">
          <input
            type="password"
            placeholder="X-Token from Monobank"
            value={monoToken}
            onChange={(e) => setMonoToken(e.target.value)}
            className="w-full h-11 rounded-xl bg-secondary px-3 text-sm"
          />
          <button
            onClick={() => void linkMono()}
            disabled={loading === "mono"}
            className="w-full h-11 rounded-xl bg-primary text-primary-foreground font-semibold text-sm"
          >
            {loading === "mono" ? "Connecting…" : "Link Monobank"}
          </button>
          <p className="text-[11px] text-muted-foreground">
            The token is not stored on the server — only account IDs.
          </p>
        </div>
      )}

      <div className="mt-8">
        <GraniteCredit compact />
      </div>

      <button
        type="button"
        onClick={handleSkip}
        className="mt-4 w-full text-center text-sm text-muted-foreground hover:text-foreground transition py-2"
      >
        Skip — go to dashboard
      </button>
    </div>
  );
}
