---
title: "Trailborne v2 (Black Forest) — Cartography buildable implementation spec"
status: current
purpose: "Per-feature, build-ready implementation specs for the three v2 cartography features (Surveyor's Table, Local Map + viewer, Cartographer's Kit). Each section gives observable acceptance criteria, the exact vanilla hooks, the feature-folder it lands in, and its SpecCheck manifest row. Authored by the architect spec-pass (card t_4be278de) once all open items locked. Implementers (engineer-systems / engineer-ui) build from THIS doc; requirements.md is the what, this is the how-to-pick-it-up-cold."
---

# Trailborne v2 cartography — buildable implementation spec

The requirements doc ([`requirements.md`](requirements.md)) is the locked *what*. This
doc is the *buildable how*: one tight section per feature an implementer can pick up
cold, with the vanilla hooks carried forward from the design doc
([`../../design/cartography-v2.md`](../../design/cartography-v2.md)), observable
acceptance criteria, the feature-folder placement, and the SpecCheck manifest impact.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base game
> (`assembly_valheim`), which is fair game to read and adapt. The reference mods
> (`NomapPrinter`, `BetterCartographyTable`) are studied for *approach only* — reproduce
> behavior from vanilla primitives, never copy their code.
>
> **Spike dependency:** the viewer render path (Local Map §2B) is gated on the UI-fork
> spike (`t_e8bbbe48`) whose findings doc lands at
> `docs/v2/investigations/2026-06-10-bounded-map-ui-fork-spike.md`. Where this spec says
> "per the spike," the spike's confirmed `m_pixelSize` and render path are the build
> truth. If the spike returns BLOCKED, the Local Map viewer card re-specs against
> whatever wall it hit — the Table and Kit cards are independent of that risk.

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. Today it carries only the
v0.1.0 Meadows manifest. These three features add **+3 entries** (all new):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `piece_sbpr_surveyors_table` | build piece | Wood-Fine ×10, Bronze ×2, DeerHide ×4, BoneFragments ×8 | (place via Spade menu; `m_craftingStation = null`) |
| 2 | `SBPR_LocalMap` | item recipe (amount 1) | DeerHide ×1, FineWood ×1 | `piece_sbpr_explorers_bench` |
| 3 | `SBPR_CartographersKit` | item recipe (amount 1) | InkRed ×10, InkWhite ×10, InkBlue ×10, InkBlack ×10, FineWood ×4 | `piece_sbpr_explorers_bench` |

**Resource prefab-name caveats (must match vanilla / existing SBPR consts, or SpecCheck
flags a NULL `m_resItem`):**
- Fine Wood = vanilla internal id **`FineWood`** (the existing cairn-marker manifest row
  uses `FineWood`; confirm the Table piece uses the same string).
- Deer Hide = vanilla **`DeerHide`** (verified, wiki Internal ID + decomp item).
- Bone Fragments = vanilla **`BoneFragments`**. Bronze = **`Bronze`**.
- The four pigments are SBPR items whose **wire/prefab names are still the historical
  `SBPR_Ink{Red,White,Blue,Black}`** (see `Pigments.cs:40-43` — the consts say "Pigment"
  but the VALUES are `SBPR_Ink*` and must not change). Reference them via
  `Pigments.PigmentRedName` etc., never a literal.

Each impl card adds **only its own row** in the same PR as its code (spec-first rule:
code + spec + SpecCheck move together). The card that touches `SpecCheck.cs` first should
also update the class's `LOCKED SOURCE` comment to cite `docs/v2/planning/requirements.md`
alongside the v0.1.0 source, and generalize the "v0.1.0 locked manifest" wording.

**Asset-renderability is now part of the watchdog (icon-crash fix, C1).** `SpecCheck.Run()`
additionally asserts that every SBPR item recipe's resolved `ItemDrop` has its real icon
loaded — concretely that `m_icons[0]` is **not** the shared `Assets.FallbackIcon`
placeholder. This closes the "server-green recipes, client-icon-missing" divergence that
hid the Kit no-cost crash: an additive item with no loaded icon shows the magenta fallback
and the server now logs `ICON MISSING` at boot instead of silently shipping a crash. This
is an **asset check, not a recipe row** — the recipe-manifest count is unchanged (the icon
checks are tallied separately in the boot summary line).

> **Drift-watchdog gotcha:** `SpecCheck.Run()` iterates `Manifest.Where(s => s.Piece !=
> null)` for build pieces and `s.Item != null` for item recipes — a new `RecipeSpec`
> with both null (or both set) won't be checked. The Table is `Piece` only; the Local Map
> and Kit are `Item` only. Match that shape.

---

## 1. Surveyor's Table — placed station retaining a shared 1000 m survey

> **IMPL STATUS (2026-06-10, card t_2715661d, engineer-systems):** built additively on
> branch `feat/surveyors-table-t_2715661d`; build 0/0; SpecCheck row 1 added. Code +
> spec + manifest move together (this PR). **Two build-card deviations from the sketch
> below, flagged for review:** (a) §1.1 sketches "one `Switch` (Use → open viewer)"; the
> impl instead implements `Hoverable + Interactable` directly on `SurveyorTableTag` — the
> repo's proven in-production interactable pattern (`CairnInteractable`), avoiding a
> child-collider/`Switch`-delegate wiring layer for the same behaviour. (b) The forked
> VIEWER itself is the downstream card `t_7b616020` (engineer-ui), so this card ships the
> survey DATA + ZDO persistence + contribute/merge + ward gate + pin-removal BACKEND +
> a `CartographyViewer` seam the viewer plugs into; until that card lands, Use records +
> persists the survey and shows a "viewer not installed yet" message (no data lost). The
> windowed-fog cell math (`BoundedMapMath`) is the productionized, executed-proven spike
> seam (`t_e8bbbe48`). **logs-green ≠ playable — Daniel verifies in-game.**

**Lands in:** `Features/Cartography/SurveyorsTable.cs` (registration) + `SurveyorTableTag.cs`

### 1.1 Construction (ADR-0006 additive — hard constraint)
- `new GameObject("piece_sbpr_surveyors_table")` + `AddComponent` of exactly: `Piece`,
  `WearNTear`, `ZNetView`, one `Switch` (Use → open viewer), and a custom
  `SurveyorTableTag : MonoBehaviour`. **Do NOT `Instantiate` the vanilla `maptable`
  prefab** (it carries a `ZNetView`; cloning ZNetView-bearing prefabs is the ADR-0006
  anti-pattern that caused the v0.2.7 ZDO-orphan soft-lock).
- Read vanilla `maptable` (decomp `MapTable` :114014) as a **blueprint only** —
  `vprefab inspect maptable` for mesh/material/`EffectList`/field values; reference-copy,
  never instantiate. `ZNetScene.GetPrefab` fires no `Awake`, so reading is safe.

