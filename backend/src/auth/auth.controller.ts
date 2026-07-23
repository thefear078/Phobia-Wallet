import {
  Body,
  Controller,
  HttpCode,
  Post,
  Req,
  Res,
  UnauthorizedException,
  UseGuards,
} from "@nestjs/common";
import { Throttle } from "@nestjs/throttler";
import type { Request, Response } from "express";
import { AuthService } from "./auth.service";
import { LoginDto, RegisterDto, RequestEmailVerificationDto, VerifyEmailDto } from "./dto/auth.dto";
import { JwtAuthGuard } from "./jwt-auth.guard";
import { ConfigService } from "@nestjs/config";

const REFRESH_COOKIE = "umbra_refresh";

@Controller("auth")
export class AuthController {
  constructor(
    private auth: AuthService,
    private config: ConfigService,
  ) {}

  @Post("register")
  @Throttle({ default: { limit: 5, ttl: 900000 } })
  async register(@Body() dto: RegisterDto, @Res({ passthrough: true }) res: Response) {
    const result = await this.auth.register(dto);
    this.setRefreshCookie(res, result.refreshToken);
    return { accessToken: result.accessToken, user: result.user };
  }

  @Post("login")
  @HttpCode(200)
  @Throttle({ default: { limit: 5, ttl: 900000 } })
  async login(@Body() dto: LoginDto, @Res({ passthrough: true }) res: Response) {
    const result = await this.auth.login(dto.username, dto.password);
    this.setRefreshCookie(res, result.refreshToken);
    return { accessToken: result.accessToken, user: result.user };
  }

  // OAuth (Google/Apple) endpoints intentionally removed — Umbrella is
  // anonymous-first (nick+password or Telegram only). No third-party identity.

  @Post("refresh")
  @HttpCode(200)
  async refresh(@Req() req: Request, @Res({ passthrough: true }) res: Response) {
    const token = req.cookies?.[REFRESH_COOKIE] as string | undefined;
    if (!token) throw new UnauthorizedException("No refresh token");
    const result = await this.auth.refresh(token);
    this.setRefreshCookie(res, result.refreshToken);
    return { accessToken: result.accessToken, user: result.user };
  }

  @Post("email/request-verification")
  @HttpCode(200)
  @UseGuards(JwtAuthGuard)
  @Throttle({ default: { limit: 3, ttl: 900000 } })
  async requestEmailVerification(@Req() req: Request, @Body() dto: RequestEmailVerificationDto) {
    const user = req.user as { id: string };
    return this.auth.requestEmailVerification(user.id, dto.email);
  }

  @Post("email/verify")
  @HttpCode(200)
  @Throttle({ default: { limit: 10, ttl: 900000 } })
  async verifyEmail(@Body() dto: VerifyEmailDto) {
    return this.auth.verifyEmail(dto.token);
  }

  @Post("logout")
  @HttpCode(200)
  @UseGuards(JwtAuthGuard)
  async logout(@Req() req: Request, @Res({ passthrough: true }) res: Response) {
    const user = req.user as { id: string };
    const token = req.cookies?.[REFRESH_COOKIE] as string | undefined;
    await this.auth.logout(token, user.id);
    res.clearCookie(REFRESH_COOKIE);
    return { ok: true };
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
