---
title: "Architecture Review — domain models, the engine-free core seam, and a phased refactor"
status: proposed
date: 2026-06-17
owner: Daniel (gates every phase); architect (authored this review)
card: t_792c699b
purpose: "Full architectural review of SBPR.Trailborne. Names the domain models the mod needs (today + planned), proposes the engine-free domain-core seam that makes them testable, maps each model to the churn it stabilizes, and lays out a reversible phased refactor. Daniel gates each phase's merge; logs-green ≠ playable, so structural phases still need an in-game smoke per phase."
---

# Architecture Review — domain models + engine-free core seam

> **STATUS: PROPOSED — for Daniel's review.** Nothing here is locked. This is the
> shared input for the CLEANUP batch (card 2/3 architecture = this doc; 3/3 code
> quality = `t_4364809c`; 1/3 docs). The phased plan (§7) is sequenced so each phase
> builds 0/0 and ships as its own Daniel-gated PR. **Reversible/swappable over
> perfect** — every seam proposed here is one we can re-cut, not a lock-in.

---

## 0. TL;DR

The mod is **63 .cs / 18,330 LOC in one `net48` assembly** (`SBPR.Trailborne`),
split `Features/` (48 files, 14,987 LOC — vertical slices) + `Runtime/` (11 files,
2,802 LOC — shared infra). The vertical-slice structure (PR-C,
`docs/v1/architecture/feature-slice-plan.md`) is **good and should stay**. The pain
is not the slicing — it's **four missing domain models** and **total engine-fusion**,
and the two compound: because pure logic is welded to UnityEngine/Valheim/HarmonyX,
the missing models can't be given a tested home, so they stay hand-rolled and drift.

**Four domain models the code is asking for (each already half-written, by hand, N times):**

1. **ZDO-backed component** — the `*Tag` family (`CairnTag`, `SignTag`,
   `MarkerSignTag`, `SurveyorTableTag`, `AncientPortalTag`) hand-rolls the same
   owner-write/ghost-guard ZDO dance 5×. The code literally says *"Mirrors
   SignTag.WriteColors' owner-claim shape"* and *"Shared verbatim with MarkerSignTag."*
2. **Recipe / content definition** — the recipe manifest exists **twice**: once as
   live registration (per-feature `DoObjectDBWiring`), once as the `SpecCheck` drift
   manifest. They drift by construction; `SpecCheck` exists *because* they drift.
3. **Cartography provider / surface** — the map-provider model
   (`docs/design/map-provider-model.md`) is fully designed but has **no model type**;
   provider-binding state and the windowed survey are scattered across patches.
4. **Trinket / charged-accessory** — Cartographer's Kit + Sunstone Lens + (incoming)
   Iron Compass are the same worn-accessory-with-state pattern, three times.

**The structural seam:** extract a pure, **engine-free domain core** (no UnityEngine,
no Valheim, no Harmony refs) that holds those models as data + policy functions, with
the Unity/Harmony shell as a thin adapter. This is **not theoretical** — the repo
already proves the pattern twice: `BoundedMapMath` and `SurveyData`'s logic are
engine-free, and the `tests/` net8.0 harness link-compiles `SignHoverHintText.cs`
to get its 14 passing assertions with zero engine. The substrate `valheim-regions`
gets 102 tests the same way (pure `WorldZones.Regions` + thin `Mod.RegionOverlay`).
SBPR has the *inverse* — fusion — which is why it has ~1 test.

**The payoff is churn reduction.** Every top-churn file is a place where a missing
model is hand-maintained: `Runtime/Assets.cs` (22 changes), `CairnTag.cs` (24),
`Trailblazing.cs` (22), `Signs.cs` (18), `SpecCheck.cs` (17), `Registrar.cs` (13).
§5 maps each model to the churn it removes.

**Phasing (§7):** 6 reversible phases, the first two executed under this card —
(P1) the ZDO-component seam, (P2) the recipe-model unification that lets
Registrar+SpecCheck stop drifting. P3–P6 become child impl cards.

---

## 1. Current architecture — what's good, what hurts

### 1.1 What's good (keep it)

- **Vertical-slice `Features/` + `Runtime/` split** (PR-C). The DAG is acyclic and
  downward: `Pigments`/`Trailhead` are foundational content; `Signs`/`Cairns`/etc.
  depend down on them, never sideways. This is the right macro-structure. **This
  review does not re-litigate it.**
- **ADR-0006 additive construction.** `new GameObject()` + `AddComponent` of only
  intended components, reading vanilla prefabs as blueprints. The whole class of
  runtime-clone ZDO-orphan crashes is structurally gone. The refactor must NOT
  reintroduce subtractive patterns.