### 1.2 Placement + recipe (LOCKED)
- Placed via the **Trailblazer's Spade build menu** (Pillar 1 — never the Hammer).
  Register the piece onto the Spade's `PieceTable` the same way Signs / Path Lamp / Cairns
  do (see `Trailblazing.cs` / `Trailhead.cs` for the existing pattern).
- **`piece.m_craftingStation = null`** — NO Explorer's Bench required in range to place.
  This matches every existing Spade-placed SBPR piece (`Signs.cs:270`,
  `Trailhead.cs:186` set it null). Do not set a station.
- **Build cost (Piece.m_resources):** FineWood ×10, Bronze ×2, DeerHide ×4,
  BoneFragments ×8. Black-Forest tier.

### 1.3 Stored data — windowed, shared, cumulative (C5 + the over-provisioning fix)
- Persist the survey **compressed in the Table's ZDO** byte array `ZDOVars.s_data`
  (`Utils.Compress` / `Decompress`) — exactly as vanilla `MapTable` does, so save/load is
  inherited and the format stays interoperable.
- The stored blob is **NOT** vanilla's full-world `GetSharedMapData` 256² array. It is the
  **windowed fog array** (the shared §2C format): the ~32×32 native-resolution window
  around the Table's origin + the bound-origin world coord + the pin list, clipped to the
  Table's own 1000 m disc.
- **Write (any surveyor):** merge the writer's explored cells *that fall inside 1000 m of
  THIS table* + their shareable pins inside the disc into the stored window. Beyond-1000 m
  is dropped (C5 — a Table is a locally-bounded shared survey, not a global map). Owner
  persists via the Table's `ZNetView` (`InvokeRPC` to the owner like vanilla `MapTable`,
  or owner-side write if the interacting player owns the ZDO).
- **Cumulative:** writes OR-merge into the existing window, never overwrite (AT-TABLE-SHARED).

### 1.4 Use → open the viewer with pin editing (D4)
- The Table's `Switch.Interact` opens the **same forked viewer** the Local Map opens
  (§2B), bound to this Table's 1000 m disc — but operating on the Table's **SHARED** blob
  and with **pin REMOVAL enabled**. Field Local-Map view = read-only; Table view = edit.
- Pin removal hook: vanilla `Minimap.RemovePin(PinData)` (decomp :48408) and
  `RemovePin(Vector3 pos, float radius)` (:48366) via `GetClosestPin`. The forked viewer's
  Table mode wires a click → `RemovePin` against the Table's pin list (not the player's
  global `m_pins`). Per-pin-add sharing (C8) is the separate pin-sharing surface; removal
  here operates on the shared record.
- **Ward-gated:** every read/write/remove checks `PrivateArea.CheckAccess` (vanilla
  `MapTable` does this; reproduce it). A Table in a ward is locked to those with access.

### 1.5 Acceptance criteria (observable — close only on Daniel's in-game check)
- **AT-TABLE-SHARED** — two surveyors writing to one Table build a combined record of its
  1000 m disc; fog/pins beyond 1000 m of the Table are not stored.
- **AT-TABLE-PINEDIT** — the Table view permits pin removal on the shared data; the field
  Local-Map view does not.
- **AT-TABLE-PERSIST** — placed Table's recorded fog+pins round-trip across a dedicated-
  server restart (ZDO blob, compressed).
- **AT-TABLE-PLACE** — the Table places from the Spade menu with NO Explorer's Bench in
  range (no `m_craftingStation` block message).
- **AT-TABLE-WARD** — a Table inside a ward is read/write-locked to non-permitted players.
- SpecCheck row 1 present; `[hold]` PR; logs-green ≠ playable.

---

## 2. Local Map — two-handed item + bounded forked viewer

