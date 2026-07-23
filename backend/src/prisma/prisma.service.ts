import { Injectable, OnModuleDestroy, OnModuleInit } from "@nestjs/common";
import { PrismaClient } from "@prisma/client";
import { ConfigService } from "@nestjs/config";

@Injectable()
export class PrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  isConnected = false;

  constructor(private config: ConfigService) {
    super();
  }

  async onModuleInit() {
    const url = this.config.get<string>("DATABASE_URL")?.trim();
    if (!url) {
      console.warn("[Prisma] DATABASE_URL missing — skipping connect (demo mode)");
      return;
    }
    if (this.config.get<string>("DEMO_MODE") === "true") {
      console.warn("[Prisma] DEMO_MODE=true — skipping DB connect");
      return;
    }
    // Retry: the DB container often comes up a few seconds after the app in dev/compose.
    const attempts = 5;
    let lastError: unknown;
    for (let i = 1; i <= attempts; i++) {
      try {
        await this.$connect();
        this.isConnected = true;
        if (i > 1) console.warn(`[Prisma] Connected on attempt ${i}`);
        return;
      } catch (error) {
        lastError = error;
        if (i < attempts) {
          console.warn(`[Prisma] Connect attempt ${i}/${attempts} failed, retrying in 3s…`);
          await new Promise((r) => setTimeout(r, 3000));
        }
      }
    }

    const isProd = this.config.get<string>("NODE_ENV") === "production";
    const allowFallback = this.config.get<string>("ALLOW_DEMO_FALLBACK") === "true";
    if (isProd || !allowFallback) {
      // A real app must not silently serve fake data because the DB is down.
      throw lastError;
    }
    console.warn(
      `[Prisma] Connect failed after ${attempts} attempts — ALLOW_DEMO_FALLBACK=true, serving demo store: ${String(lastError)}`,
    );
    this.isConnected = false;
  }

  async onModuleDestroy() {
    if (this.isConnected) await this.$disconnect();
  }
}
