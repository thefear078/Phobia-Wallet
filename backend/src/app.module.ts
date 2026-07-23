import { Module } from "@nestjs/common";
import { ConfigModule } from "@nestjs/config";
import { EventEmitterModule } from "@nestjs/event-emitter";
import { ThrottlerGuard, ThrottlerModule, ThrottlerStorage } from "@nestjs/throttler";
import { APP_GUARD } from "@nestjs/core";
import { PrismaModule } from "./prisma/prisma.module";
import { RedisModule } from "./redis/redis.module";
import { AuthModule } from "./auth/auth.module";
import { UsersModule } from "./users/users.module";
import { LinkedWalletsModule } from "./linked-wallets/linked-wallets.module";
import { LinkedBankAccountsModule } from "./linked-bank-accounts/linked-bank-accounts.module";
import { P2pModule } from "./p2p/p2p.module";
import { RatesModule } from "./rates/rates.module";
import { KycModule } from "./kyc/kyc.module";
import { WebhooksModule } from "./webhooks/webhooks.module";
import { TelegramModule } from "./telegram/telegram.module";
import { DemoModule } from "./demo/demo.module";
import { HealthController } from "./health.controller";
import { RedisThrottlerStorage } from "./common/throttler/redis-throttler.storage";

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true }),
    EventEmitterModule.forRoot(),
    ThrottlerModule.forRootAsync({
      imports: [RedisModule],
      inject: [RedisThrottlerStorage],
      useFactory: (storage: RedisThrottlerStorage) => ({
        throttlers: [{ name: "default", ttl: 60_000, limit: 100 }],
        storage,
      }),
    }),
    DemoModule,
    PrismaModule,
    RedisModule,
    AuthModule,
    UsersModule,
    LinkedWalletsModule,
    LinkedBankAccountsModule,
    P2pModule,
    RatesModule,
    KycModule,
    WebhooksModule,
    TelegramModule,
  ],
  controllers: [HealthController],
  providers: [
    { provide: ThrottlerStorage, useExisting: RedisThrottlerStorage },
    { provide: APP_GUARD, useClass: ThrottlerGuard },
  ],
})
export class AppModule {}
