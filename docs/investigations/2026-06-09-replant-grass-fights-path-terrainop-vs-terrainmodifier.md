---
title: "Spade Replant-Grass fights the Path tool — wrong donor *system* (TerrainModifier vs TerrainOp)"
status: current
last_updated: 2026-06-09
task: t_d48ac283
spec_anchor: "docs/v0.1.0/planning/requirements.md:420 (§A2.2 — Replant Grass mirrors the Cultivator's 'Grass' mode)"
proposed_fix_in: "src/SBPR.Trailborne/Features/Trailblazing/Trailblazing.cs (donor swap; gated on PR review + Daniel in-game verify)"
---

# Spade Replant-Grass fights the Path tool — wrong donor *system*

- **Date:** 2026-06-09
- **Investigator:** architect (clean-side; read Valheim's OWN assemblies — permitted, see card t_d48ac283 routing)
- **Trigger (Daniel, 2026-06-08 in-game playtest, v0.2.9, verbatim):**
  > "the path tool seems to work fine, but the grass tool doesn't work right, it
  > seems to fight or conflict with the path tool. The cultivator's grass function
  > works properly though against our path, so I would say look into the cultivator's
  > implementation."
- **Status:** Root cause located, ground-truthed against this build's `assembly_valheim.dll`
  and the shipped prefab payload. Fix is SMALL and isolated (one donor constant + one
  registration branch). **No production code changed in this investigation** — architect
  designs/specs, engineer implements. Gated on PR review + Daniel's in-game verify.

---

## TL;DR — what's actually wrong

Daniel's lead was exactly right. We cloned the **wrong donor system**, not just the wrong
prefab.

Valheim ships **two parallel terrain-op systems** that both reuse the same
`TerrainModifier.PaintType` enum but apply paint by completely different mechanisms:

| System | Prefabs | Component | Networked? | How it applies |
|---|---|---|---|---|
| **LEGACY** | `replant`, `path`, `cultivate`, `paved_road`, `mud_road`, `raise` | `TerrainModifier` | **Yes — has `ZNetView`** | Placed as a **persistent networked piece**. `OnPlaced` runs `RemoveOthers()`, a precedence battle. The piece lives on in the ZDO and competes forever. |
| **MODERN** | `replant_v2`, `cultivate_v2`, `path_v2`, `paved_road_v2`, `mud_road_v2`, `raise_v2` | `TerrainOp` | **No `ZNetView`** | On `Awake`, applies its settings straight into the heightmap's terrain **compiler** (`GetAndCreateTerrainCompiler().ApplyOperation(this)`), then **destroys its own GameObject**. The paint is baked into the compiled mask; nothing persists to fight. |

- **The vanilla Cultivator uses the MODERN system.** Its "Grass"/Replant op is
  **`replant_v2`** (a `TerrainOp`). That's why it cleanly regrasses our path: it bakes a
  `Reset` paint straight into the compiler, overriding whatever paint was there.
- **Our spade clones the LEGACY `replant`** (`Trailblazing.cs:64` `SourceReplant = "replant"`).
  That donor is a `TerrainModifier` **networked piece**. Our path op also clones a LEGACY
  `path` (`TerrainModifier`). So we end up with **two persistent, competing networked
  modifiers stacked on the same tile**, and `RemoveOthers()`'s precedence rule means the
  grass op does **not** evict the path op — they coexist and fight. That is the "grass
  fights path" Daniel saw.

The card's **Candidate A is correct** ("the correct vanilla donor is the Cultivator's
grass op, not the bare `replant` prefab"). **Candidate B is the right *mechanism*-level
description** (paint conflict), but the cause isn't a missing flag on our clone — the two
prefabs carry **identical** `TerrainModifier` settings; the conflict is structural to the
legacy networked-piece system. **Candidate C is wrong**: our path is not a separate placed
Piece that the replant can't override — both are terrain ops; the issue is op *coexistence*,
not piece-vs-terrain.

> **Open question (card) — RESOLVED.** "Is our donor op (`replant`) actually the
> Cultivator's grass op?" **No.** The Cultivator's grass op is **`replant_v2`** (modern
> `TerrainOp`), confirmed by reading `_CultivatorPieceTable.m_pieces`. We clone the legacy
> `replant`. Same paint type (`Reset`), **different application system**. The
> `Trailblazing.cs:235` code comment ("`replant` is the grass-restore op… confirmed against
> the public Cultivator wiki") is half-right (right *paint*, wrong *prefab/system*) and must
> be corrected.

---

## Evidence trail (ground-truthed, this build)

All facts below were extracted **offline** from this build's shipped server payload — no
third-party mod code, no GPL source. Methods:

- **`ilspycmd -t <Type>`** (ILSpy 9.1.0.7988 dotnet global tool) on
  `…/valheim_server_Data/Managed/assembly_valheim.dll` — to read the `PaintType` enum and
  the `TerrainModifier` / `TerrainOp` apply logic. (Reading Valheim's own assembly to mod
  the base game is explicitly permitted for clean-side per card t_d48ac283; nothing
  decompiled is committed.)
- **`vprefab` + a UnityPy `read_typetree()` subprocess** over the dedicated-server asset
  bundles — to read the actual serialized field VALUES of each op prefab and to enumerate
  the Hoe/Cultivator `PieceTable.m_pieces`.

### 1. `TerrainModifier.PaintType` enum (assembly_valheim.dll)

```
0 = Dirt
1 = Cultivate
2 = Paved
3 = Reset            ← restores natural ground cover (grass on grass biomes)
4 = ClearVegetation
```

### 2. Serialized op settings (prefab payload)

Legacy `TerrainModifier` ops:

| prefab | paintType | paintR | paintCleared | level/levelR | smooth/smoothR |
|---|---|---|---|---|---|
| `replant` | **3 = Reset** | 2.2 | 1 | 0 / 2.0 | 0 / 3.0 |
| `cultivate` | 1 = Cultivate | 3.0 | 1 | 0 / 2.0 | 1 / 3.0 |
| `path` | 0 = Dirt | 2.0 | 1 | 0 / 2.0 | 0 / 3.0 |
| `paved_road` | 2 = Paved | 2.2 | 1 | 0 / 2.0 | 1 / 3.0 |
| `raise` | 0 = Dirt | 2.5 | 1 | **1 / 1.5** | 0 / 3.0 |

Modern `TerrainOp` ops (the `m_settings` block):

| prefab | paintType | paintR | level | smooth | raise |
|---|---|---|---|---|---|
| **`replant_v2`** | **3 = Reset** | 2.2 | 0 | 0 | 0 |
| `cultivate_v2` | 1 = Cultivate | 3.0 | 0 | 1 | 0 |
| `path_v2` | 0 = Dirt | 2.0 | 0 | 0 | 0 |
| `paved_road_v2` | 2 = Paved | 2.2 | 0 | 1 | 0 |
| `mud_road_v2` | 0 = Dirt | 3.0 | 0 | 1 | 0 |
| `raise_v2` | 0 = Dirt | 2.5 | 0 | 0 | **1** |

> **`replant` and `replant_v2` carry IDENTICAL paint settings** (Reset, paintR 2.2, no
> level/smooth/raise). The bug is NOT a wrong field value on our clone — it is the wrong
> *application system*.

### 3. Which ops each vanilla tool actually exposes (resolved `PieceTable.m_pieces`)

```
_HoePieceTable        : mud_road_v2, raise_v2, path_v2, paved_road_v2, Placeable_Stone
_CultivatorPieceTable : cultivate_v2, replant_v2, sapling_*, *_Sapling, Vine*_sapling
```

Both vanilla tools are **100% modern `TerrainOp` (`_v2`)** for their terrain work. The
legacy `TerrainModifier` prefabs (`replant`, `path`, …) are **not on any current tool's
build menu** — they are the older generation the `_v2` ops superseded. We are cloning a
deprecated donor.

### 4. The mechanism, from the assembly

`TerrainModifier` (legacy, our donor) — persistent, ZNetView-bearing, precedence-gated:

```csharp
private void OnPlaced() {
    RemoveOthers(transform.position, GetRadius() / 4f);   // ← precedence battle
    ...
}
private void RemoveOthers(Vector3 point, float range) {
    ...
    foreach (TerrainModifier item in list) {
        if ((m_level || !item.m_level)
            && (!m_paintCleared || m_paintType != PaintType.Reset
                || (item.m_paintCleared && item.m_paintType == PaintType.Reset))
            && item.m_nview && item.m_nview.IsValid()) {
            item.m_nview.Destroy();                        // only destroys SOME neighbours
        }
    }
}
```

Our replant op is `m_paintType == Reset`, so the middle clause
`(m_paintType != PaintType.Reset)` is **false** → a Reset op only removes *other Reset* ops.
It does **NOT** remove our `path` op (paint `Dirt`). The path modifier survives as a
persistent peer, and the two stacked modifiers are resolved by `Heightmap.TerrainVSModifier`
sort order — the Dirt paint keeps winning where the player painted the path. ⇒ grass "fights"
the path.

`TerrainOp` (modern, the Cultivator's system) — fire-and-forget into the compiler:

```csharp
private void Awake() {
    if (m_forceDisableTerrainOps) return;
    var maps = ...; Heightmap.FindHeightmap(transform.position, GetRadius(), maps);
    foreach (Heightmap hm in maps)
        hm.GetAndCreateTerrainCompiler().ApplyOperation(this);   // bake into compiled mask
    OnPlaced();
    Object.Destroy(gameObject);                                  // nothing persists to fight
}
```

No ZNetView, no `RemoveOthers`, no persistent peer. The Reset paint is composited into the
terrain compiler's paint mask, overriding the path's Dirt paint deterministically. This is
why the Cultivator wins.

---

## Why this didn't surface earlier

- PR #16 ("UBER level" bug) correctly stopped replant from raising/leveling terrain by
  leaving `m_levelRadius`/`m_smoothRadius` untouched. That fix is **orthogonal** to this bug
  and must be preserved — the donor swap below keeps the same "paint-only, never raise/level"
  guarantee (the modern `replant_v2` has `level=0, smooth=0, raise=0`, which is *strictly
  safer* than the legacy clone).
- The legacy `replant` *does* visibly restore grass on **bare/un-modified** terrain, so it
  looked correct in isolation. The conflict only appears when a second persistent modifier
  (our path) occupies the same tile — exactly Daniel's repro.

---

## Proposed fix (spec for the engineer — DESIGN ONLY, gated)

**Strategy: swap the replant donor from the legacy `replant` to the modern `replant_v2`,
so our grass op uses the same fire-and-forget `TerrainOp` compiler path the Cultivator
does.** Keep the path ops as-is for now (they "work fine" per Daniel) — but see Decision 2.

### Decision 1 — Replant donor: `replant` → `replant_v2` (REQUIRED)

- Change `Trailblazing.cs:64` `SourceReplant = "replant"` → `"replant_v2"`.
- `replant_v2` is a `TerrainOp` (no `ZNetView`, no `Piece`-as-networked-modifier). It is
  registered/cloned the same way (`Assets.ClonePrefab` parents under the **inactive** holder,
  so `TerrainOp.Awake` does NOT fire during registration — verified `Assets.cs:48`
  `holder.SetActive(false)`). Safe with the existing pipeline.
- **Radius scaling must move from `m_paintRadius` on a `TerrainModifier` to
  `m_settings.m_paintRadius` on the `TerrainOp`.** `RegisterRadiusVariant` currently does
  `clone.GetComponentInChildren<TerrainModifier>().m_paintRadius = radius`. For a `_v2` donor
  there is no `TerrainModifier`; the field lives at `TerrainOp.m_settings.m_paintRadius`.
  The grass-restore branch must write **only** `m_settings.m_paintRadius` (and leave
  `m_settings.m_level/m_smooth/m_raise` at their stock `false`), preserving the PR #16 guard
  by construction.

### Decision 2 — Path ops: leave LEGACY for now, but file the parallel risk (OPEN, route to a follow-up card)

- Daniel says the **path tool works fine**, so do not touch it in this fix. But note: our
  path op is *also* a legacy `TerrainModifier` networked piece. Two of our own path ops on
  adjacent tiles will exhibit the same legacy precedence behavior the Cultivator avoids. It
  has not bitten us because Dirt-vs-Dirt precedence is visually benign. **Recommendation:**
  a separate follow-up to migrate path/paved/clear ops to `path_v2`/`paved_road_v2` for
  consistency and to inherit the compiler's deterministic compositing. **Out of scope for
  this card** (don't expand a grass-bug fix into a path rewrite) — file as its own task.

### Decision 3 — Correct the load-bearing comment

`Trailblazing.cs:235` asserts `replant` *is* the Cultivator's grass op. After the swap, the
comment must state the verified truth: the Cultivator's grass op is **`replant_v2`** (a
`TerrainOp`), the legacy `replant` is the deprecated `TerrainModifier` generation, and we
clone `replant_v2` to inherit the compiler-applied, non-conflicting behavior.

### Spec / SpecCheck sync (per repo AGENTS.md — code+spec+manifest move together)

- This is a **bug-fix that changes runtime behavior**, so the spec text must move with the
  code in the same PR. `requirements.md:420` already states the intended behavior ("mirrors
  the Cultivator's 'Grass' mode… NOT cultivate, NO terrain raise/level"); reality now matches
  it after the fix, so **no requirement wording change is needed** — but add a one-line
  drift note / changelog entry crediting this investigation so the divergence history
  survives.
- **`SpecCheck.cs` manifest:** verified — `SpecCheck.cs` does **not** reference any op
  donor prefab name (`replant`/`_v2`/`TerrainModifier`/`TerrainOp` all return 0 matches as
  of this build), and the replant width count is unchanged (still 3). **No manifest change
  is required** for this fix.

---

## Acceptance tests (named — engineer + Daniel verify)

- **AT-REPLANT-1 (primary):** Lay a path on grass (grass → dirt/paved). Apply Spade
  Replant-Grass (any of 1.5/3/5 m) over it. **Grass returns over the path tiles**, matching
  the vanilla Cultivator's Replant result on the same path. (Daniel verifies in-game against
  the Cultivator as the reference.)
- **AT-REPLANT-2 (PR #16 regression guard):** At **every** replant width (1.5/3/5 m), the op
  performs **no terrain raise / level / smooth** — flat ground stays flat. (`replant_v2`
  ships `level=0, smooth=0, raise=0`; the registration branch writes only
  `m_settings.m_paintRadius`.)
- **AT-REPLANT-3 (width scaling):** The regrass footprint scales with the selected width
  (≈1.5 m / 3 m / 5 m), i.e. `m_settings.m_paintRadius` is set per-variant.
- **AT-REPLANT-4 (flat stamina, §A3.9):** All three widths still cost the same flat path/
  replant stamina (unchanged — stamina is pinned on the spade item, not the op piece).
- **AT-REPLANT-5 (no orphaned ZDO):** Because `replant_v2` is a `TerrainOp` with no
  `ZNetView`, applying it leaves no persistent networked modifier (verify no ZDO-orphan
  warnings in the server log on apply — this also *removes* a latent networked-piece liability
  the legacy donor carried).

## Routing

- **Implementation** → an engineer profile (clean-side). The donor swap + the
  `RegisterRadiusVariant` `TerrainOp.m_settings` branch are the whole change.
- **Firewall note:** all evidence here came from Valheim's OWN assembly/assets (permitted).
  No third-party mod code was read or copied. Do not commit the decompiled `TerrainModifier.cs`
  / `TerrainOp.cs` scratch files — they live only in the task workspace.
- **Follow-up card (separate):** migrate path/paved/clear ops to the `_v2` `TerrainOp`
  system for consistency (Decision 2). Not part of this fix.
