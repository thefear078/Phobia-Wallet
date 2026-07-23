/**
 * Telegram Login — Mini App initData or Login Widget (browser).
 */

import { isDemoMode } from "@/lib/demoMode";
import { getTelegramInitData, isTelegramMiniApp } from "@/lib/telegram/telegramApp";

const BOT_USERNAME = import.meta.env.VITE_TELEGRAM_BOT_USERNAME as string | undefined;

export type TelegramWidgetUser = {
  id: number;
  first_name?: string;
  last_name?: string;
  username?: string;
  photo_url?: string;
  auth_date: number;
  hash: string;
};

declare global {
  interface Window {
    [key: string]: unknown;
  }
}

export function isTelegramLoginConfigured(): boolean {
  return Boolean(BOT_USERNAME?.trim()) || isTelegramMiniApp();
}

function buildDemoInitData(): string {
  const user = JSON.stringify({
    id: 999_000_001,
    username: "demo_user",
    first_name: "Demo",
  });
  return `user=${encodeURIComponent(user)}&auth_date=${Math.floor(Date.now() / 1000)}&hash=demo`;
}

function widgetUserToInitData(user: TelegramWidgetUser): string {
  const params = new URLSearchParams();
  params.set("id", String(user.id));
  if (user.first_name) params.set("first_name", user.first_name);
  if (user.last_name) params.set("last_name", user.last_name);
  if (user.username) params.set("username", user.username);
  if (user.photo_url) params.set("photo_url", user.photo_url);
  params.set("auth_date", String(user.auth_date));
  params.set("hash", user.hash);
  return params.toString();
}

function openTelegramLoginWidget(botUsername: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const callbackName = `tgAuth_${Date.now()}`;
    const container = document.createElement("div");
    container.style.position = "fixed";
    container.style.inset = "0";
    container.style.zIndex = "9999";
    container.style.display = "flex";
    container.style.alignItems = "center";
    container.style.justifyContent = "center";
    container.style.background = "rgba(0,0,0,0.55)";

    const panel = document.createElement("div");
    panel.style.background = "var(--background, #1a2e2b)";
    panel.style.borderRadius = "16px";
    panel.style.padding = "20px";
    panel.style.minWidth = "280px";

    const cleanup = () => {
      delete window[callbackName];
      container.remove();
    };

    window[callbackName] = (user: TelegramWidgetUser) => {
      cleanup();
      resolve(widgetUserToInitData(user));
    };

    const script = document.createElement("script");
    script.async = true;
    script.src = "https://telegram.org/js/telegram-widget.js?22";
    script.setAttribute("data-telegram-login", botUsername);
    script.setAttribute("data-size", "large");
    script.setAttribute("data-onauth", `${callbackName}(user)`);
    script.setAttribute("data-request-access", "write");

    panel.appendChild(script);
    container.appendChild(panel);
    container.addEventListener("click", (e) => {
      if (e.target === container) {
        cleanup();
        reject(new Error("Telegram sign-in cancelled"));
      }
    });
    document.body.appendChild(container);

    window.setTimeout(() => {
      if (document.body.contains(container)) {
        cleanup();
        reject(new Error("Telegram Login Widget failed to load"));
      }
    }, 60_000);
  });
}

export async function signInWithTelegram(): Promise<string> {
  if (isTelegramMiniApp()) {
    const initData = getTelegramInitData();
    if (!initData) throw new Error("Telegram Mini App did not provide initData");
    return initData;
  }

  if (isDemoMode() || !BOT_USERNAME?.trim()) {
    return buildDemoInitData();
  }

  return openTelegramLoginWidget(BOT_USERNAME.trim());
}
