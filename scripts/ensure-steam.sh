#!/usr/bin/env bash
# ensure-steam.sh — bring Steam up RELIABLY for a given user, or confirm it already is.
#
# Why this exists: the T022 QA client dies six seconds into boot with
#   InvalidOperationException: Steamworks is not initialized.
#     SteamUtils.IsSteamRunningOnSteamDeck () / ZInput.Load () / SceneLoader.Awake ()
# Valheim's Steamworks needs a LIVE Steam client owned by the same user. GABS forks
# the game directly, so if that user's Steam isn't up, the game crashes before the
# scene activates. steam_appid.txt is necessary but NOT sufficient — it lets a
# directly-launched binary identify itself to a RUNNING Steam, it does not start one.
#
# This was misdiagnosed for weeks as "the intermittent ValBridge startup-scene
# deadlock". It is neither intermittent nor a deadlock: it is a deterministic crash,
# which is why a 6-attempt re-roll budget never helped.
#
# Usage:
#   scripts/ensure-steam.sh                 # current user
#   scripts/ensure-steam.sh --user valbot   # another user (needs sudo; see RULE 3)
#   scripts/ensure-steam.sh --check         # report readiness only, start nothing
#
# Exit codes:
#   0  Steam is up and ready for the target user
#   1  usage / precondition failure
#   2  no usable X display
#   3  Steam binary or install not found for that user
#   4  Steam started but never became ready inside the budget
#   5  needs sudo and cannot self-elevate  <-- prints the command for Daniel to run

set -Eeuo pipefail

TARGET_USER="${USER:-$(id -un)}"
CHECK_ONLY=0
READY_TIMEOUT_S="${READY_TIMEOUT_S:-90}"
POLL_INTERVAL_S=3

log()  { printf '\033[1;34m[steam]\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m[steam] OK\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[steam] !!\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[steam] FATAL\033[0m %s\n' "$2" >&2; exit "$1"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --user)  TARGET_USER="$2"; shift 2 ;;
    --check) CHECK_ONLY=1; shift ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die 1 "unknown argument: $1" ;;
  esac
done

id -u "$TARGET_USER" >/dev/null 2>&1 || die 1 "no such user: $TARGET_USER"
TARGET_HOME="$(getent passwd "$TARGET_USER" | cut -d: -f6)"
[[ -n "$TARGET_HOME" ]] || die 1 "cannot resolve home for $TARGET_USER"

CURRENT_USER="$(id -un)"
AS_SELF=0
[[ "$TARGET_USER" == "$CURRENT_USER" ]] && AS_SELF=1

# --------------------------------------------------------------------------- #
# run_as: execute a command as the target user (direct if it's us, else sudo)
# --------------------------------------------------------------------------- #
run_as() {
  if [[ $AS_SELF -eq 1 ]]; then
    bash -lc "$1"
  else
    sudo -n -u "$TARGET_USER" bash -lc "$1" 2>/dev/null
  fi
}

# --------------------------------------------------------------------------- #
# 1. Readiness probe — explicit signals, never a blind sleep.
#    Steam is READY when: a steam process owned by the user is alive AND the
#    IPC pipe exists AND the pid file points at a live process.
# --------------------------------------------------------------------------- #
steam_ready() {
  local pidfile="$TARGET_HOME/.steam/steam.pid"
  local pipe="$TARGET_HOME/.steam/steam.pipe"

  pgrep -u "$TARGET_USER" -x steam >/dev/null 2>&1 || return 1
  [[ -p "$pipe" ]] || return 1

  if [[ -r "$pidfile" ]]; then
    local spid; spid="$(cat "$pidfile" 2>/dev/null || echo "")"
    [[ -n "$spid" ]] && kill -0 "$spid" 2>/dev/null || return 1
  fi
  return 0
}

report_state() {
  local procs pipe
  procs="$(pgrep -u "$TARGET_USER" -x steam 2>/dev/null | tr '\n' ' ' || true)"
  pipe="absent"; [[ -p "$TARGET_HOME/.steam/steam.pipe" ]] && pipe="present"
  echo "  user       : $TARGET_USER ($TARGET_HOME)"
  echo "  steam pids : ${procs:-<none>}"
  echo "  steam.pipe : $pipe"
}

