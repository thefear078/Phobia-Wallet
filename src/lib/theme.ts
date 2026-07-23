import { useSyncExternalStore } from "react";

/**
 * Black & white theme control.
 * "system" follows the OS; "light"/"dark" pin it. Applied by toggling the
 * `dark` class on <html> — the same class the CSS variants key off.
 */
export type Theme = "light" | "dark" | "system";

export const THEME_KEY = "umbra-theme";
/** Nothing stored yet → dark (wallet default). "system" is opt-in via the toggle. */
export const DEFAULT_THEME: Theme = "dark";

/** Inlined in <head> before paint to avoid a flash of the wrong theme. */
export const THEME_INIT_SCRIPT = `(function(){try{var t=localStorage.getItem(${JSON.stringify(
  THEME_KEY,
)});var d=t===null||t==="dark"||(t==="system"&&window.matchMedia("(prefers-color-scheme: dark)").matches);document.documentElement.classList.toggle("dark",d);document.documentElement.style.colorScheme=d?"dark":"light";}catch(e){}})();`;

function systemPrefersDark(): boolean {
  if (typeof window === "undefined") return true;
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

export function readStoredTheme(): Theme {
  if (typeof window === "undefined") return DEFAULT_THEME;
  try {
    const raw = window.localStorage.getItem(THEME_KEY);
    return raw === "light" || raw === "dark" || raw === "system" ? raw : DEFAULT_THEME;
  } catch {
    return DEFAULT_THEME;
  }
}

export function resolveTheme(theme: Theme): "light" | "dark" {
  return theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
}

function apply(theme: Theme) {
  if (typeof document === "undefined") return;
  const dark = resolveTheme(theme) === "dark";
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
}

let current: Theme = "system";
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

/** Call once on mount — syncs the store with what the no-FOUC script decided. */
export function initTheme() {
  current = readStoredTheme();
  apply(current);
  if (typeof window !== "undefined") {
    // Live-follow the OS while on "system"
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    mq.addEventListener("change", () => {
      if (current === "system") {
        apply(current);
        emit();
      }
    });
  }
}

export function setTheme(theme: Theme) {
  current = theme;
  try {
    window.localStorage.setItem(THEME_KEY, theme);
  } catch {
    /* storage unavailable — theme still applies for this session */
  }
  apply(theme);
  emit();
}

export function useTheme(): { theme: Theme; resolved: "light" | "dark" } {
  const theme = useSyncExternalStore(
    (cb) => {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },
    () => current,
    () => "system" as Theme,
  );
  return { theme, resolved: resolveTheme(theme) };
}
