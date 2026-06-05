---
title: SBPR Pieces & Craftables Dataset
purpose: Canonical specs for every piece, item, and crafting station across SBPR mods
status: living document — appended per-piece as specs lock
last_updated: 2026-06-03
maintained_by: Starbright + Daniel during spec rounds; doc-PR'd on spec finalization
---

# SBPR Pieces & Craftables Dataset

**Single source of truth** for the full catalog of SBPR-introduced pieces, items, and crafting stations across all SBPR mods.

## Purpose

As SBPR grows beyond Trailborne v1 into multiple mods and mod families (Guardian Stones, future location work, etc.), we need ONE place that lists every entity SBPR has put into Valheim. This dataset:

1. **Prevents naming collisions** across mods (no two SBPR mods registering the same prefab name)
2. **Tracks recipe coherence** — pigment economy, leather-scrap demand, finewood demand, etc. compose across pieces; need visibility into the whole web
3. **Surfaces shared-asset dependencies** — when Mod A and Mod B both reuse a vanilla prefab as a kitbash base, we want to know
4. **Drives ObjectDB registration order** — pieces that depend on items, items that depend on other items
5. **Acts as a wiki seed** — when self-hosted niflheim.wiki goes live, this dataset is its page-generation source

## Format conventions

Each entry has:
- **Display name** (player-facing, en-US locale)
- **Prefab name** (`SBPR_*` — must be globally unique across SBPR namespace)
- **Type** (`Piece` / `Item` / `Status Effect` / `Recipe`)
- **Mod** (which SBPR mod registers it — `Trailborne`, `Pact`, `GuardianStones`, etc.)
- **Biome tier** (Meadows / Black Forest / Swamp / Mountains / Plains / Mistlands / Ashlands / Deep North)
- **Craft station** (vanilla or SBPR station name, or "build-anywhere" for pieces)
- **Recipe** (full ingredient list with quantities)
- **Function** (one-sentence what-it-does)
- **Status** (`SPEC LOCKED`, `IN DESIGN`, `IMPLEMENTED`, `RELEASED`)
- **Source spec** (link to `specs/YYYY-MM-DD-name/planning/requirements.md`)

---

## Trailborne v1

### Pieces

#### Explorer's Bench

