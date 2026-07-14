---
status: current
---

# T003 re-verify — RACE attack transcript (PASS)

```
journal: [temporary-directory]/race.journal
=== Client A commits expecting stoneRev 0 ===
op-A OUTCOME=Applied CODE=Applied STONE_REV=1
=== Client B (SEPARATE fresh process) also commits expecting stoneRev 0 ===
op-B OUTCOME=Rejected CODE=StaleStoneRevision STONE_REV=1

=== Client B2 (fresh process) refetches stoneRev 1 and commits ===
op-B2 OUTCOME=Applied CODE=Applied STONE_REV=2
journal frames:
-rw-rw-r-- 780 bytes race.journal
TMP=[temporary-directory]
```