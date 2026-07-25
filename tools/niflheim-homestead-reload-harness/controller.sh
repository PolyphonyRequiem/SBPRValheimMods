#!/usr/bin/env bash
# ============================================================================
#  Niflheim 0003 — live cold-reload capture CONTROLLER (mechanical runbook).
# ----------------------------------------------------------------------------
#  SCOPE HONESTY (read first): this controller drives the ONE mechanically
#  executable live sequence the 0003 reload gate needs:
#
#    validate disposable fixture + exact UID
#      -> capture PRE from the production path
#      -> request + verify a REAL Valheim world save
#      -> terminate the ENTIRE graphical client process
#      -> prove the old PID/session is gone
#      -> cold-launch the SAME disposable world (fresh process)
#      -> capture POST after authoritative load/reconciliation
#      -> compare PRE vs POST (fail-closed) -> stop/cleanup
#
#  It is FAIL-CLOSED on every precondition: it refuses to proceed without an
#  OPERATE-supplied lease, rollback bytes, and a disposable Astley .db/.fwl at
#  the exact expected UID, and it NEVER targets a production Niflheim/Heistan
#  world or port. An in-process reload, copied state, engine-free selector
#  rerun, warm scene transition, or same-process serialization round-trip is
#  FORBIDDEN evidence — the PRE and POST captures MUST come from two different
#  OS processes/sessions, enforced by the shipped HomesteadReloadComparer.
#
#  Running THIS script does NOT prove live reload, persistence, deployment, or
#  playability. It only orchestrates the capture; a PASS requires a real live
#  window (OPERATE-provisioned) and is the acceptance gate on kanban t_1a1164f4.
#
#  USAGE:
#    controller.sh --dry-run [--manifest <file>]   # validate + refuse, launch nothing
#    controller.sh --run     --manifest <file>     # full live sequence (OPERATE only)
#
#  The manifest is a shell-sourced key=value file. See manifest.example.env.
#  Absent/invalid manifest or absent fixture => exit non-zero with a refusal.
# ============================================================================
set -euo pipefail

MODE="dry-run"
MANIFEST=""
EXPECTED_UID="2413287143"          # source-fixed disposable Astley fixture UID
FORBIDDEN_NAME_RE='niflheim|heistan'
FORBIDDEN_PORTS=(2456 2457 2466 2467)

log()    { printf '[reload-controller] %s\n' "$*"; }
refuse() { printf '[reload-controller] REFUSED: %s\n' "$*" >&2; exit 3; }
phase()  { printf '\n== PHASE: %s ==\n' "$*"; }

while [ $# -gt 0 ]; do
    case "$1" in
        --dry-run) MODE="dry-run"; shift ;;
        --run)     MODE="run"; shift ;;
        --manifest) MANIFEST="${2:-}"; shift 2 ;;
        --expected-uid) EXPECTED_UID="${2:-}"; shift 2 ;;
        *) refuse "unknown argument '$1'" ;;
    esac
done

# ---------------------------------------------------------------------------
phase "precondition validation (fail-closed)"

if [ -z "$MANIFEST" ]; then
    refuse "no --manifest supplied. The controller cannot proceed without an OPERATE-staged QA manifest."
fi
if [ ! -f "$MANIFEST" ]; then
    refuse "manifest '$MANIFEST' does not exist. OPERATE must stage a disposable-fixture manifest first."
fi

# shellcheck disable=SC1090
source "$MANIFEST"

# Required manifest keys. Any missing/empty => refuse.
for key in LEASE_ID ROLLBACK_BYTES DISPOSABLE_DB DISPOSABLE_FWL TARGET_WORLD_NAME TARGET_PORT \
           WORLD_UID CAPTURE_DIR VALHEIM_CLIENT_CMD; do
    if [ -z "${!key:-}" ]; then
        refuse "manifest is missing required key '$key'."
    fi
done

# Exact-UID fixture guard.
if [ "$WORLD_UID" != "$EXPECTED_UID" ]; then
    refuse "manifest WORLD_UID=$WORLD_UID != expected disposable fixture UID $EXPECTED_UID."
fi

# Production-target guard (name + port).
if printf '%s' "$TARGET_WORLD_NAME" | grep -qiE "$FORBIDDEN_NAME_RE"; then
    refuse "TARGET_WORLD_NAME='$TARGET_WORLD_NAME' names a forbidden production world (niflheim/heistan)."
fi
for p in "${FORBIDDEN_PORTS[@]}"; do
    if [ "$TARGET_PORT" = "$p" ]; then
        refuse "TARGET_PORT=$TARGET_PORT is a forbidden production port."
    fi
done

# Lease + rollback presence.
[ -n "$LEASE_ID" ] || refuse "no OPERATE lease id."
if [ ! -f "$ROLLBACK_BYTES" ]; then
    refuse "rollback bytes '$ROLLBACK_BYTES' absent — refuse to proceed without a rollback path."
fi

# Disposable fixture presence (the real, load-bearing prerequisite).
if [ ! -f "$DISPOSABLE_DB" ]; then
    refuse "disposable Astley .db '$DISPOSABLE_DB' absent — cannot cold-load. This is an OPERATE environment prerequisite."
