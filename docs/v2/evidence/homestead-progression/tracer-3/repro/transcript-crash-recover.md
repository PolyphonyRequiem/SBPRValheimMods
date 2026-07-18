---
status: current
---

# Transcript — Tracer 3 real death + recovery

Captured against authoritative main `6d0adc2af1693bdc33559c2773e0252177664574`
(worktree `verify/t011-tree-commitment`, base identical to `origin/main`).
Harness link-compiles the shipped T010 slice; built clean (0 warnings / 0 errors).

```
JOURNAL=/tmp/tracer3-GOMKBV.journal
=== CHILD commit-kill (real SIGKILL, no unwind) ===
CHILD_COMMITTED OUTCOME=Applied CODE=Applied REV=6
crash_recover.sh: line 15: 124421 Killed                  dotnet "$DLL" commit-kill "$J" op-crash
  CHILD_EXIT=137  (137 == SIGKILL)
=== journal durable on disk ===
-rw-rw-r-- 1 polyphonyrequiem polyphonyrequiem 1896 /tmp/tracer3-GOMKBV.journal
=== RECOVER (fresh process, journal-only rehydrate) ===
BOOT_COMMITTED_COUNT=1
BOOT_COMMITTED_KEY=Cooking
BOOT_STONE_REV=6
REPLAY_OUTCOME=Replayed
REPLAY_CODE=Applied
POST_COMMITTED_COUNT=1
POST_STONE_REV=6
REPLAY_RECEIPT=40d5c8a56246f66c
  RECOVER_EXIT=0
```

## Reading

- The child process `Applied` the Cooking commit (revision 5 → 6) and then died by
  `SIGKILL` (exit **137**) with no managed unwind — the journal was already fsync'd inside
  `FacetCommandHandler.Handle` before the kill.
- A **fresh process** reconstructed the projection from the journal alone:
  exactly **one** Committed Tree (`Cooking`), Stone revision **6** — advanced exactly once,
  not zero (lost) and not twice (double-applied).
- Resubmitting the same operation id returned **Replayed / Applied** with an unchanged
  committed count (1) and unchanged revision (6): exactly-once semantics survive real death.
