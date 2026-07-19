---
status: current
---

# Tracer 5 (Cooking) evidence — machine manifest (T016: Savor the Hearth; T017: Field Prep; T018: Iron Stomach; T019: Swift Preparation)

Node: **T019 [US3][US4]** — Swift Preparation, Cooking node 4 of 4 (the sole
executable Tier-2 node). Acceptance targets: `AT-SWIFT-MENU-ONLY`,
`AT-COOKING-TIER2`, `AT-NO-COOKING-COMPLETION`. Status: **implementation landed
under review** — the pure `MenuCraftDurationProvider` (factor 1/3 after vanilla
Cooking-skill adjustment, eligible menu-crafted food only, no completion) is fully
unit-tested, the Tier-2 prior-Offered-Set / level / Tier-Access gate is closed by
the shipped T013 grammar, and the live `InventoryGui.UpdateRecipe` menu-craft-timer
seam is an installed SBPR Harmony transpiler (`SwiftPreparationCraftTimer`, armed
in `Plugin.cs`); the in-world 1/3-duration last mile is client-only, REASONED
(headless has no local Player). Independent Tracer-5 verdict is T020.

Node: **T018 [US4]** — Iron Stomach, Cooking node 3 of 4. Acceptance target:
`AT-IRON-STOMACH-75`. Status: **implementation landed under review** — the pure
`FoodRefreshThresholdProvider` (durable Permanent-Effect, threshold 0.75,
highest-wins, three-slots/debit preserved) is fully unit-tested, and the live
`Player.CanEat` refresh-threshold seam is an installed SBPR Harmony postfix
(`IronStomachRefreshGate`, armed in `Plugin.cs`); the in-world refresh-at-75%
last mile is client-only, REASONED (headless has no local Player). Independent
Tracer-5 verdict is T020.

Node: **T016 [US4]** — Savor the Hearth, first Cooking vertical slice (node 1 of 4).
Acceptance target: `AT-SAVOR-AREA-EXIT`. Status: **QA PASS (data + delivery-seam
layer verified) at merge head `1019c92`** — the live `Player.UpdateFood` seam is an
installed SBPR Harmony prefix (verified on the booted server); the in-world 0.5x
food-bar last mile remains REASONED (headless has no local Player). Independent
Tracer-5 verdict is T020.

Node: **T017 [US4]** — Field Prep, Cooking node 2 of 4. Acceptance target:
`AT-FIELD-PREP-COOKING-POLICY`. Status: **implementation landed under review + QA
PASS (data + delivery-seam layer verified)** — the live
`Player.RequiredCraftingStation` station-gate seam is an installed SBPR Harmony
postfix (`FieldPrepRecipeGate`, verified on the booted server); the in-world
station-free Boar Jerky / Queen's Jam craft last mile remains REASONED (headless
has no local Player). Independent Tracer-5 verdict is T020.

## T017 Field Prep manifest

| id | claim | artifact |
|----|-------|----------|
| F1 | AT-FIELD-PREP-COOKING-POLICY: active Field Prep exposes EXACTLY the unchanged vanilla Boar Jerky + Queen's Jam recipes through Bushcraft (station-free), preserving inputs/yield/authority AND normal Cooking XP/speed/bonus | `tests/NiflheimFieldPrepTests.cs` — `ActiveEffect_ExposesUnchangedBoarJerkyAndQueensJamThroughBushcraft` |
| F2 | Purchased-but-no-relationship → dormant, exposes nothing | `tests/NiflheimFieldPrepTests.cs` — `PurchasedButNoRelationship_EffectDormant_ExposesNothing` |
| F3 | Relationship-but-no-purchase → nothing (no second ledger) | `tests/NiflheimFieldPrepTests.cs` — `RelationshipButNoPurchase_ExposesNothing` |
| F4 | Undeveloped node even with purchase + relationship → nothing | `tests/NiflheimFieldPrepTests.cs` — `UndevelopedNode_EvenWithPurchaseAndRelationship_ExposesNothing` |
| F5 | A sibling character's active reservation never leaks exposure to the purchased caller (personal per-character effect) | `tests/NiflheimFieldPrepTests.cs` — `SiblingCharacterActive_DoesNotLeakExposureToUnpurchasedCaller` |
| F6 | Relationship loss→restore flips exposure with zero writes (pure re-derivation) | `tests/NiflheimFieldPrepTests.cs` — `RelationshipLossThenRestore_FlipsExposureWithNoWrites` |
| F7 | Exposes ONLY the two Field Prep recipes, never Savor / Wood Arrow / arbitrary items; dormant per-item = false; inert None | `tests/NiflheimFieldPrepTests.cs` — `ExposesOnlyFieldPrepRecipes_NotSavorOrOtherItems`, `ExposedRecipeContent_IsStationFreeUnchangedAndNormalCooking`, `DormantEffect_ExposesRecipeFor_ReturnsFalseEvenForFieldPrepItems`, `NoneCapability_IsInert` |
| F8 | Full suite 1338/1338 (Field Prep subset 10/10, red-first verified); both net48 Release builds 0w/0e (HomesteadStones + Trailborne); docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| F9 | Engine-free CLEAN policy: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in `Adapters/Cooking/CookingCraftPolicy.cs`; net8 link-compile = real execution. NO playable/live-client claim | `src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/CookingCraftPolicy.cs`; `joined-client-t017-field-prep.md` §"What is REASONED" |
| F10 | Live delivery-seam VERIFIED on the booted throwaway server: `FieldPrepRecipeGate.RequiredCraftingStation_Postfix` is an installed SBPR Harmony postfix on `Player.RequiredCraftingStation` (coexists with T021 RefinedWorkshop postfix; fails closed off-host/outside Area/without active purchase). In-world station-free craft last mile is client-only, REASONED | `joined-client-t017-field-prep.md`; `capture/t017-boot-capture.log`; `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/FieldPrepRecipeGate.cs` |

