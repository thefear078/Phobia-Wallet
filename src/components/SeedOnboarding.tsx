import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import {
  Copy,
  Check,
  ShieldCheck,
  Download,
  ArrowLeft,
  Eye,
  EyeOff,
  KeyRound,
  Sparkles,
  Wallet,
} from "lucide-react";
import { UmbrellaLogo } from "./UmbrellaLogo";
import { useSession } from "@/lib/authStore";
import {
  generateSeedPhrase,
  saveSeedPhrase,
  importSeedPhrase,
  markSeedSetupSkipped,
  hasUnclaimedDeviceVault,
  unlockDeviceVault,
} from "@/lib/wallet/seedManager";

type Step = "intro" | "password" | "show" | "confirm" | "import" | "unlock";

function pickCheckIndexes(count: number): number[] {
  const set = new Set<number>();
  while (set.size < 3) set.add(Math.floor(Math.random() * count));
  return [...set].sort((a, b) => a - b);
}

export function SeedOnboarding({
  onDone,
  onLinkExternal,
}: {
  onDone: () => void;
  /** Chosen "link an existing wallet/card": skip seed setup and go to the link screen. */
  onLinkExternal?: () => void;
}) {
  const { userId } = useSession();
  const [step, setStep] = useState<Step>("intro");
  const [password, setPassword] = useState("");
  const [password2, setPassword2] = useState("");
  const [showPass, setShowPass] = useState(false);
  const [mnemonic, setMnemonic] = useState("");
  const [copied, setCopied] = useState(false);
  const [checkIdx, setCheckIdx] = useState<number[]>([]);
  const [checkWords, setCheckWords] = useState<string[]>(["", "", ""]);
  const [importPhrase, setImportPhrase] = useState("");
  const [busy, setBusy] = useState(false);
  const [deviceVault, setDeviceVault] = useState(false);

  useEffect(() => {
    void hasUnclaimedDeviceVault().then(setDeviceVault);
  }, []);

  const words = useMemo(() => (mnemonic ? mnemonic.split(" ") : []), [mnemonic]);

  const startCreate = () => setStep("password");

  const confirmPassword = () => {
    if (password.length < 8) {
      toast.error("Encryption password must be at least 8 characters");
      return;
    }
    if (password !== password2) {
      toast.error("Passwords do not match");
      return;
    }
    setMnemonic(generateSeedPhrase());
    setStep("show");
  };

  const toConfirm = () => {
    setCheckIdx(pickCheckIndexes(words.length || 24));
    setCheckWords(["", "", ""]);
    setStep("confirm");
  };

  const copyPhrase = async () => {
    try {
      await navigator.clipboard.writeText(mnemonic);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
      toast.success("Phrase copied — store it in a safe place");
    } catch {
      toast.error("Could not copy");
    }
  };

  const finishCreate = async () => {
    const ok = checkIdx.every((wi, i) => checkWords[i].trim().toLowerCase() === words[wi]);
    if (!ok) {
      toast.error("Words do not match — check the phrase you wrote down");
      return;
    }
    setBusy(true);
    try {
      await saveSeedPhrase(mnemonic, password, userId);
      toast.success("Wallet created; seed phrase encrypted on this device");
      onDone();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Could not save phrase");
    } finally {
      setBusy(false);
    }
  };

  const finishImport = async () => {
    if (password.length < 8) {
      toast.error("Encryption password must be at least 8 characters");
      return;
    }
    setBusy(true);
    try {
      await importSeedPhrase(importPhrase, password, userId);
      toast.success("Seed phrase imported; addresses linked");
      onDone();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Could not import phrase");
    } finally {
      setBusy(false);
    }
  };

  const finishUnlock = async () => {
    setBusy(true);
    try {
      await unlockDeviceVault(password, userId);
      toast.success("Seed phrase unlocked for this account");
      onDone();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Could not unlock phrase");
    } finally {
      setBusy(false);
    }
  };

  const skip = () => {
    markSeedSetupSkipped(userId);
    onDone();
  };

  return (
    <div className="app-scroll min-h-screen bg-background text-foreground flex justify-center">
      <div className="w-full max-w-md min-h-screen flex flex-col px-6 pt-8 pb-8">
        <div className="flex items-center justify-between">
          {step !== "intro" ? (
            <button
              onClick={() => setStep(step === "confirm" ? "show" : "intro")}
              className="h-9 w-9 rounded-full border border-border/60 flex items-center justify-center text-muted-foreground"
              aria-label="Back"
            >
              <ArrowLeft className="h-4 w-4" />
            </button>
          ) : (
            <UmbrellaLogo className="h-9 w-9" />
          )}
          <button onClick={skip} className="text-xs text-muted-foreground hover:text-foreground">
            Later
          </button>
        </div>

        {step === "intro" && (
          <div className="flex-1 flex flex-col justify-center text-center">
            <div className="mx-auto h-20 w-20 rounded-3xl border border-primary/20 flex items-center justify-center bg-primary/10">
              <ShieldCheck className="h-9 w-9 text-primary" strokeWidth={1.6} />
            </div>
            <h1 className="mt-6 text-2xl font-semibold tracking-tight">Set up your wallet</h1>
            <p className="mt-3 text-sm text-muted-foreground leading-relaxed">
              Create a new self-custody wallet, or connect one you already have. A new wallet is a{" "}
              <strong>24-word phrase</strong>, encrypted with your password and stored{" "}
              <strong>only on this device</strong> — Umbrella never sees it.
            </p>
            <div className="mt-8 space-y-3">
              <button
                onClick={startCreate}
                className="w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] flex items-center justify-center gap-2"
                style={{ height: 52 }}
              >
                <Sparkles className="h-4 w-4" /> Create a new wallet
              </button>
              <button
                onClick={() => setStep("import")}
                className="w-full rounded-2xl border border-border bg-secondary/40 text-sm font-medium hover:bg-secondary transition flex items-center justify-center gap-2"
                style={{ height: 48 }}
              >
                <Download className="h-4 w-4" /> Import a seed phrase
              </button>
              <button
                onClick={onLinkExternal ?? skip}
                className="w-full rounded-2xl border border-border bg-secondary/40 text-sm font-medium hover:bg-secondary transition flex items-center justify-center gap-2"
                style={{ height: 48 }}
              >
                <Wallet className="h-4 w-4" /> Link an existing wallet or card
              </button>
              {deviceVault && (
                <button
                  onClick={() => setStep("unlock")}
                  className="w-full rounded-2xl border border-primary/30 bg-primary/10 text-sm font-medium text-primary hover:bg-primary/15 transition flex items-center justify-center gap-2"
                  style={{ height: 48 }}
                >
                  <KeyRound className="h-4 w-4" /> Unlock the phrase stored on this device
                </button>
              )}
            </div>
          </div>
        )}

        {step === "unlock" && (
          <div className="flex-1 flex flex-col justify-center">
            <h1 className="text-xl font-semibold tracking-tight">Unlock device phrase</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              A seed phrase encrypted earlier was found on this device. Enter its encryption
              password to attach it to this account.
            </p>
            <div className="mt-6">
              <PasswordField
                value={password}
                onChange={setPassword}
                show={showPass}
                toggle={() => setShowPass((v) => !v)}
                placeholder="Encryption password"
              />
            </div>
            <button
              onClick={() => void finishUnlock()}
              disabled={busy || !password}
              className="mt-6 w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] disabled:opacity-60"
              style={{ height: 52 }}
            >
              {busy ? "Unlocking…" : "Unlock"}
            </button>
          </div>
        )}

        {step === "password" && (
          <div className="flex-1 flex flex-col justify-center">
            <h1 className="text-xl font-semibold tracking-tight">Encryption password</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              This password encrypts the phrase on the device. You will need it to view the phrase
              in settings.
            </p>
            <div className="mt-6 space-y-3">
              <PasswordField
                value={password}
                onChange={setPassword}
                show={showPass}
                toggle={() => setShowPass((v) => !v)}
                placeholder="Password (min. 8 characters)"
              />
              <PasswordField
                value={password2}
                onChange={setPassword2}
                show={showPass}
                toggle={() => setShowPass((v) => !v)}
                placeholder="Repeat password"
              />
            </div>
            <button
              onClick={confirmPassword}
              className="mt-6 w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px]"
              style={{ height: 52 }}
            >
              Next
            </button>
          </div>
        )}

        {step === "show" && (
          <div className="flex-1 flex flex-col justify-center">
            <h1 className="text-xl font-semibold tracking-tight">
              Write down your {words.length} words
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Write the phrase on paper in the correct order. Do not take a screenshot and never
              show it to anyone.
            </p>
            <div className="mt-5 grid grid-cols-2 gap-2">
              {words.map((w, i) => (
                <div
                  key={i}
                  className="flex items-center gap-2 rounded-xl bg-secondary/50 border border-border/60 px-3 py-2.5"
                >
                  <span className="text-[11px] text-muted-foreground w-5 text-right">{i + 1}.</span>
                  <span className="text-sm font-medium font-mono">{w}</span>
                </div>
              ))}
            </div>
            <button
              onClick={() => void copyPhrase()}
              className="mt-4 flex items-center justify-center gap-2 text-xs text-muted-foreground hover:text-foreground"
            >
              {copied ? (
                <Check className="h-3.5 w-3.5 text-[color:var(--success)]" />
              ) : (
                <Copy className="h-3.5 w-3.5" />
              )}
              Copy phrase
            </button>
            <button
              onClick={toConfirm}
              className="mt-6 w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px]"
              style={{ height: 52 }}
            >
              I have written it down
            </button>
          </div>
        )}

        {step === "confirm" && (
          <div className="flex-1 flex flex-col justify-center">
            <h1 className="text-xl font-semibold tracking-tight">Confirm phrase</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Enter words #{checkIdx.map((i) => i + 1).join(", #")} from your phrase.
            </p>
            <div className="mt-6 space-y-3">
              {checkIdx.map((wi, i) => (
                <label
                  key={wi}
                  className="flex items-center gap-3 h-12 rounded-xl bg-secondary/50 border border-border/60 px-3.5 focus-within:border-primary/50 transition"
                >
                  <span className="text-xs text-muted-foreground w-8">#{wi + 1}</span>
                  <input
                    value={checkWords[i]}
                    onChange={(e) =>
                      setCheckWords((prev) => prev.map((v, j) => (j === i ? e.target.value : v)))
                    }
                    autoCapitalize="none"
                    autoCorrect="off"
                    spellCheck={false}
                    className="flex-1 bg-transparent outline-none text-sm font-mono"
                  />
                </label>
              ))}
            </div>
            <button
              onClick={() => void finishCreate()}
              disabled={busy}
              className="mt-6 w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] disabled:opacity-60"
              style={{ height: 52 }}
            >
              {busy ? "Saving…" : "Confirm and create"}
            </button>
          </div>
        )}

        {step === "import" && (
          <div className="flex-1 flex flex-col justify-center">
            <h1 className="text-xl font-semibold tracking-tight">Import seed phrase</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Enter 12 or 24 words separated by spaces. The phrase will be encrypted and stored only
              on this device.
            </p>
            <textarea
              value={importPhrase}
              onChange={(e) => setImportPhrase(e.target.value)}
              rows={3}
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              placeholder="word1 word2 word3 …"
              className="mt-5 w-full rounded-xl bg-secondary/50 border border-border/60 px-3.5 py-3 text-sm font-mono outline-none focus:border-primary/50 transition resize-none"
            />
            <div className="mt-3">
              <PasswordField
                value={password}
                onChange={setPassword}
                show={showPass}
                toggle={() => setShowPass((v) => !v)}
                placeholder="Encryption password (min. 8 characters)"
              />
            </div>
            <button
              onClick={() => void finishImport()}
              disabled={busy}
              className="mt-6 w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] disabled:opacity-60"
              style={{ height: 52 }}
            >
              {busy ? "Importing…" : "Import"}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function PasswordField({
  value,
  onChange,
  show,
  toggle,
  placeholder,
}: {
  value: string;
  onChange: (s: string) => void;
  show: boolean;
  toggle: () => void;
  placeholder: string;
}) {
  return (
    <label className="flex items-center gap-3 h-12 rounded-xl bg-secondary/50 border border-border/60 px-3.5 focus-within:border-primary/50 transition">
      <input
        type={show ? "text" : "password"}
        value={value}
        placeholder={placeholder}
        autoComplete="new-password"
        onChange={(e) => onChange(e.target.value)}
        className="flex-1 bg-transparent outline-none text-sm placeholder:text-muted-foreground/70"
      />
      <button type="button" onClick={toggle} className="text-muted-foreground">
        {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
      </button>
    </label>
  );
}
