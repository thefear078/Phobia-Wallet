import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api/client";

export function useMarketRates() {
  const market = useQuery({
    queryKey: ["market-rates"],
    queryFn: () => api.marketRates(),
    staleTime: 30_000,
    retry: 1,
  });

  const pairs = useQuery({
    queryKey: ["market-pairs"],
    queryFn: () => api.marketPairs(),
    staleTime: 30_000,
    retry: 1,
  });

  return { market, pairs };
}

export function useMarketChart(symbol: string, days = 7) {
  return useQuery({
    queryKey: ["market-chart", symbol, days],
    queryFn: () => api.marketChart(symbol, days),
    staleTime: 300_000,
    retry: 1,
    enabled: Boolean(symbol),
  });
}
