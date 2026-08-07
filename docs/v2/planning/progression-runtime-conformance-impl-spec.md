---
title: "Progression runtime conformance — the third boot-time drift guard (T036)"
status: current
purpose: Specify the boot-time conformance surface that compares the authored progression manifest (registry/palette version, Facets, the 20 stable node ids, required handlers, required woven patch classes, required per-node runtime providers) against the live build, and the honesty contract that keeps a green verdict from being read as proof of playability.
---

# Progression runtime conformance

**Task:** T036 (Tracer 9), plan §"Runtime conformance and observability", spec FR-027
**Code:** `src/SBPR.Niflheim.HomesteadStones/Features/Progression/ProgressionConformance.cs`
**Caller:** `Features/Progression/FoundationalRuntimeBootstrap.cs` (already-registered patch class — no new one)
**Tests:** `tests/NiflheimProgressionConformanceTests.cs`
**Related:** [`homestead-stone-progression-plan.md`](homestead-stone-progression-plan.md),
[`homestead-stone-progression-data-model.md`](homestead-stone-progression-data-model.md),
[`operator-shape-report-impl-spec.md`](operator-shape-report-impl-spec.md)

## What a player does

Nothing. This is for whoever is running the server, and for the next engineer who
changes authored content.

## Where it sits in the drift-guard family

This is the **third** boot-time guard in this repo, and it is deliberately shaped
like the other two:

| Guard | Manifest lives in | Catches |
|---|---|---|
| `SBPR.Trailborne/Runtime/SpecCheck.cs` | code | a recipe/piece that drifted from the locked spec |
| `Features/Diagnostics/PatchCheck.cs` | reflection | any `[HarmonyPatch]` class that wove nothing |
| **`ProgressionConformance.cs`** | code | authored progression content that drifted from the manifest, and named runtime seams that are missing |

All three ERROR-log and continue. Screaming beats bricking: refusing to boot over a
diagnostic turns one inert feature into a total outage.

## What it compares

| # | Expectation | Source of live truth |
|---|---|---|
| 1 | content registry version = 1, Facet palette version = 1 | `HomesteadProgressionCatalog`, `StoneFacetPalette` |
| 2 | classification `Settlement/Homestead` | the catalog |
| 3 | exactly one Profession + one Martial Facet, with their exact candidate Trees | the palette |
| 4 | the exact 20 stable node ids — Tree, Tree level, first-build status | `catalog.TryResolveNode`, per id |
| 5 | `20 = 13 executable + 7 unavailable` | enumerated by `OperatorShapeReport.Build` |
| 6 | five required command handlers composed | caller's `HomesteadHandlerWiringObserver` observation |
| 7 | sixteen required runtime seams actually wove | caller's `PatchCheck.WovenPatchClassNames` observation |
| 8 | one provider row per executable node, advertising a resolvable current-build id | the provider's own published constant, where it publishes one |
| 9 | startup recovery verdicts | `ReceiptRecovery`, counted (never re-classified) |

Counts are **not** re-tallied here. They come from `OperatorShapeReport.Build`, which is
already the one counting path over the catalog; a second tally would be exactly the
drift this family exists to catch. What this file adds is the **expected manifest** to
compare that enumeration against — SpecCheck's trick applied to progression content.

## Why the required-patch list exists next to `PatchCheck`

`PatchCheck` enumerates the classes that **exist** and asserts each wove at least once.
It is therefore structurally blind to a seam that was deleted or renamed out of the
assembly: nothing exists, so nothing is missing. This manifest names sixteen seams
explicitly, so removing one is an ERROR from the other direction. That is the ADO #125 /
IAP-015 failure family seen from its blind side. Each entry is proven individually
load-bearing by test (drop it, expect its own error).

## Known, named gaps — reported, not hidden

- **`BuiltToLast` and `FletchersHabit` have no runtime provider** in this build. They are
  authored, executable and purchasable-shaped, and nothing delivers their outcome. That is
  a WARNING with its own finding code, not an omission from the manifest.
- **`WeaponDisciplineCommandHandler` is composed by no root** (established by ADO #123). It
  is deliberately *not* in the required-handler list: encoding a known gap as a boot ERROR
  trains an operator to ignore the guard. It renders as an informational observation.

## Config gating, secrets and PII

The plan requires config-gated diagnostics that avoid secrets and raw PII. This surface
satisfies that **structurally**, which is stronger than by discipline: its only inputs are
authored content, the Facet palette, handler/patch type **names**, and integer counts. It
never accepts an `AccountId`, `CharacterId`, SteamID, principal, world path, integrity key
or journal payload, so no code path can emit one. Recovery is four integers; the operation
ids `ReceiptRecovery` knows are deliberately not surfaced. A test asserts the rendered text
contains none of them, in both gating modes.

`Diagnostics.VerboseProgressionConformance` (server-owned, default **false**) gates only the
per-Tree/per-node detail. **The verdict line and every WARNING/ERROR are always emitted** — a
drift guard an operator can silence is not a guard.

## The load-bearing caveat

**A GREEN CONFORMANCE REPORT PROVES SHAPE, NEVER PLAYABILITY.** Every assertion is about
authored identity, counts, composition and registration. None of it proves a joined client
can develop, purchase, craft, place or feel any of it. As with the operator shape report,
the disclaimer is **rendered into the report's own output** and asserted by test with a
negative control, so it cannot be silently dropped.

## Changing authored content

The manifest and the catalog and the data model move in the **same commit**. A node added,
removed, retiered or flipped between executable and unavailable produces a named ERROR
(`NODE-UNEXPECTED`, `NODE-MISSING`, `NODE-LEVEL`, `NODE-STATUS`) until all three agree.
`SpecCheck.cs` is untouched: this task registers no SBPR recipe or buildable, so its recipe
manifest count is unchanged (verified, not assumed).
