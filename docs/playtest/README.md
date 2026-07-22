---
title: Playtest
status: living
last_updated: 2026-07-22
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
   every scoped change since the last tag against the ledger and flags any with no
   test item, so nothing ships untested silently.
2. Preparing a playtest is **one explicit human command**, not a polling cron. Run
   `scripts/prepare-playtest.py` (dry-run by default) to get an exact
   release-candidate identity + a fail-closed verdict; it mutates nothing. The old
   `sbpr-playtest-planner` / `sbpr-playtest-ready-watch` cron jobs were **removed**
   (50 checks rolled the ledger zero times) — they do not exist and nothing rolls
   the ledger automatically. Archiving/counter-bump is a deliberate human edit made
   only after a tag is actually cut.
3. **logs-green ≠ playable** — a guide is a checklist; Daniel's in-game run is the
   acceptance.

## The prepare → tag → release path

```bash
# 1. Prepare (dry-run, safe, mutates nothing). READY or BLOCKED verdict.
python3 scripts/prepare-playtest.py --manifest trailborne --ref main
#    optionally emit a candidate guide for review (ledger untouched):
python3 scripts/prepare-playtest.py --ref main --write-guide /tmp/candidate-guide.md
# 2. If READY, a human APPROVES by cutting the proposed tag (the ONE gated action):
#      git tag vX.Y.Z-playtest <commit> && git push origin vX.Y.Z-playtest
# 3. .github/workflows/release.yml then builds → packages → publishes the release,
#    and opens the installer URL+SHA pin PR (publish-then-pin, no broken window).
#    A scoped read-only preflight in that workflow refuses to publish a -playtest
#    tag whose manifest/guide is stale or incomplete.
# 4. Merge the installer-pin PR → the public one-liner serves the new build.
# 5. Playtesters install, play, file feedback as kanban cards.
```

## Regenerating a guide

```bash
python3 scripts/gen-playtest-guide.py --ref main            # dry run (prints)
python3 scripts/gen-playtest-guide.py --ref main --write    # writes the guide file
```
