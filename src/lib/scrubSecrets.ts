const SENSITIVE_KEY =
  /private[_-]?key|mnemonic|seed|password|pan|cvv|secret|x-token|personalToken|authorization|bearer|refresh|initdata|access[_-]?token|jwt|id[_-]?token|client[_-]?ip|remote[_-]?addr/i;

const SENSITIVE_VALUE = /^(?:[a-f0-9]{64}|[a-f0-9]{128}|0x[a-f0-9]{64}|(?:\w+\s+){11,23}\w+)$/i;

const JWT_LIKE = /^eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+$/;

const BIP39_WORD = /\b([a-z]{3,8}\s+){11,23}[a-z]{3,8}\b/i;

function scrubString(value: string): string {
  if (SENSITIVE_KEY.test(value)) return "[REDACTED]";
  if (JWT_LIKE.test(value)) return "[REDACTED_JWT]";
  if (BIP39_WORD.test(value)) return "[REDACTED_MNEMONIC]";
  if (SENSITIVE_VALUE.test(value)) return "[REDACTED]";
  return value;
}

export function scrubSensitive(input: unknown): unknown {
  if (input == null) return input;
  if (typeof input === "string") return scrubString(input);
  if (typeof input === "number" || typeof input === "boolean" || typeof input === "bigint") {
    return input;
  }
  if (input instanceof Error) {
    return {
      name: input.name,
      message: scrubString(input.message),
      stack: input.stack ? scrubString(input.stack) : undefined,
    };
  }
  if (Array.isArray(input)) return input.map(scrubSensitive);
  if (typeof input === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(input as Record<string, unknown>)) {
      if (SENSITIVE_KEY.test(k)) out[k] = "[REDACTED]";
      else out[k] = scrubSensitive(v);
    }
    return out;
  }
  return input;
}

let scrubberInstalled = false;

export function installLogScrubber(): void {
  if (scrubberInstalled || typeof console === "undefined") return;
  scrubberInstalled = true;

  for (const level of ["log", "info", "warn", "error", "debug"] as const) {
    const original = console[level].bind(console);
    console[level] = (...args: unknown[]) => {
      original(...args.map(scrubSensitive));
    };
  }
}
