/** API path prefixes proxied to NestJS (dev Vite proxy + prod server proxy). */
export const API_PREFIXES = [
  "/auth",
  "/users",
  "/wallets",
  "/bank-accounts",
  "/p2p",
  "/rates",
  "/kyc",
  "/webhooks",
  "/telegram",
  "/health",
] as const;

/** Prefixes that are also frontend pages — only their subpaths are API calls. */
const PAGE_COLLISIONS = new Set(["/p2p"]);

export function isApiPath(pathname: string): boolean {
  return API_PREFIXES.some((p) => {
    if (pathname.startsWith(`${p}/`)) return true;
    return pathname === p && !PAGE_COLLISIONS.has(p);
  });
}

export function resolveApiOrigin(): string | undefined {
  const fromEnv =
    (typeof process !== "undefined" && (process.env.API_ORIGIN || process.env.VITE_API_URL)) ||
    undefined;
  return fromEnv?.replace(/\/$/, "");
}
