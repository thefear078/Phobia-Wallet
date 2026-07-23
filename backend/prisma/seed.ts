import { PrismaClient } from "@prisma/client";
import { hashPassword } from "../src/common/crypto.util";

const prisma = new PrismaClient();

async function main() {
  const demoHash = await hashPassword("password123");

  await prisma.user.upsert({
    where: { username: "demo" },
    update: { passwordHash: demoHash },
    create: {
      email: "demo@umbra.local",
      emailVerified: true,
      username: "demo",
      name: "Demo",
      passwordHash: demoHash,
    },
  });

  const merchant = await prisma.user.upsert({
    where: { email: "merchant@umbra.wallet" },
    update: { username: "cryptoking", name: "CryptoKing" },
    create: {
      email: "merchant@umbra.wallet",
      emailVerified: true,
      username: "cryptoking",
      name: "CryptoKing",
      passwordHash: null,
    },
  });

  const merchant2 = await prisma.user.upsert({
    where: { email: "fasttrader@umbra.wallet" },
    update: { username: "fasttrader", name: "FastTrader" },
    create: {
      email: "fasttrader@umbra.wallet",
      emailVerified: true,
      username: "fasttrader",
      name: "FastTrader",
    },
  });

  const merchant3 = await prisma.user.upsert({
    where: { email: "safe@umbra.wallet" },
    update: { username: "safeexchange", name: "SafeExchange" },
    create: {
      email: "safe@umbra.wallet",
      emailVerified: true,
      username: "safeexchange",
      name: "SafeExchange",
    },
  });

  const count = await prisma.p2pOffer.count();
  if (count === 0) {
    await prisma.p2pOffer.createMany({
      data: [
        {
          merchantId: merchant.id,
          asset: "USDT",
          fiatCurrency: "UAH",
          price: 41.25,
          minAmount: 500,
          maxAmount: 50000,
          paymentMethods: ["monobank", "privatbank"],
          side: "buy",
        },
        {
          merchantId: merchant2.id,
          asset: "USDT",
          fiatCurrency: "UAH",
          price: 41.28,
          minAmount: 1000,
          maxAmount: 25000,
          paymentMethods: ["monobank"],
          side: "buy",
        },
        {
          merchantId: merchant3.id,
          asset: "USDT",
          fiatCurrency: "UAH",
          price: 41.31,
          minAmount: 200,
          maxAmount: 100000,
          paymentMethods: ["privatbank", "pumb"],
          side: "buy",
        },
        {
          merchantId: merchant.id,
          asset: "BTC",
          fiatCurrency: "USD",
          price: 68380,
          minAmount: 100,
          maxAmount: 15000,
          paymentMethods: ["wise", "revolut"],
          side: "sell",
        },
      ],
    });
  }

  console.log("Seed complete (demo / password123)");
}

main()
  .catch((e) => {
    console.error(e);
    process.exit(1);
  })
  .finally(() => prisma.$disconnect());