- [joined-client-t017-field-prep.md](joined-client-t017-field-prep.md) — T017 full analysis + live-seam verification
- [capture/t017-boot-capture.log](capture/t017-boot-capture.log) — raw booted-server QADiag-T017 patch-info excerpt

## T018 Iron Stomach manifest

| id | claim | artifact |
|----|-------|----------|
| I1 | AT-IRON-STOMACH-75: a durably-acquired Iron Stomach Permanent Effect raises the food refresh/replacement threshold to 0.75 (refresh at 75% remaining); vanilla baseline is 0.5 | `tests/NiflheimIronStomachTests.cs` — `AcquiredIronStomach_RaisesThresholdTo075`, `WithoutIronStomach_ThresholdIsVanillaBaseline` |
| I2 | Highest applicable provider wins (MAXIMUM composition): 0.5 ⊔ 0.75 = 0.75, a stronger 0.9 baseline is never lowered, no-candidate → safe 0.5 floor | `tests/NiflheimIronStomachTests.cs` — `HighestApplicableProviderWins_IronStomachOverBaseline`, `HighestApplicableProviderWins_NeverLowersAStrongerBaseline`, `Compose_TakesTheMaximumCandidate` |
| I3 | Refresh permitted at exactly 75% remaining (boundary-inclusive) and denied just above; 0.5..0.75 band only Iron Stomach refreshes | `tests/NiflheimIronStomachTests.cs` — `CanRefreshAt75PercentRemaining_OnlyWithIronStomach`, `CanRefreshAt74PercentRemaining_UnderBothThresholds`, `RemainingFractionAtOrBelowThreshold_IsRefreshable_BoundaryInclusive` |
| I4 | Durable Permanent Effect: the raised threshold survives relationship loss, a serialized-restart round-trip, and Tree revocation of development (no relationship/Stone conjunct) | `tests/NiflheimIronStomachTests.cs` — `ThresholdSurvivesRelationshipLoss`, `ThresholdSurvivesRestart_RoundTripsThroughSerializedCharacter`, `ThresholdSurvivesTreeRevocationOfDevelopment` |
| I5 | Keys on the exact Iron Stomach node identity + PermanentEffect outcome class; a same-Stone Field Prep Character-Effect purchase never grants it | `tests/NiflheimIronStomachTests.cs` — `OnlyPermanentEffectPurchaseCounts_NotACharacterEffect` |
| I6 | Threshold provider ONLY: three food slots (== 3) and normal debit/stats/duration preserved untouched; inert None is vanilla baseline | `tests/NiflheimIronStomachTests.cs` — `PreservesThreeSlotsAndNormalDebitStatsDuration`, `NoneCapability_IsVanillaBaselineAndInert` |
| I7 | Full suite 1379/1379 (Iron Stomach subset 14/14, red-first verified via CS0246 type-missing); both net48 Release builds 0w/0e (HomesteadStones + Trailborne); docs-lint OK; `git diff --check` clean; SpecCheck recipe manifest unchanged | build/test logs (this run) |
| I8 | Engine-free CLEAN provider: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in `Adapters/Cooking/FoodRefreshThresholdProvider.cs`; net8 link-compile = real execution. NO playable/live-client claim | `src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/FoodRefreshThresholdProvider.cs`; `joined-client-t018-iron-stomach.md` §"Honest scope" |
| I9 | Live delivery seam armed: `IronStomachRefreshGate.CanEat_Postfix` is an installed SBPR Harmony postfix on `Player.CanEat` (armed in `Plugin.cs`; rescues only the same-food 0.5..0.75 refresh band, never the three-slot cap; fails closed off-host / without a durable purchase). In-world refresh-at-75% last mile is client-only, REASONED | `joined-client-t018-iron-stomach.md`; `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/IronStomachRefreshGate.cs` |

