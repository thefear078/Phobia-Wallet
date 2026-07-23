import {
  BadRequestException,
  ConflictException,
  ForbiddenException,
  forwardRef,
  HttpException,
  HttpStatus,
  Inject,
  Injectable,
  NotFoundException,
} from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { Prisma } from "@prisma/client";
import { PrismaService } from "../prisma/prisma.service";
import { RedisService } from "../redis/redis.service";
import { TelegramNotifyService } from "../telegram/telegram-notify.service";
import { verifyEthTxHash } from "../common/blockchain.util";
import {
  CreateOfferDto,
  CreateOrderDto,
  UpdateOfferDto,
  FiatProofDto,
  CryptoProofDto,
  resolveCryptoHash,
  resolveFiatReference,
  resolveOfferFields,
  resolveOrderFields,
} from "./dto/p2p.dto";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { P2pOrderEventsService } from "./p2p-order-events.service";
import {
  P2P_CANCELLABLE_STATUSES,
  P2P_TERMINAL_STATUSES,
  canTransition,
  hasKycLevel,
  isActiveOrderStatus,
} from "./p2p-transitions";

export { P2P_ORDER_TRANSITIONS } from "./p2p-transitions";

const ORDER_RATE_LIMIT_KEY = "p2p:orders:";
const ORDER_RATE_LIMIT_MAX = 10;
const ORDER_RATE_LIMIT_TTL_SEC = 3600;

type OfferFilters = {
  asset?: string;
  method?: string;
  fiat?: string;
  side?: string;
  min_amount?: number;
  max_amount?: number;
};

@Injectable()
export class P2pService {
  constructor(
    private prisma: PrismaService,
    private redis: RedisService,
    @Inject(forwardRef(() => TelegramNotifyService))
    private telegramNotify: TelegramNotifyService,
    private config: ConfigService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
    private orderEvents: P2pOrderEventsService,
  ) {}

  async listOffers(filters: OfferFilters) {
    if (this.demoMode.isActive()) {
      return this.sortOffers(this.demoStore.listOffers(filters), filters.side);
    }

    const where: Prisma.P2pOfferWhereInput = {
      status: "active",
      ...(filters.asset ? { asset: filters.asset.toUpperCase() } : {}),
      ...(filters.fiat ? { fiatCurrency: filters.fiat.toUpperCase() } : {}),
      ...(filters.side ? { side: filters.side } : {}),
      ...(filters.method ? { paymentMethods: { has: filters.method.toLowerCase() } } : {}),
    };

    if (filters.min_amount !== undefined) {
      where.AND = [
        ...(Array.isArray(where.AND) ? where.AND : where.AND ? [where.AND] : []),
        { OR: [{ maxAmount: null }, { maxAmount: { gte: filters.min_amount } }] },
      ];
    }
    if (filters.max_amount !== undefined) {
      where.AND = [
        ...(Array.isArray(where.AND) ? where.AND : where.AND ? [where.AND] : []),
        { OR: [{ minAmount: null }, { minAmount: { lte: filters.max_amount } }] },
      ];
    }

    const offers = await this.prisma.p2pOffer.findMany({
      where,
      include: {
        merchant: { select: { id: true, name: true, email: true } },
      },
    });

    const mapped = offers.map((o) => this.serializeOffer(o));
    return this.sortOffers(mapped, filters.side);
  }

  async listMyOffers(userId: string) {
    if (this.demoMode.isActive()) {
      return this.demoStore.listMyOffers(userId);
    }

    const offers = await this.prisma.p2pOffer.findMany({
      where: { merchantId: userId, status: { in: ["active", "paused"] } },
      orderBy: { createdAt: "desc" },
      include: { merchant: { select: { id: true, name: true, email: true } } },
    });
    return offers.map((o) => ({ ...this.serializeOffer(o), status: o.status }));
  }

