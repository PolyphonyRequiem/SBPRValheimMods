---
status: current
---

# T025 Practice Range — machine manifest

Node: Archer / Practice Range (Local Effect, executable). Joined-client proof
PENDING owner clearance (live `valheim.x86_64` running at implementation time;
safety gate preserves the occupied client). Engine-free vertical shipped green.

| id | claim | artifact |
|----|-------|----------|
| T1 | Capability = active Practice Range Local Effect AND ordinary build Permission; policy-alone or Permission-alone unlocks neither | `tests/NiflheimPracticeRangeTests.cs` (Active_local_and_build_permission…, Policy_eligible_but_no_build_permission…, Build_permitted_but_outside_policy…) |
| T2 | Dormancy suppresses the capability: outside Stone Area / missing authorized Governor / Archer Tree uncommitted / Active Stone Level below node level / undeveloped node | `tests/NiflheimPracticeRangeTests.cs` (Outside_stone_area…, Missing_authorized_governor…, Archer_tree_not_committed…, Active_stone_level_below…, Undeveloped_practice_range…) |
| T3 | Exposes exact vanilla Archery Target prefab + Practice Arrow recipe (100 for 8 Wood) | `tests/NiflheimPracticeRangeTests.cs` (Capability_exposes_exact_vanilla…, Practice_arrow_recipe_is_exactly_100_for_8_wood) |
| T4 | Practice Arrow ammo damage 0; fired shot retains bow draw damage (effective == bow damage) | `tests/NiflheimPracticeRangeTests.cs` (Practice_arrow_ammo_damage_is_zero, Fired_practice_arrow_retains_bow_damage…) |
| T5 | Archery Target terminal impact deterministically returns exactly one arrow, wins, stable across evaluations | `tests/NiflheimPracticeRangeTests.cs` (Practice_arrow_impacting_archery_target…, Target_return_is_stable…) |
| T6 | Non-target terminal impact (Ground/Water/Creature/LostOrExpired) returns nothing and does not win — yields to Fletcher's Habit roll (T027) | `tests/NiflheimPracticeRangeTests.cs` (Non_target_terminal_impact…) |
| T7 | Full suite 1216/1216 (+21 this node); both net48 Release builds 0w/0e; docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| T8 | Engine-free: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in tested source; net8 link-compile = real execution. NO playable/live-client claim — logs-green is never playability | `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/PracticeRangeProvider.cs` |

- [README.md](README.md) — full node writeup