- **Two pockets of pure logic already exist and are already tested-or-testable:**
  `Features/Cartography/BoundedMapMath.cs` (Unity-free cell math, 31/31 spike
  assertions) and `Features/Signs/SignHoverHintText.cs` (Unity-free, 14 live
  assertions via `tests/`). These are the beachhead — the seam this review
  proposes is "do more of what these two already do."

### 1.2 What hurts — fusion + missing models

- **One assembly, everything fused.** `SBPR.Trailborne.csproj` references
  `assembly_valheim`, 14 UnityEngine modules, HarmonyX, TMP, uGUI. Every `.cs`
  that wants to hold a rule (recipe cost, tier ladder, provider-binding precedence,
  charge economy) sits in that assembly, so the rule can't be exercised without the
  engine. Result: **18k LOC, ~1 test, no `dotnet test` in CI** (`ci.yml` only asserts
  the DLL builds, line 121).
- **`SpecCheck` is a test wearing a boot-guard costume.** `Runtime/SpecCheck.cs`
  (454 LOC) walks every registered recipe at server boot and screams on drift. It
  exists *because there is no compile-time single-source-of-truth for recipes* — the
  manifest is duplicated from the live registration. A real model + a real test
  would retire most of it (§3.2, §5).
- **Null-as-value is pervasive.** ~1,082 raw `null` occurrences in src. The compiler
  forbids *unannounced* nulls (`<Nullable>enable</Nullable>` +
  `<TreatWarningsAsErrors>`), but null-as-a-value is a design habit, not a compiler
  problem — and it concentrates in exactly the hand-rolled ZDO accessors a model
  would replace (card 3/3 owns the null audit; the ZDO-component model in §3.1 is
  the structural assist).

### 1.3 The measured churn (last 6 weeks, change-count)

| File | Changes | Why it churns (the missing model) |
|---|---|---|
| `Features/Cairns/CairnTag.cs` | 24 | ZDO-component + cosmetic art fused in one 1,235-LOC class |
| `Runtime/Assets.cs` | 22 | the additive-construction toolkit + item-shell seeding, all hand-threaded |
| `Features/Trailblazing/Trailblazing.cs` | 22 | terrain-op + spade content, no shared op model |
| `Features/Signs/Signs.cs` | 18 | tint/registration/ZDO mixed |
| `Runtime/SpecCheck.cs` | 17 | recipe manifest drifts → constant re-sync |
| `Runtime/Registrar.cs` | 13 | per-feature wiring list edited on every new feature |

High churn = unstable abstraction = where the model is missing or wrong. §5 maps the
proposed models onto this table.

---

## 2. The structural seam — an engine-free domain core

### 2.1 The split (mirrors the substrate)

```
┌──────────────────────────────────────────────────────────────┐
│  SBPR.Trailborne  (net48 SHELL — UnityEngine/Valheim/Harmony) │
│  Features/*, Runtime/Assets, Registrar, the Tag adapters,     │
│  the Harmony patches, all rendering + UI + ZDO I/O.           │
│                        │ depends down on ▼                    │
├──────────────────────────────────────────────────────────────┤
│  SBPR.Trailborne.Core  (engine-free — NO Unity/Valheim/Harmony)│
│  Domain models (recipe defs, tier ladders, charge economy,    │
│  provider-binding state machine, survey windowing) + pure     │
│  POLICY functions. Compiles under net48 AND net8.0.           │
└──────────────────────────────────────────────────────────────┘
                         ▲ tested by ▼
┌──────────────────────────────────────────────────────────────┐
│  SBPR.Trailborne.Tests  (net8.0 — xUnit, runs in CI)          │
│  References ONLY the Core. No engine fetch needed.            │
└──────────────────────────────────────────────────────────────┘
```

The shell keeps everything that genuinely touches the engine. The Core holds the
**rules** — anything that's a pure function of data. The test project references the
Core and runs in CI with no Valheim SDK (exactly as `tests/` does today).

### 2.2 Why this is low-risk here (it's already how two files work)

- `BoundedMapMath` and `SurveyData`'s *logic* are already engine-free in spirit.
  `BoundedMapMath` has **zero** UnityEngine refs already. `SurveyData` only touches
  Unity for `Vector3` and `ZPackage` — both replaceable at the seam (a plain
  `(float x, float y, float z)` or a small `Vec3` struct in the Core; serialization
  stays shell-side or takes an injected writer).
- The `tests/` project **already link-compiles a shipped source file** under net8.0
  (`<Compile Include="../src/.../SignHoverHintText.cs" />`). The Core formalizes that
  one-off into a real project the test references — no new mechanism, just promotion.
- **No AssetBundles, no `.meta` GUIDs** (ADR context / PR-C R3). The mod is pure code
  + runtime PNG load, so there's no asset-graph to untangle when splitting projects —
  the only fragility is **string identifiers** (prefab/ZDO/config names), and those
  are values we carry verbatim across the move (R3/R4).

