#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
arch="${1:-$(uname -m)}"
./verify-macos.sh "$arch"
APP="dist/$arch/Bambu冷打板助手.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "dist/tests/BambuCoolPlateHelper-$arch" "$APP/Contents/MacOS/BambuCoolPlateHelper"
cp assets/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp Info.plist "$APP/Contents/Info.plist"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "dist/BambuCoolPlateHelper-v1.1.0-$arch.zip"
