---
status: current
---

# Patch-registration conformance evidence — machine index

Machine-readable index of the patch-registration conformance evidence. Human companion:
[README.md](README.md).

## Governing artifacts

- Standing rule: `AGENTS.md` § Hard constraints — "Every `[HarmonyPatch]` class MUST be registered
  in `Plugin.Awake()`"
- Boot-time guard (Niflheim): `src/SBPR.Niflheim.HomesteadStones/Features/Diagnostics/PatchCheck.cs`
- Boot-time guard (Trailborne, the port source): `src/SBPR.Trailborne/Runtime/PatchCheck.cs`
- PR-time guard: `tests/HomesteadPatchRegistrationConformanceTests.cs`
- Narrow predecessor retained by design:
  `OperatorSurfaceConformance` in `src/SBPR.Niflheim.HomesteadStones/Features/PilotIdentity/OperatorCommandIngressObserver.cs`
- Work items: ADO #126 (this card), ADO #125 (the third instance it generalises)

## Evidence documents

| File | Kind | Date | Status | Result |
|---|---|---|---|---|
| [`ADO-126-patch-registration-conformance.md`](ADO-126-patch-registration-conformance.md) | implementation + mutation proof | 2026-08-04 | proposed | guard ships GREEN; both historical defects reproduced red-first |

## The defect class — occurrences

| id | occurrence | cause | detected by |
|----|-----------|-------|-------------|
| C1 | IAP-015 operator surface (live smoke `t_48797ca3` at `04efd544`) | three classes never handed to `PatchAll`; `sbpr_pilotop` absent from `Terminal.commands` | human, in-world |
| C2 | T030 Ready Hands, first failure (QA `t_2b1e690d`, PR #376) | bound to `Humanoid`, which declares neither target method → discovery resolved ZERO methods | human, in-world |
| C3 | T030 Ready Hands, second failure (ADO #125, fixed in PR #487 / `5b2515e`) | correct class, correct `Player` binding, absent from the `PatchAll` list | static reachability trace |

## Acceptance claims

| id | claim | artifact |
|----|-------|----------|
| A1 | `AT-PATCH-CONFORMANCE-CATCHES`: removing the Ready Hands registration (the exact C3 defect) fails the guard, naming `ReadyHandsEquipDurationPatch`; restoring it returns green | mutation probe MUT1, `tests/HomesteadPatchRegistrationConformanceTests.cs` |
| A2 | `AT-PATCH-CONFORMANCE-CATCHES`: removing the `OperatorCommandConsole` registration (the C1 defect) fails the guard, naming `OperatorCommandConsole` | mutation probe MUT2 |
| A3 | Stale-registration direction: a `PatchAll(typeof(X))` naming a type that declares no `[HarmonyPatch]` fails the guard, naming `HomesteadPlacement` | mutation probe MUT4b |
| A4 | `AT-PATCH-CONFORMANCE-NO-FALSE-POSITIVE`: an unregistered class carrying `[DeliberatelyUnregistered("reason")]` is silent — via the explicit opt-out, not via invisibility. Honoured in namespace-qualified spelling | mutation probe MUT3 |
| A5 | The guard ships GREEN on `5b2515e`: 34 attributed patch classes, 34 registrations, zero flagged, zero stale. Its first red is a real regression, not a backlog | `MetadataLoadContext` probe over the built DLL |
| A6 | All four config-gated admin seams are unconditionally registered (`Plugin.cs:124`, `:140`, `:215`, `:259`) — the gate lives INSIDE the patch. The anticipated false-positive wall does not exist; `[DeliberatelyUnregistered]` ships with zero users | `Plugin.cs`; probe in A5 |
| A7 | `AT-PATCH-CONFORMANCE-ZERO-METHODS`: the C2 failure mode (registered, binding resolves no target) is reported DISTINCTLY from "never registered" at boot | `Features/Diagnostics/PatchCheck.cs` — **implemented, NOT runtime-proven** (see A11) |
| A8 | Both net48 Release builds 0 warnings / 0 errors; full suite 1594/1594 (+2); docs-lint OK 238; `git diff --check` clean | build/test logs (this run) |
| A9 | `OperatorSurfaceConformance` deliberately RETAINED, not absorbed: PatchCheck is broader per assembly but weaker per class ("wove ≥1" vs "these three named roles wove" + per-role log line read by the live-smoke procedure) | `Plugin.cs` inline comment; evidence doc §Design decisions |
| A10 | Three real scanner bugs were found by the probes and fixed before landing: qualified attribute names unrecognised; `HarmonyPatchX` matching `HarmonyPatch`; `[HarmonyPatch]` inside string literals flagging the guard class itself | evidence doc §AT-PATCH-CONFORMANCE-NO-FALSE-POSITIVE |
| A11 | **NOT PROVEN:** `PatchCheck.Run` has never executed on a live host — no server was booted in this card. The boot line has not been observed. "Logs green ≠ playable" applies to the guard itself | evidence doc §Honesty |
| A12 | **NOT PROVEN:** A7's zero-target branch is unproven at runtime; reproducing it requires weaving against a live `assembly_valheim` with a deliberately misbound class. A `TargetMethod()`/`TargetMethods()` class cannot be resolved statically — affecting which CAUSE is printed, never the verdict | evidence doc §Honesty |

## Scope boundaries

- The PR-time test is **HomesteadStones-scoped**. Extending it to `SBPR.Trailborne` (which has
  per-class guards and its own boot-time `PatchCheck`) is a clean follow-up, deliberately not done
  here.
- The PR-time test is a **source-conformance** guard, not an execution test. It cannot detect a
  class that is registered and woven but semantically wrong.
