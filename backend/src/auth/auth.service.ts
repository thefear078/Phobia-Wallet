import { ConflictException, Injectable, UnauthorizedException } from "@nestjs/common";
import { JwtService } from "@nestjs/jwt";
import { ConfigService } from "@nestjs/config";
import { User } from "@prisma/client";
import { PrismaService } from "../prisma/prisma.service";
import {
  generateToken,
  hashPassword,
  hashRefreshToken,
  hashToken,
  newTokenId,
  verifyPassword,
  verifyRefreshToken,
} from "../common/crypto.util";
import { MailerService } from "../common/mailer.service";
import {
  verifyTelegramInitData,
  parseTelegramInitDataUnsafe,
  resolveTelegramUser,
} from "../telegram/telegram.util";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService, type DemoUser } from "../demo/demo-store.service";
import { getJwtAccessSecret, getJwtRefreshSecret } from "../common/env.validation";
import { isUserDeleted } from "../users/user-profile.util";
import type { RegisterDto } from "./dto/auth.dto";

export type AuthTokens = {
  accessToken: string;
  refreshToken: string;
  user: {
    id: string;
    email: string;
    username: string | null;
    name: string | null;
    lang: string;
    emailVerified: boolean;
  };
};

type RefreshJwtPayload = { sub: string; jti: string };

function normalizeUsername(raw: string): string {
  return raw.trim().toLowerCase();
}

function normalizeEmail(raw: string): string {
  return raw.trim().toLowerCase();
}

function internalEmailForUsername(username: string): string {
  return `${username}@umbra.local`;
}

@Injectable()
export class AuthService {
  constructor(
    private prisma: PrismaService,
    private jwt: JwtService,
    private config: ConfigService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
    private mailer: MailerService,
  ) {}

  async register(dto: RegisterDto): Promise<AuthTokens> {
    if (this.demoMode.isActive()) {
      const identifier = dto.email ?? dto.username ?? "";
      const user = await this.demoStore.register(identifier, dto.password);
      return this.issueTokensFromDemo(user);
    }

    if (dto.email) {
      return this.registerWithEmail(dto.email, dto.password);
    }
    return this.registerWithUsername(dto.username!, dto.password);
  }

  async login(identifierRaw: string, password: string): Promise<AuthTokens> {
    if (this.demoMode.isActive()) {
      const user = await this.demoStore.login(identifierRaw, password);
      return this.issueTokensFromDemo(user);
    }

    const user = await this.findUserByIdentifier(identifierRaw);
    if (!user?.passwordHash || isUserDeleted(user)) {
      throw new UnauthorizedException("Invalid email/username or password");
    }

    const ok = await verifyPassword(user.passwordHash, password);
    if (!ok) {
      throw new UnauthorizedException("Invalid email/username or password");
    }

    return this.issueTokens(user);
  }

  // Google/Apple OAuth removed by design — Umbrella is anonymous-first.
  // Allowed identity: username+password, optional self-hosted email, optional Telegram.

  async telegramLogin(initData: string): Promise<AuthTokens> {
    const botToken = this.config.get<string>("TELEGRAM_BOT_TOKEN");

    if (this.demoMode.isActive() && !botToken) {
      const tgUser = parseTelegramInitDataUnsafe(initData);
      const user = this.demoStore.telegramLogin(tgUser);
      return this.issueTokensFromDemo(user);
    }

    if (!botToken) throw new UnauthorizedException("Telegram auth is not configured");

    const tgUser = resolveTelegramUser(initData, botToken);
    const telegramId = BigInt(tgUser.id);
    const fallbackEmail = `telegram_${tgUser.id}@umbra.local`;
    const displayName =
      [tgUser.first_name, tgUser.last_name].filter(Boolean).join(" ").trim() ||
      tgUser.username ||
      "Telegram User";

    let user = await this.prisma.user.findFirst({
      where: { telegramId, deletedAt: null },
    });

    if (!user) {
      const emailMatch = await this.prisma.user.findUnique({ where: { email: fallbackEmail } });
      if (emailMatch && !isUserDeleted(emailMatch)) {
        user = await this.prisma.user.update({
          where: { id: emailMatch.id },
          data: {
            telegramId,
            telegramUsername: tgUser.username ?? emailMatch.telegramUsername,
            name: emailMatch.name ?? displayName,
          },
        });
      } else {
        user = await this.prisma.user.create({
          data: {
            email: fallbackEmail,
            emailVerified: true,
            name: displayName,
            telegramId,
            telegramUsername: tgUser.username ?? null,
          },
        });
      }
    } else if (tgUser.username && user.telegramUsername !== tgUser.username) {
      user = await this.prisma.user.update({
        where: { id: user.id },
        data: { telegramUsername: tgUser.username },
      });
    }

    return this.issueTokens(user);
  }

