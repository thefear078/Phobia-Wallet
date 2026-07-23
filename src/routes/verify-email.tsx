import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { MailCheck, MailX, Loader2 } from "lucide-react";
import { api } from "@/lib/api/client";
import { updateSessionMeta } from "@/lib/authStore";
import { UmbrellaLogo } from "@/components/UmbrellaLogo";

export const Route = createFileRoute("/verify-email")({
  validateSearch: (search: Record<string, unknown>): { token?: string } => ({
    token: typeof search.token === "string" ? search.token : undefined,
  }),
  head: () => ({ meta: [{ title: "Email verification — Umbrella Wallet" }] }),
  component: VerifyEmailPage,
});

function VerifyEmailPage() {
  const { token } = Route.useSearch();
  const [status, setStatus] = useState<"pending" | "ok" | "error">(token ? "pending" : "error");
  const [message, setMessage] = useState("");

  useEffect(() => {
    if (!token) {
      setMessage("The link has no verification token");
      return;
    }
    api
      .verifyEmail(token)
      .then(() => {
        updateSessionMeta({ emailVerified: true });
        setStatus("ok");
      })
      .catch((e) => {
        setStatus("error");
        setMessage(e instanceof Error ? e.message : "The link is invalid or expired");
      });
  }, [token]);

  return (
    <div className="app-scroll min-h-screen bg-background text-foreground flex justify-center">
      <div className="w-full max-w-md min-h-screen flex flex-col items-center justify-center px-6 text-center">
        <UmbrellaLogo className="h-12 w-12" />
        <div className="mt-8 h-16 w-16 rounded-3xl border border-border/60 bg-card flex items-center justify-center">
          {status === "pending" && (
            <Loader2 className="h-7 w-7 animate-spin text-muted-foreground" />
          )}
          {status === "ok" && <MailCheck className="h-7 w-7 text-[color:var(--success)]" />}
          {status === "error" && <MailX className="h-7 w-7 text-destructive" />}
        </div>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">
          {status === "pending" && "Verifying email…"}
          {status === "ok" && "Email verified"}
          {status === "error" && "Verification failed"}
        </h1>
        <p className="mt-2 text-sm text-muted-foreground leading-relaxed max-w-[300px]">
          {status === "ok"
            ? "Thank you! Deal and security notifications will now be sent to this address."
            : message}
        </p>
        <Link
          to="/"
          className="mt-8 w-full max-w-[280px] rounded-2xl bg-[image:var(--gradient-primary)] text-primary-foreground font-semibold text-[15px] py-3.5"
        >
          Go to home
        </Link>
      </div>
    </div>
  );
}
