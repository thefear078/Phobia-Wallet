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
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj)"
OUT="dist/android/publish"

args=(-c Release -f net8.0-android -o "${OUT}")
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
cp "${APK}" "dist/android/phobia-wallet-${VERSION}-android.apk"
echo "Done: dist/android/phobia-wallet-${VERSION}-android.apk ($(du -h "${APK}" | cut -f1))"
