#!/bin/bash
set -euo pipefail
skua_root="$(cd "$(dirname "$0")" && pwd)"
skua_app="${2:-$skua_root/build/qexec.app}"
skua_swf="$skua_app/Contents/Resources/app/assets/skua.swf"
desktop_swf="$skua_root/desktop/assets/skua.swf"
skua_config="$HOME/Library/Application Support/Skua Mac/Pepper Data/Shockwave Flash/WritableRoot/#Security/FlashPlayerTrust/SkuaMac.cfg"

append_trust() {
  local swf="$1"
  if [[ ! -f "$swf" ]]; then
    return 1
  fi
  swf="$(cd "$(dirname "$swf")" && pwd)/$(basename "$swf")"
  mkdir -p "$(dirname "$skua_config")"
  if [[ -e "$skua_config" ]] && grep -Fxq "$swf" "$skua_config"; then
    echo "Trust entry already present for: $swf"
    return 0
  fi
  printf '%s\n' "$swf" >> "$skua_config"
  echo "Installed a trust entry for: $swf"
}

if [[ ! -f "$skua_swf" && ! -f "$desktop_swf" ]]; then
  echo "Build the app first. Missing: $skua_swf" >&2
  exit 1
fi

if [[ "${1:-}" != --enable ]]; then
  echo 'This grants each listed Flash bridge local-file and network access.'
  echo "Trust file: $skua_config"
  [[ -f "$skua_swf" ]] && echo "Packaged SWF: $(cd "$(dirname "$skua_swf")" && pwd)/$(basename "$skua_swf")"
  [[ -f "$desktop_swf" ]] && echo "Desktop SWF: $(cd "$(dirname "$desktop_swf")" && pwd)/$(basename "$desktop_swf")"
  echo 'To install these entries, run this script with --enable.'
  echo 'Existing entries are kept. The app also appends the SWF it is about to load.'
  exit 0
fi

installed=0
if [[ -f "$skua_swf" ]]; then
  append_trust "$skua_swf"
  installed=1
fi
if [[ -f "$desktop_swf" ]]; then
  append_trust "$desktop_swf"
  installed=1
fi
if [[ "$installed" -eq 0 ]]; then
  echo "Build the app first. Missing: $skua_swf" >&2
  exit 1
fi
