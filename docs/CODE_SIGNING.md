# Windows code signing: from self-signed to trusted

Every Windows file Phobia ships is signed by the release workflow and timestamped
([BUILD_VERIFY.md § B](BUILD_VERIFY.md#b-signature)). The certificate is **self-signed**, so the
signature proves a file is untouched since the workflow signed it, but no certificate authority vouches
for the name: Windows shows "Unknown publisher" and SmartScreen warns. A certificate issued by a public
authority ends that. It has to be applied for by the maintainer — it ties a legal identity to the
releases — so this page lists the routes and exactly what the pipeline needs once one exists.

*Checked 2026-10-08. Each provider's own page is the authority; terms change.*

## The routes

| Route | Cost | Who can apply | Publisher shown | Fits this project? |
|---|---|---|---|---|
| **[SignPath Foundation](https://signpath.org/)** | Free for open-source projects | Individuals and teams, any country; the *project* must qualify | "SignPath Foundation" (the foundation signs on the project's behalf) | **Best fit.** MIT licence, automated builds from this repository, already released |
| [Certum Open Source Code Signing](https://shop.certum.eu/open-source-code-signing.html) | Paid, low (yearly) | Individual open-source developers, with identity checks | The maintainer's name | Works; the key lives on Certum's cloud (SimplySign) or a card, so signing in CI needs their tool on the Windows runner |
| [Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/) (formerly Trusted Signing) | Monthly fee | Individuals **only in the US and Canada** (organisations also in the EU and UK) | The maintainer's name | Not available to an individual maintainer elsewhere |

### SignPath Foundation — what to know before applying

- Its conditions: an OSI-approved licence with no proprietary parts, an active project already released
  in the form to be signed, a fully automated build from the repository, and **no malware or
  potentially unwanted programs**.
- That last point needs a sentence in the application. Phobia bundles the Monero project's own
  `monero-wallet-rpc`, and Microsoft Defender's machine-learning detection flags that official binary as a
  potentially unwanted program (for example `PUA:Win32/Caypnamer.A!ml`) — a false positive common to
  Monero software. Say so up front: the binary is fetched from getmonero.org at a pinned version, checked
  against binaryFate's signed hash list (`scripts/check-pinned-binaries.sh`), and listed in
  [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
- Apply as the maintainer at signpath.org. Once accepted, SignPath gives the project an organisation and
  a signing policy, and the release workflow submits the built files to it from GitHub Actions instead
  of signing them with the PFX.

## What changes in the pipeline when a certificate exists

1. **A PFX certificate** (Certum's exportable form, or any authority that issues one): replace the two
   repository secrets `WINDOWS_CERT_PFX_B64` and `WINDOWS_CERT_PASSWORD` (never in the repository, never
   printed). **SignPath:** add its GitHub Action step to `release.yml` in place of
   `desktop/scripts/sign-windows.ps1`, with the organisation id, project and policy slugs it assigns, and
   its API token as a secret.
2. Update the pinned thumbprint in `release.yml` — the workflow refuses to publish a file signed by any
   other key, which is the point — and the fingerprints in [BUILD_VERIFY.md](BUILD_VERIFY.md),
   [SECURITY.md](../SECURITY.md) and the README.
3. Cut a beta and check a downloaded installer with `Get-AuthenticodeSignature`: `Status` should now
   read `Valid`.

SmartScreen also weighs a file's download reputation, which builds up over time even with a trusted
certificate; a new certificate can still meet a warning on its first releases.
