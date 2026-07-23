import { Body, Controller, Delete, Get, Param, Post, Query, Req, UseGuards } from "@nestjs/common";
import type { Request } from "express";
import { JwtAuthGuard } from "../auth/jwt-auth.guard";
import { LinkedWalletsService } from "./linked-wallets.service";
import { LinkWalletDto } from "./dto/linked-wallets.dto";

@Controller("wallets")
@UseGuards(JwtAuthGuard)
export class LinkedWalletsController {
  constructor(private wallets: LinkedWalletsService) {}

  @Get()
  list(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.wallets.list(user.id);
  }

  @Get("balances")
  balances(@Req() req: Request, @Query("chain") chain?: string) {
    const user = req.user as { id: string };
    return this.wallets.balances(user.id, chain);
  }

  @Get("challenge")
  challenge(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.wallets.createChallenge(user.id);
  }

  @Post()
  link(@Req() req: Request, @Body() dto: LinkWalletDto) {
    const user = req.user as { id: string };
    return this.wallets.link(user.id, dto);
  }

  @Delete(":id")
  unlink(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.wallets.unlink(user.id, id);
  }
}