  async createOffer(userId: string, dto: CreateOfferDto) {
    let fields: ReturnType<typeof resolveOfferFields>;
    try {
      fields = resolveOfferFields(dto);
    } catch {
      throw new BadRequestException("Invalid offer payload");
    }

    if (this.demoMode.isActive()) {
      if (!hasKycLevel(this.demoStore.kycStatus(userId), 1)) {
        throw new ForbiddenException("KYC level 1+ required to publish P2P offers");
      }
      return this.demoStore.createOffer(userId, fields, dto.asset, dto.price);
    }

    const kyc = await this.prisma.kycRecord.findUnique({ where: { userId } });
    if (!hasKycLevel(kyc, 1)) {
      throw new ForbiddenException("KYC level 1+ required to publish P2P offers");
    }

    return this.prisma.p2pOffer.create({
      data: {
        merchantId: userId,
        asset: dto.asset.toUpperCase(),
        fiatCurrency: fields.fiat,
        quoteKind: fields.quoteKind,
        price: dto.price,
        minAmount: fields.minAmount,
        maxAmount: fields.maxAmount,
        paymentMethods: fields.methods,
        side: fields.side,
        status: "active",
      },
    });
  }

  async updateOffer(userId: string, offerId: string, dto: UpdateOfferDto) {
    if (this.demoMode.isActive()) {
      return this.demoStore.updateOffer(userId, offerId, dto);
    }

    const offer = await this.prisma.p2pOffer.findFirst({
      where: { id: offerId, merchantId: userId, status: "active" },
    });
    if (!offer) throw new NotFoundException("Active offer not found");

    const minAmount = dto.min_amount ?? dto.minAmount;
    const maxAmount = dto.max_amount ?? dto.maxAmount;
    const methods = dto.payment_methods ?? dto.paymentMethods;

    if (minAmount !== undefined && maxAmount !== undefined && minAmount > maxAmount) {
      throw new BadRequestException("min_amount must be <= max_amount");
    }

    return this.prisma.p2pOffer.update({
      where: { id: offerId },
      data: {
        ...(dto.price !== undefined ? { price: dto.price } : {}),
        ...(minAmount !== undefined ? { minAmount } : {}),
        ...(maxAmount !== undefined ? { maxAmount } : {}),
        ...(methods ? { paymentMethods: methods.map((m) => m.toLowerCase()) } : {}),
      },
    });
  }

  async deactivateOffer(userId: string, offerId: string) {
    if (this.demoMode.isActive()) {
      return this.demoStore.deactivateOffer(userId, offerId);
    }

    const offer = await this.prisma.p2pOffer.findFirst({
      where: { id: offerId, merchantId: userId, status: { in: ["active", "paused"] } },
    });
    if (!offer) throw new NotFoundException("Offer not found");

    const activeOrders = await this.prisma.p2pOrder.count({
      where: { offerId, status: { notIn: [...P2P_TERMINAL_STATUSES] } },
    });
    if (activeOrders > 0) {
      throw new ConflictException("Cannot delete offer with active orders");
    }

    await this.prisma.p2pOffer.update({
      where: { id: offerId },
      data: { status: "deleted" },
    });
    return { ok: true };
  }

  async createOrder(userId: string, dto: CreateOrderDto) {
    let offerId: string;
    let paymentMethod: string | undefined;
    try {
      ({ offerId, paymentMethod } = resolveOrderFields(dto));
    } catch {
      throw new BadRequestException("offer_id is required");
    }

    if (this.demoMode.isActive()) {
      return this.serializeOrder(
        this.withDemoOffer(this.demoStore.createOrder(userId, offerId, dto.amount, paymentMethod)),
      );
    }

    await this.assertOrderRateLimit(userId);

    const offer = await this.prisma.p2pOffer.findUnique({ where: { id: offerId } });
    if (!offer || offer.status !== "active") {
      throw new NotFoundException("Offer not found or not available");
    }

    if (paymentMethod && !offer.paymentMethods.includes(paymentMethod.toLowerCase())) {
      throw new BadRequestException("payment_method not supported by this offer");
    }

    const isMerchantSelling = offer.side === "sell";
    const orderBuyerId = isMerchantSelling ? userId : offer.merchantId;
    const orderSellerId = isMerchantSelling ? offer.merchantId : userId;

    if (orderBuyerId === orderSellerId) {
      throw new BadRequestException("Cannot open order on your own offer");
    }

    if (offer.minAmount && dto.amount < Number(offer.minAmount)) {
      throw new BadRequestException("Amount below minimum");
    }
    if (offer.maxAmount && dto.amount > Number(offer.maxAmount)) {
      throw new BadRequestException("Amount above maximum");
    }

    const reserved = Number(offer.reservedAmount);
    const max = offer.maxAmount ? Number(offer.maxAmount) : null;
    if (max !== null && reserved + dto.amount > max) {
      throw new BadRequestException("Offer liquidity exceeded");
    }

    const activeOnOffer = await this.prisma.p2pOrder.count({
      where: { offerId, status: { notIn: [...P2P_TERMINAL_STATUSES] } },
    });
    if (activeOnOffer > 0) {
      throw new ConflictException("Offer already has an active order");
    }

    const order = await this.prisma.$transaction(async (tx) => {
      await tx.p2pOffer.update({
        where: { id: offerId },
        data: {
          reservedAmount: { increment: dto.amount },
          status: "paused",
        },
      });

      return tx.p2pOrder.create({
        data: {
          offerId,
          buyerId: orderBuyerId,
          sellerId: orderSellerId,
          amount: dto.amount,
          paymentMethod: paymentMethod?.toLowerCase() ?? null,
          status: "created",
        },
        include: {
          offer: { include: { merchant: { select: { id: true, name: true, email: true } } } },
        },
      });
    });

    this.publishOrderEvent(order);
    return this.serializeOrder(order);
  }

