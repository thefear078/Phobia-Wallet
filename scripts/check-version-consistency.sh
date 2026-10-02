#!/usr/bin/env bash
# Fails if the release version in VERSION is not matched byte-for-byte everywhere it is declared.
# A release is blocked (roadmap §2) unless VERSION, the app csproj, the installer script, the README
# download links/badge and the CHANGELOG top entry all agree. Run in CI and before tagging a release.
set -euo pipefail

cd "$(dirname "$0")/.."

VERSION="$(tr -d ' \t\r\n' < VERSION)"
if [ -z "$VERSION" ]; then
  echo "::error::VERSION file is empty"; exit 1
fi
echo "Expected version: $VERSION"
# A beta's files carry its beta number ("Beta-1"); a full release's, the version.
LABEL="$(echo "$VERSION" | sed -E 's/^[0-9]+\.[0-9]+\.[0-9]+-beta\.([0-9]+)$/Beta-\1/')"
echo "File label:       $LABEL"

fail=0
require() {
  # require <human label> <file> <fixed-string that must be present>
  local label="$1" file="$2" needle="$3"
  if [ ! -f "$file" ]; then
    echo "::error::$label: file not found: $file"; fail=1; return
  fi
  if ! grep -qF -- "$needle" "$file"; then
    echo "::error::$label: '$needle' not found in $file"; fail=1
  else
    echo "  ok  $label ($file)"
  fi
}

require "app csproj <Version>"      "desktop/src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj" "<Version>${VERSION}</Version>"
# The installer no longer hardcodes a version: release-windows.ps1 passes it from the csproj and the
# .iss falls back to reading it out of the published exe. A literal here is therefore a REGRESSION —
# it is exactly how a 4.6.0 build once shipped as "UmbrellaWallet-Setup-4.5.0.exe".
if grep -qE '^#define AppVersion "' desktop/installer/phobia.iss; then
  echo "::error::installer: phobia.iss hardcodes a version again; it must derive it from the build"
  fail=1
else
  echo "  ok  installer takes its version from the build (desktop/installer/phobia.iss)"
fi
# shields.io escapes a hyphen as "--" (4.10.0-beta.1 → version-4.10.0--beta.1-).
require "README version badge"      "README.md"                                                   "version-${VERSION//-/--}-"
require "README installer link"     "README.md"                                                   "PhobiaWallet-Setup-${LABEL}.exe"
require "README portable link"      "README.md"                                                   "PhobiaWallet-${LABEL}-win-x64-portable.exe"
require "README linux link"         "README.md"                                                   "PhobiaWallet-${LABEL}-linux-x64.tar.gz"
require "README android link"       "README.md"                                                   "PhobiaWallet-${LABEL}-android.apk"
# Android's versionName is the numeric part; the beta number rides in versionCode.
require "android versionName"       "desktop/src/Umbrella.Wallet.Android/Umbrella.Wallet.Android.csproj" "<ApplicationDisplayVersion>${VERSION%%-*}</ApplicationDisplayVersion>"
require "CHANGELOG entry"           "CHANGELOG.md"                                                 "## [${VERSION}]"

# The release workflow must produce EXACTLY the files the README links to. It did not: the README
# promised a portable .exe and a per-version checksum manifest while the workflow built a .zip and an
# unversioned SHA256SUMS.txt, so the front-page download 404'd on every release until somebody
# uploaded the missing file by hand. Checking the workflow here means the mismatch fails the gate
# instead of being discovered by a user clicking a dead link.
WF=".github/workflows/release.yml"
require "workflow builds the portable exe"  "$WF" 'PhobiaWallet-${{ steps.v.outputs.label }}-win-x64-portable.exe'
require "workflow builds the installer"     "$WF" 'PhobiaWallet-Setup-${{ steps.v.outputs.label }}.exe'
require "workflow builds the linux tarball" "$WF" 'PhobiaWallet-${{ steps.v.outputs.label }}-linux-x64.tar.gz'
require "workflow builds the android apk"   "$WF" 'PhobiaWallet-${{ steps.v.outputs.label }}-android.apk'
require "workflow names the manifest per release" "$WF" 'SHA256SUMS-${{ steps.v.outputs.label }}.txt'
if grep -qF 'UmbrellaWallet-Portable-' "$WF"; then
  echo "::error::workflow still builds a portable ZIP; the README links to a portable EXE"
  fail=1
else
  echo "  ok  workflow no longer builds the zip the README does not mention"
fi

if [ "$fail" -ne 0 ]; then
  echo "Version consistency check FAILED for $VERSION."
  exit 1
fi
echo "Version $VERSION is consistent across all release surfaces."