> **IMPL STATUS (2026-06-10, card t_cb831069, engineer-ui):** built FULL (not MVP) on
> branch `feat/local-map-viewer-t_cb831069` off `integ/v2-cartography`; build 0/0
> (`TreatWarningsAsErrors` ON); SpecCheck row 2 (`SBPR_LocalMap`) added; spec + code +
> manifest + dataset move together (this PR). Milestones: **M-A** = the item + equip/torch
> patch + binding controller; **M-B** = the forked viewer render (`MapViewer.cs`,
> productionized from the spike); **M-C** = pin-click removal + WorldPins-seam consumption +
> the fork's own open path. Every load-bearing vanilla signature was verified against
> `assembly_valheim.dll` before coding (EquipItem/UnequipItem/GetCurrentBlocker overloads,
> `ItemData.m_customData` round-trip, the nomap `SetMapMode` force, `AnimationState` nesting);
> the windowed-cell projection + its click-inverse are proven by a standalone math harness
> (8/8). **Two design clarifications flagged for review:** (a) §2A.4 says "equip + activate
> → active minimap"; under v1's `nomap` world key vanilla forces `Minimap` to `None` and
> keeps BOTH map roots off (decomp `SetMapMode :975`), so the viewer is a STANDALONE uGUI
> overlay (the spike's design) and equipping BINDS the map while the vanilla **"Map" button**
> (otherwise dead under nomap) ACTIVATES the full view — the fork owning its own open path,
> exactly as §2B requires. (b) The "active minimap shows ONLY its disc" is realized as the
> fork's own bounded full-view overlay, not a re-skin of vanilla's nomap-suppressed minimap
> circle. **logs-green ≠ playable — Daniel verifies the in-game pixel render + equip feel
> (F9/Map-key + in-hand) per AT-MAP-* below; the per-AT status table is in the PR handoff.**

**Lands in:** `Features/Cartography/LocalMap.cs` (item + recipe + `LocalMapItemTag`),
`Features/Cartography/LocalMapEquipPatch.cs` (the torch-exception equip patch),
`Features/Cartography/LocalMapController.cs` + `LocalMapBootstrapPatch.cs` (carry/equip
binding state machine), and `Features/Cartography/MapViewer.cs` (the forked viewer, shared
with the Table via the `CartographyViewer` seam).
**Card:** `t_cb831069` (engineer-ui). **The viewer is the tier's single biggest build-risk.**
**Depends on:** this spec (ItemType + recipe lock) **and** the UI-fork spike (`t_e8bbbe48`).

### 2A — The item, equip behavior, and the torch patch

#### 2A.1 Item + recipe (LOCKED)
- A craftable `ItemDrop` named `SBPR_LocalMap`, **blank when crafted** (no map data).
- **Recipe:** DeerHide ×1 + FineWood ×1, amount 1, **crafted at the Explorer's Bench**
  (`m_craftingStation = piece_sbpr_explorers_bench` via
  `RecipeHelpers.FindStation(Trailhead.ExplorersBenchName)` — the existing pattern in
  `Pigments.cs:143`, `Cairns.cs:268`). NOT crafted at the Surveyor's Table.

#### 2A.2 `ItemType` = `TwoHandedWeapon` (the decisive lock — decomp-grounded)
- Set `m_shared.m_itemType = ItemType.TwoHandedWeapon` (= 14). **Do NOT invent a custom
  enum value, do NOT use `Utility`.**
- **Why not a custom type:** `Humanoid.EquipItem` (decomp :13798–14011) is a closed
  `if / else-if` keyed on `m_itemType`, ending at `Trinket` (:13992) then falling to
  `if (IsItemEquiped(item))` (:14001) with **no default hand-slot branch**. A custom value
  matches nothing → never assigns `m_rightItem`/`m_leftItem` → the item never equips. You'd
  have to patch `EquipItem` to add a whole branch — strictly more surface for no gain.
- **Why `TwoHandedWeapon` is correct:** its branch (:13921–13932) already does the exact
  block-clear discipline the C3 lock demands — `UnequipItem(m_leftItem)` +
  `UnequipItem(m_rightItem)` + `m_hiddenRightItem = null` + `m_hiddenLeftItem = null`,
  then `m_rightItem = item`. True-unequip, never-hide, inherited for free.
  `IsTwoHanded()` (:58050) returns true → all two-handed gating falls out.
- **Suppress combat:** leave `m_shared.m_attack.m_attackAnimation` and
  `m_shared.m_secondaryAttack.m_attackAnimation` **empty** → `HavePrimaryAttack()` /
  `HaveSecondaryAttack()` (:58059/:58064) false → LMB/RMB do nothing, no block
  (AT-MAP-BLOCKCLEAR). Give it a benign animation state (the spike/impl picks a
  non-combat hold pose; fishing-rod-style is the closest vanilla precedent).
- **Activating the map as the minimap is NOT the attack path.** It's a separate
  equip-side hook (see §2B "binding"), so we don't repurpose `m_secondaryAttack`.

#### 2A.3 The torch exception (C12 — ships from the gate, one Harmony patch)
- The bare `TwoHandedWeapon` branch force-unequips the left hand including a Torch. To
  permit a lit map at night, **Harmony-patch `Humanoid.EquipItem`** so that *when the
  item being equipped is `SBPR_LocalMap`* it runs the TwoHandedWeapon eviction but then
  **allows a `Torch` back into `m_leftItem`** — mirror vanilla's torch-beside-one-handed
  special-case (:13846–13850 and the OneHandedWeapon-keeps-torch guard :13882).
- Discipline: shield / left-weapon are still hard-`UnequipItem`'d (never hidden); ONLY a
  `Torch` is allowed back. The block-clear guarantee holds whether or not a torch is up.
- This is the **only** Harmony patch the ItemType decision requires. Keep it scoped to our
  item (guard on the prefab name / a tag component) so it never alters vanilla two-handed
  weapons.

#### 2A.4 Binding durability (D1 / C3)
- **Minimap binding is durable while the item sits in inventory** (equipped or not) and
  **reverts to no-map the instant the item leaves inventory** (dropped/traded/destroyed).
  Hook inventory-changed; when no `SBPR_LocalMap` instance remains, drop the binding.
- **Full-screen view requires the map actively EQUIPPED** (two hands). Carrying it keeps
  the minimap bound; only equipping opens the full viewer.

#### 2A.5 Imprint + per-instance storage
- **Imprint** happens at a Surveyor's Table (§1): blank map → a **snapshot** of the
  Table's current windowed survey + the Table's bound-origin world coord. Not a live link.
- Store the windowed fog array + bound-origin coord **on the item instance**. Verify
  `ItemDrop.ItemData.m_customData` (a `Dictionary<string,string>`) exists and round-trips
  trade/drop **on our game version before relying on it** (flagged unknown — decomp it at
  build). **Fallback if absent:** a ZDO-backed "map case" carrier. The blob is the §2C
  windowed format, so one format serves item + Table + viewer.

### 2B — The forked viewer (productionize the spike's proof)

> **⚠️ RENDER PATH SUPERSEDED (2026-06-11, issue 6 → §2E).** Bullet 1 below originally
> specced the fork to "drive a custom RawImage from OUR windowed array" painted as a
> two-color fog mask. Daniel's v0.2.19-playtest report: that doesn't look/behave like the
> real map. **§2E is now the authoritative render path** — reuse a COPY of vanilla's map
> MATERIAL (the 4-texture shader composite) masked by our fog window. The fork SHELL,
> bounding, fixed zoom, and edge-arrow below are all UNCHANGED and still correct; only the
> per-pixel paint is replaced. Read §2E before touching the render.

> **⚠️ EXIT UX ADDED (2026-06-11, issue 7 → §2F).** §2B specced the viewer's own open/close
> path but never nailed down the *exit* UX. Two gaps closed in **§2F**: (1) Escape closes the
> viewer **and** leaks into vanilla's pause menu the same frame — fixed by gating `Menu.Show`
> through the shared `SignPanelInputBlock` so Escape "just works"; (2) no on-screen exit
> prompt — add a bottom-center "[Esc] Close map" label. Read §2F before touching the viewer's
> input handling or canvas build.

> **The spike (`t_e8bbbe48`) is the source of truth for the render path.** Build §2B
> against its findings doc — especially the confirmed `m_pixelSize` and which RawImage
> path renders cleanly. Everything below is the spec the spike validates; if the spike
> caveats or blocks any item, this section re-specs to match.

- **It's a FORK of the vanilla map UI**, not a reuse of the live map. Vanilla `Minimap`
  (decomp ~:46485+) builds `m_mapTexture` (:46894) and shows it via `m_mapImageLarge` /
  `m_mapImageSmall` RawImages on `m_largeRoot` / `m_smallRoot` (:46613–46619). The fork
  drives a custom RawImage from OUR windowed array — it does NOT feed our blob to
  `Minimap.AddSharedMapData` (which expects the 256² world array).
- **Hard 1000 m radius**, centered on the **bound origin** (the Surveyor's Table the map
  was imprinted at — NOT the player). Everything beyond 1000 m is permanent shroud and
  never reveals. Pins render only inside the disc.
- **Fixed zoom** on both the minimap circle AND the full-screen view — no scroll-to-zoom.
  One authored scale each.
- **No pinning interface in the field** (Local-Map view is read-only). The same viewer in
  **Table mode** (§1.4) enables pin removal; the mode flag is set by who opened it.
- **Player-outside-the-disc → edge indicator clamped to the 1000 m SHROUD RADIUS**
  (C1-corrected): project the off-disc player position onto the 1000 m circle and draw a
  direction arrow toward the bound Table. This is a **map-space clamp to the disc radius**,
  computed in map coords — **NOT** `Minimap.ClampToScreenEdge` (:34731), which is a
  screen-space ping clamp and the wrong precedent. (The spike sketches this; full polish
  here.)
- Must work / degrade gracefully under v1's map nerf (no M-key full map): the fork owns
  its own open/close, it does not rely on vanilla `Minimap.SetMapMode(Large)` being
  reachable by the M key.

### 2C — Fog storage format (the over-provisioning fix, C2-corrected)
- The fog is a **small array windowed to the 1000 m disc at the player auto-map's NATIVE
  pixel resolution** — NOT the full 256² world array, NOT a custom-resolution resample.
- Vanilla world fog: `m_explored` / `m_exploredOthers` are `bool[m_textureSize²]` with
  **`m_textureSize = 256`** (decomp :46692) and **`m_pixelSize = 64f`** (:46694), covering
  the ~16 km world. A 2000 m-diameter disc at 64 m/px ≈ a **~32×32 window** (~1,024 cells)
  — ~1.6 % of the 65,536-cell world array.
- **Resolution = whatever the player's auto-map actually uses — do NOT pick 8 vs 16 m/px**
  (C2 rejected the custom-resolution idea: the map imprints FROM the player's native fog,
  and a custom grid forces a lossy resample every imprint). **Confirm the real
  `m_pixelSize` at build** — the spike does exactly this; the personal auto-map may differ
  from the 64 m/px world-minimap default. Whatever the spike confirms is the build value.
- World→cell windowing uses vanilla `WorldToMapPoint` (:47977) / `WorldToPixel`; copy the
  sub-rectangle of cells around the bound origin out of the live `m_explored` at imprint.
  Stored = the windowed cell range + the bound-origin world coord (+ a resolution tag for
  forward-safety). The forked viewer renders THAT array directly, clipped to the disc.
- The walking-reveal source is vanilla `Minimap.Explore(Vector3, radius)` (:48015) →
  `Explore(x,y)` (:48036) writing `m_explored`; the Kit gate (§3) controls whether that
  write happens at all.

### 2D — Acceptance criteria (spec §6; close only on Daniel's in-game check)
- **AT-MAP-EQUIP** — equip the Local Map + activate → it becomes the active minimap
  showing ONLY its 1000 m disc.
- **AT-MAP-DURABLE** — binding persists while the item sits in inventory; reverts to
  no-map the instant it leaves inventory.
- **AT-MAP-BOUND** — nothing beyond 1000 m of the bound Table reveals; pins beyond 1000 m
  don't render.
- **AT-MAP-FIXEDZOOM** — neither minimap nor full view zooms; the field full view has no
  pinning interface.
- **AT-MAP-EDGEARROW** — player outside the disc → arrow clamped to the 1000 m circle
  pointing at the bound Table (map-space clamp, not screen edge).
- **AT-MAP-STORAGE** — the fog array is windowed to 1000 m at native resolution, not a full
  256² world array, not a resample.
- **AT-MAP-BLOCKCLEAR** — map equipped → RMB/block does nothing (no ghost shield block);
  unequip → weapon + shield return clean.
- **AT-MAP-TORCH** — map + left-hand torch coexist (lit map at night); still can't block or
  attack.
- **AT-TABLEMAP-1…7** (issue 6 correction, 2026-06-11) — the viewer must render vanilla
  cartography (biome/height/forest/water), not a two-color fog mask; see **§2E** for the
  named criteria + the locked route.
- **AT-VIEWEXIT-1…7** (issue 7, 2026-06-11) — the viewer must exit cleanly: Escape closes it
  WITHOUT also opening the pause menu, and a bottom-center "[Esc] Close map" prompt is visible;
  see **§2F** for the named criteria + the locked `Menu.Show`-prefix route.
- SpecCheck row 2 present; `[hold]` PR; logs-green ≠ playable.

### 2E — Vanilla-cartography render (issue 6 design correction, 2026-06-11)

> **Status: DESIGN CORRECTION.** Supersedes the "paint our own two-color texture" render
> path of §2B bullet 1 / the spike. The fork **SHELL** (own Canvas + open/close path, fixed
> zoom, fixed-radius shroud, pin + player-marker overlay, polar edge-arrow) is UNCHANGED and
> correct — **only the fog-paint step changes.** Reported by Daniel, v0.2.19-playtest, in
> game; applies to BOTH the Surveyor's Table view and the shared field Local-Map view (one
> `MapViewer`). Clean-side (ADR-0001).