### 2.3 The seam discipline (how shell talks to core)

The Core never imports the engine. Where a model needs an engine fact (the live ZDO,
the world clock, the player position), the **shell passes it in** — as a primitive or
through a tiny Core-defined interface the shell implements. Two worked seams:

- **`IZdoHandle`** (Core interface) — `GetInt/GetString/GetLong/GetBool/Set`,
  `IsOwner()`, `ClaimOwnership()`, `IsValid()`. The shell's `ZNetView`-backed adapter
  implements it; the Core's ZDO-component policy (§3.1) is written against the
  interface and unit-tested with an in-memory fake. **No `ZNetView` type crosses the
  seam.**
- **Clock / position as primitives** — `AncientPortalTag`'s grow math is
  `progress(plantTicks, nowTicks, growSeconds) -> [0,1]`. That's a pure function; the
  shell reads `ZNet.GetTime().Ticks` and passes the longs in. The Core decides; the
  shell acts on the decision (toggle the collider).

### 2.4 What does NOT move to the Core (explicit)

Renderers, particles, Cloth, `MonoBehaviour` lifecycle, Harmony patches, `ObjectDB`/
`ZNetScene` registration, UI panels, reflection into vanilla privates. The Core is
**rules and data**, not behavior that touches the world. CairnTag's 1,000 lines of
banner/cloth/kitbash art **stay in the shell** — only its tier-ladder + ZDO-accessor
spine moves. This keeps the seam honest: if it needs a GPU or a network write, it's
shell.

---

## 3. The domain models the mod needs

### 3.1 Model A — ZDO-backed component (the `*Tag` family)

**The duplication, measured.** Five `MonoBehaviour` tags hand-roll the same
owner-write ZDO discipline:

| Tag | LOC | nview/ZDO guards | `ClaimOwnership` | `GetZDO()` calls |
|---|---|---|---|---|
| `CairnTag` | 1,235 | 5 | 1 | 9 |
| `SignTag` | 176 | 4 | 1 | 9 |
| `MarkerSignTag` | 235 | 10 | 4 | 21 |
| `SurveyorTableTag` | 575 | 7 | 3 | 5 |
| `AncientPortalTag` | 197 | 3 | 1 | 6 |

Every one repeats this shape (verbatim from `SignTag.WriteColors` / `MarkerSignTag.
WritePinned` / `SurveyorTableTag.WriteTableName`):

```csharp
public bool WriteX(T value) {
    if (nview == null || nview.GetZDO() == null) return false;   // ghost-guard
    if (!nview.IsOwner()) nview.ClaimOwnership();                 // owner-claim
    nview.GetZDO().Set(ZdoKey, value);                           // write
    return true;
}
public T ReadX() {
    if (nview == null || nview.GetZDO() == null) return default;  // ghost-guard
    return nview.GetZDO().GetX(ZdoKey, fallback);
}
```

The comments admit it outright: *"Mirrors SignTag.WriteColors' owner-claim shape"*
(MarkerSignTag), *"the exact shape MarkerSignTag.WritePinned / SignTag.WriteColors
use"* (SurveyorTableTag), *"Shared verbatim with MarkerSignTag so the two features
can never diverge"* (SignGeometry). When you write "can never diverge" in a comment,
you've found a missing abstraction.

**The model:** a shell base class + a Core policy.

- **Shell:** `ZdoComponent : MonoBehaviour` — resolves `ZNetView` in `Awake`, exposes
  a non-null `IZdoHandle Zdo` (or a `TryGetZdo(out handle)` for the ghost case), and
  offers protected `ReadString/Int/Long/Bool(key, fallback)` + owner-gated
  `Write(key, value)` that do the ghost-guard + owner-claim **once, correctly**. The
  five tags inherit it and shrink to *just their feature state* (Cairn's tier ladder,
  Sign's two-tone colors, Marker's pin fields).
- **Core:** `ZdoAccess` static policy over `IZdoHandle` — the "ghost = no-op, owner
  writes, claim before write" rules as pure functions, unit-tested against an
  in-memory `FakeZdo`. This is where the **null-as-value reduction** (card 3/3) lands
  structurally: a `TryGet`-style API returning `bool + out` replaces the
  `return default`/`return ""`/`return ZDOID.None` sentinels the tags use today.

**Wire-contract safety (load-bearing):** the ZDO **key string values**
(`SBPR_CairnTier`, `SBPR_PortalPlantTime`, `SBPR_MarkerType`, `SBPR_TableName`,
`SBPR_PinName`, …) are save/multiplayer contracts (`GetStableHashCode` on the value —
PR-C R3). The base class moves *code*, never *key values*. Each tag keeps its own
`const string Zdo*` literals; the base only centralizes the *access dance*. A rename
of any key orphans every placed instance — the model must not touch them.

