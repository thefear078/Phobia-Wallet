import { Injectable, Logger } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { RedisService } from "../redis/redis.service";
import { DemoModeService } from "../demo/demo-mode.service";
import { DemoStoreService } from "../demo/demo-store.service";
import { CoinGeckoRatesProvider, CoinGeckoRateLimitError } from "./providers/coingecko.provider";
import { CustomHttpRatesProvider } from "./providers/custom-http.provider";
import { BinanceRatesProvider } from "./providers/binance.provider";
import {
  MARKET_SYMBOLS,
  type AssetPrice,
  type ConvertResult,
  type IRatesProvider,
  type MarketChart,
  type MarketSnapshot,
  type MultiFiatPrice,
  type SparklineResult,
} from "./rates.types";

const FRESH_TTL_SEC = 30;
const CHART_TTL_SEC = 300;
/** Last-known-good cache — served when every provider fails (offline-tolerant). */
const STALE_TTL_SEC = 7 * 24 * 60 * 60;

/** Rough USD→UAH / USD→EUR when Binance-only fallback has no fiat pairs. */
const FALLBACK_USD_UAH = 41.5;
const FALLBACK_USD_EUR = 0.92;

const MARKET_PAIRS: Array<{ base: string; quote: string }> = [
  { base: "BTC", quote: "UAH" },
  { base: "ETH", quote: "UAH" },
  { base: "USDT", quote: "UAH" },
  { base: "BTC", quote: "USD" },
  { base: "ETH", quote: "USD" },
];

const FIAT = new Set(["USD", "UAH", "EUR", "USDT"]);

@Injectable()
export class RatesService {
  private readonly logger = new Logger(RatesService.name);

  constructor(
    private config: ConfigService,
    private redis: RedisService,
    private demoMode: DemoModeService,
    private demoStore: DemoStoreService,
    private coingecko: CoinGeckoRatesProvider,
    private custom: CustomHttpRatesProvider,
    private binance: BinanceRatesProvider,
  ) {}

  /** Enabled adapters in priority order. Empty list = cache/manual only. */
  private providers(): IRatesProvider[] {
    return [this.custom, this.coingecko, this.binance].filter((p) => p.isEnabled());
  }

  private async readCache<T>(key: string): Promise<T | null> {
    try {
      const raw = await this.redis.client.get(key);
      return raw ? (JSON.parse(raw) as T) : null;
    } catch {
      return null;
    }
  }

  private async writeCache(key: string, value: unknown, ttl: number): Promise<void> {
    try {
      await this.redis.client.setex(key, ttl, JSON.stringify(value));
    } catch {
      /* cache is best-effort */
    }
  }

  /**
   * Fresh cache → provider chain → stale cache → fallback.
   * Never throws: a rates outage must not take the wallet down.
   */
  private async resolve<T>(
    freshKey: string,
    staleKey: string,
    ttl: number,
    attempt: (p: IRatesProvider) => Promise<T | null>,
    fallback: T,
  ): Promise<T> {
    const fresh = await this.readCache<T>(freshKey);
    if (fresh) return fresh;

    for (const provider of this.providers()) {
      try {
        const value = await attempt(provider);
        if (value === null || value === undefined) continue;
        await this.writeCache(freshKey, value, ttl);
        await this.writeCache(staleKey, value, STALE_TTL_SEC);
        return value;
      } catch (error) {
        this.logger.warn(`Rates provider "${provider.id}" failed: ${String(error)}`);
        if (error instanceof CoinGeckoRateLimitError) {
          // Skip remaining CoinGecko attempts; Binance is next in the chain.
          continue;
        }
      }
    }

    const stale = await this.readCache<T>(staleKey);
    if (stale) {
      this.logger.warn(`Serving stale rates for ${freshKey} — all providers unavailable`);
      return stale;
    }
    this.logger.warn(`No rates available for ${freshKey} — serving empty fallback`);
    return fallback;
  }

  async getRate(base: string, quote: string) {
    if (this.demoMode.isActive()) return this.demoStore.getRate(base, quote);
    const b = base.toUpperCase();
    const q = quote.toUpperCase();
    return this.resolve(
      `rate:${b}:${q}`,
      `rate:stale:${b}:${q}`,
      FRESH_TTL_SEC,
      (p) => p.getRate(b, q),
      { base: b, quote: q, rate: 0, at: Date.now() },
    );
  }

  /** Legacy array shape used by older clients. */
  async getMarketPrices(): Promise<AssetPrice[]> {
    if (this.demoMode.isActive()) return this.demoStore.marketRates();
    const snap = await this.getMarketSnapshot();
    return Object.entries(snap.prices).map(([symbol, p]) => ({
      symbol,
      price: p.usd,
      change24h: p.change24h,
    }));
  }

