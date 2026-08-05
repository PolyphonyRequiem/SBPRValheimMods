---
title: "Operator shape report — offline build-shape diagnostic (ADO #123)"
status: current
purpose: Specify the engine-free diagnostic that tells a server operator what shape a Homestead Stones build is in, and the honesty contract that keeps a green report from being read as proof of playability.
---

# Operator shape report — offline build-shape diagnostic

**Work item:** ADO #123 (parent #85 Homestead Stone Progression)
**Code:** `src/SBPR.Niflheim.HomesteadStones/Application/Diagnostics/OperatorShapeReport.cs`,
`.../Diagnostics/HomesteadHandlerWiringObserver.cs`
**Tests:** `tests/NiflheimOperatorShapeReportTests.cs`
**Related contracts:** [`homestead-stone-progression-contracts.md`](homestead-stone-progression-contracts.md),
[`homestead-stone-progression-data-model.md`](homestead-stone-progression-data-model.md)

## What a player does

Nothing. This is for whoever is running the server.

## What it provides

A rendered report naming five facts about the current build, every one derived from live code:

| # | Fact | Source of truth |
|---|------|-----------------|
| 1 | Content registry version | `HomesteadProgressionCatalog.CurrentContentRegistryVersion` |
| 2 | How many Trees and nodes exist | enumerated from `HomesteadProgressionCatalog.Nodes` |
| 3 | Which are executable, which honestly unavailable | the catalog's own `NodeFirstBuildStatus` |
| 4 | Which command handlers are wired | observed composition roots (see below) |
| 5 | What recovery did at startup | `ReceiptRecovery.InspectAll()` verdicts, counted |

## The load-bearing caveat

**A GREEN REPORT PROVES SHAPE, NEVER PLAYABILITY.** It says the pieces are present and composed. It
says nothing about whether a player can actually do the thing. AGENTS.md states this as a hard rule
("logs green ≠ playable") and this map has already been burned once by that distinction.

The disclaimer is therefore **rendered into the report's own output**, not left in a code comment:

- a one-line warning in the header, before the reader has scrolled anywhere;
- a `LIMITS OF THIS REPORT` section carrying `OperatorShapeReport.ShapeNotPlayabilityCaveat` verbatim;
- an inline note under the handler table clarifying that `COMPOSED` is not a claim the command path
  works end-to-end for a player.

A unit test asserts the caveat is present in the all-green rendering, with a negative control proving
that assertion is non-vacuous. If someone deletes the caveat, the suite fails.

## Honesty rules encoded in the design

1. **Counts are enumerated, never hardcoded.** `OperatorShapeReport.BuildFromRoster` is the single
   counting implementation; `Build(catalog, …)` is a thin adapter onto it. A test feeds a *different*
   roster through the same path and asserts every number follows it, so a count that can go stale
   fails CI.
2. **The catalog's self-description is checked, not trusted blindly.** `HomesteadProgressionCatalog`
   declares `ExpectedAuthoredNodeCount` / `ExpectedExecutableNodeCount` / `ExpectedUnavailableNodeCount`.
   The report trusts the *enumeration* and emits a `CATALOG SELF-DESCRIPTION DRIFT` section when the
   declared constants disagree — it never silently prefers one number.
3. **Unknown beats inferred.** `WiringState` has three states — `Composed`, `NotComposed`,
   `NotChecked` — and `NotChecked` is never collapsed into either. Nothing reflects over the runtime
   to guess wiring: the caller passes what it actually observed, and a composition root it did not
   supply yields `NOT CHECKED` for every handler that root owns.
4. **Recovery is reused, not reimplemented.** The report counts `ReceiptRecovery`'s own
   `RECOVERABLE` / `QUARANTINE` / `CLEAN` verdicts. With no recovery store supplied it reports
   `NOT CHECKED` rather than a reassuring zero, and a journal it cannot read is reported as a read
   failure, not as clean.
5. **The diagnostic never takes down the thing it observes.** Journal-read failures are caught and
   reported; the net48 caller wraps the whole emission in a try/catch that degrades to a warning.

## Handler wiring — what "wired" means

"Wired" means **composed into the live runtime**, not "the type exists". A type can compile, ship,
pass its unit tests, and have zero runtime callers — that is the exact class of defect this repo has
shipped repeatedly (the three unregistered operator patch classes; the `PurchaseCommandHandler` the
T021 investigation found had no production composition at all).

