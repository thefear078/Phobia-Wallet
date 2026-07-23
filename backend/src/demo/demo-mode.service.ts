import { Injectable, Logger, OnModuleInit } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { PrismaService } from "../prisma/prisma.service";

@Injectable()
export class DemoModeService implements OnModuleInit {
  private readonly logger = new Logger(DemoModeService.name);
  private active = false;

  constructor(
    private config: ConfigService,
    private prisma: PrismaService,
  ) {}

  onModuleInit() {
    const forced = this.config.get<string>("DEMO_MODE") === "true";
    const noDb = !this.config.get<string>("DATABASE_URL")?.trim();
    if (forced || noDb) {
      this.active = true;
      this.logger.warn("Backend DEMO MODE active — in-memory store (no PostgreSQL required)");
    }
  }

  isActive(): boolean {
    return this.active || !this.prisma.isConnected;
  }
}
