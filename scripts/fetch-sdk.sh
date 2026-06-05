#!/usr/bin/env bash
# ============================================================================
# SBPR.Trailborne — fetch build SDK (BepInEx) (bash)
# ----------------------------------------------------------------------------
# Idempotently obtains the managed reference assemblies needed to COMPILE the
# mod against BepInEx / HarmonyX. Specifically it downloads the Thunderstore
# "BepInExPack_Valheim" and unpacks it into a gitignored .sdk/ directory, then
# prints the BEPINEX_CORE path to put in your .env.
#
#   Placement:  <repo>/.sdk/BepInExPack_Valheim/BepInEx/core   (gitignored)
#
# NOTE on Valheim's OWN assemblies (assembly_valheim.dll, UnityEngine*.dll):
#   Those are copyrighted game binaries and are NOT downloaded by this script.
#   They come from YOUR local Valheim install (own the game) — point
#   VALHEIM_MANAGED / VALHEIM_INSTALL at it (scripts/setup.sh auto-detects).
#   Nothing copyrighted is ever committed to this repo.
#
# Usage:
#   scripts/fetch-sdk.sh                 # fetch pinned BepInEx pack
#   scripts/fetch-sdk.sh --force         # re-download even if present
#   BEPINEX_VERSION=5.4.2333 scripts/fetch-sdk.sh
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SDK_DIR="$REPO_ROOT/.sdk"

# Pinned BepInExPack_Valheim version (Thunderstore: denikson / AzumattDev).
BEPINEX_VERSION="${BEPINEX_VERSION:-5.4.2333}"
PACKAGE_NAMESPACE="${BEPINEX_NAMESPACE:-denikson}"
PACKAGE_NAME="BepInExPack_Valheim"
DOWNLOAD_URL="https://thunderstore.io/package/download/${PACKAGE_NAMESPACE}/${PACKAGE_NAME}/${BEPINEX_VERSION}/"

FORCE=0
[ "${1:-}" = "--force" ] && FORCE=1

info() { printf '  %s\n' "$*"; }
ok()   { printf '  [ok] %s\n' "$*"; }
fail() { printf '  [x]  %s\n' "$*" >&2; exit 1; }

CORE_DIR="$SDK_DIR/${PACKAGE_NAME}/BepInEx/core"

if [ "$FORCE" -eq 0 ] && [ -f "$CORE_DIR/BepInEx.dll" ] && [ -f "$CORE_DIR/0Harmony.dll" ]; then
    ok "BepInEx core already present (use --force to re-fetch):"
    info "$CORE_DIR"
    printf '\n  Add this to your .env:\n    BEPINEX_CORE=%s\n' "$CORE_DIR"
    exit 0
fi

command -v unzip >/dev/null 2>&1 || fail "'unzip' is required (apt install unzip / brew install unzip)."
DL=""
if command -v curl >/dev/null 2>&1; then DL="curl"; elif command -v wget >/dev/null 2>&1; then DL="wget"; else
    fail "need 'curl' or 'wget' to download the SDK."
fi

mkdir -p "$SDK_DIR"
TMP_ZIP="$(mktemp --suffix=.zip)"
trap 'rm -f "$TMP_ZIP"' EXIT

info "Fetching ${PACKAGE_NAME} v${BEPINEX_VERSION} from Thunderstore..."
if [ "$DL" = "curl" ]; then
    curl -fSL --retry 3 -o "$TMP_ZIP" "$DOWNLOAD_URL" || fail "download failed: $DOWNLOAD_URL"
else
    wget -q -O "$TMP_ZIP" "$DOWNLOAD_URL" || fail "download failed: $DOWNLOAD_URL"
fi

# Thunderstore packages are zips containing the pack at the root (the pack
# itself contains BepInExPack_Valheim/). Extract into a clean staging dir.
STAGE="$(mktemp -d)"
trap 'rm -f "$TMP_ZIP"; rm -rf "$STAGE"' EXIT
unzip -q -o "$TMP_ZIP" -d "$STAGE" || fail "unzip failed."

# Find the BepInExPack_Valheim folder inside the extracted package.
SRC_PACK="$(find "$STAGE" -maxdepth 3 -type d -name "$PACKAGE_NAME" | head -n1 || true)"
[ -n "$SRC_PACK" ] || fail "could not find '$PACKAGE_NAME' inside the downloaded package."

rm -rf "$SDK_DIR/${PACKAGE_NAME}"
mkdir -p "$SDK_DIR"
cp -R "$SRC_PACK" "$SDK_DIR/"

[ -f "$CORE_DIR/BepInEx.dll" ] || fail "post-extract sanity check failed: $CORE_DIR/BepInEx.dll missing."

ok "BepInEx core installed at:"
info "$CORE_DIR"
printf '\n  Add this to your .env:\n    BEPINEX_CORE=%s\n' "$CORE_DIR"
printf '\n  (scripts/setup.sh will pick this up automatically on its next run.)\n'
