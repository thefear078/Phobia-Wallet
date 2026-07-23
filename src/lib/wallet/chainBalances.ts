/**
 * Multi-chain balances via public RPCs / explorers — no commercial API keys.
 */

export type TokenBalance = {
  symbol: string;
  contract: string;
  decimals: number;
  balance: bigint;
};

type CachedBalance = {
  at: number;
  value: unknown;
};

const CACHE_TTL_MS = 30_000;
const balanceCache = new Map<string, CachedBalance>();

export const PUBLIC_RPC: Record<string, string[]> = {
  ethereum: ["https://cloudflare-eth.com", "https://rpc.ankr.com/eth", "https://eth.drpc.org"],
  polygon: ["https://polygon-rpc.com", "https://rpc.ankr.com/polygon"],
  bsc: ["https://bsc-dataseed.binance.org", "https://rpc.ankr.com/bsc"],
  solana: ["https://api.mainnet-beta.solana.com"],
};

/** Well-known ERC-20 contracts on Ethereum mainnet. */
const ERC20_ETH: Array<{ symbol: string; contract: string; decimals: number }> = [
  { symbol: "USDT", contract: "0xdAC17F958D2ee523a2206206994597C13D831ec7", decimals: 6 },
  { symbol: "USDC", contract: "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", decimals: 6 },
];

const BALANCE_OF_SELECTOR = "0x70a08231";

function cacheGet<T>(key: string): T | null {
  const hit = balanceCache.get(key);
  if (!hit) return null;
  if (Date.now() - hit.at > CACHE_TTL_MS) {
    balanceCache.delete(key);
    return null;
  }
  return hit.value as T;
}

function cacheSet(key: string, value: unknown): void {
  balanceCache.set(key, { at: Date.now(), value });
}

async function rpcCall<T>(urls: string[], method: string, params: unknown[]): Promise<T> {
  let lastError: unknown;
  for (const url of urls) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ jsonrpc: "2.0", id: 1, method, params }),
        signal: AbortSignal.timeout(10_000),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = (await res.json()) as { result?: T; error?: { message?: string } };
      if (json.error) throw new Error(json.error.message ?? "RPC error");
      return json.result as T;
    } catch (err) {
      lastError = err;
    }
  }
  throw lastError instanceof Error ? lastError : new Error("All RPCs failed");
}

function encodeBalanceOf(address: string): string {
  const addr = address.replace(/^0x/i, "").toLowerCase().padStart(64, "0");
  return `${BALANCE_OF_SELECTOR}${addr}`;
}

export async function getEthBalance(
  address: string,
  rpc?: string,
): Promise<{ eth: bigint; tokens: TokenBalance[] }> {
  const cacheKey = `eth:${address.toLowerCase()}:${rpc ?? "default"}`;
  const cached = cacheGet<{ eth: bigint; tokens: TokenBalance[] }>(cacheKey);
  if (cached) return cached;

  const urls = rpc ? [rpc] : PUBLIC_RPC.ethereum;
  const hexBal = await rpcCall<string>(urls, "eth_getBalance", [address, "latest"]);
  const eth = BigInt(hexBal);

  const tokens: TokenBalance[] = [];
  for (const token of ERC20_ETH) {
    try {
      const data = await rpcCall<string>(urls, "eth_call", [
        { to: token.contract, data: encodeBalanceOf(address) },
        "latest",
      ]);
      const balance = BigInt(data || "0x0");
      if (balance > 0n) {
        tokens.push({
          symbol: token.symbol,
          contract: token.contract,
          decimals: token.decimals,
          balance,
        });
      }
    } catch {
      /* skip token on failure */
    }
  }

  const result = { eth, tokens };
  cacheSet(cacheKey, result);
  return result;
}

export async function getBtcBalance(
  address: string,
): Promise<{ confirmed: bigint; unconfirmed: bigint }> {
  const cacheKey = `btc:${address}`;
  const cached = cacheGet<{ confirmed: bigint; unconfirmed: bigint }>(cacheKey);
  if (cached) return cached;

  const res = await fetch(`https://blockstream.info/api/address/${encodeURIComponent(address)}`, {
    signal: AbortSignal.timeout(10_000),
  });
  if (!res.ok) throw new Error(`Blockstream HTTP ${res.status}`);
  const data = (await res.json()) as {
    chain_stats?: { funded_txo_sum?: number; spent_txo_sum?: number };
    mempool_stats?: { funded_txo_sum?: number; spent_txo_sum?: number };
  };
  const funded = BigInt(data.chain_stats?.funded_txo_sum ?? 0);
  const spent = BigInt(data.chain_stats?.spent_txo_sum ?? 0);
  const memFunded = BigInt(data.mempool_stats?.funded_txo_sum ?? 0);
  const memSpent = BigInt(data.mempool_stats?.spent_txo_sum ?? 0);
  const result = {
    confirmed: funded - spent,
    unconfirmed: memFunded - memSpent,
  };
  cacheSet(cacheKey, result);
  return result;
}

export async function getSolBalance(
  address: string,
): Promise<{ sol: bigint; tokens: TokenBalance[] }> {
  const cacheKey = `sol:${address}`;
  const cached = cacheGet<{ sol: bigint; tokens: TokenBalance[] }>(cacheKey);
  if (cached) return cached;

  const urls = PUBLIC_RPC.solana;
  const lamports = await rpcCall<number>(urls, "getBalance", [address]);
  const tokenAccounts = await rpcCall<{
    value: Array<{
      account: {
        data: {
          parsed?: {
            info?: {
              mint?: string;
              tokenAmount?: { amount?: string; decimals?: number; uiAmountString?: string };
            };
          };
        };
      };
    }>;
  }>(urls, "getTokenAccountsByOwner", [
    address,
    { programId: "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA" },
    { encoding: "jsonParsed" },
  ]);

  const tokens: TokenBalance[] = [];
  for (const row of tokenAccounts?.value ?? []) {
    const info = row.account?.data?.parsed?.info;
    const amount = info?.tokenAmount?.amount;
    if (!amount || amount === "0") continue;
    tokens.push({
      symbol: info?.mint?.slice(0, 6) ?? "SPL",
      contract: info?.mint ?? "",
      decimals: info?.tokenAmount?.decimals ?? 0,
      balance: BigInt(amount),
    });
  }

  const result = { sol: BigInt(lamports ?? 0), tokens };
  cacheSet(cacheKey, result);
  return result;
}
