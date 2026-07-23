import {
  ConflictException,
  ForbiddenException,
  Injectable,
  NotFoundException,
  OnModuleInit,
  UnauthorizedException,
} from "@nestjs/common";
import { randomUUID } from "crypto";
import { hashPassword, hashToken, verifyPassword } from "../common/crypto.util";
import { P2P_CANCELLABLE_STATUSES, P2P_ORDER_TRANSITIONS } from "../p2p/p2p-transitions";

export type DemoUser = {
  id: string;
  email: string;
  username: string | null;
  name: string | null;
  lang: string;
  emailVerified: boolean;
  passwordHash: string | null;
  tfaEnabled: boolean;
  pushEnabled: boolean;
  emailAlerts: boolean;
  priceAlerts: boolean;
  telegramId: bigint | null;
  telegramUsername: string | null;
  telegramNotifications: boolean;
  oauthProvider: string | null;
  oauthSub: string | null;
  createdAt: Date;
};

type DemoWallet = {
  id: string;
  userId: string;
  chain: string;
  address: string;
  label: string | null;
  linkedAt: Date;
};

type DemoBank = {
  id: string;
  userId: string;
  provider: string;
  providerAccountId: string;
  bankName: string | null;
  maskedNumber: string | null;
  maskedIban: string | null;
  accountType: string | null;
  currency: string | null;
  status: string;
  linkedAt: Date;
};

type DemoOffer = {
  id: string;
  merchantId: string;
  merchantName: string;
  asset: string;
  fiatCurrency: string;
  price: number;
  minAmount: number | null;
  maxAmount: number | null;
  paymentMethods: string[];
  side: string;
  status: string;
};

type DemoOrder = {
  id: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  amount: number;
  paymentMethod: string | null;
  status: string;
  cryptoTxHash: string | null;
  fiatPaymentReference: string | null;
  disputeReason: string | null;
  createdAt: Date;
  updatedAt: Date;
};

type DemoRefresh = {
  id: string;
  userId: string;
  jti: string;
  tokenHash: string;
  expiresAt: Date;
  revokedAt: Date | null;
};

const BASE_RATES: Record<string, { price: number; change24h: number }> = {
  BTC: { price: 68420.55, change24h: 2.14 },
  ETH: { price: 3520.1, change24h: -0.82 },
  TON: { price: 6.42, change24h: 4.31 },
  USDT: { price: 1.0, change24h: 0.01 },
  SOL: { price: 148.3, change24h: 5.62 },
  USDC: { price: 1.0, change24h: -0.02 },
};

@Injectable()
export class DemoStoreService implements OnModuleInit {
  users = new Map<string, DemoUser>();
  usersByUsername = new Map<string, string>();
  usersByTelegram = new Map<string, string>();
  refreshTokens: DemoRefresh[] = [];
  wallets: DemoWallet[] = [];
  banks: DemoBank[] = [];
  offers: DemoOffer[] = [];
  orders = new Map<string, DemoOrder>();
  kyc = new Map<string, { status: string; level: string; provider: string | null }>();
  walletChallenges = new Map<string, { nonce: string; message: string }>();
  liveRates = { ...BASE_RATES };
  private rateTimer: ReturnType<typeof setInterval> | null = null;

  onModuleInit() {
    void this.seed().then(() => {
      this.rateTimer = setInterval(() => this.jitterRates(), 30_000);
    });
  }