**What Daniel reported (verbatim):** *"the surveyor's table does NOT appear to behave like
the regular map. It should be almost identical in behavior to the regular map, just with the
shroud at a fixed RADIUS. and the no zoom alterations."*

**Root cause (grounded).** `MapViewer.PaintFog` (`MapViewer.cs:137-165`) builds a literal
two-color `Texture2D` — `px[i] = fog[i] ? CParchment : CShroud` (`:158-159`, palette `:76-77`).
That is *all* the map shows: no biome color, no height relief, no forest, no water. The fork
was built standalone-from-scratch (PR #101) to avoid riding vanilla's nomap-suppressed
minimap — correct for the OPEN PATH, but the render went too far and discarded vanilla's
cartography wholesale.

**The decisive RE finding (this re-frames the card's "route 1").** There is **NO single
"vanilla map texture" to composite.** The vanilla map is a runtime **GPU shader blend of FOUR
textures**, allocated in `Minimap.Start` (`Minimap.cs:413-442`) and filled by
`GenerateWorldMap` (`:1639-1682`):

| Shader slot | Field | Format | Content (decomp) |
|---|---|---|---|
| `_MainTex`   | `m_mapTexture`        | RGB24 | per-cell biome base color (`GetPixelColor(biome)`, `:1754-1769`) |
| `_MaskTex`   | `m_forestMaskTexture` | RGBA  | forest stipple + ocean/ashlands gradient (`GetMaskColor`, `:1719-1752`) |
| `_HeightTex` | `m_heightTexture`     | RHalf | biome height → drives the hillshade relief |
| `_FogTex`    | `m_fogTexture`        | R8G8  | explored (R) / shared (G) mask (`Explore`, `:1555-1566`) |

…composited by the **custom shader on `m_mapImageLarge.material`** with uniforms `_mapCenter`
(world), `_pixelSize` (`= 200f / zoom`), `_zoom`, `_SharedFade` (set in `CenterMap :1023-1034`
and `Update :628-639`), and the `RawImage.uvRect` windowing the quad (`:1007-1021`). **The
"look of the map" lives in that shader, not in any one texture.** So "composite vanilla's real
map texture" is not a literal operation — the real operation is **reuse the vanilla map
MATERIAL and its four bound source textures.**

**These textures EXIST and are populated under `nomap`** — this answers the card's crux
question. `nomap` only forces `SetMapMode → MapMode.None`, which toggles the UI **roots** off
(`:961-966`). It does **not** gate generation: `Minimap.Update` runs the generation block
(`:556-568`) and `UpdateExplore` **unconditionally every frame, before any mode/`m_noMap`
check** — already VERIFIED by the Cartographer's Kit card (§3 IMPL STATUS: *"personal fog
accumulates even under v1's server-side nomap"*). Therefore `m_mapTexture` /
`m_forestMaskTexture` / `m_heightTexture` are generated, and the **public**
`Minimap.instance.m_mapImageLarge.material` carries all four textures bound (via
`SetTexture` in `Start :435-438`) — readable at runtime, clean-side.

**LOCKED ROUTE — material reuse (the card's route 1, corrected for the no-single-texture
reality). Route 2 — "drive vanilla's renderer in bounded mode" — is REJECTED:** re-enabling
roots risks AT-TABLEMAP-6 and entangles us with the nomap suppression the fork exists to
avoid.

1. **Keep the entire fork shell. Delete only the two-color paint.**
2. **Render through a COPY of the vanilla map material:**
   `var mat = UnityEngine.Object.Instantiate(Minimap.instance.m_mapImageLarge.material);`
   The copy inherits the four texture bindings (shared by reference) + the shader. Apply it
   to the viewer's existing map `RawImage`; set `RawImage.texture = mat.GetTexture("_MainTex")`
   so the RawImage has a valid main texture. Drive **our copy's** `uvRect` +
   `_mapCenter`/`_pixelSize`/`_zoom` to frame the bound origin's 1000 m disc at our single
   fixed scale. We never touch vanilla's own material/roots → **AT-TABLEMAP-6 holds by
   construction.**
3. **Shroud = OUR fog window, NOT vanilla's `_FogTex`.** Composite `SurveyData.Fog`
   (explored-AND-in-disc, already produced by `BoundedMapMath.BuildWindowedFog`) as a
   disc+explored alpha mask OVER the cartography: lit → real map, unlit / beyond-radius →
   opaque shroud. This realizes the ONE deliberate difference (fixed radius). Implementer's
   choice: overlay a mask `RawImage` (simplest, fully decoupled) **or** build a window
   texture and bind it as `_FogTex` on the copy (reuses vanilla's fog-edge fade) — visual
   polish, Daniel verifies.
4. **Fixed zoom:** one authored window; no scroll/zoom input (keep `LayoutMapRect`'s
   no-scroll discipline). Disc span = 2000 m ≈ `2000/(m_textureSize*m_pixelSize)` =
   `2000/16384 ≈ 0.122` normalized (`uvRect.width`, × aspect). The matching `_zoom`/
   `_pixelSize` uniform values are **build-calibrated** against the live render (see spike).
   Preserves AT-MAP-FIXEDZOOM.

**NO SurveyData wire-format change** (answers card open-Q2). The biome/height/forest textures
are **global and deterministic from the world seed** — vanilla regenerates them at `Start`
for the whole ~16 km world (`m_textureSize=256`, `m_pixelSize=64f`, `Minimap.cs:211/213`)
independent of exploration. The viewer **samples them live** at render time using the stored
bound-origin + radius window. `SurveyData` keeps carrying ONLY the bool fog window + pins → no
ZDO contract change, placed Surveyor's Tables do not orphan → **AT-TABLEMAP-7 by
construction.** (Local Map = same engine: live global cartography masked by the item's frozen
snapshot fog window = "the map as it was drawn," now with real terrain. Static terrain +
snapshot shroud is correct.)

**Graceful degradation (mandatory).** If `Minimap.instance == null` or `mat.GetTexture("_MainTex")`
is null at open (generation not yet run), fall back to the current two-color paint rather than
render blank. **Keep `PaintFog` as the fallback path** — do not delete it.

**Mandatory pre-build micro-spike (de-risks the one unverifiable piece — the shader).** The
shader is a GPU asset; its exact `uvRect`-vs-`_mapCenter`/`_pixelSize` sampling semantics
cannot be confirmed from the C# decomp alone. Before the full integration the engineer MUST,
in-client under nomap: instantiate the vanilla map material onto a throwaway RawImage, set
`.texture = mat.GetTexture("_MainTex")`, drive `uvRect`/`_mapCenter`/`_zoom` to a known world
window, and confirm it renders biome cartography (not blank / magenta / clipped). Lock the
calibration constants from that spike — exactly as the original UI-fork spike (t_e8bbbe48)
locked `m_pixelSize`. **If the material cannot be driven this way, BLOCK and re-route — do NOT
silently ship the two-color mask as the shipped behavior.**

**Clean/dirty:** Clean-side (ADR-0001). Reading `Minimap.instance.m_mapImageLarge.material`
plus its bound base-game textures and instantiating a copy is reusing the game we mod at
runtime — the same model as the already-shipped vanilla-UI-sprite/font reuse
(`requirements.md:353`). No decompiled IronGate source is copied into our code; no asset files
are committed; no third-party mod code is touched.

#### 2E acceptance tests (named, observable — close only on Daniel's in-game check)
- **AT-TABLEMAP-1** — the Table map shows the SAME cartographic content as the vanilla map
  for the explored area (biome color + height relief + forest + water), not a two-color mask.
- **AT-TABLEMAP-2** — unexplored cells AND everything beyond the fixed 1000 m radius render as
  opaque shroud (the one intended difference from vanilla).
- **AT-TABLEMAP-3** — no zoom controls; fixed scale (preserves AT-MAP-FIXEDZOOM).
- **AT-TABLEMAP-4** — pins + the polar edge-clamp arrow render at correct positions (preserves
  the existing overlay + `BoundedMapMath.EdgeClampToDisc`).
- **AT-TABLEMAP-5** — open/close, pin display, player marker feel like the vanilla map within
  the bounded disc.
- **AT-TABLEMAP-6** (nomap intact) — the player's world minimap is NOT re-enabled; v1 nomap
  stays in force everywhere except inside our bounded viewer.
- **AT-TABLEMAP-7** (regression) — Local-Map view (same engine) still works; no SurveyData
  wire change, so placed Tables don't orphan.
- logs-green ≠ playable — Daniel confirms in-game it looks/behaves like the real map, bounded.

**Implementation card:** routed to `engineer-ui` (owns `MapViewer.cs`, built it under
t_cb831069), as a child of the issue-6 card. **SpecCheck impact: none** (render behavior, not a
recipe row). Spec + code move together in that PR.

---

### 2F — Viewer exit UX: suppress the Escape→menu leak + show an exit prompt (issue 7, 2026-06-11)

> **Status: BUG + small UX gap.** Reported by Daniel, v0.2.19-playtest, in game. Closes a gap
> §2B left open (the viewer owns its open/close path but the *exit UX* — menu suppression +
> discoverability — was never nailed down). The fork **SHELL, render (§2E), bounding, zoom,
> overlay are all UNCHANGED** — this adds an input-gate hook + one UI label. Applies to BOTH the
> Surveyor's Table view (TableEdit) and the shared field Local-Map view (FieldReadOnly) — one
> `MapViewer`. Clean-side (ADR-0001). **SpecCheck impact: none** (input/UI, no recipe row).

**What Daniel reported (verbatim):** *"issue 7 no clear mechanism to exit the surveyor's table
map viewing mode. escape does exit, but also pulls up the game menu. There should be a prompt at
the bottom for how to exit, or escape should 'just work' without opening the game menu."*

#### 2F.1 Two defects, and a correction to the card's premise

**Defect 1 — Escape closes the viewer AND opens the vanilla pause menu the same frame.**
`MapViewer.Update` (`MapViewer.cs:387-399`) does `if (Input.GetKeyDown(KeyCode.Escape)) Close();`.
The viewer closes — but the *same* Escape keypress also reaches vanilla's menu handler that
frame, so the pause menu opens too.

The vanilla gate, grounded (decompiled `assembly_valheim.dll`, `Menu.Update`):

```
// Menu.Update, the "menu not already visible" branch (decomp):
bool flag = !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Console.IsVisible()
         && !TextInput.IsVisible() && !ZNet.instance.InPasswordDialog()
         && !ZNet.instance.InConnectingScreen() && !StoreGui.IsVisible()
         && !Hud.IsPieceSelectionVisible() && !UnifiedPopup.IsVisible()
         && !PlayerCustomizaton.IsBarberGuiVisible() && !Hud.InRadial();
if ((ZInput.GetKeyDown(KeyCode.Escape) || /* JoyMenu… */) && flag && !Chat.instance.m_wasFocused)
    Show();
```

Vanilla opens the pause menu on Escape **only when `flag` is true** — i.e. when *no* recognized
modal UI is up. Our viewer is a standalone uGUI overlay that satisfies **none** of those
predicates, so from `Menu`'s view nothing is open → `flag` stays true → Escape does double duty.

> **⚠️ The card's premise is half-wrong — corrected here (verified on `SignPanelInputBlock.cs`).**
> The card states the viewer *"does NOT route through `SignPanelInputBlock` or any equivalent."*
> **It already does.** `SignPanelInputBlock.AnyOpen` (`:41-44`) reads
> `SignPaintPanel.IsOpen || MarkerSignPanel.IsOpen || CartographyViewer.IsViewerOpen` — the
> viewer was wired in at build (card t_cb831069). All three `SignPanelInputBlock` patches
> (`Player.TakeInput`, `PlayerController.TakeInput`, `GameCamera.UpdateMouseCapture` — `:46-81`)
> already fire while the viewer is open. So the viewer is NOT missing character-input blocking,
> camera-look freeze, or cursor release — those work. **The single real gap is that none of those
> three seams touch the Escape→`Menu.Show` path.** That gate is what leaks.
>
> **Second correction: `MarkerSignPanel`/`SignPaintPanel` are NOT a working reference to copy —
> they have the *identical* leak.** Both also raw-poll `Input.GetKeyDown(KeyCode.Escape)` in their
> own `Update` (`MarkerSignPanel.cs:96`, `SignPaintPanel.cs:144`) and both route through the same
> `AnyOpen` that does *not* suppress `Menu.Show`. The reason the leak wasn't reported on the sign
> panels is incidental (they're smaller, dismissed faster, less obviously "modal"). The fix below
> closes the leak for **all three SBPR modal UIs at once** via the shared helper — which is also
> why AT-VIEWEXIT-5 is "fix the panels too," not "make the viewer match the panels."

**Defect 2 — no on-screen exit prompt exists.** Nothing in `EnsureCanvas`/`Render`
(`MapViewer.cs:463-516`) builds an instructional label. The overlay shows map + pins + player
marker but never tells the player how to leave.

#### 2F.2 Fix Defect 1 — suppress `Menu.Show` while any SBPR modal UI is open (Daniel's route a)

Daniel's preferred outcome is *"Escape just works"* — the viewer closes and the menu does NOT
open. Realize it by making the **shared** `SignPanelInputBlock` also gate the one seam it
currently misses: vanilla's pause-menu open.

**Seam = `Menu.Show()` (Harmony Prefix, skip-original when `AnyOpen`).** Grounded choice:

- `Menu.Show()` is a **single parameterless public instance method** (decomp `Menu.cs:212`); its
  **only internal caller is the Escape/JoyMenu gate** in `Menu.Update` (decomp `:366`). Prefixing
  it to early-return (skip original) while `SignPanelInputBlock.AnyOpen` is true cleanly prevents
  the pause menu from opening on the same Escape that closes our viewer — **without** consuming the
  keystroke globally or touching any other input path.
- **Why a `Menu.Show` prefix and NOT the `Minimap.IsOpen()` predicate (rejected route):** making
  our viewer report through `Minimap.IsOpen()` would satisfy `flag`, but `Minimap.IsOpen()` is
  referenced in **~10 vanilla gates** (build placement, crafting, interact, camera, attach-point —
  verified by grep over the decompiled assembly). Hooking it to return true while our overlay is up
  would silently alter all of them (e.g. suppress build/craft input) → wide, surprising blast
  radius. `Menu.Show` has exactly one caller and one effect. **Lock: `Menu.Show` prefix.**
- **Scope is self-clearing (AT-VIEWEXIT-3).** The gate keys on `AnyOpen`, which is false the moment
  the viewer/panel closes. The very Escape that closes the viewer is swallowed for the menu *that
  frame*; the *next* Escape (viewer now closed → `AnyOpen` false → prefix passes through) opens the
  menu normally. We never permanently eat Escape.
- **Unify, don't fork.** Add the prefix as a **fourth nested patch container inside
  `SignPanelInputBlock`** (e.g. `MenuOpenSuppressPatch`), gated on the same `AnyOpen`. This is the
  "one shared SBPR modal-input path" the card recommends — and because `AnyOpen` already includes
  all three surfaces, it fixes the viewer **and** the sibling `MarkerSignPanel`/`SignPaintPanel`
  Escape→menu leak in the same stroke (AT-VIEWEXIT-5), with zero new per-surface code.

**Registration (load-bearing — the PatchCheck lesson).** Each nested `[HarmonyPatch]` container
must be handed to `harmony.PatchAll(typeof(...))` individually in `Plugin.Awake()` — exactly as the
existing three `SignPanelInputBlock.*` containers are (`Plugin.cs:258-260`). A new nested patch that
is authored but never registered compiles, ships, and silently does nothing; `Runtime/PatchCheck.cs`
will ERROR-log it at boot, but the engineer must add the `PatchAll` line so it's actually woven.

**Server-safe by construction.** `AnyOpen` is false on a dedicated server (no local Player → no
panel/viewer ever opens), so the prefix is pure pass-through there — same inertness discipline as the
existing three patches (`SignPanelInputBlock.cs:30-33`).

**Keep the viewer's own `Close()` on Escape** (`MapViewer.cs:395-398`) — that's the half that
works. The new prefix only stops the *menu* from also opening. (Equivalent for the panels'
`Hide()`.) Belt-and-suspenders note for the implementer: the `Menu.Show` prefix is the load-bearing
fix; do not *also* try to consume the key via `Input`/`ZInput` reset — one clean seam, not two.

#### 2F.3 Fix Defect 2 — exit prompt label in the viewer canvas

- Add a bottom-center `Text` label to the viewer's canvas (built once in `EnsureCanvas`, parented
  to `_root` so it toggles with the overlay), e.g. **"[Esc] Close map"**.
- **Prompt key token — literal `[Esc]`, NOT a `$KEY_` bound-key token (corrects the card's open
  question).** The card recommends a bound-key token "for rebind-correctness, consistent with
  t_7816c0b0." **That is wrong for THIS key:** Escape in vanilla is a **hardcoded
  `KeyCode.Escape`** (23 call-sites across the decompiled assembly, including the `Menu` gate
  itself) — it is **never** registered as a rebindable `ZInput` button, so there is no `$KEY_`
  token that resolves to it and no rebind to stay correct against. A `$KEY_…` here would leak as a
  literal unresolved token (the exact 2026-06-05 bug `CairnInteractable.cs:58-65` documents). Use
  the literal `[Esc]`. The `$KEY_` token idiom remains correct for **bindable** actions (e.g. the
  `$KEY_Use` interact prompt on `SurveyorTableTag.cs:92`).
- **TableEdit mode — surface the pin-removal affordance too.** In TableEdit the viewer already
  does left-click-removes-pin (`MapViewer.cs:404`, gated on `_req.Mode == TableEdit && PinEditor != null`).
  Extend the prompt line in that mode only, e.g. **"[Esc] Close map    [Left-click] Remove pin"**.
  FieldReadOnly mode shows just the close hint (no pin editing there). The click verb is a bindable
  action — if a token is used for it, `$KEY_Use`-style localization via `Localization.instance.Localize`
  is the correct idiom (mirror `CairnInteractable.cs:58-65`); plain "[Left-click]" is also acceptable
  since left-click for UI interaction isn't a Trailborne-rebound action.
- **Skin/degrade like the panels.** Reuse the shared `VanillaUISkin.Font` and a flat-color fallback
  (same discipline as `MarkerSignPanel`/`SignPaintPanel`), so the label wears the native look and
  degrades gracefully if the skin donor is absent. Visual polish (placement, size, drop-shadow) is
  Daniel's in-game call.

#### 2F.4 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-VIEWEXIT-1** — With the Surveyor's Table viewer open, Escape CLOSES the viewer and does
  **NOT** open the pause menu.
- **AT-VIEWEXIT-2** — A clear exit prompt is visible while the viewer is open (bottom-center,
  e.g. "[Esc] Close map").
- **AT-VIEWEXIT-3** — After Escape closes the viewer, a **subsequent** Escape opens the pause menu
  normally (suppression is scoped to while-a-modal-is-open; Escape is never permanently eaten).
- **AT-VIEWEXIT-4** — Same clean exit for the field **Local-Map** viewer (shared `MapViewer`
  engine), both prompt and menu-suppression.
- **AT-VIEWEXIT-5** (consistency) — `MarkerSignPanel`'s Escape (`:96`) and `SignPaintPanel`'s
  Escape (`:144`) likewise no longer leak the pause menu — fixed in the same pass via the shared
  `SignPanelInputBlock` gate (they share the identical pre-fix leak; this is a fix, not a
  match-the-reference).
- **AT-VIEWEXIT-6** (no regression) — The viewer's own inputs still work while open: TableEdit
  left-click pin removal (`:404`), pin display, player marker, edge-arrow. Menu suppression must
  not block the viewer's interactions. The new `Menu.Show` prefix must NOT suppress the pause menu
  during normal play when no SBPR modal is open (`AnyOpen` false → pass-through).
- **AT-VIEWEXIT-7** (registration) — `PatchCheck` reports the new nested patch container as
  registered at boot (no UNREGISTERED PATCH CLASS error) — i.e. `Plugin.Awake()` actually
  `PatchAll`'d it.
- logs-green ≠ playable — Daniel confirms in-game: Escape closes cleanly with no menu pop, and the
  exit prompt is visible.

#### 2F.5 Routing + dependency note

- **Clean-side → `engineer-ui`** (owns `MapViewer.cs` + the sign panels + `SignPanelInputBlock`).
  Hooking `Menu.Show` / vanilla input gates is base-game (ADR-0001, fair game); no third-party mod
  code.
- **Lands in:** `Features/Signs/SignPanelInputBlock.cs` (new nested `Menu.Show` prefix container),
  `Plugin.cs` (its `PatchAll` registration), `Features/Cartography/MapViewer.cs` (exit-prompt
  label in `EnsureCanvas` + mode-aware text). No `SurveyData`/wire change.
- **Shares `MapViewer.cs` with the issue-6 render card (§2E).** Both edit the viewer. They are
  **separable** (this touches `EnsureCanvas`'s UI build + an input patch; §2E touches `PaintFog`/the
  render material) but if both run concurrently they will both modify `MapViewer.cs`. **Sequence
  recommendation:** land §2E (render) first or assign **both to the same `engineer-ui` worker** so
  the exit-prompt label and the material-reuse render land without a merge conflict on the same
  file. Note the dependency on the implementation card.

---

## 3. Cartographer's Kit — Utility-slot accessory that gates auto-mapping

> **IMPL STATUS (2026-06-10, card t_65fcfe5c, engineer-systems):** built additively on
> branch `feat/cartographers-kit-t_65fcfe5c` off `integ/v2-cartography`; build 0/0
> (`TreatWarningsAsErrors` ON); SpecCheck row 3 added; code + spec + manifest + dataset
> move together (this PR). **Construction:** new `Assets.ConstructItemShell` (the ADR-0006
> item analogue of `ConstructPieceShell`) builds the networked item skeleton (ZNetView +
> ZSyncTransform + Rigidbody + collider + ItemDrop with a FRESH `SharedData`) from scratch —
> it does NOT clone a vanilla item (the pre-ADR Pigments/cairn-marker pattern). World-drop
> mesh grafted off the vanilla `LeatherScraps` blueprint. **The gate is exactly §3.2's hook:**
> a Harmony **Prefix on `Minimap.UpdateExplore(float, Player)`** returns `false` (skips the
> fog write) unless the local player wears the Kit. **Spike claim VERIFIED against the decomp:**
> `Minimap.Update` calls `UpdateExplore` *unconditionally* every frame (`:47056`), BEFORE any
> map-mode/`Game.m_noMap` check, so personal fog accumulates even under v1's server-side nomap
> config — gating `UpdateExplore` is the correct, sufficient boundary. **One detection
> deviation flagged for review:** `Player.m_utilityItem` is `protected`, so the Kit is detected
> via the PUBLIC `Inventory.GetEquippedItems()` + `m_dropPrefab` name (the same pair vanilla
> uses at `VisEquipment` wiring, `:14158`) rather than reading `m_utilityItem` — same intended
> boundary, public API. **Coupling note for the viewer card (t_cb831069):** this gate controls
> whether vanilla writes `m_explored` at all; the viewer READS that same `m_explored` window.
> One fog-write model — the Kit is the write-gate, the viewer is the reader; not forked.
> **logs-green ≠ playable — Daniel verifies AT-KIT-* in-game.**

**Lands in:** `Features/Cartography/CartographersKit.cs` (item + recipe + the gate patch).
**Card:** `t_c871efec` (engineer-systems). Smaller / lower-risk than the viewer.
**Depends on:** this spec (recipe already locked; confirm the gate hook).

### 3.1 Item + recipe (LOCKED)
- An equippable `ItemDrop` named `SBPR_CartographersKit`, **`m_shared.m_itemType =
  ItemType.Utility`** (= 18, decomp :57646) — the SAME slot as Megingjord / Wishbone,
  written to the player's dedicated `m_utilityItem` (`EquipItem` Utility branch :13983).
  Coexists with any weapon / shield / map; never a hand item.
- **Recipe (LOCKED, C11):** InkRed ×10 + InkWhite ×10 + InkBlue ×10 + InkBlack ×10 +
  FineWood ×4, amount 1, crafted at the Explorer's Bench. Reference pigments via
  `Pigments.Pigment{Red,White,Blue,Black}Name` (values are `SBPR_Ink*`).
- **NO discovery-flag system (C10).** It's a normal recipe surfaced the vanilla way
  (`IsKnownMaterial` — appears once the player has encountered its ingredients). The
  40-pigment cost IS the gate. Do **not** build any "discovered all 4 pigments" tracking.
- **Loadable icon is a HARD requirement, not cosmetic.** The Kit is the only
  additively-constructed item (`Assets.ConstructItemShell`, fresh `SharedData` → empty
  default `m_icons`). Vanilla `ItemDrop.GetIcon()` (`ItemDrop.cs:623-625`) indexes
  `m_icons[m_variant]` with no bounds guard, so an empty `m_icons` throws
  `IndexOutOfRangeException` in the crafting panel on selection and aborts the cost repaint
  (the "no cost, inherits the previous selection" symptom). `ConstructItemShell` MUST
  guarantee a non-empty `m_icons` via a shared fallback sprite (`Assets.FallbackIcon`, a
  code-generated magenta placeholder — no disk dependency) so a missing icon degrades to a
  visible placeholder, never a crash. The Kit ships `cartographers_kit_v0.1.png`; if it
  fails to load, the item shows the magenta fallback and SpecCheck logs `ICON MISSING` at
  server boot.

### 3.2 The auto-mapping gate (the whole point)
- **With the Kit in the Utility slot, walking reveals fog; without it, ZERO passive
  reveal.** Gate the vanilla walking-reveal behind "is `SBPR_CartographersKit` the player's
  equipped `m_utilityItem`?"
- **Exact hook (confirmed):** Harmony-patch **`Minimap.UpdateExplore(float dt, Player
  player)`** (decomp :48005) — this is the per-interval driver that calls
  `Explore(player.transform.position, m_exploreRadius)` (:48011) every `m_exploreInterval`.
  A **Prefix returning `false` when the local player has no Kit equipped** cleanly no-ops
  the fog write for that tick (nothing reveals) while leaving everything else untouched.
  - Prefer patching `UpdateExplore` over `Explore(Vector3,float)` (:48015): `UpdateExplore`
    is the single gated entry point; `Explore` is also reachable from
    `ExploreOthers`/shared-data merges (:48823 path) which we do NOT want to gate (reading
    a Table's shared fog must still work without the Kit). Gating `UpdateExplore` targets
    *only* the personal walking-reveal — exactly the intended boundary.
  - Guard the patch on `player == Player.m_localPlayer` and a null-safe `m_utilityItem`
    name check (or a tag component on our item) so it only affects the local walking-reveal.
- **v1 nomap interaction:** under v1 the M-key map is gone but personal fog still
  accumulates; the gate makes that accumulation Kit-dependent. Confirm in-game that with no
  Kit, walking adds nothing to the personal auto-map (and therefore nothing imprintable at
  a Table); with the Kit, it accumulates normally.

### 3.3 Acceptance criteria (spec §6; close only on Daniel's in-game check)
- **AT-KIT-ICON** — On a CLIENT, selecting the Kit in the Explorer's Bench renders an icon
  and its full cost panel (10× each pigment R/W/B/K + 4 FineWood) with **no exception** in
  `LogOutput.log` at selection time. Deleting the Kit's icon PNG and rebooting yields the
  magenta placeholder icon + an intact cost panel (never a panel crash) AND a
  `[Trailborne/SpecCheck] ICON MISSING` ERROR at server boot. Restoring the PNG → green.
- **AT-KIT-GATE** — Kit worn → walking reveals fog; Kit absent → walking reveals ZERO fog.
- **AT-KIT-RECIPE** — crafts from 10×(R/W/B/K) + 4 FineWood at the Explorer's Bench,
  surfaced as a normal recipe (no discovery flag).
- **AT-KIT-COEXIST** — sits in the Utility slot alongside weapon / shield / Local Map with
  no slot collision.
- SpecCheck row 3 present; `[hold]` PR; logs-green ≠ playable.

---

## 4. Build order, cross-feature seams, and the shared format

- **Build order (lowest → highest risk):** Cartographer's Kit (§3, smallest — one gate
  patch + a Utility item) → Surveyor's Table (§1, re-gated `MapTable` loop on public APIs)
  → Local Map item + equip (§2A) → **the forked viewer (§2B), the one high-risk item**, and
  it is gated on the spike. The three impl cards are children of THIS spec + the spike.
- **The shared windowed-fog blob (§2C) is the seam** between all three: the Kit writes the
  player's native fog, the Table stores a windowed merge of it, the Local Map snapshots the
  Table's window, and the viewer renders that window. One format, defined once in
  `Features/Cartography/` — agree on it before §1 and §2 diverge.
- **The viewer (§2B / `MapViewer.cs`) is shared** by the Local Map (read-only field mode)
  and the Surveyor's Table (pin-removal Table mode). Build it once with a mode flag; don't
  fork two viewers.
- **All clean-side / ADR-0006:** additive construction, vanilla read as blueprint, reference
  mods studied not copied, no decompiled IronGate source committed.
- **Spec-first:** each impl card moves its `requirements.md` cross-check + its `SpecCheck.cs`
  manifest row + its code in the same PR. A card is done when code, spec, and the SpecCheck
  manifest agree — and (logs-green ≠ playable) only when Daniel verifies it in-game.

## 5. Naming reference (prefab / id strings — agree before building)
| Thing | Prefab / id (proposed; confirm at build) | Type |
|---|---|---|
| Surveyor's Table | `piece_sbpr_surveyors_table` | build piece |
| Local Map | `SBPR_LocalMap` | `ItemDrop`, `TwoHandedWeapon` |
| Cartographer's Kit | `SBPR_CartographersKit` | `ItemDrop`, `Utility` |
| Pigments (ingredient) | `SBPR_Ink{Red,White,Blue,Black}` (existing) | `ItemDrop` |

> Prefab-name strings are save/wire contracts the moment a piece/item is placed or
> crafted in a live world. Lock these three names in the first impl PR that registers each,
> and never rename them after (renaming orphans every placed/crafted instance — the same
> reason `Pigments` kept `SBPR_Ink*`).
