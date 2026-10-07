#!/usr/bin/env bash
# Publishes the Android build of Phobia Wallet as one APK (64-bit and 32-bit ARM).
#
# Needs the .NET 8 SDK with the android workload (`dotnet workload install android`), a JDK 17 and
# an Android SDK; GitHub's ubuntu runners carry the last two. The phone app is the same view models,
# signing code and pages as the desktop, shown through Views/MobileShell.
#
#   ./scripts/publish-android.sh
#
# Signing: with ANDROID_KEYSTORE (a .jks/.keystore path), ANDROID_KEY_ALIAS and ANDROID_KEY_PASS set,
# the APK is signed with that key - the one an installed copy must keep seeing for updates to install
# over it. Without them the build is signed with the SDK's debug key, which is fine for a check build
# and not for a release.
#
# Tor and monero-wallet-rpc are staged first by fetch-android-helpers.sh (pinned, hash-checked); with
# REQUIRE_HELPERS=1, as the release sets it, the APK must carry both for every ABI or the build fails.
# ANDROID_RIDS (default "android-arm64;android-arm") picks the ABIs; ABIS must name the same ones for
# the helpers. PHOBIA_SELFTEST=1 builds the emulator check (starts Tor at launch and logs the result) -
# never a release.
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj)"
OUT="dist/android/publish"

ABIS="${ABIS:-arm64-v8a armeabi-v7a}" ./scripts/fetch-android-helpers.sh

args=(-c Release -f net8.0-android -o "${OUT}")
[ -n "${ANDROID_RIDS:-}" ] && args+=("-p:PhobiaAndroidRids=${ANDROID_RIDS}")
[ "${PHOBIA_SELFTEST:-0}" = "1" ] && args+=("-p:PhobiaSelfTest=true")
SDK_DIR="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
[ -n "${SDK_DIR}" ] && args+=("-p:AndroidSdkDirectory=${SDK_DIR}")
[ -n "${JAVA_HOME:-}" ] && args+=("-p:JavaSdkDirectory=${JAVA_HOME}")
if [ -n "${ANDROID_KEYSTORE:-}" ]; then
    # env: makes the build read the password from the environment, so it never sits in a command line.
    args+=(-p:AndroidKeyStore=true
           "-p:AndroidSigningKeyStore=${ANDROID_KEYSTORE}"
           "-p:AndroidSigningKeyAlias=${ANDROID_KEY_ALIAS:-phobia}"
           -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASS
           -p:AndroidSigningStorePass=env:ANDROID_KEY_PASS)
fi

echo "Publishing Phobia Wallet ${VERSION} for Android…"
dotnet publish src/Umbrella.Wallet.Android/Umbrella.Wallet.Android.csproj "${args[@]}"

APK="$(ls "${OUT}"/*-Signed.apk | head -n 1)"

# What the APK actually carries, ABI by ABI - and for a release, proof that nothing is missing.
helpers="$(unzip -l "${APK}" | awk '{print $4}' | grep -E '^lib/[^/]+/lib(Tor|monero-wallet-rpc)\.so$' || true)"
echo "Bundled helpers:"; echo "${helpers:-  (none)}" | sed 's/^/  /'
if [ "${REQUIRE_HELPERS:-0}" = "1" ]; then
    for abi in ${ABIS:-arm64-v8a armeabi-v7a}; do
        for lib in libTor.so libmonero-wallet-rpc.so; do
            echo "${helpers}" | grep -qx "lib/${abi}/${lib}" || { echo "error: the APK has no lib/${abi}/${lib}" >&2; exit 1; }
        done
    done
fi
cp "${APK}" "dist/android/phobia-wallet-${VERSION}-android.apk"
echo "Done: dist/android/phobia-wallet-${VERSION}-android.apk ($(du -h "${APK}" | cut -f1))"