  private async seed() {
    const demoId = randomUUID();
    const demoUser: DemoUser = {
      id: demoId,
      email: "demo@umbra.local",
      username: "demo",
      name: "Demo User",
      lang: "uk",
      emailVerified: true,
      passwordHash: await hashPassword("demo12345"),
      tfaEnabled: false,
      pushEnabled: true,
      emailAlerts: false,
      priceAlerts: true,
      telegramId: null,
      telegramUsername: null,
      telegramNotifications: true,
      oauthProvider: null,
      oauthSub: null,
      createdAt: new Date(),
    };
    this.users.set(demoId, demoUser);
    this.usersByUsername.set("demo", demoId);

    this.wallets.push(
      {
        id: randomUUID(),
        userId: demoId,
        chain: "ethereum",
        address: "0xDEMO742d35Cc6634C0532925a3b844Bc9e7595f0",
        label: "Demo ETH",
        linkedAt: new Date(),
      },
      {
        id: randomUUID(),
        userId: demoId,
        chain: "ton",
        address: "EQDemo_ton_wallet_address_for_ui_preview_only",
        label: "Demo TON",
        linkedAt: new Date(),
      },
    );

    this.banks.push({
      id: randomUUID(),
      userId: demoId,
      provider: "monobank",
      providerAccountId: "demo-mono-account",
      bankName: "Monobank",
      maskedNumber: "**** 4242",
      maskedIban: "UA****4242",
      accountType: "black",
      currency: "UAH",
      status: "active",
      linkedAt: new Date(),
    });

    this.kyc.set(demoId, { status: "approved", level: "basic", provider: "demo" });

    const merchantId = demoId;
    this.offers = [
      {
        id: "demo-offer-1",
        merchantId,
        merchantName: "CryptoKing",
        asset: "USDT",
        fiatCurrency: "UAH",
        price: 41.25,
        minAmount: 500,
        maxAmount: 50000,
        paymentMethods: ["monobank", "privatbank"],
        side: "sell",
        status: "active",
      },
      {
        id: "demo-offer-2",
        merchantId,
        merchantName: "FastTrader",
        asset: "USDT",
        fiatCurrency: "UAH",
        price: 41.28,
        minAmount: 1000,
        maxAmount: 25000,
        paymentMethods: ["monobank"],
        side: "buy",
        status: "active",
      },
    ];
  }

  private jitterRates() {
    for (const sym of Object.keys(this.liveRates)) {
      const row = this.liveRates[sym];
      const delta = (Math.random() - 0.5) * 0.4;
      row.change24h = Math.round((row.change24h + delta) * 100) / 100;
      row.price = Math.round(row.price * (1 + delta / 100) * 100) / 100;
    }
  }

  private uid() {
    return randomUUID();
  }

  private normalizeUsername(raw: string) {
    return raw.trim().toLowerCase();
  }

  async register(usernameRaw: string, password: string) {
    const username = this.normalizeUsername(usernameRaw);
    if (this.usersByUsername.has(username)) {
      throw new ConflictException("This username is already taken");
    }
    const id = this.uid();
    const user: DemoUser = {
      id,
      email: `${username}@umbra.local`,
      username,
      name: usernameRaw.trim(),
      lang: "uk",
      emailVerified: true,
      passwordHash: await hashPassword(password),
      tfaEnabled: false,
      pushEnabled: true,
      emailAlerts: false,
      priceAlerts: true,
      telegramId: null,
      telegramUsername: null,
      telegramNotifications: true,
      oauthProvider: null,
      oauthSub: null,
      createdAt: new Date(),
    };
    this.users.set(id, user);
    this.usersByUsername.set(username, id);
    return user;
  }

  async login(usernameRaw: string, password: string) {
    const username = this.normalizeUsername(usernameRaw);
    const id = this.usersByUsername.get(username);
    const user = id ? this.users.get(id) : undefined;
    if (!user?.passwordHash) throw new UnauthorizedException("Invalid username or password");
    const ok = await verifyPassword(user.passwordHash, password);
    if (!ok) throw new UnauthorizedException("Invalid username or password");
    return user;
  }

