---
status: current
---

# Tracer 6 — Crafting branch evidence (T021 Refined Workshop)

This folder collects the per-node proof artifacts for the Crafting branch
(Tracer 6): T021 Refined Workshop, T022 Masterwork, T023 Built to Last, and the
T024 independent verification of the branch plus the unavailable Measured Cuts /
Artisan's Counter nodes.

## T021 — Refined Workshop (real-versus-effective station level)

Acceptance: `AT-REFINED-REAL-VS-EFFECTIVE`.

### Engine-free CLEAN-side proof (landed)

The Refined Workshop policy ships as a pure, engine-free adapter,
`Adapters/Crafting/EffectiveStationLevelProvider.cs`, composed over the already
accepted `LocalEffectActivationView` shared grammar. It grants a **+1 effective**
station level only when the Refined Workshop Local Effect is currently **active**
for the occupant (developed Stone state + committed Crafting Tree + Active Stone
Level ≥ node level + an authorized Governor present + inside the Stone Area +
Settlement Local policy eligibility) **and** the operation is one of the three
eligible portable-item kinds (production / upgrade / repair) on an eligible
portable item **and** a real station is present (real level ≥ 1).

Proven by `tests/NiflheimRefinedWorkshopTests.cs` (18 tests, all green):

| # | Claim | Test |
|---|-------|------|
| 1 | Real Level-2 station → effective Level 3 for portable production while active | `Active_refined_workshop_makes_real_level2_effective_level3_for_portable_production` |
| 2 | +1 applies to all three eligible portable operations | `Plus_one_applies_to_all_three_eligible_portable_operations` (Theory ×3) |
| 3 | Same real station WITHOUT the active effect gets no +1 | `Same_real_station_without_active_local_effect_gets_no_bonus` |
| 4 | Structure production / build placement never receive the +1 | `Structure_and_build_operations_never_receive_the_bonus_even_when_active` (Theory ×2) |
| 5 | Ineligible / non-portable item gets no +1 | `Ineligible_non_portable_item_gets_no_bonus_even_for_a_production_operation` |
| 6 | The +1 never conjures a station (real level 0 stays 0) | `Bonus_never_conjures_a_station_when_no_real_station_present` |
| 7 | Real observed level is reported and never mutated | `Real_level_is_reported_and_never_mutated_across_repeated_resolutions` |
| 8 | Bonus dormant on area exit | `Bonus_dormant_when_occupant_exits_the_stone_area` |
| 9 | Bonus dormant with no authorized Governor | `Bonus_dormant_when_no_authorized_governor_present` |
| 10 | Bonus dormant when Crafting Tree not committed | `Bonus_dormant_when_crafting_tree_not_committed` |
| 11 | Bonus dormant when Active Stone Level below node level | `Bonus_dormant_when_active_stone_level_below_node_level` |
| 12 | Attuned policy excludes an unrelated occupant, keeps real level; attuned guest gets +1 | `Attuned_policy_excludes_unrelated_occupant_from_the_bonus_but_keeps_real_level` |
| 13 | Rejoining the area re-derives the bonus with zero writes | `Rejoining_the_area_re_derives_the_bonus_with_no_writes` |
| 14 | Authored-but-unavailable Local Crafting node grants nothing | `Unavailable_crafting_local_node_is_never_active_and_never_grants_a_bonus` |
| 15 | Portable-operation classifier | `Is_portable_operation_classifies_only_the_three_portable_kinds` |

Red-first was observed for the intended reason: with the `+1` logic stubbed out
(effective == real, `bonusApplied = false`), the 7 bonus-asserting tests failed
(`Expected: 3 / Actual: 2`) while the exclusion/dormancy tests already passed;
restoring the real derivation made the suite 18/18 green.

### Joined-client effective-Level-3 transport proof — remediated ingress

**Logs-green is NEVER playable.** The T021 joined-client rerun
(`T021-JOINED-CLIENT-RERUN-FAIL.md`, retained here as the decision-grade FAIL
record) proved that even with the provider correctly wired into the vanilla gate/
UI, the Refined Workshop Local Effect could never reach `Active` at runtime: the
accepted develop/purchase handlers had **zero runtime callers**, so the node was
permanently Undeveloped and the +1 inert end-to-end.

That gap is now closed — see `T021-REMEDIATION-2-PROVISIONING-RUNTIME.md`. A gated
isolated-QA ingress (`LocalProvisioningIngress` + the net48 `sbpr_develop` admin
seam) develops the Refined Workshop Local node through the accepted, receipt-backed
Facet-commit / node-development handlers, so the Local Effect can derive `Active`
for an eligible occupant. The develop path, restart rehydration, idempotent replay,
and fail-closed hostile/unauthorized/purchase-authority cases are proven by
`tests/NiflheimLocalProvisioningIngressTests.cs` (10 tests). The in-world joined-
client effective-Level-3 frame was re-run downstream by QA `t_87538341` (superseding
the trapped `t_8261a415`) against the merged head `d5256ac`: it is a durable
data-layer **PASS** — the shipped ingress develops the node from an empty store,
writes the real Facet/Development journals, derives `Active`, yields effective
Level 3 at a real Level-2 station for all three portable operations, fails closed on
every negative case, and rehydrates the developed state across a restart. See
`T021-JOINED-CLIENT-RERUN-PASS.md`; the GPU pixel last mile is reasoned (headless
box, no user client present), not claimed observed.

## T022 — Masterwork ownership provisioning (R4)

Acceptance context: `AT-MASTERWORK-ISSUE` (issuance was accepted; the four-AT
joined-client run needed a reachable ACTIVE purchased Masterwork).

**Same class of gap as T021.** The Masterwork issuance provider + delivery were
correct, but `IsMasterworkActive` requires a personal **purchase record** for
`Masterwork@1` — and at PR #392 head `LocalProvisioningIngress.PurchaseNode` had
**zero runtime callers** while the Local develop seam only develops Stone-cultivated
nodes, so no joined principal could acquire an owned Masterwork and the four-AT run
was structurally unreachable. Closed by a gated isolated-QA ownership seam through
the SAME accepted handlers: `ProvisionOffered` (develop+offer the personal node),
`OfferMasterwork` / `BuyMasterwork` / `OwnMasterwork` on `LocalProvisioningIngress`,
and the net48 `sbpr_master offer|buy` admin seam
(`Crafting.EnableAdminMasterworkOwnershipProvisioning`, default OFF). offer/buy are
split because the reservation model gives one character only one active relationship
per Stone (develop needs a Bond, purchase needs an Attunement). Proven by
`tests/NiflheimLocalProvisioningIngressTests.cs` (+7 tests, incl. the offer→buy→active
positive path via the exact `IsMasterworkActive` gate). Full detail:
`T022-REMEDIATION-R4-OWNERSHIP-PROVISIONING.md`. The genuine two-client four-AT rerun
is QA `t_4f181af7`.
