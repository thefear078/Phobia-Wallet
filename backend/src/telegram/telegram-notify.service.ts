import { forwardRef, Inject, Injectable, Logger } from "@nestjs/common";
import { PrismaService } from "../prisma/prisma.service";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { P2P_STATUS_LABELS } from "./telegram.constants";
import { TelegramBotService } from "./telegram-bot.service";

@Injectable()
export class TelegramNotifyService {
  private readonly logger = new Logger(TelegramNotifyService.name);

  constructor(
    private prisma: PrismaService,
    @Inject(forwardRef(() => TelegramBotService))
    private telegramBot: TelegramBotService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  async notifyP2pStatusChange(order: {
    id: string;
    status: string;
    buyerId: string;
    sellerId: string;
    amount: number;
  }) {
    const statusLabel = P2P_STATUS_LABELS[order.status] ?? order.status;
    const text =
      `P2P order #${order.id.slice(0, 8)}: ${statusLabel}.\n` +
      `Amount: ${order.amount}\n` +
      "Details — /orders or Mini App.";

    const telegramIds = this.demoMode.isActive()
      ? this.collectDemoTelegramIds(order.buyerId, order.sellerId)
      : await this.collectDbTelegramIds(order.buyerId, order.sellerId);

    if (!telegramIds.length) return;

    await Promise.all(
      telegramIds.map((telegramId) => this.telegramBot.sendTextByTelegramId(telegramId, text)),
    ).catch((error) => {
      this.logger.warn(`Failed to send one or more Telegram notifications: ${String(error)}`);
    });
  }

  private collectDemoTelegramIds(buyerId: string, sellerId: string): bigint[] {
    return [buyerId, sellerId]
      .map((id) => this.demoStore.getUser(id))
      .filter((u) => u?.telegramId && u.telegramNotifications)
      .map((u) => u!.telegramId!);
  }

  private async collectDbTelegramIds(buyerId: string, sellerId: string): Promise<bigint[]> {
    const users = await this.prisma.user.findMany({
      where: {
        id: { in: [buyerId, sellerId] },
        telegramId: { not: null },
        telegramNotifications: true,
      },
      select: { telegramId: true },
    });
    return users.map((u) => u.telegramId).filter((id): id is bigint => id !== null);
  }
}