  telegramLogin(tg: { id: number; username?: string; first_name?: string; last_name?: string }) {
    const tgKey = String(tg.id);
    const userId = this.usersByTelegram.get(tgKey);
    const displayName =
      [tg.first_name, tg.last_name].filter(Boolean).join(" ").trim() ||
      tg.username ||
      "Telegram User";

    if (!userId) {
      const id = this.uid();
      const user: DemoUser = {
        id,
        email: `telegram_${tg.id}@umbra.local`,
        username: tg.username ?? null,
        name: displayName,
        lang: "uk",
        emailVerified: true,
        passwordHash: null,
        tfaEnabled: false,
        pushEnabled: true,
        emailAlerts: false,
        priceAlerts: true,
        telegramId: BigInt(tg.id),
        telegramUsername: tg.username ?? null,
        telegramNotifications: true,
        oauthProvider: null,
        oauthSub: null,
        createdAt: new Date(),
      };
      this.users.set(id, user);
      this.usersByTelegram.set(tgKey, id);
      return user;
    }

    const user = this.users.get(userId)!;
    if (tg.username && user.telegramUsername !== tg.username) {
      user.telegramUsername = tg.username;
    }
    return user;
  }

  storeRefreshToken(userId: string, refreshToken: string, jti: string) {
    this.refreshTokens.push({
      id: this.uid(),
      userId,
      jti,
      tokenHash: hashToken(refreshToken),
      expiresAt: new Date(Date.now() + 30 * 86400000),
      revokedAt: null,
    });
  }

  revokeRefresh(refreshToken: string, jti?: string) {
    const hash = hashToken(refreshToken);
    for (const t of this.refreshTokens) {
      const match = jti ? t.jti === jti : t.tokenHash === hash;
      if (match && !t.revokedAt) t.revokedAt = new Date();
    }
  }

  findRefresh(refreshToken: string, userId: string, jti?: string) {
    const hash = hashToken(refreshToken);
    return this.refreshTokens.find(
      (t) =>
        t.userId === userId &&
        !t.revokedAt &&
        t.expiresAt > new Date() &&
        (jti ? t.jti === jti : t.tokenHash === hash),
    );
  }

  getUser(id: string) {
    const user = this.users.get(id);
    if (!user) throw new NotFoundException();
    return user;
  }

  updateUser(id: string, patch: Partial<DemoUser>) {
    const user = this.getUser(id);
    Object.assign(user, patch);
    return user;
  }

  deleteUser(id: string) {
    this.users.delete(id);
    this.wallets = this.wallets.filter((w) => w.userId !== id);
    this.banks = this.banks.filter((b) => b.userId !== id);
    this.kyc.delete(id);
  }

  listWallets(userId: string) {
    return this.wallets.filter((w) => w.userId === userId);
  }

  linkWallet(userId: string, chain: string, address: string, label: string | null) {
    const exists = this.wallets.some(
      (w) => w.userId === userId && w.chain === chain && w.address === address,
    );
    if (exists) throw new ConflictException("Wallet already linked");
    const row: DemoWallet = {
      id: this.uid(),
      userId,
      chain,
      address,
      label,
      linkedAt: new Date(),
    };
    this.wallets.push(row);
    return row;
  }

  unlinkWallet(userId: string, walletId: string) {
    const idx = this.wallets.findIndex((w) => w.id === walletId && w.userId === userId);
    if (idx < 0) throw new NotFoundException("Linked wallet not found");
    this.wallets.splice(idx, 1);
    return { ok: true };
  }

  walletBalances(userId: string) {
    return this.listWallets(userId).map((w) => ({
      ...w,
      balance: {
        native: w.chain === "ethereum" ? "1.284" : w.chain === "ton" ? "245.5" : "0.0342",
        usd: null as number | null,
      },
    }));
  }

  createWalletChallenge(userId: string) {
    const nonce = randomUUID().replace(/-/g, "").slice(0, 16);
    const message = [
      "Umbrella Wallet — Link Wallet",
      `Nonce: ${nonce}`,
      `User: ${userId}`,
      `Issued: ${new Date().toISOString()}`,
    ].join("\n");
    this.walletChallenges.set(userId, { nonce, message });
    return { nonce, message, expiresIn: 300 };
  }

  getWalletChallenge(userId: string) {
    return this.walletChallenges.get(userId);
  }

  clearWalletChallenge(userId: string) {
    this.walletChallenges.delete(userId);
  }

  listBanks(userId: string) {
    return this.banks.filter((b) => b.userId === userId && b.status === "active");
  }

