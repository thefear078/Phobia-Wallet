import { JsonRpcProvider, formatEther, formatUnits, Contract } from "ethers";
import { ConfigService } from "@nestjs/config";
import { isEvmChain } from "./address.validation";

export type EvmTokenBalance = {
  symbol: string;
  balance: string;
  contractAddress: string;
};

const CURRENCY_DECIMALS: Record<number, string> = {
  980: "UAH",
  840: "USD",
  978: "EUR",
};

export function monobankCurrencyCode(code: number): string {
  return CURRENCY_DECIMALS[code] ?? String(code);
}

const ALCHEMY_NETWORK_SLUG: Record<string, string> = {
  ethereum: "eth-mainnet",
  polygon: "polygon-mainnet",
  bsc: "bnb-mainnet",
  arbitrum: "arb-mainnet",
  optimism: "opt-mainnet",
  base: "base-mainnet",
  avalanche: "avax-mainnet",
};

/**
 * Public RPC failover lists — Umbrella must work with no API keys at all.
 * Users can override per chain with EVM_RPC_<CHAIN> (comma-separated).
 */
const PUBLIC_RPC: Record<string, string[]> = {
  ethereum: [
    "https://ethereum-rpc.publicnode.com",
    "https://eth.llamarpc.com",
    "https://cloudflare-eth.com",
    "https://rpc.ankr.com/eth",
  ],
  polygon: ["https://polygon-bor-rpc.publicnode.com", "https://polygon-rpc.com"],
  bsc: ["https://bsc-rpc.publicnode.com", "https://bsc-dataseed.binance.org"],
  arbitrum: ["https://arbitrum-one-rpc.publicnode.com", "https://arb1.arbitrum.io/rpc"],
  optimism: ["https://optimism-rpc.publicnode.com", "https://mainnet.optimism.io"],
  base: ["https://base-rpc.publicnode.com", "https://mainnet.base.org"],
  avalanche: [
    "https://avalanche-c-chain-rpc.publicnode.com",
    "https://api.avax.network/ext/bc/C/rpc",
  ],
};

/** Well-known stablecoin rails per chain — used for token balances on any plain RPC. */
const TOKEN_REGISTRY: Record<
  string,
  Array<{ symbol: string; address: string; decimals: number }>
> = {
  ethereum: [
    { symbol: "USDT", address: "0xdAC17F958D2ee523a2206206994597C13D831ec7", decimals: 6 },
    { symbol: "USDC", address: "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", decimals: 6 },
  ],
  polygon: [
    { symbol: "USDT", address: "0xc2132D05D31c914a87C6611C10748AEb04B58e8F", decimals: 6 },
    { symbol: "USDC", address: "0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359", decimals: 6 },
  ],
  bsc: [{ symbol: "USDT", address: "0x55d398326f99059fF775485246999027B3197955", decimals: 18 }],
  arbitrum: [
    { symbol: "USDT", address: "0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9", decimals: 6 },
    { symbol: "USDC", address: "0xaf88d065e77c8cC2239327C5EDb3A432268e5831", decimals: 6 },
  ],
  optimism: [
    { symbol: "USDT", address: "0x94b008aA00579c1307B0EF2c499aD98a8ce58e58", decimals: 6 },
  ],
  base: [{ symbol: "USDC", address: "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", decimals: 6 }],
};

const ERC20_BALANCE_ABI = ["function balanceOf(address) view returns (uint256)"];

/** Ordered RPC candidates: user override → Alchemy (optional) → public failover list. */
export function resolveEvmRpcUrls(chain: string, config: ConfigService): string[] {
  const c = chain.toLowerCase();
  const urls: string[] = [];

  const override = config.get<string>(`EVM_RPC_${c.toUpperCase()}`)?.trim();
  if (override)
    urls.push(
      ...override
        .split(",")
        .map((u) => u.trim())
        .filter(Boolean),
    );

  const dedicated = config.get<string>("ALCHEMY_RPC_URL")?.trim();
  if (c === "ethereum" && dedicated) urls.push(dedicated);

  const apiKey = config.get<string>("ALCHEMY_API_KEY")?.trim();
  const slug = ALCHEMY_NETWORK_SLUG[c];
  if (apiKey && slug) urls.push(`https://${slug}.g.alchemy.com/v2/${apiKey}`);

  urls.push(...(PUBLIC_RPC[c] ?? []));
  return [...new Set(urls)];
}

/** Back-compat single-URL resolver (first candidate). */
export function resolveEvmRpcUrl(chain: string, config: ConfigService): string | null {
  return resolveEvmRpcUrls(chain, config)[0] ?? null;
}

/** Run `fn` against each RPC candidate until one succeeds. */
async function withRpcFailover<T>(
  chain: string,
  config: ConfigService,
  fn: (provider: JsonRpcProvider) => Promise<T>,
): Promise<T | null> {
  for (const url of resolveEvmRpcUrls(chain, config)) {
    try {
      return await fn(new JsonRpcProvider(url));
    } catch {
      // try the next endpoint
    }
  }
  return null;
}

export async function fetchEvmNativeBalance(
  chain: string,
  address: string,
  config: ConfigService,
): Promise<string | null> {
  return withRpcFailover(chain, config, async (provider) => {
    const wei = await provider.getBalance(address);
    return formatEther(wei);
  });
}

/**
 * Token balances via plain `eth_call balanceOf` over the registry — works on any
 * public RPC (no Alchemy enhanced API required).
 */
export async function fetchEvmTokenBalances(
  chain: string,
  address: string,
  config: ConfigService,
): Promise<EvmTokenBalance[]> {
  const c = chain.toLowerCase();
  if (!isEvmChain(c)) return [];
  const registry = TOKEN_REGISTRY[c];
  if (!registry?.length) return [];

  const tokens = await withRpcFailover(c, config, async (provider) => {
    const out: EvmTokenBalance[] = [];
    for (const token of registry) {
      try {
        const contract = new Contract(token.address, ERC20_BALANCE_ABI, provider);
        const raw = (await contract.balanceOf(address)) as bigint;
        if (raw <= 0n) continue;
        out.push({
          symbol: token.symbol,
          balance: formatUnits(raw, token.decimals),
          contractAddress: token.address,
        });
      } catch {
        // skip this token, keep the rest
      }
    }
    return out;
  });

  return tokens ?? [];
}
