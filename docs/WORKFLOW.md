# Development workflow

How to change Phobia Wallet without losing the roadmap, the limits or the security baseline.

Hub: [INDEX.md](INDEX.md) · Backlog: [ROADMAP.md](ROADMAP.md) · Writing rules: [STANDARDS.md](STANDARDS.md) ·
Release: [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) · Public-beta gate: [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md)

---

## 1. What comes first

The order of work follows [ROADMAP.md §1](ROADMAP.md#1-now). In short:

1. **Fund safety and the current release** (P0, and the Now table): anything that stops a send, shows a wrong
   balance or delays a release comes before anything else.
2. **User-facing gaps** (P1).
3. **New networks and features** (P2). A new network follows [adding-a-chain.md](adding-a-chain.md) in full.
4. **Platforms and maintainability** (P3).

No new feature starts while a P0 item is open, unless the user asks for it.

---

## 2. A change, from start to merge

1. **Branch** from the current base (`main`, or the branch a stacked change depends on). Name the branch for
   the work, for example `claude/<topic>`.
2. **Reproduce first.** A fix starts with a test that fails on the current code. A new behaviour starts with the
   test that describes it.
3. **Change the code and the documents together.** The checklist in [STANDARDS.md §10](STANDARDS.md#10-when-a-document-changes)
   names every document a change can touch.
4. **Run the tests** (below). A change to the send path, the vault, key derivation or the update path also runs
   the Live suite, with the reason written in the pull request.
5. **Keep signed bytes exact.** Signed fixtures and the signed checksum list are stored byte for byte; `.gitattributes` keeps their line endings from changing.
6. **Commit** with a message that says what changed and why, and names any security effect in its first
   lines. Do not commit secrets, real wallet data or signing-key contents.
7. **Pull request.** Use [.github/pull_request_template.md](../.github/pull_request_template.md). The description
   says what to check by hand, what is not tested, and which proofs changed.
8. **Merge** after CI is green and every review thread is resolved. The maintainer merges.

Stacked changes say so at the top of the description, and the base is merged first.

---

## 3. Commands

Run from the repository root. The solution is `desktop/Umbrella.Wallet.sln`.

| Task | Command |
|---|---|
| Build | `dotnet build desktop/Umbrella.Wallet.sln -c Release` |
| Offline tests (CI runs these) | `dotnet test desktop/Umbrella.Wallet.sln -c Debug --filter "Category!=Live"` |
| One test class | `dotnet test desktop/tests/Umbrella.Wallet.Core.Tests -c Debug --filter "FullyQualifiedName~ClassName"` |
| Live tests (real explorers, nodes, GitHub; run by hand) | `dotnet test desktop/tests/Umbrella.Wallet.Core.Tests -c Release --filter "Category=Live"` |
| Run the app against scratch data | set `UMBRELLA_DATA_DIR` to an empty folder first; never point a test or a run at a real wallet's data |

The offline suite takes about 20 minutes. Run a filtered set while working and the full suite before a merge.

On Windows, the .NET SDK is run by its full path, with the NuGet and temporary folders on a drive with space.
The local setup is in the maintainer's own notes; it is not a repository requirement.

---

## 4. When behaviour changes

| If you change… | Also update… |
|---|---|
| Attack surface or residual risk | [THREAT_MODEL.md](../THREAT_MODEL.md), and [security-model.md](security-model.md) if the design changes |
| What leaves the device, or who is contacted | [PRIVACY.md](../PRIVACY.md), the network catalogue (`NetworkCounterpartyTests` enforces it); [PRIVACY_POLICY.md](../PRIVACY_POLICY.md) if the store-facing text changes |
| A roadmap promise | The status row in [ROADMAP.md](ROADMAP.md), and its status date |
| A user-visible capability | The README and the coin matrix, and only after the end-to-end path works ([CONTRIBUTING.md](../CONTRIBUTING.md)) |
| A cryptographic primitive, a key format, a signature rule | [PSB.md §2](PSB.md#2-cryptography-and-data-protection) and [SECURITY_REVIEW.md](SECURITY_REVIEW.md), with a test that pins the format |
| A release process step | [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) and [BUILD_VERIFY.md](BUILD_VERIFY.md) |
| A security-relevant fix | [CHANGELOG.md](../CHANGELOG.md) under **Security**, [SECURITY/VULNERABILITY_HISTORY.md](../SECURITY/VULNERABILITY_HISTORY.md) with the next ID, and [SECURITY.md](../SECURITY.md) if the table there changes |

Keep the honesty rule from [MANIFESTO.md](../MANIFESTO.md): say the limit next to the promise.

---

## 5. When closing a roadmap item

1. Change the row in [ROADMAP.md](ROADMAP.md): the status symbol, the date, and where it is proved. Move the row to
   §5 (Done) if it is finished.
2. If the change is visible to a user, add a CHANGELOG entry under *Unreleased*.
3. If it blocks the public beta, tick the item in [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md).

---

## 6. Checks

| Check | Command | What it stops |
|---|---|---|
| Documentation links and anchors | `pwsh scripts/check-doc-links.ps1` (or `bash scripts/check-doc-links.sh`) | Broken links in the documentation |
| Pinned third-party binaries | `bash scripts/check-pinned-binaries.sh` | The fetch scripts and [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) describing different downloads |
| Version consistency | `bash scripts/check-version-consistency.sh` | `VERSION`, the project files, the installer, the README and the CHANGELOG disagreeing |
| Reproducible build | `bash scripts/verify-reproducible-build.sh` | A build that cannot be reproduced from its tag |
| Published release | `bash scripts/verify-published-release.sh [tag]` | A release whose served files no longer match its signed checksum list. Run by hand after a release. |

The first three run in CI on every pull request. The last one needs `gh` and a published release.

---

## 7. Pull request rules

- Small, single-purpose changes. Do not mix a crypto change, a UI change, a document change and a release change.
- CI must pass. `main` requires it.
- Squash merge. The commit message on `main` is the pull request title.
- Every review thread is resolved before merging.
- No secrets in logs or git history. Gitleaks runs on every push.
- Do not force-push a published branch that someone else has pulled, and never move a release tag. Tags are
  immutable by repository rule.

---

📖 Back to [Documentation Index](INDEX.md)