  async refresh(refreshToken: string): Promise<AuthTokens> {
    let payload: RefreshJwtPayload;
    try {
      payload = this.jwt.verify(refreshToken, {
        secret: getJwtRefreshSecret(this.config),
      }) as RefreshJwtPayload;
    } catch {
      throw new UnauthorizedException("Invalid refresh token");
    }

    if (!payload.jti) {
      throw new UnauthorizedException("Invalid refresh token");
    }

    if (this.demoMode.isActive()) {
      const stored = await this.demoStore.findRefresh(refreshToken, payload.sub, payload.jti);
      if (!stored) throw new UnauthorizedException("Refresh token revoked or expired");
      this.demoStore.revokeRefresh(refreshToken, payload.jti);
      const user = this.demoStore.getUser(payload.sub);
      return this.issueTokensFromDemo(user);
    }

    const stored = await this.prisma.refreshToken.findFirst({
      where: {
        jti: payload.jti,
        userId: payload.sub,
        revokedAt: null,
        expiresAt: { gt: new Date() },
      },
    });
    if (!stored) throw new UnauthorizedException("Refresh token revoked or expired");

    const hashOk = await verifyRefreshToken(stored.tokenHash, refreshToken);
    if (!hashOk) throw new UnauthorizedException("Invalid refresh token");

    const user = await this.prisma.user.findFirst({
      where: { id: payload.sub, deletedAt: null },
    });
    if (!user) throw new UnauthorizedException();

    await this.prisma.refreshToken.update({
      where: { id: stored.id },
      data: { revokedAt: new Date() },
    });

    return this.issueTokens(user);
  }

  async logout(refreshToken?: string, userId?: string) {
    if (this.demoMode.isActive()) {
      if (refreshToken) {
        try {
          const payload = this.jwt.verify(refreshToken, {
            secret: getJwtRefreshSecret(this.config),
          }) as RefreshJwtPayload;
          this.demoStore.revokeRefresh(refreshToken, payload.jti);
        } catch {
          this.demoStore.revokeRefresh(refreshToken);
        }
      }
      return;
    }

    if (refreshToken) {
      try {
        const payload = this.jwt.verify(refreshToken, {
          secret: getJwtRefreshSecret(this.config),
        }) as RefreshJwtPayload;
        if (payload.jti) {
          await this.prisma.refreshToken.updateMany({
            where: { jti: payload.jti, revokedAt: null },
            data: { revokedAt: new Date() },
          });
        }
      } catch {
        const tokenHash = hashToken(refreshToken);
        await this.prisma.refreshToken.updateMany({
          where: { tokenHash, revokedAt: null },
          data: { revokedAt: new Date() },
        });
      }
    }

    if (userId) {
      await this.prisma.refreshToken.updateMany({
        where: { userId, revokedAt: null },
        data: { revokedAt: new Date() },
      });
    }
  }

  private async registerWithEmail(emailRaw: string, password: string): Promise<AuthTokens> {
    const email = normalizeEmail(emailRaw);
    const existing = await this.prisma.user.findUnique({ where: { email } });
    if (existing && !isUserDeleted(existing)) {
      throw new ConflictException("This email is already registered");
    }

    const passwordHash = await hashPassword(password);
    const user = await this.prisma.user.create({
      data: {
        email,
        emailVerified: false,
        passwordHash,
      },
    });

    await this.createEmailVerificationToken(user.id);
    return this.issueTokens(user);
  }

  private async registerWithUsername(usernameRaw: string, password: string): Promise<AuthTokens> {
    const username = normalizeUsername(usernameRaw);
    const existing = await this.prisma.user.findUnique({ where: { username } });
    if (existing && !isUserDeleted(existing)) {
      throw new ConflictException("This username is already taken");
    }

    const email = internalEmailForUsername(username);
    const emailTaken = await this.prisma.user.findUnique({ where: { email } });
    if (emailTaken && !isUserDeleted(emailTaken)) {
      throw new ConflictException("This username is already taken");
    }

    const passwordHash = await hashPassword(password);
    const user = await this.prisma.user.create({
      data: {
        email,
        emailVerified: true,
        username,
        name: usernameRaw.trim(),
        passwordHash,
      },
    });

    return this.issueTokens(user);
  }

  private async findUserByIdentifier(identifierRaw: string) {
    const trimmed = identifierRaw.trim();
    if (trimmed.includes("@")) {
      return this.prisma.user.findUnique({ where: { email: normalizeEmail(trimmed) } });
    }
    return this.prisma.user.findUnique({ where: { username: normalizeUsername(trimmed) } });
  }

