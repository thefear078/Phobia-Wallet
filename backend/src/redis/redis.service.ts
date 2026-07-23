import { Injectable, OnModuleDestroy, OnModuleInit } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import Redis from "ioredis";
import { MemoryCache } from "../demo/memory-cache";

export type CacheClient = {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<unknown>;
  setex(key: string, ttl: number, value: string): Promise<unknown>;
  del(key: string): Promise<number>;
  incr(key: string): Promise<number>;
  expire(key: string, ttl: number): Promise<number>;
  pexpire(key: string, ttlMs: number): Promise<number>;
  pttl(key: string): Promise<number>;
  quit(): Promise<unknown>;
};

@Injectable()
export class RedisService implements OnModuleInit, OnModuleDestroy {
  readonly client: CacheClient;
  private readonly memory = new MemoryCache();
  private redis: Redis | null = null;
  useMemory = false;

  constructor(private config: ConfigService) {
    this.client = this.createProxy();
  }

  onModuleInit() {
    if (this.config.get<string>("DEMO_MODE") === "true") {
      this.useMemory = true;
      return;
    }
    const url = this.config.get<string>("REDIS_URL") ?? "redis://localhost:6379";
    this.redis = new Redis(url, {
      maxRetriesPerRequest: 1,
      lazyConnect: true,
      retryStrategy: () => null,
    });
    void this.redis.connect().catch(() => {
      console.warn("[Redis] Unavailable — using in-memory cache");
      this.useMemory = true;
    });
  }

  async onModuleDestroy() {
    if (this.redis) await this.redis.quit().catch(() => undefined);
    await this.memory.quit();
  }

  private createProxy(): CacheClient {
    const mem = this.memory;
    const getRedis = () => this.redis;
    const inMemory = () => this.useMemory || !getRedis();

    return {
      get: (key) => (inMemory() ? mem.get(key) : getRedis()!.get(key)),
      set: (key, value) => (inMemory() ? mem.set(key, value) : getRedis()!.set(key, value)),
      setex: (key, ttl, value) =>
        inMemory() ? mem.setex(key, ttl, value) : getRedis()!.setex(key, ttl, value),
      del: (key) => (inMemory() ? mem.del(key) : getRedis()!.del(key)),
      incr: (key) => (inMemory() ? mem.incr(key) : getRedis()!.incr(key)),
      expire: (key, ttl) => (inMemory() ? mem.expire(key, ttl) : getRedis()!.expire(key, ttl)),
      pexpire: (key, ttlMs) =>
        inMemory() ? mem.pexpire(key, ttlMs) : getRedis()!.pexpire(key, ttlMs),
      pttl: (key) => (inMemory() ? mem.pttl(key) : getRedis()!.pttl(key)),
      quit: () => (inMemory() ? mem.quit() : getRedis()!.quit()),
    };
  }
}
