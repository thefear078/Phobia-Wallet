import {
  BadRequestException,
  ConflictException,
  Injectable,
  NotFoundException,
} from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { PrismaService } from "../prisma/prisma.service";
import { RedisService } from "../redis/redis.service";
import { generateToken } from "../common/crypto.util";
import { validateWalletAddress, isEvmChain } from "../common/address.validation";
import { verifyEvmPersonalSign } from "../common/evm-signature.util";
import {
  fetchBtcBalance,
  fetchTonBalance,
  fetchSolBalance,
  fetchTronBalance,
} from "../common/blockchain.util";
import { fetchEvmNativeBalance, fetchEvmTokenBalances } from "../common/evm-rpc.util";
import { LinkWalletDto } from "./dto/linked-wallets.dto";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import {
  WALLET_BALANCES_CACHE_PREFIX,
  WALLET_BALANCES_CACHE_TTL_SEC,
  type WalletBalanceResponse,
  type WalletTokenBalance,
} from "./wallet-balances.types";

const CHALLENGE_TTL_SEC = 300;
const CHALLENGE_PREFIX = "wallet-challenge:";

const WALLET_LIST_SELECT = {
  id: true,
  chain: true,
  address: true,
  label: true,
  linkedAt: true,
} as const;

@Injectable()
export class LinkedWalletsService {
  constructor(
    private prisma: PrismaService,
    private redis: RedisService,
    private config: ConfigService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
  ) {}

  list(userId: string) {
    if (this.demoMode.isActive()) return this.demoStore.listWallets(userId);
    return this.prisma.linkedWallet.findMany({
      where: { userId },
      orderBy: { linkedAt: "desc" },
      select: WALLET_LIST_SELECT,
    });
  }

  async createChallenge(userId: string) {
    if (this.demoMode.isActive()) return this.demoStore.createWalletChallenge(userId);
    const nonce = generateToken(16);
    const issued = new Date().toISOString();
    const message = [
      "Umbrella Wallet — Link Wallet",
      `Nonce: ${nonce}`,
      `User: ${userId}`,
      `Issued: ${issued}`,
    ].join("\n");

    await this.redis.client.setex(
      `${CHALLENGE_PREFIX}${userId}`,
      CHALLENGE_TTL_SEC,
      JSON.stringify({ nonce, message }),
    );

    return { nonce, message, expiresIn: CHALLENGE_TTL_SEC };
  }

  async link(userId: string, dto: LinkWalletDto) {
    const chain = dto.chain.toLowerCase();
    const address = validateWalletAddress(chain, dto.address);

    if (this.demoMode.isActive()) {
      if (dto.watchOnly) {
        return this.demoStore.linkWallet(userId, chain, address, dto.label?.trim() || "Watch-only");
      }
      if (!dto.message || !dto.signature) {
        throw new BadRequestException("Signature required in demo mode for owned wallets");
      }
      const challenge = this.demoStore.getWalletChallenge(userId);
      if (!challenge || dto.message !== challenge.message) {
        throw new BadRequestException("Invalid wallet challenge");
      }
      this.demoStore.clearWalletChallenge(userId);
      return this.demoStore.linkWallet(userId, chain, address, dto.label?.trim() || null);
    }

    if (dto.watchOnly) {
      return this.persistLink(userId, chain, address, dto.label?.trim() || "Watch-only");
    }

    if (!dto.message || !dto.signature) {
      throw new BadRequestException(
        "Signature required. Connect wallet and sign the challenge, or use watchOnly for read-only addresses.",
      );
    }

    const cached = await this.redis.client.get(`${CHALLENGE_PREFIX}${userId}`);
    if (!cached) {
      throw new BadRequestException("Wallet challenge expired. Request a new challenge.");
    }

    const { nonce, message: expectedMessage } = JSON.parse(cached) as {
      nonce: string;
      message: string;
    };

    if (dto.message !== expectedMessage || !dto.message.includes(nonce)) {
      throw new BadRequestException("Invalid or tampered link message");
    }

    if (isEvmChain(chain)) {
      await verifyEvmPersonalSign(dto.message, dto.signature, address);
    } else {
      throw new BadRequestException(
        "On-chain signature proof is currently supported for EVM wallets only. Use watchOnly for other chains.",
      );
    }

    await this.redis.client.del(`${CHALLENGE_PREFIX}${userId}`);
    await this.invalidateBalancesCache(userId);

    return this.persistLink(userId, chain, address, dto.label?.trim() || null);
  }

