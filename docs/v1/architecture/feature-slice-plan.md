---
title: "PR C — Vertical-Slice `Features/` Structure Plan (Stage-1 Architecture)"
status: historical
last_reviewed: 2026-06-17
purpose: Vertical-slice Features/ architecture plan. DELIVERED — the Features/ + Runtime/ structure specified here is shipped in src/SBPR.Trailborne/ (and has grown beyond the original 5 features). Kept as the rationale record for the vertical-slice decision.
---

# PR C — Vertical-Slice `Features/` Structure Plan (Stage-1 Architecture)

> **Status:** Historical — DELIVERED. This plan's `Features/` + `Runtime/`
> structure is shipped in `src/SBPR.Trailborne/`. Kept as the *why* behind the
> vertical-slice decision, not as pending work. The engineer executed Stage 2.
> **Branch:** `pr-c-architecture` → `v1`. **Card:** `t_ddf77c44`.
> **Locked decision (do not re-litigate):** group by *feature* (vertical slice), not by *milestone* (M1/M2/M3).
> **Clean-room:** designed from our own MIT source only. No decompiled IronGate source consulted.

---

## 0. TL;DR

The current code is grouped by **milestone** (`TrailborneM1/M2/M3`). M1 mixes *inks + signs*; M2 mixes
*cairn markers + cairn pieces + tier logic + Harmony patches*; `TrailborneRegistrar` mixes *lifecycle hooks*
with *three pieces of standalone content* (bench, lamp, spade). We regroup into **5 content features** plus a
**`Runtime/`** infrastructure folder and a thin top-level entry point.

**Feature dependency DAG (all edges point DOWN — acyclic):**

```
                 Plugin (entry)
                     │
        ┌────────────┴──────────── Runtime/ (Assets, ServerContext, SpecCheck, Registrar) ──────────┐
        │                                                                                            │
   Trailhead (Explorer's Bench = the station + Path Lamp)        Pigments (4 inks = shared ingredient)
        ▲            ▲            ▲                                  ▲                    ▲
        │            │           │                                  │                    │
      Signs        Cairns    Trailblazing                         Signs               Cairns
```

- **Pigments** and **Trailhead** are *foundational content* features (everyone depends on them; they depend on no
  other feature). This is what kills the M1 ink/sign tangle: Signs and Cairns both depend **down** on Pigments
  for ink, never sideways on each other.
