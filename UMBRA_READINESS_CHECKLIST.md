# Umbra Wallet — Чек-ліст готовності до запуску (Definition of Done)

> Продовження UMBRA_BACKEND_SPEC.md і UMBRA_AGGREGATOR_ADDENDUM.md.

Див. розділи 1–9 у повному документі. Для перевірки в Cursor:

> Пройдись по розділу N з UMBRA_READINESS_CHECKLIST.md. Для кожного пункту
> знайди код або явно познач «не реалізовано».

## Статус імплементації (оновлено 2026-07-11, після аудиту та фіксів)

| Розділ | Статус |
|--------|--------|
| 1 Продукт | Auth (нік/пароль + OAuth + Telegram), seed vault (argon2id+AES-GCM, BIP-44 звірено з тест-векторами), P2P **повний цикл** (created→…→completed, E2E двома акаунтами), email-верифікація (SMTP_URL), живі графіки — **реалізовано і перевірено локально** |
| 2 Безпека | CSP (+wasm для argon2), rate-limit auth/orders/email, scrub secrets, httpOnly refresh rotation — **реалізовано**; HTTPS/секрети — **деплой (Render)** |
| 3 Юридичне | `/legal/*` — сторінки є; юрист UA/EU — **зовнішній крок** |
| 4 Інфра | ❌ Render-сервіс не існує (`x-render-routing: no-server`) — **головний блокер**; Vercel env готові; бекапи Postgres — **після створення сервісу** |
| 5 Тести | P2P state machine + telegram-reliability unit — зелені; повний E2E пройдено **локально**; на prod — після деплою |
| 6 UX | Empty states, mobile 375px — перевірено; sheets ок |
| 7 Підтримка | FAQ `/help`, email у Terms — реалізовано |

**Не реалізовано свідомо (потребує окремого рішення):** on-chain swap на біржі (зараз калькулятор + P2P), власний підпис транзакцій у додатку (зараз WalletConnect / Mini App), реальний KYC-провайдер (Sumsub-інтеграція є як каркас, потрібен акаунт), SMTP-акаунт для листів, Sentry/Datadog моніторинг.

Повний текст чек-ліста — у секціях нижче.

---

## 1. Продуктова функціональність 🔴

- [ ] 🔴 Онбординг: реєстрація (email + Apple/Google) end-to-end
- [ ] 🔴 WalletConnect — реальна прив'язка (`src/lib/wallet/walletConnect.ts`)
- [ ] 🔴 Monobank link (`POST /bank-accounts/monobank/link`)
- [ ] 🔴 Дашборд — `usePortfolio()` з API, не mockData
- [ ] 🔴 P2P повний цикл — `P2pOrderSheet.tsx`
- [ ] 🟡 Курси — CoinGecko `/rates/market`
- [ ] 🟡 Статистика — з `usePortfolio`
- [ ] ⚪ Sparkline, push

---

## 2. Безпека 🔴

- [ ] 🔴 Немає seed/PAN у БД — модель aggregator
- [ ] 🔴 HTTPS на проді — **інфра**
- [ ] 🔴 CSP — `backend/src/main.ts`, `src/server.ts`
- [ ] 🔴 Rate-limit auth — `@Throttle` + ThrottlerGuard
- [ ] 🔴 Секрети в env — `.env.example`, не в git
- [ ] 🟡 npm audit у CI
- [ ] 🟡 WebAuthn
- [ ] ⚪ Bug bounty

---

## 3. Юридичне 🔴

- [ ] 🔴 Privacy Policy — `/legal/privacy`
- [ ] 🔴 Terms — `/legal/terms` (не кастодіан)
- [ ] 🔴 Юрист — **зовнішній крок**
- [ ] 🟡 P2P disputes — FAQ + `/help`
- [ ] 🟡 Cookie consent
- [ ] ⚪ AML policy

---

## 4–9

Див. оригінальний документ у репозиторії та README.
