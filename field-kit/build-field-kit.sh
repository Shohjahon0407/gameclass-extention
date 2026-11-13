#!/usr/bin/env bash
# Builds what goes to clubs:
#   out/ManajoyGameClass-Setup-<version>.exe     — the Windows installer (wizard: language, terms and privacy,
#                                                  folder, shortcuts). This is what the website offers.
#   out/ManajoyGameClass-<version>-portable.exe  — one exe without an installer (Telegram fallback, < 50 MB).
#   out/ManajoyGameClassKit-<version>.zip        — the technician kit: bridge + inspector x64/x86, scripts, guide.
#   out/site/gameclass/                          — the download page with the installer, ready to upload.
#
# The installer is compiled with Inno Setup 6 (ISCC). On Linux it runs under Wine; set ISCC to override:
#   ISCC="wine $HOME/.wine/drive_c/InnoSetup6/ISCC.exe"  (default)
set -euo pipefail
cd "$(dirname "$0")"
ROOT=..
OUT=out/ManajoyGameClassKit
INSTALLER_FILES=out/installer-files
SITE=out/site/gameclass
# Only our own artifacts are removed: out/ may hold other files someone put there.
rm -rf "$OUT" "$INSTALLER_FILES" "$SITE" out/ManajoyGameClassKit-*.zip out/ManajoyGameClass-Setup-*.exe* out/ManajoyGameClass-*-portable.exe*
mkdir -p "$OUT"

for rid in win-x64 win-x86; do
  arch=${rid#win-}
  dotnet publish "$ROOT/src/Manajoy.GameClass.Bridge" -c Release -r "$rid" --self-contained true \
    -o "$OUT/bridge/$arch" -nologo -v quiet
  dotnet publish "$ROOT/src/Manajoy.GameClass.Inspector" -c Release -r "$rid" --self-contained true \
    -o "$OUT/inspector/$arch" -nologo -v quiet
  rm -f "$OUT"/bridge/$arch/*.pdb "$OUT"/inspector/$arch/*.pdb "$OUT"/bridge/$arch/*.staticwebassets.*.json
done

# Windows PowerShell 5.1 reads BOM-less scripts in the ANSI code page: add a UTF-8 BOM, CRLF line ends.
add_bom() { printf '\xEF\xBB\xBF' > "$2"; sed 's/\r$//; s/$/\r/' "$1" >> "$2"; }
for f in scripts/*.ps1; do add_bom "$f" "$OUT/$(basename "$f")"; done
for arch in x64 x86; do add_bom "$ROOT/src/Manajoy.GameClass.Bridge/install-service.ps1" "$OUT/bridge/$arch/install-service.ps1"; done
cp FIELD-GUIDE.md "$OUT/"

VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/src/Manajoy.GameClass.Bridge/Manajoy.GameClass.Bridge.csproj")

# The portable file (Telegram). The Bot API can upload at most 50 MB, so the size is checked here.
PORTABLE="out/ManajoyGameClass-$VERSION-portable.exe"
cp "$OUT/bridge/x86/Manajoy.GameClass.Bridge.exe" "$PORTABLE"
SIZE=$(stat -c%s "$PORTABLE")
if [ "$SIZE" -ge 50000000 ]; then echo "ERROR: $PORTABLE is $SIZE bytes (Telegram limit 50 MB)" >&2; exit 1; fi
(cd out && sha256sum "$(basename "$PORTABLE")" > "$(basename "$PORTABLE").sha256")
(cd "$OUT" && find . -type f ! -name SHA256SUMS.txt -printf '%P\n' | sort | xargs sha256sum > SHA256SUMS.txt)
(cd out && zip -qr "ManajoyGameClassKit-$VERSION.zip" ManajoyGameClassKit)

# The installer: a plain (not single-file, not trimmed) 32-bit app folder, compressed by Inno Setup.
dotnet publish "$ROOT/src/Manajoy.GameClass.Bridge" -c Release -r win-x86 --self-contained true -p:InstallerLayout=true \
  -o "$INSTALLER_FILES" -nologo -v quiet
rm -f "$INSTALLER_FILES"/*.pdb "$INSTALLER_FILES"/*.staticwebassets.*.json
ISCC=${ISCC:-"wine $HOME/.wine/drive_c/InnoSetup6/ISCC.exe"}
SOURCE_DIR=$(cd "$INSTALLER_FILES" && pwd)
case "$ISCC" in wine*) SOURCE_DIR="Z:$SOURCE_DIR" ;; esac
(cd installer && WINEDEBUG=-all $ISCC /Q "/DAppVersion=$VERSION" "/DSourceDir=$SOURCE_DIR" ManajoyGameClass.iss)
SETUP="out/ManajoyGameClass-Setup-$VERSION.exe"
[ -f "$SETUP" ] || { echo "ERROR: $SETUP was not built" >&2; exit 1; }
(cd out && sha256sum "$(basename "$SETUP")" > "$(basename "$SETUP").sha256")
rm -rf "$INSTALLER_FILES"

# The download page: static files with the version, size and checksum filled in.
mkdir -p "$SITE/download"
cp "$SETUP" "$SITE/download/"
SETUP_SIZE=$(stat -c%s "$SETUP")
SETUP_SHA=$(cut -d' ' -f1 < "$SETUP.sha256")
SETUP_MB=$(awk "BEGIN { printf \"%.1f\", $SETUP_SIZE / 1048576 }")
RELEASED=$(date +%Y-%m-%d)
for page in site/gameclass/*.html; do
  sed -e "s|{{VERSION}}|$VERSION|g" -e "s|{{FILE}}|$(basename "$SETUP")|g" -e "s|{{SIZE_MB}}|$SETUP_MB|g" \
      -e "s|{{SHA256}}|$SETUP_SHA|g" -e "s|{{DATE}}|$RELEASED|g" "$page" > "$SITE/$(basename "$page")"
done
cp site/gameclass/*.png "$SITE/"
python3 site/make-privacy.py "$SITE/privacy.html"
python3 sign-release.py "$SITE" "$VERSION" "$SETUP"

echo "Installer: $(pwd)/$SETUP ($SETUP_SIZE bytes, sha256 $SETUP_SHA)"
echo "Portable:  $(pwd)/$PORTABLE ($SIZE bytes)"
echo "Kit:       $(pwd)/out/ManajoyGameClassKit-$VERSION.zip"
echo "Site:      $(pwd)/$SITE"
