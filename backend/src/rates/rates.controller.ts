import { Controller, Get, Param, Query } from "@nestjs/common";
import { RatesService } from "./rates.service";

@Controller("rates")
export class RatesController {
  constructor(private rates: RatesService) {}

  @Get()
  getRate(@Query("base") base: string, @Query("quote") quote: string) {
    return this.rates.getRate(base, quote);
  }

  @Get("market")
  market() {
    return this.rates.getMarketSnapshot();
  }

  /** Legacy flat list — kept for older clients. */
  @Get("market/list")
  marketList() {
    return this.rates.getMarketPrices();
  }

  @Get("pairs")
  pairs() {
    return this.rates.getMarketPairs();
  }

  @Get("chart")
  chart(@Query("symbol") symbol: string, @Query("days") days?: string) {
    return this.rates.getMarketChart(symbol, days ? Number(days) : 7);
  }

  @Get("sparkline/:symbol")
  sparkline(@Param("symbol") symbol: string) {
    return this.rates.getSparkline(symbol);
  }

  @Get("convert")
  convert(@Query("from") from: string, @Query("to") to: string, @Query("amount") amount?: string) {
    return this.rates.convert(from, to, amount ? Number(amount) : 0);
  }
}
