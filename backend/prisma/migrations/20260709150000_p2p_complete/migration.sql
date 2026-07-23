-- P2P production: offer reservation, order payment/dispute metadata

ALTER TABLE "p2p_offers" ADD COLUMN "reservedAmount" DECIMAL(20,8) NOT NULL DEFAULT 0;
ALTER TABLE "p2p_offers" ADD COLUMN "updatedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP;

ALTER TABLE "p2p_orders" ADD COLUMN "paymentMethod" TEXT;
ALTER TABLE "p2p_orders" ADD COLUMN "disputeReason" TEXT;

CREATE INDEX "p2p_offers_fiatCurrency_status_idx" ON "p2p_offers"("fiatCurrency", "status");
CREATE INDEX "p2p_orders_status_idx" ON "p2p_orders"("status");
