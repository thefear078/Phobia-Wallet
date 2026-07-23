import { Injectable } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import type { AssetPrice, IRatesProvider, MarketChart, RateQuote } from "../rates.types";

const TIMEOUT_MS = 8000;

/**
 * User-supplied rate endpoint — lets an operator self-host rates (or point at a
 * local node / their own aggregator) with zero commercial dependencies.
 *
 * Enable with RATES_PROVIDER=custom and RATES_URL=https://my-host/rates
 * Expected shapes:
 *   GET {RATES_URL}/market            -> [{ symbol, price, change24h }]
 *   GET {RATES_URL}/rate?base=&quote= -> { rate }
 *   GET {RATES_URL}/chart?symbol=&days= -> { prices: number[] }
 */
@Injectable()
export class CustomHttpRatesProvider implements IRatesProvider {
  readonly id = "custom";

  constructor(private config: ConfigService) {}

  private url(): string | undefined {
    return this.config.get<string>("RATES_URL")?.trim().replace(/\/$/, "") || undefined;
  }

  isEnabled(): boolean {
    const provider = (this.config.get<string>("RATES_PROVIDER") ?? "coingecko").toLowerCase();
    return (provider === "custom" || provider === "auto") && Boolean(this.url());
  }

  private async getJson<T>(path: string): Promise<T> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
    try {
      const res = await fetch(`${this.url()}${path}`, {
        signal: controller.signal,
        headers: { Accept: "application/json" },
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  async getMarketPrices(symbols: string[]): Promise<AssetPrice[]> {
    const rows = await this.getJson<AssetPrice[]>("/market");
    const wanted = new Set(symbols.map((s) => s.toUpperCase()));
    return rows
      .filter((r) => wanted.has(r.symbol?.toUpperCase()))
      .map((r) => ({
        symbol: r.symbol.toUpperCase(),
        price: Number(r.price) || 0,
        change24h: Number(r.change24h) || 0,
      }));
  }

  async getRate(base: string, quote: string): Promise<RateQuote | null> {
    const data = await this.getJson<{ rate?: number }>(
      `/rate?base=${encodeURIComponent(base)}&quote=${encodeURIComponent(quote)}`,
    );
    if (typeof data.rate !== "number") return null;
    return {
      base: base.toUpperCase(),
      quote: quote.toUpperCase(),
      rate: data.rate,
      at: Date.now(),
    };
  }

  async getMarketChart(symbol: string, days: number): Promise<MarketChart | null> {
    const data = await this.getJson<{ prices?: number[] }>(
      `/chart?symbol=${encodeURIComponent(symbol)}&days=${days}`,
    );
    if (!Array.isArray(data.prices)) return null;
    return { symbol: symbol.toUpperCase(), days, prices: data.prices };
  }
}
