# index — docs/investigations

Machine-readable manifest of investigation / root-cause write-ups.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What investigations are, when/how to write them |
| 2026-06-06-release-workflow-steamcmd-failure.md | historical | Release workflow fails at SteamCMD; releases were published by hand. Root cause found, fix applied 2026-06-07. |
| 2026-06-07-terrain-placement-ripple-magnitude-spike.md | historical | Placement ground-ripple is a fixed-radius CircleProjector; scale it with terrain-op magnitude (Request 1). **Implemented 2026-06-07** → `PlacementMarkerRadiusPatch`. |
| 2026-06-09-replant-grass-fights-path-terrainop-vs-terrainmodifier.md | current | Spade Replant-Grass "fights" the path: we clone the LEGACY `replant` (`TerrainModifier`, persistent networked piece w/ precedence battle) instead of the Cultivator's MODERN `replant_v2` (`TerrainOp`, compiler-applied + self-destruct). Donor-swap fix spec'd + named acceptance tests; **gated on PR review + Daniel in-game verify** (t_d48ac283). |
| 2026-06-10-cairn-banner-missing-alignToWindDirection.md | current | Cairn banner waggles in place / never streams downwind. Reframed (attempt #6): the `UnityEngine.Cloth` solver is likely INERT (gravity can't drop a one-end-anchored sheet), so prove the solver steps before chasing the secondary `m_alignToWindDirection` rotation gap. |
| 2026-06-13-cairn-no-open-air-comfort-near-fire-gate.md | current | Cairns grant no comfort in the open. Root cause: vanilla's SECOND gate — the `Resting` grant in `Player.UpdateEnvStatusEffects` ends in `&& flag` (near-fire); a heat-free cairn never sets it. Our comfort-LEVEL patch defeats only the first (shelter) gate. Fix = postfix that directly maintains the `Rested` buff (heat-free, reads vanilla's post-state exclusions), spec'd + 6 named ATs; **impl routed to engineer-systems, spec+code one PR** (t_1cdea346). |

## Conventions

- Filename `YYYY-MM-DD-kebab-summary.md`; newest at the bottom.
- `status: historical` once the dig is closed; `current` while active.
