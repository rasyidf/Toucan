#!/usr/bin/env bash
# Builds Toucan.Avalonia into a self-contained macOS Toucan.app.
# Usage: packaging/build-macos-app.sh [output-dir] [rid]   (defaults: ./build/macos, host arch)
#
# Signing (set in the environment):
#   SIGN_IDENTITY   "Developer ID Application: Name (TEAMID)". Signs with the hardened runtime.
#                   Unset: ad-hoc signature only, which Gatekeeper blocks on other Macs.
#   NOTARY_PROFILE  notarytool keychain profile (xcrun notarytool store-credentials <name> ...).
#                   Needs SIGN_IDENTITY. Submits the app to Apple and staples the ticket,
#                   so it opens on any Mac without a warning.
# Also writes Toucan-<version>-<rid>.dmg (drag to Applications) next to the app.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/build/macos}"
RID="${2:-osx-$([ "$(uname -m)" = arm64 ] && echo arm64 || echo x64)}"
PROJ="$ROOT/Toucan.Avalonia/Toucan.Avalonia.csproj"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"
APP="$OUT/Toucan.app"

rm -rf "$OUT/publish" "$APP"
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained -o "$OUT/publish"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/publish/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Toucan"

# App icon: build AppIcon.icns from the 256px logo (largest slot is 128pt@2x).
ICONSET="$OUT/AppIcon.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
LOGO="$ROOT/Toucan.Avalonia/Assets/logo.png"
for sz in 16 32 64 128 256; do sips -z $sz $sz "$LOGO" --out "$ICONSET/icon_${sz}x${sz}.png" >/dev/null; done
cp "$ICONSET/icon_32x32.png"   "$ICONSET/icon_16x16@2x.png"
cp "$ICONSET/icon_64x64.png"   "$ICONSET/icon_32x32@2x.png"
cp "$ICONSET/icon_256x256.png" "$ICONSET/icon_128x128@2x.png"
rm "$ICONSET/icon_64x64.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Toucan</string>
<key>CFBundleIdentifier</key><string>dev.toucan.avalonia</string>
<key>CFBundleExecutable</key><string>Toucan</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleVersion</key><string>${VERSION:-0.0.0}</string>
<key>CFBundleShortVersionString</key><string>${VERSION:-0.0.0}</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>NSHighResolutionCapable</key><true/>
<key>UTExportedTypeDeclarations</key><array><dict>
  <key>UTTypeIdentifier</key><string>dev.rasyid.toucan.project</string>
  <key>UTTypeDescription</key><string>Toucan Translation Project</string>
  <key>UTTypeConformsTo</key><array><string>public.data</string></array>
  <key>UTTypeTagSpecification</key><dict>
    <key>public.filename-extension</key><array><string>tproj</string></array>
  </dict>
</dict></array>
<key>CFBundleDocumentTypes</key><array><dict>
  <key>CFBundleTypeName</key><string>Toucan Translation Project</string>
  <key>CFBundleTypeRole</key><string>Editor</string>
  <key>LSHandlerRank</key><string>Owner</string>
  <key>LSItemContentTypes</key><array><string>dev.rasyid.toucan.project</string></array>
</dict></array>
</dict></plist>
PLIST

# Drag-to-Applications disk image. Uses create-dmg (brew install create-dmg) for the styled
# window when present, and plain hdiutil otherwise.
make_dmg() {
  local dmg="$OUT/Toucan-${VERSION:-0.0.0}-$RID.dmg" stage="$OUT/dmg"
  rm -rf "$stage" "$dmg"
  mkdir -p "$stage"
  cp -R "$APP" "$stage/"
  if command -v create-dmg >/dev/null 2>&1; then
    create-dmg --volname "Toucan" --window-size 540 360 --icon-size 96 \
      --icon "Toucan.app" 140 170 --app-drop-link 400 170 --no-internet-enable \
      "$dmg" "$stage" >/dev/null
  else
    ln -s /Applications "$stage/Applications"
    hdiutil create -volname "Toucan" -srcfolder "$stage" -ov -format UDZO "$dmg" >/dev/null
  fi
  rm -rf "$stage"
  if [ -n "${SIGN_IDENTITY:-}" ]; then
    codesign --force --timestamp -s "$SIGN_IDENTITY" "$dmg"
  fi
  echo "Built $dmg"
}

if [ -z "${SIGN_IDENTITY:-}" ]; then
  codesign --force --deep -s - "$APP"
  echo "Built $APP ($RID), ad-hoc signed (not notarized)."
  echo "On another Mac: System Settings > Privacy & Security > Open Anyway, or run: xattr -dr com.apple.quarantine Toucan.app"
  make_dmg
  exit 0
fi

# .NET needs JIT and unsigned executable memory under the hardened runtime, and plugins are
# third-party assemblies, so library validation has to be off.
ENTITLEMENTS="$OUT/entitlements.plist"
cat > "$ENTITLEMENTS" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>com.apple.security.cs.allow-jit</key><true/>
<key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
<key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
PLIST

# Sign inside-out: every nested Mach-O first, then the bundle.
while IFS= read -r f; do
  if file "$f" | grep -q "Mach-O"; then
    codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" -s "$SIGN_IDENTITY" "$f"
  fi
done < <(find "$APP/Contents/MacOS" -type f ! -name Toucan)
codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" -s "$SIGN_IDENTITY" "$APP/Contents/MacOS/Toucan"
codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" -s "$SIGN_IDENTITY" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

if [ -n "${NOTARY_PROFILE:-}" ]; then
  ZIP="$OUT/Toucan-notarize.zip"
  ditto -c -k --keepParent "$APP" "$ZIP"
  xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$APP"
  spctl --assess --type execute --verbose "$APP"
  rm -f "$ZIP"
  echo "Built $APP ($RID), signed and notarized."
else
  echo "Built $APP ($RID), Developer ID signed (not notarized; set NOTARY_PROFILE)."
fi
make_dmg
