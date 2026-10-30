#!/usr/bin/env bash
#
# Assembles QemuXplorer.app from the output of `dotnet publish`.
#
#   dotnet publish src/QemuXplorer.App/QemuXplorer.App.csproj -c Release \
#       -r osx-arm64 --self-contained true -o publish/osx-arm64
#   bash tools/build-macos-app.sh publish/osx-arm64 dist --version 1.2.3
#
# Result: dist/QemuXplorer.app
#
# The bundle is:
#   QemuXplorer.app/Contents/Info.plist
#   QemuXplorer.app/Contents/MacOS/        the whole publish folder; the executable is QemuXplorer
#   QemuXplorer.app/Contents/Resources/icon.icns
#
# Assembling needs nothing but cp, so it runs on any OS with bash. Signing needs `codesign`,
# which only exists on macOS: elsewhere the bundle is built but left unsigned, and says so.
#
# Signing:
#   default                    ad-hoc signature ("-"). Apple Silicon refuses to run an unsigned
#                              arm64 binary at all, so this is the minimum for the app to start.
#   CODESIGN_IDENTITY="..."    sign with a real identity, e.g. "Developer ID Application: Name (TEAMID)".
#   Notarization is not done here; Gatekeeper still blocks an app downloaded from the internet
#   until it is notarized, or until the quarantine flag is removed with `xattr -dr com.apple.quarantine`.

set -euo pipefail

APP_NAME="QemuXplorer"
DISPLAY_NAME="QEMU Explorer"
BUNDLE_ID="${BUNDLE_ID:-io.github.yartat.qemuxplorer}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
icon="$script_dir/../src/QemuXplorer.App/Assets/icon.icns"
version="0.1.0"

usage() {
    echo "usage: build-macos-app.sh <publish-dir> <output-dir> [--version X.Y.Z] [--icon path/to/icon.icns]" >&2
    exit 2
}

[ $# -ge 2 ] || usage
publish_dir="$1"
output_dir="$2"
shift 2

while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || usage; version="$2"; shift 2 ;;
        --icon)    [ $# -ge 2 ] || usage; icon="$2"; shift 2 ;;
        *) usage ;;
    esac
done

# Goes straight into an XML file, so keep it to characters that need no escaping.
if ! [[ "$version" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
    echo "error: --version must look like 1, 1.2 or 1.2.3 (got '$version')" >&2
    exit 2
fi

[ -d "$publish_dir" ] || { echo "error: publish directory not found: $publish_dir" >&2; exit 1; }
[ -f "$icon" ]        || { echo "error: icon not found: $icon" >&2; exit 1; }

# The apphost is what CFBundleExecutable points at. A framework-dependent publish has one too,
# but the app would then need a .NET runtime installed, which a double-clicked app cannot ask for.
if [ ! -f "$publish_dir/$APP_NAME" ]; then
    echo "error: $publish_dir/$APP_NAME not found. Publish with -r osx-arm64 (or osx-x64) --self-contained true." >&2
    exit 1
fi

bundle="$output_dir/$APP_NAME.app"
rm -rf "$bundle"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"

# "/." copies the folder's contents, dotfiles included, rather than the folder itself.
cp -R "$publish_dir/." "$bundle/Contents/MacOS/"
cp "$icon" "$bundle/Contents/Resources/icon.icns"
chmod +x "$bundle/Contents/MacOS/$APP_NAME"

cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$DISPLAY_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIconFile</key>
    <string>icon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

if command -v codesign >/dev/null 2>&1; then
    identity="${CODESIGN_IDENTITY:--}"
    # --deep also signs the many dylibs inside MacOS/, which each need a valid signature.
    codesign --force --deep --sign "$identity" "$bundle"
    if [ "$identity" = "-" ]; then
        echo "signed ad-hoc (set CODESIGN_IDENTITY to sign with a Developer ID)"
    else
        echo "signed as: $identity"
    fi
else
    echo "warning: codesign not found (not macOS), so the bundle is NOT signed." >&2
    echo "         On Apple Silicon it will not start until it is signed on a Mac." >&2
fi

echo "built: $bundle"
