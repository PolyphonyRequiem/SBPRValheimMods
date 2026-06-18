---
title: index — docs/v3/research
status: living
last_updated: 2026-06-18
---

# index — docs/v3/research

Machine-readable manifest of v3 research / QA write-ups.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What v3 research docs are — evidence/QA trail vs planning specs |
| QA-sunstone-loot-economy.md | current | PASS/FAIL evidence that the Sunstone dual-source loot economy (PR #183) + provisional-recipe removal (PR #186) are live and correct at the server loot DATA layer, verified on Niflheim against a fresh `main` build. All four AT-SUNSTONE-* PASS at build+data layer; the joined-client last mile is Daniel's in-game check. (t_0aef1243) |

## Conventions

- Filename: kebab-case, descriptive (e.g. `QA-<feature>.md`, `<topic>-analysis.md`).
- `status: current` while the finding is active; `historical` once superseded.
- State the verified-vs-reasoned boundary explicitly — a server data-layer pass is
  not a joined-client playable pass (AGENTS.md "logs green ≠ playable").
