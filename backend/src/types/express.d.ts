import type { User as PrismaUser } from "@prisma/client";

declare global {
  namespace Express {
    // eslint-disable-next-line @typescript-eslint/no-empty-object-type -- declaration merging with Passport's Express.User
    interface User extends PrismaUser {}
  }
}

export {};
