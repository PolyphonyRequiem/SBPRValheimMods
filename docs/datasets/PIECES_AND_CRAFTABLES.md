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
| Build-menu tab | Spade → single **"Trail"** tab (`PieceCategory.Misc`). The spade's from-scratch PieceTable declares only the Misc-backed "Trail" category, so every spade piece (paths, sign, lamp, cairns) MUST be `m_category = Misc` or its tab never renders and the piece is invisible. **Regression history:** v0.2.2 shipped cairns as `PieceCategory.Crafting` → all four were added to the table but silently absent from the menu. Fixed 2026-06-07: cairn `m_category = Misc` + a boot-time `EnsureCategory` guard in `Trailblazing` that ERROR-logs + self-heals any future category drift. |
| Recipe (initial build, T1) | 3 Stone + 1 Resin + 1 Cairn Marker |
| Recipe (upgrade T1→T2→T3→T4→T5) | 3 Stone + 1 Resin (flat per tier) |
| Recipe (repair) | 3 Stone + 1 Resin (flat) |
| Comfort floor | 3 / 4 / 5 / 6 / 7 (per tier; max() clamp via patch on `SE_Rested.CalculateComfortLevel`) |
| Comfort radius | TBD (proposed: ~10m, matches vanilla Banner) |
| HP states | ≥75% pristine (cosmetic fire lit); <75% fizzled (fire OUT); <25% downgrade tier; 0% collapse |
| Function | Always-on comfort-emitting trail waypoint, color-bound to its Cairn Marker's pigment, mandatory decay. **Shows a COSMETIC fire at pristine — flame VFX + fire SFX + a small Light (below torch intensity); NO heat (EffectArea disabled), NO fuel (Fireplace forced infinite-fuel).** Fire is HP-gated: lit ≥75% HP, OUT below (fizzled = fire out). (Comfort floor is applied separately via the `SE_Rested` patch, not via fire.) Revised 2026-06-07 — the prior "non-burning marker" spec was wrong (Daniel). |
| Visual notes | Rich procedural stone pile (LOCKED 2026-06-05 — see requirements.md §A2.1b). Cloned from vanilla `bonfire` as a structural base, with the donor fire CONFIGURED into a cosmetic fire (Daniel 2026-06-07 reversal): Fireplace KEPT but forced `m_infiniteFuel=true`/fuel-less; flame ParticleSystem(s) + fire AudioSource/ZSFX KEPT; ONE Light kept + dimmed below a torch; EffectArea (heat) + SmokeSpawner DISABLED; donor mesh logs hidden. On top: a **haphazard pile of `Pickable_Stone` clones, count = the stone ladder (T1=9 → T5=21)**, each **vertically squashed + horizontally flattened**, deterministically jittered (seed = ZDO id → identical across clients + reloads), pigment-tinted. The cosmetic fire is HP-gated (lit ≥75%, out below) — fizzling reads as the fire going out. See `Features/Cairns/CairnTag.cs` (`ConfigureCosmeticFire`/`ReconcileFire`). |
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
| Recipe | 2 Wood (placed UNPAINTED; pigment is NOT a build ingredient — it is consumed at paint time via the panel) |
| Function | One buildable free-standing signpost, placed unpainted. Interacting with the placed sign opens a **custom combined Paint+Text uGUI panel** (replaces the vanilla text dialog): pick a **text/board color** AND an optional **border color** (two-tone), pay **one pigment per filled color slot** via `{Paint this and consume}` (same color in both slots = 2 of that pigment; border optional; ≥1 color required; re-paint re-consumes), and edit the label via `{Update Text}` (FREE, locked until ≥1 color is chosen). Each swatch row leads with an explicit **`∅ None`** tile and renders **only DISCOVERED pigments** (no dead reserved slots). The text color tints the **board mesh AND the written letters**. Both colors + text persist + sync via the sign's ZDO. |
| Visual notes | Vanilla `sign` placard kitbashed onto a **vanilla 2m wood pole (`wood_pole2`)** so it stands free on the ground like a trail signpost (Daniel 2026-06-05), board raised so its TOP sits just under the measured pole crown (board centre ~1.65m near the pole top, post foot flush at ground — anchored to the measured crown, pivot-robust, no magic height; fixes the 2026-06-07 "board floats mid-post" regression). The pole is a decorative child stripped of ZNetView/Piece/WearNTear/Collider (no own ZDO, not separately destructible, never intercepts the E raycast). **Two-tone:** the vanilla sign mesh has no separable frame, so a thin **border element** (`SBPR_SignBorder`, a scaled copy of the board mesh with its own material) is kitbashed in at register time to receive the second tone. Shown in plain wood (unpainted) until painted; on paint the BOARD mesh is runtime-tinted to the text color, the **TMP text widget** (`Sign.m_textWidget`) is driven to the text color, and the BORDER element to the border color (pole stays un-tinted), all re-applied on spawn from ZDO. The panel is laid out with Unity **layout groups** (not hand-computed offsets) for consistent alignment/margins. |
| Patch surface | `Sign.Interact` prefix opens the combined Paint+Text panel for SBPR signs (suppresses the vanilla text dialog; non-SBPR signs fall through). Panel `SignPaintPanel` is custom clean-room uGUI (`UnityEngine.UI` primitives — no copied vanilla prefab). While open: `Player.TakeInput` postfix blocks character input, `PlayerController.TakeInput(bool)` postfix freezes camera mouse-look (routing through vanilla's own suppression gate — the one the replaced sign dialog used), and `GameCamera.UpdateMouseCapture` postfix releases the cursor. Text color is applied to `Sign.m_textWidget.color`/`.faceColor` (TMP); `SignTintBackup` snapshots original materials so the `None` affordances revert live. Two-tone per-instance ZDO color fields `SBPR_SignTextColor` + `SBPR_SignBorderColor` (unset = empty string); legacy `SBPR_SignColor` is one-way migrated into the text-color field on spawn. `SignPaintBackend` computes the crafting-style cost (icon + name + have/need), gates swatches on `IsPigmentDiscovered` (known recipe OR owned — default, open question), checks-then-consumes pigments atomically (no partial paint), and owner-writes both tones. Build piece added to the **Spade PieceTable** (not the Hammer); `Piece.m_craftingStation` cleared so placement needs no bench proximity. Pin emission (Shift+E) stays **unregistered** (follow-up). |
| Status | SPEC LOCKED (combined Paint+Text panel, two-tone, Daniel 2026-06-05); **panel rebuilt on native-idiom uGUI 2026-06-07** (layout groups, discovered-only swatches + explicit None, text-widget color, crafting-style cost, camera lock, Pigment naming — Daniel playtest). CLIENT-SIDE — not headless-verifiable |
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
| Patch surface | Clone of vanilla `Hoe` ItemDrop + a from-scratch spade-only PieceTable (`Trailblazing`). Six terrain-op pieces (`piece_sbpr_path_{narrow,standard,wide}` + `piece_sbpr_replant_{narrow,standard,wide}`) clone vanilla `path`/`replant` and scale TerrainModifier radii to 1.5/3/5 m. **Placement-ripple preview:** a `Player.UpdatePlacementGhost` postfix (`PlacementMarkerRadiusPatch`) sizes the aiming ripple to the active op's width (see Patched-vanilla entry below) — client-cosmetic, no effect on the op's real radius. |
| Status | SPEC LOCKED |
| Source spec | `specs/2026-06-03-trailborne-v1/planning/requirements.md` |

#### Pigments (R / W / B / Blue)

| Field | Value |
|---|---|
| Display name | Red Pigment / White Pigment / Black Pigment / Blue Pigment |
| Prefab name | `SBPR_InkRed` / `SBPR_InkWhite` / `SBPR_InkBlack` / `SBPR_InkBlue` — **save/wire contract, do NOT rename** (placed signs/cairns store these strings). Display names + code identifiers were unified to "Pigment" 2026-06-07; the prefab string keeps its historical `SBPR_Ink*` spelling on purpose. |
| Type | `ItemDrop` (Material) |
| Mod | Trailborne |
| Biome tier | Meadows (R/W/B), Black Forest (Blue via blueberry) |
| Craft station | Explorer's Bench |
| Recipe (R) | 1 Raspberry → 2 Red Pigment |
| Recipe (W) | 1 Bone Fragment → 2 White Pigment |
| Recipe (B) | 1 Coal → 2 Black Pigment |
| Recipe (Blue) | 1 Blueberry → 2 Blue Pigment |
| Function | Crafting ingredient: consumed at craft time to bind a Cairn Marker's color; consumed at paint time (one per filled color slot) via the Painted Sign panel to paint/repaint a placed sign. |
| Stack size | 20 |
| Weight | 0.1 |
| Patch surface | None — pure ObjectDB registration |
| Status | SPEC LOCKED |
| Source spec | `docs/v0.1.0/planning/requirements.md` §A2.5 |

### Status Effects
*(none for Trailborne v1)*

### Patched vanilla entities

- **Cartography Table** — v1 disables build AND functionality on existing instances
- **Minimap** — v1 nomap-config controls visibility (nomap=ON → no map; nomap=OFF → minimap only, no M-key, no north indicator)
- **Vanilla Sign** — the placed Painted Sign's text-edit interaction is intercepted to open a custom combined Paint+Text uGUI panel (two-tone: text color + border color, pigment cost per slot). Non-SBPR signs fall through to vanilla. (Combined-panel + two-tone model, Daniel 2026-06-05; supersedes the 6/04 apply-ink model.)
- **Placement marker ripple (`Player.UpdatePlacementGhost` → `CircleProjector`)** — client-cosmetic. Vanilla's aiming ripple (the animated ground ring) is a fixed-radius `CircleProjector` (~5 m); a `Player.UpdatePlacementGhost` postfix sizes it to the **active spade op's effect radius** (1.5 / 3 / 5 m) so the preview matches the real affected area, and restores the captured vanilla default whenever the ghost is **not** one of our six spade ops (so a vanilla Hoe/Cultivator placement is never resized). Gated on **piece identity** (our op prefab names via `Trailblazing.TryGetSpadeOpRadius`), not the server-sanity doctrine, because it is a local preview only. **No recipe/data change** — the op already applied at its true radius; this only fixes the misleading preview. (Request 1, spike `docs/investigations/2026-06-07-terrain-placement-ripple-magnitude-spike.md`; implemented 2026-06-07.)

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
