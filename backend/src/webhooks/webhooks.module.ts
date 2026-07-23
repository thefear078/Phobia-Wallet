import { Module } from "@nestjs/common";
import { WebhooksController } from "./webhooks.controller";
import { KycModule } from "../kyc/kyc.module";

@Module({
  imports: [KycModule],
  controllers: [WebhooksController],
})
export class WebhooksModule {}