### 3.2 Model B — Recipe / content definition (the Registrar↔SpecCheck drift)

**The duplication, measured.** A recipe is defined **twice**, in two files that must
be hand-kept in sync:

- **Live registration** — each feature's `DoObjectDBWiring` builds the real `Recipe`/
  `Piece.Requirement[]` against `ObjectDB` (e.g. `CartographersKit.DoObjectDBWiring`,
  `Portals.DoObjectDBWiring`, `SunstoneLens.DoObjectDBWiring`).
- **The drift manifest** — `Runtime/SpecCheck.cs` re-declares all of it as a
  `RecipeSpec[] Manifest` (lines 63–184: bench, lamp, sign, table, spade, 4 pigments,
  local map, kit, portal seed, portal piece, sunstone, lens) and walks `ObjectDB` at
  boot comparing the two.

`SpecCheck`'s own header says why it exists: *"four recipes silently diverged from the
spec across milestone iterations because nobody re-read the spec on every change."*
That is the definition of a missing single-source-of-truth. The repo papered over it
with a **runtime** guard — which is the right instinct (fail loud) at the wrong layer
(boot, not compile/test).

**The model:** one `ContentDefinition` source of truth in the Core that BOTH the
registrar and the checker consume.

```
Core:  RecipeDef { Output, Amount, Station, Requirement[] {Resource, Amount} }
       PieceDef  { Prefab, Station, Requirement[] }
       ItemKind  { prefab name, construction (Clone|Additive), … }
       — plain data + a registry the features populate, ONE per recipe.

Shell: Registrar reads the registry → builds the live Recipe/Requirement[] (the
       ONLY place that touches ObjectDB).
       SpecCheck SHRINKS to: "did the live ObjectDB state match the registry?"
       — same fail-loud boot guard, but now there's nothing to *drift from*
       because registration and the check read the same list.
Tests: assert registry invariants at COMPILE/test time (every RecipeDef resolves a
       station that exists; pigment costs are the locked values; no duplicate
       outputs) — moving most of SpecCheck's job left of boot.
```