  private async persistLink(userId: string, chain: string, address: string, label: string | null) {
    try {
      return await this.prisma.linkedWallet.create({
        data: { userId, chain, address, label },
        select: WALLET_LIST_SELECT,
      });
    } catch {
      throw new ConflictException("Wallet already linked for this chain and address");
    }
  }

  async unlink(userId: string, walletId: string) {
    if (this.demoMode.isActive()) return this.demoStore.unlinkWallet(userId, walletId);
    const wallet = await this.prisma.linkedWallet.findFirst({
      where: { id: walletId, userId },
    });
    if (!wallet) throw new NotFoundException("Linked wallet not found");
    await this.prisma.linkedWallet.delete({ where: { id: walletId } });
    await this.invalidateBalancesCache(userId);
    return { ok: true };
  }

  async balances(userId: string, chain?: string): Promise<WalletBalanceResponse[]> {
    if (this.demoMode.isActive()) {
      const rows = this.demoStore.walletBalances(userId);
      const filtered = chain ? rows.filter((r) => r.chain === chain.toLowerCase()) : rows;
      return filtered.map((w) =>
        this.toBalanceResponse({
          id: w.id,
          chain: w.chain,
          address: w.address,
          label: w.label,
          nativeBalance: w.balance.native,
          tokens: [],
        }),
      );
    }

    const cacheKey = `${WALLET_BALANCES_CACHE_PREFIX}${userId}${chain ? `:${chain.toLowerCase()}` : ""}`;
    const cached = await this.redis.client.get(cacheKey);
    if (cached) {
      return JSON.parse(cached) as WalletBalanceResponse[];
    }

    const wallets = await this.prisma.linkedWallet.findMany({
      where: { userId, ...(chain ? { chain: chain.toLowerCase() } : {}) },
      select: WALLET_LIST_SELECT,
    });

    const results = await Promise.all(
      wallets.map(async (w) => {
        const { nativeBalance, tokens } = await this.fetchWalletBalances(w.chain, w.address);
        return this.toBalanceResponse({ ...w, nativeBalance, tokens });
      }),
    );

    await this.redis.client.setex(cacheKey, WALLET_BALANCES_CACHE_TTL_SEC, JSON.stringify(results));

    return results;
  }

  private toBalanceResponse(w: {
    id: string;
    chain: string;
    address: string;
    label: string | null;
    nativeBalance: string;
    tokens: WalletTokenBalance[];
  }): WalletBalanceResponse {
    return {
      id: w.id,
      chain: w.chain,
      address: w.address,
      label: w.label,
      nativeBalance: w.nativeBalance,
      tokens: w.tokens,
      balance: { native: w.nativeBalance, usd: null },
    };
  }

  private async fetchWalletBalances(
    chain: string,
    address: string,
  ): Promise<{ nativeBalance: string; tokens: WalletTokenBalance[] }> {
    const c = chain.toLowerCase();

    try {
      if (isEvmChain(c)) {
        const native = (await fetchEvmNativeBalance(c, address, this.config)) ?? "0";
        const tokens = await fetchEvmTokenBalances(c, address, this.config);
        return { nativeBalance: native, tokens };
      }

      if (c === "ton") {
        const ton = await fetchTonBalance(address);
        return { nativeBalance: ton ?? "0", tokens: [] };
      }

      if (c === "bitcoin") {
        const btc = await fetchBtcBalance(address);
        return { nativeBalance: btc ?? "0", tokens: [] };
      }

      if (c === "solana") {
        const sol = await fetchSolBalance(address);
        return { nativeBalance: sol ?? "0", tokens: [] };
      }

      if (c === "tron") {
        const trx = await fetchTronBalance(address);
        return { nativeBalance: trx ?? "0", tokens: [] };
      }
    } catch {
      /* fall through */
    }

    return { nativeBalance: "0", tokens: [] };
  }

  private async invalidateBalancesCache(userId: string) {
    await this.redis.client.del(`${WALLET_BALANCES_CACHE_PREFIX}${userId}`);
    // Best-effort: per-chain keys are short-lived (30s); full key scan avoided.
  }
}
