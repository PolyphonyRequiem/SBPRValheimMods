# Seer's Stone — Wisp-Eligibility Whitelist (DRAFT for ratification)

> **Status:** 🟡 DRAFT — Daniel's ratification gate. This is the "what gets a wisp"
> list referenced as open fork #5 in the thread seed. Nothing here is locked until
> Daniel approves the ★ column. Once ratified, this folds into `seers-stone.md` and
> the per-prefab whitelist becomes the spec the implementation reads.
>
> **Grounding:** prefab names verified via offline `vprefab list` against the real
> Valheim asset payload; biome assignments + lifecycle notes from the locally
> mirrored Fandom wiki (`~/valheim/sbpr-corpus`). Not vibe-listed.

## The eligibility principle (read first)

The stone reveals **abundance worth remembering** — *"without it a Black Forest is
hours of walking; with it, minutes."* So a wisp is earned by one test:

> **"Would I want to find my way back to this exact spot?"**

- **★ ELIGIBLE** = clustered forage (berries, mushrooms, thistle…), scarce valuables
  (surtling cores, dragon eggs, tin/obsidian…), and meaningful locations (dungeons,
  boss altars, treasure POIs).
- **— INELIGIBLE** = world-floor litter so common it's everywhere — **stone, wood
  (branch), flint**. You trip over these constantly; a wisp on each is visual spam
  and carries zero "remember this spot" value. (Your explicit call.)

Legend: **★** = gets a wisp · **—** = no wisp · **?** = genuine judgment call (listed
again at the bottom; your tiebreak).

> **Note on structure:** the per-biome sections below cover **pickables** (forage + ore). The exhaustive **locations** roster (118 worldgen POIs) is its own section further down (`## LOCATIONS`). Pickables and locations were split so each is a clean single-source table.


---

## A note on the mechanic's reach — ORE RESOLVED (Daniel, 2026-06-25)

The mechanic raycasts for **`Pickable`** components and **`Location`** instances
(`nomap.md` §10). Ore was the contested case; **Daniel ruled "ore yes."** Grounding
(`vprefab inspect`) shows ore splits three ways, and the ruling resolves each:

1. **Pickable ores** — `Pickable_Tin`, `Pickable_Obsidian`, `Pickable_BogIronOre`,
   `Pickable_SulfurRock`. Already `Pickable` → the specced raycast sees them. **★ free.**
2. **Surface deposits** — `MineRock_Copper` (the big copper boulders), `MineRock_Tin`,
   `MineRock_Obsidian`. These are `MineRock` (NOT `Pickable`) but **above-ground +
   raycastable**. Including them = a **bounded mechanic extension**: the stone's raycast
   also accepts `MineRock`/`Destructible` hits. **★ included.** Lifecycle keys off
   *all-mine-areas-depleted* (the `MineRock` "destroyed" state), not `m_picked`.
3. **Buried silver veins** (`silvervein`, `rock3_silver`) — `Destructible`, **buried
   underground**, vanilla-gated behind the **Wishbone**. **— EXCLUDED.** Two reasons,
   both decisive: (a) a wisp on buried silver replaces the Wishbone's signature job;
   (b) it breaks the **just-locked helix rule** (wisp orbits *visible* bounds, rides
   *above ground so it never hides in geometry*) — a buried-vein wisp would glow under
   terrain or over featureless snow. Surface-exposed silver (rare) is fine; buried is out.

**Net rule:** the stone reveals **anything physically visible you'd want to return to** —
pickables, surface ore deposits, and locations. It does **not** reveal buried/underground
nodes (preserves the Wishbone + keeps every wisp attached to a visible object).

---

## MEADOWS — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `RaspberryBush` | Raspberries | ★ | Core forage cluster. Patch-forming. |
| `Pickable_Mushroom` | Mushroom | ★ | Forage. |
| `Pickable_Dandelion` | Dandelion | ★ | Forage/ingredient cluster. |
| `Pickable_Branch` | Wood | — | Floor litter. Everywhere. (your call) |
| `Pickable_Stone` | Stone | — | Floor litter. Everywhere. (your call) |
| `Pickable_Flint` | Flint | — | RULED OUT (Daniel 2026-06-25) — ubiquitous shoreline litter. |


---

