/** In-memory cache compatible with RedisService.client subset used in the app. */

export class MemoryCache {
  private store = new Map<string, { value: string; expiresAt?: number }>();

  async get(key: string): Promise<string | null> {
    this.evict(key);
    return this.store.get(key)?.value ?? null;
  }

  async setex(key: string, ttlSec: number, value: string): Promise<"OK"> {
    this.store.set(key, { value, expiresAt: Date.now() + ttlSec * 1000 });
    return "OK";
  }

  async set(key: string, value: string): Promise<"OK"> {
    this.store.set(key, { value });
    return "OK";
  }

  async del(key: string): Promise<number> {
    return this.store.delete(key) ? 1 : 0;
  }

  async incr(key: string): Promise<number> {
    const current = Number((await this.get(key)) ?? "0");
    const next = current + 1;
    await this.set(key, String(next));
    return next;
  }

  async expire(key: string, ttlSec: number): Promise<number> {
    const entry = this.store.get(key);
    if (!entry) return 0;
    entry.expiresAt = Date.now() + ttlSec * 1000;
    return 1;
  }

  async pexpire(key: string, ttlMs: number): Promise<number> {
    const entry = this.store.get(key);
    if (!entry) return 0;
    entry.expiresAt = Date.now() + ttlMs;
    return 1;
  }

  async pttl(key: string): Promise<number> {
    this.evict(key);
    const entry = this.store.get(key);
    if (!entry) return -2;
    if (!entry.expiresAt) return -1;
    return Math.max(entry.expiresAt - Date.now(), 0);
  }

  async quit(): Promise<"OK"> {
    this.store.clear();
    return "OK";
  }

  private evict(key: string) {
    const entry = this.store.get(key);
    if (entry?.expiresAt && entry.expiresAt <= Date.now()) {
      this.store.delete(key);
    }
  }
}
