# Release checklist

What has to be true before a release is announced: the Altumptic PSB release definition of done (§11),
in the order it is checked. Conformance as a whole: [PSB.md](PSB.md).

## Before tagging

- [ ] The full offline suite and the rendered-screen tests are green on `main`; the Live suite was run
      and its failures are explained (rate limits are named, not hidden).
- [ ] No new cryptographic primitive outside [PSB.md §2.1](PSB.md#21-what-is-used-and-where); no new outbound
      server without an entry in the network catalogue (the test enforces it).
- [ ] No telemetry, analytics or crash reporting added.
- [ ] Fail-closed behaviour touched by this release has a test (kill-switch, updater, send settlement).
- [ ] `CHANGELOG.md` has a **Security** section for the release (or says "no security-relevant changes").
- [ ] Limits and audit status are current: README "The honest part", [AUDIT_STATUS.md](../AUDIT_STATUS.md),
      [PSB.md exceptions](PSB.md#12-exceptions): none past its date.
- [ ] `.well-known/security.txt` has an `Expires` date more than three months away, and its contacts work.

## The workflow does (and fails the release otherwise)

- Builds each platform on its own runner; signs the Windows programs (Authenticode, pinned thumbprint)
  and the APK (pinned fingerprint).
- Writes `SHA256SUMS-<label>.txt` and checks every line against the files.
- Attaches the SPDX SBOM, the build attestation (`provenance-<label>.sigstore.json`) and the signed
  checksum list (`SHA256SUMS-<label>.txt.sig`, checked against [keys/phobia-release-signing.pem](../keys/phobia-release-signing.pem)).

## After the workflow, on a machine that did not build it

```bash
bash scripts/verify-published-release.sh v<version>
```

It downloads the release, checks the checksum list's signature, every file against the list, and the
build attestations: the same steps [BUILD_VERIFY.md](BUILD_VERIFY.md) gives everyone else.

- [ ] The script passes.
- [ ] An installed copy of the previous Beta finds the update, downloads it and accepts it.
- [ ] README, docs and the release page name the version and capabilities this release actually has.

## Release notes template

```markdown
**Phobia Wallet Beta: <date>.** A pre-release: back up your recovery phrase before installing.

### Download
(the table of files, as in the previous release)

Check before running: `sha256sum -c SHA256SUMS-Beta.txt --ignore-missing`,
`openssl dgst -sha256 -verify phobia-release-signing.pem -signature SHA256SUMS-Beta.txt.sig SHA256SUMS-Beta.txt`
and `gh attestation verify <file> --repo thefear078/Phobia-Wallet`. Keys and fingerprints:
[BUILD_VERIFY.md](https://github.com/thefear078/Phobia-Wallet/blob/main/docs/BUILD_VERIFY.md).

### Security
- (each security-relevant change, in plain words: what was wrong or weak, who could have used it, what
  changed; IDs from SECURITY/VULNERABILITY_HISTORY.md)
- or: No security-relevant changes in this release.

### What's new
- ...

`PSB-A` · `SIGNED` · `ATTESTED` · `SBOM` · `NO-TELEMETRY` · `TOR-CAPABLE` · `FAIL-CLOSED` · `LOCAL-FIRST` · `AUDIT:NONE` · `DISCLOSURE:OK`
```

Words not used about the wallet, here or anywhere: "military-grade", "unhackable", "100% anonymous",
"AI-secured", "audited" (until it is), "bank-level".

---

📖 Back to [Documentation Index](INDEX.md)
