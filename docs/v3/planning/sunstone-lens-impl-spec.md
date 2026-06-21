---
title: "Sunstone Lens — solar-charged Swamp monster-detection accessory (v3 impl spec)"
status: current
purpose: "Build-ready architect+impl spec for the v3 Swamp-tier Sunstone Lens: a Trinket-slot accessory whose durability acts as solar energy (recharges in clear daylight, drains at a fixed rate while worn, reveals nearby hostiles via a HUD overlay while charged). Converts the locked theme/material/sourcing design (docs/design/swamp-detection-item.md, PR #144) plus the kanban card t_2fd7bc7f acceptance criteria into one tight section an implementer picks up cold: the single-prefab Trinket architecture, the exact vanilla decomp hooks (all line-cited against assembly_valheim, grepped live this pass), the durability-as-energy model, the HUD-overlay render that survives NoMap, observable named acceptance tests, the Features/ placement, and the SpecCheck manifest rows. Built and shipped by engineer-systems (card t_2fd7bc7f); Daniel gates the merge."
---

# Sunstone Lens — solar-charged Swamp monster-detection accessory

The design note ([`swamp-detection-item.md`](../../design/swamp-detection-item.md) — the
theme, material, and sourcing, merged as PR #144) is the locked *what* for the **material**.
The kanban card **t_2fd7bc7f** is the locked *what* for the **mechanic**. This doc is the
buildable *how*: the single-prefab Trinket architecture, the vanilla hooks re-verified
against the decomp, the durability-as-energy model, the HUD-overlay render method,
observable acceptance criteria, the `Features/` placement, and the SpecCheck manifest impact.

> **Clean-side note (ADR-0001):** every decomp line cited here is the base game
> (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the
> 2026-06-09 clarification). Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box) and were
> grepped live during this pass — re-confirm against the build assembly if the decomp
> drifts. The Rune Magic mod's *behavior* ("rune of detection") is reproduced **from
> vanilla primitives only** — no Rune Magic (or any third-party) code was read or copied.

> **ADR-0006 (load-bearing):** the Lens item prefab is built **additively** via
> `Assets.ConstructItemShell` (`new GameObject()` + `AddComponent`), exactly like the
> Cartographer's Kit. We never `Instantiate` a vanilla ZNetView-bearing prefab and strip it.
> The Sunstone material item clones `Coins` only because that is the established pre-ADR
> pattern for tiny Material items (same as Pigments); a future pass may migrate it to
> `ConstructItemShell`, but cloning `Coins` (no ZNetView surprises, deep-copied SharedData)
> is the lowest-risk path for a stack-20 Material today.

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. This feature adds **+1 entry**
(the Lens item recipe only — the Sunstone material is loot-sourced, not crafted, §6):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `SBPR_SunstoneLens` | item recipe (amount 1) | `SBPR_Sunstone` ×2, Iron ×1, Guck ×3 | `piece_sbpr_explorers_bench` |

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or
SpecCheck flags a NULL `m_resItem`) — verified this pass against the wiki corpus
`Internal ID` field (`~/valheim/sbpr-corpus/wiki/fandom/`):**
- `SBPR_Sunstone` = the new SBPR material const `SunstoneLens.SunstoneName` — referenced by
  the Lens recipe via the const, **never a literal**, so a rename can't drift the recipe.
- Iron = vanilla **`Iron`** (the Swamp metal — verified `Iron.md`); soft-gates the Lens to
  Swamp tier (you can't smelt Iron without Scrap Iron from Sunken Crypts → an Iron ingot in
  the Lens recipe *is* the tier gate, the "materials are the gate" heuristic).
- Guck = vanilla **`Guck`** (Swamp-surface resource off Guck sacks on Abandoned/Swamp trees
  — verified `Guck.md`); the lens housing / adhesive, on-thesis Swamp-surface material.

**The SpecCheck item shape (gotcha — same as cartography §0):** `SpecCheck.Run()` iterates
`Manifest.Where(s => s.Item != null)` and, for each, asserts the recipe exists, the output
amount + station match, the resource tuple matches, **and** the resolved `ItemDrop` has a
real (non-fallback) `m_icons[0]` (C1) and non-null `m_attack`/`m_secondaryAttack` (the
GetChainTooltip NRE guard). `ConstructItemShell` pre-seeds both, so the Lens is crash-safe
by construction; the icon assertion is what then SCREAMS at boot if the real PNG didn't ship.