  async getOrder(userId: string, orderId: string) {
    if (this.demoMode.isActive()) {
      return this.serializeOrder(this.withDemoOffer(this.demoStore.getOrder(userId, orderId)));
    }
    const order = await this.findOrderForParticipant(userId, orderId);
    return this.serializeOrder(order);
  }

  async listOrders(userId: string, role?: "buyer" | "seller") {
    if (this.demoMode.isActive()) {
      return this.demoStore
        .listOrders(userId, role)
        .map((o) => this.serializeOrder(this.withDemoOffer(o)));
    }

    const where =
      role === "buyer"
        ? { buyerId: userId }
        : role === "seller"
          ? { sellerId: userId }
          : { OR: [{ buyerId: userId }, { sellerId: userId }] };

    const orders = await this.prisma.p2pOrder.findMany({
      where,
      orderBy: { updatedAt: "desc" },
      take: 50,
      include: {
        offer: { include: { merchant: { select: { id: true, name: true, email: true } } } },
      },
    });
    return orders.map((o) => this.serializeOrder(o));
  }

  async startFiatPayment(userId: string, orderId: string) {
    return this.transition(userId, orderId, "awaiting_fiat_payment", {}, "buyer");
  }

  async submitFiatProof(userId: string, orderId: string, dto: FiatProofDto) {
    const ref = resolveFiatReference(dto);
    if (!ref) throw new BadRequestException("payment reference is required");

    // Crypto-quote deals: the buyer's "payment" is itself an on-chain transfer,
    // so the proof must be a tx hash (verified on-chain when a key is present).
    if (!this.demoMode.isActive()) {
      const existing = await this.findOrderForParticipant(userId, orderId);
      if (existing.offer?.quoteKind === "crypto") {
        if (!/^0x[a-fA-F0-9]{64}$/.test(ref)) {
          throw new BadRequestException(
            "For crypto-to-crypto deals the buyer payment proof must be an EVM tx hash (0x + 64 hex)",
          );
        }
        const alchemyKey = this.config.get<string>("ALCHEMY_API_KEY");
        if (alchemyKey) {
          const verified = await verifyEthTxHash(ref, alchemyKey);
          if (!verified.ok) {
            throw new BadRequestException(
              "Buyer transaction not found on-chain or not yet confirmed",
            );
          }
        }
      }
    }

    return this.transition(
      userId,
      orderId,
      "fiat_payment_confirmed",
      { fiatPaymentReference: ref },
      "buyer",
    );
  }

  async submitCryptoProof(userId: string, orderId: string, dto: CryptoProofDto) {
    const cryptoTxHash = resolveCryptoHash(dto);
    if (!/^0x[a-fA-F0-9]{64}$/.test(cryptoTxHash)) {
      throw new BadRequestException("Invalid EVM transaction hash (0x + 64 hex)");
    }

    const alchemyKey = this.config.get<string>("ALCHEMY_API_KEY");
    if (alchemyKey) {
      const verified = await verifyEthTxHash(cryptoTxHash, alchemyKey);
      if (!verified.ok) {
        throw new BadRequestException("Transaction not found on-chain or not yet confirmed");
      }
    }

    return this.transition(userId, orderId, "crypto_sent", { cryptoTxHash }, "seller");
  }

