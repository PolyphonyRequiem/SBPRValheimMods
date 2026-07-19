---
status: current
---

# Tracer 8 — Warrior branch evidence — T029 T.W.I.G. Training placement

Author: `engineer-systems` (implementer of T029). This is the T029 node's OWN
implementation evidence, not the independent Tracer-8 gate verdict — that is
T032 (`AT-WARRIOR-UNAVAILABLE` and the branch re-run), performed by a non-author.

## What T029 delivers

`Adapters/Warrior/LocalPlacementProvider.cs` — the T.W.I.G. Training Local
placement capability. It binds the single authored Warrior `TwigTraining@1`
Stone-cultivated Local node to the **exact unchanged vanilla T.W.I.G.** build
piece (Valheim internal id `TrainingDummy`; the fandom wiki entry "T.W.I.G.").
The node's only effect is to expose that one piece as placeable; it alters no
recipe, durability, resistance, or behaviour of the vanilla piece.

The provider adds nothing to the shared T014/T015 grammar. Its admit decision for
the exact piece is identical to `LocalEffectActivationView.CanExercisePlacement`:

- the effect must be **currently active** for the occupant — developed, an
  authorized Governor present, the owning Warrior Tree committed, Active Stone
  Level at or above the node level, the occupant inside the Stone Area, and the
  occupant a beneficiary under the single Settlement Local policy
  (Everyone / Attuned / Private), **AND**
- the occupant must **independently** hold ordinary build Permission.

This is the load-bearing AND (spec FR-016 final sentence): neither the
relationship nor the Local policy silently grants a build ACL. Stable machine
outcomes — `Admitted`, `NotTwigPiece`, `EffectNotActive`, `MissingBuildPermission`
— report which conjunct failed. The requested prefab is a server-observed fact,
never a client eligibility claim.

The node never widens into a general build grant and never overlaps another
Tree's Local node: any prefab other than the exact authored T.W.I.G. rejects
`NotTwigPiece`, including a case-mismatched `trainingdummy`, `wood_floor`,
`piece_workbench`, and a cooking-station piece.

The provider holds **no active-effects ledger** of its own
(`AT-NO-ACTIVE-LEDGER`): relationship release/rejoin, a missing authorized
Governor, an uncommitted Warrior Tree, Active Stone Level below the node level,
and leaving the Stone Area each suppress the capability while retaining developed
state, re-deriving from the same persisted Stone with zero writes.

## Automated proof (this run)

- New tests: `tests/NiflheimWarriorTwigPlacementTests.cs` — 11 tests, mirroring
  the T014 dormancy harness so both slices exercise the same shared grammar.
  They cover: the exact prefab/node binding; active + permitted admit;
  other-piece rejection (5 prefabs); the Permission AND both ways; Attuned and
  Private policy eligibility; an exhaustive parity check that `Admit` equals the
  shared `CanExercisePlacement` across owner/guest/stranger × area/governor/
  permission; Governor / Tree / level / area dormancy; release→rejoin
  re-derivation; and no cross-Tree overlap.
- Red-first: a `hasOrdinaryBuildPermission` mutation probe (disabling the build
  Permission conjunct) was confirmed to turn the Permission-AND tests RED (2
  failures) for the intended reason, then reverted to green.
- Full suite: **1206 / 1206** passing.
- Both net48 Release builds: **0 warnings / 0 errors**
  (`SBPR.Niflheim.HomesteadStones` and `SBPR.Trailborne`).
- `python3 scripts/docs-lint.py`: OK (181 docs).
- `git diff --check`: clean.
- `SpecCheck.cs` recipe manifest: **unchanged** — T029 registers no SBPR
  recipe or buildable (T.W.I.G. is the vanilla piece exposed under policy).

## Logs-green ≠ playable — joined-client artifact

This slice is engine-free (net8 link-compile of the pure provider is real
execution of the exact shipped code, but proves shape, never in-world
playability). The joined-client in-world proof — a client actually placing the
T.W.I.G. inside a Stone-Level-2 Homestead under an active Settlement policy with
build Permission, and being correctly refused when outside policy or without
Permission — is **not yet captured**.

At implementation time a live `valheim.x86_64` client (Daniel's) was running on
the host. Per the T029 safety gate, no QA client was launched. The joined-client
placement artifact is deferred until the client is clear; it is a required part
of T029's definition of done (item 9) and is independently re-run at T032.
