# index — docs/decisions

Machine-readable manifest of architecture decision records.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What ADRs are, when/how to write them |
| TEMPLATE.md | template | Copy this to start a new ADR |
| 0001-clean-room-no-jotunn.md | accepted | No Jotunn/mod-loader code; clean-room vanilla-only |
| 0002-spec-first-drift-checked.md | accepted | Spec + code change together; SpecCheck enforces at boot |
| 0003-explorers-bench-own-station.md | accepted | Explorer's Bench is its own CraftingStation, not the Workbench |
| 0004-deterministic-publish-then-pr-releases.md | accepted | Deterministic packaging + publish-then-PR release ordering |

## Conventions

- Records are append-only. Supersede with a new ADR; never edit an accepted
  decision's substance.
- Numbered `NNNN-kebab-title.md`. Next number = highest here + 1.
