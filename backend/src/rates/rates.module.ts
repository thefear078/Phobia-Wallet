import { Module } from "@nestjs/common";
import { RatesController } from "./rates.controller";
import { RatesService } from "./rates.service";
import { CoinGeckoRatesProvider } from "./providers/coingecko.provider";
import { CustomHttpRatesProvider } from "./providers/custom-http.provider";
import { BinanceRatesProvider } from "./providers/binance.provider";

@Module({
  controllers: [RatesController],
  providers: [RatesService, CoinGeckoRatesProvider, CustomHttpRatesProvider, BinanceRatesProvider],
  exports: [RatesService],
})
export class RatesModule {}
