/**
 * Tor routing helper — detect .onion, check exit, document SOCKS5 / Tor2web options.
 * Native SOCKS5 is not available in a normal browser without an extension or Tor Browser.
 */

import { isPrivacyMode } from "@/lib/privacyMode";

export const TorConfig = {
  /** Tor SOCKS5 proxy — user runs Tor Browser or a separate Tor daemon. */
  SOCKS5_PROXY: "socks5://127.0.0.1:9050",
  /** Public Tor2web gateways (browser without native SOCKS5). */
  TOR2WEB_GATEWAYS: ["https://onion.to", "https://tor2web.io"],
} as const;

export function isRunningViaTor(): boolean {
  if (typeof window === "undefined") return false;
  return window.location.hostname.endsWith(".onion");
}

/**
 * In-browser SOCKS5 is unavailable without an extension.
 * Returns null unless already on a .onion host (traffic is Tor-native there).
 */
export function getTorProxyUrl(): string | null {
  if (isRunningViaTor()) return null;
  return null;
}

export async function checkTorConnection(): Promise<boolean> {
  if (isRunningViaTor()) return true;
  // Privacy mode must not contact third-party check hosts.
  if (isPrivacyMode()) return false;
  try {
    const res = await fetch("https://check.torproject.org/api/ip", {
      signal: AbortSignal.timeout(5000),
    });
    const data = (await res.json()) as { IsTor?: boolean };
    return Boolean(data.IsTor);
  } catch {
    return false;
  }
}
