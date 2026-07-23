import { Injectable } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { PrismaService } from "../prisma/prisma.service";
import { generateToken } from "../common/crypto.util";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";

@Injectable()
export class KycService {
  constructor(
    private prisma: PrismaService,
    private config: ConfigService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  async getStatus(userId: string) {
    if (this.demoMode.isActive()) return this.demoStore.kycStatus(userId);
    const record = await this.prisma.kycRecord.findUnique({ where: { userId } });
    return {
      status: record?.status ?? "none",
      level: record?.level ?? "basic",
      provider: record?.provider ?? null,
    };
  }

  async start(userId: string) {
    if (this.demoMode.isActive()) return this.demoStore.kycStart(userId);
    const ref = generateToken(16);
    const record = await this.prisma.kycRecord.upsert({
      where: { userId },
      create: {
        userId,
        status: "pending",
        provider: "sumsub",
        providerReference: ref,
      },
      update: {
        status: "pending",
        providerReference: ref,
      },
    });

    const appToken = this.config.get<string>("SUMSUB_APP_TOKEN");
    const verificationUrl = appToken
      ? `https://cockpit.sumsub.com/checkus/#/applicant/${ref}`
      : `https://kyc.umbra.wallet/verify?ref=${ref}`;

    return {
      status: record.status,
      providerReference: ref,
      verificationUrl,
    };
  }

  async handleWebhook(payload: {
    applicantId?: string;
    reviewStatus?: string;
    externalUserId?: string;
  }) {
    const userId = payload.externalUserId;
    if (!userId) return { ok: false };

    if (this.demoMode.isActive()) {
      const statusMap: Record<string, string> = {
        completed: "approved",
        rejected: "rejected",
        pending: "pending",
        init: "pending",
      };
      const status = statusMap[payload.reviewStatus ?? "pending"] ?? "pending";
      return this.demoStore.kycWebhook(userId, status);
    }

    const statusMap: Record<string, string> = {
      completed: "approved",
      rejected: "rejected",
      pending: "pending",
      init: "pending",
    };

    const status = statusMap[payload.reviewStatus ?? "pending"] ?? "pending";

    await this.prisma.kycRecord.upsert({
      where: { userId },
      create: {
        userId,
        status,
        provider: "sumsub",
        providerReference: payload.applicantId,
      },
      update: {
        status,
        providerReference: payload.applicantId ?? undefined,
      },
    });

    return { ok: true };
  }
}
