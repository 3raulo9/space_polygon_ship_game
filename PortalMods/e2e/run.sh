#!/usr/bin/env bash
# THE RIFT, end to end: a real Minecraft 1.21.1 client with the Fabric mod and the real
# UNRENDERED with its code mod, both running at once against each other under one virtual
# display. Minecraft plays itself (the mod's autotest mode, -Drift.autotest=true); UNRENDERED
# plays itself (its RIFT_TEST hatch). Both save screenshots and logs into $OUT.
#
#   PortalMods/e2e/run.sh <out-dir>
# Expects: the Fabric project built, the game built in bin/Release, and glowstone-rift.dll
# already copied into bin/Release/net10.0/Mods.
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(mkdir -p "${1:-$ROOT/e2e-out}" && cd "${1:-$ROOT/e2e-out}" && pwd)"
mkdir -p "$OUT/unr"

export DISPLAY=:99
Xvfb :99 -screen 0 1280x800x24 +extension GLX >"$OUT/xvfb.log" 2>&1 &
XVFB=$!
sleep 2
glxinfo -B >"$OUT/glxinfo.txt" 2>&1 || true

# --- Minecraft ---------------------------------------------------------------------------
cd "$ROOT/PortalMods/fabric"
mkdir -p run
# No first-launch onboarding, no tutorial toasts, a light client for a software GPU.
cat > run/options.txt <<'OPT'
onboardAccessibility:false
skipMultiplayerWarning:true
tutorialStep:none
joinedFirstServer:true
renderDistance:8
simulationDistance:8
maxFps:30
graphicsMode:0
renderClouds:"false"
soundCategory_master:0.0
pauseOnLostFocus:false
OPT
RIFT_AUTOTEST=true ./gradlew runClient --no-daemon --console=plain >"$OUT/mc.log" 2>&1 &
MC=$!

echo "waiting for the rift bridge on 25570..."
for i in $(seq 1 900); do
  if python3 - <<'PY'
import socket, sys
s = socket.socket(); s.settimeout(0.5)
try:
    s.connect(("127.0.0.1", 25570)); sys.exit(0)
except OSError:
    sys.exit(1)
PY
  then echo "bridge up after ${i}s"; break; fi
  if ! kill -0 $MC 2>/dev/null; then echo "Minecraft exited early"; break; fi
  sleep 1
done

# --- UNRENDERED ---------------------------------------------------------------------------
cd "$ROOT/bin/Release/net10.0"
UNRENDERED_CAPTURE="$OUT/unr-final.png" UNRENDERED_CAPTURE_FRAME=100000000 \
RIFT_CONNECT=127.0.0.1:25570 RIFT_TEST=portal,link,enter,tour,lookvisitor RIFT_SHOTS="$OUT/unr" RIFT_QUIT_AFTER=140 \
  timeout 200 dotnet Unrendered.dll >"$OUT/unr.log" 2>&1 &
UNR=$!

# Minecraft quits by itself when its script is done; give it a generous ceiling.
for i in $(seq 1 600); do
  kill -0 $MC 2>/dev/null || break
  sleep 1
done
kill $UNR 2>/dev/null; wait $UNR 2>/dev/null
pkill -f 'net.fabricmc.devlaunchinjector' 2>/dev/null
kill $MC 2>/dev/null; wait $MC 2>/dev/null
kill $XVFB 2>/dev/null

cp -f "$ROOT/PortalMods/fabric/run/logs/latest.log" "$OUT/mc-latest.log" 2>/dev/null
cp -f "$ROOT/PortalMods/fabric/run/screenshots/"*.png "$OUT/" 2>/dev/null
cp -f "$ROOT/bin/Release/net10.0/Mods/rift/rift.log" "$OUT/rift.log" 2>/dev/null
echo "--- RIFT-E2E lines from Minecraft ---"
grep -a "RIFT-E2E\|Rift:" "$OUT/mc-latest.log" | cut -c1-400 | tail -150
echo "--- Minecraft warnings and errors ---"
grep -a "WARN\|ERROR\|Exception\|at net.unrendered" "$OUT/mc-latest.log" | grep -v "Missing sound\|Unable to play\|narrator" | cut -c1-300 | tail -60
echo "--- UNRENDERED stdout tail ---"
tail -40 "$OUT/unr.log" 2>/dev/null
echo "--- UNRENDERED rift.log ---"
tail -120 "$OUT/rift.log" 2>/dev/null
ls -la "$OUT" "$OUT/unr"
exit 0