  private issueTokensFromDemo(user: DemoUser): AuthTokens {
    const jti = newTokenId();
    const accessToken = this.jwt.sign(
      { sub: user.id, email: user.email },
      {
        secret: getJwtAccessSecret(this.config),
        expiresIn: this.config.get("JWT_ACCESS_TTL") ?? "15m",
      },
    );
    const refreshToken = this.jwt.sign(
      { sub: user.id, jti },
      {
        secret: getJwtRefreshSecret(this.config),
        expiresIn: this.config.get("JWT_REFRESH_TTL") ?? "30d",
      },
    );
    this.demoStore.storeRefreshToken(user.id, refreshToken, jti);
    return {
      accessToken,
      refreshToken,
      user: {
        id: user.id,
        email: user.email,
        username: user.username,
        name: user.name,
        lang: user.lang,
        emailVerified: user.emailVerified,
      },
    };
  }

  private async issueTokens(user: User): Promise<AuthTokens> {
    const jti = newTokenId();
    const accessToken = this.jwt.sign(
      { sub: user.id, email: user.email },
      {
        secret: getJwtAccessSecret(this.config),
        expiresIn: this.config.get("JWT_ACCESS_TTL") ?? "15m",
      },
    );

    const refreshToken = this.jwt.sign(
      { sub: user.id, jti },
      {
        secret: getJwtRefreshSecret(this.config),
        expiresIn: this.config.get("JWT_REFRESH_TTL") ?? "30d",
      },
    );

    const refreshTtlDays = 30;
    await this.prisma.refreshToken.create({
      data: {
        userId: user.id,
        jti,
        tokenHash: await hashRefreshToken(refreshToken),
        expiresAt: new Date(Date.now() + refreshTtlDays * 86400000),
      },
    });

    return {
      accessToken,
      refreshToken,
      user: {
        id: user.id,
        email: user.email,
        username: user.username,
        name: user.name,
        lang: user.lang,
        emailVerified: user.emailVerified,
      },
    };
  }

  /** Set/replace the user's email and send a verification link (logged when SMTP is off). */
  async requestEmailVerification(userId: string, emailRaw: string) {
    const email = normalizeEmail(emailRaw);

    if (this.demoMode.isActive()) {
      this.demoStore.updateUser(userId, { email, emailVerified: true });
      return { sent: false, demo: true };
    }

    const taken = await this.prisma.user.findUnique({ where: { email } });
    if (taken && taken.id !== userId && !isUserDeleted(taken)) {
      throw new ConflictException("This email is already used by another account");
    }

    const user = await this.prisma.user.findFirst({ where: { id: userId, deletedAt: null } });
    if (!user) throw new UnauthorizedException();

    if (user.email !== email) {
      await this.prisma.user.update({
        where: { id: userId },
        data: { email, emailVerified: false },
      });
    }

    await this.prisma.emailVerificationToken.deleteMany({ where: { userId } });
    const token = generateToken(24);
    await this.prisma.emailVerificationToken.create({
      data: {
        userId,
        tokenHash: hashToken(token),
        expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000),
      },
    });

    const webAppUrl = this.config.get<string>("TELEGRAM_WEBAPP_URL") ?? "http://localhost:5173";
    const verifyUrl = `${webAppUrl.replace(/\/$/, "")}/verify-email?token=${token}`;
    const sent = await this.mailer.send(
      email,
      "Umbrella Wallet — email verification",
      `Confirm your email for Umbrella Wallet: ${verifyUrl}\nThis link is valid for 24 hours. If this wasn't you — ignore this email.`,
    );

    return { sent };
  }

  /** Verify an email token from the /verify-email link. */
  async verifyEmail(token: string) {
    if (this.demoMode.isActive()) {
      return { verified: true };
    }

    const tokenHash = hashToken(token.trim());
    const record = await this.prisma.emailVerificationToken.findFirst({
      where: { tokenHash, expiresAt: { gt: new Date() } },
    });
    if (!record) {
      throw new UnauthorizedException("Link is invalid or expired");
    }

    await this.prisma.$transaction([
      this.prisma.user.update({
        where: { id: record.userId },
        data: { emailVerified: true },
      }),
      this.prisma.emailVerificationToken.deleteMany({ where: { userId: record.userId } }),
    ]);

    return { verified: true };
  }

  private async createEmailVerificationToken(userId: string) {
    const token = generateToken(24);
    await this.prisma.emailVerificationToken.create({
      data: {
        userId,
        tokenHash: hashToken(token),
        expiresAt: new Date(Date.now() + 15 * 60 * 1000),
      },
    });
    // In production: send email with token (no PII in logs)
  }
}
