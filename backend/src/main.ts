import { ValidationPipe } from "@nestjs/common";
import { NestFactory } from "@nestjs/core";
import { ConfigService } from "@nestjs/config";
import cookieParser from "cookie-parser";
import helmet from "helmet";
import type { NextFunction, Request, Response } from "express";
import { AppModule } from "./app.module";
import { validateProductionEnv } from "./common/env.validation";
import { createAppLogger } from "./common/logger/app-logger";
import { PrivacyModeMiddleware } from "./common/privacy-mode.middleware";

async function bootstrap() {
  const logger = createAppLogger();
  const app = await NestFactory.create(AppModule, {
    rawBody: true,
    logger,
  });
  const config = app.get(ConfigService);

  validateProductionEnv(config);

  app.use(
    helmet({
      contentSecurityPolicy: {
        directives: {
          defaultSrc: ["'self'"],
          scriptSrc: ["'self'"],
          styleSrc: ["'self'", "'unsafe-inline'"],
          imgSrc: ["'self'", "data:", "https:"],
          connectSrc: [
            "'self'",
            "wss:",
            "https://api.coingecko.com",
            "https://api.monobank.ua",
            "https://blockstream.info",
            "https://api.mainnet-beta.solana.com",
          ],
          fontSrc: ["'self'"],
          objectSrc: ["'none'"],
          frameAncestors: ["'none'"],
          upgradeInsecureRequests: [],
        },
      },
      hsts: { maxAge: 31_536_000, includeSubDomains: true },
      frameguard: { action: "deny" },
      noSniff: true,
      referrerPolicy: { policy: "no-referrer" },
      crossOriginEmbedderPolicy: false,
    }),
  );
  app.use(cookieParser());

  const privacy = new PrivacyModeMiddleware();
  app.use((req: Request, res: Response, next: NextFunction) => privacy.use(req, res, next));

  const corsOrigins = (config.get<string>("CORS_ORIGIN") ?? "http://localhost:5173")
    .split(",")
    .map((origin) => origin.trim())
    .filter(Boolean);

  app.enableCors({
    origin: (
      origin: string | undefined,
      callback: (err: Error | null, allow?: boolean | string) => void,
    ) => {
      if (!origin) return callback(null, true);
      if (corsOrigins.includes(origin)) {
        return callback(null, origin);
      }
      return callback(null, false);
    },
    credentials: true,
  });

  app.useGlobalPipes(
    new ValidationPipe({
      whitelist: true,
      transform: true,
      forbidNonWhitelisted: true,
    }),
  );

  const port = config.get<number>("PORT") ?? 3001;
  await app.listen(port);
  logger.log(`Umbrella API listening on http://localhost:${port}`);
}

bootstrap();
