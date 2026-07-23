import type { Context } from "grammy";

const DEFAULT_RETRIES = 3;
const BASE_DELAY_MS = 400;

export class CommandRateLimiter {
  private hits = new Map<number, number[]>();

  constructor(
    private readonly maxHits: number,
    private readonly windowMs: number,
  ) {}

  tryConsume(userId: number): boolean {
    const now = Date.now();
    const windowStart = now - this.windowMs;
    const prev = (this.hits.get(userId) ?? []).filter((t) => t > windowStart);
    if (prev.length >= this.maxHits) {
      this.hits.set(userId, prev);
      return false;
    }
    prev.push(now);
    this.hits.set(userId, prev);
    return true;
  }

  /** Drop stale entries (call periodically). */
  prune() {
    const now = Date.now();
    for (const [id, times] of this.hits) {
      const fresh = times.filter((t) => t > now - this.windowMs);
      if (fresh.length === 0) this.hits.delete(id);
      else this.hits.set(id, fresh);
    }
  }
}

export async function withTelegramRetry<T>(
  fn: () => Promise<T>,
  retries = DEFAULT_RETRIES,
): Promise<T> {
  let lastError: unknown;
  for (let attempt = 0; attempt < retries; attempt++) {
    try {
      return await fn();
    } catch (error) {
      lastError = error;
      const delay = BASE_DELAY_MS * 2 ** attempt;
      await sleep(delay);
    }
  }
  throw lastError;
}

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export async function safeReply(
  ctx: Context,
  text: string,
  extra?: Parameters<Context["reply"]>[1],
): Promise<void> {
  await withTelegramRetry(() => ctx.reply(text, extra));
}
