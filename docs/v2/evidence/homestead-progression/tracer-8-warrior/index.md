---
status: current
---

# Tracer 8 (Warrior) evidence — machine manifest — T029

Author: `engineer-systems` (implementer). Node evidence for **T029 — T.W.I.G.
Training Local placement**. This is the node's own implementation artifact; the
independent Tracer-8 gate verdict is T032 (non-author).

| id | claim | artifact |
|----|-------|----------|
| W1 | Provider binds the exact vanilla T.W.I.G. prefab (`TrainingDummy`) and the authored `TwigTraining@1` Stone-cultivated Warrior Local node | `tests/NiflheimWarriorTwigPlacementTests.cs:109` |
| W2 | Active + policy-eligible + build-permitted occupant may place the exact T.W.I.G. | `tests/NiflheimWarriorTwigPlacementTests.cs:125` |
| W3 | Node authorizes ONLY the exact T.W.I.G. — any other/renamed/case-mismatched prefab rejects `NotTwigPiece` | `tests/NiflheimWarriorTwigPlacementTests.cs:137` |
| W4 | Load-bearing AND: placement requires policy eligibility AND ordinary build Permission (both directions) | `tests/NiflheimWarriorTwigPlacementTests.cs:155` |
| W5 | `Admit` for the exact piece equals shared `CanExercisePlacement` across owner/guest/stranger × area/governor/permission | `tests/NiflheimWarriorTwigPlacementTests.cs:177` |
| W6 | Missing authorized Governor / uncommitted Warrior Tree / Active Stone Level below node level / outside Stone Area suppress placement; development retained | `tests/NiflheimWarriorTwigPlacementTests.cs:204,216,226,235` |
| W7 | Relationship release→rejoin re-derives capability from the same persisted Stone with zero writes; no active-effects ledger | `tests/NiflheimWarriorTwigPlacementTests.cs:244` |
| W8 | No cross-Tree overlap: the T.W.I.G. provider does not authorize another Tree's Local prefab | `tests/NiflheimWarriorTwigPlacementTests.cs:267` |
| W9 | Red-first: disabling the build-Permission conjunct turned the Permission-AND tests RED (2 failures) for the intended reason, then reverted green | README §"Automated proof" |
| W10 | Full suite 1206/1206; both net48 Release builds 0w/0e; docs-lint OK 181; `git diff --check` clean; `SpecCheck` recipe count unchanged | build/test logs (this run) |
| W11 | Engine-free CLEAN slice: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in the provider; net8 link-compile = real execution. NO joined-client/playable claim — the in-world T.W.I.G. placement artifact is deferred (live client present) and re-run at T032 | README §"Logs-green ≠ playable" |

- [README.md](README.md) — full analysis
