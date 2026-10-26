#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
arch="${1:-$(uname -m)}"
case "$arch" in arm64|x86_64) ;; *) echo 'Expected arm64 or x86_64' >&2; exit 1 ;; esac
if [[ "$(uname -m)" != "$arch" ]]; then
  echo "Use a native $arch runner to compile and execute tests." >&2
  exit 1
fi
mkdir -p dist/tests
binary="dist/tests/BambuCoolPlateHelper-$arch"
swiftc main.swift -target "$arch-apple-macos13.0" -framework AppKit -o "$binary"
lipo -verify_arch "$arch" "$binary"
file "$binary"
"$binary" --self-test
