#!/usr/bin/env bash
# Runs the published Linux build (the folder the release tarball is made from) on a virtual display and
# proves, from the outside, that:
#   - the program starts and draws its first screen (screenshot kept);
#   - with Tor on, its bundled Tor starts and carries traffic: check.torproject.org, asked through the
#     wallet's own SOCKS port, says the request came from Tor.
#
# Needs: xvfb, curl; imagemagick (import) for screenshots if present. Used by .github/workflows/device-check.yml.
set -euo pipefail

OUT="${OUT:-device-check}"
mkdir -p "${OUT}"
APP_DIR="$(ls -d desktop/dist/linux/phobia-wallet-*-linux-x64 | head -n 1)"
DATA="$(mktemp -d)"

# A fresh data folder: English, Tor on (so the wallet starts its own Tor), first-run consent given.
cat > "${DATA}/ui-settings.json" <<'JSON'
{"Language":"en","TorEnabled":true,"TorOnlyMode":true,"AcceptedTermsVersion":1}
JSON

# A screenshot when ImageMagick is there; the proof below does not depend on it.
shot() {
    if command -v import > /dev/null; then import -window root "$1" || true
    else echo "(no screenshot $1: ImageMagick not installed)"; fi
}

command -v Xvfb > /dev/null || { echo "::error::Xvfb not found"; exit 1; }
export DISPLAY=:99
Xvfb :99 -screen 0 1400x900x24 > /dev/null 2>&1 &
XVFB=$!
trap 'kill ${APP:-0} ${XVFB} 2>/dev/null || true' EXIT
sleep 2

UMBRELLA_DATA_DIR="${DATA}" "${APP_DIR}/phobia-wallet" > "${OUT}/linux-stdout.txt" 2>&1 &
APP=$!

sleep 15
kill -0 "${APP}" 2>/dev/null || { cat "${OUT}/linux-stdout.txt"; echo "::error::the Linux build exited at start"; exit 1; }
shot "${OUT}/linux-first-screen.png"

answer=""
for i in $(seq 1 48); do   # up to four minutes for a cold Tor bootstrap
    for port in 9250 9251 9252; do
        answer="$(curl -s --max-time 20 --socks5-hostname "127.0.0.1:${port}" https://check.torproject.org/api/ip || true)"
        [ -n "${answer}" ] && break 2
    done
    sleep 5
done
shot "${OUT}/linux-after-tor.png"
ps -eo pid,args | grep -E "[t]or/tor|[p]hobia-wallet" > "${OUT}/linux-processes.txt" || true
cat "${OUT}/linux-processes.txt"

echo "check.torproject.org via the wallet's Tor: ${answer:-<no answer>}"
echo "${answer}" | grep -q '"IsTor":true' || { echo "::error::the bundled Tor did not carry a request"; exit 1; }
kill -0 "${APP}" 2>/dev/null || { echo "::error::the Linux build exited while Tor started"; exit 1; }
echo "OK: the Linux build started, drew its window and its bundled Tor carries traffic."
