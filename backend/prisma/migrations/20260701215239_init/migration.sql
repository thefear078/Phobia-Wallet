-- CreateTable
CREATE TABLE "users" (
    "id" TEXT NOT NULL,
    "email" TEXT NOT NULL,
    "emailVerified" BOOLEAN NOT NULL DEFAULT false,
    "passwordHash" TEXT,
    "name" TEXT,
    "telegramId" BIGINT,
    "telegramUsername" TEXT,
    "telegramNotifications" BOOLEAN NOT NULL DEFAULT true,
    "lang" TEXT NOT NULL DEFAULT 'uk',
    "tfaEnabled" BOOLEAN NOT NULL DEFAULT false,
    "oauthProvider" TEXT,
    "oauthSub" TEXT,
    "pushEnabled" BOOLEAN NOT NULL DEFAULT true,
    "emailAlerts" BOOLEAN NOT NULL DEFAULT false,
    "priceAlerts" BOOLEAN NOT NULL DEFAULT true,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "users_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "refresh_tokens" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "tokenHash" TEXT NOT NULL,
    "expiresAt" TIMESTAMP(3) NOT NULL,
    "revokedAt" TIMESTAMP(3),
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "refresh_tokens_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "email_verification_tokens" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "tokenHash" TEXT NOT NULL,
    "expiresAt" TIMESTAMP(3) NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "email_verification_tokens_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "linked_wallets" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "chain" TEXT NOT NULL,
    "address" TEXT NOT NULL,
    "label" TEXT,
    "linkedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "linked_wallets_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "linked_bank_accounts" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "provider" TEXT NOT NULL,
    "providerAccountId" TEXT NOT NULL,
    "bankName" TEXT,
    "maskedNumber" TEXT,
    "status" TEXT NOT NULL DEFAULT 'active',
    "linkedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "linked_bank_accounts_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "p2p_offers" (
    "id" TEXT NOT NULL,
    "merchantId" TEXT NOT NULL,
    "asset" TEXT NOT NULL,
    "fiatCurrency" TEXT NOT NULL,
    "price" DECIMAL(20,8) NOT NULL,
    "minAmount" DECIMAL(20,8),
    "maxAmount" DECIMAL(20,8),
    "paymentMethods" TEXT[],
    "side" TEXT NOT NULL DEFAULT 'buy',
    "status" TEXT NOT NULL DEFAULT 'active',
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "p2p_offers_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "p2p_orders" (
    "id" TEXT NOT NULL,
    "offerId" TEXT NOT NULL,
    "buyerId" TEXT NOT NULL,
    "sellerId" TEXT NOT NULL,
    "amount" DECIMAL(20,8) NOT NULL,
    "status" TEXT NOT NULL DEFAULT 'created',
    "cryptoTxHash" TEXT,
    "fiatPaymentReference" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "p2p_orders_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "kyc_records" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "status" TEXT NOT NULL DEFAULT 'none',
    "provider" TEXT,
    "providerReference" TEXT,
    "level" TEXT NOT NULL DEFAULT 'basic',
    "updatedAt" TIMESTAMP(3) NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "kyc_records_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "users_email_key" ON "users"("email");

-- CreateIndex
CREATE UNIQUE INDEX "users_telegramId_key" ON "users"("telegramId");

-- CreateIndex
CREATE UNIQUE INDEX "users_oauthProvider_oauthSub_key" ON "users"("oauthProvider", "oauthSub");

-- CreateIndex
CREATE INDEX "refresh_tokens_userId_idx" ON "refresh_tokens"("userId");

-- CreateIndex
CREATE INDEX "email_verification_tokens_userId_idx" ON "email_verification_tokens"("userId");

-- CreateIndex
CREATE INDEX "linked_wallets_userId_idx" ON "linked_wallets"("userId");

-- CreateIndex
CREATE UNIQUE INDEX "linked_wallets_userId_chain_address_key" ON "linked_wallets"("userId", "chain", "address");

-- CreateIndex
CREATE INDEX "linked_bank_accounts_userId_idx" ON "linked_bank_accounts"("userId");

-- CreateIndex
CREATE UNIQUE INDEX "linked_bank_accounts_userId_provider_providerAccountId_key" ON "linked_bank_accounts"("userId", "provider", "providerAccountId");

-- CreateIndex
CREATE INDEX "p2p_offers_asset_status_idx" ON "p2p_offers"("asset", "status");

-- CreateIndex
CREATE INDEX "p2p_orders_buyerId_idx" ON "p2p_orders"("buyerId");

-- CreateIndex
CREATE INDEX "p2p_orders_sellerId_idx" ON "p2p_orders"("sellerId");

-- CreateIndex
CREATE INDEX "p2p_orders_offerId_idx" ON "p2p_orders"("offerId");

-- CreateIndex
CREATE UNIQUE INDEX "kyc_records_userId_key" ON "kyc_records"("userId");

-- AddForeignKey
ALTER TABLE "refresh_tokens" ADD CONSTRAINT "refresh_tokens_userId_fkey" FOREIGN KEY ("userId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "linked_wallets" ADD CONSTRAINT "linked_wallets_userId_fkey" FOREIGN KEY ("userId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "linked_bank_accounts" ADD CONSTRAINT "linked_bank_accounts_userId_fkey" FOREIGN KEY ("userId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "p2p_offers" ADD CONSTRAINT "p2p_offers_merchantId_fkey" FOREIGN KEY ("merchantId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "p2p_orders" ADD CONSTRAINT "p2p_orders_offerId_fkey" FOREIGN KEY ("offerId") REFERENCES "p2p_offers"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "p2p_orders" ADD CONSTRAINT "p2p_orders_buyerId_fkey" FOREIGN KEY ("buyerId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "p2p_orders" ADD CONSTRAINT "p2p_orders_sellerId_fkey" FOREIGN KEY ("sellerId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "kyc_records" ADD CONSTRAINT "kyc_records_userId_fkey" FOREIGN KEY ("userId") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;