- [joined-client-t018-iron-stomach.md](joined-client-t018-iron-stomach.md) — T018 full analysis + delivery-seam wiring

## T019 Swift Preparation manifest

| id | claim | artifact |
|----|-------|----------|
| W1 | AT-SWIFT-MENU-ONLY: an active Swift Preparation multiplies the vanilla skill-adjusted menu-craft duration of eligible menu-crafted food by 1/3, applied AFTER the skill adjustment | `tests/NiflheimSwiftPreparationTests.cs` — `ActiveEffect_ShortensEligibleMenuCraftedFood_ToOneThirdOfSkillAdjustedDuration`, `FactorAppliesAfterSkill_NotBefore` |
| W2 | AT-SWIFT-MENU-ONLY (exclusions): non-food, non-Cooking-station, and non-menu-craft outputs keep the full vanilla duration; the eligibility predicate gates on food + Cooking skill + menu path | `tests/NiflheimSwiftPreparationTests.cs` — `ActiveEffect_DoesNotShortenIneligibleCraft`, `EligibilityPredicate_OnlyFoodViaCookingMenuStation` |
| W3 | Dormant/unpurchased/undeveloped effect and a sibling character's active relationship leave the duration unshortened; relationship loss→restore flips shortening with zero writes | `tests/NiflheimSwiftPreparationTests.cs` — `PurchasedButNoRelationship_EffectDormant_KeepsFullDuration`, `RelationshipButNoPurchase_KeepsFullDuration`, `UndevelopedNode_EvenWithPurchaseAndRelationship_KeepsFullDuration`, `SiblingCharacterActive_DoesNotLeakShorteningToUnpurchasedCaller`, `RelationshipLossThenRestore_FlipsShorteningWithNoWrites` |
| W4 | AT-NO-COOKING-COMPLETION: a positive base is strictly shorter but never zero/instant-complete; a non-positive base returns unchanged (never fabricates a craft); view/bit overloads agree | `tests/NiflheimSwiftPreparationTests.cs` — `ShortenedDuration_NeverReachesZeroOrInstantCompletion_ForPositiveBase`, `NonPositiveBase_ReturnedUnchanged_NeverFabricatesACraft`, `FactorOverload_MatchesViewOverload` |
| W5 | AT-COOKING-TIER2: Swift Preparation is the sole executable Level-2 Cooking node with prior-Offered set Field Prep + Iron Stomach; one prior short → PriorOfferedSetIncomplete, both → purchasable; below Level 2 → level-cap reject; derived same-Tree Tier Access reaches 2 only with both priors | `tests/NiflheimSwiftPreparationTests.cs` — `SwiftPreparation_IneligibleWithOnlyOnePriorNode_PriorOfferedSetIncomplete`, `SwiftPreparation_EligibleWhenBothPriorsAcquiredAndLevel2_Purchasable`, `SwiftPreparation_RejectedBelowLevel2_EvenWithBothPriors`, `DerivedSameTreeTierAccess_Reaches2_OnlyWhenBothPriorsAcquired`, `SwiftPreparationNode_IsSoleExecutableLevel2CookingCharacterEffect` |
| W6 | Full suite 1476/1476 (Swift Preparation subset 21/21, red-first verified via CS0246 type-missing); both net48 Release builds 0w/0e (HomesteadStones + Trailborne); docs-lint OK; `git diff --check` clean; SpecCheck recipe manifest unchanged | build/test logs (this run) |
| W7 | Engine-free CLEAN provider: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in `Adapters/Cooking/MenuCraftDurationProvider.cs`; net8 link-compile = real execution. NO playable/live-client claim | `src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/MenuCraftDurationProvider.cs`; `joined-client-t019-swift-preparation.md` §"Honest scope" |
| W8 | Live delivery seam armed: `SwiftPreparationCraftTimer.UpdateRecipe_Transpiler` is an installed SBPR Harmony transpiler on `InventoryGui.UpdateRecipe` (armed in `Plugin.cs`; scales the num5 menu-craft-duration local in place at the GuiBar.SetMaxValue site, strictly after the vanilla skill line; ineligible/dormant → full duration; fails closed off-host). In-world 1/3-duration last mile is client-only, REASONED | `joined-client-t019-swift-preparation.md`; `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SwiftPreparationCraftTimer.cs` |

