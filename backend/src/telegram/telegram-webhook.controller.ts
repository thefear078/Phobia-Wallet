import { Body, Controller, Headers, Logger, Post, UnauthorizedException } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import type { Update } from "grammy/types";
import { requireWebhookSecret } from "../common/env.validation";
import { TelegramBotService } from "./telegram-bot.service";

@Controller("telegram")
export class TelegramWebhookController {
  private readonly logger = new Logger(TelegramWebhookController.name);

  constructor(
    private config: ConfigService,
    private telegramBot: TelegramBotService,
  ) {}

  @Post("webhook")
  async webhook(
    @Body() update: Update,
    @Headers("x-telegram-bot-api-secret-token") secretHeader?: string,
  ) {
    const configuredSecret = requireWebhookSecret(this.config, "TELEGRAM_WEBHOOK_SECRET");
    if (configuredSecret) {
      if (secretHeader !== configuredSecret) {
        throw new UnauthorizedException("Invalid Telegram webhook secret");
      }
    } else if (this.config.get<string>("NODE_ENV") === "production") {
      throw new UnauthorizedException("Telegram webhook secret required in production");
    }
    try {
      await this.telegramBot.handleUpdate(update);
    } catch (error) {
      this.logger.error(`Webhook handleUpdate failed: ${String(error)}`);
    }
    return { ok: true };
  }
}