**Why this is the highest-value model:** it directly retires the
`SpecCheck.cs` (17 changes) ↔ per-feature-wiring (`Registrar` 13, `Signs` 18) churn
axis. Today every recipe change is a *three-place* edit (live code, SpecCheck
manifest, the markdown spec — AGENTS.md's "spec and code change together"). After: a
*two-place* edit (the one `RecipeDef` + the markdown spec), and a test catches the
markdown drift if the def's named-cost constant disagrees.

**Keep the boot guard, demote it.** `SpecCheck`'s icon/attack **asset-renderability**
assertions (`CheckIcon`, `CheckAttack`) genuinely need the live `ObjectDB`/`ItemDrop`
— they stay shell-side boot guards (you cannot check a loaded sprite without the
engine). Only the **recipe-shape** half moves to registry + test. This is a demotion,
not a deletion: logs-green ≠ playable still holds for the asset checks.

**Wire-contract safety:** recipe *resource names* and *prefab names* are the same
`GetStableHashCode` string contracts (R3). The registry holds the existing name
constants (`Pigments.PigmentRedName`, `SunstoneLens.SunstoneName`, …) — it does not
mint new strings. Costs already live as named consts (`SunstoneLens.LensIronCost`
etc.); the registry references those, so SpecCheck and registration literally can't
disagree on a number again.

### 3.3 Model C — Cartography provider / surface

This one is **designed but un-modeled**. `docs/design/map-provider-model.md` (Daniel,
2026-06-15, LIVING) fully specifies a provider state machine — two map types
(personal global + local-map artifacts), an equipped-local-map "provider" binding with
precedence ("most-recently-equipped, still in inventory"), bidirectional table sync,
the nomap-on/off split, and the future Eye-of-Odin global unlock. But there is **no
provider type** in code: the binding lives implicitly across `LocalMapEquipPatch`,
`LocalMapController`, and `SurveyorTableTag`, and the "is this a Local Map?" predicate
is hand-copied **three times** (`SurveyorTableTag.IsLocalMap`, `LocalMapEquipPatch.
IsLocalMap`, `LocalMapController.IsLocalMap` — each file keeps "its own 3-line copy").

**The model:**

- **Core:** `SurveyData` + `BoundedMapMath` already ARE the surface model (windowed
  fog + pins + merge), they just need the `Vector3`/`ZPackage` seam-cleaning (§2.2) to
  fully land in the Core. Add a `ProviderBinding` state machine — pure: `(currently
  bound, just-equipped, left-inventory, died) -> newBinding` — encoding the §3.2
  precedence rules from the design doc as a unit-tested transition table. The
  provider-binding bugs (cards `t_1d1b505b` carry-disc, etc.) are state-machine bugs;
  a tested transition table is where they stop recurring.
- **Shell:** the equip/inventory/death **hooks** (reflection into `Humanoid.m_leftItem`,
  the death-drop unbind) feed events into the Core machine; the Core decides the
  binding; the shell re-binds the minimap + M-key. The `IsLocalMap` triple collapses
  to one shell helper (the prefab-tag/name check needs `ItemDrop`, so it's shell, but
  it's **one** copy).

**Planned-area fit:** the model is **forward-compatible with Eye-of-Odin by
construction** — the design doc's whole point is that the personal global fog
accumulates all along (it rides vanilla `m_explored`/`SaveMapData`), so the global-map
view is a *later shell surface* over data the provider model already feeds. The Core
provider machine gains one state (global-unlocked) when Mistlands ships; nothing
re-cuts. (§6.)

### 3.4 Model D — Trinket / charged-accessory

**The duplication, emerging.** Three worn accessories share one shape:

- **Cartographer's Kit** (`CartographersKit.cs`) — Utility-slot accessory that gates
  auto-mapping; additive `ConstructItemShell`.
- **Sunstone Lens** (`SunstoneLens.cs`) — Trinket-slot accessory whose **durability is
  a solar battery**: a Harmony prefix on `Humanoid.DrainEquipedItemDurability` owns the
  drain/recharge/clamp and skips vanilla's break branch; a HUD overlay renders detection.
- **Iron Compass** (v3, `docs/v3/planning/iron-compass-impl-spec.md`, spec merged) —
  the *next* Trinket-slot accessory with a HUD overlay. The Lens header already
  flags the shared slot: *"It DOES share the Trinket slot with the future Iron
  Compass — a deliberate exploration-tool choice."*

The repeated parts: additive item-shell construction (already shared via
`Assets.ConstructItemShell`), slot wiring, a HUD-overlay render path
(`SunstoneLensHudOverlay`), and — for the charged ones — a **charge economy** (max,
drain/sec, recharge/sec, inert-below-threshold). Today the Lens's charge economy is
inline tunables (`DefaultMaxCharge`, `DefaultDrainPerSec`, `DefaultChargePerSec`,
`MinChargeToDetect`).

**The model:**

- **Core:** `ChargeEconomy` — pure `step(charge, dt, isCharging) -> charge'` clamped to
  `[0,max]`, plus `isActive(charge, threshold)`. This is the Lens's whole battery rule,
  unit-tested (recharge in sun, drain when worn, inert-at-zero-but-not-consumed —
  exactly its AC#5). The Iron Compass and any future charged tool reuse it.
- **Shell:** the equip-slot wiring, the `DrainEquipedItemDurability` prefix, and the
  HUD overlay stay shell (they touch `Humanoid`/`ItemDrop`/the screen). A shared
  `HudOverlay` base can absorb the common overlay scaffolding the Lens and Compass both
  need — but that's a shell convenience, not a Core model.

**Honest scope note:** D is the **lowest-urgency** model (2 shipped + 1 specced, not
yet painful). It's named here so the Core's `ChargeEconomy` is built once, when the
Compass lands, rather than copied. Don't pre-build the rest of D speculatively.

---

## 4. One more seam worth cutting — explicit patch registration

Not a domain model, but a structural fix the existing architecture doc already
recommends and defers (PR-C R2). Today `Plugin.Awake` calls
`harmony.PatchAll(typeof(...))` per patch class. PR-C documents that this is exactly
how the `Sign_Interact_Patch` shipped **dead** (never registered, no compile error).
Every new feature adds a patch class someone must remember to wire.

**The fix (PR-C's own recommendation):** give each feature an explicit
`ApplyPatches(Harmony h)` and have `Registrar`/`Plugin` call them from a list. A
missing feature then **fails to compile** (missing method) instead of silently not
patching. This converts a whole class of "logs-green-but-dark" bugs into build errors —
which is the same fail-loud-at-the-earliest-layer move as §3.2. Cheap, reversible,
folds naturally into the Registrar phase (P2).

---

## 5. How the models stabilize the churn

Mapping each top-churn file to the model that drains its change-pressure:

| Churn file | Chg | Model | Effect |
|---|---|---|---|
| `CairnTag.cs` | 24 | A (ZDO-component) | Tier/ZDO spine → base class + Core ladder; the 1,000 LOC of banner/cloth art that ACTUALLY churns stays shell but is now *isolated* from the data spine (a tier-rule change stops risking the art and vice-versa) |
| `Assets.cs` | 22 | B + ADR-0006 toolkit | Item-shell seeding (icon/attack null-landmines) becomes a Core-described `ItemKind` contract + the existing boot assertions; the additive toolkit stays but stops absorbing per-feature recipe knowledge |
| `Trailblazing.cs` | 22 | (A, partial) | Terrain-op state via the ZDO-component base; the op-set itself is feature-specific and stays — this file churns partly on its own merits (out of scope to fully model now; flagged) |
| `Signs.cs` | 18 | A + B | Two-tone ZDO via base (A); sign recipe via registry (B). Tint/registration separate from data |
| `SpecCheck.cs` | 17 | **B** | The big one: recipe-shape half → registry + test; only asset guards remain. Most of its 454 LOC + its churn evaporate |
| `Registrar.cs` | 13 | B + §4 | Per-feature wiring becomes registry iteration + `ApplyPatches` list; adding a feature stops editing the dispatcher body |

**Honest caveat:** not all churn is model-shaped. `CairnTag`'s banner/cloth physics
churned 24× because it's an *un-verifiable client visual* that shipped wrong in-world
twice while building 0/0 — no domain model fixes that; only Daniel's in-game eyeball
does (logs-green ≠ playable). The models stabilize the **data/rule** churn; the
**visual-tuning** churn is irreducible and correctly lives in live config. Naming this
honestly matters: don't sell the refactor as fixing churn it can't fix.

---

## 6. Planned-area fit (don't retrofit to today only)

The models are checked against the locked roadmap so they're not just today-shaped:

- **v3 Swamp items** (Sunstone Lens shipped; trail-lights, twisted portal specced) —
  Model A (every new placed piece is a ZDO-component), Model B (every new recipe is a
  `RecipeDef`), Model D (Lens + future charged tools). Already exercised: the Lens is
  the proof the trinket shape repeats.
- **Eye-of-Odin global minimap** (Mistlands, `map-provider-model.md` §1, future) —
  Model C is **designed forward-compatible**: global fog accumulates now, the unlock
  is a later shell surface + one Core provider state. No re-cut.
- **v4 Mountains** — more pieces/recipes/biome-tiered pigments. Models A + B absorb
  these with zero new structure (a pigment is a `RecipeDef`; a placed marker is a
  ZDO-component). Pillar 2 (color is emergent) is a Core invariant a test can guard
  (no pigment def carries author-assigned semantics).
- **v5 maritime** (lighthouses = Beacon promotion, fog buoys —
  `docs/design/maritime-exploration-tools.md`, brainstorm) — greenfield. Lighthouse is
  a ZDO-component (A) that's a charged/fueled light (shares the v3 Beacon's eternal-fire
  helper); fog buoy is a ZDO-component + a HUD/render concern (D-adjacent). The models
  pre-fit; nothing here forces a new abstraction. **Note:** maritime is post-Mistlands
  and explicitly unlocked/brainstorm — the fit is "the models won't block it," not "build
  for it now."

**Design rule that falls out:** *a new placed thing is a ZDO-component (A); a new
craftable is a RecipeDef (B); a new worn tool with state is a charged-accessory (D); a
new map surface is a provider/surface consumer (C).* Four buckets; every roadmap item
lands in one without inventing a fifth. That's the test of whether the model set is
right — and it passes for v3→v5.

---

## 7. Phased refactor plan

Principles: **reversible, incremental, each phase builds 0/0 and ships independently;
Daniel gates every merge; logs-green ≠ playable, so structural phases still need an
in-game smoke.** No big-bang. String wire-contracts (R3) are never touched. Each phase
is its own PR with `kanban_block(reason="review-required: …")`.

### P0 — Stand up the Core + test projects (no behavior change)

Create `src/SBPR.Trailborne.Core/` (net48 **and** net8.0 multi-target, or net-standard
that both consume) with **nothing in it but the seam interfaces** (`IZdoHandle`, a
`Vec3` struct or chosen primitive) and move `BoundedMapMath` into it (it's already
engine-free — pure relocation, the SDK globs so no csproj edit for the shell). Promote
`tests/` to reference the Core and add xUnit (keep the existing
`SignHoverHintText` self-test running). Wire `dotnet test` into `ci.yml` (today it
only builds the DLL — card 3/3 owns CI test execution; P0 just makes it *possible* by
giving it a real test project). **Build 0/0; existing 14 assertions still pass; new
Core project compiles under both TFMs.** Reversible: it's additive — deleting the Core
project restores today.

> Verify: `dotnet build -c Release` 0/0 on the shell; `dotnet test` green; `git grep`
> shows no string-literal change. This phase has **no runtime delta** → no in-game
> smoke needed (the one phase that's safe without it).

### P1 — Model A: the ZDO-component seam (executed under this card)

- Add `IZdoHandle` + a shell `ZNetViewZdoHandle` adapter + `ZdoComponent` base.
- Add Core `ZdoAccess` policy + `Providerless` `TryGet` helpers, unit-tested with a
  `FakeZdo`.
- **Migrate the two LOW-RISK tags first:** `AncientPortalTag` (197 LOC, self-contained
  grow math — and its `progress()` becomes a Core pure function with tests) and
  `SignTag` (176 LOC, clean two-tone ZDO). Leave `CairnTag`/`MarkerSignTag`/
  `SurveyorTableTag` for a follow-up phase (they're bigger and carry art/RPC/UI
  weight — don't bite them off in the first pass).
- Each migrated tag keeps its exact `const string Zdo*` keys (R3).

> Verify: 0/0; new Core unit tests green (grow-progress edge cases: unstamped, clock-
> not-up, mid-grow relog, past-window); `git grep` no key-string change. **In-game
> smoke (Daniel):** plant an Ancient Portal → it still grows 15 s then activates; paint
> a Sign two-tone → color persists across relog. logs-green ≠ playable.

Reversible: the base class is additive; a tag can revert to inline ZDO calls file-local
if the seam feels wrong.

### P2 — Model B: recipe-model unification + §4 patch registration (executed under this card)

- Add Core `RecipeDef`/`PieceDef`/registry; populate it from the existing per-feature
  name+cost consts (no new strings).
- Point `Registrar` registration **and** `SpecCheck`'s recipe-shape pass at the
  registry (single source). Keep `CheckIcon`/`CheckAttack` as shell boot guards.
- Add Core tests: every `RecipeDef` resolves a known station; pigment/kit/portal/lens
  costs equal their locked values; no duplicate outputs.
- Fold in §4: give each feature `ApplyPatches(Harmony)`; `Plugin`/`Registrar` call a
  list. (This also lets a future deliberate wiring of the dead `SignInteractPatch` be
  an explicit one-line list add — NOT smuggled in here; that stays Daniel's call.)

> Verify: 0/0; Core recipe-invariant tests green; **boot the server** → SpecCheck logs
> `✓ All N recipes match` exactly as today (the manifest now IS the registry, so it
> must still pass). **In-game smoke (Daniel):** craft one recipe per feature still
> works; build menu unchanged. logs-green ≠ playable.

Reversible: the registry is data; if it's wrong, SpecCheck still screams (we kept the
guard) — the safety net survives the refactor that targets it.

### P3 — Model A continued: migrate the heavy tags (child card)

Migrate `MarkerSignTag`, `SurveyorTableTag`, and `CairnTag`'s ZDO spine to
`ZdoComponent`. `CairnTag` is the big one (1,235 LOC) — migrate ONLY its tier/ZDO
accessors; the banner/cloth/kitbash art is untouched (it's shell, stays shell). Split
`CairnTag` into `CairnTag` (data spine, ZdoComponent) + `CairnVisual` (the art) while
here — that's the file whose 24-change churn most needs the data/art separation.
**Per-tag in-game smoke required.** → child impl card, assignee `engineer-systems`.

### P4 — Model C: cartography provider/surface (child card)

Finish moving `SurveyData` logic into the Core (the `Vector3`/`ZPackage` seam-clean),
collapse the `IsLocalMap` triple to one shell helper, and add the `ProviderBinding`
state machine in the Core with the transition tests. This is the most behavior-laden
phase (touches the live map loop) → its own card, careful in-game verification of the
bind/unbind/death precedence. → child impl card.

### P5 — Model D: charged-accessory (child card, when Iron Compass lands)

Extract `ChargeEconomy` to the Core when the Iron Compass implementation starts, so the
Compass and Lens share one tested battery rule instead of a copy. Deliberately
**deferred until there's a second real consumer** — building it now would be
speculative. → child impl card, paired with the Iron Compass impl.

### P6 — SpecCheck demotion cleanup (child card)

Once P2 has the registry as the single source, prune `SpecCheck`'s now-redundant
recipe-shape manifest down to the asset guards + the registry-vs-live comparison.
Mechanical deletion (Daniel gates deletions — produce a kill-list). → child impl card,
coordinates with card 3/3's dead-code pass.

### Phase dependency / sequencing

```
P0 (Core+tests)  ──►  P1 (ZdoComponent, 2 tags)  ──►  P3 (heavy tags)
      │                                                    
      ├──────────►  P2 (recipe registry + ApplyPatches)  ──►  P6 (SpecCheck prune)
      │
      └──────────►  P4 (provider/surface)
                          
P5 (charged-accessory)  — independent, gated on Iron Compass impl start
```

P0 is the only hard prerequisite (everything needs the Core project). P1/P2/P4 are
independent of each other and can interleave. **This card executes P0 → P1 → P2** (or
as many as fit cleanly with 0/0 + a gate each); the rest are child cards for
`engineer-systems`, coordinated with card 3/3.

---

## 8. Risk register

| # | Risk | Mitigation |
|---|---|---|
| R-A | **Touching a ZDO/prefab/config key string** orphans saves + breaks MP sync (R3). | Models move *code*, never key *values*. `git grep` for `GetStableHashCode`/`.Set(`/`GetInt`/`Config.Bind` shows zero literal change each phase. Each tag keeps its own `const string Zdo*`. |
| R-B | **The Core "ports" leak the engine back in** (someone adds `using UnityEngine` to the Core). | The Core csproj references NO Unity/Valheim/Harmony assemblies — such a `using` fails to compile. The boundary is enforced by the build, not by discipline. |
| R-C | **Over-extraction** — pulling render/RPC/UI into the Core "to test it." | §2.4 names what stays shell. The rule: if it needs a GPU, a network write, or a vanilla type, it's shell. CairnTag's art stays shell. |
| R-D | **Multi-target friction** (net48 shell + net8.0 tests over one Core). | The Core targets the lowest common denominator (net48-compatible C#, no net8-only APIs) or multi-targets; `tests/` already proves a net8.0 project can link net48-targeted source. Validate in P0 before any model moves. |
| R-E | **ADR-0006 regression** — a refactor reintroduces clone-then-strip. | The refactor only moves *data/rules*; it never touches construction. Additive construction is shell and untouched. SpecCheck's `CheckIcon`/`CheckAttack` stay as the boot tripwire. |
| R-F | **"Logs-green ≠ playable" complacency** — green tests/build read as "done." | Every behavior-touching phase (P1, P3, P4) lists an explicit Daniel in-game smoke. The Core tests prove *rules*, not *playability*. |
| R-G | **Scope creep into card 3/3** (null audit, dead-code, CI test execution). | This card builds the *seam* that 3/3's structural tests need; it does not do the null audit or the brittle-test purge. P0 makes `dotnet test` *possible*; 3/3 owns wiring the suite + the null work. Coordinate, don't overlap. |

---

## 9. Open questions for Daniel (gate before P0 lands)

1. **Core packaging:** one new `SBPR.Trailborne.Core` project multi-targeting
   net48+net8.0, or a net-standard2.0 Core both consume? (Lean: multi-target net48;
   `tests/` already shows net8.0 can consume net48-targeted source, and the shell is
   net48.) Reversible either way; just want your call before P0.
2. **`SurveyData` `Vector3` seam:** introduce a Core `Vec3` struct, or keep `SurveyData`
   shell-side and move only `BoundedMapMath` + a thin pure survey-policy to the Core?
   (Lean: small Core `Vec3`; it's 3 floats and unblocks the whole cartography model.)
3. **Phase appetite for THIS card:** stop after P1 (ZDO-component, 2 tags) for a tight
   first review, or push through P2 (recipe registry) in the same card as a second
   gated PR? (Lean: P0+P1 as PR-1, P2 as PR-2 — two small gates beat one big one.)
4. **`SignInteractPatch` (dead since PR-C R1):** P2's `ApplyPatches` makes wiring it a
   one-line list add. Wire it (activates Shift+E sign-pinning — a behavior change) or
   leave it explicitly dormant? (Recommend: leave dormant; wire it in its own
   behavior card, your call.)

---

## 10. Provenance

- Authored 2026-06-17 by the architect (card `t_792c699b`), from a full read of the
  `main` source tree (63 .cs), the churn data in the card body, and the existing design
  corpus.
- Grounded against: `docs/v1/architecture/feature-slice-plan.md` (PR-C vertical-slice
  intent + R1/R2/R3 risk vocabulary), `docs/decisions/0006-additive-prefab-construction.md`,
  `docs/decisions/0001-clean-room-no-jotunn.md`, `docs/design/map-provider-model.md`,
  `docs/design/design-pillars.md`, `docs/design/maritime-exploration-tools.md`,
  `docs/v3/planning/*` (Sunstone Lens, Iron Compass), and the live files cited inline
  (`*Tag.cs`, `Runtime/{Assets,Registrar,SpecCheck,RecipeHelpers}.cs`,
  `Features/Cartography/{BoundedMapMath,SurveyData,SurveyorTableTag}.cs`, `tests/`).
- Build baseline verified before authoring: `dotnet build -c Release` → **0 warn / 0
  err**; existing `tests/` harness → **14/14 assertions pass**.
- This doc is the shared input for the CLEANUP batch; it iterates with Daniel's §9
  answers, then P0→P2 execute as Daniel-gated PRs under this card.
