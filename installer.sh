#!/usr/bin/env bash
# ___ ___ ___ ___   _____        _ _ _
#/ __| _ ) _ \ _ \ |_   _| _ __ _(_) | |__  ___ _ _ _ _  ___
#\__ \ _ \  _/   /   | || '_/ _` | | | '_ \/ _ \ '_| ' \/ -_)
#|___/___/_| |_|_\   |_||_| \__,_|_|_|_.__/\___/_| |_||_\___|
#
#  Trailborne playtest bootstrapper (Linux / macOS — Steam Valheim)
#  Repo:  https://github.com/PolyphonyRequiem/SBPRValheimMods   (MIT)
#
#  The bash twin of installer.ps1. Same safety model:
#    1. Locates your Steam Valheim install (vanilla).
#    2. COPIES it (never moves/modifies) to a separate modded folder:
#         ${XDG_DATA_HOME:-~/.local/share}/Trailborne/Valheim-Modded
#         (macOS: ~/Library/Application Support/Trailborne/Valheim-Modded)
#       Your vanilla install stays pristine — Steam still launches it normally.
#    3. Downloads the Trailborne modpack from the GitHub Release, verifies its
#       SHA256, and overlays it (the SAME cross-platform zip the PS1 uses — it
#       already ships the Linux/macOS doorstop loaders).
#    4. Writes a launcher (run-trailborne.sh) that boots the MODDED copy with
#       BepInEx via doorstop, pointed at Steam so multiplayer/ownership work.
#    5. Prints the live server join info.
#
#  Vanilla Valheim is only READ, never modified. Fully removable: delete the
#  Trailborne folder.
#
#  RUN IT (Linux/macOS):
#    curl -fsSL https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.sh | bash
#
#  ADVANCED (pass options): download, then run with flags:
#    curl -fsSL .../installer.sh -o install.sh && bash install.sh --force --no-console
#  Options: --force (skip prompt) --no-console (omit -console) --no-launch-test
#           --zip <path> (install from a local zip, skip download)
#           --url <u> --sha256 <h> (override the pinned release asset)
#           --keep <name> / --keep-config <name> (preserve a plugin/config the
#               build doesn't ship — repeatable)
#           --no-stomp (keep unexpected plugins) --no-config-reset (keep your .cfg)
#  By DEFAULT each run RESETS BepInEx/plugins + BepInEx/config to exactly what this
#  build ships (removing dev-session leftovers + stale config so new defaults apply);
#  BepInEx.cfg is always preserved. See the allowlist near the top to keep exceptions.
# ============================================================================
set -euo pipefail

# ── Pinned release asset (bumped together by the CI auto-pin step, like the PS1) ──
MODPACK_URL='https://github.com/PolyphonyRequiem/SBPRValheimMods/releases/download/v0.2.39-playtest/SBPR-Trailborne-Modpack-v0.2.39.zip'
EXPECTED_SHA256='39aa4204d9a36725ba5ce070d1f8d5167866378cf9b6a73bb2e9720edb363a5d'
# Live server status (join code drifts on every restart — fetched at runtime).
STATUS_URL='https://gist.githubusercontent.com/PolyphonyRequiem/7b54a29aeefb3effee0393df79d0b03e/raw/niflheim-status.json'
MODDED_DIRNAME='Valheim-Modded'
VERSION='0.1.0 (2026-06-18)'

# ── Reset-to-shipped-baseline allowlist (the "limited set of exceptions") ─────
# BepInEx/plugins and BepInEx/config are write-once-accumulate across installs
# (the game-file copy excludes BepInEx to preserve loaders), so anything ever
# dropped there during a dev session survives forever and keeps loading even
# though it was never in the shipped modpack. On every install we therefore RESET
# the modded BepInEx tree to exactly what the build ships, MINUS these allowlists.
#
# The expected/baseline set is derived from the shipped zip itself (zero upkeep) —
# these arrays are ONLY for things we deliberately keep that are NOT in the zip.
# Default: EMPTY (stomp everything unexpected). To allow an exception centrally,
# add its name here and push to main — the one-liner re-fetches this file each run,
# so it's live for every tester immediately. Power users can also pass --keep /
# --keep-config at runtime, or --no-stomp / --no-config-reset to skip entirely.
#
# PLUGIN names = the plugins/<NAME> folder (or a loose <NAME>.dll).
# CONFIG names = the config/<NAME> filename (BepInEx.cfg is ALWAYS preserved).
PLUGIN_ALLOWLIST=()
CONFIG_ALLOWLIST=()