## BLACK FOREST — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `BlueberryBush` | Blueberries | ★ | Core forage cluster. |
| `Pickable_Thistle` | Thistle | ★ | Forage/ingredient; glows at night already. |
| `Pickable_Mushroom` | Mushroom | ★ | Forage (also BF). |
| `Pickable_Tin` | Tin (small pickable) | ★ | Scarce resource; finding tin is a known slog. |
| `Pickable_SurtlingCoreStand` | Surtling Core | ★ | High-value, gated to chambers. |
| Copper / large Tin rock | — | **(mine — out of mechanic)** unless #2 flips. |


---

## SWAMP — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `Pickable_Thistle` | Thistle | ★ | Forage (also Swamp). |
| `Pickable_Mushroom` | Mushroom | ★ | Forage (also Swamp). |
| `Pickable_Turnip` / wild yellow clusters | Turnip seeds | ★ | Only wild source of the crop. |
| `Pickable_BogIronOre` | Bog iron (pickable form) | ★ | Resource. |
| Scrap iron muddy piles | — | **(mine — out of mechanic)** — `MineRock`, not Pickable. |


---

## MOUNTAIN — pickables  *(← the stone unlocks HERE)*

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `Pickable_Obsidian` | Obsidian | ★ | Resource (pickable form). |
| `Pickable_MountainCaveCrystal` | Crystal | ★ | Scarce; note crystal is the stone's *own* gate material. |
| `Pickable_DragonEgg` | Dragon Egg | ★ | Very high value (Moder summon). Always pin. |
| Silver | — | **(mine — out of mechanic)** unless #2 flips. |


---

## PLAINS — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `CloudberryBush` | Cloudberries | ★ | Core forage cluster. |
| `Pickable_Barley_Wild` | Barley | ★ | Only source — Fuling villages only. |
| `Pickable_Flax_Wild` | Flax | ★ | Only source — Fuling villages only. |
| `Pickable_Tar` / `Pickable_TarBig` | Tar | ★ | Resource, clustered at tar pits. |


---

## MISTLANDS — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `Pickable_Mushroom_Magecap` | Magecap | ★ | Forage. |
| `Pickable_Mushroom_JotunPuffs` | Jotun Puffs | ★ | Forage. |
| `Pickable_DvergrLantern`/`DvergrStein`/`DvergrMineTreasure` | Dvergr loot | ★ | Scarce valuables on Dvergr structures. |
| Sap / Soft tissue / Black marble | — | **(mine — out of mechanic)**. |


---

## ASHLANDS — pickables

**Pickables**
| Prefab | Item | ★? | Why |
|---|---|:--:|---|
| `Pickable_Fiddlehead` | Fiddlehead | ★ | Forage (in ruins). |
| `Pickable_SulfurRock` | Sulfur | ★ | Resource (pickable form). |
| `Pickable_VoltureEgg` | Volture Egg | ★ | Scarce valuable. |


---

## LOCATIONS — exhaustive worldgen roster (Daniel: "make it exhaustive, not sampled")

> **Grounding:** the complete vanilla worldgen POI placement table
> (`~/valheim/sbpr-corpus/wiki/fandom/Points_of_interest.md` — the IronGate
> `ZoneSystem` location list), cross-checked against the prefab roster
> (`vprefab list`). **118 locations, Meadows→Mistlands, none sampled.** Loot column
> is the in-game contents that decide ★. **Ashlands caveat:** the mirrored wiki
> table predates the Ashlands worldgen patch — only `Meteorite` is captured from
> placement data; the Charred Fortress / Ashlands ruins / Fader altar exist in the
> prefab roster but lack biome-precise placement rows here, so I've NOT fabricated
> them. Flag for the impl pass: dump Ashlands locations from a live world.

### The ★ rule is per-KIND, not per-location (the authored knob)

Rather than 118 hand-set flags, ★ falls out of the location's **kind** — which
matches how SBPR ships these (an authored per-kind table + per-row overrides, not a
baked formula). The default rule:

| Kind | ★ | Rationale |
|---|:--:|---|
| **boss-altar** | ★ | summon site — you return |
| **vendor** | ★ | Haldor / Hildir — high-value return |
| **dungeon** | ★ | the tier's main loot delve |
| **loot-structure** | ★ | has a chest / named resource |
| **resource-poi** | ★ | tar pit, drake nest, gucksack, meteorite |
| **lore** | ★ | runestones/waymarkers (**Daniel: yes, 2026-06-25**) |
| **spawn-temple** | ★ | unique landmark (your spawn) |
| **spawner-camp** | — | greydwarf nest / surtling firehole — respawning combat, no "go back" value (per camps ruling) |
| **decor** | — | statues, viaducts, stone circles — no loot |

