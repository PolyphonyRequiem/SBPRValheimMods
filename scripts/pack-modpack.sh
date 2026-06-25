#!/usr/bin/env bash
# ============================================================================
# SBPR.Trailborne — assemble the playtest modpack zip  (single source of truth)
# ----------------------------------------------------------------------------
# Produces the SAME artifact the client installer (installer.ps1) downloads:
#
#   <out>/SBPR-Trailborne-Modpack-v<ver>.zip
#   <out>/SBPR-Trailborne-Modpack-v<ver>.zip.sha256
#
# Layout inside the zip (top folder = SBPR-Trailborne-Modpack):
#   winhttp.dll, doorstop_config.ini, doorstop_libs/, .doorstop_version
#   BepInEx/core/*            (stock BepInExPack_Valheim core)
#   BepInEx/config/BepInEx.cfg
#   BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll   <- freshly built
#   BepInEx/plugins/SBPR.Trailborne/*.png                 <- item icons
#
# This is BYTE-LAYOUT-COMPATIBLE with the v0.1.0 release validated on 2026-06-04:
# it is the stock BepInExPack minus three non-functional helper files
# (changelog.txt, start_game_bepinex.sh, start_server_bepinex.sh), with our
# plugin folder overlaid.
#
# Used by:
#   * developers, locally:   scripts/pack-modpack.sh
#   * CI release workflow:    .github/workflows/release.yml
#
# Requirements: bash, unzip, zip, sha256sum (coreutils), and a built plugin DLL.
# It will run scripts/fetch-sdk.sh automatically if the stock pack is absent.
#
# Usage:
#   scripts/pack-modpack.sh \
#       --dll  src/SBPR.Trailborne/bin/Release/SBPR.Trailborne.dll \
#       --out  dist
#   # optional: --version 0.1.0  (default: read from the .csproj <Version>)
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DLL=""
OUT="$REPO_ROOT/dist"
VERSION=""
PACK_NAME="SBPR-Trailborne-Modpack"
PLUGIN_DIRNAME="SBPR.Trailborne"

info() { printf '  %s\n' "$*"; }
ok()   { printf '  [ok] %s\n' "$*"; }
fail() { printf '  [x]  %s\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --dll)     DLL="$2"; shift 2 ;;
        --out)     OUT="$2"; shift 2 ;;
        --version) VERSION="$2"; shift 2 ;;
        *) fail "unknown arg: $1" ;;
    esac
done

command -v unzip     >/dev/null 2>&1 || fail "'unzip' is required."
command -v zip       >/dev/null 2>&1 || fail "'zip' is required."
command -v sha256sum >/dev/null 2>&1 || fail "'sha256sum' is required."

# ── Resolve the plugin DLL ──────────────────────────────────────────────────
[ -n "$DLL" ] || DLL="$REPO_ROOT/src/SBPR.Trailborne/bin/Release/SBPR.Trailborne.dll"
[ -f "$DLL" ] || fail "plugin DLL not found: $DLL  (build it first: dotnet build -c Release)"

# ── Resolve the version (from .csproj <Version> if not given) ───────────────
if [ -z "$VERSION" ]; then
    CSPROJ="$REPO_ROOT/src/SBPR.Trailborne/SBPR.Trailborne.csproj"
    VERSION="$(grep -oE '<Version>[^<]+</Version>' "$CSPROJ" 2>/dev/null | head -1 | sed -E 's/<\/?Version>//g')"
    [ -n "$VERSION" ] || fail "could not read <Version> from $CSPROJ; pass --version."
fi
ok "Packaging Trailborne modpack v$VERSION"

# ── Ensure the stock BepInExPack is present (fetch if needed) ────────────────
STOCK="$REPO_ROOT/.sdk/BepInExPack_Valheim"
if [ ! -f "$STOCK/winhttp.dll" ] || [ ! -f "$STOCK/BepInEx/core/BepInEx.dll" ]; then
    info "Stock BepInExPack not found — running scripts/fetch-sdk.sh ..."
    bash "$SCRIPT_DIR/fetch-sdk.sh" >/dev/null 2>&1 || fail "fetch-sdk.sh failed; cannot assemble modpack."
fi
[ -f "$STOCK/winhttp.dll" ] || fail "stock BepInExPack still missing after fetch ($STOCK)."

# ── Stage the pack tree ─────────────────────────────────────────────────────
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
TREE="$STAGE/$PACK_NAME"
mkdir -p "$TREE"

# 1) Copy the stock pack, then drop the three non-functional helper files so the
#    artifact matches the proven v0.1.0 release layout exactly.
cp -R "$STOCK/." "$TREE/"
rm -f "$TREE/changelog.txt" "$TREE/start_game_bepinex.sh" "$TREE/start_server_bepinex.sh"

# 2) Overlay our plugin folder: freshly-built DLL + item icons from assets/.
PLUGDIR="$TREE/BepInEx/plugins/$PLUGIN_DIRNAME"
mkdir -p "$PLUGDIR"
cp "$DLL" "$PLUGDIR/SBPR.Trailborne.dll"

