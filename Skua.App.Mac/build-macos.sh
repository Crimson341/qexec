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
skua_app="$skua_root/build/qexec.app"
if [[ -e "$skua_app" ]]; then
  echo "Move the previous build out of $skua_app before packaging again." >&2
  exit 1
fi
ditto desktop/node_modules/electron/dist/Electron.app "$skua_app"
mkdir -p "$skua_app/Contents/Resources/app/assets"
cp desktop/*.cjs desktop/*.html desktop/*.css desktop/package.json "$skua_app/Contents/Resources/app/"
skua_version="${QEXEC_VERSION:-0.2.0}"
skua_bundle_version="${QEXEC_BUNDLE_VERSION:-2}"
if [[ ! "$skua_version" =~ ^[0-9]+(\.[0-9]+)*$ ]]; then
  echo 'QEXEC_VERSION must be a dotted numeric version.' >&2
  exit 1
fi
if [[ ! "$skua_bundle_version" =~ ^[0-9]+$ ]]; then
  echo 'QEXEC_BUNDLE_VERSION must be an integer.' >&2
  exit 1
fi
skua_commit="$(git -C "$skua_root/.." rev-parse HEAD 2>/dev/null || true)"
printf '{"version":"%s","commit":"%s"}\n' "$skua_version" "$skua_commit" > "$skua_app/Contents/Resources/app/version.json"
node desktop/bake-version.cjs "$skua_app/Contents/Resources/app" "$skua_version" "$skua_commit"
cp desktop/assets/skua.swf "$skua_app/Contents/Resources/app/assets/"
ditto desktop/brand "$skua_app/Contents/Resources/app/brand"
cp desktop/brand/qexec.icns "$skua_app/Contents/Resources/qexec.icns"
ditto build/backend "$skua_app/Contents/Resources/backend"
/usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier org.skua.mac.dev' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleName qexec' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleDisplayName qexec' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :CFBundleIconFile qexec.icns' "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $skua_version" "$skua_app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $skua_bundle_version" "$skua_app/Contents/Info.plist"
codesign --force --deep --sign - "$skua_app"
echo "Built $skua_app"
