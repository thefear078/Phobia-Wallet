# Release keys

The public halves of the keys that sign Phobia Wallet releases. Nothing secret is here; the private keys
live in the project's GitHub secrets and on the maintainer's computer, never in this repository.

| File | What it is | Where it is checked |
|---|---|---|
| [`phobia-codesign.cer`](phobia-codesign.cer) | The Windows code-signing certificate: `CN=the fear (thefear078), O=Phobia Wallet`, RSA-4096, valid to 2036. **Self-signed**, so Windows shows the publisher as unverified. | SHA-1 thumbprint `89C2D871C195D57C14D1911B6C47629DE052C553`; SHA-256 `A88405BED227428530E8E6A77F4D3A42C03A154A1AD2E2E0EF1C87583DEA412F`, pinned in the wallet's updater |
| [`phobia-release-signing.pem`](phobia-release-signing.pem) | The same certificate's public key. It verifies `SHA256SUMS-<label>.txt.sig`, the signature over each release's checksum list. | Pinned in the wallet's updater, which refuses a release whose list it does not verify |

Verify a release's checksum list, then the files against it:

```bash
openssl dgst -sha256 -verify phobia-release-signing.pem -signature SHA256SUMS-Beta.txt.sig SHA256SUMS-Beta.txt
sha256sum -c SHA256SUMS-Beta.txt --ignore-missing
```

Releases up to and including the Beta of 15 October 2026 have no `.sig`; check those with the
[build attestation](../docs/BUILD_VERIFY.md) instead, which also covers their checksum lists.

The Android APK is signed with a separate key; its fingerprint is in [SECURITY.md](../SECURITY.md#verifying-what-you-run).

If a key ever changes, the new one is added to the wallet in a release signed with the old one, and the
change is announced in [SECURITY.md](../SECURITY.md) and the release notes before anything is signed with it.