if steam_ready; then
  ok "Steam already running and ready for $TARGET_USER"
  report_state
  exit 0
fi

if [[ $CHECK_ONLY -eq 1 ]]; then
  warn "Steam NOT ready for $TARGET_USER"
  report_state
  exit 4
fi

# --------------------------------------------------------------------------- #
# 2. Resolve a usable X display. Steam needs one even with -silent.
# --------------------------------------------------------------------------- #
resolve_display() {
  if [[ -n "${DISPLAY:-}" ]]; then echo "$DISPLAY"; return 0; fi
  local sock
  for sock in /tmp/.X11-unix/X*; do
    [[ -S "$sock" ]] || continue
    echo ":${sock##*/X}"; return 0
  done
  return 1
}

DISP="$(resolve_display)" || die 2 "no X display found (no \$DISPLAY, nothing in /tmp/.X11-unix).
  Steam cannot start headless. A desktop session must be active."
log "using DISPLAY=$DISP"

# Xauthority matters when crossing users.
XAUTH="$TARGET_HOME/.Xauthority"
[[ -r "$XAUTH" ]] || XAUTH=""

# --------------------------------------------------------------------------- #
# 3. Locate the Steam launcher for that user.
# --------------------------------------------------------------------------- #
STEAM_BIN=""
for cand in "$TARGET_HOME/.steam/debian-installation/steam.sh" \
            "$TARGET_HOME/.local/share/Steam/steam.sh" \
            /usr/games/steam /usr/bin/steam; do
  if [[ -x "$cand" ]]; then STEAM_BIN="$cand"; break; fi
done
[[ -n "$STEAM_BIN" ]] || die 3 "no Steam launcher found for $TARGET_USER"
log "launcher: $STEAM_BIN"

# --------------------------------------------------------------------------- #
# 4. Cross-user needs sudo. Per RULE 3, Daniel enters the password himself.
# --------------------------------------------------------------------------- #
if [[ $AS_SELF -eq 0 ]] && ! sudo -n true 2>/dev/null; then
  cat >&2 <<EOF

Cannot start Steam for '$TARGET_USER' without sudo. Run this yourself:

  sudo -u $TARGET_USER env DISPLAY=$DISP ${XAUTH:+XAUTHORITY=$XAUTH} \\
    setsid $STEAM_BIN -silent -nochatui -nofriendsui >/dev/null 2>&1 &

Then re-run:  $0 --user $TARGET_USER --check

EOF
  exit 5
fi

# --------------------------------------------------------------------------- #
# 5. Start Steam detached and silent (-silent = no window steals your focus).
# --------------------------------------------------------------------------- #
log "starting Steam for $TARGET_USER (silent, detached)"
LAUNCH="DISPLAY=$DISP ${XAUTH:+XAUTHORITY=$XAUTH} setsid nohup \
  '$STEAM_BIN' -silent -nochatui -nofriendsui >/dev/null 2>&1 < /dev/null &"
run_as "$LAUNCH" || true

# --------------------------------------------------------------------------- #
# 6. Poll for readiness. Explicit probe, bounded, no sleep-and-hope.
# --------------------------------------------------------------------------- #
log "polling for readiness (up to ${READY_TIMEOUT_S}s)"
elapsed=0
while (( elapsed < READY_TIMEOUT_S )); do
  sleep "$POLL_INTERVAL_S"
  elapsed=$(( elapsed + POLL_INTERVAL_S ))
  if steam_ready; then
    ok "Steam ready for $TARGET_USER after ${elapsed}s"
    report_state
    exit 0
  fi
  printf '.'
done
echo

warn "Steam did NOT become ready within ${READY_TIMEOUT_S}s"
report_state
cat >&2 <<EOF

Most likely causes:
  - First launch for this user is updating (can exceed the budget) — re-run --check
  - The account needs an interactive login / Steam Guard code once, by hand
  - No graphical session for \$TARGET_USER on DISPLAY=$DISP

EOF
exit 4
