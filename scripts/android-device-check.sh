#!/usr/bin/env bash
# Runs the emulator-check APK on a booted Android emulator and proves, from the device itself, that:
#   - the app installs and starts without a crash, and draws its first screen (screenshots kept);
#   - the bundled Tor runs from the native-library folder and finishes bootstrapping (logcat);
#   - when the APK carries monero-wallet-rpc for the device's ABI, the bundled Monero service starts
#     behind its login, restores a wallet and reads the chain height from a node through that Tor.
#
# The APK is a self-test build (scripts/publish-android.sh with PHOBIA_SELFTEST=1): x86_64 for the main
# check, or ARM64 — the binaries phones get — on an x86_64 image that translates ARM code.
# TOR_REQUIRED (default 1) and MONERO_REQUIRED (default 0) say which results fail the check.
# Used by .github/workflows/device-check.yml.
set -euo pipefail

PKG="com.thefear.phobia"
OUT="${OUT:-device-check}"
TOR_REQUIRED="${TOR_REQUIRED:-1}"
MONERO_REQUIRED="${MONERO_REQUIRED:-0}"
mkdir -p "${OUT}"
APK="$(ls desktop/dist/android/*-android.apk | head -n 1)"

adb wait-for-device
echo "device ABIs: $(adb shell getprop ro.product.cpu.abilist | tr -d '\r')"
adb install -r "${APK}"
adb logcat -c
# The launcher intent, so the activity's generated class name does not matter.
adb shell monkey -p "${PKG}" -c android.intent.category.LAUNCHER 1 > /dev/null

selftest_log() { adb logcat -d -s PHOBIA-SELFTEST:I AndroidRuntime:E 2>/dev/null || true; }

tor=""
for i in $(seq 1 72); do   # up to six minutes: a cold Tor bootstrap fetches a consensus first
    sleep 5
    [ "$i" = "4" ] && adb exec-out screencap -p > "${OUT}/first-screen.png"
    log="$(selftest_log)"
    if echo "${log}" | grep -q "FATAL EXCEPTION"; then tor="CRASH"; break; fi
    if echo "${log}" | grep -q "TOR-OK"; then tor="OK"; break; fi
    # A failure counts only once nothing is still starting (the app logs one result per run).
    if echo "${log}" | grep -q "TOR-FAIL" && [ "$i" -ge 12 ]; then tor="FAIL"; break; fi
done
adb exec-out screencap -p > "${OUT}/after-tor.png" || true

monero=""
if [ "${tor}" = "OK" ] || [ "${tor}" = "FAIL" ]; then
    for i in $(seq 1 72); do   # up to six minutes: start, restore, and the first answer from a node
        log="$(selftest_log)"
        if echo "${log}" | grep -q "FATAL EXCEPTION"; then monero="CRASH"; break; fi
        if echo "${log}" | grep -q "MONERO-OK"; then monero="OK"; break; fi
        if echo "${log}" | grep -q "MONERO-SKIP"; then monero="SKIP"; break; fi
        if echo "${log}" | grep -q "MONERO-FAIL"; then monero="FAIL"; break; fi
        sleep 5
    done
fi

adb logcat -d > "${OUT}/logcat.txt" || true
adb shell ps -A -o PID,NAME,ARGS 2>/dev/null | grep -i -E "phobia|libTor|monero" > "${OUT}/processes.txt" || true

echo "----- self-test -----"
grep "PHOBIA-SELFTEST" "${OUT}/logcat.txt" || true
echo "----- processes -----"
cat "${OUT}/processes.txt" || true
echo "tor=${tor:-none} monero=${monero:-none}"

if [ "${tor}" = "CRASH" ] || [ "${monero}" = "CRASH" ]; then
    grep -A 30 "FATAL EXCEPTION" "${OUT}/logcat.txt" | head -60
    echo "::error::the app crashed"; exit 1
fi

failed=0
case "${tor}" in
    OK) echo "OK: the app started and its bundled Tor bootstrapped on the device." ;;
    FAIL) echo "::warning::the bundled Tor did not bootstrap on the device"; [ "${TOR_REQUIRED}" = "1" ] && failed=1 ;;
    *) echo "::error::no self-test result within six minutes"; exit 1 ;;
esac
case "${monero}" in
    OK) echo "OK: the bundled Monero service started, restored a wallet and read the chain on the device." ;;
    SKIP) echo "Monero: this APK carries no monero-wallet-rpc for the device's ABI (none is published for x86_64)."
          if [ "${MONERO_REQUIRED}" = "1" ]; then echo "::error::Monero was required but is not in the APK"; failed=1; fi ;;
    *) echo "::warning::the bundled Monero service did not reach the chain on the device (${monero:-no result})"
       if [ "${MONERO_REQUIRED}" = "1" ]; then failed=1; fi ;;
esac
exit "${failed}"
