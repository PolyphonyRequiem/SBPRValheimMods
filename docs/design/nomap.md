---
title: "Nomap design — idea ⇄ patch-surface cross reference"
status: living
purpose: Investigation: no-map navigation patch surface.
---

# Nomap design — idea ⇄ patch-surface cross reference

Each idea below lists the exact decomp surface a SBPR.Niflheim Harmony patch / new
prefab would touch.

> Line numbers in this document reference a local, non-shared decompilation of
> `assembly_valheim.dll` (Bog Witch / Ashlands era). The decompiled source is **not**
> included in this repository — only our reverse-engineering observations are. Class
> and field names match what any modder using `ILSpy` or `dnSpy` would see.

---

## 0. Foundational doctrine: how Valheim "knows" the map exists

The whole map system lives in **one MonoBehaviour singleton** — `Minimap` (line 46483, 2420 lines).
- Modes: `enum MapMode { None, Small, Large }` — Small = minimap circle, Large = full-screen M-key map.
- Fog-of-war storage: `private bool[] m_explored` + `m_exploredOthers` (size `m_textureSize²`) and a `Texture2D m_fogTexture`.
- Self-discovery radius: `public float m_exploreRadius = 100f` — the constant that makes you see terrain by walking.
- Pins: `Minimap.PinData` (line 46513), with `enum PinType { Icon0–4, Death, Bed, Shout, None, Boss, Player, RandomEvent, Ping, EventArea, Hildir1–3 }`.
- Public APIs we'll lean on:
  - `AddPin(Vector3 pos, PinType type, string name, bool save, bool isChecked, long ownerID, PlatformUserID author)` → line 1985
  - `RemovePin(PinData pin)` → line 1927
  - `DiscoverLocation(Vector3 pos, PinType type, string name, bool showMap)` → line 1944
  - `ShowPointOnMap(Vector3 point)` → line 1934
  - `ExploreAll()` → line 1484 (cheat command; useful reference)
  - `SetMapMode(MapMode mode)` → line 961
  - Mouse handlers: `OnMapLeftClick`, `OnMapDblClick`, `OnMapRightClick` → lines 2086–2125
  - Pin icon select: `SelectIcon(PinType)` → called from `OnPressedIcon0..4`
- Sharing/serialization: `GetSharedMapData(byte[])` / `AddSharedMapData(byte[])` — used by `MapTable` (line 114014, only 128 lines, very small surface).

**Key insight:** we never have to "rip out" the map. We just patch `Minimap.Awake`/`Update`/`SetMapMode` to **gate** behaviors behind "is the right unlock present?", and patch `m_exploreRadius` to 0 by default. The map prefab can stay in the world.

---

## 1. Explorer's Bench (Meadows)

