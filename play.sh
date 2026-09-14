#!/usr/bin/env bash
# Launch a real, playable Atomcraft with ONLY this mod loaded, for hands-on testing.
#
#   ./play.sh            # build the mod and launch the game on your display
#   ./play.sh --verify   # boot headless under Xvfb, confirm the mod loaded, and exit
#
# What this does and does not touch:
#   - It runs against a hardlinked copy of the harness's already-patched game install, in a
#     throwaway Wine prefix. It never patches or launches your real Steam copy, and its saves
#     live in that private prefix, so your real worlds are untouched.
#   - The copy's Mods folder holds this mod alone. The test harness in particular is kept out:
#     it suppresses the game's own automatic saves, which is right for a test run and ruinous
#     for a play session.
#   - EXTRA_MODS="../Other/build/Other.zip" ./play.sh installs companions too, for when the
#     combination is what you want to judge.
#
# Prerequisite: a patched game copy must already exist (the harness makes one). If it does
# not, this stops and tells you.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="ActualResolution"
MOD_NAME="Actual Resolution"
# shellcheck disable=SC1091
. lib/harness.sh
require_harness

# shellcheck disable=SC1091
. "$HARNESS/lib/common.sh"
load_config
require_runner

VERIFY=0
for arg in "$@"; do
  case "$arg" in
    --verify) VERIFY=1 ;;
    *) echo "unknown argument: $arg (expected --verify)" >&2; exit 1 ;;
  esac
done

# --- the patched source install --------------------------------------------------------------
SRC="$INSTALL"                    # from the harness config: the patched game copy
BACKUP="$SRC/$DATA_DIR_NAME/Atomcraft.dll.backup"
if [ ! -f "$SRC/AtomCraft.exe" ] || [ ! -f "$BACKUP" ]; then
  cat >&2 <<MSG
No patched game copy at:
  $SRC
This script runs against the harness's patched copy so it never modifies your Steam install.
Create one first:
  ( cd $HARNESS && ./bootstrap.sh )
Once it exists, re-run this script.
MSG
  exit 1
fi

# --- build the mod ---------------------------------------------------------------------------
echo "==> building the $MOD_NAME mod"
./build.sh >/dev/null
ZIP="build/$MOD_ID.zip"
[ -f "$ZIP" ] || { echo "build did not produce $ZIP" >&2; exit 1; }

# --- an isolated play install: the patched copy, this mod the only mod ------------------------
PLAY_ROOT="${PLAY_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/atomcraft-$(printf '%s' "$MOD_ID" | tr '[:upper:]' '[:lower:]')-play}"
PLAY_INSTALL="$PLAY_ROOT/install"
PLAY_PREFIX="$PLAY_ROOT/prefix"

# Refresh the clone whenever the patched source is newer than what we cloned, so a game
# update (re-bootstrapped upstream) is picked up. Hardlinked, so it costs almost nothing.
if [ ! -f "$PLAY_INSTALL/AtomCraft.exe" ] || [ "$SRC/AtomCraft.exe" -nt "$PLAY_INSTALL/AtomCraft.exe" ]; then
  echo "==> cloning the patched game copy into $PLAY_INSTALL"
  rm -rf "$PLAY_INSTALL"
  mkdir -p "$PLAY_ROOT"
  cp -al "$SRC" "$PLAY_INSTALL" 2>/dev/null || cp -a "$SRC" "$PLAY_INSTALL"
fi

echo "==> installing $MOD_ID as the only mod"
rm -f "$PLAY_INSTALL/Mods/"*.zip 2>/dev/null || true
mkdir -p "$PLAY_INSTALL/Mods"
cp "$ZIP" "$PLAY_INSTALL/Mods/$MOD_ID.zip"

for extra in ${EXTRA_MODS:-}; do
  [ -f "$extra" ] || { echo "no such mod zip: $extra" >&2; exit 1; }
  echo "==> also installing $(basename "$extra")"
  cp "$extra" "$PLAY_INSTALL/Mods/"
done

mkdir -p "$PLAY_PREFIX"

# launch_game reads these globals; point them at the isolated play copy.
INSTALL="$PLAY_INSTALL"
PREFIX="$PLAY_PREFIX"

# --- launch ---------------------------------------------------------------------------------
if [ "$VERIFY" = 1 ]; then
  # Boot headless-with-a-framebuffer just long enough to confirm the loader picked the mod
  # up, then quit. Uses a private Xvfb so nothing lands on your desktop.
  command -v Xvfb >/dev/null 2>&1 || { echo "Xvfb required for --verify" >&2; exit 1; }
  DISP=":$(( (RANDOM % 400) + 100 ))"
  Xvfb "$DISP" -screen 0 1920x1080x24 -nolisten tcp >/dev/null 2>&1 &
  XPID=$!
  trap 'kill "$XPID" 2>/dev/null || true' EXIT
  sleep 1

  LOG_DIR="$PLAY_ROOT/out"; mkdir -p "$LOG_DIR"
  echo "==> verify boot (Xvfb $DISP), quitting after a few seconds"
  set +e
  HEADFUL=1 DISPLAY="$DISP" timeout --foreground -k 5 120 \
    bash -c '. "'"$HARNESS"'/lib/common.sh"; load_config
             INSTALL="'"$PLAY_INSTALL"'"; PREFIX="'"$PLAY_PREFIX"'"; HEADFUL=1
             launch_game -s GodotMonoModLoader.gd --audio-driver Dummy --quit-after 900' \
    >"$LOG_DIR/verify.log" 2>&1
  set -e

  GLOG="$(HEADFUL=1 PREFIX="$PLAY_PREFIX" godot_log 2>/dev/null || true)"
  if [ -n "$GLOG" ] && grep -qa "\[$MOD_ID\] initialized" "$GLOG"; then
    echo "==> OK"
    grep -a "\[$MOD_ID\]" "$GLOG" | tail -3 | sed 's/^/    /'
    echo "    loaded mods: $(grep -aoE 'Loading Mod: [A-Za-z.]+' "$GLOG" | sed 's/Loading Mod: //' | sort -u | tr '\n' ' ')"
    exit 0
  fi
  echo "==> FAILED: no '[$MOD_ID] initialized' in the game log" >&2
  [ -n "$GLOG" ] && echo "    log: $GLOG" >&2
  exit 1
fi

echo "==> launching Atomcraft with $MOD_ID on display ${DISPLAY:-:0}"
echo "    install: $PLAY_INSTALL"
echo "    prefix:  $PLAY_PREFIX  (saves here are separate from your real game)"
echo "    settings: <prefix>/.../app_userdata/Atomcraft/$MOD_ID.json"
export DISPLAY="${DISPLAY:-:0}"
HEADFUL=1 launch_game -s GodotMonoModLoader.gd
