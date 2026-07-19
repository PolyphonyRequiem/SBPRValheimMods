---
status: current
---

# Tracer 8 (Warrior) evidence — machine manifest — T031

Author: `engineer-systems` (implementer). Node evidence for **T031 — Weapon
Discipline (skill-cap choice)**. This is the node's own implementation artifact; the
independent joined-client skill-cap capture (DoD item 9) and the Tracer-8 gate
verdict are QA / T032 (non-author).

Acceptance: `AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE`.

| id | claim | artifact |
|----|-------|----------|
| D1 | Authored choice catalog offers ≥2 distinct tiers, each targeting one eligible melee skill (Ready-Hands registry), each cap ≤100 | `tests/NiflheimWeaponDisciplineTests.cs` (`Catalog_offers_at_least_two_distinct_melee_tiers`) |
| D2 | A genuinely purchased character (real develop→offer→purchase pipeline) picks one offered tier; the choice + cap-provider provenance record is committed | `tests/NiflheimWeaponDisciplineTests.cs` (`Purchased_character_chooses_one_offered_tier`) |
| D3 | A choice without a Weapon Discipline purchase rejects `NotPurchased`, zero mutation | `tests/NiflheimWeaponDisciplineTests.cs` (`Choice_without_purchase_rejects_not_purchased`) |
| D4 | An unoffered choice id / stale choice-catalog version rejects `ChoiceNotOffered` | `tests/NiflheimWeaponDisciplineTests.cs` (`Unoffered_choice_id_rejects_choice_not_offered`, `Stale_choice_catalog_version_rejects_choice_not_offered`) |
| D5 | Replay of the same op is idempotent — exactly one choice record despite the replay | `tests/NiflheimWeaponDisciplineTests.cs` (`Choice_replay_is_idempotent_single_record`) |
| D6 | A SECOND distinct choice rejects `AlreadyChosen` (cannot be spent twice); the original choice is intact | `tests/NiflheimWeaponDisciplineTests.cs` (`Second_distinct_choice_rejects_already_chosen_cannot_spend_twice`) |
| D7 | Conflicting op-id reuse (different payload) rejects `OperationConflict`, zero mutation | `tests/NiflheimWeaponDisciplineTests.cs` (`Conflicting_reuse_of_operation_id_rejects`) |
| D8 | A one-choice catalog rejects `CatalogTooSmall`; an authored cap >100 rejects `CapExceedsMax` | `tests/NiflheimWeaponDisciplineTests.cs` (`Too_small_catalog_rejects_catalog_too_small`, `Authored_cap_above_hard_cap_rejects_cap_exceeds_max`) |
| D9 | The selection raises ONLY the chosen skill — every other eligible melee skill stays at baseline (sub-100 baseline probe). Cannot raise every melee cap | `tests/NiflheimWeaponDisciplineTests.cs` (`Choice_raises_only_the_chosen_skill_never_every_cap`) |
| D10 | Highest-wins composition: a lower contributor never lowers the baseline; the highest wins below a sub-100 baseline; a >100 contributor clamps to 100; no contributors → baseline | `tests/NiflheimWeaponDisciplineTests.cs` (`Compose_highest_wins_never_below_baseline_and_clamped_to_100`) |
| D11 | Effective cap never exceeds the hard cap of 100 (values ≤100) | `tests/NiflheimWeaponDisciplineTests.cs` (`Effective_cap_never_exceeds_hard_cap_of_100`) |
| D12 | Permanent Effect: the committed choice survives relationship release — still present, cap still composed | `tests/NiflheimWeaponDisciplineTests.cs` (`Permanent_choice_survives_relationship_loss`) |
| D13 | Save/restart rehydrates the permanent choice from journal truth; replay after boot is pure (no second record) | `tests/NiflheimWeaponDisciplineTests.cs` (`Save_restart_rehydrates_the_permanent_choice_and_replay_is_pure`) |
| D14 | The choice record round-trips byte-stably through the aggregate snapshot codec (state round-trip) | `tests/NiflheimWeaponDisciplineTests.cs` (`State_roundtrip_preserves_the_skill_cap_choice_record`) |
| D15 | Hostile identity claim rejects `PrincipalMismatch`; stale character revision rejects with zero mutation | `tests/NiflheimWeaponDisciplineTests.cs` (`Hostile_identity_claim_rejects`, `Stale_character_revision_rejects_with_zero_mutation`) |
| D16 | Red-first: removing the `EffectiveCap` target-skill filter (so a choice would raise EVERY skill) turned `Choice_raises_only_the_chosen_skill_never_every_cap` RED (`Expected 50, Actual 100`) for the intended reason, then reverted green | `T031-weapon-discipline-implementation.md` §"Tests" |
| D17 | Full suite 1413/1413; both net48 Release builds 0w/0e; docs-lint OK; `git diff --check` clean; `SpecCheck` recipe count unchanged (registers no recipe) | build/test logs (this run) |
| D18 | Engine-free CLEAN slice: no UnityEngine/BepInEx/Harmony/Valheim type in `SkillCapChoices.cs` / `SkillCapProvider.cs` / the choice command; net8 link-compile = real execution. NO joined-client/playable claim — the in-world skill-cap UI/gain/death artifact and the net48 `Skills` runtime seam are deferred (live client present) and re-run at T032 | `T031-weapon-discipline-implementation.md` §"Honesty" |

- [T031-weapon-discipline-implementation.md](T031-weapon-discipline-implementation.md) — full analysis