  linkBank(
    userId: string,
    data: {
      provider: string;
      bankName?: string;
      maskedNumber?: string;
      maskedIban?: string;
      accountType?: string;
      currency?: string;
      providerAccountId?: string;
    },
  ) {
    const row: DemoBank = {
      id: this.uid(),
      userId,
      provider: data.provider,
      providerAccountId: data.providerAccountId ?? this.uid(),
      bankName: data.bankName ?? null,
      maskedNumber: data.maskedNumber ?? null,
      maskedIban: data.maskedIban ?? null,
      accountType: data.accountType ?? null,
      currency: data.currency ?? null,
      status: "active",
      linkedAt: new Date(),
    };
    this.banks.push(row);
    return row;
  }

  bankBalance(userId: string, accountId: string) {
    const row = this.banks.find(
      (b) => b.id === accountId && b.userId === userId && b.status === "active",
    );
    if (!row) throw new NotFoundException("Linked bank account not found");
    return {
      accountId: row.id,
      provider: row.provider,
      providerAccountId: row.providerAccountId,
      balance: 12450.75,
      creditLimit: 0,
      currency: row.currency ?? "UAH",
      accountType: row.accountType,
      maskedIban: row.maskedIban,
    };
  }

  revokeBank(userId: string, id: string) {
    const row = this.banks.find((b) => b.id === id && b.userId === userId);
    if (!row) throw new NotFoundException();
    row.status = "revoked";
    return { ok: true };
  }

  listOffers(filters: {
    asset?: string;
    side?: string;
    method?: string;
    fiat?: string;
    min_amount?: number;
    max_amount?: number;
  }) {
    return this.offers
      .filter((o) => o.status === "active")
      .filter((o) => (filters.asset ? o.asset === filters.asset.toUpperCase() : true))
      .filter((o) => (filters.fiat ? o.fiatCurrency === filters.fiat.toUpperCase() : true))
      .filter((o) => (filters.side ? o.side === filters.side : true))
      .filter((o) =>
        filters.method ? o.paymentMethods.includes(filters.method.toLowerCase()) : true,
      )
      .filter((o) =>
        filters.min_amount !== undefined
          ? o.maxAmount === null || o.maxAmount >= filters.min_amount
          : true,
      )
      .filter((o) =>
        filters.max_amount !== undefined
          ? o.minAmount === null || o.minAmount <= filters.max_amount
          : true,
      )
      .map((o) => ({
        id: o.id,
        merchant: o.merchantName,
        merchantId: o.merchantId,
        asset: o.asset,
        fiat: o.fiatCurrency,
        price: o.price,
        min: o.minAmount,
        max: o.maxAmount,
        methods: o.paymentMethods,
        side: o.side,
      }));
  }

  findOffer(offerId: string) {
    return this.offers.find((o) => o.id === offerId);
  }

  listMyOffers(userId: string) {
    return this.offers
      .filter((o) => o.merchantId === userId && (o.status === "active" || o.status === "paused"))
      .map((o) => ({
        id: o.id,
        merchant: o.merchantName,
        merchantId: o.merchantId,
        asset: o.asset,
        fiat: o.fiatCurrency,
        price: o.price,
        min: o.minAmount,
        max: o.maxAmount,
        methods: o.paymentMethods,
        side: o.side,
        status: o.status,
      }));
  }

  createOffer(
    userId: string,
    fields: {
      fiat: string;
      methods: string[];
      side: string;
      minAmount?: number;
      maxAmount?: number;
    },
    asset: string,
    price: number,
  ) {
    const offer: DemoOffer = {
      id: this.uid(),
      merchantId: userId,
      merchantName: this.getUser(userId).name ?? "Merchant",
      asset: asset.toUpperCase(),
      fiatCurrency: fields.fiat,
      price,
      minAmount: fields.minAmount ?? null,
      maxAmount: fields.maxAmount ?? null,
      paymentMethods: fields.methods,
      side: fields.side,
      status: "active",
    };
    this.offers.push(offer);
    return offer;
  }

