---
status: current
---

# T003 re-verify — RACE attack transcript (PASS)

```
journal: /tmp/tmp.2Mtu2wFwng/race.journal
=== Client A commits expecting stoneRev 0 ===
op-A OUTCOME=Applied CODE=Applied STONE_REV=1
=== Client B (SEPARATE fresh process) also commits expecting stoneRev 0 ===
op-B OUTCOME=Rejected CODE=StaleStoneRevision STONE_REV=1

=== Client B2 (fresh process) refetches stoneRev 1 and commits ===
op-B2 OUTCOME=Applied CODE=Applied STONE_REV=2
journal frames:
-rw-rw-r-- 1 polyphonyrequiem polyphonyrequiem 780 Jul 14 16:02 /tmp/tmp.2Mtu2wFwng/race.journal
TMP=/tmp/tmp.2Mtu2wFwng
```