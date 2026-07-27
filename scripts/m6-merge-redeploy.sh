#!/usr/bin/env bash
# m6-merge-redeploy.sh — deterministic merge + helper redeploy + arming proof.
#
# Replaces the LLM agent that has performed these exact steps three times.
# Every step here is mechanical: no judgment calls, no reasoning required.
#
# Usage:
#   scripts/m6-merge-redeploy.sh --pr 434 --reviewed-head <sha> [--base <sha>]
#   scripts/m6-merge-redeploy.sh --pr 434 --reviewed-head <sha> --dry-run
#
# Exit codes:
#   0  merged, redeployed, lane ARMED (or helper unchanged and lane already armed)
#   1  usage / precondition failure (nothing mutated)
#   2  merge refused: head moved, base moved, or PR not mergeable
#   3  build failed
#   4  deploy or descriptor update failed
#   5  lane did not ARM after restart  <-- the interesting failure; escalate to an agent
#
# FAIL-CLOSED: any unexpected error aborts. This script never fabricates a hash,
# never edits the guard, and never touches production servers (2456 / 2466).

set -Eeuo pipefail

readonly REPO="${REPO:-$HOME/repos/SBPRValheimMods}"
readonly ARTIFACTS="${ARTIFACTS:-$HOME/valheim/qa-artifacts}"
readonly DESCRIPTOR="$ARTIFACTS/t022-run-descriptor.json"
readonly LANE_CONTAINER="${LANE_CONTAINER:-homestead-t009l-server}"
readonly LANE_PLUGINS="${LANE_PLUGINS:-$HOME/valheim/homestead-t009l/data/bepinex/BepInEx/plugins/SBPR.QaHarness.T022}"
readonly CLIENT_PLUGINS="${CLIENT_PLUGINS:-$HOME/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins/SBPR.QaHarness.T022}"
readonly HELPER_PROJ="qa/SBPR.QaHarness.T022/SBPR.QaHarness.T022.csproj"
readonly HELPER_DLL_NAME="SBPR.QaHarness.T022.dll"

# Production ports that must NEVER appear in the descriptor.
readonly PROD_PORTS=(2456 2466)

PR=""
REVIEWED_HEAD=""
EXPECTED_BASE=""
DRY_RUN=0

log()  { printf '\033[1;34m[m6]\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m[m6] OK\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[m6] !!\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[m6] FATAL\033[0m %s\n' "$2" >&2; exit "$1"; }

trap 'rc=$?; if [[ $rc -ne 0 ]]; then warn "aborted (exit $rc)"; fi; exit $rc' ERR

usage() {
  sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --pr)            PR="$2"; shift 2 ;;
    --reviewed-head) REVIEWED_HEAD="$2"; shift 2 ;;
    --base)          EXPECTED_BASE="$2"; shift 2 ;;
    --dry-run)       DRY_RUN=1; shift ;;
    -h|--help)       usage ;;
    *) die 1 "unknown argument: $1" ;;
  esac
done

[[ -n "$PR" ]]            || die 1 "--pr is required"
[[ -n "$REVIEWED_HEAD" ]] || die 1 "--reviewed-head is required (the SHA both reviewers accepted)"

command -v gh      >/dev/null || die 1 "gh CLI not found"
command -v jq      >/dev/null || die 1 "jq not found"
command -v dotnet  >/dev/null || die 1 "dotnet not found"
command -v docker  >/dev/null || die 1 "docker not found"

cd "$REPO" || die 1 "repo not found: $REPO"

# ---------------------------------------------------------------------------
# 1. Verify the head we're about to merge is EXACTLY the reviewed head.
# ---------------------------------------------------------------------------
log "fetching origin"
git fetch -q origin

PUBLISHED_HEAD="$(git ls-remote origin "refs/pull/$PR/head" | awk '{print $1}')"
[[ -n "$PUBLISHED_HEAD" ]] || die 2 "could not resolve refs/pull/$PR/head"

if [[ "$PUBLISHED_HEAD" != "$REVIEWED_HEAD" ]]; then
  die 2 "HEAD MOVED since review.
  reviewed:  $REVIEWED_HEAD
  published: $PUBLISHED_HEAD
  Refusing to merge unreviewed bytes. Re-review at the new head."
