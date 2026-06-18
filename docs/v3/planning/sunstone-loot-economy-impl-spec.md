---
title: "Sunstone Loot Economy — swamp surface chests + rare Draugr Elite drop (v3 impl spec)"
status: current
card: t_0445f590
parent_card: t_1fcbb780
purpose: "Build-as-shipped spec for the Sunstone dual-source loot economy: a Sunstone DropData injected into the vanilla TreasureChest_swamp Container DropTable (primary, ~15% per chest) and a Sunstone Drop injected into the vanilla Draugr_Elite CharacterDrop (secondary, 5% flat). Records the weight-solve that converts Daniel's locked 15% per-chest target into the vanilla weight-based DropTable's m_weight, the exact vanilla hooks (line-cited + verified against the real client asset), the additive-edit method (ADR-0006: append to a vanilla-owned list, never clone), the registration timing, the named acceptance tests, and the known vanilla-consistent already-populated-chest limitation. Implements the locked sourcing design (docs/design/swamp-detection-item.md §Sourcing, PR #144). Built by engineer-gameplay (card t_0445f590); Daniel gates the merge."
---

# Sunstone Loot Economy — swamp surface chests + rare Draugr Elite drop

The design note ([`swamp-detection-item.md`](../../design/swamp-detection-item.md) §Sourcing,
merged as PR #144) is the locked *what*: Sunstone is **dual-sourced** from swamp **surface**
chests (primary, exploration path) plus a **rare Draugr Elite** combat drop (secondary),
deliberately **NOT** behind the Sunken Crypts. The kanban card **t_0445f590** is the locked
*mechanic*, and Daniel locked the **rarity knob at 15% per chest / 5% per elite kill**
(card t_8f39b5fc, 2026-06-18). This doc is the buildable *how* and the as-shipped record.

> **Clean-side note (ADR-0001):** every decomp line cited here is the base game
> (`assembly_valheim`), which is fair game to read and adapt (repo AGENTS.md). Line numbers
> are from `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` and the live
> field shapes were re-verified against the **real client asset** (UnityPy probe of the Prime
> install, the same method as the parent RE cards t_5e9a4d49 / t_eb405e48). No third-party
> mod code was read or copied.

> **ADR-0006 (load-bearing):** this feature does **NOT** clone or rebuild the vanilla chest /
> Draugr prefabs. It reads each as a live blueprint via `ZNetScene.GetPrefab` (fires no Awake)
> and **appends one entry** to a `List<T>` the vanilla component already owns
> (`Container.m_defaultItems.m_drops` / `CharacterDrop.m_drops`). Appending to a vanilla list
> is a field edit, not the subtractive instantiate-and-strip pattern ADR-0006 forbids.

## 0. SpecCheck manifest impact — NONE (read first)

`Runtime/SpecCheck.cs` validates **recipes and build pieces** (item → resource tuple →
station). It does **not** model DropTables or CharacterDrops, so this feature adds **0**
manifest rows and changes no existing row. The provisional Sunstone *craft* recipe (Daniel
locked **REMOVE**, card t_8f39b5fc) was removed by the architect's card **t_c27f985e** —
which owns the SpecCheck row removal + the `sunstone-lens-impl-spec.md` §6 / dataset edits.
That card and this one landed as separate PRs by design (the decomposition split them); with
both merged, this loot economy is Sunstone's **sole** acquisition path.

## 1. Locked targets (verified vs the real client asset)

| Source | Vanilla prefab | Component | Field edited |
|---|---|---|---|
| PRIMARY — swamp surface chest | `TreasureChest_swamp` | `Container` | `m_defaultItems.m_drops` (DropTable, decomp :101726) |
| SECONDARY — rare elite drop | `Draugr_Elite` | `CharacterDrop` | `m_drops` (List&lt;Drop&gt;, decomp :11340) |
| EXCLUDED — gated dungeon | `TreasureChest_sunkencrypt` | — | **never touched** |

- **One swamp-surface chest prefab covers everything.** RE t_5e9a4d49 (re-confirmed this card)
  found there is **no per-POI chest prefab**: the named swamp POIs (Draugr Village, Ruined
  Tower, Abandoned House/Village, Viking Graveyard, swamp Shipwreck) all **reuse**
  `TreasureChest_swamp` (or generic `loot_chest_wood/stone`). So a single DropTable injection
  reaches every surface chest the design lists.
- **`Draugr_Elite` is capital E.** `Draugr_elite` lowercase is a bare mesh node, not the
  creature (RE t_eb405e48). The root `Draugr_Elite` GameObject carries the `CharacterDrop`
  (Entrails 1.0 + TrophyDraugrElite 0.10) we append to.

## 2. The vanilla DropTable algorithm (why 15% is a weight, not a percent)

`Container.Awake` populates a chest **once**, owner-side, gated by the ZDO flag
`s_addedDefaultItems` (`:101784`), calling `m_defaultItems.GetDropListItems()` (`:101795`).
That method (`DropTable.GetDropListItems`, `:56456`):

1. rolls the table-level `m_dropChance` gate (`:56463`);
2. draws `Random.Range(m_dropMin, m_dropMax+1)` items (`:56474`);
3. each draw is **weighted by `m_weight`** over the running total, and on a hit with
   `m_oneOfEach` the picked entry is **removed** (`:56487-56491`) — i.e. **sampling without
   replacement**.

The live `TreasureChest_swamp` table (read from the real client asset this card):

```
m_dropChance = 1.0   m_dropMin = 2   m_dropMax = 3   m_oneOfEach = true
10 entries, total weight 9.5:
  9 items at weight 1.0  (ArrowIron, ArrowPoison, Coins, Amber, AmberPearl,
                          Ruby, Chain, ElderBark, SledgeWood)
  WitheredBone at weight 0.5   (the rare-tail anchor the design doc named, ~5.3%/slot)
```

So a raw "15%" is **not** a weight and **not** a slot fraction — it is the probability that a
freshly-populated chest **contains at least one Sunstone**. The wiki/UI "Chance %" column is a
per-slot weight fraction; the per-*chest* probability is what a player feels.

## 3. The weight solve (15% per chest → m_weight)

Adding Sunstone at weight `w` makes the table total `9.5 + w` and gives, for `k` draws without
replacement, `P(at least one Sunstone) = 1 − P(never picked in k draws)`. Averaging over the
uniform `k ∈ {2,3}` (the `m_dropMin..m_dropMax` range) and solving `P = 0.15`:

| weight `w` | per-chest P(≥1 Sunstone) | per-slot fraction |
|---|---|---|
| 0.500 | 13.05% | 5.00% |
| **0.584** | **15.00%** | **5.79%** |
| 0.650 | 16.48% | 6.40% |
| 1.000 | 23.76% | 9.52% |
| 1.676 | 35.41% | 15.00% (slot, NOT per-chest) |

`w ≈ 0.584` hits 15.00% per chest and lands Sunstone right at the WitheredBone rare-tail
(~5.8% slot vs 5.3%) the design doc cited as precedent. Both an exact recursion and a faithful
Monte-Carlo of the real `GetDropListItems` algorithm agree to 3 decimals (the 1.676 row is the
trap: that's the weight for a 15% *slot* fraction, ≈35% per chest — **not** what Daniel asked).

`SunstoneLoot.ChestSunstoneWeight = 0.584f` is that solved constant. The solver lives at
`scripts/solve_sunstone_chest_weight.py` (reproducible; reads the live-table shape from this
doc's §2 numbers and re-derives the weight).

## 4. The elite drop (5% flat)

`Draugr_Elite`'s root `CharacterDrop.m_drops` gets one `CharacterDrop.Drop` (class, `:11321`):

```
m_prefab          = SBPR_Sunstone (the ZNetScene GameObject)
m_chance          = 0.05            // 5% per kill (m_chance is 0..1)
m_amountMin/Max   = 1 / 1
m_levelMultiplier = false           // flat 5% regardless of star — a 2★ is not a better
                                    // Sunstone source than a 0★ (mirrors how vanilla authors
                                    // TrophyDraugrElite: chance 0.10, levelMultiplier 0)
m_onePerPlayer    = false
m_dontScale       = true            // one crystal per roll; don't resource-rate scale
```

Secondary by design: the canonical elite farm is Body Piles *inside* Sunken Crypts, so an
elite-only source would quietly route players back into the crypts the design steers away from.
Chests stay primary; the elite drop is a combat-path bonus.

## 5. Architecture & timing

```
Features/Sunstone/
  SunstoneLoot.cs   — the dual injection (chest DropData + elite Drop), idempotent + null-safe.
```

- Runs at the **ZNetScene phase** (`SunstoneLoot.RegisterPrefabs`), dispatched by the Registrar
  **immediately after `SunstoneLens.RegisterPrefabs`** — Sunstone must be a registered ZNetScene
  prefab first. The `DropData`/`Drop` reference the Sunstone **GameObject** (the ZNetScene
  prefab), not the ODB ItemData; vanilla's `AddItemToList` does `m_item.GetComponent<ItemDrop>()`
  at populate time, and the Sunstone GameObject carries an `ItemDrop` (cloned from Coins). So
  **no ObjectDB-phase wiring is needed** for loot.
- **Idempotent.** Each injection checks for an existing Sunstone entry (reference identity)
  before adding, so the `ZNetScene.Awake` postfix re-firing across scene loads never double-adds.
- **Null-safe / fail-quiet.** A missing prefab, Container, or CharacterDrop logs a warning and
  skips that surface rather than throwing — a loot bug must never break world load.
- All gated behind `ServerContext.OnSBServer` via the Registrar.

## 6. Known limitation (vanilla-consistent — for QA / Daniel)

`Container.Awake` populates a chest **once** and records `s_addedDefaultItems` in its ZDO
(`:101784`). Chests already generated **and populated** in an existing save keep their old
contents; Sunstone only appears in chests populated **after** this build loads. QA should sample
**freshly-discovered** swamp chests (or a fresh world). This is exactly how every vanilla
loot-table change behaves — not a Trailborne quirk.

## 7. Observable acceptance tests (named, in-game — logs-green ≠ playable)

- **AT-SUNSTONE-CHEST:** open freshly-discovered `TreasureChest_swamp` chests across swamp POIs
  (Runestone Tower, Draugr Village, Ruined Tower, Viking Graveyard, Shipwreck); Sunstone appears
  at ≈15% of chests over a sufficient sample.
- **AT-SUNSTONE-NOT-CRYPT:** open Sunken Crypt chests; Sunstone **never** appears (the
  `TreasureChest_sunkencrypt` table is untouched).
- **AT-SUNSTONE-ELITE:** kill Draugr Elites (~20+ samples); Sunstone drops at ≈5%, flat across
  star levels.
- **AT-SUNSTONE-BUILD:** build 0 errors / 0 warnings; SpecCheck green (unchanged — this feature
  adds no manifest row); no SBPR exceptions in the boot log.

## 8. Out of scope (other cards)

- **Provisional Sunstone craft-recipe REMOVE** + SpecCheck row + `sunstone-lens-impl-spec.md` §6
  + dataset edits → architect card **t_c27f985e** (Daniel locked REMOVE).
- **In-game QA** of this loot economy on Niflheim → qa-playtest card **t_0aef1243**.
- **Lens / Sunstone icon art** — unrelated v0.x art follow-up.