A new workbench-tier crafting station.
- **Patch surface:** **none** — pure prefab work.
  - Clone `piece_workbench` → set new prefab name `SBPR_ExplorersBench`.
  - Add a new `CraftingStation` (line 56034) component with `m_name = "$sbpr_piece_explorers_bench"`.
  - **Set `m_showBasicRecipies = false` on that CraftingStation.** The vanilla Workbench is the ONLY station that ships this flag `true` — it's the flag that surfaces the stationless "basic" recipes a player can otherwise craft by hand (Club, Torch, Stone Axe, Hammer, Hoe, rag armor, …). A raw clone inherits `true`, so the Explorer's Bench wrongly offered all of them. Every other vanilla station (forge, stonecutter, cauldron, …) ships it `false`; match them so ONLY Trailborne recipes appear here. (Bugfix 2026-06-04, card t_30f97042.)
  - **Strip the inherited `GuidePoint` component** from the clone. The vanilla Workbench prefab carries a `GuidePoint` (the proximity hook that triggers Hugin's "you built a workbench" tutorial); a renamed clone still inherits it, so Hugin wrongly greets the Explorer's Bench as a Workbench. Remove all `GuidePoint` components (root + children) right after cloning so the station carries no Workbench tutorial. (Bugfix 2026-06-04, card t_53ab3232. Note: `Piece.m_firstTutorial` does NOT exist on this game version — the tutorial is driven entirely by the `GuidePoint` MonoBehaviour, verified against `assembly_valheim.dll` metadata.)
  - It becomes the `Piece.m_craftingStation` requirement for the explorer pieces below.
- **Piece definition:** `Piece` (line 116052). Set `m_category = PieceCategory.Crafting`. `m_resources = Requirement[]` where each `Requirement.m_resItem` points to an existing `ItemDrop`.
- **Locked recipe (Daniel, 2026-06-03):**
  - Wood ×10 — frame.
  - Stone ×4 — surface to draw on.
  - **Deer trophy ×1** — antlers are visually integrated INTO the bench art (carved cups / supports / pen-holders). Not "mounted on top" as a trophy decoration — the antler shapes become part of the bench mesh itself via kitbash/material composition.
  - That's it. No bone fragments. No greydwarf eyes. No deer hide. No raspberries. No resin. (The earlier brainstorm here proposed bone fragments + greydwarf eyes + deer hide; PLAYER_GUIDE.md narrative further implied raspberries + resin. Daniel corrected: the bench recipe is just Wood + Stone + Deer Trophy. Raspberries and resin in PLAYER_GUIDE were describing what the bench is USED FOR — pigment grinding, ink fixative — not what it's MADE OF.)
  - All three are vanilla Meadows drops, no new resources needed.
  - Visual cue: half-rolled hide-map on the bench surface + a chunk of bone needle stuck in a stone disk + antler shapes integrated into the bench's structure.

---

## 2. Trailblazer's Spade (Meadows)

An item halfway between Hoe and Hammer.
- **Patch surface:** mostly **none** — clone `Hammer` ItemDrop → new ItemDrop `SBPR_TrailblazersSpade`.
- Items don't have their own class — they are `ItemDrop.ItemData` with a `m_shared.m_buildPieces` (a `PieceTable`, line 59893). We supply a new `PieceTable` containing only the signage + road-widening pieces.
- **Build-stamina mod (key one):** `Player.GetBuildStamina()` returns `m_attack.m_attackStamina`. Two options:
  1. Set `m_attackStamina = 1f` on the tool itself (clean, no patches).
  2. Harmony postfix `Player.GetBuildStamina` to floor at 1 when the right-hand item is `SBPR_TrailblazersSpade` (preserves global tuning).
  - I recommend option 1 — minimal surface, can't break other mods.
- **Road-widening:** the Hoe's `level_ground` is a `TerrainOp` (line 124079) preset. We register two Trailblazer pieces (`level_narrow`, `level_wide`) that both use TerrainOp with different `m_smoothRadius` values. No patches needed.

---

## 3. Sign collection (placed by the Trailblazer's Spade)