fi
if [ ! -f "$DISPOSABLE_FWL" ]; then
    refuse "disposable Astley .fwl '$DISPOSABLE_FWL' absent — cannot cold-load. This is an OPERATE environment prerequisite."
fi

log "all preconditions satisfied: lease=$LEASE_ID uid=$WORLD_UID target='$TARGET_WORLD_NAME':$TARGET_PORT"

if [ "$MODE" = "dry-run" ]; then
    log "DRY-RUN complete: preconditions validated, NO Valheim client launched, no world touched."
    log "To run the live sequence, re-invoke with --run under an OPERATE-provisioned window."
    exit 0
fi

# ---------------------------------------------------------------------------
# LIVE SEQUENCE (OPERATE only). Each phase emits an explicit receipt; a failed
# phase aborts fail-closed and triggers cleanup. Bounded waits only.
# ---------------------------------------------------------------------------
READINESS_TIMEOUT="${READINESS_TIMEOUT:-300}"
SAVE_TIMEOUT="${SAVE_TIMEOUT:-120}"
EXIT_TIMEOUT="${EXIT_TIMEOUT:-60}"
mkdir -p "$CAPTURE_DIR"

launched_pid=""
cleanup() {
    if [ -n "$launched_pid" ] && kill -0 "$launched_pid" 2>/dev/null; then
        log "cleanup: terminating client pid $launched_pid"
        kill "$launched_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT

wait_for_file() {
    local target="$1" timeout="$2" waited=0
    while [ ! -f "$target" ]; do
        sleep 2; waited=$((waited + 2))
        if [ "$waited" -ge "$timeout" ]; then
            return 1
        fi
    done
    return 0
}

launch_boot() {
    # $1 = PRE|POST ; sets launched_pid; the client's HomesteadReloadCaptureObserver
    # writes homestead-reload-capture-<phase>.txt into $CAPTURE_DIR.
    local phase_tag="$1"
    NIFLHEIM_RELOAD_HARNESS_PHASE="$phase_tag" \
    NIFLHEIM_RELOAD_HARNESS_CAPTURE_DIR="$CAPTURE_DIR" \
        $VALHEIM_CLIENT_CMD &
    launched_pid=$!
    log "$phase_tag boot: launched client pid $launched_pid"
}

prove_pid_gone() {
    local pid="$1" timeout="$2" waited=0
    while kill -0 "$pid" 2>/dev/null; do
        sleep 1; waited=$((waited + 1))
        if [ "$waited" -ge "$timeout" ]; then
            refuse "client pid $pid did not exit within ${timeout}s — cannot prove a full client exit."
        fi
    done
    log "verified: pid $pid is gone (full client exit)."
}

phase "PRE capture (production path, boot 1)"
launch_boot "PRE"
pre_pid="$launched_pid"
if ! wait_for_file "$CAPTURE_DIR/homestead-reload-capture-pre.txt" "$READINESS_TIMEOUT"; then
    refuse "PRE capture not produced within ${READINESS_TIMEOUT}s."
fi
log "PRE capture receipt: $CAPTURE_DIR/homestead-reload-capture-pre.txt"

phase "world save + verify"
# The manifest supplies the mechanism to request a real save (e.g. an admin RPC / console).
if [ -n "${SAVE_TRIGGER_CMD:-}" ]; then
    eval "$SAVE_TRIGGER_CMD"
fi
if ! wait_for_file "${SAVE_RECEIPT_FILE:-/nonexistent}" "$SAVE_TIMEOUT"; then
    refuse "no world-save receipt observed within ${SAVE_TIMEOUT}s — nothing durable to cold-load."
fi
log "save receipt: ${SAVE_RECEIPT_FILE}"

phase "full client exit (terminate entire graphical process)"
kill "$pre_pid" 2>/dev/null || true
prove_pid_gone "$pre_pid" "$EXIT_TIMEOUT"
launched_pid=""

phase "cold reload (fresh process, same disposable world, boot 2)"
launch_boot "POST"
if ! wait_for_file "$CAPTURE_DIR/homestead-reload-capture-post.txt" "$READINESS_TIMEOUT"; then
    refuse "POST capture not produced within ${READINESS_TIMEOUT}s."
fi
post_pid="$launched_pid"
log "POST capture receipt: $CAPTURE_DIR/homestead-reload-capture-post.txt"

phase "teardown"
kill "$post_pid" 2>/dev/null || true
prove_pid_gone "$post_pid" "$EXIT_TIMEOUT"
launched_pid=""

phase "compare PRE vs POST"
# The two capture files are handed to the shipped fail-closed comparator (a small
# net8 harness runner or the test comparator). This controller only orchestrates;
# the verdict is produced by HomesteadReloadComparer over the two captured files.
log "PRE=$CAPTURE_DIR/homestead-reload-capture-pre.txt POST=$CAPTURE_DIR/homestead-reload-capture-post.txt"
log "Hand both captures to the shipped HomesteadReloadComparer for the fail-closed identity/count verdict."
log "LIVE SEQUENCE COMPLETE. A PASS still requires the comparator verdict; this controller asserts no verdict itself."
