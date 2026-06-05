---
spec_name: trailborne-v1
status: historical
created_at: 2026-06-03
source: user
project: SBPR Trailborne (first public SBPR Thunderstore release)
namespace: SBPR.Trailborne
bepinex_guid: com.sbpr.trailborne
thunderstore_slug: SBPR-Trailborne
tagline: Exploration and map progression
related_facts: [#92 valheim-regions, #93 niflheim-parked, #97 sleep-guard, #102 timestamp-hook, #105 sdd-skill, #110 kanban-swarm, #111 trailborne-naming-lock, #112 trailborne-asset-doctrine]
---

# Raw idea (verbatim from Daniel Green, 2026-06-03 morning)

> "I've been thinking about it, and I think we should still ship discrete mods
> to the public, not JUST a bespoke mod bundle for our server. I think the
> Exploration overhaul is a big one for us."
>
> [On naming, after Trailborne won the round:]
> "Trailborne - Exploration and map progression"
>
> [On assets:]
> "Inert guardian stones is a pre release polish. We can defer that until late
> minute" — confirming v1 ships zero-assetbundle, Tier 0/1/2 reuse only.

# Doctrine carried in from prior context

1. **Standalone-by-default.** Trailborne v1 ships to public Thunderstore as a
   complete, self-contained mod. Solo install (no other SBPR mods, no Pact, no
   Niflheim) must be a *complete good experience*, not a tease.
2. **"Maps are a luxury, not a right."** Design north star. Each biome tier
   unlocks a navigation tier; map-making is mid-game; on-demand map viewing is
   Mistlands-tier (far future, NOT v1).
3. **Zero Unity assetbundles in v1.** Tier 0 (vanilla prefab) / Tier 1
   (vanilla mesh + custom material) / Tier 2 (procedural assembly of vanilla
   parts at runtime) ONLY. Item/build/map icons ship as runtime-loaded PNGs.
4. **Inert Guardian Stones = pre-release polish gate, not a v1 blocker.**
   Added late if at all, behind Trailborne v1's primary mechanics work.
5. **Corpus-first.** Any claim about vanilla Valheim materials, biomes,
   creatures, mechanics, or pieces MUST be grep-verified against
   `~/valheim/sbpr-corpus/wiki/fandom/` before being written into the spec.
   Decompiled code at `~/valheim/worldgen-spike/decomp/` is source of truth for
   internal behavior. Don't trust pre-Bog-Witch memory or web search.
6. **SDD pipeline.** This spec is being driven through the
   `spec-driven-development` skill family (newly adopted 2026-06-03 from
   brobertsaz/claude-os Agent-OS pattern). After spec lock + tasks
   decomposition, implementation will be dispatched via Kanban Swarm
   (`hermes kanban swarm`) for parallel execution.

# Concept seeds discussed (NOT yet validated, NOT yet scoped)

These are Daniel's pre-spec brainstorm concepts from 2026-06-02 evening, in
biome-tier order. The spec-shaper stage decides which land in v1 vs which slip
to v2+.

- **Meadows tier:**
  - Explorer's Bench (crafting station that gates the rest)
  - Cairns (procedural stack-of-rocks waypoint, comfort-emitting like a Rested
    bench, indestructible-ish, the v1 centerpiece)
  - Pigments (item — colored dyes for painted signs)
  - Painted Signs (variant of vanilla sign, color-coded for trail discipline)
  - Trailblazer's Spade (single tool item — purpose to be defined)

- **Black Forest tier:**
  - Local Maps + Map Stations (paired — Map Station is anchor/transfer/retention
    substrate; Local Map without Station is blank leather)
  - Real Tents (collapsible shelter for overnight wilderness rest)

- **Swamps tier:**
  - Iron Compass (handheld nav aid — Swamps because iron is Swamps metal)

- **Mountains tier:**
  - Seer's Stone (the sole headline at Mountain tier — exact mechanic TBD)

- **Plains tier:**
  - Plains sailing angle (open question, deferred)

- **Mistlands tier:**
  - On-demand map viewing (the "luxury" capstone — exact mechanic TBD)

**v1 candidates from this list (Daniel-leaning):** Explorer's Bench, Cairns,
Pigments, Painted Signs, Trailblazer's Spade. Plus possibly Path Lamps as a
sub-piece (TBD).

# Things we know are NOT in v1

- Local Maps + Map Stations → v2
- Real Tents → v2
- Iron Compass → v3 (Swamps tier unlock)
- Seer's Stone → v4 (Mountains tier)
- Plains sailing → v5
- On-demand map viewing → v6 (Mistlands capstone)
- Inert Guardian Stones → pre-release polish gate only, never a v1 mechanic

# Things we know are NOT in any version of Trailborne (separate mods)

- Guardian Stones (the active version, not the inert v1-polish stones) →
  separate mod family (`SBPR.Wardens` or similar), gated on
  `valheim-regions` macro-boundary work
- Anything that changes biome generation, creature behavior, or combat —
  that's other SBPR mod surface, not Trailborne's

# Open questions surfaced at initialization (for spec-shaper to resolve)

- Which v1 candidates actually ship vs slip?
- What does the Trailblazer's Spade mechanic actually DO?
- Is Path Lamps in v1?
- Cairn comfort radius, comfort tier, decay rules?
- Painted Sign color count / Pigment recipe inputs?
- Explorer's Bench tier (workbench-tier or its own tier?) and recipe?
- PNG concepting source (FLUX local vs hand-sourced)?
- Map nerf scope — does v1 touch the vanilla Cartography Table at all, or
  only ADD pieces?
- Server-gated or always-on for solo install?
- BepInEx config knobs (and at what granularity)?

These are exactly what spec-shaper will work through with Daniel via
1-3-questions-per-round discipline. Spec-shaper does NOT proceed to write
spec.md until all are resolved or explicitly deferred.

# Hand-off

Ready for stage 2 (spec-shaper). spec_path = `specs/2026-06-03-trailborne-v1/`
in `~/repos/SBPRValheimMods/` on branch `spec/2026-06-03-trailborne-v1`.
