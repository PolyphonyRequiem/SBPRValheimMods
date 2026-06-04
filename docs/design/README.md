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

## Working state

- **[`PARKED-2026-06-03.md`](PARKED-2026-06-03.md)** — resume point from the
  2026-06-03 session. Where work paused and what the next agenda is.

See [`index.md`](index.md) for the machine-readable manifest.
