#!/usr/bin/env bash
# Regenerates the README screenshots (docs/screenshots/*.png) against the current UI.
#
#   ./tools/generate-screenshots.sh                    # default: 1280x800, System theme, English
#   ./tools/generate-screenshots.sh --theme dark       # dark theme
#   ./tools/generate-screenshots.sh --lang sk --size 1920x1080
#
# If DISPLAY is already set (a real X session), it is used as-is. Otherwise a private Xvfb
# display is started for the duration of the run and torn down afterwards.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DISPLAY_NUM="${DISPLAY_NUM:-:99}"
XVFB_SCREEN="${XVFB_SCREEN:-1280x800x24}"

if [[ -z "${DISPLAY:-}" ]]; then
  XVFB_BIN="${XVFB_BIN:-$(command -v Xvfb || true)}"
  if [[ -z "$XVFB_BIN" ]]; then
    echo "No DISPLAY set and Xvfb not found on PATH." >&2
    echo "Install it (e.g. 'sudo apt install xvfb') or run inside an existing X session." >&2
    exit 1
  fi

  "$XVFB_BIN" "$DISPLAY_NUM" -screen 0 "$XVFB_SCREEN" -nolisten tcp >/dev/null 2>&1 &
  XVFB_PID=$!
  trap 'kill "$XVFB_PID" 2>/dev/null || true' EXIT
  sleep 1
  export DISPLAY="$DISPLAY_NUM"
  echo "Started private Xvfb on $DISPLAY"
fi

dotnet run --project tools/ScreenshotDriver --configuration Debug -- "$@"
