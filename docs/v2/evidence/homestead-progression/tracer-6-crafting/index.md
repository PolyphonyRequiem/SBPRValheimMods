---
status: current
---

# Tracer 6 evidence — machine manifest

Per-node artifacts for the Crafting branch. T021 landed the engine-free CLEAN
slice; the joined-client transport proofs and the T024 independent verdict are
appended as their nodes complete.

## T021 — Refined Workshop

Acceptance: `AT-REFINED-REAL-VS-EFFECTIVE`.

| id | claim | artifact |
|----|-------|----------|
| RW1 | Real Level-2 → effective Level 3 for eligible portable production while active | `tests/NiflheimRefinedWorkshopTests.cs::Active_refined_workshop_makes_real_level2_effective_level3_for_portable_production` |
| RW2 | +1 on production/upgrade/repair | `tests/NiflheimRefinedWorkshopTests.cs::Plus_one_applies_to_all_three_eligible_portable_operations` |
| RW3 | No active effect → no +1, real level preserved | `tests/NiflheimRefinedWorkshopTests.cs::Same_real_station_without_active_local_effect_gets_no_bonus` |
| RW4 | Structure/build never boosted | `tests/NiflheimRefinedWorkshopTests.cs::Structure_and_build_operations_never_receive_the_bonus_even_when_active` |
| RW5 | Ineligible item excluded | `tests/NiflheimRefinedWorkshopTests.cs::Ineligible_non_portable_item_gets_no_bonus_even_for_a_production_operation` |
| RW6 | No station conjured at real level 0 | `tests/NiflheimRefinedWorkshopTests.cs::Bonus_never_conjures_a_station_when_no_real_station_present` |
| RW7 | Real level never mutated | `tests/NiflheimRefinedWorkshopTests.cs::Real_level_is_reported_and_never_mutated_across_repeated_resolutions` |
| RW8 | Dormancy: area exit / no Governor / uncommitted Tree / low Stone Level | `tests/NiflheimRefinedWorkshopTests.cs::Bonus_dormant_*` |
| RW9 | Policy exclusion + attuned-guest grant | `tests/NiflheimRefinedWorkshopTests.cs::Attuned_policy_excludes_unrelated_occupant_from_the_bonus_but_keeps_real_level` |
| RW10 | Area rejoin re-derives with zero writes | `tests/NiflheimRefinedWorkshopTests.cs::Rejoining_the_area_re_derives_the_bonus_with_no_writes` |
| RW11 | Unavailable Local Crafting node grants nothing | `tests/NiflheimRefinedWorkshopTests.cs::Unavailable_crafting_local_node_is_never_active_and_never_grants_a_bonus` |

Source: `src/SBPR.Niflheim.HomesteadStones/Adapters/Crafting/EffectiveStationLevelProvider.cs`.

Joined-client effective-Level-3 transport proof: **PENDING** — deferred because a
live Valheim client owned the desktop at implementation time (safety gate). See
`README.md`.