- [joined-client-t019-swift-preparation.md](joined-client-t019-swift-preparation.md) — T019 full analysis + Tier-2 gate + live-seam wiring


## T016 Savor the Hearth manifest

| id | claim | artifact |
|----|-------|----------|
| S1 | Eligible occupant inside the Stone Area drains active food timers at factor 0.5 | `tests/NiflheimSavorTheHearthTests.cs` — `Eligible_occupant_inside_area_drains_at_half_factor` |
| S2 | AT-SAVOR-AREA-EXIT: Area exit restores factor 1.0 immediately (stateless re-derive, zero writes) | `tests/NiflheimSavorTheHearthTests.cs` — `Stepping_outside_area_restores_full_factor_immediately` |
| S3 | Settlement-policy loss restores factor 1.0 even inside the Area | `tests/NiflheimSavorTheHearthTests.cs` — `Policy_loss_restores_full_factor_even_inside_area` |
| S4 | Governance dormancy (no authorized Governor) restores factor 1.0 | `tests/NiflheimSavorTheHearthTests.cs` — `Governance_dormancy_restores_full_factor` |
| S5 | Undeveloped Savor never slows | `tests/NiflheimSavorTheHearthTests.cs` — `Undeveloped_savor_never_slows` |
| S6 | No retroactive duration: `ConsumeElapsed` scales only the current slice; exit never refunds/claws back; aggregate untouched | `tests/NiflheimSavorTheHearthTests.cs` — `Consume_elapsed_scales_only_the_current_slice`, `Exit_does_not_retroactively_refund_previously_slowed_time`, `Non_positive_elapsed_consumes_nothing` |
| S7 | Provider is stateless across interleaved evaluations; attuned guest admitted when relationship + policy allow | `tests/NiflheimSavorTheHearthTests.cs` — `Provider_is_stateless_across_repeated_evaluations`, `Guest_gains_slow_when_relationship_and_policy_admit` |
| S8 | Full suite 1205/1205 (Savor subset 10/10); both net48 Release builds 0w/0e (HomesteadStones + Trailborne); docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| S9 | Engine-free CLEAN slice: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in `Adapters/Cooking/CookingProviders.cs`; net8 link-compile = real execution. NO playable/live-client claim | `src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/CookingProviders.cs`; README §"Joined-client in-area/exit artifact — BLOCKED" |
| S10 | Joined-client in-area/exit in-world proof (net48 Harmony food-timer seam on a QA client) — live seam WIRED by remediation t_803e92f6, rebased onto the merged shared Local Effect runtime PR #368 (`Player.UpdateFood` prefix consuming the authoritative `LocalActivationSnapshot` + engine-free projection `SavorFoodDrainResolver` + playtest establishment seam driving the shared `LocalNodeProvisioningDriver`'s accepted commands); in-world 0.5/1.0 artifact remains QA's to capture via the documented operator steps | `live-seam-wired-t_803e92f6.md`; `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SavorFoodTimerObserver.cs`; `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/SavorFoodDrainResolver.cs`; `tests/NiflheimSavorLiveSeamTests.cs` |

- [README.md](README.md) — full analysis, provider description, and the wired joined-client seam
- [joined-client-PASS-t_8b6e9e60.md](joined-client-PASS-t_8b6e9e60.md) — QA PASS: live seam verified installed on the booted server + mechanical merge onto fresh main
- [live-seam-wired-t_803e92f6.md](live-seam-wired-t_803e92f6.md) — remediation: the live food-timer delivery seam + QA operator steps
- [joined-client-FAIL-t_0fb85725.md](joined-client-FAIL-t_0fb85725.md) — superseded FAIL evidence (live path absent, pre-remediation)
