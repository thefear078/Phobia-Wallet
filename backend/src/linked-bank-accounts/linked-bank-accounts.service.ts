import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { PrismaService } from "../prisma/prisma.service";
import { decryptSecret, encryptSecret } from "../common/encryption.util";
import { monobankCurrencyCode } from "../common/evm-rpc.util";
import {
  LinkBankAccountDto,
  LinkMonobankDto,
  resolveMonobankToken,
} from "./dto/linked-bank-accounts.dto";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";

type MonobankAccount = {
  id: string;
  balance?: number;
  creditLimit?: number;
  type?: string;
  currencyCode?: number;
  iban?: string;
  maskedPan?: string[];
};

type MonobankClientInfo = {
  clientId: string;
  name: string;
  accounts?: MonobankAccount[];
};

const BANK_LIST_SELECT = {
  id: true,
  provider: true,
  providerAccountId: true,
  bankName: true,
  maskedNumber: true,
  maskedIban: true,
  accountType: true,
  currency: true,
  status: true,
  linkedAt: true,
} as const;

@Injectable()
export class LinkedBankAccountsService {
  constructor(
    private prisma: PrismaService,
    private config: ConfigService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  list(userId: string) {
    if (this.demoMode.isActive()) return this.demoStore.listBanks(userId);
    return this.prisma.linkedBankAccount.findMany({
      where: { userId, status: "active" },
      orderBy: { linkedAt: "desc" },
      select: BANK_LIST_SELECT,
    });
  }

  async link(userId: string, dto: LinkBankAccountDto) {
    if (this.demoMode.isActive()) {
      return this.demoStore.linkBank(userId, {
        provider: dto.provider,
        bankName: dto.bankName,
        maskedNumber: dto.maskedNumber,
      });
    }
    const provider = dto.provider.toLowerCase();
    try {
      return await this.prisma.linkedBankAccount.create({
        data: {
          userId,
          provider,
          providerAccountId: dto.providerAccountId,
          bankName: dto.bankName ?? null,
          maskedNumber: dto.maskedNumber ?? null,
          status: "active",
        },
        select: BANK_LIST_SELECT,
      });
    } catch {
      throw new ConflictException("Bank account already linked");
    }
  }

  /**
   * Monobank: validate personal token via client-info, persist accounts + encrypted token.
   * Plaintext token is never stored in DB.
   */
  async linkMonobank(userId: string, dto: LinkMonobankDto) {
    let personalToken: string;
    try {
      personalToken = resolveMonobankToken(dto);
    } catch {
      throw new BadRequestException("Monobank token is required");
    }

    if (this.demoMode.isActive()) {
      const row = this.demoStore.linkBank(userId, {
        provider: "monobank",
        bankName: "Monobank",
        maskedNumber: "**** 4242",
        maskedIban: "UA****4242",
        accountType: "black",
        currency: "UAH",
      });
      return { linked: [row], clientName: "Demo Client" };
    }

    const info = await this.fetchMonobankClientInfo(personalToken);
    const accounts = info.accounts ?? [];
    if (accounts.length === 0) {
      throw new BadRequestException("No Monobank accounts found for this token");
    }

    const encryptedToken = encryptSecret(personalToken, this.getEncryptionKey());
    const linked = [];

    for (const acc of accounts.slice(0, 10)) {
      const maskedPan = acc.maskedPan?.[0] ?? null;
      const row = await this.prisma.linkedBankAccount.upsert({
        where: {
          userId_provider_providerAccountId: {
            userId,
            provider: "monobank",
            providerAccountId: acc.id,
          },
        },
        create: {
          userId,
          provider: "monobank",
          providerAccountId: acc.id,
          bankName: "Monobank",
          maskedNumber: maskedPan,
          maskedIban: this.maskIban(acc.iban),
          accountType: acc.type ?? null,
          currency: acc.currencyCode ? monobankCurrencyCode(acc.currencyCode) : null,
          encryptedProviderToken: encryptedToken,
          status: "active",
        },
        update: {
          bankName: "Monobank",
          maskedNumber: maskedPan,
          maskedIban: this.maskIban(acc.iban),
          accountType: acc.type ?? null,
          currency: acc.currencyCode ? monobankCurrencyCode(acc.currencyCode) : null,
          encryptedProviderToken: encryptedToken,
          status: "active",
        },
        select: BANK_LIST_SELECT,
      });
      linked.push(row);
    }

    return { linked, clientName: info.name };
  }

  async balance(userId: string, accountId: string) {
    if (this.demoMode.isActive()) {
      return this.demoStore.bankBalance(userId, accountId);
    }

    const account = await this.prisma.linkedBankAccount.findFirst({
      where: { id: accountId, userId, status: "active" },
    });
    if (!account) throw new NotFoundException("Linked bank account not found");

    if (!account.encryptedProviderToken) {
      throw new BadRequestException("Bank account is not linked for live balance sync");
    }

    if (account.provider !== "monobank") {
      throw new BadRequestException(`Balance proxy not implemented for ${account.provider}`);
    }

    const token = decryptSecret(account.encryptedProviderToken, this.getEncryptionKey());
    const info = await this.fetchMonobankClientInfo(token);
    const remote = info.accounts?.find((a) => a.id === account.providerAccountId);
    if (!remote) {
      throw new NotFoundException("Account not found at Monobank");
    }

    const minor = remote.balance ?? 0;
    return {
      accountId: account.id,
      provider: account.provider,
      providerAccountId: account.providerAccountId,
      balance: minor / 100,
      creditLimit: (remote.creditLimit ?? 0) / 100,
      currency:
        account.currency ??
        (remote.currencyCode ? monobankCurrencyCode(remote.currencyCode) : "UAH"),
      accountType: account.accountType ?? remote.type ?? null,
      maskedIban: account.maskedIban,
    };
  }

  async revoke(userId: string, accountId: string) {
    if (this.demoMode.isActive()) return this.demoStore.revokeBank(userId, accountId);
    const account = await this.prisma.linkedBankAccount.findFirst({
      where: { id: accountId, userId },
    });
    if (!account) throw new NotFoundException("Linked bank account not found");

    await this.prisma.linkedBankAccount.update({
      where: { id: accountId },
      data: {
        status: "revoked",
        encryptedProviderToken: null,
      },
    });
    return { ok: true };
  }

  private async fetchMonobankClientInfo(personalToken: string): Promise<MonobankClientInfo> {
    const res = await fetch("https://api.monobank.ua/personal/client-info", {
      headers: { "X-Token": personalToken },
    });

    if (!res.ok) {
      throw new BadRequestException("Invalid Monobank personal token");
    }

    return (await res.json()) as MonobankClientInfo;
  }

  private getEncryptionKey(): string {
    const key =
      this.config.get<string>("OPEN_BANKING_ENCRYPTION_KEY")?.trim() ??
      this.config.get<string>("BANK_TOKEN_ENCRYPTION_KEY")?.trim();
    if (key && key.length >= 32) return key;
    if (this.config.get<string>("NODE_ENV") === "production") {
      throw new BadRequestException("Open banking encryption key is not configured");
    }
    return "dev-open-banking-encryption-key-32b";
  }

  private maskIban(iban?: string): string | null {
    if (!iban) return null;
    const clean = iban.replace(/\s/g, "");
    if (clean.length <= 8) return clean;
    return `${clean.slice(0, 4)}****${clean.slice(-4)}`;
  }
}