  async completeOrder(userId: string, orderId: string) {
    // Only the buyer (who receives the crypto) may confirm completion.
    if (this.demoMode.isActive()) {
      return this.transition(userId, orderId, "completed", {}, "buyer");
    }
    const order = await this.findOrderForParticipant(userId, orderId);
    if (order.status !== "crypto_sent") {
      throw new BadRequestException("Order must be in crypto_sent status");
    }
    if (!order.cryptoTxHash || !order.fiatPaymentReference) {
      throw new BadRequestException("Missing payment proofs");
    }
    return this.transition(userId, orderId, "completed", {}, "buyer");
  }

  async disputeOrder(userId: string, orderId: string, reason: string) {
    if (this.demoMode.isActive()) {
      const order = this.demoStore.transitionOrder(
        userId,
        orderId,
        "disputed",
        { disputeReason: reason },
        "any",
      );
      return this.serializeOrder(this.withDemoOffer(order));
    }

    const order = await this.findOrderForParticipant(userId, orderId);
    if (!isActiveOrderStatus(order.status) || P2P_TERMINAL_STATUSES.has(order.status)) {
      throw new BadRequestException("Cannot dispute a terminal order");
    }

    return this.transition(userId, orderId, "disputed", { disputeReason: reason }, "any");
  }

  async cancelOrder(userId: string, orderId: string) {
    if (this.demoMode.isActive()) {
      const order = this.demoStore.transitionOrder(userId, orderId, "cancelled", {}, "any");
      return this.serializeOrder(this.withDemoOffer(order));
    }

    const order = await this.findOrderForParticipant(userId, orderId);
    if (!P2P_CANCELLABLE_STATUSES.has(order.status)) {
      throw new BadRequestException(
        "Order can only be cancelled from created or awaiting_fiat_payment",
      );
    }

    return this.transition(userId, orderId, "cancelled", {}, "any");
  }

  private async transition(
    userId: string,
    orderId: string,
    targetStatus: string,
    data: {
      cryptoTxHash?: string;
      fiatPaymentReference?: string;
      disputeReason?: string;
    },
    role: "buyer" | "seller" | "any",
  ) {
    if (this.demoMode.isActive()) {
      const order = this.demoStore.transitionOrder(userId, orderId, targetStatus, data, role);
      return this.serializeOrder(this.withDemoOffer(order));
    }

    const order = await this.findOrderForParticipant(userId, orderId);

    if (role === "buyer" && order.buyerId !== userId) {
      throw new ForbiddenException("Buyer action required");
    }
    if (role === "seller" && order.sellerId !== userId) {
      throw new ForbiddenException("Seller action required");
    }

    if (!canTransition(order.status, targetStatus)) {
      throw new BadRequestException(`Cannot transition from ${order.status} to ${targetStatus}`);
    }

    if (targetStatus === "completed" && order.status === "disputed") {
      if (!order.cryptoTxHash || !order.fiatPaymentReference) {
        throw new BadRequestException("Disputed order missing proofs");
      }
    }

    const updated = await this.prisma.p2pOrder.update({
      where: { id: orderId },
      data: {
        status: targetStatus,
        ...(data.cryptoTxHash ? { cryptoTxHash: data.cryptoTxHash } : {}),
        ...(data.fiatPaymentReference ? { fiatPaymentReference: data.fiatPaymentReference } : {}),
        ...(data.disputeReason ? { disputeReason: data.disputeReason } : {}),
        updatedAt: new Date(),
      },
      include: {
        offer: { include: { merchant: { select: { id: true, name: true, email: true } } } },
      },
    });

    if (P2P_TERMINAL_STATUSES.has(targetStatus)) {
      await this.releaseOfferReservation(updated.offerId, Number(updated.amount));
    }

    this.publishOrderEvent(updated);

    void this.telegramNotify.notifyP2pStatusChange({
      id: updated.id,
      status: updated.status,
      buyerId: updated.buyerId,
      sellerId: updated.sellerId,
      amount: Number(updated.amount),
    });

    return this.serializeOrder(updated);
  }

  private async releaseOfferReservation(offerId: string, amount: number) {
    await this.prisma.p2pOffer.update({
      where: { id: offerId },
      data: { reservedAmount: { decrement: amount } },
    });

    const activeCount = await this.prisma.p2pOrder.count({
      where: { offerId, status: { notIn: [...P2P_TERMINAL_STATUSES] } },
    });

    if (activeCount === 0) {
      await this.prisma.p2pOffer.update({
        where: { id: offerId },
        data: { status: "active" },
      });
    }
  }

