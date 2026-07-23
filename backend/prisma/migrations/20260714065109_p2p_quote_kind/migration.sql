-- AlterTable
ALTER TABLE "p2p_offers" ADD COLUMN     "quoteKind" TEXT NOT NULL DEFAULT 'fiat',
ALTER COLUMN "updatedAt" DROP DEFAULT;
