import { useState } from "react";
import { ActionSheet } from "@/components/ActionSheet";
import { QrCode } from "@/components/QrCode";
import { useT } from "@/lib/i18n";
import { useProfile } from "@/lib/profileStore";
import { hapticImpact, hapticNotification, isTelegramMiniApp } from "@/lib/telegram/telegramApp";
import { parseEthToWeiHex, sendEvmTransaction } from "@/lib/wallet/walletConnect";
import {
  Copy,
  Check,
  QrCode as QrIcon,
  Bell,
  ArrowUpRight,
  ScanLine,
  AlertCircle,
  Loader2,
} from "lucide-react";
import { toast } from "sonner";
import type { PortfolioAsset } from "@/hooks/usePortfolio";

type SheetType = null | "deposit" | "withdraw" | "scan" | "notif";

type Props = {
  sheet: SheetType;
  onClose: () => void;
  address: string;
  assets: PortfolioAsset[];
};

export function WalletSheets({ sheet, onClose, address, assets }: Props) {
  const t = useT();
  const p = useProfile();
  const [copied, setCopied] = useState(false);
  const [withdrawing, setWithdrawing] = useState(false);
  const [withdrawAsset, setWithdrawAsset] = useState(assets[0]?.symbol ?? "ETH");
  const [withdrawAmount, setWithdrawAmount] = useState("");
  const [withdrawTo, setWithdrawTo] = useState("");

  const copyAddress = async () => {
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

  const submitWithdraw = async () => {
    if (!withdrawTo.trim() || !withdrawAmount.trim()) {
      hapticNotification("error");
      toast.error(t.withdrawFillAll);
      return;
    }

    if (withdrawAsset !== "ETH") {
      hapticNotification("warning");
      toast.error(t.withdrawEvmOnly);
      return;
    }

    setWithdrawing(true);
    try {
      const valueWeiHex = parseEthToWeiHex(withdrawAmount);
      const txHash = await sendEvmTransaction({ to: withdrawTo.trim(), valueWeiHex });
      hapticNotification("success");
      toast.success(`${t.withdrawSent}: ${txHash.slice(0, 10)}…`);
      setWithdrawAmount("");
      setWithdrawTo("");
      onClose();
    } catch (e) {
      hapticNotification("error");
      const msg = e instanceof Error ? e.message : "Error";
      if (msg.includes("rejected") || msg.includes("User denied")) {
        toast.error("Transaction cancelled in wallet");
      } else {
        toast.error(msg);
      }
    } finally {
      setWithdrawing(false);
    }
  };

  const notifications = [
    { id: "n1", title: t.notifTx, body: "+250 USDT from @alexcrypto", time: "12:04", read: false },
    { id: "n2", title: t.notifPrice, body: "TON +4.31% in 24h", time: "09:15", read: false },
    {
      id: "n3",
      title: t.notifP2p,
      body: "P2P deal #4821 completed",
      time: "Yesterday",
      read: true,
    },
    {
      id: "n4",
      title: t.notifSecurity,
      body: "New sign-in to your account from this device",
      time: "Apr 22",
      read: true,
    },
  ];

  return (
    <>
      <ActionSheet
        open={sheet === "deposit"}
        onOpenChange={(v) => !v && onClose()}
        title={t.deposit}
      >
        <p className="text-sm text-muted-foreground">{t.depositHint}</p>
        {address !== "—" ? (
          <div className="flex flex-col items-center gap-4">
            <div className="rounded-2xl border border-[color:var(--neon-cyan)]/30 bg-black/30 p-3">
              <QrCode value={address} size={180} />
            </div>
            <button
              type="button"
              onClick={() => void copyAddress()}
              className="flex w-full items-center gap-2 rounded-xl bg-secondary px-4 py-3 font-mono text-xs"
            >
              {copied ? (
                <Check className="h-4 w-4 text-[color:var(--success)]" />
              ) : (
                <Copy className="h-4 w-4" />
              )}
              <span className="truncate">{address}</span>
            </button>
            <p className="text-center text-[11px] text-muted-foreground">{t.depositNetworks}</p>
          </div>
        ) : (
          <div className="flex items-center gap-2 rounded-xl border border-amber-500/40 bg-amber-500/10 p-3 text-xs">
            <AlertCircle className="h-4 w-4 shrink-0" />
            {t.depositNoWallet}
          </div>
        )}
      </ActionSheet>

      <ActionSheet
        open={sheet === "withdraw"}
        onOpenChange={(v) => !v && onClose()}
        title={t.withdraw}
      >
        <p className="text-sm text-muted-foreground">{t.withdrawHint}</p>
        <div className="space-y-3">
          <div>
            <label className="mb-1 block text-xs text-muted-foreground">{t.withdrawAsset}</label>
            <select
              value={withdrawAsset}
              onChange={(e) => setWithdrawAsset(e.target.value)}
              className="w-full rounded-xl border border-border bg-secondary px-3 py-2.5 text-sm"
            >
              {assets.length > 0 ? (
                assets.map((a) => (
                  <option key={a.symbol} value={a.symbol}>
                    {a.symbol} — {a.balance.toFixed(4)}
                  </option>
                ))
              ) : (
                <>
                  <option value="ETH">ETH</option>
                  <option value="TON">TON</option>
                  <option value="BTC">BTC</option>
                </>
              )}
            </select>
          </div>
          <div>
            <label className="mb-1 block text-xs text-muted-foreground">{t.withdrawTo}</label>
            <input
              value={withdrawTo}
              onChange={(e) => setWithdrawTo(e.target.value)}
              placeholder="0x…"
              className="w-full rounded-xl border border-border bg-secondary px-3 py-2.5 text-sm font-mono"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs text-muted-foreground">{t.withdrawAmount}</label>
            <input
              value={withdrawAmount}
              onChange={(e) => setWithdrawAmount(e.target.value)}
              placeholder="0.00"
              inputMode="decimal"
              className="w-full rounded-xl border border-border bg-secondary px-3 py-2.5 text-sm"
            />
          </div>
          <p className="text-[11px] text-muted-foreground">{t.withdrawSignNote}</p>
          <button
            type="button"
            disabled={withdrawing}
            onClick={() => void submitWithdraw()}
            className="flex w-full items-center justify-center gap-2 rounded-xl bg-primary py-3 text-sm font-semibold text-primary-foreground disabled:opacity-60"
          >
            {withdrawing ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <ArrowUpRight className="h-4 w-4" />
            )}
            {withdrawing ? t.withdrawPending : t.withdrawSubmit}
          </button>
        </div>
      </ActionSheet>

      <ActionSheet open={sheet === "scan"} onOpenChange={(v) => !v && onClose()} title={t.scan}>
        <div className="flex flex-col items-center gap-4 py-4">
          <div className="flex h-32 w-32 items-center justify-center rounded-2xl border-2 border-dashed border-[color:var(--neon-cyan)]/40 bg-black/30">
            <ScanLine className="h-12 w-12 text-[color:var(--neon-cyan)]/60" />
          </div>
          <p className="text-center text-sm text-muted-foreground">{t.scanHint}</p>
          {isTelegramMiniApp() && (
            <p className="text-center text-xs text-[color:var(--neon-cyan)]/80">
              {t.scanTelegramHint}
            </p>
          )}
          <button
            type="button"
            onClick={() => {
              hapticImpact("medium");
              toast.info(t.scanSoon);
            }}
            className="flex items-center gap-2 rounded-xl border border-[color:var(--neon-cyan)]/40 px-5 py-2.5 text-sm text-[color:var(--neon-cyan)]"
          >
            <QrIcon className="h-4 w-4" />
            {t.scanOpen}
          </button>
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "notif"}
        onOpenChange={(v) => !v && onClose()}
        title={t.notifications}
      >
        <div className="divide-y divide-border rounded-2xl bg-card">
          {notifications.map((n) => (
            <div key={n.id} className={`flex gap-3 p-3.5 ${!n.read ? "bg-primary/5" : ""}`}>
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-secondary">
                <Bell className="h-4 w-4" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="text-sm font-medium">{n.title}</p>
                  {!n.read && <span className="h-1.5 w-1.5 rounded-full bg-primary" />}
                </div>
                <p className="text-xs text-muted-foreground">{n.body}</p>
                <p className="mt-0.5 text-[10px] text-muted-foreground/70">{n.time}</p>
              </div>
            </div>
          ))}
        </div>
        {!p.push && <p className="text-center text-xs text-muted-foreground">{t.notifDisabled}</p>}
      </ActionSheet>
    </>
  );
}
