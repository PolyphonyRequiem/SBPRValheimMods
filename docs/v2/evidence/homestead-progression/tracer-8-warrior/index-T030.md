---
status: current
---

# Tracer 8 (Warrior) evidence — machine manifest — T030

Author: `engineer-systems` (implementer). Node evidence for **T030 — Ready Hands
(equip/unequip durations)**. This is the node's own implementation artifact; the
independent joined-client timing capture (DoD item 9) and the Tracer-8 gate
verdict are QA / T032 (non-author).

Acceptance: `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS`.

| id | claim | artifact |
|----|-------|----------|
| R1 | Ready Hands active is derived from the shipped T004 `DerivedActivationView` (`ReadyHands@1` purchased AND active relationship); no second ledger | `tests/NiflheimReadyHandsTests.cs` (`Active_effect_is_derived_from_purchase_plus_relationship`) |
| R2 | BOTH halves shortened identically for every eligible melee skill (`2.0s → 1.0s`, factor 0.5) | `tests/NiflheimReadyHandsTests.cs` (`Both_equip_and_unequip_are_shortened_identically_for_eligible_melee`) |
| R3 | Relationship loss / dormancy restores the full vanilla duration immediately on both halves, zero writes | `tests/NiflheimReadyHandsTests.cs` (`Relationship_loss_restores_full_duration_immediately_both_halves`) |
| R4 | A non-buyer never shortens even with an active relationship | `tests/NiflheimReadyHandsTests.cs` (`Non_buyer_never_shortens_even_with_active_relationship`) |
| R5 | Eligible registry is exactly the six melee weapon skills; all excluded classes rejected | `tests/NiflheimReadyHandsTests.cs` (`Registry_membership_is_exactly_the_six_melee_weapon_skills`) |
| R6 | Every excluded class (armor/None, shields, bows, crossbows, magic, unarmed, tools) untouched when active | `tests/NiflheimReadyHandsTests.cs` (`Excluded_classes_are_never_shortened_even_when_active`) |
| R7 | Reload action never shortened (crossbow reload and spurious melee reload) — reload is built from `GetWeaponLoadingTime()`, not `m_equipDuration` | `tests/NiflheimReadyHandsTests.cs` (`Reload_action_is_never_shortened_even_for_a_crossbow_or_melee`) |
| R8 | No shared-prefab mutation: only the per-action `MinorActionData.m_duration` copy is scaled; resolving mutates no persisted state | `tests/NiflheimReadyHandsTests.cs` (`Deriving_and_resolving_mutates_no_persisted_state`); `src/SBPR.Niflheim.HomesteadStones/Features/Warrior/ReadyHandsEquipDurationPatch.cs` |
| R9 | Runtime seam postfixes `Humanoid.QueueEquipAction`/`QueueUnequipAction` (decomp :22237/:22262) and scales the just-appended copy; activation fail-closed (host derive / client personal cache) | `src/SBPR.Niflheim.HomesteadStones/Features/Warrior/ReadyHandsEquipDurationPatch.cs` |
| R10 | Full suite 1365/1365; both net48 Release builds 0w/0e; docs-lint OK 202; `git diff --check` clean; SpecCheck recipe count unchanged | build/test logs (this run) |
| R11 | Engine-free CLEAN provider: no UnityEngine/BepInEx/Harmony/Valheim type in `EquipDurationProvider.cs`; net8 link-compile = real execution. NO joined-client/playable claim — the in-world equip/unequip timing artifact is deferred and re-run at T032 | `T030-ready-hands-implementation.md` §"Honesty" |

- [T030-ready-hands-implementation.md](T030-ready-hands-implementation.md) — full analysis
