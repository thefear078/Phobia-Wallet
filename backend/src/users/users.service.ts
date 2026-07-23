import { Injectable, NotFoundException } from "@nestjs/common";
import { PrismaService } from "../prisma/prisma.service";
import { UpdateUserDto, mapUpdateUserDto } from "./dto/users.dto";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { isUserDeleted, toPublicProfile } from "./user-profile.util";
import { randomUUID } from "crypto";

@Injectable()
export class UsersService {
  constructor(
    private prisma: PrismaService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  async getMe(userId: string) {
    if (this.demoMode.isActive()) {
      const user = this.demoStore.getUser(userId);
      const kyc = this.demoStore.kycStatus(userId);
      return toPublicProfile(user, kyc.status);
    }

    const user = await this.prisma.user.findUnique({
      where: { id: userId },
      include: { kycRecord: true },
    });
    if (!user || isUserDeleted(user)) throw new NotFoundException();

    return toPublicProfile(user, user.kycRecord?.status ?? "none");
  }

  async update(userId: string, dto: UpdateUserDto) {
    const data = mapUpdateUserDto(dto);

    if (this.demoMode.isActive()) {
      this.demoStore.updateUser(userId, data);
      return this.getMe(userId);
    }

    const existing = await this.prisma.user.findUnique({ where: { id: userId } });
    if (!existing || isUserDeleted(existing)) throw new NotFoundException();

    await this.prisma.user.update({
      where: { id: userId },
      data,
    });
    return this.getMe(userId);
  }

  async deleteAccount(userId: string) {
    if (this.demoMode.isActive()) {
      this.demoStore.deleteUser(userId);
      return { ok: true };
    }

    const user = await this.prisma.user.findUnique({ where: { id: userId } });
    if (!user || isUserDeleted(user)) throw new NotFoundException();

    const anonymizedEmail = `deleted-${randomUUID()}@anonymized.local`;

    await this.prisma.$transaction([
      this.prisma.refreshToken.updateMany({
        where: { userId, revokedAt: null },
        data: { revokedAt: new Date() },
      }),
      this.prisma.user.update({
        where: { id: userId },
        data: {
          deletedAt: new Date(),
          email: anonymizedEmail,
          username: null,
          passwordHash: null,
          name: null,
          telegramId: null,
          telegramUsername: null,
          telegramNotifications: false,
          oauthProvider: null,
          oauthSub: null,
          emailVerified: false,
          tfaEnabled: false,
          pushEnabled: false,
          emailAlerts: false,
          priceAlerts: false,
        },
      }),
    ]);

    return { ok: true };
  }
}
