# Umbra Wallet — Технічний спек бекенду та ключових фіч

> Документ для розробки в Cursor. Мета — довести UI-прототип до робочого,
> безпечного, некастодіального крипто-гаманця з P2P, авторизацією через
> Apple/Google та керуванням банківськими картками.

---

## 0. Головне архітектурне рішення: НЕКАСТОДІАЛЬНА модель

Це найважливіше рішення в усьому проєкті, і його треба зафіксувати одразу.

**Некастодіальний гаманець** (як Trust Wallet, MetaMask, Rainbow):
- Seed-фраза / приватні ключі генеруються та зберігаються **тільки на пристрої
  користувача** (зашифровані).
- Бекенд **ніколи** не бачить seed-фразу, приватні ключі, і не підписує
  транзакції.
- Бекенд відповідає за: акаунт користувача, профіль, P2P-маркет, курси,
  KYC-статус, картки для fiat on/off-ramp, пуші, аналітику.

**Кастодіальна модель** (як біржа) — сервер зберігає ключі й підписує за
користувача — вимагає ліцензування як фінансова установа/VASP у більшості
юрисдикцій, набагато вищі вимоги до безпеки (HSM, страхування, аудити) і
юридичну відповідальність за чужі кошти. Судячи з опису проєкту (приватний
гаманець з P2P, а не біржа-кастодіан) — тобі потрібна **некастодіальна**
модель.

**Наслідок для `wallet-core`:** бібліотека використовується на клієнті
(браузер через WASM-білд `@trustwallet/wallet-core`, або в майбутньому
мобільний додаток через нативні біндинги), а не на сервері.

---

## 1. Загальна архітектура

```
┌─────────────────────────────────────────────────────────────┐
│                        КЛІЄНТ (браузер)                       │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  React 19 + TanStack Start (вже є)                     │   │
│  │  ┌────────────────────────────────────────────────┐   │   │
│  │  │  Secure Enclave (новий шар):                     │   │   │
│  │  │  - @trustwallet/wallet-core (WASM)                │   │   │
│  │  │  - генерація/імпорт seed (BIP39)                  │   │   │
│  │  │  - деривація адрес (BIP44)                        │   │   │
│  │  │  - підпис транзакцій (offline, локально)          │   │   │
│  │  │  - шифрування seed паролем (AES-GCM + Argon2id)   │   │   │
│  │  │  - зберігання: IndexedDB (зашифровано)            │   │   │
│  │  └────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────┘   │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTPS / REST або tRPC
                            │ (тільки публічні дані, JWT-сесія)
┌───────────────────────────▼─────────────────────────────────┐
│                    БЕКЕНД (Node.js/NestJS)                    │
│  - Auth service (email/password, Apple, Google)               │
│  - User/Profile service                                       │
│  - Cards service (проксі до payment-провайдера, НЕ зберігає   │
│    номери карток сам)                                         │
│  - P2P marketplace service                                    │
│  - Exchange/rates service (проксі до CEX API або агрегатора)  │
│  - KYC service (проксі до Sumsub/Veriff)                      │
│  - Notifications (push/websocket)                              │
│  - PostgreSQL + Redis                                          │
└─────────────────────────────────────────────────────────────┘
        │                    │                    │
   Payment provider      KYC provider        Blockchain nodes /
   (Stripe/Corefy/        (Sumsub/           indexer API
    Rapyd)                 Veriff)           (для балансів, історії —
                                              Trust Wallet's
                                              blockchain-sdk або
                                              Alchemy/Infura/Moralis)
```

Ключовий момент: бекенду не потрібно "розуміти" крипту глибоко — він більше
координатор. Реальна крипто-логіка (адреси, підписи, транзакції) —
на клієнті через `wallet-core`. Баланси/історію можна тягнути напряму з
блокчейн-нод/індексерів (Alchemy, Moralis, Blockbook від Trust Wallet) —
теж без участі твого бекенду, або через легкий проксі-кеш на бекенді, щоб не
світити API-ключі індексера в браузері.

