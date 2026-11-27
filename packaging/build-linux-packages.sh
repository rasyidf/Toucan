#!/usr/bin/env bash
# Builds the Linux packages for one architecture: packaging/build-linux-packages.sh <linux-x64|linux-arm64> [output folder]
# Writes Toucan-<version>-<rid>.tar.gz, toucan_<version>_<arch>.deb and toucan-<version>-1.<arch>.rpm to the output folder
# (default dist/). Needs the .NET SDK, python3, dpkg-deb and rpmbuild (apt install dpkg rpm on Ubuntu).
# The app is self-contained in /opt/toucan; /usr/bin/toucan points at it. Saved tokens use the Secret Service through
# secret-tool (libsecret-tools / libsecret), recommended rather than required: without it Toucan keeps its key in a file.
set -euo pipefail

rid="${1:?Usage: build-linux-packages.sh <linux-x64|linux-arm64> [output folder]}"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$(mkdir -p "${2:-$root/dist}" && cd "${2:-$root/dist}" && pwd)"
version="$(python3 -c "import xml.etree.ElementTree as E; print(E.parse('$root/Directory.Build.props').findtext('.//Version'))")"
case "$rid" in
  linux-x64) deb_arch=amd64; rpm_arch=x86_64 ;;
  linux-arm64) deb_arch=arm64; rpm_arch=aarch64 ;;
  *) echo "Unsupported runtime: $rid" >&2; exit 2 ;;
esac

work="$root/build/linux-packages/$rid"
rm -rf "$work"
publish="$work/publish"
dotnet publish "$root/Toucan.Avalonia/Toucan.Avalonia.csproj" -c Release -r "$rid" --self-contained \
  -p:DebugType=none -p:DebugSymbols=false -o "$publish"
python3 "$root/tools/Verify-SpellingArtifacts.py" --folder "$publish"
find "$publish" \( -name '*.pdb' -o -name '*.xml' \) -delete

# 1. Tarball: the published folder as is.
tar -czf "$out/Toucan-$version-$rid.tar.gz" -C "$publish" .

# 2. File system layout shared by the .deb and the .rpm.
stage="$work/stage"
mkdir -p "$stage/opt/toucan" "$stage/usr/bin" "$stage/usr/share/applications" \
  "$stage/usr/share/icons/hicolor/256x256/apps" "$stage/usr/share/mime/packages" "$stage/usr/share/doc/toucan"
cp -R "$publish/." "$stage/opt/toucan/"
chmod 755 "$stage/opt/toucan/Toucan"
ln -s /opt/toucan/Toucan "$stage/usr/bin/toucan"
install -m 644 "$root/packaging/linux/toucan.desktop" "$stage/usr/share/applications/toucan.desktop"
install -m 644 "$root/Toucan.Avalonia/Assets/logo.png" "$stage/usr/share/icons/hicolor/256x256/apps/toucan.png"
install -m 644 "$root/packaging/linux/toucan-mime.xml" "$stage/usr/share/mime/packages/toucan.xml"
install -m 644 "$root/LICENSE.txt" "$stage/usr/share/doc/toucan/copyright"

summary="Translation editor for localization files"
description="Toucan edits, reviews and synchronizes localization files (JSON, ARB, YAML, PO, XLIFF, RESX and more)."

# 3. Debian package.
deb="$work/deb"
cp -R "$stage" "$deb"
mkdir -p "$deb/DEBIAN"
cat > "$deb/DEBIAN/control" <<CONTROL
Package: toucan
Version: $(echo "$version" | tr '-' '~')
Section: devel
Priority: optional
Architecture: $deb_arch
Maintainer: Toucan <https://github.com/rasyidf/Toucan>
Homepage: https://github.com/rasyidf/Toucan
Depends: libc6, libicu74 | libicu72 | libicu70 | libicu67 | libicu66 | libicu-dev, libfontconfig1
Recommends: libsecret-tools, gnome-keyring | kwalletmanager
Description: $summary
 $description
 Saved sign-in tokens use the Secret Service (GNOME Keyring or KWallet) through
 secret-tool; without it the key is kept in a file in the user's Toucan folder.
CONTROL
cat > "$deb/DEBIAN/postinst" <<'POST'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v update-mime-database >/dev/null && update-mime-database /usr/share/mime || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
POST
cp "$deb/DEBIAN/postinst" "$deb/DEBIAN/postrm"
chmod 755 "$deb/DEBIAN/postinst" "$deb/DEBIAN/postrm"
dpkg-deb --root-owner-group --build "$deb" "$out/toucan_${version}_${deb_arch}.deb"

# 4. RPM package, from the same layout.
rpmtop="$work/rpm"
mkdir -p "$rpmtop/SPECS" "$rpmtop/BUILD" "$rpmtop/RPMS"
cat > "$rpmtop/SPECS/toucan.spec" <<SPEC
Name: toucan
Version: $(echo "$version" | tr '-' '~')
Release: 1
Summary: $summary
License: See /usr/share/doc/toucan/copyright
URL: https://github.com/rasyidf/Toucan
AutoReqProv: no
Requires: libicu, fontconfig
Recommends: libsecret
# The app is self-contained .NET: no stripping, compression or debug-info scripts over it.
%global __os_install_post %{nil}
%global _build_id_links none

%description
$description
Saved sign-in tokens use the Secret Service (GNOME Keyring or KWallet) through
secret-tool; without it the key is kept in a file in the user's Toucan folder.

%install
cp -a "$stage/." %{buildroot}/

%post
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v update-mime-database >/dev/null && update-mime-database /usr/share/mime || true

%postun
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v update-mime-database >/dev/null && update-mime-database /usr/share/mime || true

%files
/opt/toucan
/usr/bin/toucan
/usr/share/applications/toucan.desktop
/usr/share/icons/hicolor/256x256/apps/toucan.png
/usr/share/mime/packages/toucan.xml
%doc /usr/share/doc/toucan/copyright
SPEC
# --target sets the architecture; nothing is compiled, so either can be packaged on any host.
rpmbuild --quiet --define "_topdir $rpmtop" --target "$rpm_arch" -bb "$rpmtop/SPECS/toucan.spec"
cp "$rpmtop/RPMS/$rpm_arch/"*.rpm "$out/"

echo "Built in $out:"
ls -1 "$out" | grep -E "(-$rid\.tar\.gz|_${deb_arch}\.deb|\.${rpm_arch}\.rpm)$"