- **Runtime/** holds only shared *infrastructure* that ≥2 features need.
- 5 features, **24-entry rename table**, **6 migration steps** that each keep the build green.

---

## 1. Feature inventory — every type & code region mapped

Source reality (9 files, single `net48` csproj, SDK-default `**/*.cs` globbing — **moving files never needs a
csproj edit**):

| # | Current file | Type(s) | What it does | → Destination |
|---|---|---|---|---|
| 1 | `TrailbornePlugin.cs` | `TrailbornePlugin` | BepInEx entry; holds `Log`, `PluginFolder`, `DebugCairnDamage` config; applies Harmony patches | **top-level** (entry) + config refinement → Cairns |
| 2 | `TrailborneAssets.cs` | `TrailborneAssets` | PNG→Sprite, `ClonePrefab`, register prefab/item/recipe, hammer table, `BuildReq`, `FindItemDrop` | **Runtime/** |
| 3 | `SBPRContext.cs` | `SBPRContext` | server gate (`OnSBServer`) | **Runtime/** |
| 4 | `TrailborneSpecCheck.cs` | `TrailborneSpecCheck` | recipe drift watchdog; reads every feature's manifest | **Runtime/** |
| 5 | `TrailborneRegistrar.cs` | `TrailborneRegistrar` | **(a)** lifecycle Harmony hooks (`ZNetScene.Awake`, `ObjectDB.CopyOtherDB`/`Awake`) + dispatch; **(b)** Explorer's Bench prefab; **(c)** Path Lamp prefab; **(d)** Trailblazer's Spade *item* prefab + recipe + `PublicSpadeName` | **(a)** → Runtime/Registrar; **(b)(c)** → Trailhead; **(d)** → Trailblazing |
| 6 | `TrailborneM1.cs` | `TrailborneM1` | inks (4) **+** signs (4): names, `_icons`, colors, pin types, register, ODB wiring | **split:** ink half → Pigments, sign half → Signs |
| 6 | `TrailborneM1.cs` | `TrailborneSignTag` | MonoBehaviour marker on signs | **Signs/** |
| 6 | `TrailborneM1.cs` | `Sign_Interact_Patch` | Harmony prefix on `Sign.Interact` → Shift+E map pin. **⚠ currently NEVER registered (dead code) — see §6 R1** | **Signs/** |
| 7 | `TrailborneM2.cs` | `TrailborneM2` | cairn markers (4) + cairn pieces (4) + tier tables + comfort + ODB wiring | **Cairns/** |
| 7 | `TrailborneM2.cs` | `TrailborneCairnTag` | MonoBehaviour: color, ZDO tier accessors, kitbash art | **Cairns/** |
| 7 | `TrailborneM2.cs` | `TrailborneCairnInteractable` | `Hoverable, Interactable`: E repair+upgrade, Shift+E debug | **Cairns/** |
| 7 | `TrailborneM2.cs` | `TrailborneCairnPatches` | Harmony: `WearNTear.Damage`, `WearNTear.Awake`, `SE_Rested.CalculateComfortLevel` | **Cairns/** |
| 8 | `TrailborneM3.cs` | `TrailborneM3` | spade path/replant terrain ops + spade-only PieceTable | **Trailblazing/** |

### The M1 untangle (inks vs signs)

`TrailborneM1` is two features wearing one trench coat:

- **Ink region** → Pigments: `InkRedName`/`InkWhiteName`/`InkBlueName`/`InkBlackName`, `RegisterInkPrefab`,
  `AddInkRecipe`, the ink rows of `_icons`.
- **Sign region** → Signs: `SignRedName`…`SignBlackName`, `SignColors`, `SignPinTypes`, `RegisterSignPrefab`,
  `TintMeshRenderers`, `InkLookupForSign`, sign rows of `_icons`, the sign half of `DoObjectDBWiring`,
  `TrailborneSignTag`, `Sign_Interact_Patch`.

**Why inks become their own feature, not part of Signs:** ink is consumed by **two** features — Signs
(`BuildReq(InkLookupForSign(n), 1)`) **and** Cairns (`BuildReq(InkNameFor(color), 1)` in the cairn-marker recipe).
Folding ink into Signs would force `Cairns → Signs`, re-creating exactly the cross-feature coupling we're paying
PR C to remove. Promoting **Pigments** to a foundational feature gives both Signs and Cairns a clean downward
dependency on a shared *content* primitive.

### The M2 untangle (the "big mix")

M2 is 681 lines but it is **entirely Cairns** — the "mixed cairns + signs" note in the card body is stale; signs
live in M1. M2's internal mix is *four distinct concerns* that each become their own file inside `Features/Cairns/`
(content registration, the data tag, the interactable, the Harmony patches). The only **outbound** dependency is
`TrailborneM1.Ink*Name` (→ Pigments) via `InkNameFor`. No sign code in M2.

### The Registrar untangle (thin it out)

`TrailborneRegistrar` is fat (12.7 KB) because it owns both *plumbing* and *content*:

- **Plumbing** (the lifecycle Harmony hooks + the `DoObjectDBWiring` dispatch that calls each `Mn.RegisterPrefabs`
  / `Mn.DoObjectDBWiring` and `SpecCheck.Run()`) → graduates to **`Runtime/Registrar.cs`** as the thin dispatcher.
- **Content** — three standalone buildables/items that aren't cairns or signs:
  - Explorer's Bench (`piece_sbpr_explorers_bench`) → **Trailhead** (it is *the* crafting station every recipe
    attaches to).
  - Path Lamp (`piece_sbpr_path_lamp`) → **Trailhead**.
  - Trailblazer's Spade item (`SBPR_TrailblazersSpade`) + its recipe + `PublicSpadeName` → **Trailblazing**,
    co-located with the spade ops (M3) that populate its PieceTable. This deletes M3's awkward
    `TrailborneRegistrar.PublicSpadeName` back-reference.

---

## 2. Target tree

```
src/SBPR.Trailborne/
├── SBPR.Trailborne.csproj          (unchanged — SDK globs **/*.cs)
├── Directory.Build.props           (PR-B infra; unchanged)
├── Plugin.cs                       (was TrailbornePlugin.cs — entry point only)
│
├── Runtime/                        namespace SBPR.Trailborne.Runtime
│   ├── Registrar.cs                lifecycle hooks + feature dispatch (thin)
│   ├── Assets.cs                   was TrailborneAssets (PNG/clone/register/BuildReq)
│   ├── ServerContext.cs            was SBPRContext
│   ├── SpecCheck.cs                was TrailborneSpecCheck (drift watchdog)
│   └── RecipeHelpers.cs            NEW: deduped FindStation + HasRecipe(For) (see §4)
│
└── Features/
    ├── Pigments/                   namespace SBPR.Trailborne.Features.Pigments
    │   └── Pigments.cs             4 inks: names, icons, register, recipes
    │
    ├── Trailhead/                  namespace SBPR.Trailborne.Features.Trailhead
    │   └── Trailhead.cs            Explorer's Bench (station) + Path Lamp; ExplorersBenchName const
    │
    ├── Signs/                      namespace SBPR.Trailborne.Features.Signs
    │   ├── Signs.cs                4 painted signs: names, colors, pin types, register, ODB wiring
    │   ├── SignTag.cs              was TrailborneSignTag
    │   └── SignInteractPatch.cs    was Sign_Interact_Patch  (⚠ wire it — §6 R1)
    │
    ├── Cairns/                     namespace SBPR.Trailborne.Features.Cairns
    │   ├── Cairns.cs               was TrailborneM2 static: markers, pieces, tiers, comfort, ODB wiring
    │   ├── CairnTag.cs             was TrailborneCairnTag (ZDO tier + kitbash art)
    │   ├── CairnInteractable.cs    was TrailborneCairnInteractable (E / Shift+E)
    │   └── CairnPatches.cs         was TrailborneCairnPatches (WearNTear + SE_Rested)
    │
    └── Trailblazing/               namespace SBPR.Trailborne.Features.Trailblazing
        └── Trailblazing.cs         was TrailborneM3 + the Spade ITEM (prefab+recipe) lifted from Registrar
```

**Optional finer split (defer to a later PR, do NOT do in Stage 2):** `Cairns.cs` could later peel a
`CairnTiers.cs` (the tier/comfort tables) out of the registration class; Stage 2 should keep Cairns as one static
to minimise rename surface.

---

## 3. Namespace plan + prefix-drop rename table

**One namespace per folder:**

| Folder | Namespace |
|---|---|
| `/` (root) | `SBPR.Trailborne` |
| `Runtime/` | `SBPR.Trailborne.Runtime` |
| `Features/Pigments/` | `SBPR.Trailborne.Features.Pigments` |
| `Features/Trailhead/` | `SBPR.Trailborne.Features.Trailhead` |
| `Features/Signs/` | `SBPR.Trailborne.Features.Signs` |
| `Features/Cairns/` | `SBPR.Trailborne.Features.Cairns` |
| `Features/Trailblazing/` | `SBPR.Trailborne.Features.Trailblazing` |

**Rename table (drop the `Trailborne` prefix — the namespace scopes it). 24 type/member renames:**

| # | Old type / member | New type / member | New namespace | Notes |
|---|---|---|---|---|
| 1 | `TrailbornePlugin` | `Plugin` | `SBPR.Trailborne` | `BepInPlugin` attr carries the mod ID, not the class name — safe to rename. ~40 `TrailbornePlugin.Log` call sites → `Plugin.Log` (mechanical). |
| 2 | `TrailborneAssets` | `Assets` | `…Runtime` | broad utility; keep name 1:1 for predictable sweep |
| 3 | `SBPRContext` | `ServerContext` | `…Runtime` | *optional* — not required by the prefix-drop rule (it has no `Trailborne` prefix), but `…Runtime.SBPRContext` reads as `SBPR…SBPR`. Recommend the rename; if Daniel prefers minimal churn, keep `SBPRContext`. |
| 4 | `TrailborneSpecCheck` | `SpecCheck` | `…Runtime` | |
| 5 | `TrailborneRegistrar` | `Registrar` | `…Runtime` | **and** loses its content (bench/lamp/spade) — becomes thin dispatcher |
| 6 | `TrailborneM1` (ink half) | `Pigments` | `…Features.Pigments` | split out of M1 |
| 7 | `TrailborneM1` (sign half) | `Signs` | `…Features.Signs` | split out of M1 |
| 8 | `TrailborneSignTag` | `SignTag` | `…Features.Signs` | |
| 9 | `Sign_Interact_Patch` | `SignInteractPatch` | `…Features.Signs` | also drop the underscore style; **wire it (§6 R1)** |
| 10 | `TrailborneM2` | `Cairns` | `…Features.Cairns` | |
| 11 | `TrailborneCairnTag` | `CairnTag` | `…Features.Cairns` | |
| 12 | `TrailborneCairnInteractable` | `CairnInteractable` | `…Features.Cairns` | |
| 13 | `TrailborneCairnPatches` | `CairnPatches` | `…Features.Cairns` | |
| 14 | `TrailborneM3` | `Trailblazing` | `…Features.Trailblazing` | absorbs the Spade item from Registrar |
| 15 | `TrailborneRegistrar.PublicSpadeName` | `Trailblazing.SpadeName` | `…Features.Trailblazing` | back-reference deleted; M3 no longer reaches into Registrar |
| 16 | bench name literal in Registrar/M1/M2 | `Trailhead.ExplorersBenchName` | `…Features.Trailhead` | dedupe the `"piece_sbpr_explorers_bench"` magic string (see §6 R4 — **string value unchanged**) |
| 17 | (new) Trailhead static | `Trailhead` | `…Features.Trailhead` | houses bench + lamp |

**Cross-feature references after rename (all acyclic, all downward):**

- `Cairns` → `Pigments.InkRedName`/`…WhiteName`/`…BlueName`/`…BlackName` (via `InkNameFor`)
- `Signs` → `Pigments.Ink*Name` (via `InkLookupForSign`)
- `Cairns`, `Signs`, `Trailblazing`, `Pigments` → `Trailhead.ExplorersBenchName` (station lookup)
- `Runtime.SpecCheck` → `Pigments.Ink*Name`, `Cairns.Colors/MarkerName/CairnName/InkNameFor`
  (validation infra is *allowed* to read feature manifests — it is the cross-cutting drift guard)
- everyone → `Runtime.Assets.*`, `Plugin.Log`, `Plugin.PluginFolder`

**`_`-private-field drop (PR-B `.editorconfig` rule).** ~12 fields, all file-local, zero cross-file impact:
`_nview`, `_kitbashRoot`, `_lastBuiltTier` (CairnTag); `_tag`, `_wnt`, `_piece` (CairnInteractable);
`_holder` (Assets); `_icons` (Pigments/Signs); `_variants` (Trailblazing); `_harmony` (Plugin);
`_znetSceneDone`, `_objectDbDone` (Registrar). Rename to non-underscore (`nview`, `tag`, …). Pure local edits.

---

## 4. `Runtime/` graduation calls (the "≥2 features need it" test)

| Candidate | Consumers today | Verdict | Rationale |
|---|---|---|---|
| `TrailborneAssets` | M1, M2, M3, Registrar (4) | **→ Runtime** | Pure infra (clone/register/sprite/BuildReq). Clear ≥2. |
| `SBPRContext` | Registrar lifecycle hooks | **→ Runtime** | Foundational gate; gates *all* registration. Belongs with the bootstrap even though only the dispatcher reads it today. |
| `TrailborneSpecCheck` | called once by Registrar; *reads* every feature's recipes | **→ Runtime** | Cross-cutting validation. It is intentionally feature-aware (it's the drift guard) — that's an infra role, not a feature. |
| Lifecycle hooks + dispatch (in Registrar) | the whole mod | **→ Runtime/Registrar** | The thin wiring layer. |
| `FindStation(string)` | duplicated in M1, M2, Registrar (3 copies) | **→ Runtime/RecipeHelpers** | ≥2 — dedupe the 3 identical copies. |
| `HasRecipe`/`HasRecipeFor` | duplicated in M1, M2, Registrar | **→ Runtime/RecipeHelpers** | same dedupe |
| `Plugin.Log` / `Plugin.PluginFolder` | everywhere | **stays on `Plugin`** *(optional later graduation)* | Could become `Runtime.ModEnv`, but that's a ~40-site sweep with no structural payoff this PR. Leave on `Plugin`; note for a future PR. |

**Stays a FEATURE, does NOT graduate (even though ≥1 other thing references its *names*):**

- **Pigments** and **Trailhead** are *content*, not infra. Other features reference their public **name
  constants** (ink names, bench name) — that's a normal vertical-slice content dependency, not a reason to dump
  them in `Runtime/`. `Runtime/` is for plumbing; foundational *content* stays in `Features/`.
- `Capitalize` (M2-only) stays in Cairns. `TintMeshRenderers`, `InkLookupForSign` (sign-only) stay in Signs.

---

## 5. Migration order (build stays green at every commit)

The csproj uses `Microsoft.NET.Sdk` default globbing (`**/*.cs`), so **relocating/splitting `.cs` files needs no
project edit** and cannot break compilation by itself. Sequence so each commit compiles:

> **Step 0 — Pre-flight.** Confirm `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj` is green on `v1`
> first (needs the env-var SDK paths from PR B). Capture the baseline log.

**Step 1 — Physical reorg only (move + split, names & namespaces UNCHANGED).** `git mv` files into
`Runtime/` and `Features/<f>/`; split `TrailborneM1.cs` into ink/sign files and `TrailborneM2.cs` into its 4 types;
lift bench/lamp/spade out of `TrailborneRegistrar.cs`. **Every type keeps its old name and stays in namespace
`SBPR.Trailborne`.** Because it's one flat namespace still, *no* `using` changes and *no* reference edits are
needed. **Build green.** Commit: `refactor(structure): relocate files into Features/ + Runtime/ (no rename)`.

**Step 2 — Re-namespace, feature by feature.** For each folder, change the `namespace` declaration to the §3
target, then add the necessary `using SBPR.Trailborne.Features.<F>;` / `using SBPR.Trailborne.Runtime;` to every
referencing file. Do **Runtime first** (most-referenced), then Pigments, Trailhead, then Signs/Cairns/Trailblazing.
Build after each folder. Types are still prefixed, so the only churn is namespace + usings. Commit per folder (or
one commit) — green at each.

**Step 3 — Drop the `Trailborne` prefix (rename types).** Apply the §3 table, leaf types first
(`*Tag`, `*Interactable`, `*Patches`) then static classes (`Pigments`, `Signs`, `Cairns`, `Trailblazing`,
`Trailhead`, `Assets`, `SpecCheck`, `Registrar`, `Plugin`). Each rename is a global identifier replace; rebuild
between batches. **Update the two `_harmony.PatchAll(typeof(...))` calls in `Plugin` when their patch classes are
renamed** (see §6 R2). Commit: `refactor(naming): drop Trailborne type prefix (namespace-scoped)`.

**Step 4 — Move bench-name + spade-name constants to their owners.** Introduce `Trailhead.ExplorersBenchName`
and `Trailblazing.SpadeName`; replace the duplicated `"piece_sbpr_explorers_bench"` literals and the
`PublicSpadeName` references. **String VALUES unchanged** (§6 R4). Build green. Commit.

**Step 5 — Dedupe helpers into `Runtime/RecipeHelpers`.** Replace the 3 `FindStation` / `HasRecipe` copies with
calls to the shared helper. Build green. Commit.

**Step 6 — Drop the `_` field convention.** Rename underscore privates (file-local). Build green. Commit:
`style: drop _ private-field prefix per PR-B editorconfig`.

> Steps 4–6 are independent and may be reordered or squashed. Steps 1→2→3 are **ordered** — do not rename types
> (3) before namespaces exist (2), and do not re-namespace (2) before files are physically split (1), or you'll
> fight merge-noise and broken references simultaneously.

**Verification each step:** `dotnet build` green **and** `git grep -n "GetStableHashCode\|GetInt\|GetFloat\|\.Set(\|Config.Bind"`
to confirm no **string literal** changed (those are save/wire contracts — §6 R4).

---

## 6. Risk callouts

**R1 — 🐞 `Sign_Interact_Patch` is currently DEAD CODE (latent bug, surfaced by this audit).**
`Plugin.Awake` only calls `_harmony.PatchAll(typeof(TrailborneRegistrar))` and
`_harmony.PatchAll(typeof(TrailborneCairnPatches))`. `PatchAll(Type)` patches *only that type*; there is **no**
no-arg/assembly-wide `PatchAll()`. `Sign_Interact_Patch` (M1) is never passed to either call, so the Shift+E
"pin sign to map" behaviour **does not run today.** The refactor must decide consciously:
- **(a)** Preserve current behaviour: move the patch but leave it unregistered (it stays dead).
- **(b)** Fix it: register `SignInteractPatch`. **This ACTIVATES new in-game behaviour** (Shift+E on a painted
  sign starts pinning to the map) — that is a *behaviour change*, not a pure refactor, and must be its own commit
  / Daniel's explicit call, **not** smuggled into a "structure-only" PR.
- **Do NOT** "fix" this by switching to a blanket assembly-wide `_harmony.PatchAll()` as a side effect — that
  would silently flip (b) on. Recommend a separate follow-up card to wire it deliberately.

**R2 — Harmony registration is by TYPE; renaming a patch class without updating `PatchAll` silently disables it.**
`PatchAll(typeof(TrailborneCairnPatches))` and `PatchAll(typeof(TrailborneRegistrar))` reference the patch
*classes by type*. In Step 3, when `TrailborneCairnPatches → CairnPatches` and `TrailborneRegistrar → Registrar`,
the `typeof(...)` args in `Plugin` **must** be updated in the same commit, or the cairn patches / lifecycle hooks
go dark with no compile error (R1 is exactly this failure mode that already happened once).
- **Design recommendation (prevents recurrence):** give each feature an explicit `ApplyPatches(Harmony h)` (e.g.
  `Cairns.ApplyPatches`, `Signs.ApplyPatches`) and have `Plugin`/`Runtime.Registrar` call them in a list. A
  missing feature then fails to *compile* (missing method) instead of silently not patching. This is optional for
  Stage 2 but is the structural fix for the class of bug in R1/R2.

**R3 — Prefab/ZDO/config STRING literals are save-data & multiplayer wire contracts — never change the values.**
This mod ships **no AssetBundles and no `.meta` GUIDs** (it clones vanilla prefabs at runtime and loads PNGs by
filename via reflection). So there is **no asset-GUID breakage risk**. The real fragility is **string identifiers**,
keyed by `GetStableHashCode()` (hash of the *value*, not the type):
- Prefab names: `SBPR_InkRed/White/Blue/Black`, `piece_sbpr_sign_*`, `SBPR_CairnMarker_*`, `piece_sbpr_cairn_*`,
  `piece_sbpr_path_*`, `piece_sbpr_replant_*`, `piece_sbpr_explorers_bench`, `piece_sbpr_path_lamp`,
  `SBPR_TrailblazersSpade`.
- ZDO keys: `SBPR_CairnTier`, `SBPR_LastWearTick`.
- Config: section `Debug`, key `SBPR_DebugCairnDamage`.
Renaming the C# **const/field** is safe; changing the **string value** breaks existing worlds and client/server
sync. Migration moves *code*, not *strings* — verify with the `git grep` in §5.

**R4 — Bench-name dedupe must keep the literal `"piece_sbpr_explorers_bench"` exact.** Step 4 centralises the
station name into `Trailhead.ExplorersBenchName`. The constant's **value** must remain byte-identical (it's a
prefab name, R3). This is a pure reference swap, not a value change.

**R5 — Cairn config ownership (`DebugCairnDamage`).** Today `Plugin.Awake` binds the cairn-only
`DebugCairnDamage` config and Cairns reads `TrailbornePlugin.DebugCairnDamage`. Vertical-slice purity wants Cairns
to own its config (e.g. `Cairns.BindConfig(Config)` called from `Plugin`). **Recommend deferring** this to keep
Stage 2 mechanical — either leave the binding on `Plugin` (just rename `TrailbornePlugin.DebugCairnDamage` →
`Plugin.DebugCairnDamage`), or move it in a small isolated commit. Keep the config **key string** unchanged (R3).

**R6 — `ObjectDB.Awake` patch uses a string method target.** `[HarmonyPatch(typeof(ObjectDB), "Awake")]` and
`[HarmonyPatch(typeof(WearNTear), "Awake")]` target private methods by **string name**. The refactor doesn't touch
these targets, but note for Stage 2: do not "tidy" those strings into `nameof(...)` (no public `Awake` to point
at) — leave them verbatim.

**R7 — Split files must not orphan `using`s or duplicate them.** When splitting `TrailborneM1.cs` and
`TrailborneM2.cs`, each new file needs its own `using` set (`System`, `System.Collections.Generic`, `HarmonyLib`,
`UnityEngine`). Trivial, but a missing `using HarmonyLib;` in `SignInteractPatch.cs`/`CairnPatches.cs` is the most
likely Step-1 compile slip.

---

## 7. Handoff to Stage 2 (engineer)

Execute §5 in order, one commit per step, `dotnet build` green before each commit. The two ordered hazards are
**R2** (update `PatchAll(typeof(...))` args in the same commit as the patch-class renames) and **R3/R4** (never
change a string literal value). Treat **R1** (the unregistered sign patch) as **out of scope** for the structural
PR — open a separate card for Daniel to decide whether to wire it. The `Cairns.ApplyPatches`/`Signs.ApplyPatches`
registration pattern (R2) is the recommended structural fix but is optional for this PR.