  /**
   * Primary: CoinGecko multi-fiat simple/price.
   * Fallback (429 / failure): Binance USDT tickers + fiat cross estimates.
   * Redis key `rates:market`, TTL 30s.
   */
  async getMarketSnapshot(): Promise<MarketSnapshot> {
    if (this.demoMode.isActive()) {
      const rows = await this.demoStore.marketRates();
      const prices: Record<string, MultiFiatPrice> = {};
      for (const row of rows) {
        prices[row.symbol] = {
          usd: row.price,
          uah: row.price * FALLBACK_USD_UAH,
          eur: row.price * FALLBACK_USD_EUR,
          change24h: row.change24h,
        };
      }
      return { prices, updatedAt: Date.now() };
    }

    const cached = await this.readCache<MarketSnapshot>("rates:market");
    if (cached) return cached;

    try {
      if (this.coingecko.isEnabled()) {
        const prices = await this.coingecko.getSimpleMultiFiat();
        if (Object.keys(prices).length) {
          const snap: MarketSnapshot = { prices, updatedAt: Date.now() };
          await this.writeCache("rates:market", snap, FRESH_TTL_SEC);
          await this.writeCache("rates:market:stale", snap, STALE_TTL_SEC);
          return snap;
        }
      }
    } catch (error) {
      this.logger.warn(`CoinGecko market snapshot failed: ${String(error)}`);
    }

    try {
      const rows = await this.binance.getMarketPrices(MARKET_SYMBOLS);
      const prices: Record<string, MultiFiatPrice> = {};
      for (const row of rows) {
        prices[row.symbol] = {
          usd: row.price,
          uah: row.price * FALLBACK_USD_UAH,
          eur: row.price * FALLBACK_USD_EUR,
          change24h: row.change24h,
        };
      }
      if (Object.keys(prices).length) {
        const snap: MarketSnapshot = { prices, updatedAt: Date.now() };
        await this.writeCache("rates:market", snap, FRESH_TTL_SEC);
        await this.writeCache("rates:market:stale", snap, STALE_TTL_SEC);
        return snap;
      }
    } catch (error) {
      this.logger.warn(`Binance market fallback failed: ${String(error)}`);
    }

    const stale = await this.readCache<MarketSnapshot>("rates:market:stale");
    if (stale) return stale;
    return { prices: {}, updatedAt: Date.now() };
  }

  async getMarketPairs() {
    return Promise.all(
      MARKET_PAIRS.map(async ({ base, quote }) => {
        const row = await this.getRate(base, quote);
        return { ...row, pair: `${base}/${quote}` };
      }),
    );
  }

  async getMarketChart(symbol: string, days = 7): Promise<MarketChart> {
    const upper = symbol.toUpperCase();
    if (this.demoMode.isActive()) return this.demoStore.marketChart(upper, days);
    return this.resolve<MarketChart>(
      `rates:chart:${upper}:${days}`,
      `rates:chart:stale:${upper}:${days}`,
      CHART_TTL_SEC,
      async (p) => {
        const chart = await p.getMarketChart(upper, days);
        return chart?.prices.length ? chart : null;
      },
      { symbol: upper, days, prices: [] },
    );
  }

  /** 7-day sparkline with timestamps — Redis TTL 5 min. */
  async getSparkline(symbol: string): Promise<SparklineResult> {
    const upper = symbol.toUpperCase();
    const chart = await this.getMarketChart(upper, 7);
    const timestamps =
      chart.timestamps ??
      (() => {
        const now = Date.now();
        const n = chart.prices.length;
        if (n < 2) return chart.prices.map(() => now);
        const span = 7 * 86_400_000;
        return chart.prices.map((_, i) => now - span + (span * i) / (n - 1));
      })();
    return { symbol: upper, prices: chart.prices, timestamps };
  }

  async convert(from: string, to: string, amount: number): Promise<ConvertResult> {
    const f = from.toUpperCase();
    const t = to.toUpperCase();
    if (!Number.isFinite(amount) || amount < 0) {
      return { result: 0, rate: 0, fee: 0 };
    }
    if (f === t) return { result: amount, rate: 1, fee: 0 };

    const snap = await this.getMarketSnapshot();

    const usdOf = (sym: string): number | null => {
      if (sym === "USD" || sym === "USDT") return 1;
      if (sym === "UAH") {
        const usdt = snap.prices.USDT?.uah;
        return usdt && usdt > 0 ? 1 / usdt : 1 / FALLBACK_USD_UAH;
      }
      if (sym === "EUR") {
        const usdt = snap.prices.USDT?.eur;
        return usdt && usdt > 0 ? 1 / usdt : 1 / FALLBACK_USD_EUR;
      }
      const row = snap.prices[sym];
      return row?.usd && row.usd > 0 ? row.usd : null;
    };

    const fiatAmountInUsd = (sym: string, qty: number): number | null => {
      if (FIAT.has(sym) || snap.prices[sym]) {
        if (sym === "USD" || sym === "USDT") return qty;
        if (sym === "UAH") {
          const uah = snap.prices.USDT?.uah || FALLBACK_USD_UAH;
          return qty / uah;
        }
        if (sym === "EUR") {
          const eur = snap.prices.USDT?.eur || FALLBACK_USD_EUR;
          return qty / eur;
        }
        const p = snap.prices[sym];
        return p ? qty * p.usd : null;
      }
      return null;
    };

    // Prefer direct quote when available
    if (!FIAT.has(f) && (t === "USD" || t === "USDT" || t === "UAH" || t === "EUR")) {
      const row = snap.prices[f];
      if (row) {
        const rate = t === "USD" || t === "USDT" ? row.usd : t === "UAH" ? row.uah : row.eur;
        return { result: amount * rate, rate, fee: 0 };
      }
    }

    const fromUsd = fiatAmountInUsd(f, 1);
    const toUsd = usdOf(t);
    if (fromUsd == null || toUsd == null || toUsd <= 0) {
      const quote = await this.getRate(f, t);
      return { result: amount * (quote.rate || 0), rate: quote.rate || 0, fee: 0 };
    }

    // from → USD → to
    const amountUsd = fiatAmountInUsd(f, amount) ?? 0;
    let result: number;
    if (t === "USD" || t === "USDT") result = amountUsd;
    else if (t === "UAH") result = amountUsd * (snap.prices.USDT?.uah || FALLBACK_USD_UAH);
    else if (t === "EUR") result = amountUsd * (snap.prices.USDT?.eur || FALLBACK_USD_EUR);
    else result = amountUsd / toUsd;

    const rate = amount > 0 ? result / amount : 0;
    return { result, rate, fee: 0 };
  }
}