- **All four:** sub-class of `Sign` (line 121412, 178 lines, painless).
- `Sign` already handles ZDO-backed text via `ZDOVars.s_text`, plus author + UGC filter — we get text-writeable signs for free.
- **Beacon** = sub-class of `Fireplace` (line 106277) with:
  - `m_secPerFuel = 14400` (1 fuel = 4 in-game hours; matches Daniel's spec)
  - `m_maxFuel = 4`
  - `m_fuelItem = resin.ItemDrop`
  - Tall thin mesh, single point light, no smoke particles (cheap render).
- The "simple lighting to reduce load" intent: point light with `intensity = 1.5`, `range = 12`, no shadows. We control that in the prefab.

---

## 4. Traveler's Storage (public + per-player private chest)

This is the trickiest one in the nomap track. Vanilla `Container` (line 101699) is **single-inventory** keyed by ZDO. We need:
- One public `Inventory` (vanilla behavior).
- A `Dictionary<long, Inventory>` keyed by player ID for private chests, persisted in the ZDO.

**Patch surface:**
- New `MonoBehaviour SBPR_TravelerStorage` that **wraps** two `Container` instances (or one Container + a custom store).
- Storage: ZDO blob. The ZDO can hold arbitrary byte arrays via `ZDOVars.s_data`. We serialize a `Dictionary<long, byte[]>` (one inventory per player) plus the public inventory.
- Per-player ID: `Game.instance.GetPlayerProfile().GetPlayerID()` (the same `long` `Bed` uses for ownership — see `Bed.GetOwner()` line 195).
- "Postbox-style" interaction: when the player presses Use, query `playerID`; if no private chest exists, allocate one and open it.
- "Destroyed → public drops, private gone": hook the `WearNTear` (line 127992) `OnDestroyed` callback. Iterate public inventory → `Container.DropAllItems()` pattern; **don't** drop private dicts (lost to the void = "traveler beware").

**Pitfall:** ZDO blob size grows linearly with players. On Niflheim that's bounded (≤10 players), fine. If we ever publish, document this.

---

## 5. Traveler's Tent

- **Patch surface:** **none** — clone `bed` piece prefab, attach a `Bed` (line 99551, 215 lines) component, attach a small `EffectArea.Type.Heat` (or `Shelter`) collider sized to one human.
- `Bed.Interact` already checks `CheckExposure`, `CheckFire`, `CheckWet`. We satisfy all three by including a Heat EffectArea + closed mesh that registers as `underRoof = true`.
- **"Doesn't assign spawn point":** override the `Bed` behavior. Two clean approaches:
  1. Patch `Bed.Interact` with a prefix that checks for tent tag and skips the `SetCustomSpawnPoint` call (line 64, 102).
  2. Subclass `Bed` as `SBPR_TentBed`, override `Interact`. **Recommended** — patches less of the vanilla class.

---

## 6. Pocket Portal

> 🟢 **SUPERSEDED + RETHEMED → "Portal Seed → Ancient Portal" (Black Forest v2, specced
> 2026-06-13).** This historical note is kept for its vanilla anchors but is no longer the
> build plan. Two corrections the current spec makes: (1) **build ADDITIVELY, do NOT clone
> `portal_wood`** (ADR-0006 postdates this note — cloning drags EffectArea/GuidePoint, the
> bug class); (2) it lives on the **Hammer**, not the Spade (Daniel 2026-06-13). The
> build-ready plan is **`docs/v2/planning/ancient-portal-impl-spec.md`**; the design is
> `docs/design/pocket-portal.md`. Read those, not the bullets below, to implement.

- **Patch surface:** **none for the piece itself**, all prefab.
- Clone `portal_wood` (TeleportWorld, line 122902, 233 lines).
- New piece: `SBPR_PocketPortal` placed by the Trailblazer's Spade (registered in its PieceTable, not the Hammer's).
- Recipe `Requirement[]`: standard portal mats + `surtling_core ×3` (+1 over vanilla) + `greydwarf_eye ×20` (vanilla portal is 10).
- **Stack 5:** standard `ItemDrop.m_shared.m_maxStackSize = 5` — single line of config on the item.
- The TeleportWorld class is unmodified. Connection logic is just tag-matching via `ZDOVars.s_tag`.

---

## 7. Twisted Portal (THE BIG ONE)

