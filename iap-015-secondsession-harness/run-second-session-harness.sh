#!/usr/bin/env bash
# ============================================================================
#  IAP-015 AT-AIP-DEDICATED-SECOND-SESSION-REJECT — exact-binary harness runner.
#
#  Builds and runs the direct-peer harness that REFERENCE-LINKS the COMPILED
#  shipped admission assemblies and asserts the same-account concurrent-session
#  rejection at the real server-authoritative seam (Option B evidence half).
#
#  This is the exact-binary requirement of the AT: the harness links the shipped
#  DLLs (not source) and self-attests their SHA-256 at runtime.
#
#  Usage:
#    run-second-session-harness.sh [ADMISSION_DLL] [TRAILBORNE_CORE_DLL]
#
#  If no DLL paths are given, the script builds the mod from ../src (requires the
#  Valheim SDK env: VALHEIM_MANAGED + BEPINEX_CORE) and links the fresh candidate
#  build. To pin against an operator-staged shipped artifact instead, pass the
#  absolute DLL paths and (optionally) export IAP015_EXPECT_ADMISSION_SHA256 to
#  make the harness enforce the expected admission-binary hash.
# ============================================================================
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"

ADMISSION_DLL="${1:-}"
CORE_DLL="${2:-}"

if [[ -z "$ADMISSION_DLL" ]]; then
  echo "[harness] no DLL paths given — building the candidate mod from src/ …"
  dotnet build "$REPO/src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj" -c Release -v m
  dotnet build "$REPO/src/SBPR.Trailborne.Core/SBPR.Trailborne.Core.csproj" -c Release -v m
  ADMISSION_DLL="$REPO/src/SBPR.Niflheim.HomesteadStones/bin/Release/SBPR.Niflheim.HomesteadStones.dll"
  CORE_DLL="$REPO/src/SBPR.Trailborne.Core/bin/Release/net48/SBPR.Trailborne.Core.dll"
fi

echo "[harness] linking shipped admission binary: $ADMISSION_DLL"
sha256sum "$ADMISSION_DLL" || true

dotnet run --project "$HERE/Iap015SecondSessionHarness.csproj" -c Release \
  -p:AdmissionDll="$ADMISSION_DLL" \
  ${CORE_DLL:+-p:TrailborneCoreDll="$CORE_DLL"}
