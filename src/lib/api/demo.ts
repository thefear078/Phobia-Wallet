import { assets, p2pOffers } from "@/lib/mockData";
import type { ApiUser } from "./client";

const STORE_KEY = "umbra.demo.v1";

/** Ready-made test account for demo mode */
export const DEMO_TEST_USERNAME = "demo";
export const DEMO_TEST_PASSWORD = "demo12345";

type WalletRow = { id: string; chain: string; address: string; label: string | null };
type BankRow = {
  id: string;
  provider: string;
  bankName: string | null;
  maskedNumber: string | null;
  status: string;
};
type UserRow = { password: string; user: ApiUser };
type P2pOrder = {
  id: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  amount: number;
  status: string;
  paymentMethod?: string | null;
  cryptoTxHash?: string | null;
  fiatPaymentReference?: string | null;
  disputeReason?: string | null;
  createdAt?: string;
  updatedAt?: string;
};

type P2pOffer = {
  id: string;
  merchantId: string;
  merchant: string;
  asset: string;
  fiat: string;
  quoteKind: "fiat" | "crypto";
  price: number;
  min: number | null;
  max: number | null;
  methods: string[];
  side: "buy" | "sell";
  status: "active" | "paused" | "deleted";
};

type DemoStore = {
  users: Record<string, UserRow>;
  wallets: Record<string, WalletRow[]>;
  banks: Record<string, BankRow[]>;
  orders: Record<string, P2pOrder>;
  offers?: Record<string, P2pOffer>;
  profiles: Record<string, Partial<ApiUser> & { kyc?: string }>;
};

function uid() {
  return crypto.randomUUID();
}

/**
 * Demo credentials live only in this browser's localStorage, but still never
 * store the raw password — a salted SHA-256 digest is kept instead.
 */
async function hashDemoPassword(username: string, password: string): Promise<string> {
  const data = new TextEncoder().encode(`umbra-demo:${username.toLowerCase()}:${password}`);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return `sha256:${Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("")}`;
}

function readStore(): DemoStore {
  if (typeof window === "undefined") {
    return { users: {}, wallets: {}, banks: {}, orders: {}, profiles: {} };
  }
  try {
    const raw = localStorage.getItem(STORE_KEY);
    if (!raw) return { users: {}, wallets: {}, banks: {}, orders: {}, profiles: {} };
    return JSON.parse(raw) as DemoStore;
  } catch {
    return { users: {}, wallets: {}, banks: {}, orders: {}, profiles: {} };
  }
}

function writeStore(store: DemoStore) {
  if (typeof window !== "undefined") {
    localStorage.setItem(STORE_KEY, JSON.stringify(store));
  }
}

function ensureDemoSeedUser(store: DemoStore) {
  const key = DEMO_TEST_USERNAME.toLowerCase();
  if (store.users[key]) return store;
  const user = makeUser(DEMO_TEST_USERNAME);
  store.users[key] = { password: DEMO_TEST_PASSWORD, user };
  seedLinks(store, user.id);
  writeStore(store);
  return store;
}

function loadStore(): DemoStore {
  const store = readStore();
  if (typeof window === "undefined") return store;
  return ensureDemoSeedUser(store);
}

let activeDemoUserId: string | null = null;

function setActiveDemoUser(id: string | null) {
  activeDemoUserId = id;
}

function currentUserId(): string | null {
  return activeDemoUserId;
}

function makeUser(username: string, id = uid()): ApiUser {
  return {
    id,
    email: `${username}@demo.local`,
    username,
    name: username,
    lang: "uk",
    emailVerified: true,
  };
}

function authResponse(user: ApiUser) {
  setActiveDemoUser(user.id);
  return { accessToken: `demo:${user.id}`, user };
}

