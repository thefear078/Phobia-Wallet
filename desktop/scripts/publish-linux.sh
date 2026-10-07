#!/usr/bin/env bash
# Publishes the Linux build of Phobia Wallet as a self-contained tar.gz.
#
# The Tor and Monero helpers are per-OS binaries; the Windows staging scripts fetch .exe
# files, so on Linux the two helpers are fetched here from the same upstream projects.
# Everything else — wallet, signing, UI — is the same cross-platform .NET/Avalonia code.
set -euo pipefail

cd "$(dirname "$0")/.."
# Portable version extraction (avoids grep -P, which some Git Bash locales reject).
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj)"
OUT="dist/linux/phobia-wallet-${VERSION}-linux-x64"

echo "Publishing Phobia Wallet ${VERSION} for linux-x64…"
dotnet publish src/Umbrella.Wallet.App/Umbrella.Wallet.App.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${OUT}"

# REQUIRE_HELPERS=1 (the release job sets it): a helper that cannot be fetched or does not match its
# pinned hash stops the build. Locally it stays best-effort — the core wallet runs without either.
REQUIRE_HELPERS="${REQUIRE_HELPERS:-0}"
helper_missing() {
    if [ "${REQUIRE_HELPERS}" = "1" ]; then
        echo "error: $1 — a release must not ship without it." >&2
        exit 1
    fi
    echo "  warning: $1 — Linux build ships without it."
}

# Both hashes are pinned, never taken from the server that serves the file. Each was read from the
# project's own signed sums file, its signature checked against the key the Windows scripts pin:
#   Tor:    sha256sums-signed-build.txt for 15.0.24 — Tor Browser Developers EF6E286DDA85EA2A4BA7DE684E2C6E8793298290
#   Monero: getmonero.org/downloads/hashes.txt — binaryFate 81AC591FE9C4B65C5806AFC3F0AF4D462A0BDF92
# 14.5.7, pinned here before, had vanished upstream: Linux builds since then shipped without Tor.

# --- Tor (expert bundle, linux-x86_64) ---
TOR_VERSION="${TOR_VERSION:-15.0.24}"
TOR_SHA256="${TOR_SHA256:-8e012ec6815d7899cb64011582e2dade88e74119c6661068a2a3252de0ccd7f2}"
TOR_ARCHIVE="tor-expert-bundle-linux-x86_64-${TOR_VERSION}.tar.gz"
if [ ! -f "${OUT}/tor/tor" ]; then
    echo "Fetching Tor ${TOR_VERSION}…"
    if mkdir -p "${OUT}/tor" /tmp/umbrella-tor \
        && curl -fL "https://dist.torproject.org/torbrowser/${TOR_VERSION}/${TOR_ARCHIVE}" -o "/tmp/umbrella-tor/${TOR_ARCHIVE}" \
        && echo "${TOR_SHA256}  /tmp/umbrella-tor/${TOR_ARCHIVE}" | sha256sum -c - \
        && tar -xzf "/tmp/umbrella-tor/${TOR_ARCHIVE}" -C /tmp/umbrella-tor; then
        cp /tmp/umbrella-tor/tor/tor "${OUT}/tor/tor" && chmod +x "${OUT}/tor/tor"
        cp /tmp/umbrella-tor/data/geoip  "${OUT}/tor/geoip"  2>/dev/null || true
        cp /tmp/umbrella-tor/data/geoip6 "${OUT}/tor/geoip6" 2>/dev/null || true
    else
        helper_missing "could not fetch or verify Tor ${TOR_VERSION}"
    fi
fi

# --- monero-wallet-rpc (official CLI bundle, linux-x64) ---
MONERO_VERSION="${MONERO_VERSION:-v0.18.5.3}"
MONERO_SHA256="${MONERO_SHA256:-3333d0e04c9cbe3023e7e2b6dd369c407f36bb8df5b05fb0a19d7685b3d56dfc}"
if [ ! -f "${OUT}/monero/monero-wallet-rpc" ]; then
    echo "Fetching Monero ${MONERO_VERSION}…"
    if mkdir -p "${OUT}/monero" /tmp/umbrella-monero \
        && curl -fL "https://downloads.getmonero.org/cli/monero-linux-x64-${MONERO_VERSION}.tar.bz2" -o /tmp/umbrella-monero/monero.tar.bz2 \
        && echo "${MONERO_SHA256}  /tmp/umbrella-monero/monero.tar.bz2" | sha256sum -c - \
        && tar -xjf /tmp/umbrella-monero/monero.tar.bz2 -C /tmp/umbrella-monero; then
        cp /tmp/umbrella-monero/monero-*/monero-wallet-rpc "${OUT}/monero/monero-wallet-rpc" && chmod +x "${OUT}/monero/monero-wallet-rpc"
    else
        helper_missing "could not fetch or verify Monero ${MONERO_VERSION}"
    fi
fi

# Strip the Windows helpers that the build copies from the source tree — dead weight here.
rm -f "${OUT}/tor/tor.exe" "${OUT}/monero/monero-wallet-rpc.exe"

# A menu entry for launchers: the icon ships beside the app, and install-desktop-entry.sh writes a
# .desktop file pointing at wherever the folder was extracted (run it again after moving the folder).
cp src/Umbrella.Wallet.App/Assets/phobia-appicon.png "${OUT}/phobia.png"
cat > "${OUT}/install-desktop-entry.sh" <<'ENTRY'
#!/usr/bin/env sh
# Adds Phobia Wallet to your application menu, pointing at this folder.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$dir"
cat > "$dir/phobia-wallet.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Phobia Wallet
Comment=Self-custody crypto wallet
Exec="$here/phobia-wallet"
Icon=$here/phobia.png
Terminal=false
Categories=Finance;Office;
EOF
echo "Added: $dir/phobia-wallet.desktop"
ENTRY
chmod +x "${OUT}/install-desktop-entry.sh"

# The program is called phobia-wallet. The assembly keeps its name (its resources are addressed by it);
# a single-file apphost reads its own bundle, so renaming the file itself is safe.
mv -f "${OUT}/Umbrella.Wallet.App" "${OUT}/phobia-wallet"

# Ensure the app itself is executable in the tarball (the .NET apphost + bundled helpers).
chmod +x "${OUT}/phobia-wallet" 2>/dev/null || true
chmod +x "${OUT}/tor/tor" "${OUT}/monero/monero-wallet-rpc" 2>/dev/null || true

echo "Packing…"
tar -czf "dist/linux/phobia-wallet-${VERSION}-linux-x64.tar.gz" -C dist/linux \
    "phobia-wallet-${VERSION}-linux-x64"
echo "Done: dist/linux/phobia-wallet-${VERSION}-linux-x64.tar.gz"
echo "Run with: ./phobia-wallet  (./install-desktop-entry.sh adds it to the app menu)"
