import { useSyncExternalStore } from "react";
import { api, refreshSession, setAccessToken } from "@/lib/api/client";
import {
  ensureTelegramScript,
  isLikelyTelegramEnv,
  tryTelegramAuth,
} from "@/lib/telegram/telegramApp";
import { isPrivacyMode } from "@/lib/privacyMode";

export type Session = {
  authed: boolean;
  userId: string;
  name: string;
  username: string;
  email: string;
  lang: string;
  emailVerified: boolean;
  loading: boolean;
};

const KEY = "umbra.session.v1";
const DEFAULTS: Session = {
  authed: false,
  userId: "",
  name: "",
  username: "",
  email: "",
  lang: "uk",
  emailVerified: false,
  loading: true,
};

function readMeta(): Partial<Session> {
  if (typeof window === "undefined") return {};
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return {};
    return JSON.parse(raw) as Partial<Session>;
  } catch {
    return {};
  }
}

let state: Session = { ...DEFAULTS, ...readMeta(), loading: typeof window === "undefined" };
const listeners = new Set<() => void>();
const emit = () => {
  for (const l of listeners) l();
};
const subscribe = (l: () => void) => {
  listeners.add(l);
  return () => listeners.delete(l);
};
const getSnapshot = () => state;
const getServerSnapshot = () => DEFAULTS;

function writeMeta(next: Session) {
  if (typeof window !== "undefined") {
    const { loading: _, ...persist } = next;
    try {
      window.localStorage.setItem(KEY, JSON.stringify(persist));
    } catch {
      /* storage unavailable */
    }
  }
}

function setState(patch: Partial<Session>) {
  state = { ...state, ...patch };
  writeMeta(state);
  emit();
}

export async function bootstrapSession() {
  if (typeof window === "undefined") return;

  // Legacy cleanup — access tokens are memory-only (XSS hardening)
  try {
    sessionStorage.removeItem("umbra.access");
  } catch {
    /* ignore */
  }

  try {
    const refreshed = await refreshSession();
    applyAuthResult(refreshed.accessToken, refreshed.user);
    return;
  } catch {
    /* no valid refresh cookie */
  }

  if (!isPrivacyMode() && isLikelyTelegramEnv()) {
    try {
      await ensureTelegramScript();
      const res = await tryTelegramAuth();
      if (res) {
        applyAuthResult(res.accessToken, res.user);
        return;
      }
    } catch {
      /* ignore */
    }
  }
  setState({ ...DEFAULTS, loading: false });
}

export async function registerAccount(username: string, password: string) {
  const res = await api.register(username, password);
  applyAuthResult(res.accessToken, res.user, username);
}

export async function loginAccount(username: string, password: string) {
  const res = await api.login(username, password);
  applyAuthResult(res.accessToken, res.user, username);
}

export async function telegramLogin(initData: string) {
  const res = await api.telegramAuth(initData);
  const tgName =
    res.user.name ??
    res.user.username ??
    (res.user.email.startsWith("telegram_") ? "Telegram User" : res.user.email.split("@")[0]);
  applyAuthResult(res.accessToken, res.user, tgName);
}

function applyAuthResult(
  accessToken: string,
  user: Awaited<ReturnType<typeof api.login>>["user"],
  fallbackName?: string,
) {
  setAccessToken(accessToken);
  setState({
    authed: true,
    userId: user.id,
    name: user.name ?? user.username ?? fallbackName ?? user.email.split("@")[0],
    username: user.username ?? fallbackName ?? "",
    email: user.email,
    lang: user.lang,
    emailVerified: user.emailVerified,
    loading: false,
  });
}

/** Patch session metadata locally (e.g. after email change) without re-auth. */
export function updateSessionMeta(
  patch: Partial<Pick<Session, "email" | "emailVerified" | "name" | "lang">>,
) {
  setState(patch);
}

export async function signOut() {
  try {
    await api.logout();
  } catch {
    /* already logged out server-side */
  }
  setAccessToken(null);
  setState({ ...DEFAULTS, loading: false });
}

export function useSession(): Session {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}

if (typeof window !== "undefined") {
  void bootstrapSession();
  window.addEventListener("storage", (e) => {
    if (e.key === KEY) {
      state = { ...state, ...readMeta() };
      emit();
    }
  });
}
