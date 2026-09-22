#!/usr/bin/env bash
# Runs the ClientPathC spike inside a nested, isolated KWin virtual session. Never touches the
# caller's real DISPLAY or WAYLAND_DISPLAY: the nested compositor gets its own Xwayland on a
# fresh display number, and only that number is handed to the spike process.
#
# Usage: run-nested.sh [variant] [joinTimeoutSeconds]
#   variant: "1" (calls Client.Start(), default) or "baseline" (does not)
set -euo pipefail

VARIANT="${1:-1}"
JOIN_TIMEOUT="${2:-30}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DLL="$HERE/bin/Release/net10.0/ClientPathC.dll"
[ -f "$DLL" ] || { echo "build first: dotnet build $HERE/ClientPathC.csproj -c Release" >&2; exit 1; }
: "${VINTAGE_STORY:?set VINTAGE_STORY to a Vintage Story 1.22.x install}"

SOCKNAME="pathc-nested-$$"
CLIENT_RT="$(mktemp -d)"
KPID=""
cleanup() {
  rm -rf "$CLIENT_RT"
  [ -n "$KPID" ] || return 0
  pkill -TERM -P "$KPID" 2>/dev/null || true
  kill -TERM "$KPID" 2>/dev/null || true
  wait "$KPID" 2>/dev/null || true
}
trap cleanup EXIT

before=$(ls /tmp/.X11-unix/ 2>/dev/null | sort)
env -u DISPLAY -u WAYLAND_DISPLAY dbus-run-session -- \
  kwin_wayland --virtual --xwayland --no-lockscreen --no-global-shortcuts \
  --width 1280 --height 800 --socket "$SOCKNAME" &
KPID=$!

new=""
for _ in $(seq 1 20); do
  after=$(ls /tmp/.X11-unix/ 2>/dev/null | sort)
  new=$(comm -13 <(echo "$before") <(echo "$after") | head -1)
  [ -n "$new" ] && break
  sleep 1
done
[ -n "$new" ] || { echo "nested Xwayland never appeared under socket $SOCKNAME" >&2; exit 3; }
disp=":${new#X}"
echo "nested DISPLAY=$disp (kwin pid $KPID, socket $SOCKNAME)"

LIBGL_ALWAYS_SOFTWARE=1 glxinfo -display "$disp" -B 2>&1 | grep -qi llvmpipe \
  || { echo "nested display $disp is not software-rendered (llvmpipe); aborting" >&2; exit 4; }

env -u WAYLAND_DISPLAY DISPLAY="$disp" XDG_RUNTIME_DIR="$CLIENT_RT" XDG_SESSION_TYPE=x11 \
  OPENTK_4_USE_WAYLAND=0 LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
  CLIENT_PATHC_NESTED=1 \
  dotnet "$DLL" "$VARIANT" "$JOIN_TIMEOUT"
