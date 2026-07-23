import { LoggerService } from "@nestjs/common";
import pino from "pino";

const PII_KEYS = new Set([
  "password",
  "seed",
  "mnemonic",
  "privatekey",
  "private_key",
  "token",
  "pan",
  "cvv",
  "refreshtoken",
  "refresh_token",
  "accesstoken",
  "access_token",
  "idtoken",
  "id_token",
  "authorization",
  "cookie",
  "secret",
]);

const PII_VALUE_PATTERNS = [
  /\b(?:seed|mnemonic|private\s*key)\b/i,
  /\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/,
];

function scrubKey(key: string): boolean {
  const normalized = key.toLowerCase().replace(/[-_\s]/g, "");
  return PII_KEYS.has(normalized) || PII_KEYS.has(key.toLowerCase());
}

function scrubValue(value: unknown): unknown {
  if (typeof value === "string") {
    if (value.length > 80 && PII_VALUE_PATTERNS.some((re) => re.test(value))) {
      return "[REDACTED]";
    }
    if (value.length >= 20 && /^[A-Za-z0-9+/=_-]+$/.test(value) && value.includes("eyJ")) {
      return "[REDACTED_JWT]";
    }
  }
  return value;
}

export function scrubPii(value: unknown, depth = 0): unknown {
  if (depth > 8) return "[MAX_DEPTH]";
  if (value === null || value === undefined) return value;
  if (value instanceof Error) {
    return {
      name: value.name,
      message: scrubValue(value.message),
      stack: typeof value.stack === "string" ? scrubValue(value.stack) : undefined,
    };
  }
  if (typeof value === "string") return scrubValue(value);
  if (Array.isArray(value)) return value.map((item) => scrubPii(item, depth + 1));
  if (typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const [key, val] of Object.entries(value as Record<string, unknown>)) {
      out[key] = scrubKey(key) ? "[REDACTED]" : scrubPii(val, depth + 1);
    }
    return out;
  }
  return value;
}

export class PiiSafeLogger implements LoggerService {
  constructor(private readonly inner: LoggerService) {}

  log(message: unknown, ...optionalParams: unknown[]) {
    this.inner.log(scrubPii(message), ...optionalParams.map((p) => scrubPii(p)));
  }

  error(message: unknown, ...optionalParams: unknown[]) {
    this.inner.error(scrubPii(message), ...optionalParams.map((p) => scrubPii(p)));
  }

  warn(message: unknown, ...optionalParams: unknown[]) {
    this.inner.warn(scrubPii(message), ...optionalParams.map((p) => scrubPii(p)));
  }

  debug?(message: unknown, ...optionalParams: unknown[]) {
    this.inner.debug?.(scrubPii(message), ...optionalParams.map((p) => scrubPii(p)));
  }

  verbose?(message: unknown, ...optionalParams: unknown[]) {
    this.inner.verbose?.(scrubPii(message), ...optionalParams.map((p) => scrubPii(p)));
  }
}

const pinoLogger = pino({
  level: process.env.LOG_LEVEL ?? "info",
  redact: {
    paths: [
      "password",
      "req.headers.authorization",
      "req.headers.cookie",
      "*.token",
      "*.refreshToken",
      "*.accessToken",
      "*.idToken",
      "*.seed",
      "*.mnemonic",
      "*.privateKey",
      "*.pan",
      "*.cvv",
    ],
    censor: "[REDACTED]",
  },
  formatters: {
    level(label: string) {
      return { level: label };
    },
  },
});

export function createAppLogger(): LoggerService {
  return {
    log: (msg, ...rest) => pinoLogger.info({ msg: scrubPii(msg), extra: scrubPii(rest) }),
    error: (msg, ...rest) => pinoLogger.error({ msg: scrubPii(msg), extra: scrubPii(rest) }),
    warn: (msg, ...rest) => pinoLogger.warn({ msg: scrubPii(msg), extra: scrubPii(rest) }),
    debug: (msg, ...rest) => pinoLogger.debug({ msg: scrubPii(msg), extra: scrubPii(rest) }),
    verbose: (msg, ...rest) => pinoLogger.trace({ msg: scrubPii(msg), extra: scrubPii(rest) }),
  };
}
