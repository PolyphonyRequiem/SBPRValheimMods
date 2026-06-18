---
title: Playtest
status: living
last_updated: 2026-06-18
---

# Playtest

The playtest tracking system: what needs in-game testing, and the numbered
**Playtest #N** testers guides produced from it.

## The two moving parts

- **`playtest-ledger.md`** — the living source of truth for *what needs testing*.
  A `PENDING` section accumulates test items as work merges (fed by both git
  ground-truth and human judgment), and an `ARCHIVE` section records what shipped
  under each past playtest. Frontmatter carries the `playtest_counter` and
  `last_playtest_tag`.
- **`playtest-<N>-testers-guide.md`** — a generated, numbered testers guide for a
  given playtest. Produced by `scripts/gen-playtest-guide.py` from the ledger
  PENDING block + the actual code changes since the last `-playtest` tag. **Do not
  hand-edit a guide** — regenerate it.

## Playtest #N vs the build tags

The **Playtest #N** counter is the *human-facing* testing series (Playtest #1, #2,
…). It is deliberately **distinct** from the `vX.Y.Z-playtest` git tags, which are
*build* markers. One build tag can carry several playtest cycles, or a playtest can
span builds; the counter tracks the testing cadence, not the version.

## How it stays reliable

1. Items are added **as work merges**, not from memory — the generator cross-checks
   every `src/**/*.cs` change since the last tag against the ledger and flags any
   with no test item, so nothing ships untested silently.
2. The `sbpr-playtest-planner` cron watches for a new `-playtest` tag. When one
   lands it archives the shipped playtest, bumps the counter, re-seeds PENDING from
   git, regenerates the next guide, and opens the next **PLAYTEST #N** planning card
   on the kanban board.
3. **logs-green ≠ playable** — a guide is a checklist; Daniel's in-game run is the
   acceptance.

## Regenerating a guide

```bash
python3 scripts/gen-playtest-guide.py --ref main            # dry run (prints)
python3 scripts/gen-playtest-guide.py --ref main --write    # writes the guide file
```
