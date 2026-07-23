import { Controller, Get } from "@nestjs/common";
import { SkipThrottle } from "@nestjs/throttler";
import { DemoModeService } from "./demo/demo-mode.service";
import { PrismaService } from "./prisma/prisma.service";
import { TelegramBotService } from "./telegram/telegram-bot.service";

@SkipThrottle()
@Controller()
export class HealthController {
  constructor(
    private demoMode: DemoModeService,
    private prisma: PrismaService,
    private telegramBot: TelegramBotService,
  ) {}

  @Get("health")
  health() {
    return {
      ok: true,
      demo: this.demoMode.isActive(),
      database: this.prisma.isConnected,
      telegram: this.telegramBot.getBotStatus(),
      at: new Date().toISOString(),
    };
  }
}
