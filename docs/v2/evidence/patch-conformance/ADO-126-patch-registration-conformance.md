---
title: "ADO #126 — Generalised patch-registration conformance: PatchCheck port + PR-time guard"
status: proposed
author: starbright-engineering
date: 2026-08-04
purpose: >
  Close the CLASS of defect that has shipped three times in SBPR.Niflheim.HomesteadStones:
  a [HarmonyPatch] class that exists, compiles, passes its unit tests, and is never handed to
  harmony.PatchAll — so it ships as dead code and its feature is silently inert in-world.
  Delivers both halves: SBPR.Trailborne's boot-time PatchCheck ported to HomesteadStones, and
  a NEW general PR-time source-conformance test that the boot guard cannot substitute for.
---

# ADO #126 — generalised patch-registration conformance

## The defect class

HarmonyX patches **only** the types explicitly passed to `harmony.PatchAll(typeof(X))`. There is
no assembly-wide auto-scan. A `[HarmonyPatch]` class that is never passed compiles cleanly, ships
in the DLL, passes every unit test written against it, and does **nothing** — no build error, no
boot warning, no runtime signal.

Three occurrences in this assembly:

| # | Occurrence | Cause | Caught by |
|---|-----------|-------|-----------|
| 1 | IAP-015 operator surface (live smoke `t_48797ca3` at `04efd544`) | three classes never registered; `sbpr_pilotop` absent from `Terminal.commands` | a human, in-world |
| 2 | T030 Ready Hands, first failure (QA `t_2b1e690d`) | bound to `Humanoid`, which declares neither target method → discovery resolved ZERO methods | a human, in-world |
| 3 | T030 Ready Hands, second failure (ADO #125) | correct class, correct `Player` binding, absent from the `PatchAll` list | a static reachability trace, weeks later |

## Why the two existing guards do not cover it

Both are correct within their own scope; neither generalises.

- **`OperatorSurfaceConformance.Verify`** uses exactly the right technique — walk Harmony's
  registry, confirm a class produced a woven method — but asserts **three hardcoded roles**.
- **The CI metadata guard** (`.github/workflows/ci.yml`) reflects over `assembly_valheim.dll` and
  proves `Player.QueueEquipAction` / `QueueUnequipAction` still exist. It proves the **target** is
  valid and is structurally blind to whether the **patch class was registered**. It passed green
  throughout the entire period Ready Hands was dead.

## What landed

### 1. Boot-time guard — `Features/Diagnostics/PatchCheck.cs`

Ported from `SBPR.Trailborne/Runtime/PatchCheck.cs`, which HomesteadStones never received. Runs at
the END of `Plugin.Awake()`, after every `PatchAll` including `OperatorSurfaceConformance`.

Keys on the **declaring type of each woven patch method**, not on the target method. This is
load-bearing: two of our classes may patch the same vanilla method, so a target-method check (or a
coarse "patched-method count ≥ patch-class count") would see that target still owned by the
surviving sibling and let a forgotten registration pass. That shortcut would not have caught ADO #125.

Reports the two failure modes **distinctly**, because the operator needs to know which:

- `UNREGISTERED PATCH CLASS` — targets resolve fine, nothing wove → someone forgot `PatchAll`.
- `PATCH CLASS RESOLVES ZERO TARGETS` — the binding names no real method → the `Humanoid` mode.

Harmony's own `GetOriginalMethod` is `internal` to HarmonyX, so target resolution goes through the
public `AccessTools` surface (methods, constructors, property accessors). **Stated limit:** a class
using `TargetMethod()`/`TargetMethods()` computes its target in code and cannot be resolved
statically; it is treated as resolvable so it is never mislabelled as a bad binding. This affects
only which of the two CAUSES is printed — never the verdict. Such a class is examined at all only
when it already wove nothing, and it is dead either way.

Posture: **ERROR-log and continue** — scream, don't brick — matching Trailborne's PatchCheck and
`OperatorSurfaceConformance`. A dead patch class is serious, but refusing to boot over it turns one
inert feature into a total outage, and the guard is reflection over a live registry (the riskier
thing to hard-gate on). Contrast `HomesteadRuntimeDriftCheck`, which DOES fail closed because it
gates content realization on authored-data integrity.

### 2. PR-time guard — `tests/HomesteadPatchRegistrationConformanceTests.cs`

The boot guard only speaks on a live host, so it is **not deterministic CI coverage** — the defect
still reaches a server before anyone hears about it. This is the other half, and it answers the
card's design question 4 affirmatively: yes, a CI/test-time variant can assert this with no running
game, and it is both cheaper and earlier.

The repo's established idiom (`OperatorSurfaceRegistrationGuardTests`,
`InventoryOpenSuppressRegistrationGuardTests`) writes **one hand-authored guard per class**. That
does not scale, and it is precisely why ADO #125 slipped: nobody hand-wrote the twenty-ninth guard.
This test enumerates patch classes **from source**, so a new patch class is covered the moment it is
added and no one has to remember anything.

Two assertions, both directions:

- every `[HarmonyPatch]` class appears in `Plugin.Awake()`'s `PatchAll` list, or carries an explicit
  `[DeliberatelyUnregistered("reason")]`;
- every `PatchAll` registration names a type that actually declares `[HarmonyPatch]` (a stale
  registration weaves nothing and falsely implies a seam is armed).

### 3. Standing rule in `AGENTS.md`

Three repeats justify a standing constraint, per the card. Added to Hard constraints.

## Finding that reshaped the design: the guard flags ZERO classes today