Per-row exceptions carry **—?** : a `loot-structure` that on inspection has **no
chest** (just mobs) — `StoneHouse4`, `SwampWell1`, and the three ruined Mistlands
guard towers. Lean — (nothing to return for), but flagged for your eye.

> 🔴 **DENSITY FLAG (tuning, not a list error).** Under the kind-rule, **103 of 118
> locations get a wisp.** Since the stone unlocks at **Mountains**, in the endgame
> where you actually wear it the map could light up densely. Three levers if that's
> too much in playtest, each a knob not a redesign: (a) wisp only **undiscovered**
> locations (winks out once you've visited — leans on vanilla's `DiscoverLocation`);
> (b) wisp only **unlooted** structures (chest still sealed); (c) a per-wearer
> **range cap** so only nearby locations show. My lean: ship (a) — "the lens reveals
> what you haven't found yet" is the cleanest fantasy and self-declutters. Your call
> at impl time; doesn't block the list.


### Meadows  (21)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `StartTemple` | spawn-temple | ★ | your spawn vegvisir — unique landmark |
| `Eikthyrnir` | boss-altar | ★ | Eikthyr summon |
| `StoneCircle` | decor | — | Ancient Stone Circle — no loot |
| `WoodHouse1` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse2` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse3` | loot-structure | ★ | beehive + branch/stone |
| `WoodHouse4` | loot-structure | ★ | beehive |
| `WoodHouse5` | loot-structure | ★ | beehive |
| `WoodHouse6` | loot-structure | ★ | Meadows chest + beehive |
| `WoodHouse7` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse8` | loot-structure | ★ | dandelion 0-6 |
| `WoodHouse9` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse10` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse11` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodHouse12` | loot-structure | ★ | Meadows chest 50% + mushroom |
| `WoodHouse13` | loot-structure | ★ | Meadows chest 50% + beehive |
| `WoodFarm1` | dungeon | ★ | Meadows Farm, 20-30 rooms |
| `WoodVillage1` | dungeon | ★ | Meadows Village, 20-30 rooms |
| `ShipSetting01` | loot-structure | ★ | Viking Graveyard — treasure + chest |
| `Runestone_Meadows` | lore | ★ | runestone |
| `Runestone_Boars` | lore | ★ | runestone (spawns boars) |

### Meadows/BF  (3)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `Dolmen01` | loot-structure | ★ | Stone Grave — treasure 10% + bones |
| `Dolmen02` | loot-structure | ★ | Stone Grave — treasure 20% + bones |
| `Dolmen03` | loot-structure | ★ | Stone Grave — treasure 30% + bones |

### Black Forest  (18)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `GDKing` | boss-altar | ★ | The Elder summon |
| `Vendor_BlackForest` | vendor | ★ | Haldor the trader |
| `Crypt2` | dungeon | ★ | Burial Chambers — surtling cores |
| `Crypt3` | dungeon | ★ | Burial Chambers — surtling cores |
| `Crypt4` | dungeon | ★ | Burial Chambers — surtling cores |
| `TrollCave02` | dungeon | ★ | Troll Cave — chest + yellow mushroom |
| `Ruin1` | loot-structure | ★ | BF chest + greydwarves |
| `Ruin2` | loot-structure | ★ | BF chest + Elder vegvisir 30% |
| `StoneHouse3` | loot-structure | ★ | BF chest |
| `StoneHouse4` | loot-structure | —? | greydwarves only — no chest |
| `StoneTowerRuins03` | loot-structure | ★ | BF chest + Elder vegvisir |
| `StoneTowerRuins07` | loot-structure | ★ | BF chest upper level |
| `StoneTowerRuins08` | loot-structure | ★ | BF chest upper floor |
| `StoneTowerRuins09` | loot-structure | ★ | BF chest topmost floor |
| `StoneTowerRuins10` | loot-structure | ★ | BF chest topmost floor |
| `Greydwarf_camp1` | spawner-camp | — | Greydwarf nest — respawning spawner |
| `Runestone_Greydwarfs` | lore | ★ | runestone |
| `Runestone_BlackForest` | lore | ★ | runestone |