FORCE=0; NO_CONSOLE=0; NO_LAUNCH_TEST=0; LOCAL_ZIP=''
NO_STOMP=0; NO_CONFIG_RESET=0; KEEP_PLUGINS=(); KEEP_CONFIGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --force) FORCE=1; shift ;;
    --no-console) NO_CONSOLE=1; shift ;;
    --no-launch-test) NO_LAUNCH_TEST=1; shift ;;
    --zip) LOCAL_ZIP="${2:-}"; shift 2 ;;
    --url) MODPACK_URL="${2:-}"; shift 2 ;;
    --sha256) EXPECTED_SHA256="${2:-}"; shift 2 ;;
    --no-stomp) NO_STOMP=1; shift ;;          # keep unexpected plugins (don't reset to shipped set)
    --no-config-reset) NO_CONFIG_RESET=1; shift ;;  # keep existing SBPR config (don't reset to defaults)
    --keep) KEEP_PLUGINS+=("${2:-}"); shift 2 ;;     # preserve plugins/<name> this run (repeatable)
    --keep-config) KEEP_CONFIGS+=("${2:-}"); shift 2 ;;  # preserve config/<name> this run (repeatable)
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

# ── pretty ───────────────────────────────────────────────────────────────────
c() { printf '\033[%sm' "$1"; }
say()  { printf '%s\n' "$*"; }
step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
okm()  { printf '\033[1;32m  + %s\033[0m\n' "$*"; }
note() { printf '\033[0;90m  - %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m  ! %s\033[0m\n' "$*"; }
die()  { printf '\n\033[1;31mXX %s\033[0m\n' "$*" >&2; exit 1; }

OS="$(uname -s)"
case "$OS" in
  Linux)  PLATFORM=linux;  CLIENT_EXE='valheim.x86_64' ;;
  Darwin) PLATFORM=macos;  CLIENT_EXE='valheim.app/Contents/MacOS/valheim' ;;
  *) die "Unsupported OS '$OS' — this installer is Linux/macOS. On Windows use installer.ps1." ;;
esac

command -v curl >/dev/null 2>&1 || die "'curl' is required."
command -v unzip >/dev/null 2>&1 || die "'unzip' is required (install it: apt/dnf/brew install unzip)."
SHACMD=""
if command -v sha256sum >/dev/null 2>&1; then SHACMD="sha256sum"
elif command -v shasum >/dev/null 2>&1; then SHACMD="shasum -a 256"
else die "need sha256sum or shasum for checksum verification."; fi
sha_of() { $SHACMD "$1" | cut -d' ' -f1; }

say ""
printf '\033[1;37mTrailborne playtest bootstrapper  v%s  (%s)\033[0m\n' "$VERSION" "$PLATFORM"
say "Copies Valheim to a modded folder (vanilla untouched), installs the mod, writes a launcher."

