import { Body, Controller, Delete, Get, Param, Post, Req, UseGuards } from "@nestjs/common";
import type { Request } from "express";
import { JwtAuthGuard } from "../auth/jwt-auth.guard";
import { LinkedBankAccountsService } from "./linked-bank-accounts.service";
import { LinkBankAccountDto, LinkMonobankDto } from "./dto/linked-bank-accounts.dto";

@Controller("bank-accounts")
@UseGuards(JwtAuthGuard)
export class LinkedBankAccountsController {
  constructor(private accounts: LinkedBankAccountsService) {}

  @Get()
  list(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.accounts.list(user.id);
  }

  @Post()
  link(@Req() req: Request, @Body() dto: LinkBankAccountDto) {
    const user = req.user as { id: string };
    return this.accounts.link(user.id, dto);
  }

  @Post("monobank/link")
  linkMonobank(@Req() req: Request, @Body() dto: LinkMonobankDto) {
    const user = req.user as { id: string };
    return this.accounts.linkMonobank(user.id, dto);
  }

  @Delete(":id")
  revoke(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.accounts.revoke(user.id, id);
  }

  @Get(":id/balance")
  balance(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.accounts.balance(user.id, id);
  }
}
