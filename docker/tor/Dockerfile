# Minimal Tor daemon — official Alpine package, no third-party images.
# Starts as root so it can fix ownership of the mounted key volume,
# then drops privileges to the `tor` user (see `User tor` in torrc).
FROM alpine:3.20
RUN apk add --no-cache tor
ENTRYPOINT ["sh", "-c", "chown -R tor:tor /var/lib/tor && chmod 700 /var/lib/tor && exec tor -f /etc/tor/torrc"]
