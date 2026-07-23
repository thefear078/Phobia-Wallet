import { resolveApiOrigin } from "./config";
import { isDemoMode } from "@/lib/demoMode";
import { isPrivacyMode } from "@/lib/privacyMode";
import { isRunningViaTor } from "@/lib/wallet/tor";
import { demoApi } from "./demo";

function resolveApiBase(): string {
  const configured = import.meta.env.VITE_API_URL as string | undefined;
  if (configured) return configured.replace(/\/$/, "");
  // SSR: direct API URL from runtime env or local fallback.
  if (typeof window === "undefined") {
    return resolveApiOrigin() ?? "http://localhost:3001";
  }
  // Browser: same-origin — Vite proxy (dev) or Vercel server proxy (prod + API_ORIGIN).
  return "";
}

const API_BASE = resolveApiBase();

/** Privacy / Tor: never call absolute external API origins from the browser. */
function requestBase(): string {
  if (typeof window !== "undefined" && (isPrivacyMode() || isRunningViaTor())) {
    return "";
  }
  return API_BASE;
}

function privacyHeadersActive(): boolean {
  return typeof window !== "undefined" && (isPrivacyMode() || isRunningViaTor());
}

const TELEMETRY_PATH =
  /\/(analytics|telemetry|collect|metrics|sentry|ingest|lovable-events)(\/|$)/i;

export type MarketPriceRow = {
  usd: number;
  uah: number;
  eur: number;
  change24h: number;
};

export type MarketSnapshot = {
  prices: Record<string, MarketPriceRow>;
  updatedAt: number;
};

export type ApiUser = {
  id: string;
  email: string;
  username?: string | null;
  name: string | null;
  lang: string;
  emailVerified: boolean;
  tfaEnabled?: boolean;
  pushEnabled?: boolean;
  emailAlerts?: boolean;
  priceAlerts?: boolean;
  kyc?: string;
};

export type P2pOfferSummary = {
  id: string;
  merchant: string;
  asset: string;
  fiat: string;
  price: number;
  min: number | null;
  max: number | null;
  methods: string[];
  side: string;
};

export type P2pOrderDto = {
  id: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  amount: number;
  paymentMethod?: string | null;
  status: string;
  cryptoTxHash?: string | null;
  fiatPaymentReference?: string | null;
  disputeReason?: string | null;
  createdAt?: string;
  updatedAt?: string;
  offer?: P2pOfferSummary | null;
};

