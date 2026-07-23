import { api, type ApiUser } from "@/lib/api/client";

type TelegramWebApp = {
  initData?: string;
  initDataUnsafe?: {
    user?: {
      id: number;
      username?: string;
      first_name?: string;
    };
  };
  ready?: () => void;
  expand?: () => void;
  disableVerticalSwipes?: () => void;
  enableVerticalSwipes?: () => void;
  setHeaderColor?: (color: string) => void;
  setBackgroundColor?: (color: string) => void;
  viewportStableHeight?: number;
  onEvent?: (event: string, handler: () => void) => void;
  offEvent?: (event: string, handler: () => void) => void;
  isExpanded?: boolean;
  platform?: string;
  colorScheme?: "light" | "dark";
  themeParams?: Record<string, string | undefined>;
  HapticFeedback?: {
    impactOccurred?: (style: "light" | "medium" | "heavy" | "rigid" | "soft") => void;
    notificationOccurred?: (type: "error" | "success" | "warning") => void;
    selectionChanged?: () => void;
  };
  BackButton?: {
    show: () => void;
    hide: () => void;
    onClick: (cb: () => void) => void;
    offClick: (cb: () => void) => void;
    isVisible: boolean;
  };
  MainButton?: {
    text: string;
    show: () => void;
    hide: () => void;
    enable: () => void;
    disable: () => void;
    setText: (text: string) => void;
    onClick: (cb: () => void) => void;
    offClick: (cb: () => void) => void;
    isVisible: boolean;
  };
  openTelegramLink?: (url: string) => void;
  openLink?: (url: string) => void;
};

type TelegramWindow = Window & {
  Telegram?: {
    WebApp?: TelegramWebApp;
  };
};

const APP_BG = "#1a2e2b";

function applyViewportHeight(webApp: TelegramWebApp) {
  const h = webApp.viewportStableHeight;
  if (h && h > 0) {
    document.documentElement.style.setProperty("--app-height", `${h}px`);
  }
}

function applyThemeParams(webApp: TelegramWebApp) {
  const tp = webApp.themeParams;
  if (!tp) return;

  const bg = tp.bg_color ?? APP_BG;
  const text = tp.text_color;
  const hint = tp.hint_color;
  const button = tp.button_color;

  document.documentElement.style.setProperty("--tg-bg", bg);
  if (text) document.documentElement.style.setProperty("--tg-text", text);
  if (hint) document.documentElement.style.setProperty("--tg-hint", hint);
  if (button) document.documentElement.style.setProperty("--tg-button", button);

  webApp.setHeaderColor?.(bg);
  webApp.setBackgroundColor?.(bg);
}

export function hapticImpact(style: "light" | "medium" | "heavy" = "light") {
  getTelegramWebApp()?.HapticFeedback?.impactOccurred?.(style);
}

export function hapticNotification(type: "error" | "success" | "warning") {
  getTelegramWebApp()?.HapticFeedback?.notificationOccurred?.(type);
}

export function shareTelegramLink(url: string, _text?: string) {
  const webApp = getTelegramWebApp();
  if (webApp?.openTelegramLink) {
    webApp.openTelegramLink(url);
  } else if (typeof navigator.share === "function") {
    void navigator.share({ url, title: _text });
  } else {
    void navigator.clipboard.writeText(url);
  }
}

export function getTelegramWebApp(): TelegramWebApp | null {
  if (typeof window === "undefined") return null;
  const tgWindow = window as TelegramWindow;
  return tgWindow.Telegram?.WebApp ?? null;
}

/**
 * Telegram opens Mini Apps with `#tgWebAppData=…` in the URL — detectable
 * without loading any telegram.org script, so ordinary (and Tor) visitors
 * never make a third-party request.
 */
export function isLikelyTelegramEnv(): boolean {
  if (typeof window === "undefined") return false;
  if (getTelegramWebApp()) return true;
  const marker = window.location.hash + window.location.search;
  return marker.includes("tgWebAppData") || marker.includes("tgWebAppPlatform");
}

let scriptPromise: Promise<void> | null = null;

/** Inject telegram-web-app.js on demand (only inside the Telegram client). */
export function ensureTelegramScript(): Promise<void> {
  if (typeof window === "undefined") return Promise.resolve();
  if (getTelegramWebApp()) return Promise.resolve();
  if (!scriptPromise) {
    scriptPromise = new Promise((resolve) => {
      const el = document.createElement("script");
      el.src = "https://telegram.org/js/telegram-web-app.js";
      el.async = true;
      el.onload = () => resolve();
      el.onerror = () => resolve(); // degrade gracefully — app still works as plain web
      document.head.appendChild(el);
    });
  }
  return scriptPromise;
}

export function isTelegramMiniApp(): boolean {
  const webApp = getTelegramWebApp();
  return Boolean(webApp?.initData);
}

export function getTelegramInitData(): string | null {
  return getTelegramWebApp()?.initData || null;
}

/** Call once on app mount — fixes Mini App rubber-band / swipe-to-close UX. */
export function initTelegramWebApp(): void {
  const webApp = getTelegramWebApp();
  if (!webApp) return;

  webApp.ready?.();
  webApp.expand?.();
  webApp.disableVerticalSwipes?.();
  applyThemeParams(webApp);
  applyViewportHeight(webApp);

  document.documentElement.classList.add("tg-mini-app");

  const onViewport = () => applyViewportHeight(webApp);
  const onTheme = () => applyThemeParams(webApp);
  webApp.onEvent?.("viewportChanged", onViewport);
  webApp.onEvent?.("themeChanged", onTheme);
}

export async function tryTelegramAuth(): Promise<{ accessToken: string; user: ApiUser } | null> {
  const webApp = getTelegramWebApp();
  const initData = webApp?.initData;
  if (!initData) return null;

  initTelegramWebApp();
  return api.telegramAuth(initData);
}
