import { Injectable, UnauthorizedException } from "@nestjs/common";
import { PassportStrategy } from "@nestjs/passport";
import { ExtractJwt, Strategy } from "passport-jwt";
import { ConfigService } from "@nestjs/config";
import { PrismaService } from "../prisma/prisma.service";
import { getJwtAccessSecret } from "../common/env.validation";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { isUserDeleted } from "../users/user-profile.util";

export type JwtPayload = { sub: string; email: string };

@Injectable()
export class JwtStrategy extends PassportStrategy(Strategy) {
  constructor(
    config: ConfigService,
    private prisma: PrismaService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: getJwtAccessSecret(config),
    });
  }

  async validate(payload: JwtPayload) {
    if (this.demoMode.isActive()) {
      try {
        return this.demoStore.getUser(payload.sub);
      } catch {
        throw new UnauthorizedException();
      }
    }

    const user = await this.prisma.user.findUnique({ where: { id: payload.sub } });
    if (!user || isUserDeleted(user)) throw new UnauthorizedException();
    return user;
  }
}