> 🔴 **COST MODEL SUPERSEDED (Daniel-locked 2026-06-24) →
> [`twisted-portal-food-charge.md`](twisted-portal-food-charge.md).** The
> "charged accessory burns durability per teleport, food restores durability"
> economy below (the `SBPR_TwistedKey` trinket, the `EatFood`/`RemoveOneFood`
> charge hooks) is **REPLACED by food-as-fuel**: there is **no key trinket**;
> teleport range is gated by the food in the player's belly (Portal Energy =
> remaining-food-minutes × a stat-derived tier). The **bukeberry purge-accelerator
> is re-homed** (not dropped) as an emergency reserve fuel — Bukeperries are burned
> only when belly food can't cover a jump (30 m/berry, 10 = the 300 m ceiling), and
> a berry-burning jump arrives *Feeling Sick*. This resolves the impl-spec's open
> decision #2 ("charge economy"). Everything else in this §7 — the distinct portal
> class, rune-name pairing, the `NoPortals` bypass, the through-terrain name
> overlay — is **unaffected**. Read the food-charge doc for the cost model; the
> notes below are kept for the portal-mechanics history.
>
> 🟢 **SPECCED → `docs/v3/planning/twisted-portal-impl-spec.md`** (architect spec-pass, card
> t_f9cab392). Read that to implement; the notes below are the historical anchors, kept for
> reference. Two factual drifts the spec-pass corrected: (1) the ZDOMan API is
> **`GetAllZDOsWithPrefabIterative`** (`:65497`), not `GetAllZDOsWithPrefab(int)` as written below;
> (2) the food item is **`Pukeberries`** (internal id `Pukeberries`), not "bukeberries". The spec
> also catches a multiplayer gotcha this note missed — a client only holds portal ZDOs within
> ~64–128 m, so the "300m client-side query" works in singleplayer but is short on a dedicated
> server; the spec routes travel through **server-side rune-name pairing** and treats the 300m
> overlay as a best-effort client cosmetic. **The feature is BLOCKED on three Daniel decisions**
> (coexist-vs-replace, charge economy, destination UX) — see the spec's §1/§2.

This is the gnarly one. Spec: distinct namespace from vanilla portals, no-portal-restriction override, 300m range, on-step shows visible portal names through terrain, charged accessory burns durability per teleport, food restores durability, bukeberries are a "purge accelerator", charge syncs across stacks while in inventory.

