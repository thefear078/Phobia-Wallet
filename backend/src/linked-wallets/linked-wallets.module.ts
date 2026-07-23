import { Module } from "@nestjs/common";
import { LinkedWalletsController } from "./linked-wallets.controller";
import { LinkedWalletsService } from "./linked-wallets.service";

@Module({
  controllers: [LinkedWalletsController],
  providers: [LinkedWalletsService],
  exports: [LinkedWalletsService],
})
export class LinkedWalletsModule {}