function seedLinks(store: DemoStore, userId: string) {
  if (!store.wallets[userId]?.length) {
    store.wallets[userId] = [
      {
        id: uid(),
        chain: "ethereum",
        address: "0xDEMO742d35Cc6634C0532925a3b844Bc9e7595f0",
        label: "Demo ETH",
      },
      {
        id: uid(),
        chain: "ton",
        address: "EQDemo_ton_wallet_address_for_ui_preview_only",
        label: "Demo TON",
      },
    ];
  }
  if (!store.banks[userId]?.length) {
    store.banks[userId] = [
      {
        id: uid(),
        provider: "monobank",
        bankName: "Monobank",
        maskedNumber: "**** 4242",
        status: "active",
      },
    ];
  }
}

const CHAIN_BALANCE: Record<string, { native: string; usd: number | null }> = {
  ethereum: { native: "1.284", usd: 4519.8 },
  ton: { native: "245.5", usd: 1576.1 },
  bitcoin: { native: "0.0342", usd: 2340.0 },
};

const delay = () => new Promise((r) => setTimeout(r, 120));

/** Allowed order-status transitions — mirrors the backend state machine. */
const ORDER_TRANSITIONS: Record<string, string[]> = {
  created: ["awaiting_fiat_payment", "disputed", "cancelled"],
  awaiting_fiat_payment: ["fiat_payment_confirmed", "disputed", "cancelled"],
  fiat_payment_confirmed: ["crypto_sent", "disputed"],
  crypto_sent: ["completed", "disputed"],
  completed: [],
  disputed: ["cancelled", "completed"],
  cancelled: [],
};

const BOT_ACTION_DELAY_MS = 4000;

function isBot(id: string) {
  return id.startsWith("merchant-");
}

function findAnyOffer(store: DemoStore, offerId: string) {
  const custom = store.offers?.[offerId];
  if (custom) return custom;
  const mock = p2pOffers.find((o) => o.id === offerId);
  if (!mock) return undefined;
  return {
    id: mock.id,
    merchantId: `merchant-${mock.id}`,
    merchant: mock.merchant,
    asset: mock.asset,
    fiat: mock.fiat,
    price: mock.price,
    min: mock.min,
    max: mock.max,
    methods: mock.methods,
    side: mock.side,
    status: "active" as const,
  };
}

function offerSummary(store: DemoStore, offerId: string) {
  const o = findAnyOffer(store, offerId);
  if (!o) return null;
  const { id, merchant, asset, fiat, price, min, max, methods, side } = o;
  return { id, merchant, asset, fiat, price, min, max, methods, side };
}

function withOffer(store: DemoStore, order: P2pOrder) {
  return { ...order, offer: offerSummary(store, order.offerId) };
}

function touchOrder(order: P2pOrder) {
  order.updatedAt = new Date().toISOString();
}

function releaseOfferIfTerminal(store: DemoStore, order: P2pOrder) {
  if (order.status !== "completed" && order.status !== "cancelled") return;
  const offer = store.offers?.[order.offerId];
  if (offer && offer.status === "paused") offer.status = "active";
}

/**
 * Simulated counterparty: when the other side of a demo deal is a fake
 * merchant, it "acts" a few seconds after the user's last step so the whole
 * flow is playable end-to-end.
 */
function maybeAdvanceBot(store: DemoStore, order: P2pOrder): boolean {
  const last = order.updatedAt ? Date.parse(order.updatedAt) : 0;
  if (Date.now() - last < BOT_ACTION_DELAY_MS) return false;

  let next: Partial<P2pOrder> | null = null;
  if (isBot(order.buyerId)) {
    if (order.status === "created") next = { status: "awaiting_fiat_payment" };
    else if (order.status === "awaiting_fiat_payment")
      next = {
        status: "fiat_payment_confirmed",
        fiatPaymentReference: `DEMO-${order.id.slice(0, 6).toUpperCase()}`,
      };
    else if (order.status === "crypto_sent") next = { status: "completed" };
  } else if (isBot(order.sellerId) && order.status === "fiat_payment_confirmed") {
    next = {
      status: "crypto_sent",
      cryptoTxHash: `0x${order.id.replace(/-/g, "").padEnd(64, "0").slice(0, 64)}`,
    };
  }

  if (!next) return false;
  Object.assign(order, next);
  touchOrder(order);
  releaseOfferIfTerminal(store, order);
  return true;
}

