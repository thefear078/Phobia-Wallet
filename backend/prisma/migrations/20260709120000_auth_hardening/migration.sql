-- Auth hardening: GDPR soft delete + refresh token jti for rotation lookup

ALTER TABLE "users" ADD COLUMN "deletedAt" TIMESTAMP(3);

ALTER TABLE "refresh_tokens" ADD COLUMN "jti" TEXT;

UPDATE "refresh_tokens" SET "jti" = gen_random_uuid()::text WHERE "jti" IS NULL;

ALTER TABLE "refresh_tokens" ALTER COLUMN "jti" SET NOT NULL;

CREATE UNIQUE INDEX "refresh_tokens_jti_key" ON "refresh_tokens"("jti");
