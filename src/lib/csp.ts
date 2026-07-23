export function generateCspNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

export type SecurityHeaderOptions = {
  /** Serving via a Tor hidden service: plain HTTP by design, so no HTTPS-upgrade directives. */
  onion?: boolean;
  /** Clearnet responses advertise the mirror via the Onion-Location header. */
  onionLocation?: string;
};

export function buildContentSecurityPolicy(
  nonce: string,
  opts: SecurityHeaderOptions = {},
): string {
  return [
    "default-src 'self'",
    // 'wasm-unsafe-eval' — hash-wasm (argon2id) compiles WebAssembly for the seed vault.
    // No Google/Apple origins: Umbrella is anonymous-first, OAuth is removed by design.
    `script-src 'self' 'nonce-${nonce}' 'wasm-unsafe-eval' https://telegram.org https://verify.walletconnect.org https://verify.walletconnect.com`,
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "img-src 'self' data: https:",
    "connect-src 'self' wss: http://localhost:3001 https: https://api.monobank.ua https://api.coingecko.com https://blockstream.info https://api.mainnet-beta.solana.com https://check.torproject.org",
    "font-src 'self' data: https://fonts.gstatic.com",
    "frame-src https://oauth.telegram.org https://verify.walletconnect.org https://verify.walletconnect.com",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    // Telegram Web runs Mini Apps in an iframe — allow only Telegram as ancestor.
    "frame-ancestors 'self' https://web.telegram.org",
    // .onion services speak plain HTTP inside Tor — upgrading would break them.
    ...(opts.onion ? [] : ["upgrade-insecure-requests"]),
  ].join("; ");
}

export function buildSecurityHeaders(
  nonce: string,
  opts: SecurityHeaderOptions = {},
): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Security-Policy": buildContentSecurityPolicy(nonce, opts),
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "strict-origin-when-cross-origin",
    // allow-popups keeps Telegram/WalletConnect popups working while isolating the window
    "Cross-Origin-Opener-Policy": "same-origin-allow-popups",
    "Cross-Origin-Resource-Policy": "same-origin",
    "Permissions-Policy": "camera=(self), microphone=(), geolocation=(), payment=(), usb=()",
    "X-Permitted-Cross-Domain-Policies": "none",
  };
  if (!opts.onion) {
    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    if (opts.onionLocation) headers["Onion-Location"] = opts.onionLocation;
  }
  return headers;
}

/** Add nonce to inline `<script>` tags (no `src`) for strict script-src. */
export function injectScriptNonces(html: string, nonce: string): string {
  return html.replace(/<script(?![^>]*\bsrc=)([^>]*)>/gi, `<script nonce="${nonce}"$1>`);
}
