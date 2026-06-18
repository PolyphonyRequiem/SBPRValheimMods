---
title: index — docs/playtest
status: living
last_updated: 2026-06-18
---

# index — docs/playtest

Machine-readable manifest of the playtest tracking system.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What the playtest system is — ledger + numbered testers guides, Playtest #N vs build tags |
| playtest-ledger.md | living | Living source of truth for what needs in-game testing: PENDING (accrues as work merges) + ARCHIVE (shipped playtests). Carries `playtest_counter` + `last_playtest_tag` in frontmatter. |
| playtest-1-testers-guide.md | current | Generated Playtest #1 testers guide — install + acceptance checklist for everything merged since v0.2.25-playtest, with git ground-truth cross-check. |

## Conventions

- The ledger is hand-editable (workers + orchestrator append to PENDING). The
  `playtest-<N>-testers-guide.md` files are **generated** by
  `scripts/gen-playtest-guide.py` — regenerate, don't hand-edit.
- Guide filename: `playtest-<N>-testers-guide.md` where `<N>` is the human-facing
  Playtest number (distinct from the `vX.Y.Z-playtest` build tag).
- A shipped playtest's guide keeps `status: current` while it's the active test
  target; flip to `status: historical` once the next playtest supersedes it.
- The `sbpr-playtest-planner` cron maintains the roll (archive → bump → re-seed →
  regenerate → open next planning card) when a new `-playtest` tag lands.
