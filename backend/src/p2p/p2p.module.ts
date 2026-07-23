import { Module, forwardRef } from "@nestjs/common";
import { TelegramModule } from "../telegram/telegram.module";
import { P2pController } from "./p2p.controller";
import { P2pService } from "./p2p.service";
import { P2pOrderEventsService } from "./p2p-order-events.service";

@Module({
  imports: [forwardRef(() => TelegramModule)],
  controllers: [P2pController],
  providers: [P2pService, P2pOrderEventsService],
  exports: [P2pService, P2pOrderEventsService],
})
export class P2pModule {}
