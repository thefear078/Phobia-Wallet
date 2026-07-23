# Changelog

All notable releases of **Umbrella Wallet** by [kiurakku](https://github.com/kiurakku).

Format follows [Keep a Changelog](https://keepachangelog.com/). Versioning follows [SemVer](https://semver.org/).

## [1.7.0] — 2026-07-23

### Desktop
- Monero and Tor startup reliability
- Encrypted backup export
- Panel placement fixes
- Four additional colour themes

### Web & API
- Market rates aggregator (CoinGecko + Binance fallback)
- Tor / privacy mode with IP redaction on backend
- Security hardening (Helmet, rate limiting, log scrubbing)
- Monero view-only derivation, multi-chain balances via public RPCs

## [1.6.0] — 2026-07-22

### Desktop
- Exchange-style candle charts
- Vector tile icons for coin list
- QR receive scrim fix (no white flash)

## [1.5.1] — 2026-07-22

### Desktop
- Fix: the fear brand mark no longer carries a white background

## [1.5.0] — 2026-07-22

### Desktop
- Six colour themes
- Searchable language picker
- Real market charts
- Nine exchange connectors (read-only API keys)

## [1.4.0] — 2026-07-21

### Desktop
- Exchange account linking (Binance, Bybit, OKX, Kraken, KuCoin, Gate.io, MEXC, Bitget, Telegram CryptoBot)
- Improved buttons and dropdown controls

## [1.3.0] — 2026-07-21

### Desktop
- Linked wallets counter on portfolio
- QR code popup for receive
- Six interface languages

## [1.2.0] — 2026-07-20

### Desktop
- USDT (TRC-20) and TRX sending
- Wallet data stored off the system drive (user-chosen path)
- Brand logo restored in title bar

## [1.1.0] — 2026-07-20

### Desktop
- Monero as a full coin (receive, send, balance via local `monero-wallet-rpc`)
- Telegram sticker animations in onboarding

## [1.0.2] — 2026-07-19

### Desktop
- Fix clipping on small window sizes
- Settings panes layout
- Glass-style UI polish

## [1.0.1] — 2026-07-19

### Desktop
- the fear branding
- Network labels on receive addresses
- Chain pickers and layout fixes

## [1.0.0] — 2026-07-18

### Desktop (initial release)
- Native Avalonia wallet for Windows
- BIP39 24-word seed generation and import
- Argon2id + AES-256-GCM encrypted local vault
- BTC, ETH, SOL, TRX, LTC, DOGE receive addresses
- Built-in Tor routing
- Real ETH and BTC send
- Watch-only address tracking
- Live market prices from CoinGecko

### Web (companion)
- React + NestJS stack
- Non-custodial browser wallet shell
- P2P marketplace, exchange view, portfolio stats

---

**Owner:** [kiurakku](https://github.com/kiurakku) · **License:** [LICENSE](LICENSE)
