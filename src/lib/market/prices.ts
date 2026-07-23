import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type MarketSnapshot } from "@/lib/api/client";

export function useMarketPrices() {
  const q = useQuery({
    queryKey: ["market-snapshot"],
    queryFn: () => api.marketSnapshot(),
    staleTime: 30_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: 1,
  });

  return {
    prices: q.data?.prices ?? ({} as MarketSnapshot["prices"]),
    updatedAt: q.data?.updatedAt,
    isLoading: q.isLoading,
    error: q.error,
    refetch: q.refetch,
    data: q.data,
  };
}

export function useSparkline(symbol: string) {
  return useQuery({
    queryKey: ["sparkline", symbol],
    queryFn: () => api.marketSparkline(symbol),
    staleTime: 300_000,
    retry: 1,
    enabled: Boolean(symbol),
  });
}

export function useConvert(from: string, to: string, amount: number) {
  const [debounced, setDebounced] = useState(amount);

  useEffect(() => {
    const id = window.setTimeout(() => setDebounced(amount), 500);
    return () => window.clearTimeout(id);
  }, [amount]);

  return useQuery({
    queryKey: ["convert", from, to, debounced],
    queryFn: () => api.convertRate(from, to, debounced),
    staleTime: 15_000,
    retry: 1,
    enabled: Boolean(from && to && Number.isFinite(debounced) && debounced >= 0),
  });
}
