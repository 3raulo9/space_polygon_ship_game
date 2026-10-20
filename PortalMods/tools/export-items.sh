#!/usr/bin/env bash
# Regenerates the Fabric mod's copy of UNRENDERED's item models
# (fabric/src/main/resources/assets/unrendered_portal/unrendered/items.json) and of the crab
# core's throw and blast sounds (…/sounds/*.ogg, through ffmpeg) from the game
# itself: builds UNRENDERED and the glowstone-rift mod, starts the game for a moment with
# RIFT_EXPORT set, and keeps what the mod wrote. Run it after changing an item's mesh in
# UNRENDERED. Needs a display (or xvfb-run).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ASSETS="$ROOT/PortalMods/fabric/src/main/resources/assets/unrendered_portal"
OUT="$ASSETS/unrendered/items.json"
WAVS="$(mktemp -d)"
cd "$ROOT"
dotnet build -c Release -v q
dotnet build PortalMods/unrendered -c Release -v q
mkdir -p bin/Release/net10.0/Mods "$(dirname "$OUT")"
cp PortalMods/unrendered/out/glowstone-rift.dll bin/Release/net10.0/Mods/
cd bin/Release/net10.0
RUN=(dotnet Unrendered.dll)
[ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null && RUN=(xvfb-run -a "${RUN[@]}")
RIFT_EXPORT="$OUT" RIFT_EXPORT_SOUNDS="$WAVS" RIFT_QUIT_AFTER=3 timeout 60 "${RUN[@]}" >/dev/null 2>&1 || true
test -s "$OUT" && echo "wrote $OUT"
mkdir -p "$ASSETS/sounds"
for w in "$WAVS"/*.wav; do
  ffmpeg -loglevel error -y -i "$w" -c:a libvorbis -q:a 5 "$ASSETS/sounds/$(basename "${w%.wav}").ogg"
  echo "wrote $ASSETS/sounds/$(basename "${w%.wav}").ogg"
done
rm -rf "$WAVS"
