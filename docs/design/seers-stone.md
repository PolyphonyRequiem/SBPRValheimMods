---
title: "Seer's Stone — the Explorer's wisp-lens (v4 / Mountains)"
status: current
purpose: "The consolidated design + as-built spec for the Seer's Stone: a crystal-gated Mountains-tier Utility accessory that reveals personal 'wisps' over eligible forage/ore/locations, pinnable by look (Alt+E). Locks the decisions from Daniel's #design thread (2026-06-25) and records the v0.x as-built (M1–M4)."
---

# Seer's Stone — the Explorer's wisp-lens (v4 / Mountains)

> **Status: BUILT (M1–M4), render-verified, pending Daniel's in-game accept.**
> The whitelist substrate, the item, the wisp field, and pin-by-look are implemented,
> build 0/0, 451 unit tests green, and the wisp render + helix motion were captured on
> a real GPU client (Prime, Niflheim). The wisp glow + item icon are **placeholders**
> (the demister_ball effect / magenta-fallback icon) — visual polish is flagged, not a
> blocker. logs-green ≠ playable: the final feel is Daniel's eye.

## 1. What it is (one paragraph)

The **Seer's Stone** is the Explorer's signature item — a sunfinder's lens bound in
silver, worn in the **Utility slot** (the Megingjord/Wishbone slot). While worn, the
world whispers: faint **wisps** drift over clusters of interesting things — a berry
patch, an ore deposit, a dungeon entrance — visible **to you alone** because you wear
the lens. Look at a wisp and press **Alt+E**, and that thing goes onto your map. Take
the stone off and the wisps vanish; the world looks ordinary again. The stone is why
the Explorer is load-bearing: without it a biome is hours of walking; with it, minutes.

## 2. Locked decisions (Daniel, #design thread 2026-06-25)

| # | Decision | Lock |
|---|----------|------|
| Tier | **Mountains / crystal-gated** — sole v4 headline (the locked spec wins over the narrative docs that mis-tiered it to Black Forest; those are reconciled — §7). | LOCKED |
| Scope | **Pickables + Locations + SURFACE ore.** NO creatures (not a hunting tool). NO buried silver veins (preserves the Wishbone + a wisp can't ride a buried object's bounds). | LOCKED |
| Eligibility | A **pre-packaged, owner-editable whitelist** (`BepInEx/config/<mod>/`), seeded on first run. **IGNORE-UNLISTED**: unlisted prefab → no wisp; the owner's edit is the opt-in path. | LOCKED |
| Pin label | The friendly name only — **NO count** ("Blueberries", not "Blueberries ×12"). | LOCKED |
| Pin model | The **pin is a frozen memory**; the **wisp is the live eye**. Pins default **private** (the lens is personal). | LOCKED |
| Radius | **One R**, default **15 m** (merge-radius == abundance-radius), authored per-prefab override. | LOCKED |
| Wisp motion | **Helix on a cylinder**: the patch centroid is the axis; the wisp rides the WALL — a slow perimeter **orbit** at radius `bounds + margin` (floats just outside the foliage), plus a slow **vertical sine** bob. Ground-aware Y (samples ground at the orbit point so it never clips the uphill side on a slope). Extends **slightly beyond** the bounds so it's never hidden in geometry. | LOCKED |
| Wisp lifecycle | Wisps are **personal + client-only** (no networking, no ZDO). A wisp marks an object's **existence**, not its current fruit: it persists while the source object exists (picked-bare is fine), and only leaves when the object is destroyed/cleared, leaves range, or the stone comes off. | LOCKED |
| Abundance | The **wisp IS the spawn-time group aggregate** (no count shown), so pinning the wisp pins the **whole patch** as one pin — one press, the patch is recorded. | LOCKED |

## 3. The whitelist substrate (M1) — as built

- **File:** `seers_stone_whitelist.yaml` in `BepInEx/config/SBPR.Trailborne/`, **seeded on
  first run** from the shipped `seers_stone_whitelist.default.yaml` (overlaid into the
  plugin folder by `pack-modpack.sh`, the icons/textures precedent). Delete the live file
  to re-seed.
- **Format:** a flat, comment-friendly YAML-subset — `version:` + three sections
  (`pickables`, `ore_surface`, `locations`), each a list of bare prefab names. Eligibility
  is their **union**.
- **No serializer dependency.** Parsed by a hand-written engine-free scanner
  (`WhitelistDocument`), NOT YamlDotNet. Rationale (Daniel's stated concern was
  *assembly-load version collisions from shipped libs*): the cleanest answer to "don't
  collide" is "don't ship the lib." The file stays valid in any YAML editor, so if the
  schema ever grows nested structure we can swap in YamlDotNet without changing the file
  format. **Zero dependency = zero collision surface.**
- **Default roster:** 132 vanilla entries — 20 pickables, 9 surface-ore, 103 locations —
  grounded against the real `ZoneSystem` POI placement table + the `vprefab` prefab roster
  (not memory). See [`seers-stone-pickable-whitelist.md`](seers-stone-pickable-whitelist.md)
  for the exhaustive per-biome roster + the ★-by-kind rule.
- **Ore split (the contested case, resolved):** pickable ores + above-ground `MineRock`
  deposits (copper boulders, tin/obsidian) **wisp**; **buried silver veins do NOT** (Wishbone
  preservation + the helix needs a visible object).

### Engine-free cores (CI-gated headless)
- `SeersStoneEligibility` — normalize (strip `(Clone)`, case/space) + match; **ignore-unlisted**;
  empty-set fail-safe (a missing/corrupt config reveals nothing, never everything).
- `WhitelistDocument` — the flat parser (comments, sections, version, dedup union).

## 4. The item (M2) — as built

