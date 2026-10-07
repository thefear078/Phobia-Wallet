#!/usr/bin/env bash
# Runs the emulator-check APK on a booted Android emulator and proves, from the device itself, that:
#   - the app installs and starts without a crash, and draws its first screen (screenshots kept);
#   - the bundled Tor runs from the native-library folder and finishes bootstrapping (logcat).
#
# The APK is the x86_64 self-test build (scripts/publish-android.sh with ABIS=x86_64,
# ANDROID_RIDS=android-x64, PHOBIA_SELFTEST=1). Used by .github/workflows/device-check.yml.
set -euo pipefail

PKG="com.thefear.phobia"
OUT="${OUT:-device-check}"
mkdir -p "${OUT}"
APK="$(ls desktop/dist/android/*-android.apk | head -n 1)"

adb wait-for-device
adb install -r "${APK}"
adb logcat -c
# The launcher intent, so the activity's generated class name does not matter.
adb shell monkey -p "${PKG}" -c android.intent.category.LAUNCHER 1 > /dev/null

result=""
for i in $(seq 1 60); do   # up to five minutes: a cold Tor bootstrap fetches a consensus first
    sleep 5
    [ "$i" = "4" ] && adb exec-out screencap -p > "${OUT}/first-screen.png"
    log="$(adb logcat -d -s PHOBIA-SELFTEST:I AndroidRuntime:E 2>/dev/null || true)"
    if echo "${log}" | grep -q "FATAL EXCEPTION"; then result="CRASH"; break; fi
    if echo "${log}" | grep -q "TOR-OK"; then result="OK"; break; fi
    # A failure counts only once nothing is still starting (the app logs one result per run).
    if echo "${log}" | grep -q "TOR-FAIL" && [ "$i" -ge 12 ]; then result="FAIL"; break; fi
done

adb exec-out screencap -p > "${OUT}/after-tor.png" || true
adb logcat -d > "${OUT}/logcat.txt" || true
adb shell ps -A -o PID,NAME,ARGS 2>/dev/null | grep -i -E "phobia|libTor" > "${OUT}/processes.txt" || true

echo "----- self-test -----"
grep "PHOBIA-SELFTEST" "${OUT}/logcat.txt" || true
echo "----- processes -----"
cat "${OUT}/processes.txt" || true

case "${result}" in
    OK)    echo "OK: the app started and its bundled Tor bootstrapped on the device." ;;
    CRASH) grep -A 30 "FATAL EXCEPTION" "${OUT}/logcat.txt" | head -60; echo "::error::the app crashed"; exit 1 ;;
    FAIL)  echo "::error::the bundled Tor did not bootstrap on the device"; exit 1 ;;
    *)     echo "::error::no self-test result within five minutes"; exit 1 ;;
esac
