# Umbrella Wallet Desktop

Native, local-first desktop wallet built with .NET 8 and Avalonia by **kiurakku**.
This folder is independent from the web UI and is designed so seed material remains on the PC.

**Current version:** 1.7.0 — see [CHANGELOG](../CHANGELOG.md) for release history.

## Current MVP

- Native Windows/Linux/macOS UI shell (Avalonia; no Electron/WebView).
- Generate or import a BIP39 24-word recovery phrase.
- Encrypt the phrase locally with Argon2id (64 MiB, 4 iterations) and AES-256-GCM.
- Versioned vault envelope with authenticated metadata and atomic writes.
- Lock/unlock lifecycle; the phrase is never written to logs or sent to the API.
- Deterministic receive accounts for the chains implemented by
  `Umbrella.Wallet.Core`.
- Explicit capability flags: unsupported signing/derivation is shown as planned,
  never faked.

## Security model

The desktop app is **non-custodial**. The backend is optional and may only receive
public addresses and P2P metadata.

### Protected against

- Theft of the vault file without the password.
- Vault tampering (AES-GCM authentication fails closed).
- Accidental plaintext persistence.
- Cross-app network leakage: the MVP has no telemetry and does not call the
  Umbrella API while creating/unlocking a vault.

### Not protected against

- Malware/keyloggers or an already-compromised OS.
- A weak vault password.
- Screen capture while the recovery phrase is visible.
- Physical attacks while the wallet is unlocked.

For meaningful cold storage, use an offline PC, verify release hashes, keep an
offline paper/metal backup, and never type the seed into websites or chats.

## Build and run

```powershell
$env:NUGET_PACKAGES = "D:\nuget-cache\packages" # optional on low-space systems
dotnet restore desktop/Umbrella.Wallet.sln
dotnet test desktop/Umbrella.Wallet.sln
dotnet run --project desktop/src/Umbrella.Wallet.App
```

## Release gate

This MVP is **not yet an independently audited hardware wallet**. Before using it
with significant funds:

1. Independent cryptography and supply-chain audit.
2. Reproducible signed builds and update-signature verification.
3. Hardware-wallet integration and PSBT/air-gapped signing for Bitcoin.
4. Per-chain transaction builders, fee simulation, and broadcast tests.
5. Secure clipboard timeout, auto-lock, backup verification, and panic wipe.
6. Test vectors for every supported derivation path.

The architecture intentionally separates these future signing modules from the
vault so no unreviewed network adapter can access seed material directly.