- `SBPR_SeersStone`, an **ItemDrop accessory in the Utility slot** (`ItemType.Utility`),
  built **additively** (ADR-0006 — `Assets.TryConstructItemShell`, no vanilla clone),
  the Cartographer's Kit sibling pattern.
- **Recipe (crystal-gated, Mountains):** Crystal ×5 + Silver ×2 + JuteRed ×2 at the Forge.
  Numbers are eyeball, Daniel's to tune — authored as the single source.
- **Worn-detection:** `SeersStone.IsWearing(player)` via the public
  `Inventory.GetEquippedItems()` + `m_dropPrefab` name (m_utilityItem is protected) — the
  exact CartographersKit idiom.
- **Icon:** `seers_stone_v0.1.png` placeholder; crash-safe magenta fallback if absent.

## 5. The wisp field (M3) — as built

- **`WispMotion`** (engine-free) — the helix solver: `HorizontalOffset` (orbit on the
  cylinder wall, radius `bounds+margin`) + `VerticalHeight` (the sine bob). Unit-tested:
  radius invariant, orbit closure over period, bob amplitude band, phase-offset spread,
  divide-by-zero safety. **Render-verified on Prime:** measured offsets were 2.75 m radius
  (bounds 2.0 + margin 0.75) at all three capture samples, ~90° apart, Y bobbing 1.12–1.85 m.
- **`WispBehaviour`** (engine-side) — drives one wisp's transform off `WispMotion`, with the
  **ground-aware-Y** read (`ZoneSystem.GetGroundHeight` at the orbit point). Carries the
  source identity for pin-by-look.
- **`WispField`** (engine-side) — the per-frame manager on the local player
  (`SeersStoneFieldHost`, attached on `Player.OnSpawned`). While the stone is worn, a
  **throttled OverlapSphere scan** (1 Hz, 60 m) finds eligible Pickables + Locations and
  reconciles the wisp set (spawn new, despawn gone/out-of-range); stone off → tear down all.
  Wisps are children of the host (destroyed on logout/death).
- **Visual:** the `demister_ball` glow subtree (Point light + particles) grafted ZNetView-free
  (placeholder; polish flagged).

## 6. Pin-by-look (M4) — as built

- **Input:** a `Player.Update` postfix; while the stone is worn, **Alt+E** raycasts the camera
  forward (50 m). Hit → resolve to Pickable or Location.
- **Decision:** `SeersStonePinDecision.Decide` (engine-free) — re-checks eligibility (defense in
  depth), cleans the label (**strips any count**), checks **merge-radius dedup** (same-name pin
  within 15 m → no duplicate), defaults **private**. Unit-tested across all branches.
- **Placement:** Pickable → `Minimap.AddPin(pos, Icon3, label, save:true, isChecked:false)` (the
  abundance pin — one pin for the patch). Location → `Minimap.DiscoverLocation(pos, Icon3, label)`.

## 7. Tier reconciliation (the corpus mis-tiered it)

The locked spec (`requirements.md`, `PARKED-2026-06-03.md`, `initialization.md`) puts the
Seer's Stone at **v4 / Mountains / crystal**. Three narrative/planning docs still placed it at
Black Forest / copper and are corrected to point here:
- `PLAYER_GUIDE.md` — the "(copper, surtling core, greydwarf eyes)" recipe line + the Black
  Forest framing.
- `MILESTONES.md` — the "Black Forest tier (… Seer's Stone) = v0.2.0+" line.
- `playtest-1-expected-progression.md` — the Black Forest placement.

> **Consequence flagged (Daniel to confirm):** at Mountains the stone lands **two biomes after**
> the Black Forest grind it was originally pitched to relieve. That makes it a **late payoff** that
> trivializes Mountains-onward + backtracking, rather than early-game fatigue relief. If early relief
> is wanted, that's a *different, smaller* tool and the Seer's Stone stays the Mountains-grade version.
> Recorded as an open note; the tier itself is locked.

## 8. Acceptance tests (named)
- [ ] **AT-STONE-CRAFT:** the Seer's Stone is craftable at the Forge with Crystal+Silver+JuteRed; uncraftable without crystal.
- [ ] **AT-WISP-ELIGIBLE:** wearing the stone, wisps appear over eligible pickables/ore/locations and NOT over stone/wood/flint/creatures/buried silver.
- [ ] **AT-WISP-HELIX:** a wisp orbits the patch perimeter (radius = bounds+margin) and bobs vertically; it rides above local ground on a slope. *(render-verified on Prime 2026-06-25.)*
- [ ] **AT-WISP-PERSONAL:** a second player without a stone sees no wisps.
- [ ] **AT-WISP-LIFECYCLE:** a wisp persists after the patch is picked bare; leaves when the object is destroyed or the stone is removed.
- [ ] **AT-PIN-LOOK:** Alt+E while looking at a wisp's source pins it ("Blueberries", no count); a second look within 15 m does not double-pin.
- [ ] **AT-OWNER-EDIT:** editing the config whitelist (add/remove a prefab) changes what wisps; deleting the file re-seeds the default.
- [ ] logs-green ≠ playable: every AT closes only on Daniel's in-game check.

## 9. Known placeholders / polish (not blockers)
- **Wisp glow** is the raw demister_ball effect — may want bespoke color/size/intensity, or a
  face-bias anti-occlusion tweak (parked; free because wisps are per-wearer).
- **Item icon** is a v0.1 placeholder (magenta fallback if the PNG is absent).
- **Density:** under the kind-rule 103/118 locations are eligible; three declutter levers
  (undiscovered-only / unlooted-only / range-cap) are parked in the whitelist doc if the
  endgame map gets busy.
- **Recipe numbers** are eyeball pending Daniel's tune.
