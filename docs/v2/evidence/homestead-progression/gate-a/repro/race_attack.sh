#!/bin/bash
# Two-"client" race across SEPARATE OS processes sharing one journal (T003).
# Each client boots a FRESH server (empty in-memory aggregates) over the shared journal and commits
# a distinct op expecting stone revision 0. This probes whether CAS is sound when the aggregate is
# NOT rehydrated from the journal at process boot — the real multiplayer server-restart condition.
set -u
HARNESS="dotnet /home/polyphonyrequiem/.hermes/kanban/workspaces/t_11ce6067/gatea-harness/bin/Release/net8.0/GateAHarness.dll"
TMP=$(mktemp -d)
J="$TMP/race.journal"
echo "journal: $J"
echo "=== Client A commits expecting stoneRev 0 ==="
$HARNESS race-child "$J" "op-A" 0
echo "=== Client B (SEPARATE fresh process) also commits expecting stoneRev 0 ==="
$HARNESS race-child "$J" "op-B" 0
echo
echo "=== Client B2 (fresh process) refetches stoneRev 1 and commits ==="
$HARNESS race-child "$J" "op-B2" 1
echo "journal frames:"
ls -l "$J"
echo "TMP=$TMP"