### Swamp  (15)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `Bonemass` | boss-altar | ★ | Bonemass summon |
| `SunkenCrypt4` | dungeon | ★ | Sunken Crypt — iron (key-gated) |
| `Grave1` | loot-structure | ★ | Swamp chest 50% + bonepiles |
| `SwampRuin1` | loot-structure | ★ | Bonemass vegvisir 30% + chest |
| `SwampRuin2` | loot-structure | ★ | Bonemass vegvisir 30% + chest |
| `SwampHut1` | loot-structure | ★ | BF chest 10% + wraith |
| `SwampHut2` | loot-structure | ★ | BF chest 10% + wraith |
| `SwampHut3` | loot-structure | ★ | BF chest 10% + wraith |
| `SwampHut4` | loot-structure | ★ | BF chest 75% + draugr |
| `SwampHut5` | loot-structure | ★ | BF chest 10% + wraith |
| `SwampWell1` | loot-structure | —? | draugr elite only — no chest |
| `FireHole` | spawner-camp | — | Surtling spawner — respawns 5min |
| `InfestedTree01` | resource-poi | ★ | Gucksack — guck resource |
| `Runestone_Draugr` | lore | ★ | runestone (spawns draugr) |
| `Runestone_Swamps` | lore | ★ | runestone |

### Mountain  (14)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `Dragonqueen` | boss-altar | ★ | Moder summon |
| `MountainCave02` | dungeon | ★ | Frost Cave — fenris/jute/gold |
| `StoneTowerRuins04` | loot-structure | ★ | Moder vegvisir 70% + 2 chests |
| `StoneTowerRuins05` | loot-structure | ★ | Mountain chest + skeletons |
| `AbandonedLogCabin02` | loot-structure | ★ | Mountain chest + stone golem |
| `AbandonedLogCabin03` | loot-structure | ★ | Mountain chest |
| `AbandonedLogCabin04` | loot-structure | ★ | Mountain chest |
| `MountainGrave01` | loot-structure | ★ | bones + silver necklace 50% |
| `MountainWell1` | loot-structure | ★ | Mountain chest 75% |
| `DrakeNest01` | resource-poi | ★ | Dragon egg — respawns 8h |
| `Waymarker01` | lore | ★ | waymarker |
| `Waymarker02` | lore | ★ | waymarker |
| `DrakeLorestone` | lore | ★ | lorestone |
| `Runestone_Mountains` | lore | ★ | runestone |

### Plains  (15)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `GoblinKing` | boss-altar | ★ | Yagluth summon (Stonehenge) |
| `GoblinCamp2` | dungeon | ★ | Fuling Village — 15-25 rooms |
| `Ruin3` | loot-structure | ★ | Plains chest + fulings |
| `StoneTower1` | loot-structure | ★ | Fuling totem + Plains chest |
| `StoneTower3` | loot-structure | ★ | Plains chest + fulings |
| `StoneHenge1` | loot-structure | ★ | Yagluth vegvisir + chest |
| `StoneHenge2` | loot-structure | ★ | Stonehenge chest + berserkers |
| `Stonehenge3` | loot-structure | ★ | Yagluth vegvisir + chest |
| `Stonehenge4` | loot-structure | ★ | Yagluth vegvisir + berserkers |
| `Stonehenge5` | loot-structure | ★ | Yagluth vegvisir + fulings |
| `Stonehenge6` | decor | — | no loot |
| `TarPit1` | resource-poi | ★ | Tar (4 big + 12 small) + growths |
| `TarPit2` | resource-poi | ★ | Tar (4 big + 8 small) + growths |
| `TarPit3` | resource-poi | ★ | Tar (3 big + 6 small) + growths |
| `Runestone_Plains` | lore | ★ | runestone |