# ── Live server status (fetched, not hardcoded) ───────────────────────────────
SRV_CODE=""; SRV_STATUS=""; SRV_STAMP=""
if STATUS_JSON="$(curl -fsSL --max-time 10 "$STATUS_URL?cb=$RANDOM" 2>/dev/null)"; then
  SRV_CODE="$(printf '%s' "$STATUS_JSON"   | grep -oE '"join_code"[^,]*'  | grep -oE '[0-9]+' | head -1)"
  SRV_STATUS="$(printf '%s' "$STATUS_JSON" | grep -oE '"status"[^,]*'     | sed -E 's/.*:\s*"?([a-zA-Z]+)"?.*/\1/' | head -1)"
  SRV_STAMP="$(printf '%s' "$STATUS_JSON"  | grep -oE '"updated_utc"[^,]*'| sed -E 's/.*:\s*"([^"]*)".*/\1/' | head -1)"
fi
join_line() {
  if [ -z "$SRV_STATUS" ]; then echo "join code: (couldn't reach status — ask Daniel for the current code)";
  elif [ "$SRV_STATUS" != "online" ]; then echo "server is currently OFFLINE (checked just now) — ask Daniel";
  else echo "join code  $SRV_CODE   (live, as of $SRV_STAMP)"; fi
}

# ── 1. Locate Steam + Valheim ─────────────────────────────────────────────────
step "Locating Steam and Valheim"
CANDIDATE_LIBS=()
if [ "$PLATFORM" = linux ]; then
  CANDIDATE_LIBS+=(
    "$HOME/.steam/steam" "$HOME/.local/share/Steam" "$HOME/.steam/debian-installation"
    "$HOME/.steam/root" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
  )
else
  CANDIDATE_LIBS+=( "$HOME/Library/Application Support/Steam" )
fi
# Parse libraryfolders.vdf for non-default library drives.
for base in "${CANDIDATE_LIBS[@]}"; do
  vdf="$base/steamapps/libraryfolders.vdf"
  [ -f "$vdf" ] || continue
  while IFS= read -r p; do [ -n "$p" ] && CANDIDATE_LIBS+=("$p"); done < <(grep -oE '"path"[[:space:]]*"[^"]+"' "$vdf" 2>/dev/null | sed -E 's/.*"path"[[:space:]]*"([^"]+)".*/\1/')
done

VANILLA=""
for lib in "${CANDIDATE_LIBS[@]}"; do
  cand="$lib/steamapps/common/Valheim"
  if [ -f "$cand/$CLIENT_EXE" ] || [ -f "$cand/valheim.x86_64" ]; then VANILLA="$cand"; break; fi
done
[ -n "$VANILLA" ] || die "Couldn't find a Steam Valheim install (looked for steamapps/common/Valheim with $CLIENT_EXE). Install Valheim via Steam, then re-run."
VANILLA="$(cd "$VANILLA" && pwd)"
okm "Vanilla Valheim: $VANILLA"

# ── 2. Plan the modded copy ───────────────────────────────────────────────────
if [ "$PLATFORM" = macos ]; then BASE="$HOME/Library/Application Support/Trailborne"
else BASE="${XDG_DATA_HOME:-$HOME/.local/share}/Trailborne"; fi
MODDED="$BASE/$MODDED_DIRNAME"
DOWNLOADS="$BASE/downloads"

step "Plan"
note "Modded copy  -> $MODDED"
note "Vanilla stays pristine (Steam still launches it normally)."
note "Server status: $(join_line)"

if [ "$FORCE" != 1 ]; then
  printf '\nProceed? This copies Valheim (~1-2 GB) into the modded folder. [Y/n] '
  read -r resp </dev/tty || resp=""
  case "$resp" in ""|y|Y|yes|YES) ;; *) say "Aborted — nothing changed."; exit 0 ;; esac
fi
mkdir -p "$BASE" "$DOWNLOADS" "$MODDED"

# ── 3. Copy vanilla -> modded (vanilla only READ) ─────────────────────────────
step "Copying Valheim into the modded folder (vanilla is only READ)"
# Preserve the mod overlay on re-runs: refresh game files but keep BepInEx + loaders.
EXCL=(BepInEx doorstop_libs doorstop_config.ini .doorstop_version winhttp.dll run_bepinex.sh run-trailborne.sh)
if command -v rsync >/dev/null 2>&1; then
  rsync_excl=(); for e in "${EXCL[@]}"; do rsync_excl+=(--exclude "$e"); done
  rsync -a --delete "${rsync_excl[@]}" "$VANILLA"/ "$MODDED"/ || die "rsync copy failed."
else
  warn "rsync not found — using cp -a (slower; first run only is fine)."
  cp -a "$VANILLA"/. "$MODDED"/ || die "cp copy failed."
fi
# steam_appid.txt lets the modded copy talk to Steam (multiplayer/ownership).
printf '892970' > "$MODDED/steam_appid.txt"
note "Wrote steam_appid.txt (892970) so the modded copy authenticates with Steam."
okm "Game files copied (vanilla untouched)."

# ── 4. Obtain + verify the modpack ────────────────────────────────────────────
ZIP="$DOWNLOADS/SBPR-Trailborne-Modpack.zip"
if [ -n "$LOCAL_ZIP" ]; then
  step "Using local modpack zip"
  [ -f "$LOCAL_ZIP" ] || die "--zip path not found: $LOCAL_ZIP"
  cp "$LOCAL_ZIP" "$ZIP"
  # Auto-adopt sidecar sha if present and no explicit --sha256 override was given.
  if [ -f "$LOCAL_ZIP.sha256" ] && [ "$EXPECTED_SHA256" = 'a2bb2e07cf611dbb119106477d13df510ade2be3c0e245fb7e5f918ed1fad2ae' ]; then
    EXPECTED_SHA256="$(cut -d' ' -f1 < "$LOCAL_ZIP.sha256")"
    note "Adopted checksum from sidecar: $EXPECTED_SHA256"
  fi
