---
status: current
---

# T003 re-verify — CRASH/recovery transcript (PASS)

```
journal dir: /tmp/tmp.0YeKSmDjKK
=== Boundary 1 (IntentJournaled) ===
child exit code: 137 (137 == 128+SIGKILL expected)
child stdout: CHILD_FSYNCED_BOUNDARY=1
PRE_STATUS=Quarantine
PRE_MIRRORED=0 PRE_PERSONAL=0 PRE_CUM=0
OUTCOME=Applied
MIRRORED=1
PERSONAL=1
CUMULATIVE=1
RECEIPT=99e8c99e2fc716ee
STONE_REV=1
  => recovered mirrored=1 personal=1 cumulative=1 (pre-status=Quarantine)

=== Boundary 2 (StoneApplied) ===
child exit code: 137 (137 == 128+SIGKILL expected)
child stdout: CHILD_FSYNCED_BOUNDARY=2
PRE_STATUS=Quarantine
PRE_MIRRORED=1 PRE_PERSONAL=0 PRE_CUM=0
OUTCOME=Applied
MIRRORED=1
PERSONAL=1
CUMULATIVE=1
RECEIPT=99e8c99e2fc716ee
STONE_REV=1
  => recovered mirrored=1 personal=1 cumulative=1 (pre-status=Quarantine)

=== Boundary 3 (CharacterApplied) ===
child exit code: 137 (137 == 128+SIGKILL expected)
child stdout: CHILD_FSYNCED_BOUNDARY=3
PRE_STATUS=Quarantine
PRE_MIRRORED=1 PRE_PERSONAL=1 PRE_CUM=1
OUTCOME=Applied
MIRRORED=1
PERSONAL=1
CUMULATIVE=1
RECEIPT=99e8c99e2fc716ee
STONE_REV=1
  => recovered mirrored=1 personal=1 cumulative=1 (pre-status=Quarantine)

=== Boundary 4 (Committed) ===
child exit code: 137 (137 == 128+SIGKILL expected)
child stdout: CHILD_FSYNCED_BOUNDARY=4
PRE_STATUS=Recoverable
PRE_MIRRORED=1 PRE_PERSONAL=1 PRE_CUM=1
OUTCOME=Replayed
MIRRORED=1
PERSONAL=1
CUMULATIVE=1
RECEIPT=99e8c99e2fc716ee
STONE_REV=1
  => recovered mirrored=1 personal=1 cumulative=1 (pre-status=Recoverable)

TMP retained at /tmp/tmp.0YeKSmDjKK
ALL CHILDREN DIED BY REAL SIGKILL
```