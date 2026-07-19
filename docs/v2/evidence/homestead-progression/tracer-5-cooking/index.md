---
status: current
---

# Tracer 5 (Cooking) evidence — machine manifest (T016: Savor the Hearth; T017: Field Prep)

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
