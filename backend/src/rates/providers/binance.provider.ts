import { Injectable } from "@nestjs/common";
import type { AssetPrice, IRatesProvider, MarketChart, RateQuote } from "../rates.types";

const BINANCE_API = "https://api.binance.com/api/v3";
const TIMEOUT_MS = 8000;

/** Binance symbols for USD-ish quotes (USDT pairs). */
const BINANCE_PAIR: Record<string, string> = {
  BTC: "BTCUSDT",
  ETH: "ETHUSDT",
  SOL: "SOLUSDT",
  BNB: "BNBUSDT",
  MATIC: "MATICUSDT",
  XMR: "XMRUSDT",
  TON: "TONUSDT",
  USDC: "USDCUSDT",
  USDT: "USDTUSDT",
};

async function getJson<T>(url: string): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(url, {
      signal: controller.signal,
      headers: { Accept: "application/json" },
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return (await res.json()) as T;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Binance public ticker fallback — used when CoinGecko returns 429 / fails.
 * Prices are USDT-denominated (≈ USD). Fiat cross-rates come from the orchestrator.
 */
@Injectable()
export class BinanceRatesProvider implements IRatesProvider {
  readonly id = "binance";

  isEnabled(): boolean {
    return true;
  }

  async getMarketPrices(symbols: string[]): Promise<AssetPrice[]> {
    const tickers = await getJson<
      Array<{ symbol: string; lastPrice: string; priceChangePercent: string }>
    >(`${BINANCE_API}/ticker/24hr`);
    const byPair = new Map(tickers.map((t) => [t.symbol, t]));
    const out: AssetPrice[] = [];

    for (const sym of symbols) {
      const upper = sym.toUpperCase();
      if (upper === "USDT") {
        out.push({ symbol: "USDT", price: 1, change24h: 0 });
        continue;
      }
      const pair = BINANCE_PAIR[upper];
      if (!pair) continue;
      const row = byPair.get(pair);
      if (!row) continue;
      out.push({
        symbol: upper,
        price: Number.parseFloat(row.lastPrice) || 0,
        change24h: Number.parseFloat(row.priceChangePercent) || 0,
      });
    }
    return out;
  }

  async getRate(base: string, quote: string): Promise<RateQuote | null> {
    const b = base.toUpperCase();
    const q = quote.toUpperCase();
    if (b === q) return { base: b, quote: q, rate: 1, at: Date.now() };
    const prices = await this.getMarketPrices([b, q === "USD" || q === "USDT" ? "USDT" : q]);
    const baseRow = prices.find((p) => p.symbol === b);
    if (!baseRow) return null;
    if (q === "USD" || q === "USDT") {
      return { base: b, quote: q, rate: baseRow.price, at: Date.now() };
    }
    const quoteRow = prices.find((p) => p.symbol === q);
    if (!quoteRow || quoteRow.price <= 0) return null;
    return { base: b, quote: q, rate: baseRow.price / quoteRow.price, at: Date.now() };
  }

  async getMarketChart(_symbol: string, _days: number): Promise<MarketChart | null> {
    // Binance klines could fill this; leave null so CoinGecko chart cache is preferred.
    return null;
  }
}
