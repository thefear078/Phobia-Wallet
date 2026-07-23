-- Linking: Monobank metadata + encrypted provider token storage

ALTER TABLE "linked_bank_accounts" ADD COLUMN "masked_iban" TEXT;
ALTER TABLE "linked_bank_accounts" ADD COLUMN "account_type" TEXT;
ALTER TABLE "linked_bank_accounts" ADD COLUMN "currency" TEXT;
ALTER TABLE "linked_bank_accounts" ADD COLUMN "encrypted_provider_token" TEXT;
