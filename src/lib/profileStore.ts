import { useSyncExternalStore } from "react";

export type Lang = "uk" | "en" | "ru" | "zh" | "es" | "ar";
export type KycStatus = "none" | "pending" | "verified";

export type Profile = {
  tfa: boolean;
  biometric: boolean;
  push: boolean;
  email: boolean;
  priceAlerts: boolean;
  lang: Lang;
  kyc: KycStatus;
};

const KEY = "cw.profile.v1";
const DEFAULTS: Profile = {
  tfa: false,
  biometric: false,
  push: true,
  email: false,
  priceAlerts: true,
  lang: "en",
  kyc: "none",
};

function read(): Profile {
  if (typeof window === "undefined") return DEFAULTS;
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) return DEFAULTS;
    return { ...DEFAULTS, ...(JSON.parse(raw) as Partial<Profile>) };
  } catch {
    return DEFAULTS;
  }
}

let state: Profile = read();
const listeners = new Set<() => void>();

function emit() {
  for (const l of listeners) l();
}

function subscribe(l: () => void) {
  listeners.add(l);
  return () => listeners.delete(l);
}

function getSnapshot() {
  return state;
}

function getServerSnapshot() {
  return DEFAULTS;
}

export function setProfile(patch: Partial<Profile>) {
  state = { ...state, ...patch };
  if (typeof window !== "undefined") {
    try {
      window.localStorage.setItem(KEY, JSON.stringify(state));
    } catch {
      /* storage unavailable */
    }
  }
  emit();
}

export function useProfile(): Profile {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}

// Cross-tab sync
if (typeof window !== "undefined") {
  window.addEventListener("storage", (e) => {
    if (e.key === KEY) {
      state = read();
      emit();
    }
  });
}