else
  step "Downloading the Trailborne modpack"
  note "$MODPACK_URL"
  curl -fSL --retry 3 --retry-delay 2 -o "$ZIP" "$MODPACK_URL" || die "Couldn't download the modpack. If the release isn't published yet, ping Daniel."
fi
GOT="$(sha_of "$ZIP")"
if [ -n "$EXPECTED_SHA256" ] && [ "$GOT" != "$EXPECTED_SHA256" ]; then
  die "Checksum mismatch! expected $EXPECTED_SHA256 got $GOT — refusing to install a tampered/corrupt modpack."
fi
okm "Modpack ready + SHA256 verified."

# ── 5. Overlay the modpack into the modded copy ───────────────────────────────
step "Installing BepInEx + Trailborne into the modded copy"
EXTRACT="$DOWNLOADS/extract"
rm -rf "$EXTRACT"; mkdir -p "$EXTRACT"
unzip -oq "$ZIP" -d "$EXTRACT"
# The zip has a single top folder; the PAYLOAD ROOT is the dir holding the
# doorstop loader + config. We key on `.doorstop_version` (a root-level marker
# present on all 3 OSes) rather than winhttp.dll (the PS1's Windows-only key) or
# the .so itself (which lives one level down in doorstop_libs/, NOT at the root).
PAYLOAD="$(dirname "$(find "$EXTRACT" -name '.doorstop_version' 2>/dev/null | head -1)")"
if [ -z "$PAYLOAD" ] || [ ! -d "$PAYLOAD" ]; then
  # Fallback: parent of the doorstop_libs/ dir that holds the platform loader.
  loaderdir="$(dirname "$(find "$EXTRACT" \( -name 'libdoorstop_x64.so' -o -name 'libdoorstop_x64.dylib' \) 2>/dev/null | head -1)")"
  [ -n "$loaderdir" ] && PAYLOAD="$(dirname "$loaderdir")"
fi
{ [ -n "$PAYLOAD" ] && [ -d "$PAYLOAD" ]; } || die "Modpack layout unexpected (no doorstop loader inside)."
cp -a "$PAYLOAD"/. "$MODDED"/
PLUGIN_DLL="$MODDED/BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"
[ -f "$PLUGIN_DLL" ] || die "Overlay finished but the Trailborne DLL isn't where expected. Aborting so you don't launch a half-install."
# Clear BepInEx's typeloader cache so an UPGRADE always re-reads plugin metadata.
# BepInEx invalidates this cache by DLL mtime+length, but the modpack is packed
# DETERMINISTICALLY (every file pinned to a constant 2020-01-01 mtime), so two
# releases whose DLLs happen to share a byte-length look "unchanged" to BepInEx —
# it then reuses the OLD release's cached type metadata and prints the PREVIOUS
# version in its "Loading [SBPR Trailborne x.y.z]" banner even though the new code
# is what actually runs. The cache is regenerable; deleting it on every install is
# harmless and kills the stale-banner class outright. (Refreshes preserve BepInEx/
# via the overlay, so without this the stale cache survives forever.)
rm -rf "$MODDED/BepInEx/cache"
okm "BepInEx doorstop + Trailborne DLL + icons in place."

# ── 5b. Reset the BepInEx tree to the shipped baseline (stomp + config-reset) ──
# WHY: the game-file copy (step 3) excludes BepInEx so loaders/config survive a
# refresh — but that makes BepInEx/plugins and BepInEx/config WRITE-ONCE-ACCUMULATE.
# A plugin or .cfg dropped in once (a dev-session DLL, a fork, hand-tuned values)
# then loads on EVERY launch forever, even though it was never in the shipped zip.
# So after overlaying we RESET to exactly what THIS build ships, minus an allowlist.
# The "expected" set is read from the freshly-extracted payload ($PAYLOAD) itself,
# so it tracks the build with zero maintenance. Loud by design — prints removals.
shipped_set() {  # names present under the shipped payload's BepInEx/<1=plugins|2=config>
  local sub="$1"
  [ -d "$PAYLOAD/BepInEx/$sub" ] || return 0
  ( cd "$PAYLOAD/BepInEx/$sub" && find . -maxdepth 1 -mindepth 1 -printf '%f\n' 2>/dev/null )
}
in_list() { local n="$1"; shift; local x; for x in "$@"; do [ "$x" = "$n" ] && return 0; done; return 1; }

