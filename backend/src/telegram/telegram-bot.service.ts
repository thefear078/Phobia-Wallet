import { existsSync } from "node:fs";
import {
  forwardRef,
  HttpException,
  Inject,
  Injectable,
  Logger,
  OnModuleDestroy,
  OnModuleInit,
} from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { Bot, Context, InlineKeyboard, InputFile } from "grammy";
import type { Update } from "grammy/types";
import { PrismaService } from "../prisma/prisma.service";
import { LinkedWalletsService } from "../linked-wallets/linked-wallets.service";
import { RatesService } from "../rates/rates.service";
import { P2pService } from "../p2p/p2p.service";
import type { CreateOrderDto } from "../p2p/dto/p2p.dto";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { stickerPath, type StickerName } from "./telegram-stickers";
import { BOT_COMMANDS, P2P_STATUS_LABELS } from "./telegram.constants";
import { CommandRateLimiter, safeReply, withTelegramRetry } from "./telegram-bot-reliability";

export type TelegramBotMode = "disabled" | "webhook" | "polling" | "mock";

type TelegramUser = {
  id: number;
  username?: string;
  first_name?: string;
  last_name?: string;
};

const SENSITIVE_TEXT_RE = /\b(seed|mnemonic|private\s*key|secret\s*phrase|recovery\s*phrase)\b/i;

const PENDING_ORDER_TTL_MS = 5 * 60 * 1000;

type PendingOrder = {
  offerId: string;
  asset: string;
  fiat: string;
  price: number;
  side: string;
  min: number | null;
  max: number | null;
  expiresAt: number;
};

