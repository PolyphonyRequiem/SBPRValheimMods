---
title: "Homestead Stone progression S2 — implementation task decomposition"
status: accepted
purpose: Dependency-ordered, test-first implementation tasks for the accepted Homestead progression technical proof; this artifact does not authorize implementation.
---

# Tasks: Homestead Stone progression S2

**Input:**
[`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md),
[`homestead-stone-progression-research.md`](homestead-stone-progression-research.md),
[`homestead-stone-progression-data-model.md`](homestead-stone-progression-data-model.md),
[`homestead-stone-progression-contracts.md`](homestead-stone-progression-contracts.md), and
[`homestead-stone-progression-plan.md`](homestead-stone-progression-plan.md).

**Stage:** Daniel separately authorized **tasks** authoring after accepting the S2 package, then accepted the
decomposition by merging PR #297 after its fresh-context audit passed with no blockers. Implementation remains a
separate gate. The checklist is a map of future work, not permission to execute it.

**Organization:** Tasks follow the plan's blocking Gate A and Tracers 1–9. Each implementation task is a
vertical slice: write a failing test, cross the domain/application/persistence-or-adapter seam needed for one
observable result, update the accepted package and runtime conformance surface in the same PR, then produce the
named evidence. Tasks marked **[P]** may proceed in parallel only after their shared predecessor is accepted.

## Format

`[ID] [P?] [US?] Description`

- **[P]** — different feature files and no unmet dependency; merge conflict risk in shared registries/docs still
  requires coordination.
- **[US1]…[US5]** — traceability to the feature specification's prioritized user stories.
- Every runtime task includes tests and docs. A task is not complete when only its code or only its tests exist.
- Every verifier task must be performed by an agent other than the task's implementer.
- Every new evidence folder carries both `README.md` human orientation and `index.md` machine manifest; T003
  creates the shared parents, and later verifier tasks add their own leaf pairs.

## Execution gates

1. **No implementation authorization:** do not start any checkbox below until Daniel separately authorizes the
   implementation stage.
2. **Current-main prerequisite:** at decomposition time, current `main` contains this accepted package but does
   not contain `src/SBPR.Niflheim.HomesteadStones/`. The separately owned v1 placement/identity runtime described
   by [`homestead-stone-v1-impl-spec.md`](homestead-stone-v1-impl-spec.md) must first land on the chosen
   implementation base. Do not silently recreate or redesign that substrate inside S2 progression work.
3. **Gate A blocks everything:** no Tree, node, relationship, purchase, or gameplay-effect implementation may
   begin before Gate A's identity and atomic-receipt acceptance passes.
4. **Test first:** each implementation task starts by adding the named failing automated tests. Record the red
   result, then implement to green.
5. **One scoped PR per implementation task unless explicitly regrouped before execution:** each PR carries its
   spec/code/test/conformance changes together and is independently reviewable.
6. **Writer ≠ verifier:** the corresponding verifier task is a merge gate, not optional cleanup.
7. **Playable means joined-client evidence:** logs, registration, or engine-free tests alone never satisfy a
   user-visible node task.
8. **No speculative framework extraction:** all first-consumer work stays in
   `src/SBPR.Niflheim.HomesteadStones/`; extract shared infrastructure only after a real second consumer exists.

## Common path contract

These are the planned owning paths. If the landed v1 substrate uses a materially different layout, amend this
artifact and the plan before implementation rather than improvising divergent homes.

```text
src/SBPR.Niflheim.HomesteadStones/
├── Domain/{Identity,Content,StoneProgression,CharacterProgression,Activation}/
├── Application/{Commands,Queries,Receipts}/
├── Persistence/{Stone,Characters,Recovery}/
├── Adapters/{Identity,Activities,Cooking,Crafting,Archer,Warrior}/
└── Features/Progression/

tests/
├── NiflheimProgressionDomainTests.cs
├── NiflheimProgressionContractTests.cs
├── NiflheimProgressionRecoveryTests.cs
└── NiflheimProgressionAdapterTests.cs
```

The exact acceptance names below are normative. Keep them searchable in test names, evidence manifests, or both.

---

## Phase 0 — Gate A: authenticated identity plus one atomic AP receipt

**Purpose:** Resolve the only blocking technical unknowns and prove one hostile-client-safe, crash-safe mutation
before any gameplay progression work.

- [ ] **T001 [US1] Select the authenticated-principal provider and durable transaction mechanism by executable spike.** Add disposable spike/harness code under `tools/niflheim-progression-spike/`, exercise candidate connection binding and each durable-write boundary, and record the selected mechanism plus rejected alternatives in `docs/v2/planning/homestead-stone-progression-research.md` and `docs/v2/planning/homestead-stone-progression-plan.md`. The spike must prove the required properties without adding gameplay Trees or nodes. Acceptance: `AT-P0-IDENTITY`, `AT-P0-CRASH-EACH-WRITE`, `AT-P0-RECOVERY-REPORT`.
- [x] **T002 [US1] Implement the smallest end-to-end Foundational AP receipt vertical slice.** Add authenticated value objects in `Domain/Identity/ProgressionIdentity.cs`, the command envelope in `Application/Commands/ProgressionCommandPipeline.cs`, durable result handling in `Application/Receipts/OperationReceiptStore.cs`, Stone/character writes in `Persistence/Stone/ZdoStoneProgressionStore.cs` and `Persistence/Characters/CharacterProgressionStore.cs`, recovery in `Persistence/Recovery/ReceiptRecovery.cs`, and one trusted placement adapter in `Adapters/Activities/FoundationalPlacementAdapter.cs`. Until T007 lands real relationships, the spike may use only an explicit preconfigured-test authorization; it must not add a production relationship bypass. The command carries expected Stone/character revisions and the handler compare-and-sets them before any durable write (a losing concurrent client rejects `StaleStoneRevision`/`StaleCharacterRevision` with zero mutation). Add red-to-green coverage in `tests/NiflheimProgressionContractTests.cs`, `tests/NiflheimProgressionRecoveryTests.cs`, and `tests/NiflheimProgressionRevisionTests.cs`. Acceptance: `AT-P0-AP-ATOMIC`, `AT-P0-REPLAY`, `AT-P0-HOSTILE-PRINCIPAL`, `AT-P0-MIRRORED-ACCUMULATES-ONLY`.
  - Implementation landed (engine-free CLEAN-side slice + net48 ZDO projection sink) including the revisioned command + optimistic-concurrency (CAS) gate; the four named acceptance tests plus the revision/two-client-race suite are red-first then green (24 tests, 600 total pass) and the net48 mod builds zero-warning. Recovery coverage models a SIMULATED in-process crash + fresh-store-over-fsync'd-journal replay (not a real OS process kill); real child-process death at every durable boundary was proven by T001 and a real in-world reproduction is scoped to T003. Independent Gate-A acceptance is T003; this box marks code+tests landed, not gate sign-off.
  - **Gate-A remediation (card t_e1073dd8):** T003 independently FAILED the merged slice — a fresh process reset every projection balance/revision to 0 because committed operations were not rehydrated from the durable journal (two processes could each commit against expected revision 0; the ZDO read cache was an unwarmed in-memory 0 while journal truth was non-zero). Remediation: the receipt record now persists the spec-mandated identity fields (`AccountId`/`CharacterId`/`StoneId`), and the store replays the durable journal at construction to rebuild every balance and CAS revision from journal truth before any command (only committed ops project; partial ops stay quarantined). Six red-first fresh-process/post-restart tests were added (`tests/NiflheimProgressionRehydrationTests.cs`); 606/606 tests pass, net48 Release builds 0/0, docs-lint OK. T003 must rerun its out-of-process harness; this box is not Gate-A sign-off.
- [ ] **T003 [US1] Independently verify Gate A and publish the gate result.** A non-author reruns hostile principal substitution, same/conflicting operation replay, two-client revision race, and process death after every durable-write boundary; inspects balances/provenance/recovery output; and creates the docs-lint-compliant evidence scaffold at `docs/v2/evidence/`, `docs/v2/evidence/homestead-progression/`, and `docs/v2/evidence/homestead-progression/gate-a/` with a `README.md` and `index.md` in each new folder. Record the Gate-A PASS/FAIL evidence in the leaf pair. Any ambiguity, partial result, guessed repair, or non-auditable recovery is FAIL and keeps every later task blocked.

**Checkpoint:** one authenticated event converges to exactly one Personal AP, Cumulative AP, and Mirrored Stone AP
result under replay and crash. Gate A must be independently accepted before Phase 1.

---

## Phase 1 — Tracer 1: versioned state skeleton and read model

**Purpose:** Establish the authoritative aggregates, current-build definitions, derived projection, reset path,
and honest no-effect read surface.

- [x] **T004 [US1] Implement versioned aggregate envelopes and the Stone progression read model with no gameplay effects.** Add `Domain/StoneProgression/StoneProgressionAggregate.cs`, `Domain/CharacterProgression/CharacterProgressionAggregate.cs`, `Domain/Identity/AccountStoneAuthorityIndex.cs`, `Domain/Activation/DerivedActivationView.cs`, `Application/Queries/GetStoneProgressionView.cs`, and engine-free round-trip tests in `tests/NiflheimProgressionDomainTests.cs`. Persist earned/selected/provenance state only; do not add an active-effects ledger. Acceptance: `AT-STATE-ROUNDTRIP`, `AT-READMODEL-STONE-ID`, `AT-NO-ACTIVE-LEDGER`.
  - Implementation landed (engine-free CLEAN-side envelopes). The three data-model aggregates (StoneProgressionAggregate, CharacterProgressionAggregate, AccountStoneAuthorityIndex) plus a shared deterministic snapshot codec (`Domain/Snapshots/AggregateSnapshot.cs`, with `VersionedId` for stable content key+version) round-trip every authoritative owner/revision/stable-identity/provenance field (`AT-STATE-ROUNDTRIP`, asserted field-by-field and by full serialize equality). `GetStoneProgressionView` returns the world-scoped `StoneId.FromHostZone` identity shared across callers plus a caller-specific projection (balances, relationship, per-node derived status), rejects an authority index keyed to another account or Stone, and carries no client-authoritative ready flag (`AT-READMODEL-STONE-ID`). `DerivedActivationView` is disposable-by-construction: no `Serialize`/persistence surface, and identical persisted aggregates derive Active vs Dormant purely from the authority snapshot with zero writes, proving no independently mutable active-effects ledger exists (`AT-NO-ACTIVE-LEDGER`). Eight red-first-then-green tests added to `tests/NiflheimProgressionDomainTests.cs`; suite 614/614, net48 Release builds 0/0, docs-lint OK 129 docs, `git diff --check` clean. Independent Tracer-1 acceptance is T006; this box marks code+tests+docs landed, not gate sign-off.
- [x] **T005 [US3] Implement the immutable current-build content registry, exact 20-node roster, and explicit incompatible-fixture reset.** Add stable definitions in `Domain/Content/HomesteadProgressionCatalog.cs`, registry validation in `Domain/Content/ContentRegistryValidator.cs`, reset/quarantine operations in `Persistence/Recovery/ProgressionStateRepair.cs`, and roster/mismatch tests in `tests/NiflheimProgressionDomainTests.cs` and `tests/NiflheimProgressionRecoveryTests.cs`. Assert 20 authored = 13 executable + seven unavailable and reject unknown same-build references without migration invention. Acceptance: `AT-CONTENT-MISMATCH-REJECT`, `AT-INVARIANT-QUARANTINE`, `AT-UNRELEASED-DATA-RESET`.
  - Implementation landed (engine-free CLEAN-side registry). `HomesteadProgressionCatalog` is the immutable current-build registry: the exact 20-node first-build roster with stable `NodeId`/version, Tree/Facet/Foundational identities, and per-node outcome/ownership/first-build status; display labels are carried as metadata, never as identity. `ContentRegistryValidator` asserts the roster arithmetic invariant (20 authored = 13 executable + 7 unavailable, of which 12 executable Level-1 + Swift Preparation the sole executable Level-2) as an in-code drift guard, and `ValidateNodeReference` rejects unknown keys, stale node versions, wrong-tree claims, and stale registry versions as the stable `ContentVersionMismatch` code with no misbinding to a "closest" definition (`AT-CONTENT-MISMATCH-REJECT`). `ProgressionStateRepair.Scan` isolates contradictory/unknown state (bad Stone level, negative ledgers, unknown developed/purchased nodes, Local-node-as-purchase, authority keyed to another account/Stone) with a reason and never guesses a repair (`AT-INVARIANT-QUARANTINE`); `ResetIncompatibleFixture` explicitly discards a disposable unreleased fixture, stamps the current build, and rebuilds the derived view, while leaving a compatible fixture untouched (`AT-UNRELEASED-DATA-RESET`). 18 red-first-then-green tests added across the domain + recovery suites; suite 632/632, net48 Release builds 0/0, docs-lint OK 140 docs, `git diff --check` clean. Independent Tracer-1 acceptance is T006; this box marks code+tests+docs landed, not gate sign-off.
- [ ] **T006 [US1] Independently verify Tracer 1.** A non-author reconstructs projections from persisted fixtures, injects invalid IDs/revisions/ledger values, confirms quarantine and explicit reset behavior, and verifies the read model reports stable IDs, versions, authored status, exact prices/requirements, and no client-authoritative ready flag. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-1/`.

**Checkpoint:** all logical owners round-trip, invalid state fails safely, and the read model is useful without
claiming any node is playable.

---

## Phase 2 — Tracer 2: relationships and ongoing Foundational AP

**Purpose:** Deliver User Story 1 across real Homestead relationship lifecycle and trusted placement evidence.

- [ ] **T007 [US1] Implement Bond, Attunement, Homestead sibling exclusivity, and release/rejoin as one recoverable relationship slice.** Add relationship transitions in `Domain/CharacterProgression/Relationships.cs`, commands in `Application/Commands/RelationshipCommands.cs`, authority-index persistence in `Persistence/Characters/AccountStoneAuthorityStore.cs`, and contract/domain tests in `tests/NiflheimProgressionContractTests.cs` and `tests/NiflheimProgressionDomainTests.cs`. Include the variant-authored Community Attunement exception while keeping Community Bond exclusive. Acceptance: `AT-BOND`, `AT-ATTUNEMENT`, `AT-SIBLING-EXCLUSIVE`, `AT-SEQUENTIAL-SIBLING`, `AT-COMMUNITY-ATTUNEMENT-EXCEPTION`, `AT-ATTUNEMENT-RELEASE`, `AT-BOND-RELEASE-DORMANCY`.
- [ ] **T008 [US1] Harden Foundational placement into the ongoing protected AP source.** Extend `Adapters/Activities/FoundationalPlacementAdapter.cs`, `Domain/Content/HomesteadProgressionCatalog.cs`, and `Application/Commands/ActivityCommands.cs` so stable piece membership, exclusions, Stone Area, authenticated actor, placement success, and repetition policy are server-observed; Tree commitment must not disable the source. Add adapter/contract tests and one joined-client placement proof. Acceptance: `AT-FOUNDATIONAL-CATALOG`, `AT-FOUNDATIONAL-ONGOING`, `AT-FOUNDATIONAL-EXCLUDED`, `AT-RELATIONSHIP-RESTART`.
- [ ] **T009 [US1] Independently verify Tracer 2.** A non-author attempts sibling-account evasion, Bond/Attunement overlap, stale release, unauthorized placement, outside-area placement, excluded content, relog, and restart; then confirms exact retained/dormant state and one real joined-client AP receipt. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-2/`.

**Checkpoint:** User Story 1 is independently demonstrable on a preconfigured Stone-Level-2 Homestead.

---

## Phase 3 — Tracer 3: commit Trees into Stone Facets

**Purpose:** Deliver the first Stone-owned choice without mutating Stone Level or granting a personal purchase.

- [ ] **T010 [US2] Implement revisioned Profession/Martial Tree commitment into authored Stone Facets.** Add Facet and commitment transitions in `Domain/StoneProgression/StoneFacets.cs`, `CommitTreeToFacet` in `Application/Commands/FacetCommands.cs`, persistence through `Persistence/Stone/ZdoStoneProgressionStore.cs`, panel/read affordances in `Features/Progression/HomesteadProgressionPanel.cs`, and tests in `tests/NiflheimProgressionDomainTests.cs` and `tests/NiflheimProgressionContractTests.cs`. Acceptance: `AT-COMMIT-PROFESSION-FACET`, `AT-COMMIT-MARTIAL-FACET`, `AT-COMMIT-STALE`, `AT-FACET-OCCUPIED`, `AT-FACET-CATEGORY`, `AT-COMMIT-UNAUTHORIZED`, `AT-COMMIT-REPLAY`, `AT-NO-STONE-LEVEL-MUTATION`.
- [ ] **T011 [US2] Independently verify Tracer 3.** A non-author commits each valid category, exercises every named rejection/replay, restarts, and confirms no operation changes Historical or Active Stone Level, AP, BP, or purchase state. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-3/`.

**Checkpoint:** one Profession and one Martial choice are inspectable, persistent, and independently testable.

---

## Phase 4 — Tracer 4: BP development, Tree advancement, Offering, purchase, and policy grammar

**Purpose:** Prove the shared progression grammar once before family-specific gameplay providers branch.

- [ ] **T012 [US2] Implement aligned-activity BP credit and Stone-wide personal BP development.** Add trusted aligned evidence in `Adapters/Activities/AlignedActivityAdapter.cs`, BP transitions in `Domain/CharacterProgression/BondPower.cs`, node/Tree investment in `Domain/StoneProgression/TreeDevelopment.cs`, `RecordAlignedActivity` and `ApplyBPToNode` in `Application/Commands/ActivityCommands.cs` and `Application/Commands/DevelopmentCommands.cs`, and tests in the domain/contract suites. Acceptance: `AT-BP-STONE-WIDE`, `AT-BP-NOT-SHARED`, `AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT`, `AT-NO-DIRECT-LEVEL-METER`, `AT-TREE-ADVANCE-1-2`, `AT-ESCALATING-COST-CONFIG`.
- [ ] **T013 [US3] Implement personal Offering, AP/Facet-Credit payment, purchase provenance, and derived same-Tree Tier Access.** Add `Domain/CharacterProgression/NodePurchases.cs`, Offering derivation in `Domain/Activation/DerivedActivationView.cs`, `PurchaseNode` in `Application/Commands/PurchaseCommands.cs`, and tests proving Local/unavailable/unoffered rejection, idempotent purchase, same-Tree prior-Offered-Set rules, and no stored Tier XP. Acceptance: `AT-LOCAL-NOT-OFFERED`, `AT-PERSONAL-BECOMES-OFFERED`, `AT-PURCHASE-IDEMPOTENT`, `AT-TIER-SAME-TREE`.
- [ ] **T014 [US2] [US3] Implement the single Settlement Local policy and relationship-driven dormancy projection.** Add policy state in `Domain/StoneProgression/SettlementLocalPolicy.cs`, command handling in `Application/Commands/LocalPolicyCommands.cs`, eligibility derivation in `Domain/Activation/DerivedActivationView.cs`, and boundary tests that AND Local policy with ordinary build Permission. Acceptance: `AT-LOCAL-POLICY`, `AT-RELATIONSHIP-DORMANCY`.
- [ ] **T015 [US2] [US3] Independently verify Tracer 4.** A non-author proves cross-Tree BP spend, per-Governor isolation, BP-driven Cooking 1→2, no direct Tree-level meter, Offering/purchase ownership, Tier Access exclusions, policy changes during occupancy, and dormancy/rejoin. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-4/`.

**Checkpoint:** the shared grammar is accepted. Only now may the four family branches proceed in parallel.

---

## Phase 5 — Tracer 5: Cooking branch

**Purpose:** Deliver four executable Cooking nodes and one honestly unavailable node. T016–T019 are sequential
within the branch because each extends the same Cooking adapter/provider surface.

- [ ] **T016 [US4] Implement Savor the Hearth as the first Cooking vertical slice.** Add the Cooking adapter/provider boundary in `Adapters/Cooking/CookingProviders.cs`, wire active Local-node and Settlement-policy evaluation through `Domain/Activation/DerivedActivationView.cs`, add deterministic adapter tests, and produce joined-client in-area/exit evidence proving timer factor 0.5→1 without item/stat mutation. Acceptance: `AT-SAVOR-AREA-EXIT`.
- [ ] **T017 [US4] Implement Field Prep through the shared Cooking-aware Bushcraft policy.** Extend `Adapters/Cooking/CookingCraftPolicy.cs`, preserve unchanged Boar Jerky/Queen's Jam inputs/yields and normal Cooking XP/speed/bonus behavior, add contract/adapter tests, and produce joined-client recipe/craft evidence. Acceptance: `AT-FIELD-PREP-COOKING-POLICY`.
- [ ] **T018 [US4] Implement Iron Stomach as a durable refresh-threshold provider.** Add `Adapters/Cooking/FoodRefreshThresholdProvider.cs`, preserve three food slots and normal debit/stats/duration, test highest-provider composition and relationship loss/restart, and produce joined-client 75%-threshold evidence. Acceptance: `AT-IRON-STOMACH-75`.
- [ ] **T019 [US3] [US4] Implement Swift Preparation and close the Cooking Tier-2 path.** Add `Adapters/Cooking/MenuCraftDurationProvider.cs`, apply factor 1/3 only after vanilla Cooking-skill adjustment and only to eligible menu-crafted food, prove Field Prep + Iron Stomach prior-Offered-Set access, add tests, and produce joined-client timing evidence. Acceptance: `AT-SWIFT-MENU-ONLY`, `AT-COOKING-TIER2`, `AT-NO-COOKING-COMPLETION`.
- [ ] **T020 [US4] Independently verify the Cooking branch and unavailable Watchful Cook.** A non-author reruns each executable node's own joined-client proof and verifies Watchful Cook is visible but rejects BP/AP/Offering/activation and has no fake effect. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-5-cooking/`. Acceptance: `AT-WATCHFUL-UNAVAILABLE`.

**Checkpoint:** four Cooking effects are individually playable; Watchful Cook is honestly unavailable.

---

## Phase 6 — Tracer 6: Crafting branch

**Purpose:** Deliver three executable Crafting nodes with exact-item authority and two honestly unavailable nodes.
T021–T023 are sequential within this branch.

- [ ] **T021 [P] [US4] Implement Refined Workshop as real-versus-effective station-level policy.** Add `Adapters/Crafting/EffectiveStationLevelProvider.cs`, apply +1 only to eligible portable-item production/upgrade/repair inside the active Homestead, preserve the observed real station level and structure/build gates, add tests, and produce joined-client effective-Level-3 operation evidence. Acceptance: `AT-REFINED-REAL-VS-EFFECTIVE`.
- [ ] **T022 [US4] Implement Masterwork exact-instance Workmanship issuance.** Add `Adapters/Crafting/WorkmanshipIssuanceProvider.cs` and `Domain/CharacterProgression/ItemProvenance.cs`, issue one deterministic visible property only on eligible non-stackable durable outputs, explicitly dirty persistence, preserve valid upgrade/transfer, degrade tampered/unknown metadata to vanilla, and add joined-client issuance/transfer evidence. Acceptance: `AT-MASTERWORK-ISSUE`, `AT-ITEM-UPGRADE-PRESERVE`, `AT-ITEM-TRANSFER`, `AT-ITEM-TAMPER-DEGRADE`.
- [ ] **T023 [US4] Implement Built to Last as durable future-output provenance.** Add `Adapters/Crafting/DurabilityIssuanceProvider.cs`, prove maximum-durability property issuance on future eligible outputs, relationship loss/restart survival, idempotency, and no retroactive mutation; add tests and joined-client evidence. Acceptance: `AT-BUILT-TO-LAST`.
- [ ] **T024 [US4] Independently verify the Crafting branch and unavailable Measured Cuts/Artisan's Counter.** A non-author reruns each executable node's own joined-client proof, exact-item lifecycle/tamper cases, and both unavailable-node rejection paths. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-6-crafting/`. Acceptance: `AT-CRAFTING-UNAVAILABLE`.

**Checkpoint:** three Crafting effects are individually playable; both remaining Crafting nodes are honestly unavailable.

---

## Phase 7 — Tracer 7: Archer branch

**Purpose:** Deliver three executable Archer nodes and two honestly unavailable nodes. T025–T027 are sequential
within this branch.

- [ ] **T025 [P] [US4] Implement Practice Range Local placement/recipe capabilities.** Add `Adapters/Archer/PracticeRangeProvider.cs`, require active Local policy plus ordinary Permission, expose the exact vanilla Archery Target and 100 Practice Arrows for 8 Wood, retain bow damage with 0 ammo damage and deterministic vanilla target return, add tests, and produce joined-client placement/recipe/impact evidence. Acceptance: `AT-PRACTICE-RANGE`, `AT-PRACTICE-ARROW-DAMAGE`, `AT-TARGET-RETURN`.
- [ ] **T026 [US4] Implement Field Fletching I through Bushcraft.** Add `Adapters/Archer/BushcraftRecipeProvider.cs`, expose the unchanged Wood Arrow recipe only while active, preserve ordinary recipe inputs/yield/authority, add adapter/contract tests, and produce joined-client craft evidence. Acceptance: `AT-FIELD-FLETCHING`.
- [ ] **T027 [US4] Implement Fletcher's Habit exact-arrow terminal-impact recovery.** Add `Adapters/Archer/ProjectileRecoveryProvider.cs` and exact consumed-item provenance, make one authoritative result for hit/water/shield/miss/TTL/multishot cases, suppress the roll when Practice Range target return wins, add tests, and produce joined-client lifecycle/no-duplicate evidence. Acceptance: `AT-FLETCHER-HIT-LIFECYCLE`, `AT-FLETCHER-NO-DUP`.
- [ ] **T028 [US4] Independently verify the Archer branch and unavailable Steady Aim/Bowyer's Lore.** A non-author reruns each executable node's own joined-client proof, exact-arrow authority/one-result cases, and both unavailable-node rejection paths. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-7-archer/`. Acceptance: `AT-ARCHER-UNAVAILABLE`.

**Checkpoint:** three Archer effects are individually playable; both remaining Archer nodes are honestly unavailable.

---

## Phase 8 — Tracer 8: Warrior branch

**Purpose:** Deliver three executable Warrior nodes and two honestly unavailable nodes. T029–T031 are sequential
within this branch.

- [ ] **T029 [P] [US4] Implement T.W.I.G. Training Local placement capability.** Add `Adapters/Warrior/LocalPlacementProvider.cs`, expose exact unchanged T.W.I.G. placement only under active Settlement policy plus ordinary Permission, add overlap/relationship/policy tests, and produce joined-client placement evidence. Acceptance: `AT-TWIG-LOCAL`.
- [ ] **T030 [US4] Implement Ready Hands on copied queued equip and unequip durations.** Add `Adapters/Warrior/EquipDurationProvider.cs`, cover both halves for the exact data-defined eligible melee registry, exclude armor/tools/bows/reload/shared-prefab mutation, test cancellation/attack behavior, and produce joined-client timing evidence. Acceptance: `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS`.
- [ ] **T031 [US4] Implement Weapon Discipline as one permanent idempotent skill-cap choice.** Add `Adapters/Warrior/SkillCapProvider.cs`, `ChooseWeaponDisciplineSkill` in `Application/Commands/PurchaseCommands.cs`, and durable choice/provenance state in `Domain/CharacterProgression/SkillCapChoices.cs`; test UI/gain/death/save/restart and highest-provider composition for values ≤100, then produce joined-client choice/lifecycle evidence. Acceptance: `AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE`.
- [ ] **T032 [US4] Independently verify the Warrior branch and unavailable Shrug It Off I/Heavy Hands.** A non-author reruns each executable node's own joined-client proof, exact choice idempotency/exclusions, and both unavailable-node rejection paths. Record PASS/FAIL under `docs/v2/evidence/homestead-progression/tracer-8-warrior/`. Acceptance: `AT-WARRIOR-UNAVAILABLE`.

**Checkpoint:** three Warrior effects are individually playable; both remaining Warrior nodes are honestly unavailable.

---

## Phase 9 — Tracer 9: revocation, recovery, remote-shaped command, and cross-Tree proof

**Purpose:** Close lifecycle and integration boundaries only after all four family branches pass.

- [ ] **T033 [US5] Implement atomic Tree revocation and replacement-Facet Credit.** Add revocation transitions in `Domain/StoneProgression/TreeRevocation.cs` and `Domain/CharacterProgression/FacetCredit.cs`, `RevokeTree` in `Application/Commands/FacetCommands.cs`, journaled fan-out/recovery in `Persistence/Recovery/ReceiptRecovery.cs`, and tests for full rollback/refund/durable-outcome semantics. Acceptance: `AT-REVOKE-ATOMIC`, `AT-REVOKE-NO-BP-REFUND`, `AT-FACET-CREDIT`, `AT-REPLACEMENT-NO-AUTOBUY`, `AT-DURABLE-OUTCOMES-SURVIVE`.
- [ ] **T034 [US5] Complete relog/restart/rejoin recovery and explicit disposable-data reset across all aggregates.** Extend `Persistence/Recovery/ProgressionStateRepair.cs`, add operator inspection/quarantine output in `Features/Progression/ProgressionDiagnostics.cs`, and run process-kill/restart fixtures across relationship, purchase, item, choice, and revocation boundaries. Acceptance: `AT-RESTART-SUITE`, `AT-UNRELEASED-DATA-RESET`.
- [ ] **T035 [US5] Prove one non-proximate selection through the shared Stone-identity command seam without remote evidence fabrication.** Add transport-neutral routing in `Features/Progression/ProgressionCommandEndpoint.cs`, the compact portfolio query in `Application/Queries/GetRelationshipPortfolio.cs`, and bounded revision/invalidation notifications in `Application/Queries/ProgressionNotifications.cs`; keep local/server-observed evidence adapters non-client-callable, add hostile remote contract tests, and produce a joined-client proof away from the Stone. Acceptance: `AT-REMOTE-SHAPED`, `AT-LOCAL-EVIDENCE-NOT-REMOTE`.
- [ ] **T036 [US1] [US5] Add the progression runtime conformance and bounded observability surface.** Add `Features/Progression/ProgressionConformance.cs` for registry/version, Facet/Tree/node counts, executable/unavailable status, required handlers/providers, stable IDs, and startup recovery report; add config-gated diagnostics that avoid secrets/raw PII; and update `AGENTS.md` only if a new always-on rule is required. A green conformance report proves shape, never playability.
- [ ] **T037 [US4] [US5] Independently verify the complete technical proof.** A non-author runs at least three preconfigured Stone-Level-2 Homesteads, all relationship and hostile-client cases, all four family branches, revocation/replacement, restart/reset, and the remote-shaped command. Confirm each of the 13 executable nodes has its own joined-client/in-world artifact and all seven unavailable nodes reject AP/BP/Offering/activation. Record the final matrix under `docs/v2/evidence/homestead-progression/tracer-9/`. Acceptance: `AT-CROSS-TREE-13`, `AT-UNAVAILABLE-7`.

**Checkpoint:** Tracer 9 is PASS only when the entire accepted S2 proof is recoverable, hostile-client-safe, and
visibly demonstrated. This does not ratify final balance, production migrations, release compatibility, or S5.

---

## Dependency graph

```text
Implementation authorization
  └── v1 placement/identity runtime present on implementation base
        └── T001 Gate-A mechanism spike
              └── T002 Gate-A receipt slice
                    └── T003 independent Gate-A verification
                          └── T004 state/read model
                                └── T005 content/reset
                                      └── T006 verify Tracer 1
                                            └── T007 relationships
                                                  └── T008 Foundational AP
                                                        └── T009 verify Tracer 2
                                                              └── T010 Facet commitment
                                                                    └── T011 verify Tracer 3
                                                                          └── T012 BP/development
                                                                                └── T013 purchase/Tier Access
                                                                                      └── T014 policy/dormancy
                                                                                            └── T015 verify Tracer 4
                                                                                                  ├── T016→T020 Cooking
                                                                                                  ├── T021→T024 Crafting
                                                                                                  ├── T025→T028 Archer
                                                                                                  └── T029→T032 Warrior
                                                                                                        all four branches
                                                                                                              └── T033→T036
                                                                                                                    └── T037 final verification
```

### Parallel opportunities

- After T015, the Cooking, Crafting, Archer, and Warrior branches may run in parallel.
- T021, T025, and T029 are marked **[P]** relative to one another, not relative to unfinished T016–T020 work.
- Within each family branch, tasks remain sequential because they extend shared adapter/provider files and each
  branch closes with a distinct non-author verification task.
- Evidence directories may be authored independently, but shared package docs, registry definitions, project
  files, and README/index manifests are conflict surfaces; union changes rather than choosing one branch's rows.

## Requirements traceability

| Accepted requirements | Owning tasks |
|---|---|
| FR-001–FR-002 stable Stone identity and authenticated principal | T001–T003 |
| FR-003–FR-006 relationships, fixtures, Foundational Tree, Facets/palette | T004–T011 |
| FR-007–FR-012 command envelope, AP/BP ownership, node-investment Tree advancement, replay/concurrency | T002–T003, T008, T012–T015 |
| FR-013–FR-020 commitment, per-Tree Tier Access, Local policy, 20/13/7 roster, derived activation, attuned purchase authority | T010–T032 |
| FR-021–FR-025 revocation, current-build identity/reset, lifecycle recovery, reusable local/remote command/read seams | T033–T035 |
| FR-026 per-node joined-client proof | T016–T032 and T037 |
| FR-027 spec/code/tests/runtime-conformance synchronization | every implementation task's definition of done, especially T036 |

## Per-task definition of done

Every implementation checkbox requires all of the following in its own PR unless the task explicitly says it is
a spike or verifier:

1. Named automated tests added first and observed failing for the intended reason.
2. The smallest vertical implementation that makes those tests pass.
3. Success plus every applicable stable rejection/replay/revision behavior from the contracts document.
4. Package docs and runtime conformance updated in the same PR when behavior or a technical choice moves.
5. `python3 scripts/docs-lint.py` passes.
6. `git diff --check` passes.
7. `dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release` completes with
   zero warnings and zero errors.
8. The relevant automated test suite passes.
9. Every user-visible node task includes its own smallest joined-client/in-world artifact; logs alone do not count.
10. The paired independent verifier records PASS. FAIL returns the task to implementation; it is not reframed away.
11. Any AFK/live Valheim client is stopped after evidence and its process absence is verified.

## Scope guard

These tasks do **not** include Stone-level advancement, Level 3+, upkeep/decay design, finished Stones UI,
production migration/grandfathering, final economy/balance, unavailable-node implementation, non-Homestead Stone
families, clustered-50-player scale, release packaging, or final pre-release persistence ratification. Any such
work requires a new accepted specification and task decomposition.
