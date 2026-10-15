# Contributing to Phobia Wallet

Thank you. Phobia is MIT-licensed source for a non-custodial wallet by **the fear** (thefear078).
Contributions that improve fund safety, privacy, honesty and auditability are welcome — and so are
translations, documentation and small fixes. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Find something to work on

| Label | Means |
|---|---|
| [`good first issue`](https://github.com/thefear078/Phobia-Wallet/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22) | Small, well described, no wallet internals needed — a good way in |
| [`help wanted`](https://github.com/thefear078/Phobia-Wallet/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22) | Larger work the maintainer would welcome help with |
| `area: coins` · `area: ui` · `area: privacy` · `area: security` · `area: android` · `area: docs` · `translations` | Where the change lands |

Not sure where to start? Translations ([docs/localization.md](docs/localization.md)), a theme
([docs/theming.md](docs/theming.md)) or a test for an uncovered path ([docs/testing.md](docs/testing.md)) are
all real contributions. For anything large, open an issue first so the work is not wasted.

## Set up

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the version in
[`global.json`](global.json)). Nothing else for the desktop app.

```bash
git clone https://github.com/thefear078/Phobia-Wallet.git
cd Phobia-Wallet
dotnet build desktop/Umbrella.Wallet.sln -c Release
dotnet run --project desktop/src/Umbrella.Wallet.App
dotnet test desktop/Umbrella.Wallet.sln -c Release --filter "Category!=Live"
```

**Never point a development build at your real wallet.** Give it a throwaway data folder:
`UMBRELLA_DATA_DIR=/tmp/phobia-dev dotnet run --project desktop/src/Umbrella.Wallet.App`
(PowerShell: `$env:UMBRELLA_DATA_DIR="$env:TEMP\phobia-dev"`). The Android app builds in CI; see
[docs/building.md](docs/building.md).

Where things are: [docs/architecture.md](docs/architecture.md). `Core` is pure logic with no network;
everything that does I/O is in `Infrastructure`; the UI is `App`.

## A good pull request

1. **One change, explained.** What was wrong, what the change does, how you checked it.
2. **Tests.** Anything touching the send path, the vault, key derivation, fees or amounts needs them —
   pinned to something independent (published vectors, a reference library, a transaction the network
   accepted), not to the wallet's own output. How: [docs/testing.md](docs/testing.md).
3. **CI green.** The offline suite, the rendered screens, CodeQL and Semgrep run on every pull request.
4. **No new network call outside `PublicHttp`**, and every new host declared in
   `NetworkCounterpartyCatalog` with what it learns (a test enforces it).
5. **No secret in logs, git history or a process's command line.**
6. **Honest wording.** A capability is not announced in the README, CHANGELOG or UI before the whole path
   works; limits are stated next to promises ([MANIFESTO.md](MANIFESTO.md)).
7. **Every user-facing string in all six languages** — `Localization.cs`; a test fails otherwise.
8. **Comments say why**, the way the surrounding code does.

Pull requests are squash-merged. Dependabot keeps packages and actions current; `packages.lock.json` pins
every NuGet package by hash, so a dependency change shows up in review.

## What we push back on

- Silent fee recipients, weakened randomness, or any way around the Tor kill-switch.
- Telemetry, analytics or advertising of any kind.
- Marketing claims that contradict [THREAT_MODEL.md](THREAT_MODEL.md) or [APP_STORE_NOTES.md](APP_STORE_NOTES.md).
- Renames that strip "the fear" attribution without a full, honest rebrand.

## Reporting

- **Bugs** — [GitHub Issues](https://github.com/thefear078/Phobia-Wallet/issues/new/choose), with version, OS and steps.
- **Security** — privately, through a [GitHub Security Advisory](https://github.com/thefear078/Phobia-Wallet/security/advisories/new). Never in a public issue. See [SECURITY.md](SECURITY.md).
- **A new coin** — the "Add a new coin" issue template; the path is in [docs/adding-a-chain.md](docs/adding-a-chain.md).
- **Never** paste a seed phrase, private key or vault file anywhere — nobody here will ever ask for one.

## Licence and brand

- **Code:** [MIT](LICENSE) — fork, patch and redistribute under its terms.
- **Brand:** [TRADEMARK_POLICY.md](TRADEMARK_POLICY.md) — do not ship a look-alike named "Phobia Wallet" or
  "the fear"; rebrand derivatives ([docs/forking.md](docs/forking.md)).

## Questions

[t.me/PhobiaStat](https://t.me/PhobiaStat) · [CONTACT.md](CONTACT.md) · [Sponsor](https://github.com/sponsors/thefear078)

---

📖 Back to the [README](README.md) · [Documentation index](docs/INDEX.md)
