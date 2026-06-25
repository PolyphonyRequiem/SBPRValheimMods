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

## Accepted (locked, awaiting impl-spec)

Design decisions Daniel has **locked**. The *why* and the locked parameters live
here; the buildable *how* graduates to a version-scoped impl-spec when built.

- **[`twisted-portal-food-charge.md`](twisted-portal-food-charge.md)** — the v3
  Twisted Portal cost model. **No key trinket** — teleport range is gated by the
  **food in your belly**: Portal Energy = remaining-food-minutes × a stat-derived
  tier, summed across food slots; a jump spends PE as food-time, so distance both
  costs provisioning AND lands you depleted. Tier is computed from total stats
  (`round(clamp(total/30, 1, 5) × 2)/2`, snapped to 0.5 rungs) — making the
  **stat fallback the primary rule**, so vanilla and modded foods slot in with
  zero hand-authoring. Feasts run on a separate normalized ~28 m range clock
  (their 50 m buff timer untouched) so they land **slightly under** personal
  crafted meals for travel. **Bukeperries** (vanilla Pukeberries) are a burnable
  **emergency reserve** — spent *only* when belly food can't cover a jump, at
  30 m/berry (10 = the 300 m portal ceiling); a berry-burning jump arrives
  food-empty **and** *Feeling Sick*, advertised only by a Greydwarf-portal-magick
  lore breadcrumb (no UI). **Supersedes** the trinket-key/durability charge
  economy in `nomap.md` §7 (resolves the impl-spec's open "charge economy"
  decision); the old Bukeperry purge-accelerator is **re-homed** as the reserve
  tank, not dropped. Locked 2026-06-24 as a tuning baseline — architecture fixed,
  numbers (the `/30` divisor, `[1,5]` clamp, 28 m feast cap, eitr weighting,
  30 m/berry, the `SE_Puke` debuff) are live playtest knobs.

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
  structural **twin** of the Sunstone handoff, mirror-imaged on north: Daniel's
  2026-06-20 idea that when the Iron Compass is worn AND an SBPR map surface
  (carry-disc or full-map) is showing, the surface gains a **compass-gated north
  indicator** — an **iron bezel + N + ticks** that reverts to bronze when the
  compass comes off — else it falls back to the current HUD needle. Consciously
  **supersedes** the iron-compass impl-spec's "never a north arrow on any map"
  thesis ("never *ungated*; the compass-gated ring is the sanctioned exception")
  AND the "no north-up alternative" disc-rotation lock (an opt-in auto-north-orient
  config, default OFF). Re-grounded against what SHIPPED: the N marker is a
  **player-chevron sibling** on the rotating container, NOT routed through the
  already-merged `IThreatMarkerProvider` (wrong marker kind — world-positioned vs
  screen-bearing), so no shared seam is needed. Resolved with the twin by ONE rule:
  *the surface shows north IFF the compass is worn.* Daniel's 4 follow-up answers
  folded in; one knob (nomap-OFF vanilla minimap) open. CLEAN-SIDE, SpecCheck +0.

- **[`sunstone-lens-aura.md`](sunstone-lens-aura.md)** — Daniel's 2026-06-21
  `/bug` idea: while the Sunstone Lens is worn, a faint golden aura **pulses**
  "around the outer rim of the minimap (or as the art for the no-minimap ring)."
  The reframe that makes this small: the gold ring **already exists** as the lens'
  empty-state affordance (`_emptyRing`, colour `CSolarRing`, sprite `RingSprite()`),
  so the work is **extend + animate** — make it pulse, and **re-home** it onto the
  minimap rim when a minimap owns detection. The central insight: Daniel's "or as
  the…" maps exactly onto the **already-shipped** Sunstone→minimap handoff — the
  handoff hides the whole ring when a minimap takes over, so the aura is just the
  empty-ring re-homed onto whichever surface the handoff already targets (one
  concept, no new gate or art). Corrects three grounding slips in the bug card and
  flags one real conflict — on the carry-disc the bezel colour channel is **already
  owned** by the Iron Compass tint, so a lens aura there needs its own element.
  Two knobs are settled by grounding; three (pulse style/rate/depth) are eyeball
  calls Daniel locks via an interactive HTML mock (`~/sunstone-aura-mock/`). Clean,
  no new assets, SpecCheck +0.

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
