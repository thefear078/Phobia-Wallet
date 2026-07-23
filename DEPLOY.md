# Деплой Umbrella Wallet

## Архітектура

| Компонент | Платформа | Режим |
|-----------|-----------|-------|
| Frontend + Mini App | **Vercel** | Demo (localStorage) або proxy до API |
| NestJS API | **Render** (опційно) | Demo (in-memory) або PostgreSQL |

**Production URL (frontend):** https://umbra-wallet-web.vercel.app

**GitHub:** https://github.com/kiurakku/umbrella-wallet

---

## 1. Frontend → Vercel (демо, за замовчуванням)

У Vercel → Settings → Environment Variables:

| Змінна | Значення |
|--------|----------|
| `VITE_DEMO_MODE` | `true` |

Redeploy. Додаток працює **без бекенду** — дані в `localStorage`.

### Тестовий акаунт

- **Нік:** `demo`
- **Пароль:** `demo12345`

### Чек-ліст

| Крок | Дія | Очікування |
|------|-----|------------|
| 1 | Відкрити https://umbra-wallet-web.vercel.app | Банер «Демо-режим» |
| 2 | Реєстрація (нік + пароль ≥8) | Вхід без помилок |
| 3 | Дашборд | Демо-баланси ETH/TON |
| 4 | P2P | Оголошення, створення угоди |
| 5 | Telegram Mini App | Без «Failed to fetch» |

---

## 2. Backend → Render (demo API, без PostgreSQL)

Blueprint: https://dashboard.render.com/blueprint/new?repo=https://github.com/kiurakku/umbrella-wallet

`render.yaml` уже налаштований на `DEMO_MODE=true`. Після деплою API доступний на `https://umbra-api.onrender.com` (ім'я може відрізнятися).

Health check: `GET /health` → `{ ok: true, demo: true, database: false }`

### Env на Render (demo)

| Змінна | Значення |
|--------|----------|
| `DEMO_MODE` | `true` |
| `NODE_ENV` | `production` |
| `CORS_ORIGIN` | `https://umbra-wallet-web.vercel.app` |
| `JWT_ACCESS_SECRET` | auto-generated (≥32 chars) |
| `JWT_REFRESH_SECRET` | auto-generated (≥32 chars) |

`DATABASE_URL` і `REDIS_URL` **не потрібні** в demo-режимі.

---

## 3. Frontend + Backend разом (production API)

### Vercel

| Змінна | Значення |
|--------|----------|
| `VITE_DEMO_MODE` | `false` |
| `API_ORIGIN` | `https://umbra-api.onrender.com` |

`server.ts` проксує `/auth`, `/p2p`, `/rates` тощо на `API_ORIGIN` — без CORS-помилок у браузері.

### Render (full production)

| Змінна | Обов'язково |
|--------|-------------|
| `DEMO_MODE` | `false` |
| `DATABASE_URL` | PostgreSQL connection string |
| `REDIS_URL` | Redis URL |
| `JWT_ACCESS_SECRET` | ≥32 chars |
| `JWT_REFRESH_SECRET` | ≥32 chars |
| `TELEGRAM_BOT_TOKEN` | для бота та валідації Mini App |
| `CORS_ORIGIN` | URL Vercel frontend |

При старті виконується `prisma migrate deploy` (лише якщо `DEMO_MODE=false` і є `DATABASE_URL`).

---

## Локальна розробка

### Демо (без Docker)

```bash
npm install
# .env.local: VITE_DEMO_MODE=true
npm run dev
```

### З локальним API

```bash
docker compose up -d
cd backend && cp .env.example .env && npm run start:dev
# .env.local: VITE_DEMO_MODE=false
npm run dev
```

Backend demo без Docker:

```bash
cd backend && cp .env.example .env   # DEMO_MODE=true, без DATABASE_URL
npm run start:dev
```