ICON_SRC="$REPO_ROOT/assets/icons/items"
shopt -s nullglob
icons=("$ICON_SRC"/*.png)
shopt -u nullglob
[ ${#icons[@]} -gt 0 ] || fail "no item icons found in $ICON_SRC"
for png in "${icons[@]}"; do cp "$png" "$PLUGDIR/"; done
ok "Plugin folder: 1 DLL + ${#icons[@]} icon(s)"

# 2b) Overlay world-mesh TEXTURES (held-item albedos etc.) the plugin loads at runtime
#     via Assets.LoadPngAsTexture. These are NOT inventory icons (the equipable-icon
#     transparency test deliberately does NOT scan them) — e.g. the Local Map's painted
#     deer-hide held sheet. Shipped flat into the plugin folder alongside the icons, since
#     Plugin.PluginFolder is the single load root for both. The *_preview*.png review-only
#     renders are skipped so they never bloat the shipped pack.
TEX_SRC="$REPO_ROOT/assets/textures"
if [ -d "$TEX_SRC" ]; then
    shopt -s nullglob
    texes=("$TEX_SRC"/*.png)
    shopt -u nullglob
    texcount=0
    for png in "${texes[@]}"; do
        case "$(basename "$png")" in
            *_preview*) continue ;;   # review-only nearest-neighbor previews — not shipped
        esac
        cp "$png" "$PLUGDIR/"
        texcount=$((texcount + 1))
    done
    [ "$texcount" -gt 0 ] && ok "Plugin folder: + ${texcount} world-mesh texture(s)"
fi

# 2c) Overlay custom AssetBundles the plugin loads at runtime via AssetBundle.LoadFromFile
#     (Plugin.PluginFolder root, same as icons/textures). Currently: sbpr_tradertent.unity3d
#     — the Bear Hide Tent's placeholder mesh (vanilla TraderTent repacked to Unity 6;
#     built by scripts/build_bear_hide_tent_bundle.py). SBPR's first custom bundle.
BUNDLE_SRC="$REPO_ROOT/assets/bundles"
if [ -d "$BUNDLE_SRC" ]; then
    shopt -s nullglob
    bundles=("$BUNDLE_SRC"/*.unity3d)
    shopt -u nullglob
    bundlecount=0
    for b in "${bundles[@]}"; do
        cp "$b" "$PLUGDIR/"
        bundlecount=$((bundlecount + 1))
    done
    [ "$bundlecount" -gt 0 ] && ok "Plugin folder: + ${bundlecount} asset bundle(s)"
fi

# 3) Overlay bundled third-party mods we ship for playtesters.
#    ServerDevcommands (JereKuusela, Unlicense/public-domain) turns on the dev
#    console cheat commands (spawn/god/fly) for admin playtesters — on a DEDICATED
#    server, vanilla F5+admin only enables the *admin* verbs; the cheat verbs stay
#    blocked until ServerDevcommands runs on the admin's CLIENT. Bundling it here is
#    what actually lights up devcommands for everyone via the one-liner installer.
SDC_DIR="$REPO_ROOT/assets/bundled-mods/ServerDevcommands-1.106.0"
[ -f "$SDC_DIR/ServerDevcommands.dll" ] || fail "bundled ServerDevcommands.dll missing: $SDC_DIR"
PLUGDIR_SDC="$TREE/BepInEx/plugins/ServerDevcommands"
mkdir -p "$PLUGDIR_SDC"
cp "$SDC_DIR/ServerDevcommands.dll" "$PLUGDIR_SDC/"
cp "$SDC_DIR/manifest.json"        "$PLUGDIR_SDC/"
cp "$SDC_DIR/LICENSE"              "$PLUGDIR_SDC/" 2>/dev/null || true
ok "ServerDevcommands 1.106.0 bundled"

# ── Zip it (DETERMINISTIC: fixed mtimes, sorted entries, no extra attrs) ─────
# Normalising timestamps makes the zip's SHA256 reproducible for identical
# content — so the same commit always packs to the same hash. That is what lets
# the release pipeline publish an immutable, tag-versioned asset and bump the
# installer's pin in a separate PR with NO broken window (the old asset keeps
# serving the old installer until the pin PR merges).
find "$TREE" -exec touch -h -d "2020-01-01T00:00:00Z" {} + 2>/dev/null \
    || find "$TREE" -exec touch -d "2020-01-01T00:00:00Z" {} +

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
ZIP="$OUT/${PACK_NAME}-v${VERSION}.zip"
rm -f "$ZIP"
( cd "$STAGE" && TZ=UTC find "$PACK_NAME" -type f | LC_ALL=C sort | TZ=UTC zip -q -X -9 "$ZIP" -@ )
[ -f "$ZIP" ] || fail "zip not produced."

# ── Checksum sidecar (same format the installer + release expect) ───────────
SHA="$(cd "$OUT" && sha256sum "$(basename "$ZIP")" | cut -d' ' -f1)"
printf '%s  %s\n' "$SHA" "$(basename "$ZIP")" > "$ZIP.sha256"

ok "Wrote $ZIP"
ok "SHA256 $SHA"

# Emit machine-readable lines for CI to capture (GITHUB_OUTPUT-friendly).
echo "MODPACK_ZIP=$ZIP"
echo "MODPACK_SHA256=$SHA"
echo "MODPACK_VERSION=$VERSION"
