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

> **⚠️ OPEN PATH SUPERSEDED (2026-06-11, issue 7 → §2F).** The §2 IMPL STATUS banner above
> and the last bullet below originally said the Local Map's full view is ACTIVATED by the
> vanilla **"Map" button** ("otherwise dead under nomap"). Daniel's v0.2.19-playtest proves
> that premise FALSE: the playtest world is `nomap=OFF`, where vanilla's M-key map is fully
> alive, so binding our viewer to "Map" stacks both surfaces. **§2F is now the authoritative
> open-input path** — the viewer opens on the **Use key (E)** while equipped, off the "Map"
> button entirely. Read §2F before touching the open trigger.

- **Minimap binding is durable while the item sits in inventory** (equipped or not) and
  **reverts to no-map the instant the item leaves inventory** (dropped/traded/destroyed).
  Hook inventory-changed; when no `SBPR_LocalMap` instance remains, drop the binding.
- **Full-screen view requires the map actively EQUIPPED** (two hands). Carrying it keeps
  the minimap bound; only an equipped map can be opened — **opened by the Use key (E), NOT
  the "Map" button (§2F, issue-7 correction).**

#### 2A.5 Imprint + per-instance storage
- **Imprint** happens at a Surveyor's Table (§1): blank map → a **snapshot** of the
  Table's current windowed survey + the Table's bound-origin world coord. Not a live link.
- Store the windowed fog array + bound-origin coord **on the item instance**. Verify
  `ItemDrop.ItemData.m_customData` (a `Dictionary<string,string>`) exists and round-trips
  trade/drop **on our game version before relying on it** (flagged unknown — decomp it at
  build). **Fallback if absent:** a ZDO-backed "map case" carrier. The blob is the §2C
  windowed format, so one format serves item + Table + viewer.

