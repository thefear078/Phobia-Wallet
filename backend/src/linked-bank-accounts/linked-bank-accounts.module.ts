import { Module } from "@nestjs/common";
import { LinkedBankAccountsController } from "./linked-bank-accounts.controller";
import { LinkedBankAccountsService } from "./linked-bank-accounts.service";

@Module({
  controllers: [LinkedBankAccountsController],
  providers: [LinkedBankAccountsService],
  exports: [LinkedBankAccountsService],
})
export class LinkedBankAccountsModule {}
