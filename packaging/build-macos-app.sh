#!/usr/bin/env bash
# Builds Toucan.Avalonia into a self-contained, ad-hoc-signed macOS Toucan.app.
# Usage: packaging/build-macos-app.sh [output-dir] [rid]   (defaults: ./build/macos, host arch)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/build/macos}"
RID="${2:-osx-$([ "$(uname -m)" = arm64 ] && echo arm64 || echo x64)}"
PROJ="$ROOT/Toucan.Avalonia/Toucan.Avalonia.csproj"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -1)"
APP="$OUT/Toucan.app"

rm -rf "$OUT/publish" "$APP"
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained -o "$OUT/publish"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/publish/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Toucan"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Toucan</string>
<key>CFBundleIdentifier</key><string>dev.toucan.avalonia</string>
<key>CFBundleExecutable</key><string>Toucan</string>
<key>CFBundleVersion</key><string>${VERSION:-0.0.0}</string>
<key>CFBundleShortVersionString</key><string>${VERSION:-0.0.0}</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

codesign --force --deep -s - "$APP"
echo "Built $APP ($RID)"
