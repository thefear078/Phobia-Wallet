import { Body, Controller, Delete, Get, Patch, Req, UseGuards } from "@nestjs/common";
import type { Request } from "express";
import { UsersService } from "./users.service";
import { JwtAuthGuard } from "../auth/jwt-auth.guard";
import { UpdateUserDto } from "./dto/users.dto";

@Controller("users")
@UseGuards(JwtAuthGuard)
export class UsersController {
  constructor(private users: UsersService) {}

  @Get("me")
  me(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.users.getMe(user.id);
  }

  @Patch("me")
  update(@Req() req: Request, @Body() dto: UpdateUserDto) {
    const user = req.user as { id: string };
    return this.users.update(user.id, dto);
  }

  @Delete("me")
  deleteMe(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.users.deleteAccount(user.id);
  }
}
