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

## Architecture

How the codebase is shaped — the structural intent behind the slices and the
shared core.

- **[`architecture-review.md`](architecture-review.md)** — PROPOSED (2026-06-17).
  The full architectural review: the four domain models the mod needs (the `*Tag`
  ZDO-component family, the recipe single-source-of-truth that retires the
  Registrar↔SpecCheck drift, the cartography provider/surface, the charged-accessory
  trinket), the engine-free domain-core seam that makes them testable, and a
  reversible phased refactor (P0–P6) each building 0/0 under a Daniel gate. Shared
  input for the CLEANUP batch. (The vertical-slice `Features/` structure it builds on
  lives in [`../v1/architecture/feature-slice-plan.md`](../v1/architecture/feature-slice-plan.md).)

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

- **[`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)** —
  Daniel's 2026-06-20 idea: when a local-map minimap **disc** is available, move
  the Sunstone Lens' hostile detection onto the disc instead of the
  camera-relative trophy-ring HUD. The trigger (`IsMinimapBound`) is true exactly
  in *nomap-ON + a local map bound* — the very config the old "no map surface to
  pin to" rationale was written for. Grounds the lowest-coupling seam (a
  Cartography transient-threat-marker provider mirroring `WorldPins`), the shared
  `Character → blip` projection both surfaces consume, and the two invariants the
  move must not break (the #209 dead-Update-pump fix; the camera-relative,
  never-north AT-LENS-RING-CAMREL thesis). Converts the load-bearing
  replace-vs-supplement question into a live `MinimapHandoffMode` Config enum so
  Daniel converges it in-game. Three open knobs for his gate.

- **[`iron-compass-minimap-ring.md`](iron-compass-minimap-ring.md)** — the
  **structural twin** of the Sunstone handoff above, designed jointly: when the
  Iron Compass is worn **and** a minimap disc is bound, draw a **compass-gated north
  ring on the disc**, else fall back to the current HUD needle. 🔴 Consciously
  **supersedes** the Iron Compass impl-spec's "non-negotiable: the compass NEVER
  adds a north arrow to any map" thesis — re-wording absolute *never* to *"never
  ungated; the compass-gated disc ring is the sanctioned exception"* (north stays an
  earned, compass-only payoff; the disc is north-blind for the compass-less player).
  Designs the **one shared `IDiscOverlayProvider` disc-superimpose seam** both twins
  consume — carrying world-positioned blips (Sunstone) and a single cardinal mark
  (Compass) — resolved by one rule: *the disc shows north iff the compass is worn.*
  Geometry is locked (north rides the rotating container, zero yaw math). Six open
  knobs; **Q1 is whether Daniel ratifies the thesis supersession.**

- **[`forge-masters-trinket.md`](forge-masters-trinket.md)** — a **standalone-mod**
  Trinket whose power, fired when the vanilla adrenaline bar caps (~80), repairs
  **+5 durability on equipped gear** instead of a combat burst. Decomp-grounded:
  adrenaline is a LIVE vanilla trinket mechanic (`m_fullAdrenalineSE` cap-fire hook),
  NOT a custom meter — the build is ~95% vanilla (one StatusEffect + one item).
  Carries a repo/project structure proposal (separate repo vs in-monorepo standalone
  DLL — recommends the latter) and AT-FORGE-* tests, for Daniel to ratify before
  scaffold+impl.

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
