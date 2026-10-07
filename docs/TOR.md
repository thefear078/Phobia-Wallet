# Phobia Wallet over Tor

How the wallet reaches the network without handing your IP address to every explorer it asks — and
exactly where that protection ends. The code is `PublicHttp` (`desktop/src/Umbrella.Wallet.Infrastructure/
Network/PublicChainClients.cs`) and `EmbeddedTorService`; the tests named below pin each claim.

## The bundled Tor (Windows and Linux)

- The Windows and Linux builds ship Tor itself, taken from the Tor Browser bundle at a pinned version and
  checked against the Tor Project's **signed** checksums before it is packaged (`desktop/scripts/
  fetch-tor.ps1`, `publish-linux.sh`; see [BUILD_VERIFY.md](BUILD_VERIFY.md)).
- **Settings → Privacy → Tor** starts it as a child process on `127.0.0.1:9250` (or the next free port up to
  9259) — never 9050/9150, so a Tor Browser or system Tor you run is left alone. Its torrc is written by
  the wallet: `ClientOnly 1`, `AvoidDiskWrites 1`, a data directory inside the wallet's own data folder.
- Tor is told which process owns it (`__OwningControllerProcess`), so it exits with the wallet; a copy
  left behind by a crash is found by its pid file and ended at the next start.
- Turned on once, Tor comes back by itself at the next launch.

## The kill-switch (Tor-only)

**Settings → Privacy → Tor-only (block clearnet).** With it on, a request that cannot go through the
proxy is refused inside the HTTP client's connect step — **before DNS, before a socket** — so a dropped or
disabled Tor means "no request", never "quietly direct". Turning it on also starts Tor.

- `NetworkIsolationTests` puts a listener on loopback and proves that with the switch armed nothing
  reaches it, and that without the switch the same client does (so the test measures the switch).
- No `HttpClient` may be built anywhere but `PublicHttp` (one named exception, the loopback Monero
  client) — a test scans the source, because a client built elsewhere would not obey the switch.
- Sends are gated too: the Review and the Confirm step both refuse when the live route is not the one
  chosen in Settings (`SendTransportGate`).

## Separate circuits

Tor puts two connections on different circuits when their SOCKS5 username/password differ. The wallet
gives Tor a different username for every **purpose** — chain data, broadcasts, prices, swaps, maintenance,
a connected exchange account, PayJoin — so the exit relay that saw the wallet ask about an address is not
the one that sees the transaction spending it.

Destinations always go to Tor as **names**: Tor resolves them at the exit, and this machine's DNS server
never hears which explorers the wallet uses.

`SocksHandshakeTests` runs a SOCKS5 server on loopback and reads all of this off the wire. (Before
4.10.0-beta.3 the circuit label was written where .NET's SOCKS client never reads it, and every request
reached Tor with no credentials — one circuit for everything. The test exists so that cannot recur.)

Not per server: a circuit for every explorer was built and measured — a fresh wallet asks some thirty
servers at once, and Tor left the Bitcoin-family balance scans unfinished after four minutes, where one
circuit per purpose read every chain. Within a purpose, then, one exit at a time carries the lookups to
different explorers.

## Verify it

**Settings → Privacy → Verify Tor** asks `check.torproject.org` through the wallet's own route and says
whether the exit really is Tor — or, with the kill-switch armed and Tor down, that the request was
blocked. The connection chip in the sidebar (TOR / PROXY / DIRECT / BLOCKED) reads the same live state
the send gate uses.

## Your own proxy instead

**Settings → Privacy → Custom proxy (SOCKS5)** routes everything through a proxy you run — another Tor,
an SSH tunnel, a VPN's SOCKS port. It and the bundled Tor are mutually exclusive.

- `host:port`, `socks5://` and `socks5h://` all mean SOCKS5 with the name resolved by the proxy.
- `socks4://` is used as **SOCKS4a**: plain SOCKS4 cannot carry a name, so .NET would look the server up
  in this machine's DNS first.
- Anything that is not SOCKS (`http://…`) is refused rather than guessed.

## Android

From Beta 3 the APK carries Tor: the Tor Project's own Android build (`tor-expert-bundle-android-*`,
checked against the same signed sums as the desktop's), shipped as `libTor.so` in the native-library
folder — the one place Android lets an app run a program from, and the way Tor Browser for Android runs
its own Tor. Everything above applies unchanged: the same switch, kill-switch, port range and circuits.
Two differences, both from Android:

- Android may stop Tor while the app sits in the background. The wallet starts it again when it comes
  back to the front (and checks on every refresh tick); until then requests go to Tor's port and are
  refused — never direct.
- A CI job (`device-check`) installs the APK on an Android emulator and proves that Tor starts from the
  native-library folder and bootstraps.

[Orbot](https://orbot.app) still works instead: set the custom proxy to `127.0.0.1:9050` and turn on
Tor-only; the kill-switch stays armed across restarts while the proxy is set. Step by step:
[Phobia on Android](guides/android.md#tor-on-the-phone).

## Monero

Monero's balance and sends go through the local `monero-wallet-rpc`, which talks to a Monero node:

- With Tor (or your proxy) on, the daemon is started with `--proxy`, so the node sees an exit, not you.
- On Android it is the Monero project's Android build, shipped as `libmonero-wallet-rpc.so` beside Tor,
  started and logged in to exactly as on the desktop.
- With the kill-switch armed and no proxy, the daemon is **not started at all**.
- A `.onion` node is only ever used over Tor and is never swapped for a clearnet one.
- The daemon listens on loopback only and runs with a random login it writes to a file only your account
  can read; a web page or another account on the machine gets "401" and nothing else
  (`MoneroRpcLoginTests`, and `MoneroDaemonLoginTests` against the real `monero-wallet-rpc` in CI).

## Where this ends

- Tor hides **who** is asking, not **what** is asked. An explorer still sees the addresses it is asked
  about; on a transparent chain that is the whole point of asking. Choosing which server answers for each
  chain (Settings → Privacy) is the other half — see [PRIVACY.md](../PRIVACY.md).
- What the price circuit carries: one fixed price list, the same for every wallet (it used to include
  the tokens you hold, which told wallets apart), currency rates, the history of the whole market list
  for the Home balance chart (it used to ask for exactly the coins held), and a chart for each coin you
  open. Those requests share one circuit, so that exit and the price service can tell they come from
  one wallet, though not whose.
- Timing and volume are observable. A clearnet server over Tor still has an exit in front of it; a
  `.onion` server does not.
- Every Phobia request carries the same User-Agent, so a server can tell it is talking to this wallet.
- Without Tor or a proxy, every server sees your IP address — the Security Center says so plainly.

---

📖 Back to [Documentation Index](INDEX.md) · [Threat model](../THREAT_MODEL.md) · [Privacy](../PRIVACY.md)
