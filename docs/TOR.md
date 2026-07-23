# Umbrella Wallet over Tor

Umbrella is anonymous-first: the web client makes **zero third-party requests**
for ordinary visitors (Telegram's script loads only inside the Telegram client,
WalletConnect only when the user links a wallet). This document covers running
the full stack as a Tor v3 hidden service.

## Quick start (self-hosted onion)

```bash
cp .env.tor.example .env.tor          # fill in real secrets
docker compose -f docker-compose.tor.yml --env-file .env.tor up -d --build
docker compose -f docker-compose.tor.yml exec tor cat /var/lib/tor/umbrella/hostname
```

The printed `…onion` address serves the whole product: the web container
proxies `/auth`, `/p2p`, `/rates`, … to the API (see `src/server.ts`), so one
`HiddenServicePort 80 web:3000` covers app + API. Keys persist in the
`umbra_tor_keys` volume — back it up; it _is_ your address.

## What the app does differently on .onion

| Concern      | Behaviour                                                                                                                                |
| ------------ | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Privacy mode | Auto-enabled when `hostname` ends in `.onion` (`src/lib/privacyMode.ts`); users elsewhere can toggle it in Settings → Privacy (Tor) mode |
| Telegram     | Login button hidden; `telegram-web-app.js` never injected                                                                                |
| CSP          | `upgrade-insecure-requests` dropped (onion speaks HTTP inside Tor)                                                                       |
| HSTS         | Not sent on onion responses                                                                                                              |
| Cookies      | `COOKIE_SECURE=false` in the onion compose — refresh cookie works over onion-HTTP                                                        |
| Rates        | Always proxied through our API (`/rates`), CoinGecko is never called from the client                                                     |

## Advertising the mirror from clearnet

On the **clearnet** web deployment set:

```
ONION_LOCATION=http://<your-address>.onion
```

Tor Browser shows a ".onion available" pill via the `Onion-Location` header
(sent by `src/server.ts` on non-onion HTML responses).

## Hardening notes

- The tor image is built locally from `alpine` (`docker/tor/Dockerfile`) — no
  third-party registry trust required.
- Consider `HiddenServiceNonAnonymousMode`/single-hop only if you do NOT need
  server-location anonymity (faster, weaker).
- For production add `HiddenServicePoWDefensesEnabled 1` (tor ≥ 0.4.8) to the
  torrc to resist introduction-flood DoS.
- The Telegram bot is a clearnet service by nature; leave `TELEGRAM_BOT_TOKEN`
  empty in the onion deployment unless you accept that trade-off.
