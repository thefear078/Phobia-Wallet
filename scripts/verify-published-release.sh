#!/usr/bin/env bash
# Verify a PUBLISHED GitHub release against its own checksum manifest (roadmap P0.3).
#
# The release workflow already self-verifies before publishing: it downloads the attached artifacts,
# checks the expected set is complete, generates SHA256SUMS and runs `sha256sum -c` over it. What it
# cannot check is what the release page serves AFTERWARDS — an asset replaced or re-uploaded later
# looks exactly like the original to anybody who trusts the page.
#
# So this downloads what the release actually serves, right now, and checks every file against the
# manifest attached to it, the manifest's signature against the published key (keys/), and every file's
# build attestation. It is the same thing a user does by hand from docs/BUILD_VERIFY.md, which is the
# point: the instructions the project gives people are run by the project too.
#
# Usage:
#   bash scripts/verify-published-release.sh            # the latest release
#   bash scripts/verify-published-release.sh v4.8.2     # a specific tag
#
# Requires: gh (authenticated), sha256sum, openssl.
set -euo pipefail

TAG="${1:-}"
REPO="${GH_REPO:-thefear078/Phobia-Wallet}"
KEY="$(cd "$(dirname "$0")/.." && pwd)/keys/phobia-release-signing.pem"
# Releases published from this date on must carry SHA256SUMS-<label>.txt.sig; earlier ones never did.
SIGNED_SINCE="2026-10-29"

command -v gh > /dev/null || { echo "gh is required (https://cli.github.com)"; exit 2; }

if [ -z "$TAG" ]; then
  TAG="$(gh release view --repo "$REPO" --json tagName --jq .tagName)"
fi

echo "Verifying published release $TAG of $REPO"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "PhobiaWallet-*" --pattern "UmbrellaWallet-*"
gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "SHA256SUMS-*.txt"
# Not every release has these; their absence is judged below, not here.
gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "SHA256SUMS-*.txt.sig" 2>/dev/null || true
gh release download "$TAG" --repo "$REPO" --dir "$WORK" --pattern "provenance-*.sigstore.json" 2>/dev/null || true
PUBLISHED="$(gh release view "$TAG" --repo "$REPO" --json publishedAt --jq .publishedAt)"

cd "$WORK"

manifests=(SHA256SUMS-*.txt)
if [ ! -e "${manifests[0]}" ]; then
  echo "::error::$TAG has no SHA256SUMS manifest attached — nothing to verify against."
  echo "A release without one asks every downloader to trust the page instead of checking it."
  exit 1
fi

# A beta carries two: SHA256SUMS-Beta.txt for its files, and SHA256SUMS-Beta-<n>.txt for the numbered
# copies that the updaters of Beta 3 and older look for. Each is checked on its own.
for manifest in "${manifests[@]}"; do
  # v4.5.0 shipped a manifest that was zero bytes. It verified nothing, and nothing said so.
  if [ ! -s "$manifest" ]; then
    echo "::error::$manifest is empty."
    exit 1
  fi
  echo "----- $manifest -----"
  cat "$manifest"
  echo "---------------------"
done

# Every artifact the release serves must be named in a manifest. A file that is downloadable but
# unlisted is precisely the one worth substituting.
unlisted=0
for f in PhobiaWallet-* UmbrellaWallet-*; do
  [ -e "$f" ] || continue
  if ! cat "${manifests[@]}" | grep -Fq " $f" && ! cat "${manifests[@]}" | grep -Fq "*$f"; then
    echo "::error::$f is attached to the release but missing from every manifest"
    unlisted=1
  fi
done
[ "$unlisted" -eq 0 ] || exit 1

# And every line of every manifest must match the bytes actually served.
for manifest in "${manifests[@]}"; do
  sha256sum -c "$manifest"
done

# The manifest itself must be the project's: signed with the key in keys/. The wallet's updater refuses
# a release without this signature, so a release that should have one and does not is an error.
signed=0
for sig in SHA256SUMS-*.txt.sig; do
  [ -e "$sig" ] || continue
  command -v openssl > /dev/null || { echo "openssl is required to check $sig"; exit 2; }
  openssl dgst -sha256 -verify "$KEY" -signature "$sig" "${sig%.sig}"
  signed=1
done
if [ "$signed" -eq 0 ]; then
  if [[ "$PUBLISHED" > "$SIGNED_SINCE" ]]; then
    echo "::error::$TAG was published on ${PUBLISHED%%T*} but carries no SHA256SUMS-<label>.txt.sig."
    echo "Every copy of the wallet from that time on will refuse it as an update."
    exit 1
  fi
  echo "No signed manifest: $TAG was published on ${PUBLISHED%%T*}, before manifests were signed."
fi

# Who built the files: the build attestation, checked against the bundle attached to the release.
bundles=(provenance-*.sigstore.json)
if [ -e "${bundles[0]}" ]; then
  for f in PhobiaWallet-* SHA256SUMS-*.txt; do
    [ -e "$f" ] || continue
    # The numbered copies' manifest has an attestation of its own, kept by GitHub rather than attached.
    gh attestation verify "$f" --repo "$REPO" --bundle "${bundles[0]}" > /dev/null 2>&1 ||
      gh attestation verify "$f" --repo "$REPO" > /dev/null
    echo "$f: attestation OK"
  done
else
  echo "No attestation bundle on $TAG (releases before 4.8.0 have none)."
fi

echo
echo "OK — $TAG matches ${manifests[*]}, byte for byte."
