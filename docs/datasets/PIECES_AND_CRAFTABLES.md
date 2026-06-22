---
title: SBPR Pieces & Craftables Dataset
purpose: Canonical specs for every piece, item, and crafting station across SBPR mods
status: living document — appended per-piece as specs lock
last_updated: 2026-06-17
last_reviewed: 2026-06-17
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
| Visual notes | Rich procedural stone pile (LOCKED 2026-06-05 — see requirements.md §A2.1b). Cloned from vanilla `bonfire` as a structural base, with the donor fire CONFIGURED into a cosmetic fire (Daniel 2026-06-07 reversal): Fireplace KEPT but forced `m_infiniteFuel=true`/fuel-less; flame ParticleSystem(s) + fire AudioSource/ZSFX KEPT; ONE Light kept + dimmed below a torch; EffectArea (heat) + SmokeSpawner DISABLED; donor mesh logs hidden. On top: a **haphazard pile of stones, count = the stone ladder (T1=9 → T5=21)**, each **vertically squashed + horizontally flattened**, deterministically jittered (seed = ZDO id → identical across clients + reloads), pigment-tinted. **Each stone is DELIBERATELY CONSTRUCTED (2026-06-07 pivot) — a bare GameObject with only Transform+MeshFilter+MeshRenderer, built from the `Pickable_Stone` donor's mesh+material WITHOUT instantiating that networked prefab** (the old runtime-clone-then-DestroyImmediate path orphaned null-ZDO entries in `ZNetScene.m_instances` → per-frame NRE soft-lock; constructing from bare mesh removes the crash by construction). The cosmetic fire is HP-gated (lit ≥75%, out below) — fizzling reads as the fire going out. See `Features/Cairns/CairnTag.cs` (`ConfigureCosmeticFire`/`ReconcileFire`). |
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
| Visual notes | Vanilla `sign` placard kitbashed onto a **vanilla 2m wood pole (`wood_pole2`)** so it stands free on the ground like a trail signpost (Daniel 2026-06-05), board raised so its TOP sits just under the measured pole crown (board centre ~1.65m near the pole top, post foot flush at ground — anchored to the measured crown, pivot-robust, no magic height; fixes the 2026-06-07 "board floats mid-post" regression). The pole is a decorative child stripped of ZNetView/Piece/WearNTear and its OWN collider (no own ZDO, not separately destructible). A separate thin **non-trigger post-foot ground-contact collider** (bottom plane at the measured planted-post foot, root-local y≈0) makes the placed sign seat FLUSH instead of ~3/4 buried (fixes t_4ad60d6f / parent t_1dc88742): Valheim seats a piece by its lowest enabled non-trigger collider, and after the pole's collider is stripped the board's crown-lifted interact collider was the lowest one and drove the post underground. In the placement ghost the foot collider is enabled (on the `piece` layer) so the seat counts it; on the placed sign `SignTag` disables it (gated on a live ZDO) so the BOARD stays the sole interact/paint target (never intercepts the E raycast). **Two-tone:** the vanilla sign mesh has no separable frame, so a thin **border element** (`SBPR_SignBorder`, a scaled copy of the board mesh with its own material) is kitbashed in at register time to receive the second tone. Shown in plain wood (unpainted) until painted; on paint the BOARD mesh is runtime-tinted to the text color, the **TMP text widget** (`Sign.m_textWidget`) is driven to the text color, and the BORDER element to the border color (pole stays un-tinted), all re-applied on spawn from ZDO. The panel is laid out with Unity **layout groups** (not hand-computed offsets) for consistent alignment/margins. |
| Patch surface | `Sign.Interact` prefix opens the combined Paint+Text panel for SBPR signs (suppresses the vanilla text dialog; non-SBPR signs fall through). Panel `SignPaintPanel` is custom clean-room uGUI (`UnityEngine.UI` primitives — no copied vanilla prefab). While open: `Player.TakeInput` postfix blocks character input, `PlayerController.TakeInput(bool)` postfix freezes camera mouse-look (routing through vanilla's own suppression gate — the one the replaced sign dialog used), and a `GameCamera.LateUpdate` postfix (`SignPanelInputBlock.CursorPumpPatch`, §2L) frees + shows the cursor each frame and restores the gameplay lock on close. (The cursor pump was re-seated from the old `GameCamera.UpdateMouseCapture` seam onto `LateUpdate` in card t_1f82da71: a vanilla Input-System update emptied `UpdateMouseCapture` to a 1-byte `ret`, so the old postfix silently did nothing and the cursor stayed locked.) Text color is applied to `Sign.m_textWidget.color`/`.faceColor` (TMP); the **board + border mesh tint rides a per-renderer `MaterialPropertyBlock` (MPB) `_Color` override** — the render-time layer vanilla itself paints build-piece colour through (`MaterialMan`/`WearNTear.Highlight`). The earlier `sharedMaterials.SetColor` + `SignTintBackup` clone-snapshot mechanism wrote to a layer the piece's MPB sits in front of, so the board/border never visibly changed and only the TMP text (a Canvas renderer outside `MaterialMan`) recoloured — fixed in t_f3310406 (diagnosis t_24ad2570). `None` reverts a renderer to its material's own `_Color` (no clone to restore). `SignMeshRetintPatch` (postfix on `WearNTear.Highlight`, gated to `SignTag`) debounces a one-shot mesh re-assert ~0.3s after a hammer-hover ends, so the support-overlay's MaterialMan `_Color` wipe can't leave a painted sign stuck on plain wood (the mesh-layer twin of `SignTextRetintPatch`). Two-tone per-instance ZDO color fields `SBPR_SignTextColor` + `SBPR_SignBorderColor` (unset = empty string); legacy `SBPR_SignColor` is one-way migrated into the text-color field on spawn. `SignPaintBackend` computes the crafting-style cost (icon + name + have/need), gates swatches on `IsPigmentDiscovered` (known recipe OR owned — default, open question), checks-then-consumes pigments atomically (no partial paint), and owner-writes both tones. Build piece added to the **Spade PieceTable** (not the Hammer); `Piece.m_craftingStation` cleared so placement needs no bench proximity. Pin emission (Shift+E) stays **unregistered** (follow-up). |
| Status | SPEC LOCKED (combined Paint+Text panel, two-tone, Daniel 2026-06-05); **panel rebuilt on native-idiom uGUI 2026-06-07** (layout groups, discovered-only swatches + explicit None, text-widget color, crafting-style cost, camera lock, Pigment naming — Daniel playtest); **chrome skinned with real vanilla UI sprites + font 2026-06-09** (t_b47035e7 — wood-panel background/frame sprite, carved-wood button sprite + hover states, Norse display font harvested at runtime from live vanilla GUI donors via `VanillaUISkin`; flat-colour fallback if a donor is absent — Daniel playtest). CLIENT-SIDE — not headless-verifiable |
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
| Patch surface | Clone of vanilla `Hoe` ItemDrop + a from-scratch spade-only PieceTable (`Trailblazing`). Six terrain-op pieces (`piece_sbpr_path_{narrow,standard,wide}` + `piece_sbpr_replant_{narrow,standard,wide}`) are built **ADDITIVELY as modern `TerrainOp` pieces** (v0.2.17, card t_6fc9b3fa, ADR-0006) — `new GameObject()` + `Piece` + `TerrainOp`, NO ZNetView, NOT registered in ZNetScene — mirroring vanilla `path_v2` (Dirt, paintR 2.0) / `replant_v2` (Reset, paintR 2.2), scaling only `m_settings.m_paintRadius` to 1.5/3/5 m and never writing level/smooth/raise. This replaced the pre-0.2.17 clone of the LEGACY `path`/`replant` `TerrainModifier` donors, whose persistent networked pieces fought for precedence on a shared tile (the "grass fights path" bug). A server-only one-time `ZNet.LoadWorld` postfix (`LegacyTerrainOpZdoCleanup`) destroys legacy `piece_sbpr_{path,replant}_*` ZDOs left in existing worlds. **Placement-ripple preview:** a `Player.UpdatePlacementGhost` postfix (`PlacementMarkerRadiusPatch`) sizes the aiming ripple to the active op's width (see Patched-vanilla entry below) — client-cosmetic, no effect on the op's real radius. |
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

## Trailborne v2 (Black Forest) — Cartography

> Spec: `docs/v2/planning/requirements.md` + `docs/v2/planning/cartography-impl-spec.md`.
> Three interlocking features (Surveyor's Table + Local Map + Cartographer's Kit). The
> Table is the FOUNDATION (lowest risk); the Local Map viewer (highest risk) builds on it.

### Pieces

#### Surveyor's Table

| Field | Value |
|---|---|
| Display name | Surveyor's Table |
| Prefab name | `piece_sbpr_surveyors_table` |
| Type | `Piece` (placed station; custom `SurveyorTableTag : MonoBehaviour`, Hoverable + Interactable) |
| Mod | Trailborne |
| Biome tier | Black Forest |
| Craft station | **NONE to place** (`m_craftingStation = null`) — placed in-world via the **Trailblazer's Spade build menu** ('Trail' tab), like every Spade-placed SBPR piece. (Architect REVERSED the proposed bench-in-range lean, 2026-06-10.) |
| Recipe (build) | Fine Wood ×10 + Bronze ×2 + Deer Hide ×4 + Bone Fragments ×8 |
| Build-menu tab | Spade → single **"Trail"** tab (`PieceCategory.Misc`, like all spade pieces; `EnsureCategory` guards drift) |
| Function | Retains a **shared, cumulative, windowed 1000 m survey** of its own disc, persisted compressed in the Table ZDO (`ZDOVars.s_data`) like vanilla MapTable. **Must be NAMED before it will bind/imprint maps** (issue 10, §1.6): an unnamed Table prompts the player to name it (vanilla `TextInput` rename dialog) and refuses to imprint until named; the name persists per-instance (owner-write `SBPR_TableName` ZDO) and shows in the hover + as the viewer title. **Using it (E)** merges the surveyor's in-disc explored fog + in-disc shareable pins into the shared record (cumulative OR-merge; beyond-1000 m dropped — C5) and opens the forked viewer on the SHARED data with **pin removal enabled** (D4). **Imprinting a Local Map (issue 6, §2I): look at the Table and press the hotbar number (1–8) of the slot holding the blank Local Map** — that one map is imprinted with the Table's survey (wrong/empty/non-map slot safely refused; Use no longer auto-imprints). Ward-gated (`PrivateArea.CheckAccess`) like vanilla. Persists across server restart (AT-TABLE-PERSIST). |
| Naming (issue 10) | Per-instance name in `SBPR_TableName` (owner-write ZDO string; the KEY string is LOCK/never-rename — the player VALUE is freely re-nameable, see below). Set via the vanilla `TextInput`/`TextReceiver` rename dialog (the Tameable/Sign/Portal mechanism — NOT a custom uGUI panel; NOT shared with the marker-namable card per Daniel 2026-06-11). **Bind-gate:** imprint is refused while the name is empty (§1.6.4). **RE-name (issue 3, §1.6.5):** an already-named Table is re-named with **`[Use]+Alt`** (KBM Shift+E; vanilla `Tameable` precedent decomp `:27075`) — the named-table hover advertises `[$KEY_AltPlace + $KEY_Use] $hud_rename`; the dialog pre-fills the current name (edit, not retype); re-naming changes FUTURE imprints only (already-imprinted maps keep their stamped `sbpr_map_name` — no migration). The name is stamped onto imprinted Local Maps (`sbpr_map_name`, §2A.6) and shown as the viewer title (§2B.1). AT-TABLENAME-1..8 + AT-TABLE-RENAME / -DISCOVERABLE / -NOMIGRATE / -NOREGRESS. **IMPLEMENTED 2026-06-12 (card t_41482aa3, build 0/0):** `SurveyorTableTag` implements `TextReceiver`; unnamed Table on Use → `$hud_rename` dialog (always prompt, the spec-sanctioned variant) + Center message, no imprint; censored read/write via `CensorShittyWords.FilterUGC`. **RE-name affordance ADDED 2026-06-15 (card t_91e81d80, build 0/0):** named + Alt-Use → re-name dialog (context-aware message), hover advertisement. [hold] PR, in-game verify pending. |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructPieceShell` builds the networked skeleton (ZNetView + Piece + WearNTear + collider) from scratch; the vanilla `piece_cartographytable` is read ONLY as a visual blueprint and its mesh subtree (`new`, material `Cartographer_mat`) is grafted as a ZNetView-free cosmetic child (`Assets.GraftVisualSubtree`). NEVER instantiates the vanilla MapTable prefab. HP 800 (tunable v0.2+). Collider/material/HP are visual-polish flags for Daniel's in-game pass. |
| Patch surface | None on the Table itself. `SurveyorTableTag` is self-contained: survey + ZDO persistence (owner-write via `ZNetView.IsOwner`/`InvokeRPC` like vanilla MapTable `RPC_MapData`), contribute-on-use, ward gate, pin-removal backend (`ICartographyPinEditor`), and (issue 10) the `SBPR_TableName` ZDO + `TextInput` naming dialog + bind-gate — all non-Harmony. The forked viewer is the separate card t_7b616020; the `CartographyViewer` seam decouples them. Windowed-fog cell math = `BoundedMapMath` (productionized spike seam t_e8bbbe48). *(The name-display Harmony patch lives on the Local Map item side — §2A.6b, see the Local Map row.)* |
| Status | IMPLEMENTED (code + spec + SpecCheck row 1, card t_2715661d, 2026-06-10; `[hold]` PR, awaiting Daniel merge + in-game verify). **Naming + bind-gate (`SBPR_TableName`) + viewer title = IMPLEMENTED 2026-06-12 (issue 10, card t_41482aa3), build 0/0, `[hold]` PR awaiting Daniel review + in-game verify (AT-TABLENAME-1/2/4/5).** **logs-green ≠ playable.** |
| Source spec | `docs/v2/planning/requirements.md` §1 + `docs/v2/planning/cartography-impl-spec.md` §1 |

### Items (v2 cartography)

#### Local Map

| Field | Value |
|---|---|
| Display name | Local Map |
| Prefab name | `SBPR_LocalMap` |
| Type | `ItemDrop`, **`ItemType.TwoHandedWeapon`** (=14, architect lock PR #94) |
| Mod | Trailborne |
| Biome tier | Black Forest |
| Craft station | Explorer's Bench (`piece_sbpr_explorers_bench`) — NOT the Surveyor's Table |
| Recipe (craft) | Deer Hide ×2 + Fine Wood ×4 (amount 1) |
| Function | A field map, **blank when crafted**. **Imprint at a Surveyor's Table** (which must be NAMED first — issue 10, §1.6) copies a SNAPSHOT (not a live link) of that table's windowed 1000 m survey + bound-origin onto the item instance, AND stamps the Table's name onto the item so its inventory title reads e.g. **"Map: Northern Outpost"** (distinguishable from other bound maps — §2A.6). **Imprint trigger (issue 6, §2I): look at the Table and press the hotbar number (1–8) of this map's slot** — only that one map is imprinted (replaces the old auto-imprint-on-Use). **Two-handed equip** hard-unequips weapon+shield (never hides — block-clears by construction via the vanilla `TwoHandedWeapon` `EquipItem` branch); a left-hand **Torch** is allowed back (lit map at night). **Minimap binding durable while in inventory; reverts to no-map the instant it leaves.** **Full-screen bounded view requires it actively EQUIPPED**, opened with **Use (E)** (§2G; reliability fix issue 6 §2I) and shows the Table name as an on-screen title (§2B.1). Field view is read-only (no pin editing). |
| Storage | Per-instance in `ItemDrop.ItemData.m_customData` (`sbpr_map_blob` = Base64(`Utils.Compress`(windowed `SurveyData`)), `sbpr_map_bound` = origin X;Z, and — issue 10 — `sbpr_map_name` = the imprinted Table name; all LOCK, never rename). **Verified at build** to round-trip `Inventory.Save/Load` (player profile ZPackage) AND the dropped-item ZDO on this game version — so it persists restart/drop/trade; no ZDO "map case" fallback needed. (`m_customData` is the ONLY per-instance survivable store: `m_shared` is ref-shared across instances + reassigned from the prefab on every load, so it can't carry a per-instance name — §2A.6.) One format with the Table + viewer (§2C). |
| Patch surface | `LocalMapEquipPatch` — prefix+postfix on `Humanoid.EquipItem(ItemData,bool)` (overload-disambiguated): the torch exception (C12/AT-MAP-TORCH). `LocalMapBootstrapPatch` — postfix on `Minimap.Start` attaching the client-only `LocalMapController` (carry/equip state machine). **(Issue 10, IMPLEMENTED 2026-06-12, card t_41482aa3)** `LocalMapTooltipNamePatch` (Postfix on private `InventoryGrid.CreateItemTooltip` → overwrites `UITooltip.m_topic`, the title) + `LocalMapHoverNamePatch` (Postfix on `ItemDrop.GetHoverName` → world-drop hover) substitute the bare `sbpr_map_name` (with a `"Map: "` display prefix) for the title — **guarded on presence of the `sbpr_map_name` key** (not `m_dropPrefab`, which is `[NonSerialized]`), pure pass-through otherwise; both `PatchAll`-registered in `Plugin.Awake` (PatchCheck-guarded, AT-TABLENAME-8). Combat suppressed by empty `m_attack`/`m_secondaryAttack` animations (`HavePrimaryAttack`/`HaveSecondaryAttack` false → no LMB/RMB/block — AT-MAP-BLOCKCLEAR). Item built by the repo's clone-and-reshape idiom (donor `Hoe`, like the Spade): type reshaped to TwoHandedWeapon, build PieceTable nulled, attacks emptied. |
| Status | IMPLEMENTED (code + spec + SpecCheck row 2, card t_cb831069; merged to `integ/v2-cartography` via PR #101, awaiting Daniel's integ→v1 merge + in-game verify). **Name inheritance (`sbpr_map_name` + title patches) + viewer title = IMPLEMENTED 2026-06-12 (issue 10, card t_41482aa3), build 0/0, [hold] PR awaiting Daniel review + in-game verify (AT-TABLENAME-3/4/5).** **logs-green ≠ playable** — the in-game pixel render + equip feel + name-on-item are F9/in-hand checks. |
| Source spec | `docs/v2/planning/requirements.md` §2 + `docs/v2/planning/cartography-impl-spec.md` §2 |

#### Forked map viewer (`MapViewer`) — not a craftable

The bounded forked viewer is the render engine shared by the Local Map (field, read-only)
and the Surveyor's Table (TableEdit, pin removal). It is **not an item/piece** (no recipe,
no dataset row of its own); it registers behind the `CartographyViewer` seam. Productionized
from the GO-WITH-CAVEATS spike (`t_e8bbbe48`): paints OUR windowed fog `Texture2D` onto a
standalone uGUI `Canvas`/`RawImage` at fixed zoom (NOT vanilla's 4-texture shader composite,
NOT vanilla's nomap-suppressed map roots), hard 1000 m disc clip, polar edge-arrow clamp to
the disc, WorldPins rendered via the shared `#100` projection. Card t_cb831069.

#### Cartographer's Kit

| Field | Value |
|---|---|
| Display name | Cartographer's Kit |
| Prefab name | `SBPR_CartographersKit` |
| Type | `ItemDrop`, **`ItemType.Utility` (= 18)** — the Utility slot (player's `m_utilityItem`), same slot as Megingjord / Wishbone. Coexists with any weapon / shield / Local Map; never a hand item (AT-KIT-COEXIST). |
| Mod | Trailborne |
| Biome tier | Black Forest |
| Craft station | Explorer's Bench (`piece_sbpr_explorers_bench`) |
| Recipe (craft) | Red Pigment ×10 + White Pigment ×10 + Blue Pigment ×10 + Black Pigment ×10 + Fine Wood ×4 → 1 (the **40-pigment cost IS the gate**; pigments referenced via `Pigments.Pigment{Red,White,Blue,Black}Name`, values `SBPR_Ink*`) |
| Function | **Gates the personal auto-map's passive fog reveal.** Kit worn → walking reveals fog (vanilla `Minimap.UpdateExplore` runs); Kit absent → ZERO passive reveal (AT-KIT-GATE). The fog it accumulates is what gets imprinted at a Surveyor's Table. **NO discovery-flag system** (C10) — a normal recipe surfaced the vanilla way (`IsKnownMaterial`). |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructItemShell` builds the networked item skeleton (ZNetView + ZSyncTransform + Rigidbody + collider + ItemDrop with a FRESH SharedData) from scratch; NEVER clones a vanilla item (the pre-ADR Pigments/cairn-marker pattern). World-drop mesh grafted as a ZNetView-free cosmetic child off the vanilla `LeatherScraps` blueprint (`Assets.GraftVisualSubtree`, child `attach`). Inventory icon `cartographers_kit_v0.1.png` (v0.1 placeholder; icon is MANDATORY — the crafting UI indexes `m_icons[0]`). Utility items have no worn-body attach visual. Mesh/icon are visual-polish flags for Daniel's in-game pass. |
| Patch surface | **Harmony Prefix on `Minimap.UpdateExplore(float, Player)`** (decomp :48005) — no-ops the personal walking-reveal fog write unless the local player wears the Kit. Gates ONLY `UpdateExplore` (the single personal-reveal entry), NOT `Explore` directly (also reached from shared-data merges that must work without the Kit). Equipped-Kit detection via public `Inventory.GetEquippedItems()` + `m_dropPrefab` name (`m_utilityItem` is protected). Client-only by construction (no Minimap on the dedicated server); fails OPEN on error. **Touches the same Minimap explore path the Local-Map viewer (t_cb831069) reads — one fog-write model, not forked.** |
| Status | IMPLEMENTED (code + spec + SpecCheck row 3, card t_65fcfe5c, 2026-06-10; merged to `integ/v2-cartography` via PR #102, awaiting Daniel's integ→v1 merge + in-game verify). **logs-green ≠ playable.** |
| Source spec | `docs/v2/planning/requirements.md` §3 + `docs/v2/planning/cartography-impl-spec.md` §3 |

> **v2 cartography tier status:** the **Surveyor's Table** (`piece_sbpr_surveyors_table`, #99),
> **Local Map** + **forked map viewer** (`SBPR_LocalMap` / `MapViewer`, #101), and the
> **Cartographer's Kit** (`SBPR_CartographersKit`, #102) are ALL implemented and merged onto
> `integ/v2-cartography`. The branch awaits Daniel's in-game playtest + the final integ→v1
> merge gate. **logs-green ≠ playable** until that pass.

## Trailborne v2 (Black Forest) — Marker Signs / WorldPins

> v2 cartography tier. These four Marker Sign pieces are the **WorldPin substrate**
> the tier consumes (the Surveyor's Table edits WorldPins; the Local Map renders them).
> Design lock: `docs/design/marker-signs-worldpin.md`; impl spec:
> `docs/v2/planning/marker-signs-impl-spec.md`. Shipped in build-order milestones:
> **M1 = the four pieces + tag + spade wiring + SpecCheck (this entry)**; the Shift+E
> pin/unpin gesture + the WorldPin projection/reconcile engine are gated follow-ups
> (see the impl card t_0c7b782d review-required handoff — the durable cross-player
> unpin needs a server-authoritative scan/RPC the spec deferred to the cartography
> Table card).

### Pieces

#### Marker Signs (POI / Mining / Shelter / Portal)

| Field | Value |
|---|---|
| Display name | Marker: Point of Interest / Marker: Mining / Marker: Shelter / Marker: Portal |
| Prefab name | `piece_sbpr_marker_poi` / `piece_sbpr_marker_mining` / `piece_sbpr_marker_shelter` / `piece_sbpr_marker_portal` — **save/wire contract, do NOT rename** (placed markers store these as the ZDO-keyed pin identity). Four distinct pieces, NOT one piece with a type selector (Q3). |
| Type | `Piece` (Sign variant — carries a vanilla `Sign` so the paint/text panel + interaction stack apply) |
| Mod | Trailborne (v2) |
| Biome tier | Black Forest (v2 cartography tier) |
| Craft station | None to place — on the **Trailblazer's Spade build menu** ('Trail' tab, Pillar 1: Spade never Hammer). `Piece.m_craftingStation = null`. |
| Recipe | 2 Wood + 1 Greydwarf eye each (placed cost). The +1 Greydwarf eye is a **Black Forest availability gate**, not a cost tax — the eye is a Common BF drop, so it simply proves the player reached the Black Forest and killed a Greydwarf (this makes the recipe match the Black Forest biome tier above; previously the recipe was Meadows-wood-only). Markers stay cheap; the map pin is still the value-add. Locked by Daniel 2026-06-11. Token `GreydwarfEye` confirmed (prefab_index + wiki Internal ID). |
| Function | A buildable trail marker that, on **Shift+use**, pins/unpins itself on the player's map with a **custom type-coded marker icon** (magnifying glass / pickaxe / tent / circle). The pin is a durable, ZDOID-keyed WorldPin that disappears when the sign is destroyed — including destroyed while its zone is unloaded / the placer is offline (derive-by-scan reconcile). Primary use (E) opens a dedicated reference panel showing the marker icon, name, pin state, a Pin/Unpin button, **and a textbox to give the marker a custom name** (ENHANCEMENT, card t_62af5802) — that name becomes the map pin's label (dynamically: it re-projects on commit, no relog), falling back to the type label (e.g. "Point of Interest") when left blank. |
| Visual notes | **ADDITIVE construction (ADR-0006, AT-PIN-ADR0006)** — `new GameObject()` + `AddComponent` of Piece + WearNTear + ZNetView(`m_persistent=true`) + Sign + `MarkerSignTag` + a root BoxCollider. The board plank + 2m post are grafted by **reading** the vanilla `sign` / `wood_pole2` mesh+material references (NOT an Instantiate-then-strip of those ZNetView-bearing prefabs). A minimal additive world-space TMP widget is built and bound to `Sign.m_textWidget` so the vanilla Sign poll has a widget to write into (a null widget would NRE). First cut: **piece build-icon art = the marker icon art** (Daniel: "for now, just make the piece art the icon art"); the "icon overlaid on the piece art" is v2.1 polish. Placeholder glyph PNGs in `assets/icons/items/marker_{poi,mining,shelter,portal}_v0.1.png` (regenerable at the same filename via `scripts/gen_marker_icons_v01.py`). Board/post geometry: the board is **crown-anchored** to the planted pole top + **stood off the post's near side face** (no embed, sub-mm anti-z-fight kiss gap), and the post foot is **planted at y=0 with a post-foot ground collider** so the placed marker seats flush — the same silhouette as the Painted Sign, shared via `Runtime/SignGeometry.cs` (constants + crown/standoff math + foot collider + placed-instance neutralize), so the markers and the Painted Sign can never drift again (card t_cc093d04, spec `docs/v2/planning/marker-signs-geometry-fix-impl-spec.md`). |
| Patch surface | Registration via `MarkerSigns.RegisterPrefabs` (Registrar fan-out) + ODB resource rebuild + spade-table add in `Trailblazing`. `SignInteractPatch` recognises `MarkerSignTag`: **primary E → dedicated `MarkerSignPanel`** (icon + name + pin-state + Pin/Unpin button — NOT the pigment `SignPaintPanel`, which hard-requires a `SignTag` and has no marker colors); **Shift+E (`alt==true`) → toggle `SBPR_Pinned` + project/remove the WorldPin** (fast path). WorldPin engine in `WorldPins.cs` (`AddPin save:false` + `m_icon` override + derive-by-scan reconcile); triggers in `WorldPinReconcilePatches.cs` (`Minimap.SetMapMode` map-open, throttled `Minimap.Update` tick, `Minimap.Awake` stale-projection reset). Destroy hook = `MarkerSignTag` subscribes `WearNTear.m_onDestroyed` (public `Action`, no Harmony) → `WorldPins.OnMarkerDestroyed`. `SignPanelInputBlock` widened to gate on either panel (`AnyOpen`). |
| ZDO fields | `SBPR_MarkerType` (string: poi/mining/shelter/portal), `SBPR_Pinned` (bool), `SBPR_PinName` (string: player's custom pin label — ENHANCEMENT card t_62af5802; empty = fall back to type `PinLabel`; drives the WorldPin label), `SBPR_PinIconColor` + `SBPR_PinTextColor` (string: per-pin icon tint + label text color, HTML hex `"#RRGGBB"`, "" = unset = default white — color fast-follow card t_3d7aaa90, impl-spec §8; re-applied each pin rebuild via a `Minimap.UpdatePins` postfix; no ZDO migration). Owner-write via ZNetView. All keys are locked wire contracts — never rename. |
| Status | **M1+M2+M3 IMPLEMENTED** (card t_0c7b782d): 4 additive pieces + tag + spade wiring + SpecCheck +4 rows (M1); WorldPin projection + derive-by-scan reconcile engine + triggers (M2); Shift+E pin/unpin gesture + dedicated MarkerSignPanel + WearNTear destroy hook (M3). Build 0/0. **logs-green ≠ playable** — AT-MARK-1/2, AT-PIN-PERSIST, AT-PIN-DESTROY-LOADED/DURABLE, AT-PIN-ADR0006, AT-PILLAR-2 close only on Daniel's in-game check. **MVP scope:** renders to the player-centered minimap circle (v1 nerfs the full map); the 1000 m disc-bound, server-authoritative-RPC variant is deferred to the cartography viewer cards (both currently archived/triage). |
| Source spec | `docs/design/marker-signs-worldpin.md` + `docs/v2/planning/marker-signs-impl-spec.md` |

### Patched vanilla entities (v2)

- **Minimap (WIRED, M2/M3, card t_0c7b782d)** — the WorldPin projection calls `Minimap.AddPin(..., save:false)` + overrides `PinData.m_icon` with the custom marker sprite (a `save:false` projection so vanilla never persists our pins), reconciled by a derive-by-scan over live marker-sign ZDOs (`ZDOMan.GetAllZDOsWithPrefabIterative`). Trigger postfixes: `Minimap.SetMapMode` (map-open full reconcile), throttled `Minimap.Update` (periodic tick), `Minimap.Awake` (stale-projection reset for fresh-map rebuild). Renders to the player-centered minimap circle in the v1 MVP (no disc bound); the 1000 m disc-bound, server-authoritative-scan variant is deferred to the cartography viewer cards.

---

## Trailborne v2 (Black Forest) — Portal Seed → Ancient Portal

> **Status: SPECCED 2026-06-13 (architect card t_9a5540b2).** Design:
> `docs/design/pocket-portal.md` + `docs/design/ancient-portal-placeholder-art.md`.
> Buildable spec: `docs/v2/planning/ancient-portal-impl-spec.md`. A two-prefab feature on
> the **cairn pattern** — an item (the Seed) whose recipe is checked, and a piece (the
> Portal) whose build cost IS that item, so break→seed is free vanilla `DropResources`.
> Impl is the engineer-systems child of the spec card. **SpecCheck delta +2.**

### Items

#### Portal Seed

| Field | Value |
| --- | --- |
| Display name | Portal Seed |
| Prefab name | `SBPR_PortalSeed` |
| Type | `ItemDrop` (`ItemType.Material`) — a carried build ingredient, consumed by the Ancient Portal piece (cairn-marker pattern) |
| Mod | Trailborne |
| Biome tier | Black Forest |
| Craft station | Explorer's Bench (`piece_sbpr_explorers_bench`) |
| Recipe | 1 Ancient seed (`AncientSeed`) + 20 Greydwarf eye (`GreydwarfEye`) + 2 Surtling core (`SurtlingCore`), amount 1 |
| Weight / stack | **25 kg**, `m_maxStackSize = 1` (one-per-slot — 25 kg × stack defeats the carry-one fantasy) |
| Function | Plant with the **Hammer** (no station in range) → grows over ~15 s into an Ancient Portal. Dropped back (×1) when an Ancient Portal is destroyed — replantable. 🟡 Ectoplasm as an eye/core substitute is a **playtest-contingent** tuning lever, NOT in the first build. |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructItemShell` + a grafted script-free root/seed mesh child. Ship a real icon PNG (SpecCheck C1 screams if it's the fallback). |
| Patch surface | None (additive item + recipe). |
| Status | SPEC LOCKED |
| Source spec | `docs/v2/planning/ancient-portal-impl-spec.md` §2 |

### Pieces

#### Ancient Portal

| Field | Value |
| --- | --- |
| Display name | Ancient Portal |
| Prefab name | `piece_sbpr_ancient_portal` |
| Type | `Piece` + real vanilla `TeleportWorld` + custom `AncientPortalTag : MonoBehaviour` (grow timer) |
| Mod | Trailborne |
| Biome tier | Black Forest |
| Build menu | **Hammer** PieceTable (`m_category = Misc`; `m_craftingStation = null` — no bench in range). Hammer exception to design-pillars Pillar 1 (Daniel 2026-06-13) — it's a deployable convenience, not a trail mark. |
| Placement surface | **Solid earth only** (`m_groundOnly = true`) — ghost is valid on dirt/grass/rock terrain, REJECTED on any wood/stone floor or built piece (Daniel 2026-06-13: *"built on solid earth, not on structures"*). |
| Recipe (build cost) | **1 Portal Seed** (`SBPR_PortalSeed`, recoverable) → break returns the seed via vanilla `Piece.DropResources` on every destroy path |
| Durability | **300** HP (= 75% of vanilla portal's 400 — "more fragile"; Wood material; no rain decay for v1, `m_noRoofWear = true`). DECIDED by Daniel 2026-06-13 — supersedes the earlier fabricated "175" (never Daniel's word, retracted with the "150–200 lean"). |
| Geometry | **Horizontal overhead ring**, ~3 m tall × ~3 m wide; ring at the top → jump up into it. `TeleportWorldTrigger` BoxCollider repositioned horizontal/overhead (the main novel-geometry risk). |
| Function | Otherwise a **regular vanilla portal**: identical `ZDOVars.s_tag` pairing, identical teleport, **keeps the ore/metal ban** (`m_allowAllItems` left false; `Inventory.IsTeleportable` enforces). 🔴 **Requires registering the prefab hash in `Game.instance.PortalPrefabHash`** or it places + grows but never tag-pairs. ~15 s scale-lerp grow (ZDO-stamped plant time, relog-durable). **Proximity/connected effect matches vanilla** (issue 1, 2026-06-15): emission glow lerp (black↔HDR-orange) + `_target_found_red` "target found" shimmer within 3 m of a connected portal — see impl-spec §3.5.1. |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructPieceShell` + grafted script-free meshes: `portal_wood`→`small_portal` ring (rotated flat, ×0.71), `Greydwarf_Root`→`default` tendrils, `stubbe` legs. PLUS the `portal_wood`→`_target_found_red` EFFECT subtree (particles+light+audio+EffectFade, grafted via `Assets.GraftEffectSubtree` — kept, not stripped) wired to `TeleportWorld.m_target_found` for the connected-portal shimmer. **OMIT** the donor's PlayerBase EffectArea / GuidePoint / portal_destruction / LODGroup. |
| Patch surface | **None** — TeleportWorld + trigger + ZDO grow timer + PortalPrefabHash add are all non-Harmony component wiring. (If a patch creeps in, register it in `Plugin.Awake` or PatchCheck ERRORs.) |
| Status | SPEC LOCKED |
| Source spec | `docs/v2/planning/ancient-portal-impl-spec.md` §3 |

---

## Trailborne v3 (Swamp) — Sunstone Lens (solar-charged monster detection) + Iron Compass (HUD orientation)

> **Status: IMPLEMENTED 2026-06-16 (engineer-systems card t_2fd7bc7f).** Design (theme +
> material + sourcing): `docs/design/swamp-detection-item.md` (PR #144). Buildable spec:
> `docs/v3/planning/sunstone-lens-impl-spec.md`. The **first v3 Swamp-tier** content. A
> Trinket-slot accessory whose **durability is a solar battery**: recharges in clear
> daylight, drains at a fixed rate while worn, and reveals nearby hostiles via a HUD overlay
> while charged. Built on confirmed base-game APIs only (ADR-0001); reproduces the Rune Magic
> "rune of detection" *behaviour* from vanilla primitives (`BaseAI.IsEnemy`) — no third-party
> mod code. **logs-green ≠ playable** — Daniel verifies AT-LENS-* in-game.

### Items

#### Sunstone (material)

| Field | Value |
|---|---|
| Display name | Sunstone |
| Prefab name | `SBPR_Sunstone` — **save/wire contract, do NOT rename** |
| Type | `ItemDrop`, `ItemType.Material`, stack 20, weight 0.3 |
| Mod | Trailborne (v3) |
| Biome tier | Swamp |
| Craft station | **None — not crafted** (loot-sourced; see Source row below) |
| Source (loot — **NO craft recipe**) | **Loot-sourced only** — swamp **surface** chests (primary, ~15% per chest) + rare **Draugr Elite** drop (secondary, ~5% flat), shipped as `SunstoneLoot.cs` (card t_0445f590 / PR #183; spec `sunstone-loot-economy-impl-spec.md`). Daniel locked the rarity at 15% / 5% (card t_8f39b5fc). An earlier **provisional** Iron ×1 + Crystal ×2 Explorer's-Bench craft was a bridge until the drops shipped; **Daniel locked REMOVE** once they did (card t_c27f985e). EXCLUDES Sunken-Crypt chests. |
| Function | A **standalone resource** (Daniel's 2026-06-13 amendment) modelled after Iceland-spar / the Viking *sólarsteinn*. The Sunstone Lens is its **first consumer**, not its only use — future v3 crafts can draw on the same resource. 🔴 **NO Sunstone in vanilla** (0 wiki hits) — authored fiction, not a reskin. |
| Visual notes | Clones `Coins` (the established tiny-Material pattern, same as Pigments; ADR-0006 carves out tiny Material items the same way). Inventory icon `sunstone_v0.1.png` (v0.1 placeholder; falls back to the Coins donor icon if the PNG is missing). Note: the material has **no recipe manifest row**, so SpecCheck's C1 icon check does not cover it (C1 only runs on manifested item recipes — the Lens row covers the Lens). |
| Patch surface | Registration via `SunstoneLens.RegisterPrefabs` / `DoObjectDBWiring` (Registrar fan-out). No Harmony. Loot wiring via `SunstoneLoot.RegisterPrefabs` (DropTable + CharacterDrop append; PR #183). |
| Status | IMPLEMENTED (code + spec, card t_2fd7bc7f; loot economy card t_0445f590 / PR #183; provisional craft REMOVED card t_c27f985e). **No SpecCheck recipe row** (loot-sourced, not crafted). Build 0/0. **logs-green ≠ playable.** |
| Source spec | `docs/design/swamp-detection-item.md` + `docs/v3/planning/sunstone-lens-impl-spec.md` §6 |

#### Sunstone Lens (the detector)

| Field | Value |
|---|---|
| Display name | Sunstone Lens |
| Prefab name | `SBPR_SunstoneLens` — **save/wire contract, do NOT rename** (every crafted instance + the recipe key on it) |
| Type | `ItemDrop`, **`ItemType.Trinket` (= 24)** — the player's dedicated `m_trinketItem` slot (the Bog-Witch demister slot), SEPARATE from the Utility slot. 🔴 **Resolves the cross-card Utility-slot contention:** the Lens coexists with the Cartographer's Kit (Utility). It DOES share the Trinket slot with the future Iron Compass — a deliberate exploration-tool choice (wear threat-sense OR orientation, not both), flagged for Daniel, not pre-decided. |
| Mod | Trailborne (v3) |
| Biome tier | Swamp |
| Craft station | Explorer's Bench (`piece_sbpr_explorers_bench`) |
| Recipe (craft) | **Sunstone ×2 + Iron ×1 + Guck ×3 → 1.** Every material has a sentence: Sunstone = the solar core; Iron = the Swamp-tier frame AND the tier gate (Iron needs Sunken-Crypt scrap to smelt); Guck = the Swamp-surface housing/adhesive. `m_maxQuality = 1`. Sunstone referenced via the `SunstoneLens.SunstoneName` const (never a literal). |
| Function | **Energy = durability, modelled as a solar battery.** Recharges (durability ↑) ONLY in clear weather AND daylight AND not wet AND outdoors AND outside the Swamp (`EnvMan.IsDaylight`/`IsWet` + `EnvSetup.m_isWet` + `Player.InShelter` + biome ≠ Swamp). The Swamp is always-wet/overcast so it can **never charge there** — the "sun battery in the sunless mire" tension. Drains (durability ↓) at a **fixed rate while worn**, independent of how many hostiles are detected. While worn AND charged, reveals nearby **hostile** creatures (vanilla `BaseAI.IsEnemy` filter over `Character.GetAllCharacters` within a radius — tamed/friendly/players excluded) on a HUD overlay. At **zero charge it goes inert** (detection off) but is **NOT consumed/broken** and stays equipped; works again after recharging (AC#5). |
| Render | **HUD overlay** (`Hud.Awake` postfix → MonoBehaviour under `Hud.m_rootObject`), the Iron Compass doctrine (`nomap.md` §8). 🔴 **NOT minimap pins** — the SB server runs NoMap by default (NoMapEnforcer sets `GlobalKeys.NoMap`), so pins have no surface; a HUD overlay works regardless of map state. ✅ **TROPHY RING BUILT (card t_b8a19487, Daniel 2026-06-18/19):** the surface is a **camera-relative trophy RING** around the player — each hostile's TROPHY (from its `CharacterDrop` Trophy drop) on a fixed-radius screen ring at its bearing-relative-to-facing, **size ∝ proximity**, **vanilla nameplate star pips** (`GetLevel()-1`, sprite harvested from `EnemyHud.m_baseHud` level_2/level_3 — NOT Unicode ★), and **aggro-state colour tint** (🟡 idle / 🟠 aggroed on another player / 🔴 aggroed on YOU — the "Rune of Awareness" element, reproduced from vanilla `BaseAI.IsAlerted`/`GetTargetCreature`). Trophy-less hostiles wear a generic threat glyph (`threat_fallback_v0.1.png`). Empty (worn+charged, nothing near) → a **faint solar ring** outline (`ShowEmptyRing` default ON); depleted/not-worn → ring off. Camera-relative, NOT north-up (preserves the Iron Compass's exclusive north payoff). Supersedes the old bottom-center text line (now an optional `DebugTextReadout`, default off). Spec: `docs/design/sunstone-lens-trophy-ring.md`. |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructItemShell` builds the networked item skeleton from scratch (NEVER clones a vanilla item). World-drop mesh grafted as a ZNetView-free cosmetic child off the vanilla `Crystal` blueprint (`Assets.GraftVisualSubtree`, child `attach`). Inventory icon `sunstone_lens_v0.1.png` (v0.1 placeholder; icon MANDATORY — crafting UI indexes `m_icons[0]`; `ConstructItemShell` pre-seeds a magenta fallback + SpecCheck C1 screams if the real PNG didn't ship). Ring render is additive too (`new GameObject` + `AddComponent<Image>` under `Hud.m_rootObject`); trophy + star sprites are READ from vanilla prefabs (reading an asset is not cloning), the faint solar ring is a code-generated annulus. New asset: `threat_fallback_v0.1.png` (the trophy-less glyph, flat-packed via pack-modpack.sh, loaded by bare filename; not a manifested item so SpecCheck C1 ignores it). |
| Patch surface | **(1) Harmony Prefix on `Humanoid.DrainEquipedItemDurability(ItemData, float)`** (decomp :13227) — for OUR lens on the LOCAL player only: drains at the fixed rate or recharges in the sun, clamps to [0, max], returns false to SKIP vanilla so the break/unequip/destroy-at-zero branch is NEVER reached (AC#5). Pure pass-through for every other item / non-local player; fails OPEN. **(2) Harmony Postfix on `Hud.Awake`** — builds the client-only trophy-ring detection overlay. Both registered in `Plugin.Awake` (PatchCheck asserts they wove); inert on the dedicated server. Equipped-lens detection via public `Inventory.GetEquippedItems()` + `m_dropPrefab` name (`m_trinketItem` is protected). Ring reads `CharacterDrop`/`ItemType.Trophy`, `EnemyHud.instance.m_baseHud`, `BaseAI.IsAlerted`/`GetTargetCreature`/`Character.GetLevel` — all public base-game (ADR-0001 clean-side; Rune Magic *behaviour* reproduced from vanilla primitives, no third-party code read). |
| Config | `SunstoneLens` section (live-tunable): `MaxCharge` (100), `DrainPerSecond` (0.33 ≈ 5 min full→empty), `ChargePerSecond` (1.0 ≈ 1.7 min empty→full), `DetectRadius` (50 m), `DetectIntervalSeconds` (0.5), `ClearWeatherNames` (optional allowlist; empty = m_isWet-driven). **Ring render knobs (card t_b8a19487):** `RingRadiusPx` (180), `RingCenterOffsetY` (0), `RingIconMinPx` (28), `RingIconMaxPx` (64), `RingMaxIcons` (12), `ShowEmptyRing` (true — faint solar ring), `ShowDepletedHint` (false), `DebugTextReadout` (false). **Render-bug diagnostic (card t_d5949685):** `DebugMount` (true — logs overlay mount + visibility transitions + first-show placement; bake to false once the ring is confirmed in-game). |
| Status | IMPLEMENTED (code + spec + SpecCheck item row, card t_2fd7bc7f, 2026-06-16). **Trophy-ring render BUILT** (card t_b8a19487, 2026-06-19 — aggro-colour + vanilla stars + faint solar ring per Daniel). **HUD-render bug FIXED** (card t_d5949685, 2026-06-19 — the overlay rendered NOTHING worn-or-not: a self-deactivating-host dead `Update` pump, the same bug as the Iron Compass PR #208; `SetVisible` now toggles a `_content` child so the host stays active + `Update` keeps pumping. `DebugMount` diagnostic default ON. No builtin-resource fragility found — all sprite paths already degrade procedurally). Build 0/0; SpecCheck manifest unchanged (render-only). **logs-green ≠ playable** — AT-LENS-CHARGE/NOCHARGE-SWAMP/DRAIN-CONST/ZERO-INERT + AT-LENS-RING-1..5/AGGRO/CAMREL close only on Daniel's in-game check. |
| Source spec | `docs/v3/planning/sunstone-lens-impl-spec.md` §1-§5 |

#### Iron Compass (the orientation payoff)

| Field | Value |
|---|---|
| Display name | Iron Compass |
| Prefab name | `SBPR_IronCompass` — **save/wire contract, do NOT rename** (every crafted instance + the recipe key on it) |
| Type | `ItemDrop`, **`ItemType.Trinket` (= 24)** — the player's dedicated `m_trinketItem` slot (decomp :57652). SHARES the Trinket slot with the Sunstone Lens — a deliberate exploration-tool opportunity cost (wear orientation OR threat-sense, not both at once). Coexists with the Cartographer's Kit (Utility slot). |
| Mod | Trailborne (v3) |
| Biome tier | Swamp |
| Craft station | Explorer's Bench (`piece_sbpr_explorers_bench`) |
| Recipe (craft) | **Iron ×4 + Ooze ×2 + Red Pigment ×1 → 1** (Daniel Q1 LOCK 2026-06-17). Every material has a sentence: Iron = the Swamp-tier metal AND the tier gate (needs Sunken-Crypt scrap to smelt); Ooze = the Swamp Blob/Oozer drop, the wet resin bedding the needle; Red Pigment (`SBPR_InkRed`, via `Pigments.PigmentRedName`) = paints the north tip. `m_maxQuality = 1`, `m_useDurability = false` (the compass does not wear). Red Pigment referenced via the const (never a literal). |
| Function | **The earned no-map orientation payoff.** v1/v2 ship the map with **no north indicator** by design (`requirements.md:646`; cartography re-lock §2H.1); the Iron Compass is the tool that finally grants cardinal orientation — on a **separate HUD overlay**, *never* by adding a north arrow back onto the map (doing so would delete this item's reason to exist and reverse a Daniel-locked difficulty). While worn, a dial + red-tipped needle reads **true world north** relative to the camera yaw, with a **slight lag** (lerp-toward-target, Q3 — "a little lag is good"). Looking up/down tilts the dial face up to ~45° (Q4-tunable). No game-state mutation — reads `GameCamera.instance.transform` client-side every frame, writes nothing. |
| Render | **HUD overlay** (`Hud.Awake` postfix → `SBPR_CompassHud` MonoBehaviour under `Hud.m_rootObject`), the no-map orientation doctrine (`nomap.md` §8). 🔴 **NoMap-safe** — the default anchor `TopCenter` is independent of any minimap (the SB server runs NoMap by default), so the dial renders regardless of map state (AT-COMPASS-NOMAP-SAFE). The anchor is a **Config enum** (`CompassAnchor`) scaffolded from day one to extend to the carry-state Local Map disc (`BelowMapDisc`/`OnMapDiscOverlay`, t_7dd54899) and the future **Eye-of-Odin** global minimap; only `TopCenter` is wired in v1 (the others fall back to it with a one-time log until their dock targets exist). v0.1 dial + needle are procedural UGUI primitives (legible, zero art dependency — "you can tell it's a compass"); a polished authored sprite drops into the `Image` later. 🔧 **Render-bug fixed (t_61aff612, 2026-06-19):** visibility toggles a `_content` CHILD, never the host GameObject — deactivating the host killed the MonoBehaviour's `Update` pump and the overlay rendered NOTHING when worn (shared root cause with the Sunstone Lens overlay). Dial sprite is now a procedural disc (`DiscSprite()`) — the builtin `UI/Skin/Knob.psd` does not load on Valheim's 0.221.x Unity build. |
| Visual notes | **Additive (ADR-0006)** — `Assets.ConstructItemShell` builds the networked item skeleton from scratch (NEVER clones a vanilla item). The HUD overlay is additive too (`new GameObject` + `AddComponent<Image>`/`Text` under `Hud.m_rootObject`). Inventory icon `iron_compass_v0.1.png` (v0.1 placeholder — dark iron ring + parchment dial + red north needle; icon MANDATORY — crafting UI indexes `m_icons[0]`; `ConstructItemShell` pre-seeds a magenta fallback + SpecCheck C1 screams if the real PNG didn't ship). The held-trinket **world mesh is DEFERRED to v0.2+** (`requirements.md:696`) — placeholder item art is fine for v1 (a Trinket's in-world mesh is rarely seen; the overlay sprite is the art that matters, Q2). |
| Patch surface | **ONE Harmony Postfix on `Hud.Awake`** (`CompassHudBootstrapPatch`) — mounts the client-only overlay under `Hud.m_rootObject`; idempotent, server-gated (`OnSBServer`), fail-quiet, never fires on the dedicated server (no Hud). Registered in `Plugin.Awake` (PatchCheck asserts it wove — the unregistered-patch lesson). The item + recipe are otherwise **patch-free**. Equip-gate via public `Inventory.GetEquippedItems()` + Trinket-slot filter + `m_dropPrefab` name (the `CartographersKit.IsWearingKit` precedent — NOT a `HaveItem` carry-gate; `m_trinketItem` is protected). |
| Config | `IronCompass` section (live-tunable, the cairn-banner "client visual can't be verified headless" pattern): `NeedleLag` (8, range 0.5–30), `MaxTiltDegrees` (45, range 0–80), `Anchor` (enum, default `TopCenter`), `SizePx` (140, range 48–400), `OffsetXPx` (0), `OffsetYPx` (−94), `DebugMount` (bool, default **ON** for the t_61aff612 diagnostic cut — logs mount/wearing/anchor so a client log splits mount-fail from off-screen; bake to false once the dial is confirmed visible in-game). Daniel converges lag + placement in one joined session, then we bake the values into `SBPR_CompassHud.Default*`. |
| Status | IMPLEMENTED (code + spec + SpecCheck item row, card t_ee61472f, 2026-06-17). 🔧 **HUD-render bug fixed (card t_61aff612, 2026-06-19):** the overlay rendered NOTHING when worn — host-self-deactivation killed the `Update` pump; now toggles a `_content` child + procedural dial sprite + DebugMount diagnostics. Build 0/0. **logs-green ≠ playable** — AT-COMPASS-CRAFT/EQUIP-GATE/HEADING/LAG/TILT/NOMAP-SAFE/HUD-HIDE/VANILLA-ONLY/ART close only on Daniel's in-game check (needle direction, lag feel, anchor placement are GPU-client checks); the render fix's in-game accept is PENDING Daniel pulling a fresh cut. |
| Source spec | `docs/v3/planning/iron-compass-impl-spec.md` §0-§7 |

---

## Trailborne v1.1 (planned, not yet specced)

- Ember Lamps
- Beacons
- (Path Lamp upgrade tier — TBD)
- (Additional pigment colors — Yellow from cloudberry blocked until Plains-tier release)

---

## Future SBPR mods (not yet specced)

- **Guardian Stones** family — server worldbuilding (separate mod, separate spec)
- **Local Maps** + **Cartographer's Kit** (Trailborne v2 cartography — SPECCED, see `docs/v2/planning/`; the **Surveyor's Table** of this tier is now IMPLEMENTED — see the "Trailborne v2 (Black Forest)" section above)
- **Real Tents** (Trailborne v2)
- **Twisted Portal** (Trailborne v3 Swamp — the endgame no-restriction portal; distinct from the v2 Ancient Portal, which is the convenience portal that KEEPS the ore ban). **SPECCED — see `docs/v3/planning/twisted-portal-impl-spec.md`** (card t_f9cab392, blocked on Daniel). Two prefabs: the `piece_sbpr_twisted_portal` (Hammer-placed, solid-earth; teleports even with `NoPortals` set; paired by player-assigned RUNE NAMES in a dedicated `sbpr_rune_name` ZDO slot) + the `SBPR_TwistedKey` (a Trinket whose durability is a charge meter — eating food charges it, each teleport burns charge, Pukeberries dump-charge it fast). Consumes the shared v3 **Sunstone** material (second consumer after the Sunstone Lens). *(The "Pocket Portal" idea was rethemed + specced as the v2 **Portal Seed → Ancient Portal** above.)*
- **Iron Compass** (Trailborne v3 Swamp — the earned no-map orientation payoff). ✅ **IMPLEMENTED — see the "Trailborne v3 (Swamp)" section above** (card t_ee61472f, 2026-06-17; recipe Iron ×4 + Ooze ×2 + Red Pigment ×1 LOCKED per Daniel's Q1–Q4). A worn, Iron-gated **Trinket** (`SBPR_IronCompass`, crafted at the Explorer's Bench) whose **HUD overlay** finally grants cardinal orientation — *without ever touching the local map*. The map keeps its Daniel-locked no-north disorientation (cartography §2H.1); the compass is the **separate earned tool**. A `[HarmonyPatch(Hud,"Awake")]` postfix mounts an `SBPR_CompassHud` UGUI overlay under `Hud.instance.m_rootObject`; the needle is driven from `GameCamera.instance.transform.eulerAngles.y` (lerp lag = the "slight lag"), pitch mapped to a ~45° UI tilt, anchor is a Config enum (default `TopCenter`, NoMap-safe). Gated on **equip** (Trinket slot via `GetEquippedItems()`), not mere carry. Pure HUD-overlay + item — no game-state patches, no map mutation. Build 0/0; **logs-green ≠ playable** (Daniel gates the in-game playtest).
- **Seer's Stone** (Trailborne v4+)

---

## Maintenance discipline

1. **No piece/item ships without an entry in this file.** Add the row during spec finalization, before code is written.
2. **When a recipe changes, this file is updated in the same PR.** Spec docs and this dataset are co-canonical for catalog data.
3. **Prefab names must be unique across the SBPR namespace.** Check this file before naming.
4. **Status field is the ground truth for "is this shipping yet?"** When promoting from IN DESIGN → SPEC LOCKED → IMPLEMENTED → RELEASED, update here first.
5. **When self-hosted niflheim.wiki ships, this file becomes the wiki page-generation source.** Keep entries wiki-ready (display name, function, recipe, biome tier are the public-facing fields).