---

## 2. Стек бекенду (рекомендація)

| Шар | Технологія | Чому |
|---|---|---|
| Runtime | Node.js 20+ | Той самий екосистема, що й фронтенд |
| Фреймворк | NestJS | Структура з коробки, DI, guards для auth, добре масштабується |
| API | REST або tRPC | tRPC зручно, якщо фронт теж на TS (TanStack Query вже є) |
| БД | PostgreSQL | Реляційні дані (users, cards-refs, p2p-offers) |
| ORM | Prisma | Типобезпечні міграції, добре з NestJS |
| Кеш/сесії | Redis | Rate-limit, сесії, кеш курсів |
| Черги | BullMQ (Redis) | Асинхронні задачі: webhooks від KYC/payment, нотифікації |
| Auth | Passport.js + власний JWT-шар | Стандарт для OAuth (Apple/Google) в Node |
| Секрети | Doppler / Vault / хоча б `.env` + KMS в проді | Ключі API, підписи JWT |

---

## 3. Автентифікація

### 3.1 Email + пароль
- Пароль ніколи не зберігається в plain-text. Хешування: **Argon2id**
  (не bcrypt — Argon2 стійкіший до GPU-атак).
- Мінімальні вимоги: 8+ символів, перевірка на "популярні паролі" (можна
  через `zxcvbn` на клієнті для UX-фідбеку).
- Email-верифікація через одноразовий токен (лінк або 6-значний код),
  TTL 15 хв.
- Rate-limit на логін: 5 спроб / 15 хв на IP + акаунт (через Redis),
  інакше — brute-force вразливість.

### 3.2 Вхід через Apple / Google
Це **окремий ідентифікатор**, не заміна гаманця. Схема:

1. Клієнт викликає `Sign in with Apple` / `Google Identity Services (GIS)` —
   отримує `id_token` (JWT від Apple/Google).
2. Клієнт шле цей токен на бекенд: `POST /auth/oauth/apple` або
   `/auth/oauth/google`.
3. Бекенд **валідує підпис токена** проти публічних ключів Apple/Google
   (JWKS endpoint), перевіряє `aud` (це має бути твій Client ID) і `exp`.
4. Якщо валідно — знаходить або створює `User` за `email`/`sub` (унікальний
   ID від провайдера), видає власну сесію (JWT access + refresh token).
5. **Важливо:** прив'язка гаманця (seed) до акаунта відбувається окремо, на
   клієнті, і бекенд про приватні ключі нічого не знає навіть після OAuth.

Для Apple специфічно:
- Потрібен Apple Developer акаунт, Services ID, ключ для Sign in with Apple.
- Apple повертає email лише при першому вході (треба зберегти одразу).
- Apple вимагає підтримку "Hide My Email" (relay-адреси) — це нормально,
  просто зберігай, що прийшло.

### 3.3 Сесії
- **Access token** (JWT, короткий TTL — 15 хв) в пам'яті клієнта.
- **Refresh token** (довший TTL — 30 днів), httpOnly + Secure cookie
  (не localStorage — вразливо до XSS).
- Refresh-токени зберігати в БД (hash, не сам токен) з можливістю
  відкликання (logout всіх пристроїв, компрометація).

### 3.4 Захист самого гаманця (окремо від акаунта!)
- **PIN/пароль розблокування додатку** — окремий від пароля акаунта,
  локальний, ніколи не йде на сервер. Він шифрує/розшифровує seed локально.
- Опційно: **WebAuthn / Passkeys** для біометрії в браузері (Face ID/Touch ID
  через platform authenticator) — гарний сучасний UX, і Apple/Google це
  добре підтримують у 2026.
- Автоблокування додатку після N хвилин бездіяльності.

---

## 4. Seed-фраза, ключі, `wallet-core`

