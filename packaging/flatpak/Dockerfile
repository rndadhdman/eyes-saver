# Build environment for the Flatpak bundle. Run privileged (flatpak-builder uses bubblewrap).
# Debian 13 ships flatpak-builder 1.4, which composes AppStream data with the host's appstreamcli;
# 1.2 expects appstream-compose inside the SDK, which current Freedesktop SDKs no longer include.
FROM debian:trixie-slim
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
       flatpak flatpak-builder ca-certificates appstream appstream-compose \
    && rm -rf /var/lib/apt/lists/*
