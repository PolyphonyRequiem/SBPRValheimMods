#!/usr/bin/env bash
# Tracer 3 (T011) — real out-of-process death + journal-only recovery.
# A child commits one Tree through the shipped FacetCommandHandler and SIGKILLs itself;
# a fresh process then reconstructs the commitment from the fsync'd journal only and
# proves exactly-once persistence (same-op resubmission Replays, no double-commit).
set -u
cd "$(dirname "$0")"

dotnet build Tracer3Harness.csproj -c Release >/dev/null || { echo "BUILD FAILED"; exit 1; }
DLL=bin/Release/net8.0/Tracer3Harness.dll
J=$(mktemp -u /tmp/tracer3-XXXXXX.journal)
echo "JOURNAL=$J"

echo "=== CHILD commit-kill (real SIGKILL, no unwind) ==="
dotnet "$DLL" commit-kill "$J" op-crash
echo "  CHILD_EXIT=$?  (137 == SIGKILL)"

echo "=== journal durable on disk ==="
ls -l "$J"

echo "=== RECOVER (fresh process, journal-only rehydrate) ==="
dotnet "$DLL" recover "$J" op-crash
echo "  RECOVER_EXIT=$?"

rm -f "$J"
