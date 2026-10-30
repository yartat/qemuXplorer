#!/usr/bin/env bash
#
# Builds a .deb from the output of `dotnet publish`.
#
#   dotnet publish src/QemuXplorer.App/QemuXplorer.App.csproj -c Release \
#       -r linux-x64 --self-contained true -o publish/linux-x64
#   bash tools/build-deb.sh publish/linux-x64 dist --version 1.2.3 --arch amd64
#
# Result: dist/qemuxplorer_1.2.3_amd64.deb
#
# Layout:
#   /opt/qemuxplorer/                         the whole publish folder
#   /usr/bin/qemuxplorer                      symlink to /opt/qemuxplorer/QemuXplorer
#   /usr/share/applications/qemuxplorer.desktop
#   /usr/share/icons/hicolor/{256x256,512x512}/apps/qemuxplorer.png
#
# Needs dpkg-deb, so Debian/Ubuntu or a runner that has it. Permissions are set while staging
# (the publish folder may come from a filesystem that has no executable bit).

set -euo pipefail

usage() {
    echo "usage: build-deb.sh <publish-dir> <output-dir> --version X.Y.Z --arch amd64|arm64" >&2
    exit 2
}

[[ $# -ge 2 ]] || usage
publish_dir="$1"; out_dir="$2"; shift 2
version=""; arch=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) version="${2:-}"; shift 2 ;;
        --arch)    arch="${2:-}";    shift 2 ;;
        *) usage ;;
    esac
done
[[ -n "$version" && -n "$arch" ]] || usage
[[ "$version" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]] || { echo "version must be 1, 1.2, 1.2.3 or 1.2.3.4" >&2; exit 2; }
[[ "$arch" == amd64 || "$arch" == arm64 ]] || { echo "arch must be amd64 or arm64" >&2; exit 2; }
[[ -f "$publish_dir/QemuXplorer" ]] || { echo "no QemuXplorer executable in $publish_dir" >&2; exit 1; }

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
assets="$script_dir/../src/QemuXplorer.App/Assets"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/opt/qemuxplorer" "$stage/usr/bin" "$stage/usr/share/applications" \
    "$stage/usr/share/icons/hicolor/256x256/apps" "$stage/usr/share/icons/hicolor/512x512/apps" "$stage/DEBIAN"

cp -R "$publish_dir"/. "$stage/opt/qemuxplorer/"
find "$stage/opt/qemuxplorer" -type d -exec chmod 755 {} +
find "$stage/opt/qemuxplorer" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/qemuxplorer/QemuXplorer"

ln -s /opt/qemuxplorer/QemuXplorer "$stage/usr/bin/qemuxplorer"
install -m 644 "$assets/icon-256.png" "$stage/usr/share/icons/hicolor/256x256/apps/qemuxplorer.png"
install -m 644 "$assets/icon-512.png" "$stage/usr/share/icons/hicolor/512x512/apps/qemuxplorer.png"

cat > "$stage/usr/share/applications/qemuxplorer.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=QEMU Explorer
Comment=Define and run QEMU virtual machines
Exec=/usr/bin/qemuxplorer
Icon=qemuxplorer
Terminal=false
Categories=System;Emulator;
StartupWMClass=QemuXplorer
DESKTOP
chmod 644 "$stage/usr/share/applications/qemuxplorer.desktop"

# .NET needs ICU unless globalization is invariant; the package name carries the ICU version.
size_kb="$(du -sk "$stage/opt" | cut -f1)"
cat > "$stage/DEBIAN/control" <<CONTROL
Package: qemuxplorer
Version: $version
Section: utils
Priority: optional
Architecture: $arch
Installed-Size: $size_kb
Maintainer: QemuXplorer <noreply@users.noreply.github.com>
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1, libicu76 | libicu74 | libicu72 | libicu70 | libicu67 | libicu66
Recommends: qemu-system-x86 | qemu-system
Description: Desktop frontend for QEMU
 Define virtual machines in a UI, store them in a local database,
 build the qemu-system command line and launch the process.
CONTROL
chmod 644 "$stage/DEBIAN/control"

mkdir -p "$out_dir"
deb="$out_dir/qemuxplorer_${version}_${arch}.deb"
dpkg-deb --root-owner-group --build "$stage" "$deb" >/dev/null
echo "Built $deb"
