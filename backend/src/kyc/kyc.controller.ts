import { Controller, Get, Post, Req, UseGuards } from "@nestjs/common";
import type { Request } from "express";
import { KycService } from "./kyc.service";
import { JwtAuthGuard } from "../auth/jwt-auth.guard";

@Controller("kyc")
export class KycController {
  constructor(private kyc: KycService) {}

  @Get("status")
  @UseGuards(JwtAuthGuard)
  status(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.kyc.getStatus(user.id);
  }

  @Post("start")
  @UseGuards(JwtAuthGuard)
  start(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.kyc.start(user.id);
  }
}
