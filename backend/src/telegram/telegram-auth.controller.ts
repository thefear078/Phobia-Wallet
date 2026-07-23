import { Body, Controller, HttpCode, Post, Res } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { Throttle } from "@nestjs/throttler";
import type { Response } from "express";
import { AuthService } from "../auth/auth.service";
import { TelegramAuthDto } from "../auth/dto/auth.dto";

const REFRESH_COOKIE = "umbra_refresh";

@Controller("auth")
export class TelegramAuthController {
  constructor(
    private auth: AuthService,
    private config: ConfigService,
  ) {}

  @Post("telegram")
  @HttpCode(200)
  @Throttle({ default: { limit: 10, ttl: 60000 } })
  async telegramAuth(@Body() dto: TelegramAuthDto, @Res({ passthrough: true }) res: Response) {
    const result = await this.auth.telegramLogin(dto.initData);
    this.setRefreshCookie(res, result.refreshToken);
    return { accessToken: result.accessToken, user: result.user };
  }

  private setRefreshCookie(res: Response, token: string) {
    const secure = this.config.get<string>("COOKIE_SECURE") === "true";
    res.cookie(REFRESH_COOKIE, token, {
      httpOnly: true,
      secure,
      sameSite: "lax",
      maxAge: 30 * 24 * 60 * 60 * 1000,
      path: "/auth",
    });
  }
}
