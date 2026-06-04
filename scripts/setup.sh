#!/usr/bin/env bash
# ============================================================================
# SBPR.Trailborne — developer setup (bash)
# ----------------------------------------------------------------------------
# Helps a fresh developer get to a green build:
#   1. Verifies prerequisites (.NET SDK).
#   2. Locates the Valheim "Managed" assembly folder (or accepts a hint).
#   3. Ensures the BepInEx core folder exists (offers to fetch it).
#   4. Writes / updates a gitignored repo-root .env with the resolved paths.
#
# Usage:
#   scripts/setup.sh                      # interactive-ish auto-detect
#   VALHEIM_INSTALL=/path/to/game scripts/setup.sh
#   VALHEIM_MANAGED=/path/.../Managed BEPINEX_CORE=/path/.../core scripts/setup.sh
#
# Safe to re-run (idempotent): existing valid .env values are kept unless you
# override them via environment variables.
# ============================================================================
set -euo pipefail

# --- locate repo root (this script lives in <root>/scripts) -----------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

info()  { printf '  %s\n' "$*"; }
ok()    { printf '  [ok] %s\n' "$*"; }
warn()  { printf '  [!]  %s\n' "$*" >&2; }
fail()  { printf '  [x]  %s\n' "$*" >&2; exit 1; }
head()  { printf '\n== %s ==\n' "$*"; }

# Read a KEY=value from an existing .env (returns empty if absent).
read_env() {
    local key="$1"
    [ -f "$ENV_FILE" ] || return 0
    sed -n -E "s/^[[:space:]]*${key}[[:space:]]*=[[:space:]]*(.*[^[:space:]])[[:space:]]*$/\1/p" "$ENV_FILE" | tail -n1
}

# Write/replace KEY=value in .env (creates the file if needed).
write_env() {
    local key="$1" val="$2"
    touch "$ENV_FILE"
    if grep -qE "^[[:space:]]*${key}[[:space:]]*=" "$ENV_FILE"; then
        # replace in place via a temp file (portable; no in-place sed -i quirks)
        local tmp; tmp="$(mktemp)"
        awk -v k="$key" -v v="$val" '
            BEGIN { done=0 }
            $0 ~ "^[[:space:]]*" k "[[:space:]]*=" { print k "=" v; done=1; next }
            { print }
            END { if (!done) print k "=" v }
        ' "$ENV_FILE" > "$tmp"
        mv "$tmp" "$ENV_FILE"
    else
        printf '%s=%s\n' "$key" "$val" >> "$ENV_FILE"
    fi
}

head "Prerequisites"
if command -v dotnet >/dev/null 2>&1; then
    ok "dotnet $(dotnet --version)"
else
    fail "the .NET SDK ('dotnet') is not on PATH. Install .NET SDK 8.x: https://dotnet.microsoft.com/download"
fi

# ----------------------------------------------------------------------------
head "Valheim managed assemblies"
# Precedence: explicit env > existing .env > auto-detect common locations.
VM="${VALHEIM_MANAGED:-$(read_env VALHEIM_MANAGED)}"

if [ -z "$VM" ]; then
    VI="${VALHEIM_INSTALL:-$(read_env VALHEIM_INSTALL)}"
    candidates=()
    [ -n "$VI" ] && candidates+=("$VI/valheim_server_Data/Managed" "$VI/valheim_Data/Managed")
    # Common Steam locations (Linux / macOS / WSL / Proton).
    candidates+=(
        "$HOME/.steam/steam/steamapps/common/Valheim dedicated server/valheim_server_Data/Managed"
        "$HOME/.steam/steam/steamapps/common/Valheim/valheim_Data/Managed"
        "$HOME/.local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed"
        "$HOME/Library/Application Support/Steam/steamapps/common/Valheim/valheim.app/Contents/Resources/Data/Managed"
    )
    for c in "${candidates[@]}"; do
        if [ -f "$c/assembly_valheim.dll" ]; then VM="$c"; break; fi
    done
fi

if [ -z "$VM" ]; then
    warn "Could not auto-detect the Valheim Managed folder."
    info "Set it explicitly and re-run, e.g.:"
    info "  VALHEIM_MANAGED=/path/to/.../valheim_server_Data/Managed scripts/setup.sh"
    info "  (or VALHEIM_INSTALL=/path/to/game/root)"
elif [ ! -f "$VM/assembly_valheim.dll" ]; then
    warn "VALHEIM_MANAGED='$VM' does not contain assembly_valheim.dll — double-check the path."
    write_env VALHEIM_MANAGED "$VM"
else
    ok "Valheim Managed: $VM"
    write_env VALHEIM_MANAGED "$VM"
fi

# ----------------------------------------------------------------------------
head "BepInEx core assemblies"
BC="${BEPINEX_CORE:-$(read_env BEPINEX_CORE)}"
DEFAULT_SDK_CORE="$REPO_ROOT/.sdk/BepInExPack_Valheim/BepInEx/core"

if [ -z "$BC" ] && [ -f "$DEFAULT_SDK_CORE/BepInEx.dll" ]; then
    BC="$DEFAULT_SDK_CORE"
fi

if [ -z "$BC" ] || [ ! -f "$BC/BepInEx.dll" ]; then
    warn "BepInEx core not found."
    info "Fetch it locally (downloads the Thunderstore BepInExPack_Valheim into .sdk/):"
    info "  scripts/fetch-sdk.sh"
    info "...then re-run scripts/setup.sh, or set BEPINEX_CORE / BEPINEX_PATH yourself."
else
    ok "BepInEx core: $BC"
    write_env BEPINEX_CORE "$BC"
fi

# ----------------------------------------------------------------------------
head "Result"
if [ -f "$ENV_FILE" ]; then
    ok ".env written at: $ENV_FILE"
    info "Current contents:"
    sed -n -E 's/^/    /p' "$ENV_FILE"
fi

if [ -n "$VM" ] && [ -f "$VM/assembly_valheim.dll" ] && [ -n "${BC:-}" ] && [ -f "${BC:-/nonexistent}/BepInEx.dll" ]; then
    cat <<EOF

  You're ready to build:
    dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
EOF
else
    cat <<EOF

  Not fully configured yet. Resolve the warnings above, then build:
    dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
EOF
fi