function transitionDemoOrder(
  store: DemoStore,
  orderId: string,
  target: string,
  patch: Partial<P2pOrder> = {},
) {
  const order = store.orders[orderId];
  if (!order) throw new Error("Deal not found");
  const userId = currentUserId();
  if (!userId || (order.buyerId !== userId && order.sellerId !== userId)) {
    throw new Error("No access to this deal");
  }
  if (!(ORDER_TRANSITIONS[order.status] ?? []).includes(target)) {
    throw new Error(`Invalid transition from status "${order.status}"`);
  }
  Object.assign(order, patch, { status: target });
  touchOrder(order);
  releaseOfferIfTerminal(store, order);
  return order;
}

export const demoApi = {
  async register(username: string, password: string) {
    await delay();
    const store = loadStore();
    const key = username.trim().toLowerCase();
    if (store.users[key]) throw new Error("User already exists");
    const user = makeUser(username.trim());
    store.users[key] = { password: await hashDemoPassword(username.trim(), password), user };
    seedLinks(store, user.id);
    writeStore(store);
    return authResponse(user);
  },

  async login(username: string, password: string) {
    await delay();
    const store = loadStore();
    const name = username.trim();
    const row = store.users[name.toLowerCase()];
    if (!row) throw new Error("Invalid nickname or password");
    const hashed = await hashDemoPassword(name, password);
    if (row.password !== hashed) {
      // Legacy plaintext record — accept once, then upgrade to the hash.
      if (row.password !== password) throw new Error("Invalid nickname or password");
      row.password = hashed;
    }
    seedLinks(store, row.user.id);
    writeStore(store);
    return authResponse(row.user);
  },

  async telegramAuth(_initData: string) {
    await delay();
    const store = loadStore();
    const key = "telegram:demo";
    let row = store.users[key];
    if (!row) {
      const user = makeUser("telegram_user");
      row = { password: "", user };
      store.users[key] = row;
      seedLinks(store, user.id);
      writeStore(store);
    }
    return authResponse(row.user);
  },

  async logout() {
    await delay();
    setActiveDemoUser(null);
    return { ok: true };
  },

  async me() {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const user = Object.values(store.users).find((u) => u.user.id === userId)?.user;
    if (!user) throw new Error("Unauthorized");
    const profile = store.profiles[userId] ?? {};
    return { ...user, ...profile, kyc: profile.kyc ?? "none" };
  },

  async updateMe(patch: Partial<ApiUser>) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    store.profiles[userId] = { ...store.profiles[userId], ...patch };
    writeStore(store);
    const base = Object.values(store.users).find((u) => u.user.id === userId)!.user;
    return { ...base, ...store.profiles[userId] };
  },

  async p2pOffers(params?: { asset?: string; side?: string; method?: string; fiat?: string }) {
    await delay();
    const store = readStore();
    const custom = Object.values(store.offers ?? {})
      .filter((o) => o.status === "active")
      .map(({ id, merchant, asset, fiat, quoteKind, price, min, max, methods, side }) => ({
        id,
        merchant,
        asset,
        fiat,
        quoteKind: quoteKind ?? "fiat",
        price,
        min,
        max,
        methods,
        side,
      }));
    const mock = p2pOffers.map(
      ({ id, merchant, asset, fiat, price, min, max, methods, side, rating, deals }) => ({
        id,
        merchant,
        asset,
        fiat,
        quoteKind: "fiat" as const,
        price,
        min,
        max,
        methods,
        side,
        rating,
        deals,
      }),
    );
    return [...custom, ...mock]
      .filter((o) => (params?.asset ? o.asset === params.asset : true))
      .filter((o) => (params?.side ? o.side === params.side : true))
      .filter((o) => (params?.fiat ? o.fiat === params.fiat : true))
      .filter((o) =>
        params?.method
          ? o.methods.some((m) => m.toLowerCase().includes(params.method!.toLowerCase()))
          : true,
      );
  },

  async myP2pOffers() {
    await delay();
    const userId = currentUserId();
    if (!userId) return [];
    const store = readStore();
    return Object.values(store.offers ?? {})
      .filter((o) => o.merchantId === userId && o.status !== "deleted")
      .map(({ id, merchant, asset, fiat, quoteKind, price, min, max, methods, side, status }) => ({
        id,
        merchant,
        asset,
        fiat,
        quoteKind: quoteKind ?? "fiat",
        price,
        min,
        max,
        methods,
        side,
        status,
      }));
  },

  async createP2pOffer(data: {
    asset: string;
    fiatCurrency: string;
    quoteKind?: "fiat" | "crypto";
    price: number;
    minAmount?: number;
    maxAmount?: number;
    paymentMethods: string[];
    side: "buy" | "sell";
  }) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const me = Object.values(store.users).find((u) => u.user.id === userId)?.user;
    const offer: P2pOffer = {
      id: uid(),
      merchantId: userId,
      merchant: me?.name ?? me?.username ?? "Me",
      asset: data.asset.toUpperCase(),
      fiat: data.fiatCurrency.toUpperCase(),
      quoteKind: data.quoteKind ?? "fiat",
      price: data.price,
      min: data.minAmount ?? null,
      max: data.maxAmount ?? null,
      methods: data.paymentMethods,
      side: data.side,
      status: "active",
    };
    store.offers = { ...(store.offers ?? {}), [offer.id]: offer };
    writeStore(store);
    return { id: offer.id };
  },

  async updateP2pOffer(
    offerId: string,
    patch: { price?: number; minAmount?: number; maxAmount?: number; paymentMethods?: string[] },
  ) {
    await delay();
    const userId = currentUserId();
    const store = loadStore();
    const offer = store.offers?.[offerId];
    if (!offer || offer.merchantId !== userId) throw new Error("Offer not found");
    if (patch.price !== undefined) offer.price = patch.price;
    if (patch.minAmount !== undefined) offer.min = patch.minAmount;
    if (patch.maxAmount !== undefined) offer.max = patch.maxAmount;
    if (patch.paymentMethods) offer.methods = patch.paymentMethods;
    writeStore(store);
    return { id: offer.id };
  },

  async deleteP2pOffer(offerId: string) {
    await delay();
    const userId = currentUserId();
    const store = loadStore();
    const offer = store.offers?.[offerId];
    if (!offer || offer.merchantId !== userId) throw new Error("Offer not found");
    const hasActive = Object.values(store.orders).some(
      (o) => o.offerId === offerId && o.status !== "completed" && o.status !== "cancelled",
    );
    if (hasActive) throw new Error("Cannot delete an offer with an active deal");
    offer.status = "deleted";
    writeStore(store);
    return { ok: true };
  },

  async listP2pOrders(role?: "buyer" | "seller") {
    await delay();
    const userId = currentUserId();
    if (!userId) return [];
    const store = loadStore();
    let dirty = false;
    const all = Object.values(store.orders).filter(
      (o) => o.buyerId === userId || o.sellerId === userId,
    );
    for (const o of all) dirty = maybeAdvanceBot(store, o) || dirty;
    if (dirty) writeStore(store);
    const visible =
      role === "buyer"
        ? all.filter((o) => o.buyerId === userId)
        : role === "seller"
          ? all.filter((o) => o.sellerId === userId)
          : all;
    return visible
      .sort((a, b) => Date.parse(b.updatedAt ?? "") - Date.parse(a.updatedAt ?? ""))
      .map((o) => withOffer(store, o));
  },

  async getP2pOrder(orderId: string) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const order = store.orders[orderId];
    if (!order || (order.buyerId !== userId && order.sellerId !== userId)) {
      throw new Error("Deal not found");
    }
    if (maybeAdvanceBot(store, order)) writeStore(store);
    return withOffer(store, order);
  },

  async createP2pOrder(offerId: string, amount: number) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const offer = findAnyOffer(store, offerId);
    if (!offer || offer.status !== "active") throw new Error("Offer unavailable");
    if (offer.merchantId === userId) throw new Error("Cannot trade on your own offer");
    if (offer.min != null && amount < offer.min) throw new Error("Amount below minimum");
    if (offer.max != null && amount > offer.max) throw new Error("Amount above maximum");

    const isMerchantSelling = offer.side === "sell";
    const now = new Date().toISOString();
    const order: P2pOrder = {
      id: uid(),
      offerId,
      buyerId: isMerchantSelling ? userId : offer.merchantId,
      sellerId: isMerchantSelling ? offer.merchantId : userId,
      amount,
      status: "created",
      paymentMethod: offer.methods[0] ?? null,
      cryptoTxHash: null,
      fiatPaymentReference: null,
      createdAt: now,
      updatedAt: now,
    };
    store.orders[order.id] = order;
    const custom = store.offers?.[offerId];
    if (custom) custom.status = "paused";
    writeStore(store);
    return withOffer(store, order);
  },

  async startP2pFiat(orderId: string) {
    await delay();
    const store = loadStore();
    const order = transitionDemoOrder(store, orderId, "awaiting_fiat_payment");
    writeStore(store);
    return withOffer(store, order);
  },

  async submitP2pFiatProof(orderId: string, ref: string) {
    await delay();
    const store = loadStore();
    const order = transitionDemoOrder(store, orderId, "fiat_payment_confirmed", {
      fiatPaymentReference: ref,
    });
    writeStore(store);
    return withOffer(store, order);
  },

  async submitP2pCryptoProof(orderId: string, hash: string) {
    await delay();
    if (!/^0x[a-fA-F0-9]{64}$/.test(hash)) {
      throw new Error("Invalid tx hash format (0x + 64 hex characters)");
    }
    const store = loadStore();
    const order = transitionDemoOrder(store, orderId, "crypto_sent", { cryptoTxHash: hash });
    writeStore(store);
    return withOffer(store, order);
  },

  async completeP2pOrder(orderId: string) {
    await delay();
    const store = loadStore();
    const userId = currentUserId();
    const existing = store.orders[orderId];
    if (existing && userId && existing.buyerId !== userId) {
      throw new Error("Only the buyer can confirm receipt");
    }
    const order = transitionDemoOrder(store, orderId, "completed");
    writeStore(store);
    return withOffer(store, order);
  },

  async cancelP2pOrder(orderId: string) {
    await delay();
    const store = loadStore();
    const order = transitionDemoOrder(store, orderId, "cancelled");
    writeStore(store);
    return withOffer(store, order);
  },

  async disputeP2pOrder(orderId: string, reason: string) {
    await delay();
    const store = loadStore();
    const order = transitionDemoOrder(store, orderId, "disputed", { disputeReason: reason });
    writeStore(store);
    return withOffer(store, order);
  },

  async listWallets() {
    await delay();
    const userId = currentUserId();
    if (!userId) return [];
    return readStore().wallets[userId] ?? [];
  },

  async walletChallenge() {
    await delay();
    return {
      nonce: "demo-nonce",
      message: "Umbrella Wallet — Link Wallet\nNonce: demo-nonce\nUser: demo\nIssued: demo",
      expiresIn: 300,
    };
  },

  async linkWallet(data: {
    chain: string;
    address: string;
    label?: string;
    watchOnly?: boolean;
    message?: string;
    signature?: string;
  }) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const row: WalletRow = {
      id: uid(),
      chain: data.chain,
      address: data.address,
      label: data.label ?? (data.watchOnly ? "Watch-only" : null),
    };
    store.wallets[userId] = [...(store.wallets[userId] ?? []), row];
    writeStore(store);
    return row;
  },

  async unlinkWallet(id: string) {
    await delay();
    const userId = currentUserId();
    if (!userId) return;
    const store = loadStore();
    store.wallets[userId] = (store.wallets[userId] ?? []).filter((w) => w.id !== id);
    writeStore(store);
  },

  async walletBalances() {
    await delay();
    const wallets = await demoApi.listWallets();
    return wallets.map((w) => ({
      id: w.id,
      chain: w.chain,
      address: w.address,
      balance: CHAIN_BALANCE[w.chain] ?? { native: "0", usd: 0 },
    }));
  },

  async listBankAccounts() {
    await delay();
    const userId = currentUserId();
    if (!userId) return [];
    return readStore().banks[userId] ?? [];
  },

  async linkBankAccount(data: {
    provider: string;
    providerAccountId: string;
    bankName?: string;
    maskedNumber?: string;
  }) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const row: BankRow = {
      id: uid(),
      provider: data.provider,
      bankName: data.bankName ?? null,
      maskedNumber: data.maskedNumber ?? null,
      status: "active",
    };
    store.banks[userId] = [...(store.banks[userId] ?? []), row];
    writeStore(store);
    return row;
  },

  async linkMonobank(_personalToken: string) {
    await delay();
    const userId = currentUserId();
    if (!userId) throw new Error("Unauthorized");
    const store = loadStore();
    const row: BankRow = {
      id: uid(),
      provider: "monobank",
      bankName: "Monobank",
      maskedNumber: "**** 9012",
      status: "active",
    };
    store.banks[userId] = [...(store.banks[userId] ?? []), row];
    writeStore(store);
    return { linked: [row], clientName: "Demo Client" };
  },

  async revokeBankAccount(id: string) {
    await delay();
    const userId = currentUserId();
    if (!userId) return;
    const store = loadStore();
    store.banks[userId] = (store.banks[userId] ?? []).map((b) =>
      b.id === id ? { ...b, status: "revoked" } : b,
    );
    writeStore(store);
  },

  async marketRates() {
    await delay();
    return assets.map((a) => ({ symbol: a.symbol, price: a.price, change24h: a.change24h }));
  },

  async marketPairs() {
    await delay();
    const uah = 41.5;
    return [
      { base: "BTC", quote: "UAH", rate: assets[0].price * uah, pair: "BTC/UAH" },
      { base: "ETH", quote: "UAH", rate: assets[1].price * uah, pair: "ETH/UAH" },
      { base: "USDT", quote: "UAH", rate: uah, pair: "USDT/UAH" },
      { base: "BTC", quote: "USD", rate: assets[0].price, pair: "BTC/USD" },
      { base: "ETH", quote: "USD", rate: assets[1].price, pair: "ETH/USD" },
    ];
  },

  async marketChart(symbol: string, days: number) {
    await delay();
    const row = assets.find((a) => a.symbol === symbol.toUpperCase());
    const base = row?.price ?? 100;
    const points = Math.min(Math.max(days, 1), 30) * 4;
    const prices: number[] = [];
    let v = base * 0.92;
    for (let i = 0; i < points; i++) {
      v += (Math.random() - 0.45) * base * 0.02;
      prices.push(Number(v.toFixed(2)));
    }
    prices.push(base);
    return { symbol: symbol.toUpperCase(), days, prices };
  },

  async getRate(base: string, quote: string) {
    await delay();
    if (base.toUpperCase() === "USD" && quote.toUpperCase() === "UAH") {
      return { base: "USD", quote: "UAH", rate: 41.5 };
    }
    const row = assets.find((a) => a.symbol === base);
    const rate = row?.price ?? 1;
    return { base, quote, rate };
  },

  async kycStatus() {
    await delay();
    const userId = currentUserId();
    const kyc = userId ? (readStore().profiles[userId]?.kyc ?? "none") : "none";
    return { status: kyc, level: kyc === "approved" ? "basic" : "none" };
  },

  async kycStart() {
    await delay();
    const userId = currentUserId();
    if (userId) {
      const store = loadStore();
      store.profiles[userId] = { ...store.profiles[userId], kyc: "pending" };
      writeStore(store);
    }
    return { verificationUrl: "/legal/privacy", status: "pending" };
  },
};