> **§6 sourcing note:** the Sunstone material has **no craft recipe** — its sole
> acquisition path is the **loot economy** (swamp surface chests ~15% + rare Draugr Elite
> ~5%, `swamp-detection-item.md` §Sourcing; shipped as `SunstoneLoot.cs` / PR #183, card
> t_0445f590). An earlier **provisional** Iron×1 + Crystal×2 craft existed as a bridge so the
> material was obtainable before the drops landed; **Daniel locked REMOVE** once the loot
> economy shipped (card t_8f39b5fc → this card t_c27f985e). The manifest row, this doc §6,
> and the code moved together when it was removed (the spec-and-code-together rule).

## 1. Theme (resolves the design note's open-question #1 render sub-knob)

**Display name: "Sunstone Lens."** A polished shard of **Sunstone** (the Iceland-spar /
*sólarsteinn* material locked in `swamp-detection-item.md`) set in an iron-and-guck frame,
worn at the belt as a Trinket. Fiction: the lens drinks daylight when you hold it to the
open sky, storing it as a warm inner glow; carried into the Swamp's sunless murk, that
stored light lets the bearer **sense what moves in the dark** — and bleeds away as you spend
it. This is the exact "solar battery used in the sunless Swamp" tension Daniel flagged, and
it falls straight out of vanilla weather (see §3): **the Swamp is always-wet + overcast, so
the Lens can never recharge there** — you must carry a charge in from the sunlit overworld.

The Lens is the **first consumer** of Sunstone, not its only use (Daniel's 2026-06-13
standalone-resource amendment) — Sunstone is registered as its own Material item that future
v3 crafts can also draw on.

## 2. Architecture — one Trinket item + one Material item + one HUD overlay + two patches

```
Features/Sunstone/
  SunstoneLens.cs         — both item prefabs (Sunstone material + Lens trinket),
                            recipes, equipped-detection helper, the energy model,
                            and the DrainEquipedItemDurability override patch.
  SunstoneLensHudOverlay.cs — the client-only HUD MonoBehaviour + Hud.Awake postfix.
```

- **`SBPR_Sunstone`** — `ItemType.Material`, stack 20, the standalone resource. Clones
  `Coins` (Pigments pattern). **Loot-sourced only** (no craft recipe) — swamp surface chests
  + rare Draugr Elite drop (§6, `SunstoneLoot.cs`).
- **`SBPR_SunstoneLens`** — `ItemType.Trinket` (enum 24), the worn accessory. Built
  additively via `ConstructItemShell` (Cartographer's Kit pattern). `m_useDurability = true`
  so the energy bar renders and reads as durability; **we own the drain + recharge** (§3, §4).
- **HUD overlay** — a client-only `MonoBehaviour` attached under `Hud.instance.m_rootObject`
  via a `Hud.Awake` postfix, mirroring the Iron Compass render doctrine (`nomap.md` §8). This
  is the render surface that survives NoMap (§5).

### Why Trinket, not Utility (resolves the cross-card Utility-slot contention)

Three prior worker comments flagged Utility-slot contention (Sunstone Detector vs Cartographer's
Kit vs Iron Compass — vanilla wears only ONE Utility item). **Grounded resolution:** the
**Trinket slot is a separate, fully-wired equip slot** in current Valheim (the Bog-Witch
demister slot):
- `Humanoid.m_trinketItem` (`:12876`) — distinct field from `m_utilityItem` (`:12874`).
- `Humanoid.EquipItem` (`:13798`) has a dedicated `ItemType.Trinket` branch (`:13992`) that
  evicts only the previous trinket and sets `m_trinketItem`.
- `VisEquipment.SetTrinketItem` (`:28478`), `SetTrinketEquipped` (`:29080`),
  `m_currentTrinketItemHash` (`:28152`) — full vis pipeline.
- `ItemType.Trinket = 24` (`:57652`).

So the Lens occupies the **Trinket** slot and coexists with the Cartographer's Kit (Utility).
The Iron Compass design *also* specs Trinket (`nomap.md` §8) — so Lens + Compass would
contend for the single Trinket slot with each other. That is a **deliberate, legible
exploration-tool choice** (you wear the threat-sense OR the orientation aid, not both), and is
flagged for Daniel as a cross-card note, not pre-decided here. The Lens does not contend with
the Utility-slot cartography gear, which was the original worry.

## 3. The energy model — durability as a solar battery

Energy is the item's **durability** (`ItemData.m_durability`, `:57989`, default 100, persists
across relog via `pkg.Write(item.m_durability)` at `:57326`). `m_useDurability = true` makes
the inventory bar render (`InventoryGrid` draws the bar only when
`m_useDurability && m_durability < GetMaxDurability()`, `:38853`) and reads naturally as a
charge meter. `m_maxDurability` = the battery capacity.