  updateOffer(
    userId: string,
    offerId: string,
    dto: {
      price?: number;
      min_amount?: number;
      minAmount?: number;
      max_amount?: number;
      maxAmount?: number;
      payment_methods?: string[];
      paymentMethods?: string[];
    },
  ) {
    const offer = this.offers.find(
      (o) => o.id === offerId && o.merchantId === userId && o.status === "active",
    );
    if (!offer) throw new NotFoundException("Active offer not found");
    if (typeof dto.price === "number") offer.price = dto.price;
    const min = dto.min_amount ?? dto.minAmount;
    const max = dto.max_amount ?? dto.maxAmount;
    if (min !== undefined) offer.minAmount = min;
    if (max !== undefined) offer.maxAmount = max;
    const methods = dto.payment_methods ?? dto.paymentMethods;
    if (methods) offer.paymentMethods = methods.map((m) => m.toLowerCase());
    return offer;
  }

  deactivateOffer(userId: string, offerId: string) {
    const offer = this.offers.find((o) => o.id === offerId && o.merchantId === userId);
    if (!offer) throw new NotFoundException("Offer not found");
    const hasActiveOrder = [...this.orders.values()].some(
      (o) => o.offerId === offerId && o.status !== "completed" && o.status !== "cancelled",
    );
    if (hasActiveOrder) {
      throw new ForbiddenException("Cannot delete offer with active orders");
    }
    offer.status = "deleted";
    return { ok: true };
  }

  createOrder(buyerId: string, offerId: string, amount: number, paymentMethod?: string) {
    const offer = this.offers.find((o) => o.id === offerId && o.status === "active");
    if (!offer) throw new NotFoundException("Offer not found");

    const isMerchantSelling = offer.side === "sell";
    const orderBuyerId = isMerchantSelling ? buyerId : offer.merchantId;
    const orderSellerId = isMerchantSelling ? offer.merchantId : buyerId;

    if (orderBuyerId === orderSellerId) {
      throw new ForbiddenException("Cannot open order on your own offer");
    }
    if (offer.minAmount && amount < offer.minAmount) {
      throw new ForbiddenException("Amount below minimum");
    }
    if (offer.maxAmount && amount > offer.maxAmount) {
      throw new ForbiddenException("Amount above maximum");
    }

    const order: DemoOrder = {
      id: this.uid(),
      offerId,
      buyerId: orderBuyerId,
      sellerId: orderSellerId,
      amount,
      paymentMethod: paymentMethod?.toLowerCase() ?? null,
      status: "created",
      cryptoTxHash: null,
      fiatPaymentReference: null,
      disputeReason: null,
      createdAt: new Date(),
      updatedAt: new Date(),
    };
    this.orders.set(order.id, order);
    offer.status = "paused";
    return order;
  }

  getOrder(userId: string, orderId: string) {
    const order = this.orders.get(orderId);
    if (!order) throw new NotFoundException("Order not found");
    if (order.buyerId !== userId && order.sellerId !== userId) {
      throw new ForbiddenException();
    }
    return order;
  }

  listOrders(userId: string, role?: "buyer" | "seller") {
    const all = [...this.orders.values()].filter(
      (o) => o.buyerId === userId || o.sellerId === userId,
    );
    if (role === "buyer") return all.filter((o) => o.buyerId === userId);
    if (role === "seller") return all.filter((o) => o.sellerId === userId);
    return all;
  }