let accessToken: string | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  if (privacyHeadersActive() && TELEMETRY_PATH.test(path)) {
    return undefined as T;
  }

  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");
  if (privacyHeadersActive()) headers.set("X-Privacy-Mode", "1");
  const token = getAccessToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const base = requestBase();
  let res: Response;
  try {
    res = await fetch(`${base}${path}`, {
      ...init,
      headers,
      credentials: "include",
    });
  } catch {
    throw new ApiError(
      0,
      "No connection to the server. Check your internet or wait until the API is available.",
    );
  }

  if (res.status === 401 && path !== "/auth/refresh") {
    const refreshed = await refreshSession().catch(() => null);
    if (refreshed) {
      headers.set("Authorization", `Bearer ${refreshed.accessToken}`);
      const retry = await fetch(`${base}${path}`, { ...init, headers, credentials: "include" });
      if (!retry.ok) throw await toApiError(retry);
      return retry.json() as Promise<T>;
    }
  }

  if (!res.ok) {
    throw await toApiError(res);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

/** Extract the human-readable `message` from a NestJS error body. */
async function toApiError(res: Response): Promise<ApiError> {
  const text = await res.text();
  let message = text;
  try {
    const parsed = JSON.parse(text) as { message?: string | string[] };
    if (Array.isArray(parsed.message)) message = parsed.message.join(", ");
    else if (parsed.message) message = parsed.message;
  } catch {
    /* plain text body */
  }

  const normalized = message.trim();
  if (
    res.status === 404 ||
    res.status === 502 ||
    res.status === 503 ||
    res.status === 504 ||
    /^not\s*found$/i.test(normalized)
  ) {
    if (!normalized || /^not\s*found$/i.test(normalized) || res.status >= 500) {
      message =
        "API on Render is offline. Open https://dashboard.render.com/blueprint/new?repo=https://github.com/kiurakku/umbra-wallet → Deploy Blueprint → wait for Live, then retry login.";
    }
  }

  return new ApiError(res.status, message);
}

export class ApiError extends Error {
  constructor(
    public status: number,
    body: string,
  ) {
    super(body || `HTTP ${status}`);
    this.name = "ApiError";
  }
}

export async function refreshSession() {
  const data = await request<{ accessToken: string; user: ApiUser }>("/auth/refresh", {
    method: "POST",
  });
  setAccessToken(data.accessToken);
  return data;
}

export const api = {
  register: (username: string, password: string) =>
    isDemoMode()
      ? demoApi.register(username, password)
      : request<{ accessToken: string; user: ApiUser }>("/auth/register", {
          method: "POST",
          body: JSON.stringify({ username, password }),
        }),

  login: (username: string, password: string) =>
    isDemoMode()
      ? demoApi.login(username, password)
      : request<{ accessToken: string; user: ApiUser }>("/auth/login", {
          method: "POST",
          body: JSON.stringify({ username, password }),
        }),

  telegramAuth: (initData: string) =>
    isDemoMode()
      ? demoApi.telegramAuth(initData)
      : request<{ accessToken: string; user: ApiUser }>("/auth/telegram", {
          method: "POST",
          body: JSON.stringify({ initData }),
        }),

  logout: () =>
    isDemoMode() ? demoApi.logout() : request<{ ok: boolean }>("/auth/logout", { method: "POST" }),

  requestEmailVerification: (email: string) =>
    isDemoMode()
      ? Promise.resolve({ sent: false, demo: true })
      : request<{ sent: boolean; demo?: boolean }>("/auth/email/request-verification", {
          method: "POST",
          body: JSON.stringify({ email }),
        }),

  verifyEmail: (token: string) =>
    isDemoMode()
      ? Promise.resolve({ verified: true })
      : request<{ verified: boolean }>("/auth/email/verify", {
          method: "POST",
          body: JSON.stringify({ token }),
        }),

  me: () => (isDemoMode() ? demoApi.me() : request<ApiUser & { kyc: string }>("/users/me")),

  updateMe: (patch: Partial<ApiUser>) =>
    isDemoMode()
      ? demoApi.updateMe(patch)
      : request<ApiUser>("/users/me", { method: "PATCH", body: JSON.stringify(patch) }),

  myP2pOffers: () =>
    isDemoMode()
      ? demoApi.myP2pOffers()
      : request<
          Array<{
            id: string;
            merchant: string;
            asset: string;
            fiat: string;
            quoteKind: "fiat" | "crypto";
            price: number;
            min: number | null;
            max: number | null;
            methods: string[];
            side: string;
            status: string;
          }>
        >("/p2p/offers/mine"),

  createP2pOffer: (data: {
    asset: string;
    fiatCurrency: string;
    quoteKind?: "fiat" | "crypto";
    price: number;
    minAmount?: number;
    maxAmount?: number;
    paymentMethods: string[];
    side: "buy" | "sell";
  }) =>
    isDemoMode()
      ? demoApi.createP2pOffer(data)
      : request<{ id: string }>("/p2p/offers", { method: "POST", body: JSON.stringify(data) }),

  updateP2pOffer: (
    offerId: string,
    patch: { price?: number; minAmount?: number; maxAmount?: number; paymentMethods?: string[] },
  ) =>
    isDemoMode()
      ? demoApi.updateP2pOffer(offerId, patch)
      : request<{ id: string }>(`/p2p/offers/${offerId}`, {
          method: "PATCH",
          body: JSON.stringify(patch),
        }),

  deleteP2pOffer: (offerId: string) =>
    isDemoMode()
      ? demoApi.deleteP2pOffer(offerId)
      : request<{ ok: boolean }>(`/p2p/offers/${offerId}`, { method: "DELETE" }),

  p2pOffers: (params?: { asset?: string; method?: string; side?: string; fiat?: string }) => {
    if (isDemoMode()) return demoApi.p2pOffers(params);
    const q = new URLSearchParams();
    if (params?.asset) q.set("asset", params.asset);
    if (params?.method) q.set("method", params.method);
    if (params?.side) q.set("side", params.side);
    if (params?.fiat) q.set("fiat", params.fiat);
    const qs = q.toString();
    return request<
      Array<{
        id: string;
        merchant: string;
        asset: string;
        fiat: string;
        quoteKind: "fiat" | "crypto";
        price: number;
        min: number | null;
        max: number | null;
        methods: string[];
        side: string;
      }>
    >(`/p2p/offers${qs ? `?${qs}` : ""}`);
  },

  listP2pOrders: (role?: "buyer" | "seller"): Promise<P2pOrderDto[]> => {
    if (isDemoMode()) return demoApi.listP2pOrders(role);
    const qs = role ? `?role=${role}` : "";
    return request<P2pOrderDto[]>(`/p2p/orders${qs}`);
  },

  getP2pOrder: (orderId: string): Promise<P2pOrderDto> =>
    isDemoMode() ? demoApi.getP2pOrder(orderId) : request<P2pOrderDto>(`/p2p/orders/${orderId}`),

  createP2pOrder: (offerId: string, amount: number): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.createP2pOrder(offerId, amount)
      : request<P2pOrderDto>("/p2p/orders", {
          method: "POST",
          body: JSON.stringify({ offerId, amount }),
        }),

  startP2pFiat: (orderId: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.startP2pFiat(orderId)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/await-fiat`, { method: "PATCH" }),

  submitP2pFiatProof: (orderId: string, fiatPaymentReference: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.submitP2pFiatProof(orderId, fiatPaymentReference)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/fiat-proof`, {
          method: "PATCH",
          body: JSON.stringify({ fiatPaymentReference }),
        }),

  submitP2pCryptoProof: (orderId: string, cryptoTxHash: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.submitP2pCryptoProof(orderId, cryptoTxHash)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/crypto-proof`, {
          method: "PATCH",
          body: JSON.stringify({ cryptoTxHash }),
        }),

  completeP2pOrder: (orderId: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.completeP2pOrder(orderId)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/complete`, { method: "PATCH" }),

  cancelP2pOrder: (orderId: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.cancelP2pOrder(orderId)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/cancel`, { method: "PATCH" }),

  disputeP2pOrder: (orderId: string, reason: string): Promise<P2pOrderDto> =>
    isDemoMode()
      ? demoApi.disputeP2pOrder(orderId, reason)
      : request<P2pOrderDto>(`/p2p/orders/${orderId}/dispute`, {
          method: "POST",
          body: JSON.stringify({ reason }),
        }),

  listWallets: () =>
    isDemoMode()
      ? demoApi.listWallets()
      : request<Array<{ id: string; chain: string; address: string; label: string | null }>>(
          "/wallets",
        ),

  walletChallenge: () =>
    isDemoMode()
      ? demoApi.walletChallenge()
      : request<{ nonce: string; message: string; expiresIn: number }>("/wallets/challenge"),

  linkWallet: (data: {
    chain: string;
    address: string;
    label?: string;
    watchOnly?: boolean;
    message?: string;
    signature?: string;
  }) =>
    isDemoMode()
      ? demoApi.linkWallet(data)
      : request("/wallets", { method: "POST", body: JSON.stringify(data) }),

  unlinkWallet: (id: string) =>
    isDemoMode() ? demoApi.unlinkWallet(id) : request(`/wallets/${id}`, { method: "DELETE" }),

  walletBalances: (chain?: string) =>
    isDemoMode()
      ? demoApi.walletBalances()
      : request<
          Array<{
            id: string;
            chain: string;
            address: string;
            balance: { native: string; usd: number | null };
          }>
        >(`/wallets/balances${chain ? `?chain=${encodeURIComponent(chain)}` : ""}`),

  listBankAccounts: () =>
    isDemoMode()
      ? demoApi.listBankAccounts()
      : request<
          Array<{
            id: string;
            provider: string;
            bankName: string | null;
            maskedNumber: string | null;
            status: string;
          }>
        >("/bank-accounts"),

  linkBankAccount: (data: {
    provider: string;
    providerAccountId: string;
    bankName?: string;
    maskedNumber?: string;
  }) =>
    isDemoMode()
      ? demoApi.linkBankAccount(data)
      : request("/bank-accounts", { method: "POST", body: JSON.stringify(data) }),

  linkMonobank: (personalToken: string) =>
    isDemoMode()
      ? demoApi.linkMonobank(personalToken)
      : request<{ linked: unknown[]; clientName: string }>("/bank-accounts/monobank/link", {
          method: "POST",
          body: JSON.stringify({ personalToken }),
        }),

  revokeBankAccount: (id: string) =>
    isDemoMode()
      ? demoApi.revokeBankAccount(id)
      : request(`/bank-accounts/${id}`, { method: "DELETE" }),

  marketSnapshot: () =>
    isDemoMode()
      ? demoApi.marketRates().then((rows) => {
          const prices: Record<string, MarketPriceRow> = {};
          for (const row of rows) {
            prices[row.symbol] = {
              usd: row.price,
              uah: row.price * 41.5,
              eur: row.price * 0.92,
              change24h: row.change24h,
            };
          }
          return { prices, updatedAt: Date.now() } satisfies MarketSnapshot;
        })
      : request<MarketSnapshot>("/rates/market"),

  /** Flat list for portfolio / legacy UI (USD spot). */
  marketRates: async () => {
    if (isDemoMode()) return demoApi.marketRates();
    const snap = await request<
      MarketSnapshot | Array<{ symbol: string; price: number; change24h: number }>
    >("/rates/market");
    if (Array.isArray(snap)) return snap;
    return Object.entries(snap.prices).map(([symbol, p]) => ({
      symbol,
      price: p.usd,
      change24h: p.change24h,
    }));
  },

  marketPairs: () =>
    isDemoMode()
      ? demoApi.marketPairs()
      : request<Array<{ base: string; quote: string; rate: number; pair: string }>>("/rates/pairs"),

  marketChart: (symbol: string, days = 7) =>
    isDemoMode()
      ? demoApi.marketChart(symbol, days)
      : request<{ symbol: string; days: number; prices: number[]; timestamps?: number[] }>(
          `/rates/chart?symbol=${encodeURIComponent(symbol)}&days=${days}`,
        ),

  marketSparkline: (symbol: string) =>
    isDemoMode()
      ? demoApi.marketChart(symbol, 7).then((c) => ({
          symbol: c.symbol,
          prices: c.prices,
          timestamps: c.prices.map((_, i) => Date.now() - (c.prices.length - 1 - i) * 3_600_000),
        }))
      : request<{ symbol: string; prices: number[]; timestamps: number[] }>(
          `/rates/sparkline/${encodeURIComponent(symbol)}`,
        ),

  convertRate: (from: string, to: string, amount: number) =>
    isDemoMode()
      ? demoApi.getRate(from, to).then((r) => ({
          result: amount * r.rate,
          rate: r.rate,
          fee: 0,
        }))
      : request<{ result: number; rate: number; fee: number }>(
          `/rates/convert?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&amount=${encodeURIComponent(String(amount))}`,
        ),

  getRate: (base: string, quote: string) =>
    isDemoMode()
      ? demoApi.getRate(base, quote)
      : request<{ base: string; quote: string; rate: number }>(
          `/rates?base=${encodeURIComponent(base)}&quote=${encodeURIComponent(quote)}`,
        ),

  kycStatus: () =>
    isDemoMode() ? demoApi.kycStatus() : request<{ status: string; level: string }>("/kyc/status"),

  kycStart: () =>
    isDemoMode()
      ? demoApi.kycStart()
      : request<{ verificationUrl: string; status: string }>("/kyc/start", { method: "POST" }),
};
