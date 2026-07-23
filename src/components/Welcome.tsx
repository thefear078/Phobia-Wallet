import { useState } from "react";
import { UmbrellaLogo } from "./UmbrellaLogo";
import { loginAccount, registerAccount, telegramLogin } from "@/lib/authStore";
import { toast } from "sonner";
import {
  EyeOff,
  KeyRound,
  Users,
  ArrowRight,
  ArrowLeft,
  Fingerprint,
  AtSign,
  Send,
  Shield,
  Lock,
  Zap,
} from "lucide-react";
import { formatApiError } from "@/lib/api/errors";
import { isPrivacyMode } from "@/lib/privacyMode";
import { signInWithTelegram } from "@/lib/telegramLogin";
import { GraniteCredit } from "@/components/GraniteCredit";

type Step = 0 | 1 | 2 | 3 | 4;

const slides = [
  {
    icon: EyeOff,
    title: "Anonymous by default",
    text: "No email, phone, or documents. A nickname and password are all you need to get started.",
  },
  {
    icon: KeyRound,
    title: "Keys stay with you",
    text: "Your seed phrase is encrypted on your device. Umbrella never sees your keys and never holds your funds.",
  },
  {
    icon: Users,
    title: "P2P without middlemen",
    text: "Trade directly with people at live rates. Telegram notifications are optional.",
  },
];

