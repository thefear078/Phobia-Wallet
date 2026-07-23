import { Injectable } from "@nestjs/common";
import type { ThrottlerStorage } from "@nestjs/throttler";
import { RedisService } from "../../redis/redis.service";

type ThrottlerStorageRecord = {
  totalHits: number;
  timeToExpire: number;
  isBlocked: boolean;
  timeToBlockExpire: number;
};

@Injectable()
export class RedisThrottlerStorage implements ThrottlerStorage {
  constructor(private readonly redis: RedisService) {}

  async increment(
    key: string,
    ttl: number,
    limit: number,
    blockDuration: number,
    _throttlerName: string,
  ): Promise<ThrottlerStorageRecord> {
    const blockKey = `${key}:block`;
    const hitsKey = `${key}:hits`;

    const blocked = await this.redis.client.get(blockKey);
    if (blocked) {
      const timeToBlockExpire = await this.redis.client.pttl(blockKey);
      return {
        totalHits: limit + 1,
        timeToExpire: 0,
        isBlocked: true,
        timeToBlockExpire: Math.max(timeToBlockExpire, 0),
      };
    }

    const totalHits = await this.redis.client.incr(hitsKey);
    if (totalHits === 1) {
      await this.redis.client.pexpire(hitsKey, ttl);
    }

    const timeToExpire = await this.redis.client.pttl(hitsKey);

    if (totalHits > limit) {
      await this.redis.client.setex(blockKey, Math.ceil(blockDuration / 1000), "1");
      return {
        totalHits,
        timeToExpire: Math.max(timeToExpire, 0),
        isBlocked: true,
        timeToBlockExpire: blockDuration,
      };
    }

    return {
      totalHits,
      timeToExpire: Math.max(timeToExpire, 0),
      isBlocked: false,
      timeToBlockExpire: 0,
    };
  }
}