| Field | Value |
|---|---|
| Display name | Explorer's Bench |
| Prefab name | `SBPR_ExplorersBench` |
| Type | `Piece` (CraftingStation) |
| Mod | Trailborne |
| Biome tier | Meadows |
| Craft station | Vanilla Workbench (to BUILD the Explorer's Bench itself) |
| Recipe | 10 Wood + 4 Stone + 1 Deer Trophy |
| Function | Crafting hub for all Trailborne items and pieces; gates the entire Trailborne progression. |
| Visual notes | Kitbash vanilla Workbench mesh; **antlers from the Deer Trophy visually integrated INTO the bench art** (not mounted on top — the antlers are part of the bench mesh itself, e.g. carved cups / leg-supports / pen-holders); plus half-rolled hide-map + bone-needle-in-stone-disk per design/nomap.md §1 |
| Patch surface | Pure prefab work — clone `piece_workbench`, add `CraftingStation` component with `m_name = "$sbpr_piece_explorers_bench"`, set **`m_showBasicRecipies = false`** (the Workbench is the only vanilla station shipping this `true`; it surfaces the stationless basic hand-craft recipes — Club, Torch, Stone Axe, Hammer, Hoe — so a raw clone wrongly offers them; bugfix t_30f97042), and **strip the inherited `GuidePoint` component** (the Workbench's Hugin "you built a workbench" tutorial hook, which the clone wrongly inherits — bugfix t_53ab3232) |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` §Explorer's Bench |

#### Cairn (5 tiers)

| Field | Value |
|---|---|
| Display name | Cairn (Tier I / II / III / IV / V) |
| Prefab name | `SBPR_Cairn_T1` through `SBPR_Cairn_T5` |
| Type | `Piece` (Waypoint, comfort-emitting) |
| Mod | Trailborne |
| Biome tier | Meadows |
| Craft station | Explorer's Bench (to ACCESS the build menu; placed in-world via the Trailblazer's Spade) |
| Recipe (initial build, T1) | 3 Stone + 1 Resin + 1 Cairn Marker |
| Recipe (upgrade T1→T2→T3→T4→T5) | 3 Stone + 1 Resin (flat per tier) |
| Recipe (repair) | 3 Stone + 1 Resin (flat) |
| Comfort floor | 3 / 4 / 5 / 6 / 7 (per tier; max() clamp via patch on `SE_Rested.CalculateComfortLevel`) |
| Comfort radius | TBD (proposed: ~10m, matches vanilla Banner) |
| HP states | ≥75% pristine (top ember lit); <75% fizzled (ember out); <25% downgrade tier; 0% collapse |
| Function | Always-on comfort-emitting trail waypoint, color-bound to its Cairn Marker's pigment, mandatory decay. **A cairn is a non-burning stone marker — no light, no heat, no fuel, no fire SFX.** (Comfort floor is applied separately via the `SE_Rested` patch, not via fire.) |
| Visual notes | Rich procedural stone pile (LOCKED 2026-06-05 — see requirements.md §A2.1b). Cloned from vanilla `bonfire` as a STRUCTURAL base only (WearNTear / Piece / ZNetView), donor fire NEUTRALIZED on the client (Fireplace destroyed; Light/LightFlicker/LightLod, ParticleSystem/SmokeSpawner, EffectArea heat, AudioSource/ZSFX crackle, donor mesh Renderers disabled — PR #23 path, kept). On top: a **haphazard pile of `rock_low` clones, count = the stone ladder (T1=9 → T5=21)**, each **vertically squashed + horizontally flattened**, deterministically jittered (seed = ZDO id → identical across clients + reloads), pigment-tinted. Plus a **small HP-gated wear-state ember** at the top, lit only at ≥75% HP and fizzling out below it (decorative only — NOT light/heat/comfort, does NOT re-enable the donor Fireplace). See `Features/Cairns/CairnTag.cs`. |
| Patch surface | `WearNTear.OnDamage`/`OnRepair` postfix for glow + tier transitions; `SE_Rested.CalculateComfortLevel` for comfort patch |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` |

#### Painted Sign

| Field | Value |
|---|---|
| Display name | Painted Sign |
| Prefab name | `piece_sbpr_sign` (single piece — color is per-instance ZDO state, NOT a prefab fork) |
| Type | `Piece` (Sign variant) |
| Mod | Trailborne |
| Biome tier | Meadows |
| Craft station | Explorer's Bench (to craft the Trailblazer's Spade that opens the build menu) — placed in-world via the **Trailblazer's Spade build menu** ('Trail' tab). **No station-proximity required to PLACE** the sign. |
| Recipe | 2 Wood (placed UNPAINTED; ink is NOT a build ingredient) |
| Function | One buildable free-standing signpost, placed unpainted. Painted AFTER placement by applying a pigment/ink item (red/white/blue/black) to the placed sign; re-applying a different ink repaints it. Color persists + syncs via the sign's ZDO. Text written via vanilla E dialog. |
| Visual notes | Vanilla `sign` placard kitbashed onto a **vanilla 2m wood pole (`wood_pole2`)** so it stands free on the ground like a trail signpost (Daniel 2026-06-05), board raised to readable height (~1.2m) near the pole top. The pole is a decorative child stripped of ZNetView/Piece/WearNTear/Collider (no own ZDO, not separately destructible, never intercepts the E raycast). Shown in plain wood (unpainted) until painted; on paint the BOARD mesh is runtime-tinted (pole stays un-tinted), re-applied on spawn from ZDO. |
| Patch surface | `Sign.UseItem` prefix (apply-ink-to-paint, the ItemStand `Interactable.UseItem` pattern); per-instance ZDO color field `SBPR_SignColor` ("" = unpainted). Build piece added to the **Spade PieceTable** (not the Hammer); `Piece.m_craftingStation` cleared so placement needs no bench proximity. Pin emission (Shift+E) piggybacks on the Minimap pin system from design/nomap.md §3 — currently unregistered (follow-up). |
| Status | SPEC LOCKED (single-sign + paint-via-ink model, Daniel 2026-06-04; Spade-menu + 2m-pole kitbash, Daniel 2026-06-05) |
| Source spec | `docs/v0.1.0/planning/requirements.md` §A2.6 |

#### Path Lamp

| Field | Value |
|---|---|
| Display name | Path Lamp |
| Prefab name | `piece_sbpr_path_lamp` |
| Type | `Piece` (Light source, fueled) |
| Mod | Trailborne |
| Biome tier | Meadows (downshifted from Black Forest/Corewood per Daniel 2026-06-04; see requirements.md §A3.7) |
| Craft station | Explorer's Bench (to craft the Trailblazer's Spade that opens the build menu) — placed in-world via the **Trailblazer's Spade build menu** ('Trail' tab). **No station-proximity required to PLACE** the lamp. |
| Recipe | 3 Wood + 2 Resin |
| Function | Trail-illumination light source — dimmer than vanilla torch, longer fuel duration, manual ignition |
| Visual notes | Tier 1 reuse — clone of vanilla `piece_groundtorch_wood`, **scaled 3× vertically** (Daniel 2026-06-05) so it reads as a tall standing path lamp. Scaling is foot-anchored: the base stays flush with the ground and the flame/light rides to the new top (geometry children scale, the flame/Light children keep their size and only translate up). Root collider intentionally NOT rescaled (flag QA if the collision box should match the taller visual). |
| Patch surface | Pure prefab work (clone + 3× foot-anchored Y-scale + Fireplace component config). Build piece added to the **Spade PieceTable** (not the Hammer); `Piece.m_craftingStation` cleared so placement needs no bench proximity. |
| Status | SPEC LOCKED (Meadows recipe 3 Wood + 2 Resin, Daniel 2026-06-04; Spade-menu + 3× tall, Daniel 2026-06-05) |
| Source spec | `docs/v0.1.0/planning/requirements.md` §A2.4 / §A3.7 |

### Items

#### Cairn Marker

| Field | Value |
|---|---|
| Display name | Cairn Marker |
| Prefab name | `SBPR_Item_CairnMarker` (or per-color variants — TBD if pigment color forks the item or is metadata) |
| Type | `ItemDrop` (Consumable, build-ingredient) |
| Mod | Trailborne |
| Biome tier | Meadows |
| Craft station | Explorer's Bench |
| Recipe | 2 Leather Scraps + 1 Finewood + 1 Pigment (player's color choice) |
| Function | Required ingredient for Cairn initial-build (consumed on placement). Pigment color used to craft the marker binds the Cairn's color at craft-time. |
| Stack size | TBD (likely 10) |
| Weight | TBD (likely 0.5) |
| Patch surface | None — pure ObjectDB registration |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` |

#### Trailblazer's Spade

| Field | Value |
|---|---|
| Display name | Trailblazer's Spade |
| Prefab name | `SBPR_TrailblazersSpade` |
| Type | `ItemDrop` (Tool, hoe/hammer-equivalent) |
| Mod | Trailborne |
| Biome tier | Meadows |
| Craft station | Explorer's Bench |
| Recipe | 5 Wood + 2 Flint + 2 Leather Hides |
| Function | Single tool item — holds the Trailborne build menu (Cairns, Painted Signs, Path Lamps). 1.5/3/5m path widths (mirror Hoe). Replant Grass in 3 widths (1.5/3/5m) mirroring the path widths — each restores grass over the stated footprint like the vanilla Cultivator's "Grass" mode (scales only the grass/paint radius, NOT cultivate, no terrain raise/level at any width). Clear Vegetation deferred to v0.2.0. |
| Patch surface | Likely a new `ItemDrop` with `Tool` ItemType + custom `Hoe`-derived component for path-laying |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` |

#### Pigments (R / W / B / Blue)

| Field | Value |
|---|---|
| Display name | Red Pigment / White Pigment / Black Pigment / Blue Pigment |
| Prefab name | `SBPR_Item_PigmentRed` / `_White` / `_Black` / `_Blue` |
| Type | `ItemDrop` (Material) |
| Mod | Trailborne |
| Biome tier | Meadows (R/W/B), Black Forest (Blue via blueberry) |
| Craft station | Explorer's Bench |
| Recipe (R) | 1 Raspberry → 2 Red Pigment |
| Recipe (W) | 1 Bone Fragment → 2 White Pigment |
| Recipe (B) | 1 Coal → 2 Black Pigment |
| Recipe (Blue) | 1 Blueberry → 2 Blue Pigment |
| Function | Crafting ingredient: consumed at craft time to bind a Cairn Marker's color; applied to a placed Painted Sign to paint/repaint it. |
| Stack size | 20 |
| Weight | 0.1 |
| Patch surface | None — pure ObjectDB registration |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` |

### Status Effects
*(none for Trailborne v1)*

### Patched vanilla entities

- **Cartography Table** — v1 disables build AND functionality on existing instances
- **Minimap** — v1 nomap-config controls visibility (nomap=ON → no map; nomap=OFF → minimap only, no M-key, no north indicator)
- **Vanilla Sign** — the placed Painted Sign's text-edit interaction is intercepted to open a custom combined Paint+Text uGUI panel (two-tone: text color + border color, pigment cost per slot). Non-SBPR signs fall through to vanilla. (Combined-panel + two-tone model, Daniel 2026-06-05; supersedes the 6/04 apply-ink model.)

---

## Trailborne v1.1 (planned, not yet specced)

- Ember Lamps
- Beacons
- (Path Lamp upgrade tier — TBD)
- (Additional pigment colors — Yellow from cloudberry blocked until Plains-tier release)

---

## Future SBPR mods (not yet specced)

- **Guardian Stones** family — server worldbuilding (separate mod, separate spec)
- **Map Station** (Trailborne v2)
- **Real Tents** (Trailborne v2)
- **Pocket Portal / Twisted Portal** (Trailborne v3+)
- **Iron Compass** (Trailborne v3+, optional)
- **Seer's Stone** (Trailborne v4+)

---

## Maintenance discipline

1. **No piece/item ships without an entry in this file.** Add the row during spec finalization, before code is written.
2. **When a recipe changes, this file is updated in the same PR.** Spec docs and this dataset are co-canonical for catalog data.
3. **Prefab names must be unique across the SBPR namespace.** Check this file before naming.
4. **Status field is the ground truth for "is this shipping yet?"** When promoting from IN DESIGN → SPEC LOCKED → IMPLEMENTED → RELEASED, update here first.
5. **When self-hosted niflheim.wiki ships, this file becomes the wiki page-generation source.** Keep entries wiki-ready (display name, function, recipe, biome tier are the public-facing fields).