# (a) STOMP unexpected plugins — reset plugins/ to (shipped ∪ allowlist ∪ --keep).
if [ "$NO_STOMP" != 1 ]; then
  step "Resetting plugins to the shipped set (removing anything unexpected)"
  mapfile -t SHIPPED_PLUGINS < <(shipped_set plugins)
  KEEP_P=( "${SHIPPED_PLUGINS[@]}" "${PLUGIN_ALLOWLIST[@]}" "${KEEP_PLUGINS[@]}" )
  removed=0
  if [ -d "$MODDED/BepInEx/plugins" ]; then
    while IFS= read -r entry; do
      [ -n "$entry" ] || continue
      if in_list "$entry" "${KEEP_P[@]}"; then continue; fi
      rm -rf "$MODDED/BepInEx/plugins/$entry"
      warn "removed unexpected plugin: $entry  (not in this build; pass --keep '$entry' or add to PLUGIN_ALLOWLIST to keep)"
      removed=$((removed+1))
    done < <(cd "$MODDED/BepInEx/plugins" && find . -maxdepth 1 -mindepth 1 -printf '%f\n' 2>/dev/null)
  fi
  if [ "$removed" = 0 ]; then okm "Plugins match the shipped set (nothing unexpected)."
  else okm "Reset plugins to the shipped set ($removed removed)."; fi
  [ ${#PLUGIN_ALLOWLIST[@]} -gt 0 ] && note "Kept by allowlist: ${PLUGIN_ALLOWLIST[*]}"
  [ ${#KEEP_PLUGINS[@]}    -gt 0 ] && note "Kept by --keep:    ${KEEP_PLUGINS[*]}"
else
  warn "--no-stomp: leaving any extra plugins in place (they will load)."
fi

# (b) CONFIG-RESET — delete generated plugin .cfg so BepInEx regenerates this
# build's DEFAULTS. A returning tester's existing .cfg keeps OLD values for every
# key it already has (BepInEx only writes defaults for MISSING keys), silently
# defeating new shipped defaults (e.g. a new corona/halo default). We always keep
# BepInEx.cfg (framework settings) + the shipped configs + the allowlist/--keep-config.
if [ "$NO_CONFIG_RESET" != 1 ]; then
  step "Resetting plugin configs to this build's defaults"
  mapfile -t SHIPPED_CONFIGS < <(shipped_set config)
  KEEP_C=( BepInEx.cfg "${SHIPPED_CONFIGS[@]}" "${CONFIG_ALLOWLIST[@]}" "${KEEP_CONFIGS[@]}" )
  wiped=0
  if [ -d "$MODDED/BepInEx/config" ]; then
    while IFS= read -r cfg; do
      [ -n "$cfg" ] || continue
      case "$cfg" in *.cfg) ;; *) continue ;; esac   # only touch .cfg files
      if in_list "$cfg" "${KEEP_C[@]}"; then continue; fi
      rm -f "$MODDED/BepInEx/config/$cfg"
      note "reset config (regenerates at defaults): $cfg"
      wiped=$((wiped+1))
    done < <(cd "$MODDED/BepInEx/config" && find . -maxdepth 1 -mindepth 1 -name '*.cfg' -printf '%f\n' 2>/dev/null)
  fi
  if [ "$wiped" = 0 ]; then okm "No stale plugin config to reset."
  else okm "Reset $wiped plugin config(s) — BepInEx regenerates them at this build's defaults on launch."; fi
  [ ${#CONFIG_ALLOWLIST[@]} -gt 0 ] && note "Kept by config allowlist: ${CONFIG_ALLOWLIST[*]}"
  [ ${#KEEP_CONFIGS[@]}     -gt 0 ] && note "Kept by --keep-config:    ${KEEP_CONFIGS[*]}"
else
  warn "--no-config-reset: keeping your existing config (new build defaults will NOT apply to keys you already have)."
fi

# ── 6. Launcher (doorstop 4.x env contract) ───────────────────────────────────
step "Creating the launcher"
CONSOLE_ARG=" -console"; [ "$NO_CONSOLE" = 1 ] && CONSOLE_ARG=""
LAUNCHER="$MODDED/run-trailborne.sh"
if [ "$PLATFORM" = macos ]; then DOORSTOP_LIB='libdoorstop_x64.dylib'; PRELOAD_VAR='DYLD_INSERT_LIBRARIES'; LIBPATH_VAR='DYLD_LIBRARY_PATH'
else DOORSTOP_LIB='libdoorstop_x64.so'; PRELOAD_VAR='LD_PRELOAD'; LIBPATH_VAR='LD_LIBRARY_PATH'; fi
cat > "$LAUNCHER" <<EOF
#!/usr/bin/env bash
# Launches the MODDED Valheim copy with BepInEx via doorstop 4.x.
# Steam must be running so Steamworks (multiplayer/ownership) initializes.
set -e
HERE="\$(cd "\$(dirname "\${BASH_SOURCE[0]}")" && pwd)"
cd "\$HERE"
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY="\$HERE/BepInEx/core/BepInEx.Preloader.dll"
export $LIBPATH_VAR="\$HERE/doorstop_libs:\${$LIBPATH_VAR:-}"
export $PRELOAD_VAR="\$HERE/doorstop_libs/$DOORSTOP_LIB:\${$PRELOAD_VAR:-}"
echo "Starting modded Valheim (Trailborne)..."
exec "\$HERE/$CLIENT_EXE"$CONSOLE_ARG "\$@"
EOF
chmod +x "$LAUNCHER"
okm "Wrote $LAUNCHER"
[ "$NO_CONSOLE" != 1 ] && note "Launcher includes -console (press F5 in-game for the dev console)."

# ── 6b. Optional headless boot smoke-test (Linux only — proves the mod loads) ──
if [ "$PLATFORM" = linux ] && [ "$NO_LAUNCH_TEST" != 1 ]; then
  step "Quick headless boot check (proves the mod actually loads)"
  rm -f "$MODDED/BepInEx/LogOutput.log" 2>/dev/null || true
  ( cd "$MODDED"
    export DOORSTOP_ENABLED=1 DOORSTOP_TARGET_ASSEMBLY="$MODDED/BepInEx/core/BepInEx.Preloader.dll"
    export LD_LIBRARY_PATH="$MODDED/doorstop_libs:${LD_LIBRARY_PATH:-}"
    export LD_PRELOAD="$MODDED/doorstop_libs/libdoorstop_x64.so:${LD_PRELOAD:-}"
    timeout 16 ./valheim.x86_64 -batchmode -nographics -console >/tmp/trailborne_boot.log 2>&1 || true
  )
  if grep -qiE "Loading \[SBPR Trailborne" "$MODDED/BepInEx/LogOutput.log" 2>/dev/null; then
    okm "Mod loaded: $(grep -oE 'Loading \[SBPR Trailborne[^]]*\]' "$MODDED/BepInEx/LogOutput.log" | head -1)"
    note "(The trailing segfault on -nographics headless is EXPECTED — the mod already loaded.)"
  else
    warn "Couldn't confirm the mod loaded headlessly (this can be a false negative on some boxes)."
    warn "If the GPU launch also fails, check $MODDED/BepInEx/LogOutput.log and ping Daniel."
  fi
fi

# ── 7. Verify vanilla stayed pristine ─────────────────────────────────────────
if [ -d "$VANILLA/BepInEx" ]; then warn "Note: vanilla install has a BepInEx folder (pre-existing — not from this installer)."
else note "Vanilla install is clean (no BepInEx leaked into it)."; fi

# ── 8. Done ───────────────────────────────────────────────────────────────────
say ""
printf '\033[1;32m============================================================\033[0m\n'
printf '\033[1;32m Trailborne is installed. Vanilla Valheim is untouched.\033[0m\n'
printf '\033[1;32m============================================================\033[0m\n'
say ""
printf '\033[1;37m Play:    %s\033[0m\n' "$LAUNCHER"
note "         (run it from a terminal; Steam must be running)"
printf '\033[1;37m Folder:  %s\033[0m\n' "$MODDED"
printf '\033[1;37m Server:  %s\033[0m\n' "$(join_line)"
say ""
note "First launch shows BepInEx console output — that's normal."
[ "$NO_CONSOLE" != 1 ] && note "In-game: press F5 for the dev console (admin commands work once you're an admin)."
note "To uninstall: delete  $BASE"
say ""
