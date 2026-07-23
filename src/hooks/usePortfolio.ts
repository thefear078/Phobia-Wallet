import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { api } from "@/lib/api/client";

const CHAIN_SYMBOL: Record<string, string> = {
  ethereum: "ETH",
  bitcoin: "BTC",
  ton: "TON",
  solana: "SOL",
  tron: "TRX",
  polygon: "MATIC",
  bsc: "BNB",
  avalanche: "AVAX",
  // EVM L2s settle in ETH natively
  arbitrum: "ETH",
  optimism: "ETH",
  base: "ETH",
};

const COLORS: Record<string, string> = {
  ETH: "oklch(0.68 0.14 265)",
  BTC: "oklch(0.75 0.18 60)",
  TON: "oklch(0.72 0.16 235)",
  USDT: "oklch(0.72 0.16 155)",
  SOL: "oklch(0.65 0.18 300)",
  TRX: "oklch(0.64 0.2 25)",
  MATIC: "oklch(0.62 0.2 290)",
  BNB: "oklch(0.78 0.16 85)",
  AVAX: "oklch(0.63 0.22 20)",
};

type WalletBalanceRow = {
  id: string;
  chain: string;
  address: string;
  balance?: { native?: string; usd?: number | null };
  nativeBalance?: string;
};

function readNativeBalance(row: WalletBalanceRow): number {
  const raw = row.balance?.native ?? row.nativeBalance ?? "0";
  const parsed = Number.parseFloat(raw);
  return Number.isFinite(parsed) ? parsed : 0;
}

const DEFAULT_USD_UAH = 41.5;

export type PortfolioAsset = {
  symbol: string;
  name: string;
  balance: number;
  price: number;
  change24h: number;
  color: string;
  source: "wallet" | "market";
  valueUsd: number;
  valueUah: number;
};

export function usePortfolio() {
  const wallets = useQuery({
    queryKey: ["wallets"],
    queryFn: () => api.listWallets(),
    retry: 1,
  });

  const balances = useQuery({
    queryKey: ["wallet-balances"],
    queryFn: () => api.walletBalances(),
    retry: 1,
    enabled: (wallets.data?.length ?? 0) > 0,
  });

  const banks = useQuery({
    queryKey: ["bank-accounts"],
    queryFn: () => api.listBankAccounts(),
    retry: 1,
  });

  const market = useQuery({
    queryKey: ["market-rates"],
    queryFn: () => api.marketRates(),
    staleTime: 120_000,
    retry: 1,
  });

  const usdUah = useQuery({
    queryKey: ["rate", "USD", "UAH"],
    queryFn: () => api.getRate("USD", "UAH"),
    staleTime: 300_000,
    retry: 1,
  });

  const uahPerUsd = usdUah.data?.rate && usdUah.data.rate > 0 ? usdUah.data.rate : DEFAULT_USD_UAH;

  const assets = useMemo((): PortfolioAsset[] => {
    const priceMap = new Map(market.data?.map((m) => [m.symbol, m]) ?? []);
    const bySymbol = new Map<string, PortfolioAsset>();

    for (const row of balances.data ?? []) {
      const symbol = CHAIN_SYMBOL[row.chain] ?? row.chain.toUpperCase();
      const native = readNativeBalance(row as WalletBalanceRow);
      if (native <= 0) continue;
      const m = priceMap.get(symbol);
      const price = m?.price ?? row.balance?.usd ?? 0;
      const valueUsd = native * price;
      const existing = bySymbol.get(symbol);
      if (existing) {
        const balance = existing.balance + native;
        const valueUsdTotal = balance * price;
        bySymbol.set(symbol, {
          ...existing,
          balance,
          valueUsd: valueUsdTotal,
          valueUah: valueUsdTotal * uahPerUsd,
        });
      } else {
        bySymbol.set(symbol, {
          symbol,
          name: symbol,
          balance: native,
          price,
          change24h: m?.change24h ?? 0,
          color: COLORS[symbol] ?? "oklch(0.7 0.1 200)",
          source: "wallet",
          valueUsd,
          valueUah: valueUsd * uahPerUsd,
        });
      }
    }

    return [...bySymbol.values()].sort((a, b) => b.valueUsd - a.valueUsd);
  }, [balances.data, market.data, uahPerUsd]);

  const totalUsd = assets.reduce((s, a) => s + a.valueUsd, 0);
  const totalUah = totalUsd * uahPerUsd;
  const avgChange =
    assets.length > 0 ? assets.reduce((s, a) => s + a.change24h, 0) / assets.length : 0;

  const isLoading = wallets.isLoading || balances.isLoading || market.isLoading || usdUah.isLoading;
  const hasLinks = (wallets.data?.length ?? 0) > 0 || (banks.data?.length ?? 0) > 0;
  const error = wallets.error ?? balances.error ?? market.error ?? usdUah.error;

  return {
    assets,
    totalUsd,
    totalUah,
    uahPerUsd,
    avgChange,
    wallets: wallets.data ?? [],
    banks: banks.data ?? [],
    hasLinks,
    isLoading,
    error,
    refetch: () => {
      void wallets.refetch();
      void balances.refetch();
      void banks.refetch();
      void market.refetch();
      void usdUah.refetch();
    },
  };
}