Before writing anything, a throwaway `MetadataLoadContext` probe reflected over the **built** DLL and
diffed attributed classes against the `PatchAll` list at `5b2515e`:

```
attributed patch classes in assembly : 34
PatchAll(typeof(X)) registrations    : 34
WOULD BE FLAGGED                     : 0
REGISTERED BUT NOT ATTRIBUTED        : 0
```

This matters for two of the card's design questions:

- **The feared false-positive wall does not exist.** The card anticipated the config-gated admin
  seams (`RelationshipProvisioningAdmin`, `SavorProvisioningAdmin`,
  `MasterworkOwnershipProvisioningAdmin`, `LocalProgressionProvisioningAdmin`) crying wolf. They do
  not: **all four are unconditionally handed to `PatchAll`** (`Plugin.cs:124`, `:259`, `:215`, `:140`)
  — the gate lives INSIDE the patch, which checks its server-owned flag at runtime. They weave
  normally and pass with no opt-out. The `[DeliberatelyUnregistered]` attribute is therefore built as
  a **forward-looking** mechanism with zero current users, not a wall of suppressions.
- **The guard ships GREEN**, so its first red is a real regression rather than a backlog to triage.

## Design decisions (the card's four questions, answered)

1. **Does `OperatorSurfaceConformance` get absorbed?** **No — both kept, deliberately.** PatchCheck
   supersedes it in *coverage* but is *weaker per class*: it asserts "wove at least once" over all
   classes, while the operator check asserts three SPECIFIC named roles wove and prints a per-role
   `console= / server-request= / client-reply=` line that the live-smoke procedure reads by name.
   Deleting it would lose a documented operational signal to gain nothing.
2. **Fail-loud or fail-closed?** **Fail-loud-and-continue.** Rationale above.
3. **Does this belong in Trailborne too?** Trailborne **already has it** — this card is the port in
   the other direction. The PR-time test is currently HomesteadStones-scoped; extending it to
   Trailborne is a clean follow-up but was not done here (Trailborne has per-class guards already,
   and widening scope mid-card is how scope creep starts).
4. **Can a CI/test-time variant assert this without a running game?** **Yes** — delivered as #2 above.

## Verification

### AT-PATCH-CONFORMANCE-CATCHES — mutation-proven, red first

Each mutation applied to a known-good tree, test run, then reverted. The guard names the offending
class **by name**:

| Probe | Mutation | Result |
|-------|----------|--------|
| Baseline | none | **PASS** (2/2) |
| MUT1 | remove the Ready Hands `PatchAll` line — *the exact ADO #125 defect* | **FAIL**, names `ReadyHandsEquipDurationPatch` |
| MUT2 | remove the `OperatorCommandConsole` `PatchAll` line — *the IAP-015 defect* | **FAIL**, names `OperatorCommandConsole` |
| MUT4b | register `Domain.HomesteadPlacement`, which declares no `[HarmonyPatch]` | **FAIL**, names `HomesteadPlacement` (stale registration) |
| Restore | revert all | **PASS** (2/2) |

### AT-PATCH-CONFORMANCE-NO-FALSE-POSITIVE

| Probe | Mutation | Result |
|-------|----------|--------|
| MUT3 | unregister Ready Hands AND mark it `[Features.Diagnostics.DeliberatelyUnregistered("probe")]` | **PASS** — silent, via the explicit opt-out |

Note the opt-out is honoured in its **namespace-qualified** spelling. Two real scanner bugs were
found by these probes and fixed before landing: (a) qualified attribute names were not recognised,
so a legal C# spelling would have been silently ignored — the exact silence-by-omission failure this
card exists to end; (b) `[HarmonyPatchX]` matched `HarmonyPatch` for want of a word boundary. A third
was caught on the test's very first run, when it flagged `PatchCheck` itself: a naive substring
search matched `[HarmonyPatch]` inside PatchCheck's own ERROR-message string literals. All three are
now covered by attribute-syntax matching.

### AT-PATCH-CONFORMANCE-ZERO-METHODS

The `Humanoid`-mode branch (registered, binding resolves no target) is implemented and reported
distinctly in the **boot-time** guard. It is **NOT mutation-proven** — see honesty below.

### Build floor

- `SBPR.Niflheim.HomesteadStones` net48 Release: **0 warnings / 0 errors**
- `SBPR.Trailborne` net48 Release: **0 warnings / 0 errors**
- Full suite: **1594/1594** (was 1592; +2 new)
- `python3 scripts/docs-lint.py`: **OK — 238 docs**
- `git diff --check`: clean

## Honesty — what is NOT proven

- **The boot-time `PatchCheck.Run` has never executed on a live host.** No server was booted in this
  card. Its logic is a direct port of code proven in Trailborne, and the PR-time test that exercises
  the same *policy* is mutation-proven — but "logs green ≠ playable" cuts here too: what is verified
  is that it compiles, and that the equivalent source-level policy catches both historical defects.
  The boot line itself (`✓ All N registrable patch class(es) woven`) has not been observed.
- **`AT-PATCH-CONFORMANCE-ZERO-METHODS` is unproven at runtime.** Reproducing it requires actually
  weaving against a live `assembly_valheim` with a deliberately misbound class — a boot, not a unit
  test. The code path exists and is reasoned about; it has not been observed firing.
- **The `TargetMethod()`/`TargetMethods()` limitation is real**, stated in the source, and affects
  only which cause is printed for an already-dead class.
- The PR-time test is a **source-conformance** guard, not an execution test. It reads shipped source
  text. It cannot detect a class that is registered and woven but semantically wrong.