@Injectable()
export class TelegramBotService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(TelegramBotService.name);
  private bot: Bot<Context> | null = null;
  /** telegramId → offer awaiting an amount reply (short-lived, single process). */
  private pendingOrders = new Map<number, PendingOrder>();
  private runningMode: TelegramBotMode = "disabled";
  private botUsername: string | null = null;
  private startedAt: string | null = null;
  private cleanupTimer: ReturnType<typeof setInterval> | null = null;
  private readonly rateLimiter = new CommandRateLimiter(12, 60_000);

  constructor(
    private config: ConfigService,
    private prisma: PrismaService,
    private wallets: LinkedWalletsService,
    private rates: RatesService,
    @Inject(forwardRef(() => P2pService))
    private p2p: P2pService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  async onModuleInit() {
    const token = this.config.get<string>("TELEGRAM_BOT_TOKEN");
    if (!token) {
      if (this.demoMode.isActive()) {
        this.runningMode = "mock";
        this.startedAt = new Date().toISOString();
      }
      this.logger.warn(
        this.demoMode.isActive()
          ? "TELEGRAM_BOT_TOKEN missing — Telegram bot in MOCK mode (console logging)"
          : "TELEGRAM_BOT_TOKEN is missing, Telegram bot is disabled",
      );
      return;
    }

    this.bot = new Bot(token);
    this.registerHandlers(this.bot);

    try {
      const me = await this.bot.api.getMe();
      this.botUsername = me.username ?? null;
      this.startedAt = new Date().toISOString();
      this.logger.log(`Telegram bot @${this.botUsername ?? "?"} ready`);
    } catch (error) {
      this.logger.warn(`getMe failed: ${String(error)}`);
    }

    await this.registerBotMenu();
    this.cleanupTimer = setInterval(() => {
      this.prunePendingOrders();
      this.rateLimiter.prune();
    }, 60_000);

    const webhookUrl = this.config.get<string>("TELEGRAM_WEBHOOK_URL")?.trim();
    const forcePolling = this.config.get<string>("TELEGRAM_USE_POLLING") === "true";

    if (webhookUrl && !forcePolling) {
      const webhookSecret = this.config.get<string>("TELEGRAM_WEBHOOK_SECRET");
      await this.bot.api.setWebhook(webhookUrl, {
        secret_token: webhookSecret || undefined,
        drop_pending_updates: true,
      });
      this.runningMode = "webhook";
      this.logger.log(`Telegram webhook configured: ${webhookUrl}`);
      return;
    }

    // Clear stale webhook — otherwise getUpdates receives nothing (common local-dev issue).
    await this.bot.api.deleteWebhook({ drop_pending_updates: true });

    this.runningMode = "polling";
    void this.bot
      .start({ drop_pending_updates: true })
      .then(() => {
        this.logger.log("Telegram bot started in long-polling mode");
      })
      .catch((error) => {
        this.runningMode = "disabled";
        this.logger.error(`Telegram bot failed to start: ${String(error)}`);
      });
  }

  async onModuleDestroy() {
    if (this.cleanupTimer) {
      clearInterval(this.cleanupTimer);
      this.cleanupTimer = null;
    }
    if (this.bot) {
      await this.bot.stop();
    }
  }

  getBotStatus() {
    return {
      running: this.runningMode !== "disabled",
      mode: this.runningMode,
      username: this.botUsername,
      startedAt: this.startedAt,
      pendingOrders: this.pendingOrders.size,
    };
  }

  async handleUpdate(update: Update) {
    if (!this.bot) {
      if (this.demoMode.isActive()) {
        const text =
          update.message?.text ??
          update.callback_query?.data ??
          JSON.stringify(update).slice(0, 200);
        this.logger.log(`[Telegram MOCK] ${text}`);
      }
      return;
    }
    try {
      await this.bot.handleUpdate(update);
    } catch (error) {
      this.logger.error(`handleUpdate failed: ${String(error)}`);
    }
  }

  async sendTextByTelegramId(telegramId: bigint, text: string) {
    if (!this.bot) return;
    try {
      await withTelegramRetry(() => this.bot!.api.sendMessage(telegramId.toString(), text));
    } catch (error) {
      this.logger.warn(`Failed sending Telegram notification: ${String(error)}`);
    }
  }

  private async registerBotMenu() {
    if (!this.bot) return;
    try {
      await withTelegramRetry(() => this.bot!.api.setMyCommands(BOT_COMMANDS));
    } catch (error) {
      this.logger.warn(`setMyCommands failed: ${String(error)}`);
    }
  }

  private async sendSticker(ctx: Context, name: StickerName) {
    const path = stickerPath(name);
    if (!existsSync(path)) {
      this.logger.warn(`Sticker not found: ${path}`);
      return;
    }
    try {
      await ctx.replyWithSticker(new InputFile(path));
    } catch (error) {
      this.logger.warn(`sendSticker(${name}) failed: ${String(error)}`);
    }
  }

  private registerHandlers(bot: Bot<Context>) {
    bot.on("message:text", async (ctx, next) => {
      const text = ctx.message.text ?? "";
      if (!SENSITIVE_TEXT_RE.test(text)) {
        await next();
        return;
      }

      await this.sendSticker(ctx, "secret");
      try {
        await ctx.api.deleteMessage(ctx.chat.id, ctx.message.message_id);
      } catch {
        /* no delete rights in this chat — warning below still applies */
      }
      await ctx.reply("Message deleted for security. Never send a seed / private key in chat.");
    });

    bot.command("start", async (ctx) => {
      try {
        const tg = this.extractTelegramUser(ctx.from);
        if (!tg) return;
        await this.ensureTelegramUser(tg);

        await this.sendSticker(ctx, "welcome");

        const name = tg.first_name || tg.username || "friend";
        await ctx.reply(
          `Hi, ${name}! 👋\n\n` +
            "Umbrella Wallet is a non-custodial aggregator: your keys stay in your wallet, and we help with balances, rates, and P2P.\n\n" +
            "Tap the button below to open the Mini App.\n\n" +
            "💎 Built by Granite Consulting",
          { reply_markup: this.webAppKeyboard("🌑 Open Umbrella Wallet") },
        );
      } catch (error) {
        this.logger.error(`/start failed: ${String(error)}`);
        await ctx.reply("Hi! 👋 Open Umbrella Wallet:", {
          reply_markup: this.webAppKeyboard("🌑 Open Umbrella Wallet"),
        });
      }
    });

    bot.command("help", async (ctx) => {
      await this.sendSticker(ctx, "thinking");
      await ctx.reply(
        "Umbrella Wallet commands:\n" +
          "/start — welcome and Mini App\n" +
          "/balance — balances\n" +
          "/rates BTC UAH — rate\n" +
          "/receive — receive address\n" +
          "/orders — your P2P orders (with cancel)\n" +
          "/p2p — market: pick an offer and create an order in the bot\n" +
          "/link — link a wallet\n" +
          "/notifications on|off — notifications\n" +
          "/ping — connection check\n" +
          "/support — help\n\n" +
          "Granite Consulting · Umbrella Wallet",
        { reply_markup: this.webAppKeyboard("Open Umbrella") },
      );
    });

    bot.command("ping", async (ctx) => {
      const started = Date.now();
      const msg = await ctx.reply("🏓 pong");
      const ms = Date.now() - started;
      const text = `🏓 pong · ${ms} ms · ${this.runningMode}`;
      try {
        await ctx.api.editMessageText(ctx.chat!.id, msg.message_id, text);
      } catch {
        await ctx.reply(text);
      }
    });

    bot.command("balance", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      const user = await this.getLinkedUser(ctx.from);
      if (!user) return this.askToLink(ctx);

      const balances = await this.wallets.balances(user.id);
      if (!balances.length) {
        await ctx.reply("No linked wallets. Tap the button below to connect one.", {
          reply_markup: this.webAppKeyboard("Open Umbrella"),
        });
        return;
      }

      const lines = balances
        .slice(0, 6)
        .map(
          (w) => `• ${w.chain}: ${w.balance.native}${w.balance.usd ? ` (~$${w.balance.usd})` : ""}`,
        );
      await ctx.reply(`Your balances:\n${lines.join("\n")}`);
    });

    bot.command("rates", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      await this.sendSticker(ctx, "thinking");
      const text = ctx.message?.text ?? "";
      const parts = text.split(/\s+/);
      const symbol = parts[1]?.trim()?.toUpperCase() || "BTC";
      const quote = parts[2]?.trim()?.toUpperCase() || "USDT";
      const rate = await this.rates.getRate(symbol, quote);
      await ctx.reply(`${rate.base}/${rate.quote}: ${rate.rate}`);
    });

    bot.command("orders", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      const user = await this.getLinkedUser(ctx.from);
      if (!user) return this.askToLink(ctx);

      const orders = (await this.p2p.listOrders(user.id)).slice(0, 5);

      if (!orders.length) {
        await ctx.reply("You have no P2P orders yet. Type /p2p to browse offers.");
        return;
      }

      const lines = orders.map((o) => {
        const asset = o.offer?.asset ?? "?";
        const status = P2P_STATUS_LABELS[o.status] ?? o.status;
        return `• #${o.id.slice(0, 8)} ${asset} ${o.amount} — ${status}`;
      });

      const kb = new InlineKeyboard();
      for (const o of orders) {
        if (o.status === "created" || o.status === "awaiting_fiat_payment") {
          kb.text(`Cancel #${o.id.slice(0, 8)}`, `p2p_cancel:${o.id}`).row();
        }
      }
      kb.webApp("Details in Mini App", this.webAppUrl());

      await ctx.reply(`Recent P2P orders:\n${lines.join("\n")}`, { reply_markup: kb });
    });

    bot.command("receive", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      await this.sendSticker(ctx, "sending");
      const user = await this.getLinkedUser(ctx.from);
      if (!user) return this.askToLink(ctx);

      const wallet = this.demoMode.isActive()
        ? this.demoStore.getFirstWallet(user.id)
        : await this.prisma.linkedWallet.findFirst({
            where: { userId: user.id },
            orderBy: { linkedAt: "asc" },
          });
      if (!wallet) {
        await ctx.reply("No linked wallet. Open the Mini App to add an address.", {
          reply_markup: this.webAppKeyboard("Open Umbrella"),
        });
        return;
      }

      await ctx.reply(
        `Receive address (${wallet.chain}):\n\`${wallet.address}\`\n\nQR code is available in Mini App → Deposit.`,
        { parse_mode: "Markdown" },
      );
    });

    const openMiniApp = async (ctx: Context) => {
      await ctx.reply("Open Umbrella Mini App:", {
        reply_markup: this.webAppKeyboard("Open Umbrella"),
      });
    };
    bot.command("link", openMiniApp);
    bot.command("wallet", openMiniApp);
    bot.command("send", openMiniApp);

    bot.command("p2p", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      const offers = (await this.p2p.listOffers({})).slice(0, 6);

      if (!offers.length) {
        await ctx.reply("P2P market is empty. Open the Mini App.", {
          reply_markup: this.webAppKeyboard("Open P2P"),
        });
        return;
      }

      const lines = offers.map((o, i) => {
        const sideLabel = o.side === "sell" ? "Sell" : "Buy";
        const limits = o.min != null || o.max != null ? ` [${o.min ?? "…"}–${o.max ?? "…"}]` : "";
        return `${i + 1}. ${sideLabel} ${o.asset} @ ${o.price} ${o.fiat}${limits} (${o.merchant})`;
      });

      const kb = new InlineKeyboard();
      offers.forEach((o, i) => {
        const action = o.side === "sell" ? "Buy" : "Sell";
        kb.text(`${i + 1} · ${action} ${o.asset}`, `p2p_offer:${o.id}`);
        if (i % 2 === 1) kb.row();
      });
      kb.row().webApp("Open P2P in Mini App", this.webAppUrl());

      await ctx.reply(
        `Umbrella P2P market:\n${lines.join("\n")}\n\nTap a button to create an order right here.`,
        { reply_markup: kb },
      );
    });

    bot.callbackQuery(/^p2p_offer:(.+)$/, async (ctx) => {
      const tg = this.extractTelegramUser(ctx.from);
      const user = await this.getLinkedUser(ctx.from);
      if (!tg || !user) {
        await ctx.answerCallbackQuery({ text: "Please tap /start first" });
        return;
      }

      const offerId = ctx.match[1];
      const offers = await this.p2p.listOffers({});
      const offer = offers.find((o) => o.id === offerId);
      if (!offer) {
        await ctx.answerCallbackQuery({ text: "Offer is no longer available" });
        return;
      }

      this.pendingOrders.set(tg.id, {
        offerId: offer.id,
        asset: offer.asset,
        fiat: offer.fiat,
        price: offer.price,
        side: offer.side,
        min: offer.min,
        max: offer.max,
        expiresAt: Date.now() + PENDING_ORDER_TTL_MS,
      });

      await ctx.answerCallbackQuery();
      const action = offer.side === "sell" ? "buying" : "selling";
      const limits =
        offer.min != null || offer.max != null
          ? ` (from ${offer.min ?? "—"} to ${offer.max ?? "—"})`
          : "";
      await ctx.reply(
        `You are ${action} ${offer.asset} at ${offer.price} ${offer.fiat}.\n` +
          `Enter the amount in ${offer.asset}${limits} in a single message.\n\n` +
          'To cancel, type "cancel".',
      );
    });

    bot.callbackQuery(/^p2p_cancel:(.+)$/, async (ctx) => {
      const user = await this.getLinkedUser(ctx.from);
      if (!user) {
        await ctx.answerCallbackQuery({ text: "Please tap /start first" });
        return;
      }
      try {
        const order = await this.p2p.cancelOrder(user.id, ctx.match[1]);
        await ctx.answerCallbackQuery({ text: "Order cancelled" });
        await ctx.reply(`Order #${order.id.slice(0, 8)} cancelled.`);
      } catch (error) {
        await ctx.answerCallbackQuery({
          text: error instanceof HttpException ? error.message : "Could not cancel",
          show_alert: true,
        });
      }
    });

    bot.command("support", async (ctx) => {
      await ctx.reply(
        "Umbrella support:\n• /help — commands\n• Mini App — account settings\n• Granite Consulting — product development",
        {
          reply_markup: this.webAppKeyboard("Open Umbrella"),
        },
      );
    });

    bot.command("notifications", async (ctx) => {
      if (!(await this.guardRateLimit(ctx))) return;
      const user = await this.getLinkedUser(ctx.from);
      if (!user) return this.askToLink(ctx);
      const text = ctx.message?.text ?? "";
      const arg = text.split(" ")[1]?.trim().toLowerCase();
      const nextValue = arg === "on" ? true : arg === "off" ? false : !user.telegramNotifications;
      if (this.demoMode.isActive()) {
        const updated = this.demoStore.setTelegramNotifications(user.id, nextValue);
        await ctx.reply(`Telegram notifications: ${updated.telegramNotifications ? "on" : "off"}`);
        return;
      }
      const updated = await this.prisma.user.update({
        where: { id: user.id },
        data: { telegramNotifications: nextValue },
      });
      await ctx.reply(`Telegram notifications: ${updated.telegramNotifications ? "on" : "off"}`);
    });

    bot.on("message:text", async (ctx) => {
      const text = ctx.message.text ?? "";
      if (text.startsWith("/")) return;

      const tg = this.extractTelegramUser(ctx.from);
      const pending = tg ? this.pendingOrders.get(tg.id) : undefined;

      if (tg && pending) {
        if (pending.expiresAt < Date.now()) {
          this.pendingOrders.delete(tg.id);
        } else if (/^(скасувати|отмена|cancel)$/i.test(text.trim())) {
          this.pendingOrders.delete(tg.id);
          await ctx.reply("Order creation cancelled.");
          return;
        } else {
          const amount = Number(text.trim().replace(",", "."));
          if (!Number.isFinite(amount) || amount <= 0) {
            await ctx.reply('Enter the amount as a number, e.g. 0.05, or type "cancel".');
            return;
          }

          const user = await this.getLinkedUser(ctx.from);
          if (!user) {
            this.pendingOrders.delete(tg.id);
            return this.askToLink(ctx);
          }

          try {
            const order = await this.p2p.createOrder(user.id, {
              offerId: pending.offerId,
              amount,
            } as CreateOrderDto);
            this.pendingOrders.delete(tg.id);

            const total = (amount * pending.price).toFixed(2);
            const kb = new InlineKeyboard()
              .text("Cancel order", `p2p_cancel:${order.id}`)
              .row()
              .webApp("Continue in Mini App", this.webAppUrl());

            await ctx.reply(
              `Order created ✅\n` +
                `#${order.id.slice(0, 8)} · ${amount} ${pending.asset} @ ${pending.price} ${pending.fiat}\n` +
                `Total: ≈ ${total} ${pending.fiat}\n` +
                `Status: ${P2P_STATUS_LABELS[order.status] ?? order.status}\n\n` +
                "Payment and confirmation — in the Mini App.",
              { reply_markup: kb },
            );
          } catch (error) {
            const msg = error instanceof HttpException ? error.message : "Could not create order";
            await ctx.reply(`Error: ${msg}\nTry a different amount or type "cancel".`);
          }
          return;
        }
      }

      await ctx.reply("Type /help or open the Mini App 👇", {
        reply_markup: this.webAppKeyboard("Open Umbrella"),
      });
    });

    bot.catch((error) => {
      this.logger.error(`Telegram bot unhandled error: ${String(error)}`);
    });
  }

  private prunePendingOrders() {
    const now = Date.now();
    for (const [id, pending] of this.pendingOrders) {
      if (pending.expiresAt < now) this.pendingOrders.delete(id);
    }
  }

  private async guardRateLimit(ctx: Context): Promise<boolean> {
    const tg = this.extractTelegramUser(ctx.from);
    if (!tg) return true;
    if (!this.rateLimiter.tryConsume(tg.id)) {
      await safeReply(ctx, "Too many requests. Try again in a minute.");
      return false;
    }
    return true;
  }

  private extractTelegramUser(from: TelegramUser | undefined | null): TelegramUser | null {
    if (!from?.id) return null;
    return from;
  }

  private async getLinkedUser(from: TelegramUser | undefined | null) {
    const tg = this.extractTelegramUser(from);
    if (!tg) return null;
    if (this.demoMode.isActive()) {
      return this.demoStore.findUserByTelegramId(BigInt(tg.id));
    }
    return this.prisma.user.findUnique({
      where: { telegramId: BigInt(tg.id) },
    });
  }

  private async ensureTelegramUser(tg: TelegramUser) {
    if (this.demoMode.isActive()) {
      return this.demoStore.telegramLogin(tg);
    }
    const existing = await this.prisma.user.findUnique({
      where: { telegramId: BigInt(tg.id) },
    });
    if (existing) return existing;

    const fullName = [tg.first_name, tg.last_name].filter(Boolean).join(" ").trim();
    const email = `telegram_${tg.id}@umbra.local`;
    return this.prisma.user.create({
      data: {
        email,
        emailVerified: true,
        name: fullName || tg.username || "Telegram User",
        telegramId: BigInt(tg.id),
        telegramUsername: tg.username ?? null,
      },
    });
  }

  private async askToLink(ctx: Context) {
    await ctx.reply("Please sign in via Mini App first — tap /start.", {
      reply_markup: this.webAppKeyboard("Open Umbrella"),
    });
  }

  private webAppUrl() {
    return (
      this.config.get<string>("TELEGRAM_WEBAPP_URL")?.trim() ||
      "https://umbra-wallet-web.vercel.app"
    );
  }

  private webAppKeyboard(label: string) {
    return new InlineKeyboard().webApp(label, this.webAppUrl());
  }
}
