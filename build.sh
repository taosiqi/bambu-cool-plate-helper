#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
APP='dist/Bambu冷打板助手.app'
mkdir -p "$APP/Contents/MacOS"
swiftc main.swift -target arm64-apple-macos13.0 -framework AppKit -o "$APP/Contents/MacOS/BambuCoolPlateHelper"
cp Info.plist "$APP/Contents/Info.plist"
"$APP/Contents/MacOS/BambuCoolPlateHelper" --self-test
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" dist/BambuCoolPlateHelper-v1.1.0-arm64.zip