### Mistlands  (27)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `Mistlands_DvergrTownEntrance1` | dungeon | ★ | Infested Mine — black cores/jelly |
| `Mistlands_DvergrTownEntrance2` | dungeon | ★ | Infested Mine — black cores/jelly |
| `Mistlands_DvergrBossEntrance1` | dungeon | ★ | Infested Citadel — sealbreaker |
| `Mistlands_GuardTower1_new` | loot-structure | ★ | Dvergr lanterns + crates |
| `Mistlands_GuardTower1_ruined_new` | loot-structure | —? | seekers — ruined, sparse |
| `Mistlands_GuardTower1_ruined_new2` | loot-structure | —? | seekers — ruined, sparse |
| `Mistlands_GuardTower2_new` | loot-structure | ★ | Dvergr lanterns + crates |
| `Mistlands_GuardTower3_new` | loot-structure | ★ | Dvergr crates + barrels |
| `Mistlands_GuardTower3_ruined_new` | loot-structure | —? | seekers + yggdrasil — ruined |
| `Mistlands_Lighthouse1_new` | loot-structure | ★ | Dvergr lanterns + crate |
| `Mistlands_Excavation1` | loot-structure | ★ | Giant skull + soft tissue + crates |
| `Mistlands_Excavation2` | loot-structure | ★ | Giant ribs + crates |
| `Mistlands_Excavation3` | loot-structure | ★ | Dvergr crates |
| `Mistlands_Harbour1` | loot-structure | ★ | Dvergr crates (coastal) |
| `Mistlands_Giant1` | loot-structure | ★ | Ancient sword 25% + soft tissue |
| `Mistlands_Giant2` | loot-structure | ★ | Giant ribs |
| `Mistlands_Swords1` | loot-structure | ★ | Ancient sword + armor |
| `Mistlands_Swords2` | loot-structure | ★ | Ancient sword + armor |
| `Mistlands_Swords3` | loot-structure | ★ | Ancient swords |
| `Mistlands_Viaduct1` | decor | — | no loot |
| `Mistlands_Viaduct2` | decor | — | no loot |
| `Mistlands_RockSpire1` | decor | — | no loot |
| `Mistlands_StatueGroup1` | decor | — | no loot |
| `Mistlands_Statue1` | decor | — | no loot |
| `Mistlands_Statue2` | decor | — | no loot |
| `Mistlands_RoadPost1` | lore | ★ | road post |
| `Runestone_Mistlands` | lore | ★ | runestone |

### Ocean/coast  (4)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `ShipWreck01` | loot-structure | ★ | Shipwreck chest 75% (BF/Swamp/Plains/Ocean) |
| `ShipWreck02` | loot-structure | ★ | Shipwreck chest 75% |
| `ShipWreck03` | loot-structure | ★ | Shipwreck chest 75% |
| `ShipWreck04` | loot-structure | ★ | Shipwreck chest 75% |

### Ashlands  (1)
| Location prefab | Kind | ★ | Loot / why |
|---|---|:--:|---|
| `Meteorite` | resource-poi | ★ | Glowing metal + surtlings |

---

## Lifecycle footnote that affects the whitelist (grounded)

Per the wiki: **raspberry/blueberry bushes do NOT regrow if the *plant* is removed**,
but **mushrooms regrow even if the ground is cleared/excavated**. This matters for our
"wisp leaves only on destruction" rule:
- Berry **bush** destroyed → patch gone for good → wisp correctly never returns.
- **Mushroom** "destroyed" (ground cleared) → it *does* respawn → wisp should return.

So "destroyed" is **per-prefab**, keyed off whether the prefab's pickable spawn is
gone permanently vs on a respawn timer. The doc will spec this off the `Pickable`
respawn fields, not a blanket rule.

---

## Judgment calls — RESOLVED (Daniel, 2026-06-25)

1. **Flint** — **— NO wisp.** (Ubiquitous shoreline litter; and you don't have the
   stone until Mountains, by which point flint is irrelevant.)
2. **Ore deposits** — **★ YES**, resolved in full in "the mechanic's reach" section
   above: pickable ores + **surface** `MineRock` deposits get wisps (bounded raycast
   extension); **buried silver veins excluded** (preserves Wishbone + respects the
   visible-bounds helix rule).
3. **Runestones** — **★ YES wisp.** (Daniel's call — lore POIs are worth marking.)
4. **Combat camps** — **agreed as-leaned (for now):** **★** fixed loot structures
   (Draugr village huts, Fuling structures); **—** roaming creatures / respawning
   nests (Greydwarf nests, Stone Golems) — consistent with creatures being out of
   scope per fork #2. Revisitable later.

**ALL FORKS CLOSED.** Next: write `seers-stone.md` and fold the three tier
reconciliations (PLAYER_GUIDE / MILESTONES / playtest-1 → Mountains) into one
spec-first PR for Daniel to gate.
