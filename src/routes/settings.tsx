import { createFileRoute } from "@tanstack/react-router";
import { AppShell } from "@/components/AppShell";
import {
  ChevronRight,
  Shield,
  Bell,
  Globe,
  HelpCircle,
  LogOut,
  KeyRound,
  CreditCard,
  Wallet,
  FileKey2,
  Copy,
  Check,
  Trash2,
  Mail,
  EyeOff,
  Palette,
} from "lucide-react";
import { LinkAccountsPrompt } from "@/components/LinkAccountsPrompt";
import { useState, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { ActionSheet } from "@/components/ActionSheet";
import { Switch } from "@/components/ui/switch";
import { toast } from "sonner";
import { useProfile, setProfile, type Lang, type KycStatus } from "@/lib/profileStore";
import { useT, langLabel, SUPPORTED_LANGS } from "@/lib/i18n";
import { useSession, signOut, updateSessionMeta } from "@/lib/authStore";
import { isOnionHost, isPrivacyMode, setPrivacyMode } from "@/lib/privacyMode";
import { ThemeToggle } from "@/components/ThemeToggle";
import { api } from "@/lib/api/client";
import { UmbrellaLogo } from "@/components/UmbrellaLogo";
import { GraniteCredit } from "@/components/GraniteCredit";
import {
  connectWalletConnect,
  connectInjectedWallet,
  disconnectWalletConnect,
  hasInjectedWallet,
  signLinkProof,
} from "@/lib/wallet/walletConnect";
import {
  hasSeedVault,
  revealSeedPhrase,
  importSeedPhrase,
  removeSeedFromDevice,
  clearSeedSetupSkipped,
} from "@/lib/wallet/seedManager";

export const Route = createFileRoute("/settings")({
  head: () => ({
    meta: [
      { title: "Profile — Umbrella Wallet" },
      {
        name: "description",
        content: "Umbrella Wallet account and security settings.",
      },
    ],
  }),
  component: SettingsPage,
});

function SettingsPage() {
  const [sheet, setSheet] = useState<
    null | "2fa" | "pay" | "kyc" | "notif" | "lang" | "wallets" | "seed" | "email"
  >(null);
  const [emailInput, setEmailInput] = useState("");
  const [emailBusy, setEmailBusy] = useState(false);
  const [monoToken, setMonoToken] = useState("");
  const [wcLoading, setWcLoading] = useState(false);
  const [vaultExists, setVaultExists] = useState(false);
  const [seedPass, setSeedPass] = useState("");
  const [seedPhrase, setSeedPhrase] = useState<string | null>(null);
  const [seedCopied, setSeedCopied] = useState(false);
  const [seedImport, setSeedImport] = useState("");
  const [seedBusy, setSeedBusy] = useState(false);
  const [seedDeleteArmed, setSeedDeleteArmed] = useState(false);
  const [privacyOn, setPrivacyOn] = useState(false);

  useEffect(() => {
    setPrivacyOn(isPrivacyMode());
  }, []);
  const p = useProfile();
  const t = useT();
  const s = useSession();

  const { data: bankAccounts = [], refetch: refetchBanks } = useQuery({
    queryKey: ["bank-accounts"],
    queryFn: () => api.listBankAccounts(),
    retry: false,
  });

  const { data: wallets = [], refetch: refetchWallets } = useQuery({
    queryKey: ["wallets"],
    queryFn: () => api.listWallets(),
    retry: false,
  });

  useEffect(() => {
    if (!s.userId) return;
    void hasSeedVault(s.userId).then(setVaultExists);
  }, [sheet, s.userId]);

  useEffect(() => {
    void api
      .me()
      .then((me) => {
        const kycMap: Record<string, KycStatus> = {
          approved: "verified",
          pending: "pending",
          rejected: "none",
          none: "none",
        };
        setProfile({
          lang: me.lang as Lang,
          tfa: me.tfaEnabled ?? false,
          push: me.pushEnabled ?? true,
          email: me.emailAlerts ?? false,
          priceAlerts: me.priceAlerts ?? true,
          kyc: kycMap[me.kyc ?? "none"] ?? "none",
        });
      })
      .catch(() => {});
  }, []);

  const syncProfile = async (patch: Parameters<typeof setProfile>[0]) => {
    setProfile(patch);
    try {
      await api.updateMe({
        lang: patch.lang,
        tfaEnabled: patch.tfa,
        pushEnabled: patch.push,
        emailAlerts: patch.email,
        priceAlerts: patch.priceAlerts,
      });
    } catch {
      /* ignore */
    }
  };

  const submitEmail = async () => {
    setEmailBusy(true);
    try {
      const res = await api.requestEmailVerification(emailInput.trim());
      updateSessionMeta({ email: emailInput.trim().toLowerCase(), emailVerified: false });
      if (res.sent) {
        toast.success("Email sent — check your inbox");
      } else if (res.demo) {
        updateSessionMeta({ emailVerified: true });
        toast.success("Email saved (demo mode)");
      } else {
        toast.info("Email saved. Outbound email is not configured — contact support");
      }
      setSheet(null);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Could not send email");
    } finally {
      setEmailBusy(false);
    }
  };

  const linkWalletConnect = async () => {
    setWcLoading(true);
    try {
      const { address, chain, label } = await connectWalletConnect();
      const challenge = await api.walletChallenge();
      const signature = await signLinkProof(challenge.message, address);
      await api.linkWallet({ chain, address, label, message: challenge.message, signature });
      toast.success("Wallet linked via WalletConnect");
      void refetchWallets();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "WalletConnect error");
    } finally {
      setWcLoading(false);
    }
  };

  const linkInjected = async () => {
    setWcLoading(true);
    try {
      const { address, chain, label } = await connectInjectedWallet();
      const challenge = await api.walletChallenge();
      const signature = await signLinkProof(challenge.message, address);
      await api.linkWallet({ chain, address, label, message: challenge.message, signature });
      toast.success("Browser wallet linked");
      void refetchWallets();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error");
    } finally {
      setWcLoading(false);
    }
  };

  const unlinkWallet = async (id: string) => {
    try {
      await api.unlinkWallet(id);
      toast.success("Wallet disconnected");
      void refetchWallets();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error");
    }
  };

  const linkMonobank = async () => {
    if (!monoToken.trim()) {
      toast.error("Paste your Monobank personal token");
      return;
    }
    try {
      const res = await api.linkMonobank(monoToken.trim());
      toast.success(`Linked ${res.linked.length} account(s)`);
      setMonoToken("");
      void refetchBanks();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Monobank error");
    }
  };

  const closeSeedSheet = () => {
    setSheet(null);
    setSeedPass("");
    setSeedPhrase(null);
    setSeedImport("");
    setSeedCopied(false);
    setSeedDeleteArmed(false);
  };

  const revealSeed = async () => {
    setSeedBusy(true);
    try {
      const phrase = await revealSeedPhrase(seedPass, s.userId);
      setSeedPhrase(phrase);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Error");
    } finally {
      setSeedBusy(false);
    }
  };

  const importSeed = async () => {
    if (seedPass.length < 8) {
      toast.error("Encryption password must be at least 8 characters");
      return;
    }
    setSeedBusy(true);
    try {
      await importSeedPhrase(seedImport, seedPass, s.userId);
      toast.success("Seed phrase imported; addresses linked");
      setVaultExists(true);
      closeSeedSheet();
      void refetchWallets();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Import error");
    } finally {
      setSeedBusy(false);
    }
  };

  const deleteSeed = async () => {
    if (!seedDeleteArmed) {
      setSeedDeleteArmed(true);
      return;
    }
    await removeSeedFromDevice(s.userId);
    clearSeedSetupSkipped(s.userId);
    setVaultExists(false);
    setSeedPhrase(null);
    setSeedDeleteArmed(false);
    toast.success("Seed phrase removed from device");
  };

  const copySeed = async () => {
    if (!seedPhrase) return;
    try {
      await navigator.clipboard.writeText(seedPhrase);
      setSeedCopied(true);
      setTimeout(() => setSeedCopied(false), 1500);
      toast.success("Phrase copied");
    } catch {
      toast.error("Could not copy");
    }
  };

  const item = (
    icon: React.ElementType,
    label: string,
    value: string | undefined,
    onClick: () => void,
  ) => {
    const Icon = icon;
    return (
      <button
        key={label}
        onClick={onClick}
        className="w-full flex items-center gap-3 p-3.5 text-left hover:bg-secondary/40 transition"
      >
        <div className="h-9 w-9 rounded-xl bg-secondary flex items-center justify-center">
          <Icon className="h-4 w-4" />
        </div>
        <span className="flex-1 text-sm font-medium">{label}</span>
        {value && <span className="text-xs text-muted-foreground">{value}</span>}
        <ChevronRight className="h-4 w-4 text-muted-foreground" />
      </button>
    );
  };

  return (
    <AppShell>
      <header className="px-5 pt-6 pb-4">
        <h1 className="text-2xl font-bold">{t.profile}</h1>
      </header>

      <section className="px-5">
        <div
          className="rounded-2xl p-4 flex items-center gap-4 border border-border/60"
          style={{ background: "var(--gradient-card)", boxShadow: "var(--shadow-card)" }}
        >
          <div className="h-14 w-14 rounded-full bg-secondary flex items-center justify-center border border-border/60">
            <UmbrellaLogo className="h-9 w-9" />
          </div>
          <div className="flex-1">
            <div className="font-semibold">{s.name || "Umbrella User"}</div>
            <div className="text-xs text-muted-foreground truncate max-w-[180px]">
              {s.username
                ? `@${s.username}`
                : s.email.endsWith("@umbra.local")
                  ? "—"
                  : s.email || "—"}
            </div>
          </div>
          {p.kyc === "verified" ? (
            <span className="text-[11px] px-2 py-1 rounded-full bg-primary/15 text-primary font-semibold border border-primary/30">
              {t.verifiedBadge}
            </span>
          ) : p.kyc === "pending" ? (
            <span className="text-[11px] px-2 py-1 rounded-full bg-accent/20 text-accent font-semibold border border-accent/40">
              {t.pending}
            </span>
          ) : (
            <span className="text-[11px] px-2 py-1 rounded-full bg-secondary text-muted-foreground font-semibold border border-border">
              {t.unverifiedBadge}
            </span>
          )}
        </div>
      </section>

      <section className="px-5 mt-6">
        <h2 className="text-xs uppercase tracking-wide text-muted-foreground mb-2 px-1">
          {t.account}
        </h2>
        <div className="rounded-2xl bg-card divide-y divide-border">
          {item(Wallet, "Wallets", String(wallets.length), () => setSheet("wallets"))}
          {item(FileKey2, "Seed phrase", vaultExists ? "Saved" : "None", () => setSheet("seed"))}
          {item(
            Mail,
            "Email",
            s.email && !s.email.endsWith("@umbra.local") && !s.email.endsWith(".oauth")
              ? s.emailVerified
                ? "Verified"
                : "Not verified"
              : "Not set",
            () => {
              setEmailInput(
                s.email && !s.email.endsWith("@umbra.local") && !s.email.endsWith(".oauth")
                  ? s.email
                  : "",
              );
              setSheet("email");
            },
          )}
          {item(KeyRound, t.security2fa, p.tfa ? t.on : t.off, () => setSheet("2fa"))}
          {item(CreditCard, t.payMethods, String(bankAccounts.length), () => setSheet("pay"))}
          {item(
            Shield,
            t.kycLabel,
            p.kyc === "verified" ? t.verified : p.kyc === "pending" ? t.pending : t.notVerified,
            () => setSheet("kyc"),
          )}
        </div>
      </section>

      <section className="px-5 mt-6">
        <h2 className="text-xs uppercase tracking-wide text-muted-foreground mb-2 px-1">{t.app}</h2>
        <div className="rounded-2xl bg-card p-3.5 mb-2">
          <div className="flex items-center gap-3 mb-2.5">
            <div className="h-9 w-9 rounded-xl bg-secondary flex items-center justify-center">
              <Palette className="h-4 w-4" />
            </div>
            <span className="text-sm font-medium">Appearance</span>
          </div>
          <ThemeToggle />
        </div>
        <div className="rounded-2xl bg-card divide-y divide-border">
          {item(Bell, t.notifications, p.push ? t.on : t.off, () => setSheet("notif"))}
          {item(Globe, t.language, langLabel(p.lang), () => setSheet("lang"))}
          {item(
            EyeOff,
            "Privacy (Tor) mode",
            isOnionHost() ? "Active (.onion)" : privacyOn ? "On" : "Off",
            () => {
              if (isOnionHost()) {
                toast.info("Always active on .onion — no third-party connections");
                return;
              }
              const next = !privacyOn;
              setPrivacyMode(next);
              setPrivacyOn(next);
              toast.success(
                next
                  ? "Privacy mode on — Telegram and all third-party connections disabled"
                  : "Privacy mode off",
              );
            },
          )}
          {item(HelpCircle, t.help, undefined, () => {
            window.location.href = "/help";
          })}
        </div>
      </section>

      <section className="px-5 mt-6">
        <GraniteCredit />
      </section>

      <section className="px-5 mt-6 pb-8">
        <button
          onClick={() => {
            void signOut().then(() => {
              void disconnectWalletConnect();
              toast.success("You signed out");
            });
          }}
          className="w-full flex items-center justify-center gap-2 p-3.5 rounded-2xl bg-card text-destructive font-semibold hover:bg-destructive/10 transition"
        >
          <LogOut className="h-4 w-4" /> {t.logout}
        </button>
      </section>

      <ActionSheet
        open={sheet === "wallets"}
        onOpenChange={(v) => !v && setSheet(null)}
        title="Wallets"
      >
        <div className="space-y-3">
          <button
            disabled={wcLoading}
            onClick={() => void linkWalletConnect()}
            className="w-full py-3 rounded-xl bg-primary text-primary-foreground text-sm font-semibold"
          >
            WalletConnect (QR)
          </button>
          {hasInjectedWallet() && (
            <button
              disabled={wcLoading}
              onClick={() => void linkInjected()}
              className="w-full py-3 rounded-xl bg-secondary text-sm font-semibold"
            >
              Browser wallet
            </button>
          )}
          {wallets.map((w) => (
            <div
              key={w.id}
              className="flex items-center gap-3 rounded-2xl bg-card p-3.5 border border-border/60"
            >
              <div className="flex-1 min-w-0">
                <div className="text-sm font-medium">{w.label ?? w.chain}</div>
                <div className="text-xs text-muted-foreground font-mono truncate">{w.address}</div>
              </div>
              <button
                onClick={() => void unlinkWallet(w.id)}
                className="text-xs text-destructive shrink-0"
              >
                Disconnect
              </button>
            </div>
          ))}
          {wallets.length === 0 && (
            <p className="text-xs text-muted-foreground text-center py-2">No linked wallets</p>
          )}
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "email"}
        onOpenChange={(v) => !v && setSheet(null)}
        title="Email"
      >
        <div className="space-y-4">
          <p className="text-xs text-muted-foreground leading-relaxed">
            Email for notifications and account recovery. We will send a confirmation link (valid
            for 24 hours).
          </p>
          <input
            type="email"
            value={emailInput}
            onChange={(e) => setEmailInput(e.target.value)}
            placeholder="you@example.com"
            autoComplete="email"
            className="w-full bg-secondary rounded-xl px-3 py-2.5 text-sm outline-none"
          />
          {s.email && !s.email.endsWith("@umbra.local") && !s.email.endsWith(".oauth") && (
            <p className="text-xs text-muted-foreground">
              Current: <span className="text-foreground">{s.email}</span>{" "}
              {s.emailVerified ? "· verified ✓" : "· not verified"}
            </p>
          )}
          <button
            onClick={() => void submitEmail()}
            disabled={emailBusy || !/^\S+@\S+\.\S+$/.test(emailInput.trim())}
            className="w-full py-3 rounded-xl bg-primary text-primary-foreground text-sm font-semibold disabled:opacity-60"
          >
            {emailBusy ? "Sending…" : "Send confirmation"}
          </button>
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "seed"}
        onOpenChange={(v) => !v && closeSeedSheet()}
        title="Seed phrase"
      >
        {vaultExists ? (
          <div className="space-y-4">
            {seedPhrase ? (
              <>
                <p className="text-xs text-amber-400 leading-relaxed">
                  Never share these words. Anyone with the phrase has full access to the funds.
                </p>
                <div className="grid grid-cols-2 gap-2">
                  {seedPhrase.split(" ").map((w, i) => (
                    <div
                      key={i}
                      className="flex items-center gap-2 rounded-xl bg-secondary/50 border border-border/60 px-3 py-2"
                    >
                      <span className="text-[11px] text-muted-foreground w-5 text-right">
                        {i + 1}.
                      </span>
                      <span className="text-sm font-mono">{w}</span>
                    </div>
                  ))}
                </div>
                <button
                  onClick={() => void copySeed()}
                  className="w-full flex items-center justify-center gap-2 py-2.5 rounded-xl bg-secondary text-sm font-medium"
                >
                  {seedCopied ? (
                    <Check className="h-4 w-4 text-[color:var(--success)]" />
                  ) : (
                    <Copy className="h-4 w-4" />
                  )}
                  Copy
                </button>
              </>
            ) : (
              <>
                <p className="text-xs text-muted-foreground leading-relaxed">
                  The phrase is encrypted on this device. Enter the encryption password to view it.
                </p>
                <input
                  type="password"
                  value={seedPass}
                  onChange={(e) => setSeedPass(e.target.value)}
                  placeholder="Encryption password"
                  autoComplete="current-password"
                  className="w-full bg-secondary rounded-xl px-3 py-2.5 text-sm outline-none"
                />
                <button
                  onClick={() => void revealSeed()}
                  disabled={seedBusy || !seedPass}
                  className="w-full py-3 rounded-xl bg-primary text-primary-foreground text-sm font-semibold disabled:opacity-60"
                >
                  {seedBusy ? "Decrypting…" : "Show phrase"}
                </button>
              </>
            )}
            <button
              onClick={() => void deleteSeed()}
              className={`w-full flex items-center justify-center gap-2 py-2.5 rounded-xl text-sm font-medium transition ${
                seedDeleteArmed
                  ? "bg-destructive text-destructive-foreground"
                  : "text-destructive hover:bg-destructive/10"
              }`}
            >
              <Trash2 className="h-4 w-4" />
              {seedDeleteArmed
                ? "Tap again to delete — no backup, no recovery"
                : "Delete from this device"}
            </button>
          </div>
        ) : (
          <div className="space-y-4">
            <p className="text-xs text-muted-foreground leading-relaxed">
              No seed phrase is stored on this device. Import an existing one — it will be encrypted
              with a password and saved locally only.
            </p>
            <textarea
              value={seedImport}
              onChange={(e) => setSeedImport(e.target.value)}
              rows={3}
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              placeholder="12 or 24 words separated by spaces"
              className="w-full bg-secondary rounded-xl px-3 py-2.5 text-sm font-mono outline-none resize-none"
            />
            <input
              type="password"
              value={seedPass}
              onChange={(e) => setSeedPass(e.target.value)}
              placeholder="Encryption password (min. 8 characters)"
              autoComplete="new-password"
              className="w-full bg-secondary rounded-xl px-3 py-2.5 text-sm outline-none"
            />
            <button
              onClick={() => void importSeed()}
              disabled={seedBusy}
              className="w-full py-3 rounded-xl bg-primary text-primary-foreground text-sm font-semibold disabled:opacity-60"
            >
              {seedBusy ? "Importing…" : "Import phrase"}
            </button>
            <button
              onClick={() => {
                clearSeedSetupSkipped(s.userId);
                window.location.href = "/";
              }}
              className="w-full py-2.5 rounded-xl bg-secondary text-sm font-medium"
            >
              Create a new phrase
            </button>
          </div>
        )}
      </ActionSheet>

      <ActionSheet
        open={sheet === "2fa"}
        onOpenChange={(v) => !v && setSheet(null)}
        title={t.security2fa}
      >
        <div className="rounded-2xl bg-card divide-y divide-border">
          <div className="flex items-center justify-between p-3.5">
            <span className="text-sm">2FA</span>
            <Switch
              checked={p.tfa}
              onCheckedChange={(v) => {
                void syncProfile({ tfa: v });
                toast.success(`2FA: ${v ? t.on : t.off}`);
              }}
            />
          </div>
          <div className="flex items-center justify-between p-3.5">
            <span className="text-sm">Biometric</span>
            <Switch checked={p.biometric} onCheckedChange={(v) => setProfile({ biometric: v })} />
          </div>
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "pay"}
        onOpenChange={(v) => !v && setSheet(null)}
        title={t.payMethods}
      >
        <LinkAccountsPrompt compact onLinked={() => void refetchBanks()} />
        <div className="mt-4 space-y-2">
          <label className="text-xs text-muted-foreground">Monobank personal token</label>
          <input
            value={monoToken}
            onChange={(e) => setMonoToken(e.target.value)}
            type="password"
            placeholder="X-Token…"
            className="w-full bg-secondary rounded-xl px-3 py-2.5 text-sm outline-none"
          />
          <button
            onClick={() => void linkMonobank()}
            className="w-full py-2.5 rounded-xl bg-primary text-primary-foreground text-sm font-semibold"
          >
            Connect Monobank
          </button>
        </div>
        {bankAccounts.map((c) => (
          <div key={c.id} className="flex items-center gap-3 rounded-2xl bg-card p-3.5 mt-3">
            <div className="h-9 w-9 rounded-xl bg-secondary flex items-center justify-center">
              <CreditCard className="h-4 w-4" />
            </div>
            <div className="flex-1">
              <div className="text-sm font-medium">{c.bankName ?? c.provider}</div>
              <div className="text-xs text-muted-foreground font-mono">
                {c.maskedNumber ?? c.provider}
              </div>
              {c.status !== "active" && (
                <div className="text-xs text-amber-400">Access revoked</div>
              )}
            </div>
            <button
              onClick={() =>
                void api.revokeBankAccount(c.id).then(() => {
                  void refetchBanks();
                  toast.success("Card disconnected");
                })
              }
              className="text-xs text-destructive"
            >
              Delete
            </button>
          </div>
        ))}
      </ActionSheet>

      <ActionSheet
        open={sheet === "kyc"}
        onOpenChange={(v) => !v && setSheet(null)}
        title={t.kycLabel}
      >
        <div className="rounded-2xl bg-card p-4 space-y-3">
          <p className="text-sm text-muted-foreground">
            Status:{" "}
            {p.kyc === "verified" ? t.verified : p.kyc === "pending" ? t.pending : t.notVerified}
          </p>
          <button
            onClick={() =>
              void api.kycStart().then((r) => {
                setProfile({ kyc: "pending" });
                window.open(r.verificationUrl, "_blank");
                toast.success("KYC started");
              })
            }
            className="w-full py-3 rounded-xl bg-primary text-primary-foreground font-semibold text-sm"
          >
            Start verification
          </button>
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "notif"}
        onOpenChange={(v) => !v && setSheet(null)}
        title={t.notifications}
      >
        <div className="rounded-2xl bg-card divide-y divide-border">
          <div className="flex items-center justify-between p-3.5">
            <span className="text-sm">Push</span>
            <Switch checked={p.push} onCheckedChange={(v) => void syncProfile({ push: v })} />
          </div>
          <div className="flex items-center justify-between p-3.5">
            <span className="text-sm">Email</span>
            <Switch checked={p.email} onCheckedChange={(v) => void syncProfile({ email: v })} />
          </div>
          <div className="flex items-center justify-between p-3.5">
            <span className="text-sm">Price alerts</span>
            <Switch
              checked={p.priceAlerts}
              onCheckedChange={(v) => void syncProfile({ priceAlerts: v })}
            />
          </div>
        </div>
      </ActionSheet>

      <ActionSheet
        open={sheet === "lang"}
        onOpenChange={(v) => !v && setSheet(null)}
        title={t.language}
      >
        <div className="rounded-2xl bg-card divide-y divide-border">
          {SUPPORTED_LANGS.map((l) => (
            <button
              key={l}
              onClick={() => {
                void syncProfile({ lang: l });
                setSheet(null);
                toast.success(langLabel(l));
              }}
              className="w-full flex items-center justify-between p-3.5 text-sm hover:bg-secondary/40"
            >
              <span>{langLabel(l)}</span>
              {p.lang === l && <span className="text-primary text-xs">✓</span>}
            </button>
          ))}
        </div>
      </ActionSheet>
    </AppShell>
  );
}