fi
ok "published head == reviewed head ($REVIEWED_HEAD)"

MERGE_STATE="$(gh pr view "$PR" --json mergeable,mergeStateStatus -q '.mergeable + "/" + .mergeStateStatus')"
[[ "$MERGE_STATE" == MERGEABLE/* ]] || die 2 "PR #$PR not mergeable: $MERGE_STATE"
ok "PR #$PR mergeable ($MERGE_STATE)"

CURRENT_BASE="$(git rev-parse origin/main)"
if [[ -n "$EXPECTED_BASE" && "$CURRENT_BASE" != "$EXPECTED_BASE"* ]]; then
  die 2 "base moved: expected $EXPECTED_BASE, origin/main is $CURRENT_BASE"
fi
ok "base origin/main == $CURRENT_BASE"

REVIEWED_TREE="$(git rev-parse "$REVIEWED_HEAD^{tree}" 2>/dev/null || echo "")"

if [[ $DRY_RUN -eq 1 ]]; then
  log "DRY RUN — all preconditions pass; stopping before merge."
  exit 0
fi

# ---------------------------------------------------------------------------
# 2. Squash merge, pinned to the exact reviewed head.
# ---------------------------------------------------------------------------
log "squash-merging PR #$PR at $REVIEWED_HEAD"
gh pr merge "$PR" --squash --match-head-commit "$REVIEWED_HEAD" \
  || die 2 "gh pr merge refused (head moved mid-flight?)"

git fetch -q origin
MERGE_SHA="$(git rev-parse origin/main)"
MERGED_TREE="$(git rev-parse "origin/main^{tree}")"
ok "merged to main: $MERGE_SHA (tree $MERGED_TREE)"

if [[ -n "$REVIEWED_TREE" && "$MERGED_TREE" != "$REVIEWED_TREE" ]]; then
  warn "merged tree $MERGED_TREE != reviewed tree $REVIEWED_TREE"
  warn "(expected for a squash that rewrites history; verify the diff if this surprises you)"
fi

# ---------------------------------------------------------------------------
# 3. Rebuild the helper at merged main. Compare hashes — skip a no-op deploy.
# ---------------------------------------------------------------------------
OLD_HASH=""
[[ -f "$LANE_PLUGINS/$HELPER_DLL_NAME" ]] && \
  OLD_HASH="$(sha256sum "$LANE_PLUGINS/$HELPER_DLL_NAME" | awk '{print $1}')"

log "building helper at merged main (net48 Release)"
BUILD_DIR="$(mktemp -d)"
git worktree add -q --detach "$BUILD_DIR" "$MERGE_SHA"
# shellcheck disable=SC2064
trap "git worktree remove --force '$BUILD_DIR' 2>/dev/null || true" EXIT

unset DOTNET_ROOT  # ~/.dotnet breaks this build on Prime-U
if ! dotnet build "$BUILD_DIR/$HELPER_PROJ" -c Release \
      --nologo -v minimal >"$BUILD_DIR/build.log" 2>&1; then
  tail -30 "$BUILD_DIR/build.log" >&2
  die 3 "helper build FAILED (log: $BUILD_DIR/build.log)"
fi

NEW_DLL="$(find "$BUILD_DIR" -name "$HELPER_DLL_NAME" -path '*/Release/*' | head -1)"
[[ -f "$NEW_DLL" ]] || die 3 "built DLL not found under $BUILD_DIR"
NEW_HASH="$(sha256sum "$NEW_DLL" | awk '{print $1}')"
ok "helper built: $NEW_HASH"

HELPER_CHANGED=1
if [[ "$NEW_HASH" == "$OLD_HASH" ]]; then
  HELPER_CHANGED=0
  ok "helper byte-identical to deployed — skipping redeploy (NOT a no-op deploy)"
fi

# ---------------------------------------------------------------------------
# 4. Deploy to BOTH paths. Plugin must live in its own subdirectory.
# ---------------------------------------------------------------------------
if [[ $HELPER_CHANGED -eq 1 ]]; then
  for dest in "$LANE_PLUGINS" "$CLIENT_PLUGINS"; do
    mkdir -p "$dest"
    cp -f "$NEW_DLL" "$dest/$HELPER_DLL_NAME" || die 4 "deploy failed: $dest"
    got="$(sha256sum "$dest/$HELPER_DLL_NAME" | awk '{print $1}')"
    [[ "$got" == "$NEW_HASH" ]] || die 4 "post-deploy hash mismatch at $dest: $got"
    ok "deployed -> $dest ($got)"
  done

  # 5. Repin the descriptor's helper hash. Change NOTHING else.
  [[ -f "$DESCRIPTOR" ]] || die 4 "descriptor not found: $DESCRIPTOR"
  tmp="$(mktemp)"
  jq --arg h "$NEW_HASH" '.pins.helper = $h' "$DESCRIPTOR" >"$tmp" \
    || die 4 "jq failed updating helper pin"
  mv "$tmp" "$DESCRIPTOR"
  chmod 600 "$DESCRIPTOR"
  ok "descriptor helper pin -> $NEW_HASH"
fi

# ---------------------------------------------------------------------------
# 6. Regenerate the wire token (always — it is short-lived by design).
# ---------------------------------------------------------------------------
NONCE="$(head -c 32 /dev/urandom | base64 | tr -d '\n=' | tr '+/' '-_')"
HMAC="$(head -c 32 /dev/urandom | base64 | tr -d '\n=' | tr '+/' '-_')"
OPTOK="$(head -c 32 /dev/urandom | base64 | tr -d '\n=' | tr '+/' '-_')"
EXPIRY_MS=$(( ($(date +%s) + 3600) * 1000 ))

tmp="$(mktemp)"
jq --arg n "$NONCE" --arg h "$HMAC" --arg o "$OPTOK" --argjson e "$EXPIRY_MS" \
   '.wire.nonce = $n | .wire.hmac_secret = $h
    | .wire.operator_token = $o | .wire.expiry_unix_ms = $e' \
   "$DESCRIPTOR" >"$tmp" || die 4 "jq failed regenerating wire token"
mv "$tmp" "$DESCRIPTOR"
chmod 600 "$DESCRIPTOR"
ok "wire token regenerated, expires $(date -d "@$((EXPIRY_MS/1000))" '+%H:%M:%S %Z')"

# 7. Production ports must not appear anywhere in the descriptor.
for port in "${PROD_PORTS[@]}"; do
  if grep -q "\b$port\b" "$DESCRIPTOR"; then
    die 4 "PRODUCTION PORT $port present in descriptor — refusing to proceed"
  fi
done
ok "no production ports (${PROD_PORTS[*]}) in descriptor"

# ---------------------------------------------------------------------------
# 8. Restart the lane and prove the guard ARMS.
# ---------------------------------------------------------------------------
if [[ $HELPER_CHANGED -eq 0 ]]; then
  log "helper unchanged — checking existing lane arming state without restart"
else
  log "restarting lane valheim-server (full BepInEx chainloader reload)"
  docker exec "$LANE_CONTAINER" supervisorctl restart valheim-server >/dev/null \
    || die 5 "lane restart failed"
  sleep 25
fi

LANE_LOG="$(docker logs --tail 400 "$LANE_CONTAINER" 2>&1 || true)"

DRIFT="$(grep -c 'assembly drift.*staying DISARMED' <<<"$LANE_LOG" || true)"
ARMED_LINE="$(grep -m1 'SBPR.QaHarness.T022 — ARMED' <<<"$LANE_LOG" || true)"
VERSION_LINE="$(grep -m1 'Valheim version:' <<<"$LANE_LOG" || true)"

echo
echo "  observed version : ${VERSION_LINE:-<none>}"
echo "  drift lines      : $DRIFT"
echo "  armed line       : ${ARMED_LINE:-<none>}"
echo

if [[ "$DRIFT" -gt 0 || -z "$ARMED_LINE" ]]; then
  die 5 "lane did NOT arm. This needs an agent, not a rerun — capture what the
  observer actually returned. Empty version => the read path is broken; a non-empty
  version matching no pin => genuine drift or a new build."
fi

ok "lane ARMED"
echo
echo "merge_sha=$MERGE_SHA"
echo "helper_sha256=$NEW_HASH"
echo "helper_changed=$HELPER_CHANGED"
echo "token_expiry_unix_ms=$EXPIRY_MS"
