# Bundled Tor

Umbrella ships its own Tor client so "anonymous traffic" needs no external install.
`EmbeddedTorService` launches `tor.exe` from this folder as a child process, on a private
SOCKS port (**9250**) so a Tor Browser the user already runs is never disturbed.

## Contents (not committed)

| File     | Purpose                                    |
| -------- | ------------------------------------------ |
| `tor.exe`| The Tor client (Windows Expert Bundle)     |
| `geoip`  | IPv4 GeoIP database                        |
| `geoip6` | IPv6 GeoIP database                        |

These are third-party binaries (~35 MB), so they are **git-ignored** rather than committed —
see `.gitignore`. The build copies whatever is here into the output directory, and the
installer packages it.

## Fetching them

```powershell
pwsh desktop/scripts/fetch-tor.ps1
```

The script downloads the official **Tor Expert Bundle** from `dist.torproject.org`, verifies
the archive, and extracts `tor.exe` + the GeoIP databases into this folder.

Verified working with **Tor 0.4.9.11** (bootstraps to 100% in ~20 s).

## Licence

Tor is distributed under the 3-clause BSD licence by The Tor Project, Inc.
Bundling it does not change that licence; it is redistributed unmodified.
