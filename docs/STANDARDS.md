# Documentation standard

How every document in this repository is written, checked and kept current. It applies the Altumptic
Product Security Baseline ([PSB.md](PSB.md)) to writing, and it is the reference for anyone, human or
agent, who changes a document.

The rules are short on purpose. When a rule and a document disagree, the rule wins and the document is
fixed in the same change.

---

## 1. Write for a careful reader

- **Plain English.** Short sentences, concrete nouns, no corporate fog (PSB §9). Say what the thing does,
  then what it does not do.
- **Limits sit next to claims.** A sentence that says what is protected is followed, in the same place, by
  what is not. A feature without its limit is not finished.
- **No hype.** No superlatives, no “revolutionary”, no urgency. The calm register of the Altumptic site is
  the target: precise, sourced, honest about what has not happened yet.
- **State dates in full** as `2026-11-08`. Write “next Beta”, not “soon”, and link the pull request that makes
  it true.

## 2. Words that are not used

These are forbidden without substance behind them (PSB §3.3). Use the plain alternative.

| Do not write | Write instead |
|---|---|
| military-grade, bank-grade, bank-level | the algorithm and its parameters, by name (AES-256-GCM, Argon2id 64 MiB) |
| unhackable, unbreakable | what the control stops, and what it does not |
| 100% anonymous, fully private, untraceable | what is hidden, from whom, and what the chain still shows |
| AI-secured, AI-powered security | (do not use “AI” as a security claim) |
| secure from governments, uncensorable | what the wallet does against a specific attacker |
| audited | “not audited” or the name and date of the audit, once one exists |

## 3. Every claim has a proof

A claim is a sentence that a reader could check. Each one points at its proof in the same paragraph or the
same table row:

- a test (`CapabilityMatrixTests`), a file (`release.yml`), a command (`sha256sum -c …`), a signature or an
  attestation, or a CHANGELOG entry;
- a live check names the date it was run.

A claim with no proof is either removed or written as an open item in [ROADMAP.md](ROADMAP.md).

## 4. Labels

The PSB labels (PSB §3.3) appear only where their proof exists, and the proof is linked from the same place:

`PSB-A` · `SIGNED` · `ATTESTED` · `SBOM` · `NO-TELEMETRY` · `OPT-IN-TELEMETRY` · `TOR-CAPABLE` · `TOR-DEFAULT` ·
`FAIL-CLOSED` · `LOCAL-FIRST` · `AUDIT:NONE` · `AUDIT:PLANNED` · `AUDIT:YYYY` · `DISCLOSURE:OK`

Phobia today: `PSB-A`, `SIGNED`, `ATTESTED`, `SBOM`, `NO-TELEMETRY`, `TOR-CAPABLE` (Tor is off until the
user switches it on, so not `TOR-DEFAULT`), `FAIL-CLOSED`, `LOCAL-FIRST`, `AUDIT:NONE`, `DISCLOSURE:OK`.

## 5. Versions and names

- Betas are called **“Beta”** to people: in the app, on release pages, in file names and in these documents.
  Git tags keep their number (`v4.10.0-beta.N`); the number appears in the CHANGELOG heading and nowhere else
  a user reads.
- Version numbers in the same release match everywhere: `VERSION`, the tag, the installer, the README and the
  CHANGELOG heading. [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) checks them.
- The product is **Phobia Wallet**. “Umbrella” appears only in the history and in legacy file names that old
  updaters look for.

## 6. Status rows

Every status row in [ROADMAP.md](ROADMAP.md) and [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) has:

1. a stable ID (`P0.1`, `N.10`, `R.7`) that never changes, even when the row is closed;
2. one status symbol from the legend, and the date the status last changed;
3. what remains and the next step, in one or two sentences;
4. for a done row, where it is proved.

Rows are not deleted. A closed row stays in §5 of the roadmap, so the history of a decision can be found.

## 7. Exceptions

A deviation from the standard is an **exception** (PSB §12). Each one has, in [PSB.md §12](PSB.md#12-exceptions):

- what deviates and why;
- the date it ends (a date, not “for now”);
- the compensating control that applies until then;
- an owner.

An exception past its date is a defect. It is fixed, or renewed by a written decision with a new date.

## 8. Language

- Repository documents are in **English**, so that contributors and auditors can read them. The Ukrainian
  user guide (`docs/guides/user-guide-uk.md`) and the in-app strings are maintained in parallel.
- Every user-visible string exists in all six app languages. A missing translation is a bug.
- Quotations of other projects keep their names and wording exactly.

## 9. Things that never go into a document, a commit or a pull request

- Secret values: passwords, seed phrases, private keys, the contents of signing-key files, base64 secrets.
  Describe where they are kept, never what they are.
- Real transaction or address data from the maintainer's own wallet: amounts, times, addresses and signature
  fragments. Use synthetic fixtures. Screenshots that show a real wallet are not published.
- The maintainer's personal contact details, unless the maintainer publishes them.

## 10. When a document changes

A change to behaviour, a status or a number changes the documents that state it, in the same pull request.
The author checks this list before asking for a merge:

- [ ] [ROADMAP.md](ROADMAP.md): the status row, and the status date at the top.
- [ ] [CHANGELOG.md](../CHANGELOG.md): an entry under *Unreleased*, with a **Security** line if one applies.
- [ ] The README: its results table and its honest part, if a user-visible claim changed.
- [ ] [PSB.md](PSB.md): the proof link, if a requirement is affected.
- [ ] [PRE_BETA_CHECKLIST.md](PRE_BETA_CHECKLIST.md) and [INDEX.md](INDEX.md), if a document was added or its role changed.
- [ ] The link check passes (`pwsh scripts/check-doc-links.ps1`), and the documentation tests pass.

## 11. Review

- **At every release:** [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md), in full.
- **Once a month:** the owner reads ROADMAP, PSB, README and THREAT_MODEL against the code and the release
  page. The review date goes at the top of the document it checked.
- **When a standard changes:** the change goes into this file first, then into the documents it affects,
  within the same week.

---

📖 Back to [Documentation Index](INDEX.md)
