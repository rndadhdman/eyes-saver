#!/bin/bash
# Builds a single-file Flatpak bundle. Runs inside the container from ./Dockerfile with /pkg laid
# out like build-linux.sh expects. The Flathub runtimes are cached in the flatpak user directory,
# which build.ps1 keeps in a Docker volume.
set -euo pipefail

VERSION="$1"
APP_ID="io.github.eyessaver.EyesSaver"
RUNTIME_VERSION="$(sed -n "s/^runtime-version: '\(.*\)'/\1/p" /pkg/flatpak/$APP_ID.yml)"
OUT=/pkg/out
mkdir -p "$OUT"

flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y --noninteractive flathub \
  "org.freedesktop.Platform//$RUNTIME_VERSION" "org.freedesktop.Sdk//$RUNTIME_VERSION"

cd /pkg/flatpak
# State and build directories must share a filesystem, so keep both in the container's /tmp.
flatpak-builder --user --force-clean --disable-rofiles-fuse --disable-cache \
  --state-dir=/tmp/flatpak-state --repo=/tmp/repo /tmp/build "$APP_ID.yml"

flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo \
  /tmp/repo "$OUT/EyesSaver-$VERSION-x86_64.flatpak" "$APP_ID"

ls -l "$OUT/EyesSaver-$VERSION-x86_64.flatpak"