### 4.1 Бібліотека
`@trustwallet/wallet-core` — офіційний npm-пакет (WASM-збірка з того самого
репо, що ти знайшов). Встановлюється на **фронтенд**:

```bash
npm install @trustwallet/wallet-core
```

Дає: генерацію мнемоніки (BIP39), деривацію ключів (BIP32/44/SLIP-0010),
адреси для 100+ блокчейнів, підпис транзакцій — все офлайн, у браузері.

### 4.2 Потік створення гаманця
1. `WalletCore.HDWallet.create(strength, passphrase)` → мнемоніка (12/24
   слова).
2. Показати користувачу для запису (з попередженням "ніхто, включно з нами,
   не може її відновити").
3. Підтвердження — попросити ввести кілька слів назад, щоб переконатись, що
   записав.
4. Користувач ставить локальний пароль/PIN.
5. Seed шифрується: `AES-256-GCM`, ключ шифрування виводиться з
   пароля через **Argon2id** (KDF), сіль — рандомна, зберігається поряд.
6. Зашифрований blob → `IndexedDB` (не `localStorage` — той синхронний і
   легше вичитати через XSS-скрипти масово; хоча обидва вразливі до XSS
   в принципі, тому нижче окремий пункт про XSS-захист).

### 4.3 Потік імпорту існуючого гаманця
Той самий шлях, тільки мнемоніка вводиться користувачем, а не генерується.
Валідація через `Mnemonic.isValid()` з wallet-core.

### 4.4 Критично: web-гаманці й безпека seed
Чесно попереджаю: **веб-застосунок (браузер) — найслабше середовище для
зберігання приватних ключів** порівняно з мобільним Keychain/Keystore, бо:
- Будь-яка XSS-вразливість = потенційний витік зашифрованого blob'а
  (а якщо ще й keylogger на пароль — то й розшифровка).
- Немає апаратного Secure Enclave, як на iOS/Android.

Мітигації, які обов'язково потрібні:
- Строгий **Content Security Policy** (CSP), без `unsafe-inline` для скриптів.
- **Subresource Integrity (SRI)** на всі зовнішні скрипти.
- Регулярний аудит npm-залежностей (`npm audit`, Snyk) — supply-chain атаки
  через залежності це реальний вектор для гаманців.
- Розглянути **аппаратний ліміт**: невеликі суми ОК в web-гаманці, для
  великих сум — рекомендувати hardware wallet (Ledger) інтеграцію через
  WalletConnect, або відклали мобільний застосунок (React Native + нативний
  Keychain/Keystore) як наступний крок.
- Явно комунікувати користувачу цей trade-off в UI (не приховувати).

---

## 5. Банківські картки

### 5.1 Головне правило — НЕ зберігай номери карток сам
Зберігання PAN (номера картки) підпадає під **PCI DSS** — дорогий і складний
комплаєнс. Замість цього:

- Використай payment-провайдера з готовою токенізацією:
  **Stripe** (Setup Intents / Payment Methods), **Corefy**, **Rapyd**,
  **Wallester** (більш SNG/EU-орієнтовані варіанти для крипто-фінтеху).
- Провайдер віддає тобі **токен картки**, а не номер. Ти зберігаєш тільки
  токен + маску (`•••• 4242`) + бренд (Visa/Mastercard) у своїй БД.
- Додавання картки: клієнт → SDK провайдера (Stripe Elements тощо) →
  токен → бекенд зберігає токен, прив'язаний до `userId`.
- Видалення картки: видаляєш зв'язок токена в БД + викликаєш API провайдера
  на detach/delete токена.

### 5.2 Схема таблиці `payment_cards` (тільки метадані, без PAN)

```sql
CREATE TABLE payment_cards (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  provider TEXT NOT NULL,              -- 'stripe' | 'corefy' | ...
  provider_token TEXT NOT NULL,        -- токен від провайдера
  brand TEXT,                          -- 'visa' | 'mastercard'
  last4 TEXT NOT NULL,
  exp_month SMALLINT,
  exp_year SMALLINT,
  is_default BOOLEAN DEFAULT FALSE,
  created_at TIMESTAMPTZ DEFAULT now(),
  deleted_at TIMESTAMPTZ                -- soft delete для аудиту
);
```

---

## 6. P2P-маркет (бекенд-частина)

Судячи з опису — офери мерчантів з фільтрами по активу й банку
(monobank/privatbank). Це вже кастодіальна логіка щодо *escrow*, тому
продумай окремо:

- **Offers**: мерчант публікує офер (актив, ціна, ліміти, банківський метод).
- **Orders**: покупець відкриває угоду проти офера → статус-машина
  `created → payment_pending → paid_by_buyer → confirmed_by_seller →
  released → completed` (+ `disputed`, `cancelled`).
- **Escrow крипти під час угоди** — це ключове питання: хто "тримає" крипту
  поки триває угода? Варіанти:
  1. Смарт-контракт escrow (найкраще для non-custodial принципу) —
     продавець блокує крипту в контракті, звільняється або по підтвердженню,
     або через арбітраж/таймаут.
  2. Централізований escrow-гаманець (простіше, але це вже кастодіальна дія
     для конкретної суми на час угоди — потребує окремого секьюріті-периметру,
     ключі escrow-гаманця на бекенді з HSM/KMS).
- Для MVP простіше й чесніше почати з (1) — простий escrow-контракт (є
  готові аудитовані патерни, наприклад OpenZeppelin Escrow) на мережах, які
  підтримуєш (EVM-сумісні — простіше).

Базова схема БД:

```sql
CREATE TABLE p2p_offers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  merchant_id UUID NOT NULL REFERENCES users(id),
  asset TEXT NOT NULL,
  fiat_currency TEXT NOT NULL,
  price NUMERIC(20,8) NOT NULL,
  min_amount NUMERIC(20,8),
  max_amount NUMERIC(20,8),
  payment_methods TEXT[] NOT NULL,     -- ['monobank', 'privatbank']
  status TEXT DEFAULT 'active',        -- active | paused | deleted
  created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE p2p_orders (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  offer_id UUID NOT NULL REFERENCES p2p_offers(id),
  buyer_id UUID NOT NULL REFERENCES users(id),
  amount NUMERIC(20,8) NOT NULL,
  status TEXT NOT NULL DEFAULT 'created',
  escrow_tx_hash TEXT,                 -- якщо смарт-контракт escrow
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now()
);
```

---

## 7. KYC

Для P2P із фіатом майже завжди потрібен хоч базовий KYC (легальні вимоги
проти відмивання коштів залежно від юрисдикції).

- Провайдер: Sumsub, Veriff, або SumUp KYC — приймають документ + селфі,
  віддають вебхуком статус (`approved`/`rejected`/`pending`).
- Бекенд зберігає лише **статус** і **provider reference id**, не самі
  документи (вони лишаються у провайдера, який сертифікований під це).
- Рівні: без KYC → малі ліміти P2P; з KYC → повний доступ.

---

## 8. Ключові API-ендпоінти (орієнтир для Cursor)

```
POST   /auth/register              — email + пароль
POST   /auth/login
POST   /auth/oauth/apple           — обмін id_token → сесія
POST   /auth/oauth/google
POST   /auth/refresh
POST   /auth/logout

GET    /users/me
PATCH  /users/me                   — мова, налаштування
DELETE /users/me                   — видалення акаунта (GDPR)

GET    /cards
POST   /cards                      — додати (провайдер-токен з клієнта)
DELETE /cards/:id

GET    /p2p/offers?asset=&method=
POST   /p2p/offers                 — тільки для верифікованих мерчантів
POST   /p2p/orders
PATCH  /p2p/orders/:id/confirm-payment
PATCH  /p2p/orders/:id/release
POST   /p2p/orders/:id/dispute

GET    /rates?base=BTC&quote=USD
GET    /kyc/status
POST   /kyc/start                  — редирект/лінк на провайдера

POST   /webhooks/kyc               — вебхуки від KYC-провайдера
POST   /webhooks/payments          — вебхуки від payment-провайдера
```

---

## 9. Security-чекліст перед продом

- [ ] Argon2id для паролів, ніколи не bcrypt/sha256 напряму
- [ ] HTTPS всюди, HSTS, TLS 1.2+
- [ ] Rate-limit на всі auth-ендпоінти (Redis + `@nestjs/throttler`)
- [ ] CSP без `unsafe-inline`, SRI на зовнішні скрипти
- [ ] Refresh-токени в httpOnly Secure cookie, не localStorage
- [ ] Валідація Apple/Google JWT проти офіційних JWKS, перевірка `aud`/`iss`
- [ ] Жодного PAN/CVV на бекенді — тільки токени провайдера
- [ ] Seed/приватні ключі — ніколи на бекенді, ніколи в логах, ніколи в
      аналітиці/Sentry (додай scrub-правила на всі поля з "seed", "mnemonic",
      "privateKey")
- [ ] 2FA (TOTP) опційно для акаунта, окремо від локального PIN гаманця
- [ ] Аудит залежностей (`npm audit`, Dependabot/Snyk) в CI
- [ ] Rate-limit і моніторинг на P2P (анти-фрод, підозрілі патерни)
- [ ] Логування дій без чутливих даних (structured logs, без PII в plain)
- [ ] GDPR: видалення акаунта, експорт даних за запитом

---

## 10. Порядок імплементації (для Cursor, по кроках)

1. **Backend skeleton**: NestJS проєкт, Prisma + Postgres, базова структура
   модулів (auth, users, cards, p2p, kyc, rates).
2. **Auth**: email/password + JWT сесії (без OAuth поки що) — щоб мати
   робочий логін для тестів решти фіч.
3. **Client-side wallet layer**: інтеграція `@trustwallet/wallet-core`,
   генерація/імпорт seed, локальне шифрування, розблокування паролем —
   повністю ізольовано від бекенду.
4. **OAuth (Apple/Google)**: додати як альтернативний вхід до вже робочого
   auth-модуля.
5. **Cards**: інтеграція з обраним payment-провайдером (почни зі Stripe —
   найпростіша документація), CRUD токенів.
6. **Rates/Exchange**: проксі до агрегатора курсів (напр. CoinGecko API для
   старту, пізніше можна маркет-мейкера для реального обміну).
7. **P2P MVP**: offers + orders без escrow-контракту спочатку (ручне
   підтвердження, статус-машина) — щоб перевірити UX, потім додати
   смарт-контракт escrow.
8. **KYC**: інтеграція Sumsub/Veriff, вебхуки, ліміти по рівню верифікації.
9. **Hardening**: пройтись по чеклісту з розділу 9 перед публічним релізом.

---

## 11. Що сказати Cursor одним промптом (стартовий)

Приклад першого промпту для Cursor, коли будеш починати бекенд:

> Створи NestJS-бекенд для Umbra Wallet за специфікацією з
> UMBRA_BACKEND_SPEC.md. Почни з модулів `auth` (email/password реєстрація й
> логін, Argon2id для паролів, JWT access+refresh) та `users`. Використай
> Prisma з PostgreSQL. Структуруй по NestJS-конвенціях (module/controller/
> service/dto на кожен домен). Не додавай логіку роботи з приватними ключами
> чи seed-фразами на бекенд — ця частина лишається на клієнті.

Так Cursor матиме контекст всього документа, але почне з керованого,
маленького шматка, а не спробує згенерувати все одразу (що зазвичай дає
гірший результат).
