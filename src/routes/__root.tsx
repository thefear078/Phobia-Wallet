import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  Outlet,
  Link,
  createRootRouteWithContext,
  useRouter,
  HeadContent,
  Scripts,
} from "@tanstack/react-router";
import { useEffect, useState, type ReactNode } from "react";

import appCss from "../styles.css?url";
import { reportLovableError } from "../lib/lovable-error-reporting";
import { Toaster } from "@/components/ui/sonner";
import { TelegramInit } from "@/components/TelegramInit";
import { LanguageDirection } from "@/components/LanguageDirection";
import { useSession } from "@/lib/authStore";
import { Welcome } from "@/components/Welcome";
import { SeedOnboarding } from "@/components/SeedOnboarding";
import { hasSeedVault, hasSkippedSeedSetup } from "@/lib/wallet/seedManager";
import { LinkAccountsPrompt } from "@/components/LinkAccountsPrompt";
import { usePortfolio } from "@/hooks/usePortfolio";
import { isDemoMode } from "@/lib/demoMode";
import { hasSkippedLinkPrompt } from "@/lib/linkPromptSkip";
import { MOBILE_VIEWPORT_META, lockMobileViewportZoom } from "@/lib/mobileViewport";
import { THEME_INIT_SCRIPT, initTheme } from "@/lib/theme";
import { useRouterState } from "@tanstack/react-router";

function NotFoundComponent() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-7xl font-bold text-foreground">404</h1>
        <h2 className="mt-4 text-xl font-semibold text-foreground">Page not found</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          The page you're looking for doesn't exist or has been moved.
        </p>
        <div className="mt-6">
          <Link
            to="/"
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            Go home
          </Link>
        </div>
      </div>
    </div>
  );
}

function ErrorComponent({ error, reset }: { error: Error; reset: () => void }) {
  console.error(error);
  const router = useRouter();
  useEffect(() => {
    reportLovableError(error, { boundary: "tanstack_root_error_component" });
  }, [error]);

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-xl font-semibold tracking-tight text-foreground">
          This page didn't load
        </h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Something went wrong on our end. You can try refreshing or head back home.
        </p>
        <div className="mt-6 flex flex-wrap justify-center gap-2">
          <button
            onClick={() => {
              router.invalidate();
              reset();
            }}
            className="inline-flex items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
          >
            Try again
          </button>
          <a
            href="/"
            className="inline-flex items-center justify-center rounded-md border border-input bg-background px-4 py-2 text-sm font-medium text-foreground transition-colors hover:bg-accent"
          >
            Go home
          </a>
        </div>
      </div>
    </div>
  );
}

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: MOBILE_VIEWPORT_META },
      { title: "Umbrella Wallet — private crypto wallet, exchange & P2P" },
      {
        name: "description",
        content:
          "Umbrella Wallet — a discreet private crypto wallet with instant exchange, cards, and P2P trading.",
      },
      { name: "author", content: "Umbrella Wallet" },
      { property: "og:title", content: "Umbrella Wallet" },
      { property: "og:description", content: "Private crypto wallet with exchange and P2P." },
      { property: "og:type", content: "website" },
      { name: "twitter:card", content: "summary_large_image" },
      { name: "twitter:site", content: "@Lovable" },
    ],
    links: [
      {
        rel: "stylesheet",
        href: appCss,
      },
      { rel: "icon", href: "/umbrella-icon.svg", type: "image/svg+xml" },
      { rel: "apple-touch-icon", href: "/umbrella-icon.svg" },
      { rel: "preconnect", href: "https://fonts.googleapis.com" },
      { rel: "preconnect", href: "https://fonts.gstatic.com", crossOrigin: "anonymous" },
      {
        rel: "stylesheet",
        href: "https://fonts.googleapis.com/css2?family=DM+Sans:ital,opsz,wght@0,9..40,400;0,9..40,500;0,9..40,600;1,9..40,400&family=Instrument+Serif:ital@0;1&family=JetBrains+Mono:wght@400;500&display=swap",
      },
    ],
  }),
  shellComponent: RootShell,
  component: RootComponent,
  notFoundComponent: NotFoundComponent,
  errorComponent: ErrorComponent,
});

function RootShell({ children }: { children: ReactNode }) {
  return (
    <html lang="en" className="h-full">
      {/* telegram-web-app.js is injected lazily by TelegramInit only inside the
          Telegram client — ordinary and Tor visitors make no third-party requests. */}
      <head>
        <HeadContent />
        {/* Applies the stored theme before first paint (no flash). The CSP nonce
            is injected server-side by injectScriptNonces(). */}
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
      </head>
      <body className="app-viewport">
        {children}
        <Scripts />
      </body>
    </html>
  );
}

function RootComponent() {
  const { queryClient } = Route.useRouteContext();

  useEffect(() => lockMobileViewportZoom(), []);
  useEffect(() => initTheme(), []);

  return (
    <QueryClientProvider client={queryClient}>
      <LanguageDirection />
      <TelegramInit />
      <AuthGate />
      <Toaster position="top-center" theme="dark" richColors />
    </QueryClientProvider>
  );
}

function AuthGate() {
  const { authed, loading, userId } = useSession();
  const { hasLinks, isLoading: portfolioLoading } = usePortfolio();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const onLinkPage = pathname.startsWith("/link");
  const isLegalPage = pathname.startsWith("/legal/");
  const [seedState, setSeedState] = useState<"checking" | "needed" | "done">("checking");

  useEffect(() => {
    if (!authed || !userId) return;
    setSeedState("checking");
    if (hasSkippedSeedSetup(userId)) {
      setSeedState("done");
      return;
    }
    void hasSeedVault(userId).then((v) => setSeedState(v ? "done" : "needed"));
  }, [authed, userId]);

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center text-muted-foreground text-sm">
        Loading…
      </div>
    );
  }
  if (!authed && isLegalPage) return <Outlet />;
  if (!authed) return <Welcome />;
  if (seedState === "checking" && !isLegalPage) {
    return (
      <div className="min-h-screen flex items-center justify-center text-muted-foreground text-sm">
        Loading…
      </div>
    );
  }
  if (seedState === "needed" && !isLegalPage) {
    return <SeedOnboarding onDone={() => setSeedState("done")} />;
  }
  if (
    !isDemoMode() &&
    !portfolioLoading &&
    !hasLinks &&
    !hasSkippedLinkPrompt() &&
    !onLinkPage &&
    pathname === "/"
  ) {
    return (
      <LinkAccountsPrompt
        onLinked={() => window.location.reload()}
        onSkip={() => window.location.reload()}
      />
    );
  }
  return <Outlet />;
}
