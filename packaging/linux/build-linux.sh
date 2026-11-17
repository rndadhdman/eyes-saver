#!/bin/bash
# Builds the portable tarball and the .deb from an already published linux-x64 folder.
# Runs inside a Debian container (see build.ps1). Layout of /pkg:
#   app/      published files (EyesSaver, libSkiaSharp.so, libHarfBuzzSharp.so)
#   icons/    eyessaver-<size>.png
#   linux/    this folder (desktop file, metainfo, readme)
#   out/      results are written here
set -euo pipefail

VERSION="$1"
APP_ID="io.github.eyessaver.EyesSaver"
SRC=/pkg
OUT=/pkg/out
mkdir -p "$OUT"

# ------------------------------------------------------------------ portable tarball
TARROOT=/tmp/tar
PORTABLE="$TARROOT/eyessaver-$VERSION"
rm -rf "$TARROOT"
mkdir -p "$PORTABLE/data"
install -m755 "$SRC/app/EyesSaver" "$PORTABLE/EyesSaver"
install -m644 "$SRC"/app/*.so "$PORTABLE/"
install -m644 "$SRC/icons/eyessaver-256.png" "$PORTABLE/eyessaver.png"
install -m644 "$SRC/linux/README-portable.txt" "$PORTABLE/README.txt"
printf '%s\n' \
  "Eyes Saver keeps its settings in this folder because the folder exists." \
  "Delete it to keep settings in ~/.config/eyessaver instead." > "$PORTABLE/data/README.txt"
tar -C "$TARROOT" --owner=0 --group=0 -czf "$OUT/EyesSaver-$VERSION-linux-x64-portable.tar.gz" "eyessaver-$VERSION"

# ------------------------------------------------------------------ .deb
ROOT=/tmp/deb/eyessaver
rm -rf /tmp/deb
install -d "$ROOT/DEBIAN" "$ROOT/opt/eyessaver" "$ROOT/usr/bin" \
  "$ROOT/usr/share/applications" "$ROOT/usr/share/metainfo" "$ROOT/usr/share/doc/eyessaver"

# The single-file executable carries its managed code appended to the host; never strip it.
install -m755 "$SRC/app/EyesSaver" "$ROOT/opt/eyessaver/EyesSaver"
install -m644 "$SRC"/app/*.so "$ROOT/opt/eyessaver/"
ln -s /opt/eyessaver/EyesSaver "$ROOT/usr/bin/eyessaver"

install -m644 "$SRC/linux/$APP_ID.desktop" "$ROOT/usr/share/applications/$APP_ID.desktop"
install -m644 "$SRC/linux/$APP_ID.metainfo.xml" "$ROOT/usr/share/metainfo/$APP_ID.metainfo.xml"
for size in 16 24 32 48 64 128 256 512; do
  install -Dm644 "$SRC/icons/eyessaver-$size.png" "$ROOT/usr/share/icons/hicolor/${size}x${size}/apps/$APP_ID.png"
done

cat > "$ROOT/usr/share/doc/eyessaver/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Eyes Saver
Files: *
Copyright: Eyes Saver authors
License: proprietary
 Bundles the .NET runtime (MIT), Avalonia (MIT), SkiaSharp and HarfBuzzSharp (MIT)
 and the Inter font (SIL Open Font License 1.1).
EOF

INSTALLED_SIZE=$(du -sk --exclude=DEBIAN "$ROOT" | cut -f1)
cat > "$ROOT/DEBIAN/control" <<EOF
Package: eyessaver
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: $INSTALLED_SIZE
Maintainer: Eyes Saver <eyessaver@users.noreply.invalid>
Depends: libc6 (>= 2.27), libstdc++6, libgcc-s1, libfontconfig1, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libice6, libsm6
Recommends: libgl1
Suggests: picom
Description: Screen tint for every display
 Eyes Saver lays a warm, click-through color tint over each display to cut
 glare and blue light. Each display can have its own color and strength, and
 displays you plug in or out are picked up automatically.
 .
 Works on X11 and on Wayland through XWayland. With a compositor the tint is an
 overlay; without one it is applied through the display gamma ramp.
EOF

# hicolor and desktop-file-utils refresh their caches through dpkg triggers, so no maintainer scripts are needed.
dpkg-deb --build --root-owner-group "$ROOT" "$OUT/eyessaver_${VERSION}_amd64.deb"
dpkg-deb --info "$OUT/eyessaver_${VERSION}_amd64.deb" | sed -n '1,12p'
