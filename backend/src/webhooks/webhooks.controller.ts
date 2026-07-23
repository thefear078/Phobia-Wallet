import {
  Body,
  Controller,
  Headers,
  Post,
  RawBodyRequest,
  Req,
  UnauthorizedException,
} from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import type { Request } from "express";
import { KycService } from "../kyc/kyc.service";
import { requireWebhookSecret } from "../common/env.validation";
import { verifyHmacSha256Hex } from "../common/webhook.util";

@Controller("webhooks")
export class WebhooksController {
  constructor(
    private kyc: KycService,
    private config: ConfigService,
  ) {}

  @Post("kyc")
  async kycWebhook(
    @Headers("x-kyc-signature") signature: string | undefined,
    @Req() req: RawBodyRequest<Request>,
    @Body() body: { applicantId?: string; reviewStatus?: string; externalUserId?: string },
  ) {
    const secret = requireWebhookSecret(this.config, "KYC_WEBHOOK_SECRET");
    if (!secret) {
      if (this.config.get<string>("NODE_ENV") === "production") {
        throw new UnauthorizedException("KYC webhook not configured");
      }
    } else {
      const raw = req.rawBody?.toString("utf8") ?? JSON.stringify(body);
      if (!verifyHmacSha256Hex(raw, secret, signature)) {
        throw new UnauthorizedException("Invalid webhook signature");
      }
    }
    return this.kyc.handleWebhook(body);
  }

  @Post("open-banking")
  openBanking(
    @Headers("x-open-banking-signature") signature: string | undefined,
    @Req() req: RawBodyRequest<Request>,
    @Body() body: { type?: string; accountId?: string; status?: string },
  ) {
    const secret = requireWebhookSecret(this.config, "OPEN_BANKING_WEBHOOK_SECRET");
    if (!secret) {
      if (this.config.get<string>("NODE_ENV") === "production") {
        throw new UnauthorizedException("Open banking webhook not configured");
      }
      return { received: true, type: body.type ?? "unknown" };
    }

    const raw = req.rawBody?.toString("utf8") ?? JSON.stringify(body);
    if (!verifyHmacSha256Hex(raw, secret, signature)) {
      throw new UnauthorizedException("Invalid webhook signature");
    }

    return { received: true, type: body.type ?? "unknown" };
  }
}