  private async assertOrderRateLimit(userId: string) {
    const key = `${ORDER_RATE_LIMIT_KEY}${userId}`;
    const hits = await this.redis.client.incr(key);
    if (hits === 1) {
      await this.redis.client.expire(key, ORDER_RATE_LIMIT_TTL_SEC);
    }
    if (hits > ORDER_RATE_LIMIT_MAX) {
      throw new HttpException(
        "P2P order limit exceeded (10 per hour)",
        HttpStatus.TOO_MANY_REQUESTS,
      );
    }
  }

  private publishOrderEvent(order: {
    id: string;
    offerId: string;
    buyerId: string;
    sellerId: string;
    status: string;
    amount: { toString(): string } | number;
  }) {
    this.orderEvents.emitStatusChange({
      orderId: order.id,
      offerId: order.offerId,
      buyerId: order.buyerId,
      sellerId: order.sellerId,
      status: order.status,
      amount: Number(order.amount),
      at: new Date().toISOString(),
    });
  }

  /** Best deal first: merchants buying — highest price; merchants selling — lowest price. */
  private sortOffers<T extends { side: string; price: number }>(offers: T[], sideFilter?: string) {
    return [...offers].sort((a, b) => {
      if (!sideFilter && a.side !== b.side) return a.side.localeCompare(b.side);
      return a.side === "buy" ? b.price - a.price : a.price - b.price;
    });
  }

  private serializeOffer(o: {
    id: string;
    merchantId: string;
    asset: string;
    fiatCurrency: string;
    quoteKind?: string;
    price: { toString(): string } | number;
    minAmount?: { toString(): string } | number | null;
    maxAmount?: { toString(): string } | number | null;
    paymentMethods: string[];
    side: string;
    merchant?: { name: string | null; email: string };
    merchantName?: string;
  }) {
    const merchantName =
      o.merchant?.name ?? o.merchant?.email?.split("@")[0] ?? o.merchantName ?? "Merchant";
    return {
      id: o.id,
      merchant: merchantName,
      merchantId: o.merchantId,
      asset: o.asset,
      fiat: o.fiatCurrency,
      quoteKind: (o.quoteKind ?? "fiat") as "fiat" | "crypto",
      price: Number(o.price),
      min: o.minAmount != null ? Number(o.minAmount) : null,
      max: o.maxAmount != null ? Number(o.maxAmount) : null,
      methods: o.paymentMethods,
      side: o.side,
    };
  }

  private async findOrderForParticipant(userId: string, orderId: string) {
    const order = await this.prisma.p2pOrder.findUnique({
      where: { id: orderId },
      include: {
        offer: { include: { merchant: { select: { id: true, name: true, email: true } } } },
      },
    });
    if (!order) throw new NotFoundException("Order not found");
    if (order.buyerId !== userId && order.sellerId !== userId) {
      throw new ForbiddenException();
    }
    return order;
  }

  /** Attach the (in-memory) offer to a demo order so serializeOrder can embed offer details. */
  private withDemoOffer<T extends { offerId: string }>(order: T) {
    return { ...order, offer: this.demoStore.findOffer(order.offerId) };
  }

  private serializeOrder(order: {
    id: string;
    offerId: string;
    buyerId: string;
    sellerId: string;
    amount: { toString(): string } | number;
    status: string;
    paymentMethod?: string | null;
    cryptoTxHash: string | null;
    fiatPaymentReference: string | null;
    disputeReason?: string | null;
    createdAt: Date;
    updatedAt: Date;
    offer?: Parameters<P2pService["serializeOffer"]>[0] | null;
  }) {
    return {
      id: order.id,
      offerId: order.offerId,
      buyerId: order.buyerId,
      sellerId: order.sellerId,
      amount: Number(order.amount),
      paymentMethod: order.paymentMethod ?? null,
      status: order.status,
      cryptoTxHash: order.cryptoTxHash,
      fiatPaymentReference: order.fiatPaymentReference,
      disputeReason: order.disputeReason ?? null,
      createdAt: order.createdAt,
      updatedAt: order.updatedAt,
      offer: order.offer ? this.serializeOffer(order.offer) : null,
    };
  }
}
