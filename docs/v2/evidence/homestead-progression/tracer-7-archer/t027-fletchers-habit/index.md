---
status: current
---

# T027 Fletcher's Habit — machine manifest

Node: Archer / Fletcher's Habit (Permanent Effect, personal Offered, executable).
Acceptance: `AT-FLETCHER-HIT-LIFECYCLE`, `AT-FLETCHER-NO-DUP`. First personal
Permanent Effect: ownership is durable (developed + purchased), relationship-
independent (spec line 130 / line 260). Engine-free vertical + host/pure-client
runtime seam shipped green. Joined-client in-world recovery pending T028. Logs-green
is never playability.

| id | claim | artifact |
|----|-------|----------|
| T1 | Durable ownership: purchased persists through relationship loss (owned), sibling-held relationship (owned), unpurchased (not owned), undeveloped (not owned) — via `OwnsFletchersHabit` over `DerivedActivationView` (developed + purchased, NOT active-relationship-gated) | `tests/NiflheimFletchersHabitTests.cs` (PurchasedWithActiveRelationship_IsOwned, PurchasedButRelationshipLost_StillOwned_PermanentEffectPersists, SiblingHoldsRelationship_PurchasedCallerStillOwned, NotPurchased_IsNotOwned, UndevelopedNode_EvenPurchased_IsNotOwned) |
| T2 | One authoritative result per surface: recoverable (solid/ground/creature/shield-blocked) rolls the one configured chance and recovers exactly one EXACT instance on a pass; a high roll recovers nothing | `tests/NiflheimFletchersHabitTests.cs` (OwnedEligibleArrow_HitRecoverableSurface_LowRoll_Recovers_ExactInstance, …_HighRoll_DoesNotRecover, OwnedEligibleArrow_ShieldBlocked_IsRecoverable) |
| T3 | Non-recoverable surfaces (water, miss/TTL) are definitively lost — the roll does not run even at roll 0 | `tests/NiflheimFletchersHabitTests.cs` (OwnedEligibleArrow_NonRecoverableSurface_NeverRecovers_NoRollEvenAtZero) |
| T4 | Not owned / ineligible arrow ⇒ vanilla behaviour, nothing recovered | `tests/NiflheimFletchersHabitTests.cs` (NotOwned_EligibleArrow_YieldsVanillaBehaviour_NoRecovery, Owned_IneligibleArrow_YieldsVanillaBehaviour_NoRecovery) |
| T5 | Target-return exclusion: the deterministic Practice Range return (T025) suppresses the roll entirely (no recovered arrow) even with full ownership + roll 0 | `tests/NiflheimFletchersHabitTests.cs` (TargetReturnWon_SuppressesTheRoll_NoRecoveredArrow, TargetReturnDecision_FromPracticeRange_IntegratesAsSuppression) |
| T6 | No duplication: the same fired instance resolves at most once (`AlreadyResolved`); a multishot volley resolves each instance independently (2 recovered of 3 in the mixed volley) | `tests/NiflheimFletchersHabitTests.cs` (SameArrowInstance_ResolvedTwice_RecoversAtMostOnce, MultishotVolley_ResolvesEachInstanceIndependently_NoCrossInstanceDup) |
| T7 | Exact-instance provenance: the recovered arrow preserves item id, quality, variant, durability, crafter, custom data field-by-field (no substitution); half-open `roll < chance` boundary | `tests/NiflheimFletchersHabitTests.cs` (RecoveredArrow_PreservesExactProvenance_NotASubstitute, ConfiguredChance_IsBoundaryInclusiveAtRollBelowChance_ExclusiveAtOrAbove) |
| T8 | Permanent-vs-Character carried on the delivery wire: a relationship-dormant snapshot reports `IsOwned == true`, `IsActive == false`; client cache `IsOwnedForStone` is durable + fails closed | `tests/NiflheimFletchersHabitTests.cs` (Snapshot_PurchasedButRelationshipInactive_IsOwned_ButNotActive, ClientCache_IsOwnedForStone_ReadsDurableOwnership_RelationshipIndependent, DeniedSnapshot_OwnsNothing_FailClosed, ClientCache_IsOwnedForStone_FailsClosed_WhenNoSnapshotHeld) |
| T9 | Full suite 1390/1390 (+25 this node); both net48 Release builds 0w/0e; docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| T10 | Runtime seam: `Projectile.Setup` captures exact consumed Wood Arrow provenance for local shots; `Projectile.OnHit` classifies the surface, resolves durable ownership (host composed stores / pure-client `PersonalActivationSnapshot.IsOwned`), makes the one authoritative decision with a single trusted RNG draw, and drops the exact `ItemData` once via `ItemDrop.DropItem` (additive, ADR-0006); per-ZDOID session enforces no duplication; ArcheryTarget surface suppresses via target-return | `src/SBPR.Niflheim.HomesteadStones/Features/Archer/ProjectileRecoveryGate.cs` |
| T11 | Engine-free provider + session: no UnityEngine/BepInEx/Valheim type in tested source; net8 link-compile = real execution | `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/ProjectileRecoveryProvider.cs`, `ProjectileRecoverySession.cs` |
| T12 | No new SBPR recipe/buildable (patches vanilla arrow recovery), so the SpecCheck recipe manifest count is unchanged | `src/SBPR.Trailborne/Runtime/SpecCheck.cs` (unchanged) |

- [README.md](README.md) — full node writeup