| Handler | Composition root | Current build state |
|---------|------------------|---------------------|
| `RelationshipCommandHandler` | `FoundationalProgressionServer.Create` | composed |
| `ActivityCommandHandler` | `LocalProgressionServer.Create` | composed |
| `DevelopmentCommandHandler` | `LocalProgressionServer.Create` | composed |
| `FacetCommandHandler` | `LocalProgressionServer.Create` | composed |
| `LocalPolicyCommandHandler` | `LocalProgressionServer.Create` | composed |
| `PurchaseCommandHandler` | `LocalProgressionServer.CreateLocalProvisioningIngress()` | on demand only — production reaches it solely through the config-gated, admin-only provisioning seam, so the boot report says NOT COMPOSED |
| `WeaponDisciplineCommandHandler` | *(none)* | ships and is unit-tested, but **no composition root constructs one** |

The last row is a finding, not a defect this card fixes. ADO #123 observes; it does not restructure.
Changing how handlers are composed is explicitly out of scope — if `WeaponDisciplineCommandHandler`
should be wired, that is a separate card.

## Where the report surfaces

**Decision: one boot-time emission, from inside an already-registered patch class.**

`FoundationalRuntimeBootstrap.OnZNetAwake` is the one place where both composition roots and the
receipt store are simultaneously in hand, and it already runs exactly once on the authoritative
server. The report is emitted there as a `Plugin.Log.LogInfo` immediately after the Local progression
runtime is composed.

Rationale:

- **Smallest thing that satisfies "an operator can ask."** The operator reads the server log they
  already read; no new console command, no new wire verb, no new authority surface to get wrong.
- **No new Harmony patch class.** AGENTS.md is emphatic that an unregistered `[HarmonyPatch]` class
  compiles, ships, passes its tests and does nothing — it has shipped three times. The safest way to
  not reintroduce that bug is to add no patch class at all. `FoundationalRuntimeBootstrap` is already
  in `Plugin.Awake()`'s `PatchAll` list, so this code cannot be silently inert.
- **The net48 side owns no logic.** It calls `HomesteadHandlerWiringObserver.Observe` and
  `OperatorShapeReport.BuildAndRender`, both engine-free and unit-tested. This follows the existing
  `FoundationalProgressionServer.Create` (engine-free, tested) vs `FoundationalRuntimeBootstrap`
  (net48, untested) split.

The structured `OperatorShapeSnapshot` is public, so a later card can hang the same facts off an
operator command without a second renderer. There is deliberately one renderer.

## Named acceptance

| ID | Assertion |
|----|-----------|
| AT-SHAPE-RENDER | The report renders and names registry version, Tree count, node counts, and per-Tree rows from the live catalog. |
| AT-SHAPE-COUNTS-TRACK | A perturbed roster moves every count and Tree row — no tally is a literal. |
| AT-SHAPE-DECLARED-DRIFT | Declared `Expected*NodeCount` constants disagreeing with the roster produce a visible drift section; enumerated numbers still win. |
| AT-SHAPE-UNAVAILABLE | Unavailable nodes are listed by name from the catalog's own status; an executable node never appears there. |
| AT-SHAPE-WIRED | Composed handlers report `COMPOSED`; `PurchaseCommandHandler` reports `COMPOSED` only once an ingress exists; `WeaponDisciplineCommandHandler` reports `NOT COMPOSED`. |
| AT-SHAPE-NOTCHECKED | With no composition roots supplied, every handler renders `NOT CHECKED` — never a guessed green. |
| AT-SHAPE-RECOVERY | Counts match `ReceiptRecovery.InspectAll()` exactly, including a real `QUARANTINE` produced by a simulated partial write. |
| AT-SHAPE-RECOVERY-UNCHECKED | With no recovery store, the report says `NOT CHECKED` and emits no zero-count line. |
| AT-SHAPE-CAVEAT | The rendered all-green report contains the shape-not-playability caveat verbatim (with a non-vacuous negative control). |

## What this card does NOT prove

Everything above is about the report's own correctness against the real catalog and a real durable
journal, verified offline. **None of it proves any reported-present thing works for a player.** No
live server, no joined client, no in-world test was run. That distinction is the entire point of the
card, and the artifact states it in its own output so a reader cannot miss it.

## SpecCheck

+0 rows. This card adds no recipe, piece, item, or station.
