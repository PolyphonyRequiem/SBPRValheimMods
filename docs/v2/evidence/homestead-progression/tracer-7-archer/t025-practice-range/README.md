---
status: current
---

# T025 — Practice Range: joined-client placement/recipe/impact evidence

Node: Archer / Practice Range (Local Effect, Stone-cultivated, executable).
Acceptance: `AT-PRACTICE-RANGE`, `AT-PRACTICE-ARROW-DAMAGE`, `AT-TARGET-RETURN`.

## Status: JOINED-CLIENT PROOF PENDING OWNER CLEARANCE

At implementation time a live `valheim.x86_64` client (Steam AppId 892970) was
running under the owner's desktop session. Per the task's joined-client safety
gate, no QA client was launched: an occupied client must be preserved and the
in-world proof deferred until the owner grants clearance. Server/engine-free work
(implementation, tests, both net48 Release builds, docs) proceeded and is
recorded below; the in-world artifact is the one remaining item and is captured
in a follow-up run once the desktop is free.

The engine-free vertical (below) is fully green and shipped; what remains is
exercising it on a joined client. **Logs-green is never playability** — this
node is not "done" until the in-world capture lands.

## Engine-free proof (shipped, green)

Provider: `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/PracticeRangeProvider.cs`.
Tests: `tests/NiflheimPracticeRangeTests.cs` (21 facts, all passing under the
net8 link-compiled suite; full suite 1216/1216).

### AT-PRACTICE-RANGE — capability = active Local Effect AND ordinary build Permission

- The Practice Range Local Effect's active/dormant status is re-derived through the
  shipped T014 `LocalEffectActivationView` (single Settlement Local policy +
  relationship/governance/level dormancy, no second ledger).
- The placement (exact vanilla Archery Target) and recipe (Practice Arrow)
  capabilities are the load-bearing AND of that active status and the occupant's
  ordinary build Permission (spec FR-016 final sentence). Proven: policy-eligible
  without build Permission → no capability; build-permitted but outside the policy
  → no capability; outside the Stone Area / missing authorized Governor / Archer
  Tree uncommitted / Active Stone Level below node level / undeveloped node → all
  dormant.

### AT-PRACTICE-ARROW-DAMAGE — 100 for 8 Wood, 0 ammo damage, bow damage retained

- Authored Practice Arrow recipe is exactly 100 arrows for 8 Wood
  (`PracticeRangeContent.PracticeArrowRecipe`).
- Practice Arrow ammo damage is `0`; the fired shot's effective damage equals the
  bow's own draw damage across sampled draw values (0, 22, 47.5) — the arrow adds
  and removes nothing from the weapon's output.

### AT-TARGET-RETURN — deterministic vanilla return, no roll

- A practice arrow terminally impacting the `ArcheryTarget` surface is returned
  exactly once, `TargetReturnWon = true`, `Deterministic = true`, stable across
  repeated evaluations (no RNG).
- Every other terminal surface (Ground/Water/Creature/LostOrExpired) returns
  nothing and does not win — yielding to the ordinary path and to the later
  Fletcher's Habit recovery roll (T027).

## Build / suite

- `dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release` → 1216/1216 passed
  (+21 for this node).
- `dotnet build src/SBPR.Niflheim.HomesteadStones/...csproj -c Release` → 0w/0e.
- `dotnet build src/SBPR.Trailborne/...csproj -c Release` → 0w/0e.
- `python3 scripts/docs-lint.py` → OK. `git diff --check` → clean.

## Vanilla-binding note

The exact Archery Target prefab (`piece_archery_target`) and Practice Arrow item
(`ArrowPractice`) are authored as single binding points in `PracticeRangeContent`
(cf. `FoundationalPrefabMap`); the concrete ZNetScene prefab/item names are
confirmed against the running build during the joined-client capture.
