---
title: "QA — Sunstone loot economy in-game verification (Niflheim, dual-source drops + recipe removal)"
status: current
purpose: PASS/FAIL evidence that the Sunstone dual-source loot economy (PR #183) and the provisional-recipe removal (PR #186) are live and correct at the data layer a joined client samples loot from — verified on Niflheim against a freshly-built `main` DLL. Verified-vs-reasoned split is explicit (logs-green ≠ playable).
task: t_0aef1243
parents: [t_0445f590, t_c27f985e]
verified_against: main @ c7463ac (contains PR #183 6e01c83 + PR #186 c7463ac)
dll_md5: 7a76a882fddae790cde5c979dfd414dc
---

# QA — Sunstone loot economy in-game verification

**Scope.** Smoke-test the Sunstone loot implementation on Niflheim per the impl
spec acceptance tests (`docs/v3/planning/sunstone-loot-economy-impl-spec.md` §7):
AT-SUNSTONE-CHEST, AT-SUNSTONE-NOT-CRYPT, AT-SUNSTONE-ELITE, AT-SUNSTONE-BUILD,
plus the recipe-removal call from the parent reconciliation card (t_c27f985e:
Sunstone is loot-sourced only, the provisional craft is gone).

**Honesty boundary (load-bearing, per AGENTS.md "logs green ≠ playable").** A
Valheim dedicated server runs `-nographics` with **no local `Player`** — it never
opens a chest or kills a Draugr Elite, so the actual *drop events* cannot be
observed headless. This QA therefore verifies the **server-side loot DATA layer a
joined client samples from**: the live `TreasureChest_swamp` / `TreasureChest_sunkencrypt`
`Container.m_defaultItems` DropTables and the `Draugr_Elite` `CharacterDrop.m_drops`,
read **after** SBPR's `ZNetScene.Awake` wiring settles, and it drives Valheim's own
DropTable selection algorithm over the **live injected weights** to confirm the
per-chest rate empirically. **What is reasoned, not observed:** the last mile — a
joined client looting a freshly-discovered chest / getting an elite kill and the
Sunstone landing pickable in inventory. Daniel's in-game run closes that. See
"Remaining client-only risks" at the bottom.

## How this was verified

- **Build.** Clean `main` (`c7463ac`) built with
  `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` →
  **0 errors, 0 warnings** (`<TreatWarningsAsErrors>` is ON). DLL md5
  `7a76a882fddae790cde5c979dfd414dc`. The structural xUnit suite ran **27/27 pass**.
- **Weight solver.** `scripts/solve_sunstone_chest_weight.py` reproduces
  `ChestSunstoneWeight = 0.584` two ways (exact recursion + 2M-trial Monte-Carlo):
  `m_weight = 0.58418 → exact per-chest P = 0.15000` (Daniel's 15% lock).
- **Deploy.** Fresh DLL + the full 15-icon set deployed to Niflheim
  (`lloesche/valheim-server`, world `niflheim`); `valheim-server` restarted.
  (The DLL previously on the box was a stale Jun-13 build `fec594bf…` that predated
  PR #183/#186 — this QA rebuilt and redeployed `main` to verify the right artifact.)
- **Data-layer probe.** A throwaway read-only companion BepInEx plugin
  (`SBPR.QADiag`, clean-room: vanilla public API + reflection only; ordered
  `[HarmonyPriority(Last)] [HarmonyAfter("net.danielgreen.sbpr.trailborne")]`)
  dumped the settled swamp + crypt DropTables and the Draugr Elite CharacterDrop
  **after** SBPR's injection, and ran 200,000 draws of the vanilla selection loop
  over the live table. It was an instrument only and has been **removed from both
  the config and data plugin dirs** — the final live boot confirms **0** QADiag lines.

---

## PASS / FAIL checklist

### ✅ AT-SUNSTONE-BUILD — 0 err / 0 warn, SpecCheck green, no SBPR exceptions → **PASS**

Final clean boot (11:18, DLL `7a76a882`, QADiag removed):

```
[Trailborne/Sunstone] Registered Sunstone material item: SBPR_Sunstone
[Trailborne/SunstoneLoot] Added SBPR_Sunstone to TreasureChest_swamp DropTable (weight 0.584 → ~15% per chest). Sunken Crypt table untouched.
[Trailborne/SunstoneLoot] Added SBPR_Sunstone to Draugr_Elite CharacterDrop (chance 0.05 = 5%, flat across stars).
[Trailborne/Sunstone] Sunstone ObjectDB wiring complete (Sunstone material + Lens trinket + 1 recipe).
[Trailborne/SpecCheck] ✓ All 27 recipes match the v0.1.0 spec manifest; 14 item icon(s) loaded (no fallback placeholders); 14 item(s) have non-null m_attack (no tooltip-NRE landmine).
```

- SBPR-tagged **Error** lines on the live boot: **0**.
- One HarmonyX `ZNetScene::Awake … Illegal byte sequence` error appears at 11:18:05
  — this is the **previous process's shutdown teardown** (`OnApplicationQuit` 11:18:02,
  "Steam manager on destroy" 11:18:05), the known BepInEx `UnityPatches.GetCodeBase`
  teardown noise. The **live** boot (11:18:09+) has **zero** Harmony patch failures.
  **Not an SBPR defect.**
- ⚠️ First boot of this DLL went SpecCheck-RED with 2 `ICON MISSING` drifts
  (SunstoneLens, IronCompass). Root cause was a **partial deploy on my side** — I
  copied only the DLL, not the 3 new icon PNGs (`sunstone_v0.1`, `sunstone_lens_v0.1`,
  `iron_compass_v0.1`) that `main` references. After deploying the full icon set
  (what `scripts/pack-modpack.sh` ships), SpecCheck is **green**. Not a code defect.

### ✅ AT-SUNSTONE-CHEST — Sunstone in swamp surface chest table at ~15%/chest → **PASS (data layer)**

Live `TreasureChest_swamp` `Container.m_defaultItems` after SBPR wiring (QADiag dump):

```
TreasureChest_swamp DropTable: dropChance=1  draws=2-3  oneOfEach=True  entries=11  totalWeight=10.084
   WitheredBone w=0.5  | ArrowIron w=1 | ArrowPoison w=1 | Coins w=1 | Amber w=1
   AmberPearl w=1 | Ruby w=1 | Chain w=1 | ElderBark w=1 | SledgeWood w=1
   SBPR_Sunstone w=0.584  stack=1-1          ← injected
   Sunstone present: YES (weight 0.584)
   Empirical per-chest P(≥1 Sunstone) over 200,000 draws of the vanilla selection
   algorithm on the LIVE table = 15.01%   (Daniel's lock: 15.00%)
```

The injected DropData matches spec exactly: `m_item=SBPR_Sunstone`, `m_weight=0.584`,
`stack 1-1`. Driving Valheim's own weighted-draw-without-replacement loop (2-3 draws,
`m_oneOfEach`) over the **live** 11-entry table yields **15.01%** per chest — i.e. the
game samples Sunstone at Daniel's locked 15%. The single swamp-surface chest prefab
covers every named swamp POI (all reuse `TreasureChest_swamp` per RE t_5e9a4d49).

### ✅ AT-SUNSTONE-NOT-CRYPT — Sunstone NEVER in Sunken Crypt chests → **PASS**

Live `TreasureChest_sunkencrypt` table is a **distinct** object (10 entries, has
`IronScrap w=2`, no Sunstone):

```
TreasureChest_sunkencrypt DropTable: entries=10  totalWeight=10.5
   WitheredBone | ArrowIron | ArrowPoison | Coins | Amber | AmberPearl | Ruby | Chain | ElderBark | IronScrap
   Sunstone present: NO   → EXPECT Sunstone=NO (excluded crypt) → PASS
```

The injection targets only `TreasureChest_swamp`; the crypt table is untouched, as the
boot log also states ("Sunken Crypt table untouched"). The gated-dungeon loop the design
steers away from yields no Sunstone.

### ✅ AT-SUNSTONE-ELITE — Sunstone drops from Draugr Elite at 5% flat → **PASS (data layer)**

Live `Draugr_Elite` `CharacterDrop.m_drops` after SBPR wiring:

```
Draugr_Elite CharacterDrop (3 entries):
   Entrails           chance=1    amt=2-3  lvlMult=True
   TrophyDraugrElite  chance=0.1  amt=1-1  lvlMult=False
   SBPR_Sunstone      chance=0.05 amt=1-1  lvlMult=False        ← injected
   Sunstone elite drop present: YES (chance 0.05 = 5%, levelMultiplier=False) → PASS
```

Matches spec: `m_chance=0.05`, `m_amountMin/Max=1`, `m_levelMultiplier=false` (flat 5%
regardless of star — a 2★ elite is not a better source than a 0★).

### ✅ Recipe removal (per t_c27f985e) — Sunstone material NOT craftable → **PASS**

Daniel's call was **REMOVE** (loot-only). Verified three ways:

1. **Live boot:** Sunstone ObjectDB wiring logs `(Sunstone material + Lens trinket +
   **1 recipe**)` — the single recipe is the **Lens**, not the material. SpecCheck
   counts **27** recipes with the Sunstone-material row absent (DropTables aren't
   manifested; the material has no recipe row).
2. **Source (`main`):** `AddSunstoneRecipe()` + `SunstoneIronCost` / `SunstoneCrystalCost`
   consts are **fully removed** (grep returns nothing). The only Sunstone-material
   "recipe" reference left is a SpecCheck *comment* documenting it has none. The sole
   Sunstone-consuming recipe is `AddLensRecipe()` (the Lens trinket).
3. **Git:** PR #186 (`c7463ac`) "remove provisional craft recipe — loot-sourced only"
   deleted the method, the cost consts, the SpecCheck row, and moved the docs together.

> Note: QADiag's live recipe scan in the **first** boot reported "ObjectDB/recipes null"
> because it ran at the ZNetScene phase (recipes wire later, at ObjectDB.Awake). The probe
> was fixed to scan at the ObjectDB phase, but the **boot-log `+1 recipe` line + the
> source/git deletion** already establish the result decisively, so this is belt-and-braces.

---

## Observations (not blockers for this card)

- **SunstoneLens world-drop visual graft fails** — `donor 'Crystal' has no 'attach'
  child`, so a *dropped* `SBPR_SunstoneLens` has no mesh this build (boot log:
  "Functionally unaffected"). This is the **Lens** (card **t_2fd7bc7f**), NOT the
  Sunstone material my loot economy drops. The **Sunstone material** is cloned from
  `Coins`, so it keeps the Coins world mesh + loads its own `sunstone_v0.1.png` icon —
  the loot drop a player picks up IS visible. Flagging the Lens graft for the Lens card's
  owner; out of scope for t_0aef1243.
- **`local_map_v0.1.png` icon missing** — pre-existing (16 occurrences across all boots
  back to Jun 16, before this work); the `local_map` icon isn't shipped in
  `assets/icons/items/`. Unrelated to Sunstone. Worth a separate cleanup card.

## Remaining client-only risks (reasoned, not observed)

- **Populate timing.** `Container.Awake` populates a chest **once**, owner-side, gated
  by the `s_addedDefaultItems` ZDO flag. Chests already generated+populated in the
  existing Niflheim save keep their old contents — Sunstone only appears in chests
  populated **after** this build loaded. Daniel should sample **freshly-discovered**
  swamp chests (or a fresh world). This is standard vanilla loot-table behavior, not a
  Trailborne quirk (impl spec §6).
- **Pickup last mile.** That the sampled `ItemDrop.ItemData` becomes a pickable
  inventory stack on a real client is reasoned from the Coins-clone donor (a valid
  vanilla material with an ItemDrop) + the icon now loading — not observed headless.
- **Empirical rate caveat.** 15.01% is the per-chest probability from Valheim's own
  selection algorithm over the live table. Real felt rate also depends on how many
  swamp chests a player opens (small-sample variance is wide — ~20 chests will swing
  noticeably around 15%).

## Verdict

**All four named acceptance tests PASS at the build + server data layer**, and the
recipe-removal call is verified at source, git, and live boot. Dual-source drops are
live: swamp surface chests carry Sunstone at an empirically-confirmed ~15%/chest, the
Draugr Elite carries it at 5% flat, the Sunken Crypt table is clean, and the material
has no craft recipe. **logs-green ≠ playable** — Daniel's in-game run on freshly-
discovered swamp chests + a few Draugr Elite kills closes the observed last mile.