**Patch surface:**
1. **Distinct portal class** — new `SBPR_TwistedPortal : MonoBehaviour, Hoverable, Interactable, TextReceiver`. Don't inherit from `TeleportWorld` directly (we don't want tag collision). Reimplement the small bits we need:
   - Look-up other Twisted Portals within 300m: query `ZDOMan` for all ZDOs of the SBPR prefab hash within radius (cheap — there's already `ZDOMan.GetAllZDOsWithPrefab(int)` available; need to confirm exact API).
   - Display a list overlay through terrain → custom UI on standing-trigger, populated from that query.
2. **No-portal-restriction override:** vanilla `TeleportWorld.Teleport` blocks on `ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals)` (line 108). Our class **just doesn't check it** — we're independent code.
3. **Through-terrain rendering:** Unity-side shader trick on the portal name labels (ZTest Always). Pure prefab work, no patches.
4. **Charged accessory:**
   - New ItemDrop `SBPR_TwistedKey` with `m_shared.m_useDurability = true`, `m_maxDurability = N`, `m_itemType = ItemData.ItemType.Trinket` (enum value `24` — confirmed in `ItemDrop.ItemData.ItemType`, line 57627).
   - "Charge from food consumed": patch **`Player.EatFood(ItemDrop.ItemData item)`** (line 17462) with a postfix. When `__result == true`, scan `Player.m_localPlayer.GetInventory()` for `SBPR_TwistedKey` instances; for each, increment durability proportional to `item.m_shared.m_food` (or a flat per-meal charge). Bukeberry note below.
   - **"Bukeperries (Bukeberries) purge food to charge accessory faster":** Bukeberry is data-only — there is no hardcoded "bukeberry" path. The actual food-purge primitive is **`Player.RemoveOneFood()`** (line 17452) which pops one random food slot. A `StatusEffect` attached to bukeberry (via `ItemDrop.m_shared.m_consumeStatusEffect`) is what calls it. For Twisted Key charging-via-bukeberry: postfix `Player.RemoveOneFood` (when `__result == true`) and apply a *larger* charge increment to keys in inventory than a normal `EatFood` postfix would. This way bukeberries become a "spend stomach space for portal range" strategy.
   - **"Charging splits with other copies in inventory":** trivial — count `SBPR_TwistedKey` instances in inventory, divide the increment, apply to each.
5. **Rune-name registry:** I like the "rune names" framing. Implement as ZDOVars custom string slot (separate from `s_tag`) so vanilla Portals can't accidentally connect via tag collision. Concretely: `m_zdo.Set("sbpr_rune_name", string)`.

**Risk note:** standing-on-portal overlay is the most "stranger" UI we'll have to build. It's possible but it's a chunk of work — a worldspace `Canvas` with line-of-sight raycasts hidden, plus a `UnityEngine.UI.Text` per nearby portal. I'd build this as the third milestone, not the first.

---

## 8. Iron Compass (Swamps)

> **→ BUILD SPEC: [`docs/v3/planning/iron-compass-impl-spec.md`](../v3/planning/iron-compass-impl-spec.md)**
> (card t_d35405e3, architect spec-pass 2026-06-17). The note below is the historical design surface;
> the impl spec is the buildable HOW. **One correction the impl pass made:** the visibility gate below
> reads `GetInventory().HaveItem(...)` — a **carry**-gate — but a Trinket grants its effect only when
> **equipped in the accessory slot**, not merely carried. The spec uses the **equip-gate**
> (`Inventory.GetEquippedItems()` + Trinket-slot filter + prefab-name match), the in-repo
> `CartographersKit.IsWearingKit` precedent, not `HaveItem`. (Enum `Trinket = 24` re-verified this pass
> at decomp line 57652; the `:57627` below is the enum's start line.) Everything else here is confirmed
> accurate (`Hud.m_rootObject`, `GameCamera.instance.transform.eulerAngles`, no game-state patches).
>
> **→ SURFACE-RING BRANCH (2026-06-20, Daniel-gated):** when the compass is worn AND an SBPR map
> surface is showing, the cardinal payoff also draws **on the surface** as a compass-gated iron
> N-ring (HUD needle hides) — design [`iron-compass-minimap-ring.md`](iron-compass-minimap-ring.md),
> build spec [`docs/v3/planning/iron-compass-minimap-ring-impl-spec.md`](../v3/planning/iron-compass-minimap-ring-impl-spec.md).
> The HUD overlay here stays the **no-surface fallback**.

- **Item:** new ItemDrop `SBPR_IronCompass`, accessory slot (`m_shared.m_itemType = ItemData.ItemType.Trinket`, enum value 24 — confirmed at line 57627).
- **Render:** a small UGUI Image as child of `Hud.instance.m_rootObject`, positioned bottom-center below the map-icon area.
  - Awake of a new `SBPR_CompassHud : MonoBehaviour` attached via `[HarmonyPatch(typeof(Hud), "Awake")]` postfix — instantiate a `RectTransform` under `Hud.instance.m_rootObject`.
- **Behavior:** `Update()` reads:
  - `Player.m_localPlayer.GetInventory().HaveItem("SBPR_IronCompass")` → toggle visibility.
  - `GameCamera.instance` exposes the live camera; read `transform.eulerAngles.y` for yaw → drive a lerp-toward-target rotation on the needle (the "slight lag").
  - `transform.eulerAngles.x` (clamped/wrapped) for pitch → map 180° pitch range to 45° UI tilt as you specified.
- **No game patches needed.** Pure HUD overlay + item.

---

## 9. Map table change: zoom cap + 1000m visibility + no scroll

Three patches on `Minimap` (the central class):
- **Zoom cap:** `m_largeZoom = 0.1f` (line 307), `m_smallZoom = 0.01f` (line 309). The wheel scroll math at lines 807–827 reads `m_largeZoom`. We patch the **target zoom variable** that gets multiplied — either clamp `m_largeZoom` on `Awake` postfix, or clamp the runtime mutable zoom value.
- **1000m visible cap (everything else shrouded):** the explored-array is `bool[]` over a fixed `m_textureSize` (default 2048). We add a postfix to `Explore(Vector3 pos, float radius)` (this method exists in the decomp around the explore radius logic) that, when the local player is the explorer, only writes pixels within 1000m of the **current player position**. **Better:** write a postfix on `UpdateExplore` that re-blackens fog pixels outside 1000m of player every tick.
  - **Decision needed (asking below):** does "1000m" mean from your current position (rolling shroud), or from the cartography-table the map was drawn at (fixed-window)?
- **No scroll:** the pan logic is in `OnMapMiddleClick(UIInputHandler handler)` (line 2114) and the drag-pan inside `UpdateLargeMap` (around lines 700–800). We patch those to no-op when in Large mode, or always.

These three are all on **one class**, all clean Harmony postfixes/prefixes. No risk of breaking other mods.

---

## 10. Seer's Stone (Alt+E pin-by-look)

- **Item:** new ItemDrop `SBPR_SeersStone`, accessory.
- **Patch surface:**
  - `Player.Update` (or wherever input is polled — `Player.UpdateInput` exists) — postfix to check `Input.GetKeyDown(KeyCode.E) && Input.GetKey(KeyCode.LeftAlt)` when carrying a Seer's Stone.
  - On trigger: raycast from camera forward (use `GameCamera.instance.m_camera`) → hit something.
    - If it's a `Pickable` (line 59380) → `Minimap.instance.AddPin(pos, PinType.Icon3, pickable.GetHoverName(), save: true, isChecked: false)`.
    - If it's a `Location` (line 113683) → use `DiscoverLocation` for the special "shown on map" behavior.
  - **"Count within range":** before adding the pin, query existing pins (`Minimap.instance.m_pins` or via reflection / find adjacent) and merge — if there are 3 of the same name within radius R, just append "×3" to the new pin's `m_name`.

**Note on "Player.m_pins":** the player has `private List<Minimap.PinData> m_customPins;` (visible in the Minimap.PinData usage). We use `AddPin` and let the existing serialization handle persistence — `m_save = true`.

---

## Cross-cutting infra (used by all of the above)

- **Asset bundle:** SBPR.Niflheim ships a Unity-built `assetbundle` containing prefabs/meshes/icons. Loaded via `AssetBundle.LoadFromMemory(EmbeddedResource("...sbpr_niflheim"))`.
- **Localization:** all `$sbpr_*` keys defined in `localization/sbpr_english.json`. Patched into the localization system via `Localization.instance.AddJsonFile` (or whatever — need to confirm the public API; there's an exposed `AddWord` we can fall back on).
- **Piece registration:** patch `ObjectDB.Awake` postfix to register our prefabs with `ObjectDB.instance.m_items` and recipes with `ObjectDB.instance.m_recipes`. Standard mod pattern.
- **Server-gating (from SBPR doctrine):** every patch top is `if (!SBPR.Pact.SBPRContext.OnSBServer) return;`. This is what keeps the mod safe on vanilla/other servers.

---

## Open questions for Daniel (the design ones I can't decide for you)

1. **Map shroud radius — rolling vs fixed?** "No map beyond 1000m" → from your current position as you move (rolling shroud), or from where you stood at the cartography table?
2. **What unlocks the Explorer's Bench itself?** I assumed it's available at Meadows tier from start. Or does it require something first (kill an Eikthyr, find a specific item)?
3. **Twisted Portal vs vanilla portal — coexist or replace?** Are vanilla portals still craftable for people who don't want to bother with the Key durability dance? Or does Twisted replace vanilla entirely once unlocked?
4. **Seer's Stone scope** — only `Pickable` + `Location`, or also creatures (deer / boars for hunting)? "pickables and locations" was the spec but I want to confirm I'm not under-reading.
5. **Trailblazer's Spade tier** — Meadows (any antler-pickaxe-tier player can build one) or gated behind the Explorer's Bench?
6. **Public chest size** of Traveler's Storage — 4×4? Bigger? Smaller (forces selection of what to leave)?

I have **no opinion** on the answers to these; they're aesthetic/pacing calls I want from you.

---

## Risk ranking (lowest to highest implementation risk)

1. ✅ Traveler's Tent (subclass Bed, no surprises)
2. ✅ Sign collection + Beacon (prefabs only, Fireplace fuel works perfectly)
3. ✅ Iron Compass (pure HUD overlay, no game-state patches)
4. ✅ Pocket Portal (prefab clone + recipe)
5. ⚠️ Explorer's Bench + Trailblazer's Spade (prefab work, but new ItemDrop with PieceTable requires asset-bundle build pipeline working)
6. ⚠️ Map shroud / zoom cap / no scroll (Minimap is a 2420-line god-class, patches are surgically simple but the class is big — high risk of conflict with other Minimap-touching mods)
7. ⚠️ Seer's Stone (raycast + camera + pin merging — straightforward but lots of edge cases)
8. 🔴 Traveler's Storage (per-player private inventory in ZDO blob — novel serialization, needs careful testing)
9. 🔴 Twisted Portal (custom class, range query, through-terrain UI overlay, durability accessory, food-consumption hook, bukeberry purge integration, stack-sync — this is 4+ subsystems wired together)

**Suggested build order:** start with Tent + Sign + Beacon as a "warm-up PR" that proves the whole pipeline (asset bundle → BepInEx loader → server gating → Thunderstore publish → r2modman install → smoke test on Niflheim). Then map shroud + compass to validate UI/Minimap patches. Then storage. Then portals (pocket then twisted last).

---

## Appendix: precedent mods studied

These are open-source Valheim mods that solve overlapping problems. We treat them as
**reference reading** — we do not depend on their code, but we study how they
patch the same vanilla classes we plan to touch.

| Mod                                    | What we learn from it                                |
| -------------------------------------- | ---------------------------------------------------- |
| `shudnal/NomapPrinter`                 | How to patch `MapTable.OnRead`/`OnWrite` and `Player.Save`/`Load` for a "screenshot the map and hand it to you" approach. Direct philosophical cousin to what we're doing. |
| `nbusseneau/BetterCartographyTable`    | The cleanest example of patching `Minimap.AddPin`, `OnMapLeftClick`, `OnMapRightClick`, `GetSharedMapData`, `AddSharedMapData`. Uses Harmony **IL transpilers** when postfixes aren't enough. Essential reading for our zoom-cap / no-scroll / 1000m-shroud patches. |
| `BugattiBoys/PortalIndicator`          | Portal-aware HUD overlay. Reference for the Iron Compass UI and the Twisted Portal through-terrain name labels. |
| `KadrioS/RecipePinner`, `FroggerHH/LastTombstonePin`, `yudi7ll/AutoRemoveDeathPin` | Pin add / remove / merge patterns — directly applicable to the Seer's Stone Alt+E logic. |
| `ArgusMagnus/ValheimServersideQoL`     | Pure server-side mod patterns. Shows what's possible when no client install is required — informs our `SBPRContext.OnSBServer` gating. |
| `OrianaVenture/VentureValheim`         | A whole stable of mods under one author, organized as one folder per mod. Includes `AsocialCartography` (don't share map data across players), `Custom_RPC_Guide.md`, `Icon_Overlay_101.md`. Structural template for `SBPR.Pact + SBPR.Nomap + SBPR.GuardianStones`. |
| `RandyKnapp/ValheimMods`               | Gold-standard mod author (also wrote EpicLoot). Read for general code quality and Harmony patterns. |
| `Valheim-Modding/Jotunn` (source, not dependency) | Their `MinimapManager`, `PieceManager`, `ItemManager`, `SynchronizationManager` source is the canonical reference for *how to do common modding tasks the right way* — even though SBPR doctrine says no Jotunn dependency, their code shows what the well-trodden path looks like. |
