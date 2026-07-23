/** Dev helper: set a login password for seeded merchants (local testing only). */
import { PrismaClient } from "@prisma/client";
import { hashPassword } from "../src/common/crypto.util";

const prisma = new PrismaClient();

async function main() {
  const password = process.env.DEV_MERCHANT_PASSWORD ?? "merchant123";
  const hash = await hashPassword(password);
  const res = await prisma.user.updateMany({
    where: { username: { in: ["cryptoking", "fasttrader", "safeexchange"] } },
    data: { passwordHash: hash },
  });
  console.log(`Updated ${res.count} merchant password(s)`);
}

main()
  .catch((e) => {
    console.error(e);
    process.exit(1);
  })
  .finally(() => prisma.$disconnect());