**The trap (AC#5):** with `m_useDurability = true`, vanilla's `Humanoid.UpdateEquipment`
(`:13162`) calls `DrainEquipedItemDurability(m_trinketItem, dt)` (`:13198`→`:13227`) every
tick, which does `m_durability -= m_durabilityDrain * dt` and, **at zero, shows `$msg_broke`,
unequips, and removes the item if `m_destroyBroken`** (default `true`, `:57900`). That
violates AC#5 ("at zero it stops detecting but is NOT consumed/broken; active again after
recharge"). So we cannot let vanilla own the drain.

**Resolution — own the drain + recharge in a Harmony prefix on `DrainEquipedItemDurability`:**

```
[HarmonyPatch(typeof(Humanoid), "DrainEquipedItemDurability")]
Prefix(Humanoid __instance, ItemDrop.ItemData item, float dt) -> bool
  if item is not our Lens (m_dropPrefab name != SBPR_SunstoneLens): return true  // vanilla
  if __instance is not the local player: return true                            // client-only
  // We own it entirely:
  bool charging = CanRecharge(player)
  float delta = charging ? +RechargePerSec*dt : -DrainPerSec*dt
  item.m_durability = Clamp(item.m_durability + delta, 0f, item.GetMaxDurability())
  return false   // SKIP vanilla — never reaches the break/unequip/destroy branch
```

- **Drain is constant** while worn and not charging — `DrainPerSec` is a fixed config
  constant, independent of how many hostiles are detected (AC#4). Detection reads charge; it
  never spends extra per-hostile.
- **Clamp at 0** — the Lens goes inert (detection off, §4) but stays equipped and intact.
  Re-enters service the moment `m_durability` climbs back above the `MinChargeToDetect`
  threshold (AC#5).
- **`DrainEquipedItemDurability` is `private`** — Harmony patches private methods by name;
  registered in `Plugin.Awake` and asserted by `PatchCheck` (the unregistered-patch guard).
- Because we drain in the SAME method vanilla would, the cadence (per-`UpdateEquipment`-tick)
  is identical to a vanilla wearing-drain — no separate `Update` loop needed for energy.

**Recharge condition — `CanRecharge(Player p)` (AC#2), all vanilla, line-cited:**

```
clearWeather  =  !EnvMan.instance.GetCurrentEnvironment().m_isWet         // EnvSetup.m_isWet :80218
                 && EnvMan.GetCurrentEnvironment().m_name is a clear key   // GetCurrentEnvironment :81274
daylight      =  EnvMan.IsDaylight()                                       // :81159
notWet        =  !EnvMan.IsWet()                                           // :81134 (global wet state)
outdoors      =  !p.InShelter()                                            // :19375 (cover>=0.8 && underRoof)
notSwamp      =  p... GetCurrentBiome() != Heightmap.Biome.Swamp           // EnvMan.GetCurrentBiome :81258, Swamp=2 :108730
CanRecharge   =  clearWeather && daylight && notWet && outdoors && notSwamp
```

Recharge requires ALL of: clear weather AND daylight AND not wet AND outdoors AND not in the
Swamp. It will NOT recharge in rain, overcast, at night, under a roof, or in the Swamp (AC#2).
The Swamp check is technically redundant (the Swamp is always-wet + overcast, so `notWet` and
`clearWeather` already fail there — verified: "it is always raining in the mire regardless of
the weather elsewhere," wiki `Swamp.md`), but the card names it explicitly so we keep it as a
belt-and-suspenders guard and a self-documenting invariant.

**"Clear weather" definition:** we treat an environment as clear when it is **not wet** AND
its `m_name` is one of the sunny/clear keys (`"Clear"`, `"ThunderStorm"` excluded, etc.).
The simplest robust test is `!env.m_isWet && EnvMan.IsDaylight()`; we additionally allow an
optional clear-name allowlist as a config so a server can tune what counts as "sun." The
`m_isWet` field is the load-bearing one (rain/overcast envs set it), so the allowlist is a
refinement, not the gate.

**Charge is sunlight-only — the Lens is NOT bench-repairable (`m_canBeReparied = false`).**
Because `m_useDurability = true` (the energy bar) and the Lens's recipe craft-station is the
Explorer's Bench, vanilla `InventoryGui.CanRepair` (`:42798`) would otherwise treat the
partially-drained Lens as a valid **Repair** target at that bench — letting a player refill the
solar battery to full for free with one click, bypassing the entire `CanRecharge` sun-gate above.
We set `shared.m_canBeReparied = false` in `RegisterLensTrinket`; it is the **first** gate in
`CanRepair` (`:42776`) and short-circuits before any station-name match **and** before the
world-level OR-clause, so the Lens is non-repairable at **every** station (Explorer's Bench,
vanilla Workbench, Forge) unconditionally. The only charge source is sunlight (§3 drain/recharge
prefix). This mirrors `LocalMap.cs` (`m_canBeReparied = false`); the difference is that the Lens
**keeps** `m_useDurability = true` (it needs the meter), which is exactly why the repair flag is
load-bearing here rather than redundant. (sic: the vanilla field is misspelled `m_canBeReparied`.)

## 4. Detection — reproduce rune-of-detection from vanilla primitives (AC#3)

> 🔴 **RENDER SUPERSEDED (card t_b8a19487, 2026-06-18).** Daniel gave the real detection render
> design: a **trophy ring around the player** (each hostile's trophy on a fixed-radius
> screen-space ring, size ∝ proximity, ★ pips for star-levels), NOT the text/arrow placeholder
> described in this section. The **detection MECHANIC below is unchanged and correct** (the
> `GatherHostiles` sweep, the hostility filter, the equip-gate, the constant-cost rule); only the
> **render** (the "Render (v0.1)" bullet + §5) is replaced. See
> [`docs/design/sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md) for the
> ring render spec + the AT-LENS-RING-* acceptance tests. The text overlay survives only as an
> optional `Sunstone.DebugTextReadout` (default off).

A client-only sweep, run on a throttle (every `DetectIntervalSec`, default ~0.5s) from the HUD
overlay's `Update` (so it costs nothing on the dedicated server, which has no Hud):

```
if not wearing Lens (Inventory equipped scan, §4.1): hide overlay; return
if Lens.m_durability < MinChargeToDetect: show "depleted" overlay state; return
foreach c in Character.GetAllCharacters():           // static List<Character> :10313
   if c == null || c.IsDead(): continue
   if c.IsPlayer(): continue                          // :7422 — never reveal players
   if c.IsTamed(): continue                           // :10634 — never reveal tamed/friendly
   if c.GetFaction() is Players or AnimalsVeg: continue // :7427 — passive fauna are not threats
   if Distance(player, c) > DetectRadius: continue
   -> add c to the revealed set
render revealed set on the HUD overlay (count + nearest direction/bearing)
```

- **Hostility filter (AC#3):** `Character.Faction` (`:6818`) — `Players` and `AnimalsVeg`
  (deer, boar... the passive fauna) are excluded; every monster faction (ForestMonsters,
  Undead, Demon, ...Monsters, Boss, Dverger-when-hostile) is a threat. We additionally gate on
  `!IsTamed()` so a tamed boar/wolf (which is `AnimalsVeg`/`ForestMonsters` but friendly) is
  never flagged. Tamed + faction together cover "tamed/friendly creatures are not revealed."
- **Render (v0.1):** a HUD edge/compass-style threat indicator under `Hud.m_rootObject` —
  a small count badge plus a directional tick toward the nearest hostile (bearing derived from
  `GameCamera.instance` yaw vs the hostile's `GetCenterPoint()` `:8660`). v0.1 ships a
  placeholder-art indicator per the icon doctrine; the *mechanic* (who is revealed, when) is
  the acceptance target, the polish is a v0.x follow-up. **Deliberately NOT minimap pins** —
  see §5.
- **Constant cost:** detection only READS `m_durability`; it never decrements it. All drain is
  the fixed §3 tick (AC#4).

### 4.1 Equipped-Lens detection (public API only — m_trinketItem is protected)

`Humanoid.m_trinketItem` is `protected` — unreadable from a patch. Mirror the Cartographer's
Kit's proven pattern: scan `Inventory.GetEquippedItems()` (public) and match each item's
`m_dropPrefab.name` (clone-suffix-stripped) against `SBPR_SunstoneLens`, gating on
`m_shared.m_itemType == Trinket`. This is the exact `(item → m_dropPrefab.name)` pair vanilla
uses to wire trinket visuals (`VisEquipment.SetTrinketItem`). For the energy-drain patch (§3)
we get the `item` directly as a method arg, so no scan is needed there — the scan is only for
the HUD overlay's "is the Lens worn?" visibility gate.

## 5. Render surface under NoMap (resolves the design note's #1 coupling)

> 🔴 **The specific render is SUPERSEDED by the trophy ring (card t_b8a19487) — but the
> NoMap-safe HUD-overlay DOCTRINE below is still correct and load-bearing.** The trophy ring is
> *also* a `Hud.m_rootObject` overlay (not minimap pins), for exactly the reasons this section
> gives. What changes is the overlay's *content* (a camera-relative ring of trophies, not a text
> line). Read this section for *why a HUD overlay* (NoMap), and
> [`docs/design/sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md) for
> *what the overlay draws*.

> 🟢 **CARVE-OUT — superseded again, partially, by the minimap handoff (card t_91e86951,
> ACCEPTED design PR #214 / impl-spec
> [`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md)).** The HUD
> trophy ring is now the **no-minimap FALLBACK surface only.** Daniel's gated universal rule
> (2026-06-20): when ANY minimap is present — the SBPR carry-disc in nomap-ON, OR the vanilla
> corner minimap in nomap-OFF — Lens detection moves onto that minimap and the ring hides
> (default `MinimapHandoffMode = DiscWhenBound`). The "render via a HUD overlay, not minimap
> pins" doctrine below is still correct for the **NoMap-with-no-bound-disc** case (the genuine
> no-surface world the design note worried about); it is no longer the *only* surface. The
> nomap-OFF branch specifically REVERSES the old "a minimap reveal has no surface here" premise:
> in nomap-OFF the vanilla corner map DOES exist, so the handoff draws a custom threat overlay
> directly onto it (not `AddPin` — see the handoff impl-spec §5).

The design note flagged: *"if the minimap is off by default, a minimap-based reveal won't have
a surface — design the reveal independently of the minimap."* This is real on the SB server:
`NoMapEnforcer` sets `GlobalKeys.NoMap` **server-side by default** (`Cartography/NoMapEnforcer.cs`),
and the v1 map policy is "nomap ON → no map at all; nomap OFF → minimap only, no M-key"
(`requirements.md` §1.2). A `Minimap.PinType` reveal (`AddPin`, `:48466`) renders nothing when
the map is disabled.

**Therefore the Lens renders via a HUD overlay, not minimap pins** — the same doctrinal choice
the Iron Compass made ("pure HUD overlay, no game-state patches," `nomap.md` §8, risk-rank #3).
The overlay lives under `Hud.instance.m_rootObject` (`Hud.instance` `:39259`, `m_rootObject`
`:38949`) and works regardless of map state. This is the documented render-method decision the
card's AC#7 asks for.

## 6. Recipes

**Sunstone Lens (the deliverable, LOCKED this pass):** at the Explorer's Bench —
`SBPR_Sunstone ×2 + Iron ×1 + Guck ×3`, output 1, `m_minStationLevel = 1`. Every material has
a sentence: Sunstone = the solar core; Iron = the Swamp-tier frame (and the tier gate — Iron
needs Sunken-Crypt scrap to smelt); Guck = the Swamp-surface adhesive/housing. `m_maxQuality = 1`
(no upgrade tiers in v0.x).

**Sunstone (the material): NO craft recipe — loot-sourced only.** Sunstone is acquired
exclusively through the loot economy: swamp **surface** chests (primary, ~15% per chest) and
a rare **Draugr Elite** combat drop (secondary, ~5% flat), shipped as `SunstoneLoot.cs`
(card t_0445f590 / PR #183, spec `sunstone-loot-economy-impl-spec.md`). An earlier
**provisional** Explorer's-Bench craft (`Iron ×1 + Crystal ×2 → SBPR_Sunstone ×1`) existed as
a stopgap so the material was obtainable before the drops landed; **Daniel locked REMOVE**
once the loot economy shipped (card t_8f39b5fc → this card t_c27f985e). The recipe and this
note moved together when it was removed.

## 7. Server-gating & client/server split (doctrine)

- **Registration** (both items + both recipes) runs under `ServerContext.OnSBServer` via the
  `Registrar` dispatch (the M0 stub returns `true`; registration always runs locally). The
  Sunstone material registers before the Lens (the Lens recipe consumes it) — placed in the
  Registrar dispatch order accordingly, the same Pigments-before-Kit ordering rule.
- **The DrainEquipedItemDurability patch** is gated to the local player and our item — inert on
  the dedicated server (no local player) and a pure pass-through for every non-Lens item.
- **The HUD overlay + detection sweep** are client-only by construction: `Hud.instance` and
  `Character.GetAllCharacters()` only matter on a client; the `Hud.Awake` postfix never fires
  on the dedicated server (no Hud).
- Both patch classes are registered in `Plugin.Awake` and asserted by `PatchCheck` (the
  unregistered-patch boot guard).

## 8. Observable acceptance tests (named, in-game — logs-green ≠ playable)

- **AT-LENS-OBTAIN:** Sunstone is obtained from the loot economy (swamp surface chests +
  Draugr Elite drop — `SunstoneLoot.cs`), NOT craftable at any station; the
  Lens is craftable from Sunstone+Iron+Guck. The Iron Compass and all other tier items are
  unchanged (AC#1). SpecCheck green for the Lens recipe row (the material has no recipe row).
- **AT-LENS-CHARGE:** stand in the open in clear daylight, dry, outside the Swamp → the Lens'
  durability bar climbs. Step under a roof, or wait for rain/night, or enter the Swamp → it
  stops climbing (AC#2).
- **AT-LENS-NOCHARGE-SWAMP:** inside the Swamp, the bar never climbs even at midday (always-wet
  + overcast) (AC#2).
- **AT-LENS-DETECT:** worn with charge remaining, a Draugr/Blob/Leech within `DetectRadius` is
  surfaced on the HUD overlay; a tamed boar and a wild deer are NOT (AC#3). *(🔴 RENDER SUPERSEDED
  by card t_b8a19487 — the surface is now the trophy ring, not the text line; the detect-filter
  half of this AT is unchanged. The ring's observable accepts are AT-LENS-RING-1..5 +
  AT-LENS-RING-CAMREL in `docs/design/sunstone-lens-trophy-ring.md` §3.)*
- **AT-LENS-DRAIN-CONST:** the bar falls at the same rate whether 0 or 10 hostiles are nearby
  (AC#4).
- **AT-LENS-ZERO-INERT:** drain the bar to 0 → detection stops, but the Lens stays equipped and
  in inventory (NOT consumed, NO `$msg_broke`); recharge it in sunlight → detection resumes
  (AC#5).
- **AT-LENS-NOREPAIR:** with the Lens partially drained, stand at an Explorer's Bench → the Lens
  has **no working Repair** (the repair affordance does not refill its charge). Same at a vanilla
  Workbench and a Forge → no repair at any station. Charge only ever returns via sunlight
  (regression-paired with AT-LENS-CHARGE). Guards the `m_canBeReparied = false` knob (§3).
- **AT-LENS-VANILLA-ONLY:** no third-party mod code; all hooks are base-game (AC#6).
- **AT-LENS-DOCS:** theme + render method documented (this doc, the dataset entry, PLAYER_GUIDE)
  (AC#7).

## 9. Follow-up (NOT in this card's scope)

- **Sunstone loot economy — SHIPPED** (card t_0445f590 / PR #183,
  `sunstone-loot-economy-impl-spec.md`). Sunstone is wired into the swamp **surface** chest
  DropTable (`Container.m_defaultItems`, `:101726`, primary ~15% per chest) + a rare Draugr
  Elite `CharacterDrop` (`:11321`, secondary ~5% flat), per `swamp-detection-item.md`
  §Sourcing. Daniel locked the rarity knob at 15% / 5% (card t_8f39b5fc). This is now the
  **sole** Sunstone source — the provisional craft (§6) was removed (this card t_c27f985e).
- **Lens HUD art** — v0.1 ships a placeholder indicator; a polished threat-overlay (and a real
  Sunstone/Lens icon PNG) is a v0.x art follow-up per the icon doctrine.
