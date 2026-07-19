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
| W12 | **Remediation** — the pure provider is now WIRED: `WarriorLocalPlacementGate` (the missing runtime caller) + a provisional server-owned Stone-state source + net48 listen-host observer + dedicated ingress, armed in `Plugin.cs`; refused placements are undone (destroyed) | `T029-remediation-runtime-wiring.md`; `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/WarriorLocalPlacementGate.cs`, `Features/Progression/WarriorTwigPlacementObserver.cs` |
| W13 | Runtime wiring exercised against the composed `FoundationalProgressionServer` (real production wiring): admit / no-permission-undo / outside-policy-undo / unbound-fail-closed / outside-area / non-twig-decline / governance-dormancy / dedicated creator-mismatch / race-safe pump | `tests/NiflheimWarriorTwigRuntimeGateTests.cs` (18 tests) |
| W14 | Full suite 1224/1224; both net48 Release builds 0w/0e; docs-lint OK 185; `git diff --check` clean; SpecCheck recipe count unchanged | build/test logs (remediation run) |
| Q1 | **QA joined-client PASS (non-author, `t_a811a842`)** — the wired seam binds live: `warriorTwigArmed=True` on a server-authoritative isolated-server boot, 0 `Failed to patch` / 0 SBPR exceptions from the live-boot line onward, drift watchdog green, SpecCheck 31; full admit/refuse/undo matrix executes against the real authoritative runtime (30/30 `~WarriorTwig`, suite 1328/1328, both net48 0w/0e) at reconciled head `84c51ad` (fresh-`origin/main` merged) | `QA-joined-client-T029-PASS.md` |

- [QA-joined-client-T029-PASS.md](QA-joined-client-T029-PASS.md) — independent QA verdict (PASS)

- [README.md](README.md) — full analysis
