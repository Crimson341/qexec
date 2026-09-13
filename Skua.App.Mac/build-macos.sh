#!/bin/bash
set -euo pipefail
skua_root="$(cd "$(dirname "$0")" && pwd)"
cd "$skua_root"
if [[ "$(uname -s)" != Darwin ]]; then
  echo 'Build the macOS app on a Mac.' >&2
  exit 1
fi
case "$(uname -m)" in
  arm64) skua_rid=osx-arm64 ;;
  x86_64) skua_rid=osx-x64 ;;
  *) echo 'Unsupported Mac architecture.' >&2; exit 1 ;;
esac
npm ci --prefix desktop --arch=x64
./build-flash.sh
dotnet publish host -c Release -r "$skua_rid" --self-contained true -p:SkuaPortable=true -p:PublishSingleFile=false -o build/backend --nologo -v:quiet
skua_app="$skua_root/build/Skua Mac.app"
if [[ -e "$skua_app" ]]; then
  echo "Move the previous build out of $skua_app before packaging again." >&2
  exit 1
fi
ditto desktop/node_modules/electron/dist/Electron.app "$skua_app"
mkdir -p "$skua_app/Contents/Resources/app/assets"
cp desktop/*.cjs desktop/*.html desktop/*.css desktop/package.json "$skua_app/Contents/Resources/app/"
cp desktop/assets/skua.swf "$skua_app/Contents/Resources/app/assets/"
ditto build/backend "$skua_app/Contents/Resources/backend"
/usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier org.skua.mac.dev' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleName Skua Mac' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleDisplayName Skua Mac' "$skua_app/Contents/Info.plist"
codesign --force --deep --sign - "$skua_app"
echo "Built $skua_app"