export function Welcome() {
  const [step, setStep] = useState<Step>(0);
  const [mode, setMode] = useState<"signup" | "login">("signup");
  const [username, setUsername] = useState("");
  const [pass, setPass] = useState("");

  const next = () => setStep((s) => Math.min(4, s + 1) as Step);
  const back = () => setStep((s) => Math.max(0, s - 1) as Step);

  const submit = async () => {
    if (!username.trim() || !pass.trim()) {
      toast.error("Enter a nickname and password");
      return;
    }
    if (pass.length < 8) {
      toast.error("Password must be at least 8 characters");
      return;
    }
    try {
      if (mode === "signup") {
        await registerAccount(username, pass);
        toast.success("Account created");
      } else {
        await loginAccount(username, pass);
        toast.success("Welcome back");
      }
    } catch (e) {
      toast.error(formatApiError(e));
    }
  };

  return (
    <div className="app-scroll min-h-screen bg-background text-foreground flex justify-center">
      <div className="w-full max-w-md min-h-screen relative flex flex-col overflow-hidden">
        <div
          aria-hidden
          className="pointer-events-none absolute inset-x-0 -top-24 h-[420px]"
          style={{
            background:
              "radial-gradient(ellipse at 50% 0%, color-mix(in oklab, var(--primary) 32%, transparent) 0%, transparent 70%)",
          }}
        />

        <div className="relative pt-8 px-6 flex items-center justify-between">
          <button
            onClick={back}
            className={`h-9 w-9 rounded-full border border-border/60 flex items-center justify-center text-muted-foreground transition-opacity ${
              step === 0 ? "opacity-0 pointer-events-none" : "opacity-100"
            }`}
            aria-label="Back"
          >
            <ArrowLeft className="h-4 w-4" />
          </button>
          <div className="flex items-center gap-1.5">
            {[0, 1, 2, 3].map((i) => (
              <span
                key={i}
                className={`h-1.5 rounded-full transition-all ${
                  i === Math.min(step, 3) ? "w-6 bg-primary" : "w-1.5 bg-border"
                }`}
              />
            ))}
          </div>
          <button
            onClick={() => setStep(4)}
            className={`text-xs text-muted-foreground hover:text-foreground transition ${
              step >= 3 ? "opacity-0 pointer-events-none" : ""
            }`}
          >
            Skip
          </button>
        </div>

        <div className="relative flex-1 flex flex-col px-6 pt-10 pb-8">
          {step === 0 && <Cover onStart={next} />}
          {step >= 1 && step <= 3 && <SlideView step={step - 1} />}
          {step === 4 && (
            <AuthForm
              mode={mode}
              setMode={setMode}
              username={username}
              setUsername={setUsername}
              pass={pass}
              setPass={setPass}
            />
          )}

          <div className="mt-auto pt-8">
            {step < 4 ? (
              <button
                onClick={next}
                className="w-full h-13 rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] flex items-center justify-center gap-2 shadow-[var(--shadow-glow)] active:scale-[0.98] transition"
                style={{ height: 52 }}
              >
                {step === 0 ? "Get started" : step === 3 ? "Create wallet" : "Next"}
                <ArrowRight className="h-4 w-4" />
              </button>
            ) : (
              <button
                onClick={submit}
                className="w-full rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] flex items-center justify-center gap-2 shadow-[var(--shadow-glow)] active:scale-[0.98] transition"
                style={{ height: 52 }}
              >
                {mode === "signup" ? "Create account" : "Log in"}
              </button>
            )}

            {step === 0 && (
              <button
                onClick={() => setStep(4)}
                className="w-full mt-3 text-sm text-muted-foreground hover:text-foreground transition"
              >
                I already have an account
              </button>
            )}
            {step === 4 && (
              <>
                <div className="mt-6">
                  <GraniteCredit />
                </div>
                <p className="mt-4 text-center text-[11px] text-muted-foreground leading-relaxed px-4">
                  By continuing, you agree to the{" "}
                  <a href="/legal/terms" className="text-foreground/80 underline">
                    Terms
                  </a>
                  ,{" "}
                  <a href="/legal/agreement" className="text-foreground/80 underline">
                    User agreement
                  </a>{" "}
                  and{" "}
                  <a href="/legal/privacy" className="text-foreground/80 underline">
                    Privacy policy
                  </a>{" "}
                  of Umbrella Wallet.
                </p>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

const FEATURES = [
  { Icon: Shield, label: "Anonymous" },
  { Icon: Lock, label: "Your keys" },
  { Icon: Zap, label: "P2P" },
] as const;

function Cover({ onStart: _ }: { onStart: () => void }) {
  return (
    <div className="flex-1 flex flex-col items-center justify-center text-center">
      <div className="relative">
        {/* Monochrome glow — the token resolves to translucent black on light
            and translucent white on dark, so it reads in both themes. */}
        <div
          aria-hidden
          className="absolute inset-0 -m-12 rounded-full blur-3xl"
          style={{ background: "radial-gradient(circle, var(--neon-cyan-dim), transparent 70%)" }}
        />
        {/* White logo art: invert it on light so the mark is always high-contrast. */}
        <UmbrellaLogo className="relative h-40 w-40 invert dark:invert-0" />
      </div>

      <h1 className="mt-8 text-[38px] leading-none font-semibold uppercase tracking-[0.16em]">
        Umbrella
      </h1>
      <p className="mt-3 text-[13px] text-muted-foreground">Anonymous Wallet · P2P Exchange</p>

      <div className="mt-9 grid grid-cols-3 gap-2.5 w-full max-w-[300px]">
        {FEATURES.map(({ Icon, label }) => (
          <div
            key={label}
            className="flex flex-col items-center gap-2 rounded-2xl border border-border bg-card py-3.5"
          >
            <Icon className="h-4 w-4" strokeWidth={1.8} />
            <span className="text-[9.5px] uppercase tracking-[0.12em] text-muted-foreground">
              {label}
            </span>
          </div>
        ))}
      </div>

      <div className="mt-8 w-full max-w-[280px]">
        <GraniteCredit compact />
      </div>
    </div>
  );
}

function SlideView({ step }: { step: number }) {
  const s = slides[step];
  const Icon = s.icon;
  return (
    <div className="flex-1 flex flex-col items-center justify-center text-center">
      <div className="h-20 w-20 rounded-3xl flex items-center justify-center bg-foreground text-background">
        <Icon className="h-9 w-9" strokeWidth={1.5} />
      </div>
      <h2 className="mt-8 text-2xl font-semibold tracking-tight">{s.title}</h2>
      <p className="mt-3 text-[15px] text-muted-foreground max-w-[300px] leading-relaxed">
        {s.text}
      </p>
    </div>
  );
}

function AuthForm(props: {
  mode: "signup" | "login";
  setMode: (m: "signup" | "login") => void;
  username: string;
  setUsername: (s: string) => void;
  pass: string;
  setPass: (s: string) => void;
}) {
  const { mode, setMode, username, setUsername, pass, setPass } = props;
  return (
    <div className="flex-1 flex flex-col">
      <div className="flex items-center gap-3">
        <UmbrellaLogo className="h-11 w-11" />
        <div>
          <h2 className="text-xl font-semibold tracking-tight">
            {mode === "signup" ? "Create your wallet" : "Welcome back"}
          </h2>
          <p className="text-xs text-muted-foreground">
            {mode === "signup"
              ? "Choose a nickname and password"
              : "Sign in with your nickname and password"}
          </p>
        </div>
      </div>

      <div className="mt-6 grid grid-cols-2 rounded-xl bg-secondary/60 p-1 text-sm">
        {(["signup", "login"] as const).map((m) => (
          <button
            key={m}
            onClick={() => setMode(m)}
            className={`h-9 rounded-lg font-medium transition ${
              mode === m ? "bg-background text-foreground shadow-sm" : "text-muted-foreground"
            }`}
          >
            {m === "signup" ? "Sign up" : "Log in"}
          </button>
        ))}
      </div>

      <div className="mt-5 space-y-3">
        <Field
          icon={<AtSign className="h-4 w-4" />}
          placeholder="Nickname (3–32 characters)"
          value={username}
          onChange={setUsername}
          autoComplete="username"
        />
        <Field
          icon={<Fingerprint className="h-4 w-4" />}
          placeholder="Password (min. 8 characters)"
          value={pass}
          onChange={setPass}
          type="password"
          autoComplete={mode === "signup" ? "new-password" : "current-password"}
        />
      </div>

      {isPrivacyMode() ? (
        <p className="mt-5 text-center text-[11px] text-muted-foreground leading-relaxed">
          Privacy mode: no third-party connections. Nick and password only — we do not collect your
          identity.
        </p>
      ) : (
        <>
          <div className="mt-5 flex items-center gap-3 text-[11px] text-muted-foreground">
            <span className="h-px flex-1 bg-border/60" />
            or
            <span className="h-px flex-1 bg-border/60" />
          </div>

          <div className="mt-4">
            <TelegramAuthButton />
            <p className="mt-3 text-center text-[11px] text-muted-foreground leading-relaxed">
              Telegram is optional, for deal notifications. No Google, Apple, or email — we do not
              collect your identity.
            </p>
          </div>
        </>
      )}
    </div>
  );
}

function TelegramAuthButton() {
  return (
    <button
      type="button"
      onClick={async () => {
        try {
          const initData = await signInWithTelegram();
          await telegramLogin(initData);
          toast.success("Signed in with Telegram");
        } catch (e) {
          toast.error(formatApiError(e));
        }
      }}
      className="w-full h-11 rounded-xl border border-border bg-secondary/40 text-sm font-medium hover:bg-secondary transition flex items-center justify-center gap-2"
    >
      <Send className="h-4 w-4" />
      Continue with Telegram
    </button>
  );
}

function Field({
  icon,
  placeholder,
  value,
  onChange,
  type = "text",
  autoComplete,
}: {
  icon: React.ReactNode;
  placeholder: string;
  value: string;
  onChange: (s: string) => void;
  type?: string;
  autoComplete?: string;
}) {
  return (
    <label className="flex items-center gap-3 h-12 rounded-xl bg-secondary/50 border border-border/60 px-3.5 focus-within:border-primary/50 transition">
      <span className="text-muted-foreground">{icon}</span>
      <input
        type={type}
        value={value}
        placeholder={placeholder}
        autoComplete={autoComplete}
        onChange={(e) => onChange(e.target.value)}
        className="flex-1 bg-transparent outline-none text-sm placeholder:text-muted-foreground/70"
      />
    </label>
  );
}
