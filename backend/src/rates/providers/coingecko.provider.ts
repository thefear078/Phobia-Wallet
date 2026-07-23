import { Injectable } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import {
  COIN_IDS,
  MARKET_COINS,
  type AssetPrice,
  type IRatesProvider,
  type MarketChart,
  type RateQuote,
} from "../rates.types";

const COINGECKO_DEFAULT = "https://api.coingecko.com/api/v3";
const TIMEOUT_MS = 8000;

export class CoinGeckoRateLimitError extends Error {
  constructor(message = "CoinGecko rate limited (429)") {
    super(message);
    this.name = "CoinGeckoRateLimitError";
  }
}

async function getJson<T>(url: string, apiKey?: string): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(url, {
      signal: controller.signal,
      headers: {
        Accept: "application/json",
        ...(apiKey ? { "x-cg-demo-api-key": apiKey } : {}),
      },
    });
    if (res.status === 429) throw new CoinGeckoRateLimitError();
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return (await res.json()) as T;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * CoinGecko adapter — OPTIONAL. Disabled by setting RATES_PROVIDER to something
 * else (or "none"). No API key required for the public endpoint.
 */
@Injectable()
export class CoinGeckoRatesProvider implements IRatesProvider {
  readonly id = "coingecko";

  constructor(private config: ConfigService) {}

  isEnabled(): boolean {
    const provider = (this.config.get<string>("RATES_PROVIDER") ?? "coingecko").toLowerCase();
    return provider === "coingecko" || provider === "auto";
  }

  private base(): string {
    return this.config.get<string>("COINGECKO_API_URL") ?? COINGECKO_DEFAULT;
  }

  private apiKey(): string | undefined {
    return this.config.get<string>("COINGECKO_API_KEY")?.trim() || undefined;
  }

  async getMarketPrices(symbols: string[]): Promise<AssetPrice[]> {
    const ids = symbols
      .map((s) => COIN_IDS[s.toUpperCase()])
      .filter(Boolean)
      .join(",");
    if (!ids) return [];
    const url = `${this.base()}/simple/price?ids=${ids}&vs_currencies=usd,uah,eur&include_24hr_change=true`;
    const data = await getJson<
      Record<
        string,
        {
          usd?: number;
          uah?: number;
          eur?: number;
          usd_24h_change?: number;
        }
      >
    >(url, this.apiKey());

    const byId = new Map(MARKET_COINS.map((c) => [c.id, c.symbol]));
    const out: AssetPrice[] = [];
    for (const [id, row] of Object.entries(data)) {
      const symbol =
        byId.get(id) ??
        Object.entries(COIN_IDS).find(([, coinId]) => coinId === id)?.[0] ??
        id.toUpperCase();
      if (!symbols.map((s) => s.toUpperCase()).includes(symbol.toUpperCase())) continue;
      out.push({
        symbol: symbol.toUpperCase(),
        price: row.usd ?? 0,
        change24h: row.usd_24h_change ?? 0,
      });
    }
    return out;
  }

  /** Full multi-fiat snapshot for /rates/market. */
  async getSimpleMultiFiat(): Promise<
    Record<string, { usd: number; uah: number; eur: number; change24h: number }>
  > {
    const ids = MARKET_COINS.map((c) => c.id).join(",");
    const url = `${this.base()}/simple/price?ids=${ids}&vs_currencies=usd,uah,eur&include_24hr_change=true`;
    const data = await getJson<
      Record<string, { usd?: number; uah?: number; eur?: number; usd_24h_change?: number }>
    >(url, this.apiKey());

    const prices: Record<string, { usd: number; uah: number; eur: number; change24h: number }> = {};
    for (const coin of MARKET_COINS) {
      const row = data[coin.id];
      if (!row) continue;
      prices[coin.symbol] = {
        usd: row.usd ?? 0,
        uah: row.uah ?? 0,
        eur: row.eur ?? 0,
        change24h: row.usd_24h_change ?? 0,
      };
    }
    return prices;
  }

  async getRate(base: string, quote: string): Promise<RateQuote | null> {
    const baseId = COIN_IDS[base.toUpperCase()] ?? base.toLowerCase();
    const vs = quote.toLowerCase();
    const url = `${this.base()}/simple/price?ids=${baseId}&vs_currencies=${vs}`;
    const data = await getJson<Record<string, Record<string, number>>>(url, this.apiKey());
    const rate = data[baseId]?.[vs];
    if (typeof rate !== "number") return null;
    return { base: base.toUpperCase(), quote: quote.toUpperCase(), rate, at: Date.now() };
  }

  async getMarketChart(symbol: string, days: number): Promise<MarketChart | null> {
    const coinId = COIN_IDS[symbol.toUpperCase()];
    if (!coinId) return null;
    const url = `${this.base()}/coins/${coinId}/market_chart?vs_currency=usd&days=${days}`;
    const data = await getJson<{ prices: Array<[number, number]> }>(url, this.apiKey());
    const pairs = data.prices ?? [];
    return {
      symbol: symbol.toUpperCase(),
      days,
      prices: pairs.map(([, price]) => price),
      timestamps: pairs.map(([ts]) => ts),
    };
  }
}
