#!/bin/bash
set -euo pipefail
skua_root="$(cd "$(dirname "$0")" && pwd)"
skua_app="${2:-$skua_root/build/Skua Mac.app}"
skua_swf="$skua_app/Contents/Resources/app/assets/skua.swf"
skua_config="$HOME/Library/Application Support/Skua Mac/Pepper Data/Shockwave Flash/WritableRoot/#Security/FlashPlayerTrust/SkuaMac.cfg"
if [[ ! -f "$skua_swf" ]]; then
  echo "Build the app first. Missing: $skua_swf" >&2
  exit 1
fi
skua_swf="$(cd "$(dirname "$skua_swf")" && pwd)/$(basename "$skua_swf")"
if [[ "${1:-}" != --enable ]]; then
  echo 'This grants the single bundled Flash bridge local-file and network access.'
  echo "SWF: $skua_swf"
  echo "Trust file: $skua_config"
  echo 'To install this entry, run this script with --enable.'
  exit 0
fi
if [[ -e "$skua_config" ]] && [[ "$(cat "$skua_config")" != "$skua_swf" ]]; then
  echo "An existing different trust entry is present at $skua_config; it has not been changed." >&2
  exit 1
fi
mkdir -p "$(dirname "$skua_config")"
printf '%s\n' "$skua_swf" > "$skua_config"
echo "Installed a trust entry for: $skua_swf"
