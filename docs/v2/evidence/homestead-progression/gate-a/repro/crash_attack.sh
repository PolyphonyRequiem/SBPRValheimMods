#!/bin/bash
# Independent Gate-A REAL process-death attack (T003).
# For each durable boundary 1..4: a child process fsyncs that boundary then SIGKILLs its own PID
# (genuine OS process death, no managed unwind). A fresh process then recovers from the journal only.
set -u
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
HARNESS_DLL="${GATE_A_HARNESS_DLL:-$SCRIPT_DIR/bin/Release/net8.0/GateAHarness.dll}"
HARNESS=(dotnet "$HARNESS_DLL")
TMP=$(mktemp -d)
echo "journal dir: $TMP"
declare -A BNAME=([1]=IntentJournaled [2]=StoneApplied [3]=CharacterApplied [4]=Committed)
FAIL=0
for B in 1 2 3 4; do
  J="$TMP/b$B.journal"
  echo "=== Boundary $B (${BNAME[$B]}) ==="
  # Child crashes via SIGKILL right after boundary B is fsync'd.
  OUT=$("${HARNESS[@]}" child-crash "$J" "op-crash" "$B" 2>&1)
  RC=$?
  echo "child exit code: $RC (137 == 128+SIGKILL expected)"
  echo "child stdout: $OUT"
  if [ $RC -ne 137 ]; then echo "  !! child did NOT die by SIGKILL (rc=$RC)"; FAIL=1; fi
  # Fresh process recovers.
  REC=$("${HARNESS[@]}" recover "$J" "op-crash" 2>&1)
  echo "$REC"
  MIR=$(echo "$REC" | grep '^MIRRORED=' | cut -d= -f2)
  PER=$(echo "$REC" | grep '^PERSONAL=' | cut -d= -f2)
  CUM=$(echo "$REC" | grep '^CUMULATIVE=' | cut -d= -f2)
  # After recovery the three AP deltas must converge to exactly 1/1/1 (or, if crash was before the
  # character write became terminal, the whole op must be QUARANTINE with NO partial mutation applied).
  PRE=$(echo "$REC" | grep '^PRE_STATUS=' | cut -d= -f2)
  echo "  => recovered mirrored=$MIR personal=$PER cumulative=$CUM (pre-status=$PRE)"
  echo
done
echo "TMP retained at $TMP"
[ $FAIL -eq 0 ] && echo "ALL CHILDREN DIED BY REAL SIGKILL" || echo "SOME CHILDREN DID NOT DIE BY SIGKILL"
