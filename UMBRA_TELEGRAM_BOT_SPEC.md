# Umbra Telegram Bot Spec (Aggregator Model)

## Product Scope
- Bot username: `@UmbraWBot`
- Umbra remains a **non-custodial aggregator**: bot/web app never stores private keys or seed phrases.
- Telegram bot is an access and notification layer for the existing web app and backend API.

## Security Requirements
- Telegram Login init data must be verified via HMAC using bot token.
- `auth_date` TTL must be enforced (max 1 hour).
- Any messages containing seed/mnemonic/private key hints must be deleted with a warning.
- Bot token is local-only secret: keep it in `backend/.env`, never commit.

## Backend Changes

### Prisma User fields
- `telegramId BigInt? @unique`
- `telegramUsername String?`
- `telegramNotifications Boolean @default(true)`

### Auth
- `POST /auth/telegram` accepts `{ initData }`.
- Backend verifies Telegram `initData`, creates or links user, issues JWT + refresh cookie.
- Fallback user creation mode: `telegram_{id}@umbra.local`.

### Telegram Module
- `telegram.util.ts`: Telegram initData verification logic + TTL.
- `telegram-auth.controller.ts`: Telegram auth endpoint.
- `telegram-bot.service.ts`: grammY bot lifecycle and command handlers.
- `telegram-webhook.controller.ts`: webhook intake with secret validation.
- `telegram-notify.service.ts`: sends P2P status updates to linked users.

### Webhook / Runtime
- Supports webhook mode when `TELEGRAM_WEBHOOK_URL` is set.
- Validates `x-telegram-bot-api-secret-token` against `TELEGRAM_WEBHOOK_SECRET`.
- Falls back to polling mode for local development.

## Frontend Changes
- Add Telegram Mini App helper to detect Telegram context and extract `initData`.
- Add API client method `telegramAuth(initData)`.
- Update auth bootstrap to attempt Telegram auth automatically inside Mini App.

## Bot Commands
- `/start`: welcome + account link flow + `Відкрити Umbra` web_app button.
- `/balance`: linked wallet balances; if not linked, prompt link flow.
- `/rates BTC`: live rates via rates service.
- `/orders`: user P2P orders.
- `/receive`: primary linked wallet address + QR link.
- `/link`, `/wallet`, `/send`, `/p2p`: open Mini App button.
- `/support`: FAQ/help entry point.
- `/notifications`: toggle Telegram notifications.

## Environment Variables
```env
TELEGRAM_BOT_TOKEN=
TELEGRAM_WEBHOOK_SECRET=
TELEGRAM_WEBAPP_URL=http://localhost:5173
TELEGRAM_WEBHOOK_URL=
```

## CI / Delivery
- Frontend and backend build must pass in CI.
- Telegram secrets remain outside committed files.
