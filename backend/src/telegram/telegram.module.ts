import { Module, forwardRef } from "@nestjs/common";
import { AuthModule } from "../auth/auth.module";
import { LinkedWalletsModule } from "../linked-wallets/linked-wallets.module";
import { P2pModule } from "../p2p/p2p.module";
import { RatesModule } from "../rates/rates.module";
import { TelegramAuthController } from "./telegram-auth.controller";
import { TelegramBotService } from "./telegram-bot.service";
import { TelegramNotifyService } from "./telegram-notify.service";
import { TelegramWebhookController } from "./telegram-webhook.controller";

@Module({
  imports: [AuthModule, LinkedWalletsModule, RatesModule, forwardRef(() => P2pModule)],
  providers: [TelegramBotService, TelegramNotifyService],
  controllers: [TelegramWebhookController, TelegramAuthController],
  exports: [TelegramBotService, TelegramNotifyService],
})
export class TelegramModule {}
