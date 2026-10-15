#!/usr/bin/env bash
# Stages Tor and monero-wallet-rpc for the Android app, from the projects' own Android builds.
#
# Android runs a program only from an app's native-library folder, so both ship inside the APK as
# lib*.so and Android unpacks them there at install - the way Tor Browser and Orbot run Tor:
#   Tor:    the Tor Project's expert bundle for Android - tor/libTor.so, an executable despite the name
#   Monero: the Monero project's Android CLI build - monero-wallet-rpc, renamed libmonero-wallet-rpc.so
#
# Every archive is checked against a SHA-256 pinned here, never taken from the server that serves it.
# Each pin was read from the project's signed sums file, its signature checked against the key the
# Windows scripts pin (and the supply-chain CI job re-checks the pins against those signed files):
#   Tor:    sha256sums-signed-build.txt for 15.0.25 - Tor Browser Developers EF6E286DDA85EA2A4BA7DE684E2C6E8793298290
#   Monero: getmonero.org/downloads/hashes.txt      - binaryFate 81AC591FE9C4B65C5806AFC3F0AF4D462A0BDF92
#
#   ./scripts/fetch-android-helpers.sh                       # arm64-v8a and armeabi-v7a (the release APK)
#   ABIS="x86_64" ./scripts/fetch-android-helpers.sh         # an emulator test build (Tor only: Monero
#                                                            # publishes no x86_64 Android build)
#
# REQUIRE_HELPERS=1 (the release job sets it): anything that cannot be fetched or does not match its pin
# stops the build. Otherwise the APK is built without it and the phone says Tor is not bundled.
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="src/Umbrella.Wallet.Android/natives"
ABIS="${ABIS:-arm64-v8a armeabi-v7a}"
REQUIRE_HELPERS="${REQUIRE_HELPERS:-0}"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

TOR_VERSION="15.0.25"
MONERO_VERSION="v0.18.5.3"

missing() {
    if [ "${REQUIRE_HELPERS}" = "1" ]; then
        echo "error: $1 - a release APK must not ship without it." >&2
        exit 1
    fi
    echo "  warning: $1 - this APK ships without it."
}

# fetch <url> <sha256> <file>: download and check against the pin; non-zero on any failure.
fetch() {
    curl -fsSL --retry 3 "$1" -o "${WORK}/$3" && echo "$2  ${WORK}/$3" | sha256sum -c --quiet -
}

for abi in ${ABIS}; do
    case "${abi}" in
        arm64-v8a)
            tor_arch="aarch64"; tor_sha="a1a67ec77ce0a7c4fca446fd9f915fbb0302c3107c2d56ab213196187cfd894a"
            monero_arch="armv8"; monero_sha="2621293d288d5ce23d251d1f4106d7e1bdd5bf3a37423e8a8c6851e4edba1768" ;;
        armeabi-v7a)
            tor_arch="armv7"; tor_sha="4fcdb5540f7ea2f529b9063115d66dd7dfa61e5602bfaab96a78e8046bb4b8d5"
            monero_arch="armv7"; monero_sha="542cc0322a7d0d3c9cd47c7cc8d195bb74861a7eec79235b58a5877a90b04e6a" ;;
        x86_64)
            tor_arch="x86_64"; tor_sha="11dbac44e2ee29df2cd6e3a5f38164d2630b2c85342f6a041b9cb57d7012d071"
            monero_arch=""; monero_sha="" ;;
        *) echo "error: unknown ABI ${abi}" >&2; exit 1 ;;
    esac
    mkdir -p "${OUT}/${abi}"

    tor_archive="tor-expert-bundle-android-${tor_arch}-${TOR_VERSION}.tar.gz"
    if [ -f "${OUT}/${abi}/libTor.so" ]; then
        echo "${abi}: Tor already staged"
    elif fetch "https://dist.torproject.org/torbrowser/${TOR_VERSION}/${tor_archive}" "${tor_sha}" "${tor_archive}" \
        && tar -xzf "${WORK}/${tor_archive}" -C "${WORK}" tor/libTor.so \
        && mv "${WORK}/tor/libTor.so" "${OUT}/${abi}/libTor.so"; then
        echo "${abi}: Tor ${TOR_VERSION} staged ($(du -h "${OUT}/${abi}/libTor.so" | cut -f1))"
    else
        missing "could not fetch or verify Tor ${TOR_VERSION} for ${abi}"
    fi

    [ -n "${monero_arch}" ] || { echo "${abi}: no Monero Android build for this ABI"; continue; }
    monero_archive="monero-android-${monero_arch}-${MONERO_VERSION}.tar.bz2"
    if [ -f "${OUT}/${abi}/libmonero-wallet-rpc.so" ]; then
        echo "${abi}: Monero already staged"
    elif fetch "https://downloads.getmonero.org/cli/${monero_archive}" "${monero_sha}" "${monero_archive}" \
        && tar -xjf "${WORK}/${monero_archive}" -C "${WORK}" --wildcards '*/monero-wallet-rpc' \
        && mv "${WORK}"/monero-*/monero-wallet-rpc "${OUT}/${abi}/libmonero-wallet-rpc.so"; then
        echo "${abi}: Monero ${MONERO_VERSION} staged ($(du -h "${OUT}/${abi}/libmonero-wallet-rpc.so" | cut -f1))"
    else
        missing "could not fetch or verify Monero ${MONERO_VERSION} for ${abi}"
    fi
done
