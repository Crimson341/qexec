#!/bin/bash
set -euo pipefail
skua_root="$(cd "$(dirname "$0")" && pwd)"
cd "$skua_root"
npm ci --prefix tools --ignore-scripts
skua_java="${SKUA_JAVA:-}"
if [[ -z "$skua_java" && -n "${JAVA_HOME:-}" ]]; then skua_java="$JAVA_HOME/bin/java"; fi
if [[ -z "$skua_java" ]] && command -v brew >/dev/null; then
  skua_java="$(brew --prefix openjdk)/libexec/openjdk.jdk/Contents/Home/bin/java"
fi
skua_java="${skua_java:-java}"
mkdir -p desktop/assets
"$skua_java" -jar tools/node_modules/@apache-royale/royale-js/royale-asjs/lib/mxmlc.jar \
  -load-config=tools/flash-config.xml \
  -external-library-path=tools/node_modules/playerglobal/lib/15.0/playerglobal.swc \
  -source-path=../Skua.AS3/skua/src \
  -output=desktop/assets/skua.swf \
  -default-size=958,550 -default-frame-rate=30 \
  -target-player=28.0 -swf-version=39 -use-network=true \
  ../Skua.AS3/skua/src/skua/Main.as
