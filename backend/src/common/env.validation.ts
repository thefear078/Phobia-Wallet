import { ConfigService } from "@nestjs/config";

const PROD_REQUIRED = ["JWT_ACCESS_SECRET", "JWT_REFRESH_SECRET"] as const;

const PROD_RECOMMENDED = [
  "TELEGRAM_WEBHOOK_SECRET",
  "KYC_WEBHOOK_SECRET",
  "OPEN_BANKING_WEBHOOK_SECRET",
] as const;

function isProduction(config: ConfigService): boolean {
  return config.get<string>("NODE_ENV") === "production";
}

export function validateProductionEnv(config: ConfigService): void {
  if (!isProduction(config)) return;
  if (config.get<string>("DEMO_MODE") === "true") {
    console.warn(
      "[Umbrella] DEMO_MODE=true in production — JWT validation relaxed for demo deploy",
    );
    return;
  }

  for (const key of PROD_REQUIRED) {
    const value = config.get<string>(key);
    if (!value || value.length < 32) {
      throw new Error(
        `[Umbrella] ${key} must be set (min 32 chars) in production. Refusing to start.`,
      );
    }
  }

  for (const key of PROD_RECOMMENDED) {
    if (!config.get<string>(key)) {
      console.warn(
        `[Umbrella] Warning: ${key} is not set — related webhooks will reject requests in production.`,
      );
    }
  }
}

export function requireWebhookSecret(config: ConfigService, envKey: string): string {
  const secret = config.get<string>(envKey);
  if (secret) return secret;
  if (isProduction(config)) {
    throw new Error(`Webhook secret ${envKey} is required in production`);
  }
  return "";
}

export function getJwtAccessSecret(config: ConfigService): string {
  const secret = config.get<string>("JWT_ACCESS_SECRET");
  if (secret && secret.length >= 32) return secret;
  if (isProduction(config)) {
    throw new Error("JWT_ACCESS_SECRET missing in production");
  }
  return secret ?? "dev-access-secret-change-in-production-min-32-chars";
}

export function getJwtRefreshSecret(config: ConfigService): string {
  const secret = config.get<string>("JWT_REFRESH_SECRET");
  if (secret && secret.length >= 32) return secret;
  if (isProduction(config)) {
    throw new Error("JWT_REFRESH_SECRET missing in production");
  }
  return secret ?? "dev-refresh-secret-change-in-production-min-32-chars";
}
