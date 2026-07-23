/**
 * Privacy (Tor) mode — no third-party requests at all.
 *
 * Auto-enabled on .onion hosts; can be toggled manually in settings.
 * When active the app must not touch telegram.org, walletconnect, or any
 * other external origin: Telegram login is hidden and the Mini App script
 * is never injected.
 */

const PRIVACY_KEY = "umbra.privacy.v1";

export function isOnionHost(): boolean {
  if (typeof window === "undefined") return false;
  return window.location.hostname.endsWith(".onion");
}

export function isPrivacyMode(): boolean {
  if (typeof window === "undefined") return false;
  if (isOnionHost()) return true;
  try {
    return window.localStorage.getItem(PRIVACY_KEY) === "1";
  } catch {
    return false;
  }
}

/** Manual toggle; the .onion auto-detection cannot be turned off. */
export function setPrivacyMode(on: boolean) {
  try {
    if (on) window.localStorage.setItem(PRIVACY_KEY, "1");
    else window.localStorage.removeItem(PRIVACY_KEY);
  } catch {
    /* ignore */
  }
}
