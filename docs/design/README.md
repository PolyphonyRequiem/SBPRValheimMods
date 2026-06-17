# Design

Durable design intent for the SBPR Valheim mods — the *why* behind what gets
built. Unlike the version-scoped specs under `../v0.1.0/`, these documents are
meant to outlive any single release. When a design question comes up months from
now, this is where the reasoning lives.

## Start here

1. **[`trailborne-vision.md`](trailborne-vision.md)** — the north star. What the
   Explorer role is and what success feels like. Read this first.
2. **[`design-pillars.md`](design-pillars.md)** — the load-bearing constraints
   that every feature must respect. Non-negotiable.

## Investigations

Deep dives into specific patch surfaces, done before committing to an approach:

- **[`nomap.md`](nomap.md)** — how Valheim "knows" the map exists, and the full
  idea ⇄ patch-surface cross-reference for the no-map navigation loop.
- **[`pin-sharing.md`](pin-sharing.md)** — the multiplayer pin-sharing surface
  and how shared map pins can work under server gating.

## Proposed features (designed, not yet locked)

Designs awaiting a Daniel decision before they graduate to a version-scoped
impl-spec. Each carries its open questions inline.

- **[`travellers-cache.md`](travellers-cache.md)** — a trailside chest with a
  shared public shelf plus a per-player private drawer. Architecture is grounded
  on the in-tree Surveyor's Table ZDO-blob pattern; five design knobs (tier,
  placement tool, public/private sizes, destroy-warning) are open for Daniel.

## Working state

- **[`PARKED-2026-06-03.md`](PARKED-2026-06-03.md)** — resume point from the
  2026-06-03 session. Where work paused and what the next agenda is.

## Seeds & future-tier brainstorms

Idea-tier captures for tiers not yet in active build — held in a grounded,
promotable shape until Daniel rates them up to their own design docs and cards:

- **[`maritime-exploration-tools.md`](maritime-exploration-tools.md)** — v5 Plains
  sailing tier: lighthouses (the v3 Beacon's promotion), fog buoys, and the wider
  sea-navigation set (Star Glass, route/depth/harbor markers, sextant). Brainstorm
  only; open questions left open.

See [`index.md`](index.md) for the machine-readable manifest of *all* design docs
(including the per-feature design specs not called out above).