### 2B — The forked viewer (productionize the spike's proof)

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
- Must work / degrade gracefully under v1's map nerf: the fork owns its own open/close on
  the **Use key (E) while equipped (§2F)**, and does not rely on vanilla
  `Minimap.SetMapMode(Large)` or the "Map" button. (Issue-7 correction: the earlier "Map
  button repurposed under nomap" open path is replaced — see §2F.)

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
- **AT-LMAP-OPEN-1…6** (issue 7 correction, 2026-06-11) — the equipped Local Map opens its
  viewer on the **Use key (E)**, not the "Map" button; no double-map stacking; an on-screen
  prompt shows the open key. See **§2F** for the named criteria + the locked input model.
- SpecCheck row 2 present; `[hold]` PR; logs-green ≠ playable.

---

### 2F — Local Map open input (issue 7 design correction, 2026-06-11)

> **Status: DESIGN CORRECTION.** Supersedes the "the vanilla **'Map' button** activates the
> full view (dead under nomap, so the fork repurposes it)" open path asserted in the §2 IMPL
> STATUS banner, §2A.4, §2B, and the code comments at `LocalMapController.cs:79-86` /
> `MapViewer.cs:391-394` / `LocalMapController.cs:14-16`. The fork SHELL, binding state machine,
> imprint, torch exception, and render (§2E) are all UNCHANGED and correct — **only the OPEN
> TRIGGER changes.** Reported by Daniel, v0.2.19-playtest, in game. Clean-side (ADR-0001).

**What Daniel reported (verbatim):** *"local map doesn't seem usable with left or right click.
Just appears to be a hoe :P. M pulls up the local map on top of the global map which is weird."*

**The decisive RE finding — the card's premise was inverted.** The whole "Map button is dead
under nomap, repurpose it" design rests on a false reading of vanilla. Verified against the
decomp (`assembly_valheim`, `Minimap.cs`):

- `Minimap.SetMapMode(MapMode mode)` (`:961-999`) clamps `mode = MapMode.None` **only when
  `Game.m_noMap == true`** (`:963-966`), which sets BOTH `m_largeRoot.SetActive(false)` AND
  `m_smallRoot.SetActive(false)` (`:976-977`). So `Game.m_noMap` does not "kill only the M
  map" — **it kills the minimap circle too.**
- `Minimap.Update` (`:604`) fires the `ZInput.GetButtonDown("Map")` → `SetMapMode` toggle
  inside the modal gate at `:593` (`!Chat.HasFocus && !Console.IsVisible && !TextInput.IsVisible
  && !Menu.IsActive && !InventoryGui.IsVisible`).
- **Therefore the "Map" button is dead *only* when `Game.m_noMap == true* — and in that world
  there is no minimap either.** Daniel sees a minimap circle AND a global map opening on M.
  That combination is **only possible when `Game.m_noMap == false` (`nomap=OFF`)**, where
  vanilla's M-key Large map is fully alive.

**Two coupled defects fall out of that one inverted premise:**

1. **Open-trigger collision (the "M opens local-map-on-top-of-global-map" defect).**
   `LocalMapController.cs:86` opens our viewer on `ZInput.GetButtonDown("Map")`. Under
   `nomap=OFF` that SAME press also drives vanilla's `Minimap.Update` Small→Large toggle →
   both surfaces appear. The fork was wired for a world (`nomap=ON`) the playtest isn't using.

2. **The v1 "no M-key full map" baseline was specced but never built.** The locked v1 baseline
   (`PARKED-2026-06-03.md:20`, `requirements.md:10`, `design/cartography-v2.md:21-26`) is:
   `nomap=ON` → no map at all; `nomap=OFF` (default) → **minimap only, no M-key full map**. But
   "minimap-only-without-the-M-map" is **not a vanilla state** — vanilla gives you both or
   neither (per the decomp above). Delivering it requires an SBPR patch that clamps vanilla's
   Large map. **No such patch exists in `src/`** (audited: the only `Minimap` prefixes are the
   Cartographer's Kit `UpdateExplore` gate and the equip patch; the rest are reconcile
   postfixes). So under `nomap=OFF` vanilla's full M-map leaks — exactly what Daniel sees.

**The "just a hoe" half is INTENDED and stays.** The click-suppression (`LocalMap.cs:112-121`
empties `m_attack`/`m_secondaryAttack` → `HavePrimaryAttack`/`HaveSecondaryAttack` false) is
**correct** (AT-MAP-BLOCKCLEAR). The item is not supposed to attack. The fix is to give it a
working, discoverable OPEN action — not to restore clicks.

#### 2F.1 LOCKED open input — the Use key (E) while equipped

**Route (a) from the card is locked. Routes (b) "gate vanilla's M" and (c) "a new bind" are
rejected** (rationale below).

- **Open gesture:** while a Local Map is **equipped** (two hands), pressing the **Use key**
  (`ZInput.GetButtonDown("Use")` / `"JoyUse"` — the same input vanilla routes to
  `Player.Interact`, decomp `Player.cs:806`) opens the bounded viewer in `FieldReadOnly` mode.
  This is the SAME open gesture the Surveyor's Table uses (`Switch.Interact`/Use → open viewer,
  §1.4) — one consistent "Use to read the map" model across both surfaces.
- **Collision-free by construction:** the Use key is NOT the "Map" button, so pressing it never
  drives vanilla's `Minimap.Update` map toggle. Vanilla's M continues to do whatever it does in
  the world's nomap config, independently; our viewer never rides it. (AT-LMAP-OPEN-2/3.)
- **Toggle + close:** while the viewer is open, **Use** (or the §2-exit card t_e2cc8183's
  Escape handling) closes it. The controller already self-closes on unequip and when the map
  leaves inventory — keep those. Replace the `GetButtonDown("Map")` edge at
  `LocalMapController.cs:86` with a `GetButtonDown("Use")` edge under the same
  `_mapEquipped && _equippedMap != null && !tableViewOwnsViewer` guard.

**Use-key interaction discipline (the one real hazard — must be handled):**

- **Do not let one Use press both open the viewer AND interact with a hovered world object.**
  Vanilla `Player.Update` (`:806`) sends Use → `Interact(m_hovering, …)` when something is
  hovered. The Local-Map open path must only fire when the press is NOT being consumed by a
  world interaction — i.e. gate our open on **`Player.m_localPlayer.GetHoverObject() == null`**
  (public accessor, decomp `Player.cs:4055`) so standing in front of a door/Table/chest and
  pressing E still interacts with it, and only opens the map on an otherwise-idle Use press.
  This mirrors how a Local Map at a Surveyor's Table should still **Use the Table** (survey +
  imprint), not pop the field viewer — the Table is the hovered object, so our open suppresses
  and the Table's `Interact` wins. (AT-LMAP-OPEN-3, AT-LMAP-TABLE-COEXIST.)
- **Suppress while a modal SBPR UI / text input is up.** Reuse the existing
  `SignPanelInputBlock.AnyOpen` check (it already covers `CartographyViewer.IsViewerOpen`) plus
  the vanilla `TextInput.IsVisible()` / `InventoryGui.IsVisible()` guards, so opening text
  fields or the inventory doesn't trip a map-open. (The controller's `Update` runs every frame;
  keep its existing graphics-client guard.)
- **Implementer's-choice mechanism, equivalent outcomes:** either (i) keep reading `ZInput`
  directly in `LocalMapController.Update` with the hover-null guard above (smallest change,
  matches the current controller shape), or (ii) route through a `Humanoid.UseItem` /
  Interact-side hook on the equipped item. (i) is recommended — it's the minimal delta to the
  existing polled controller and avoids a new Harmony surface. Whichever is chosen, the
  hover-null + modal-suppress discipline is mandatory.

**Why not (b) "keep Map, gate vanilla's M":** it requires a reliable Harmony clamp on vanilla's
map-open under `nomap=OFF` — which is the very "no M-key full map" nerf that was specced but
never built (defect 2). That's a **separate, larger design decision** (see §2F.3) about whether
v1's intended nerf ships at all; do NOT smuggle it in through the Local Map's open path. Moving
off "Map" entirely makes the Local Map correct **regardless of how that nerf question lands.**

**Why not (c) "a new dedicated bind":** a brand-new keybind is undiscoverable without a rebind
UI and duplicates the "Use to read" affordance the Table already establishes. The Use key is
the consistent, already-bound, prompt-backed gesture.

#### 2F.2 The equipped prompt (so it doesn't read as an inert hoe)

- While a Local Map is **equipped**, show an on-screen hint: **`[<$KEY_Use>] $piece_readmap`**
  (or plain "Open map") rendered through `Localization.instance.Localize` so the bound-key
  token resolves to the player's actual key (e.g. "E") — the **same rebind-correct pattern**
  `SurveyorTableTag.GetHoverText` / `CairnInteractable` use (`$KEY_Use` localizes; a CUSTOM
  `$piece_*` token would leak as a literal — the 2026-06-05 sign-bug lesson). `$piece_readmap`
  is a **vanilla** token (decomp `MapTable.cs:34`), so it localizes; if a fuller string is
  wanted, keep the `$KEY_Use` token and put the rest in plain English.
- **Placement:** a HUD hint while the map is equipped (not a hover-text — the map is a held
  item, not a hovered piece). Bottom-center is consistent with the viewer's own exit prompt
  (t_e2cc8183 adds "[Esc] Close map" while the viewer is open); the equipped-prompt and the
  open-viewer-prompt are mutually exclusive (one says how to open, the other how to close).
  Implementer picks the exact HUD surface; the bound-key token + equipped-only visibility are
  the locked requirements. (AT-LMAP-OPEN-4.)
- **Coordinate with t_7816c0b0 / t_e2cc8183** on token wording so all three cartography prompts
  read consistently.

#### 2F.3 The deferred question — does v1's "no M-key full map" nerf actually ship? (NOT this card)

This card makes the Local Map correct on `nomap=OFF` **without** depending on the nerf. But the
playtest exposed that **vanilla's full M-map is currently reachable**, which contradicts the v1
baseline's "no M-key full map." Two coherent end-states, and **Daniel must call it** (it is a
gameplay-pillar decision, not an implementation detail):

- **(I) Ship the nerf:** add the missing SBPR patch that clamps vanilla `Minimap.SetMapMode`
  Large→Small (or gates the `:604` toggle) under `nomap=OFF`, so M only ever opens the minimap,
  never the full world map. This is what the locked v1 baseline says should already be true. The
  Local Map (Use-key) is unaffected either way.
- **(II) Drop/relax the nerf:** accept that under `nomap=OFF` players have vanilla's full M-map,
  and the Local Map is an *additional* bounded artifact. Re-word the v1 baseline + requirements
  to match reality.

**This card does NOT implement either** — it routes the Local Map off "Map" so it's correct in
both. If Daniel wants (I), that's a **separate card** (clean-side; a `Minimap.SetMapMode` clamp,
the same hook the WorldPin reconcile already postfixes). Flagged here, surfaced as the card's
open question, NOT silently chosen.

#### 2F.4 Files touched + clean/dirty

- **`LocalMapController.cs`** — replace the `GetButtonDown("Map")` open edge (`:86`) with a
  `GetButtonDown("Use")` edge guarded by `GetHoverObject() == null` + the modal-suppress check;
  fix the false-premise comments (`:14-16`, `:79-86`).
- **`MapViewer.cs`** — the `:391-394` comment asserting "vanilla Minimap's M/ESC handling, which
  is dead under nomap" is the same false premise; correct it. (The Escape close itself is the
  t_e2cc8183 exit card's surface — coordinate; this card only fixes the OPEN trigger + the
  comment.)
- **`LocalMap.cs`** — no code change to the item; the click-suppression stays (intended). Add
  the equipped prompt via the controller or a small HUD hook (implementer's choice per §2F.2).
- **Clean-side (ADR-0001):** reading `ZInput`, `Player.GetHoverObject()`, vanilla `$KEY_Use` /
  `$piece_readmap` tokens, and the vanilla `Minimap` decomp is all base-game. No third-party mod
  code. No SpecCheck impact (input/UI behavior, not a recipe row).

#### 2F.5 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-LMAP-OPEN-1** — with the Local Map equipped, pressing **Use (E)** on an otherwise-idle
  press opens its bounded viewer. It WORKS and is discoverable.
- **AT-LMAP-OPEN-2** — opening the Local Map viewer does NOT also open vanilla's map; no
  double-map stacking on any single input.
- **AT-LMAP-OPEN-3** — pressing **M** (vanilla's map, in whatever state the world's nomap config
  leaves it) does NOT open our viewer; pressing **Use** while hovering a world object interacts
  with that object (door/chest/Table), NOT the map. The two input paths are non-colliding.
- **AT-LMAP-OPEN-4** — an on-screen prompt (bound-key token, e.g. "[E] Open map") is visible
  while the Local Map is equipped and not-yet-open, so it never reads as an inert hoe.
- **AT-LMAP-OPEN-5** (intended behavior preserved) — LMB/RMB still do no attack/block
  (AT-MAP-BLOCKCLEAR holds; click-suppression is correct and unchanged).
- **AT-LMAP-OPEN-6** — closing the viewer is clean (pairs with t_e2cc8183: Escape closes without
  leaking the game menu; Use also toggles it shut). Same `MapViewer` engine.
- **AT-LMAP-TABLE-COEXIST** — standing at a Surveyor's Table with a Local Map equipped and
  pressing Use **surveys + imprints at the Table** (Table is the hovered object), and does NOT
  pop the field viewer over the Table view.
- logs-green ≠ playable — Daniel confirms in-game: equipped map opens on Use, no global-map
  overlap, prompt visible.

**Shared-file coordination (viewer-UX cluster).** This card (open), **t_e2cc8183** (Table-viewer
Escape exit + prompt), and **t_c90f4d8c**/§2E (vanilla-cartography render) all touch
`MapViewer.cs` + the viewer input model. They are one coherent input/UX problem. **Routing
recommendation:** sequence them onto ONE engineer-ui worker (or strictly serialize the PRs) so
they don't conflict on `MapViewer.cs`. Suggested order: §2E render (largest, already specced via
PR #107) → t_e2cc8183 exit → this open-input card, OR fold all three into a single viewer-UX
implementation card. The architect (this card) flags the coupling; the merge sequencing is
Daniel's call at review.

**Implementation card:** to be routed to `engineer-ui` (owns `MapViewer.cs` +
`LocalMapController.cs`), as a child of this card on approve. **SpecCheck impact: none.** Spec +
code move together in that PR.

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