  transitionOrder(
    userId: string,
    orderId: string,
    targetStatus: string,
    data: { cryptoTxHash?: string; fiatPaymentReference?: string; disputeReason?: string },
    role: "buyer" | "seller" | "any",
  ) {
    const order = this.getOrder(userId, orderId);

    if (targetStatus === "cancelled" && !P2P_CANCELLABLE_STATUSES.has(order.status)) {
      throw new ForbiddenException(
        "Order can only be cancelled from created or awaiting_fiat_payment",
      );
    }

    if (role === "buyer" && order.buyerId !== userId) {
      throw new ForbiddenException("Buyer action required");
    }
    if (role === "seller" && order.sellerId !== userId) {
      throw new ForbiddenException("Seller action required");
    }

    const allowed = P2P_ORDER_TRANSITIONS[order.status] ?? [];
    if (!allowed.includes(targetStatus)) {
      throw new ForbiddenException(`Cannot transition from ${order.status} to ${targetStatus}`);
    }

    if (targetStatus === "completed" && order.status === "disputed") {
      if (!order.cryptoTxHash || !order.fiatPaymentReference) {
        throw new ForbiddenException("Disputed order missing proofs");
      }
    }

    order.status = targetStatus;
    if (data.cryptoTxHash) order.cryptoTxHash = data.cryptoTxHash;
    if (data.fiatPaymentReference) order.fiatPaymentReference = data.fiatPaymentReference;
    if (data.disputeReason) order.disputeReason = data.disputeReason;
    order.updatedAt = new Date();

    if (targetStatus === "completed" || targetStatus === "cancelled") {
      const offer = this.offers.find((o) => o.id === order.offerId);
      if (offer) offer.status = "active";
    }

    return order;
  }

  marketRates() {
    return Object.entries(this.liveRates).map(([symbol, r]) => ({
      symbol,
      price: r.price,
      change24h: r.change24h,
    }));
  }

  getRate(base: string, quote: string) {
    const baseUp = base.toUpperCase();
    const quoteUp = quote.toUpperCase();
    if (quoteUp === "UAH") {
      const usd = this.liveRates[baseUp]?.price ?? 1;
      return { base: baseUp, quote: quoteUp, rate: usd * 41.5, at: Date.now() };
    }
    if (baseUp === "USD" && quoteUp === "UAH") {
      return { base: baseUp, quote: quoteUp, rate: 41.5, at: Date.now() };
    }
    const row = this.liveRates[baseUp];
    return {
      base: baseUp,
      quote: quoteUp,
      rate: row?.price ?? 1,
      at: Date.now(),
    };
  }

  marketChart(symbol: string, days: number) {
    const base = this.liveRates[symbol]?.price ?? 100;
    const points = Math.min(Math.max(days, 1), 30) * 4;
    const prices: number[] = [];
    let v = base * 0.92;
    for (let i = 0; i < points; i++) {
      v += (Math.random() - 0.45) * base * 0.02;
      prices.push(Number(v.toFixed(2)));
    }
    prices.push(base);
    return { symbol, days, prices };
  }

  findUserByTelegramId(telegramId: bigint) {
    const userId = this.usersByTelegram.get(String(telegramId));
    return userId ? (this.users.get(userId) ?? null) : null;
  }

  listUserOrders(userId: string, limit = 5) {
    return [...this.orders.values()]
      .filter((o) => o.buyerId === userId || o.sellerId === userId)
      .sort((a, b) => b.updatedAt.getTime() - a.updatedAt.getTime())
      .slice(0, limit)
      .map((o) => {
        const offer = this.offers.find((x) => x.id === o.offerId);
        return { ...o, offer: { asset: offer?.asset ?? "?" } };
      });
  }

  getFirstWallet(userId: string) {
    return (
      this.wallets
        .filter((w) => w.userId === userId)
        .sort((a, b) => a.linkedAt.getTime() - b.linkedAt.getTime())[0] ?? null
    );
  }

  setTelegramNotifications(userId: string, enabled: boolean) {
    const user = this.getUser(userId);
    user.telegramNotifications = enabled;
    return user;
  }

  kycStatus(userId: string) {
    const r = this.kyc.get(userId);
    return {
      status: r?.status ?? "none",
      level: r?.level ?? "basic",
      provider: r?.provider ?? null,
    };
  }

  kycStart(userId: string) {
    this.kyc.set(userId, { status: "pending", level: "basic", provider: "demo" });
    return {
      status: "pending",
      providerReference: "demo-ref",
      verificationUrl: "/legal/privacy",
    };
  }

  kycWebhook(userId: string, status: string) {
    this.kyc.set(userId, { status, level: "basic", provider: "demo" });
    return { ok: true };
  }
}
