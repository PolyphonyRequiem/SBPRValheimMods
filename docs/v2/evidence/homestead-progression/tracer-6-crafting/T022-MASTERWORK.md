---
status: current
---

# T022 — Masterwork exact-instance Workmanship issuance

Acceptance: `AT-MASTERWORK-ISSUE`, `AT-ITEM-UPGRADE-PRESERVE`, `AT-ITEM-TRANSFER`,
`AT-ITEM-TAMPER-DEGRADE`.

Masterwork is a personal Crafting Character Effect: while active for a crafter it
issues **one deterministic visible Workmanship Property** onto an eligible
non-stackable durable item they craft/upgrade, bound behind a server-keyed
integrity token so the seal survives legitimate upgrade/transfer but a forged or
hand-edited stamp degrades to a plain vanilla item.

## Engine-free CLEAN-side proof (landed)

The Masterwork slice ships as two pure, engine-free files composed over the
already-accepted shared grammar:

- `Domain/CharacterProgression/ItemProvenance.cs` — the `WorkmanshipCodec` that
  stamps/reads/validates one Workmanship Property (`Workmanship=Masterwork`)
  onto an item's custom-data map behind the abstract
  `IItemMetadataWriter`/`IItemMetadataReader` surface (the exact mirror of the
  accepted Stone `HomesteadProvenanceCodec` abstraction), plus the eligibility
  rule (`IsEligible` = non-stackable **and** durable), the `ItemProvenanceId`,
  and the server-held HMAC-SHA-256 `WorkmanshipIntegrityKey`. The integrity token
  is computed over the canonical, length-framed **immutable** provenance fields
  only (schema, issuing node `Masterwork@1`, provenance id, crafter account, exact
  item type, the one property) — it deliberately excludes mutable per-instance
  facts (quality/upgrade level, durability, stack), which is what lets a
  legitimate upgrade/transfer keep validating while a forgery cannot.
- `Adapters/Crafting/WorkmanshipIssuanceProvider.cs` — the pure decision. Masterwork
  activation derives through the shipped T004 `DerivedActivationView` (purchase
  record for `Masterwork@1` at this Stone **and** an active relationship; no second
  ledger — `AT-NO-ACTIVE-LEDGER`). While active it issues the deterministic stamp on
  an eligible, not-already-stamped output, returning stable outcomes
  `Issue`/`EffectNotActive`/`IneligibleItem`/`AlreadyStamped`.

Proven by `tests/NiflheimMasterworkTests.cs` (21 tests, all green):

| # | Claim | Test | Acceptance |
|---|-------|------|------------|
| 1 | Active Masterwork issues one deterministic Workmanship on an eligible durable output | `ActiveMasterwork_IssuesOneDeterministicWorkmanshipProperty_OnEligibleDurableOutput` | `AT-MASTERWORK-ISSUE` |
| 2 | Same inputs always produce the same stamp (no RNG) | `Issuance_IsDeterministic_SameInputsProduceTheSameStamp` | `AT-MASTERWORK-ISSUE` |
| 3 | Dormant Masterwork (no purchase) issues nothing | `DormantMasterwork_NoPurchase_IssuesNothing` | `AT-MASTERWORK-ISSUE` |
| 4 | Dormant Masterwork (relationship released) issues nothing | `DormantMasterwork_NoActiveRelationship_IssuesNothing` | `AT-MASTERWORK-ISSUE` |
| 5 | A sibling without their own purchase issues nothing even with a relationship | `SiblingWithoutOwnPurchase_IssuesNothing_EvenWhenSiblingHoldsRelationship` | `AT-MASTERWORK-ISSUE` |
| 6 | Ineligible outputs (stackable and/or non-durable) issue nothing even when active | `IneligibleOutput_IssuesNothing_EvenWhenActive` (Theory ×3) | `AT-MASTERWORK-ISSUE` |
| 7 | Already-stamped output is an idempotent no-op | `AlreadyStampedOutput_IsIdempotentNoOp` | `AT-MASTERWORK-ISSUE` |
| 8 | Eligibility requires BOTH non-stackable and durable | `EligibilityRule_RequiresBothNonStackableAndDurable` | `AT-MASTERWORK-ISSUE` |
| 9 | Stamp→read round-trips a valid Workmanship | `StampThenRead_RoundTripsAValidWorkmanship` | `AT-MASTERWORK-ISSUE` |
| 10 | Unstamped item reads Absent → vanilla | `UnstampedItem_ReadsAbsent_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 11 | Valid stamp keeps validating after a preserving upgrade | `ValidStamp_KeepsValidating_AfterAnUpgradeThatPreservesCustomData` | `AT-ITEM-UPGRADE-PRESERVE` |
| 12 | Valid stamp survives clone / inventory / container transfer | `ValidStamp_SurvivesCloneAndTransfer` | `AT-ITEM-TRANSFER` |
| 13 | Hand-edited property degrades to vanilla | `TamperedProperty_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 14 | Forged token without the server key degrades to vanilla | `ForgedTokenWithoutTheServerKey_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 15 | Foreign-server-key stamp degrades to vanilla here (valid under its own key) | `StampMintedUnderAForeignServerKey_DegradesToVanillaHere` | `AT-ITEM-TAMPER-DEGRADE` |
| 16 | Lifted-and-pasted stamp on a different item type degrades to vanilla | `LiftedAndPastedStamp_OntoADifferentItemType_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 17 | Unknown schema degrades to vanilla | `UnknownSchema_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 18 | Partial write (missing provenance id or token) degrades to vanilla | `PartialWriteMissingTheProvenanceIdOrToken_DegradesToVanilla` | `AT-ITEM-TAMPER-DEGRADE` |
| 19 | A weak integrity key is rejected at construction | `IntegrityKey_RejectsAWeakKey` | `AT-ITEM-TAMPER-DEGRADE` |

Red-first was observed for the intended reason: with the issuance decision stubbed
to always-refuse and the token validation short-circuited (`if (false && ...)`),
14 of 21 failed (issuance never issued; every tampered stamp read `Valid`) while the
already-refusing/absent cases passed; restoring the real derivation + validation
made the file 21/21 green and the full suite **1386/1386** (1365 baseline + 21 new).

## Live wiring (net48, host-authoritative)

`Features/Crafting/MasterworkIssuanceObserver` postfixes `InventoryGui.DoCrafting`
(decomp `assembly_valheim` :42523) on the **authoritative host**:

1. Fails closed unless the durable server integrity key is armed **and** the
   composed `LocalProgressionObserver.Server` is present (a pure remote client
   issues nothing).
2. Resolves the crafter's Masterwork activation straight from the composed server
   stores — Stone-Area membership at the crafter's position + the transport-bound
   internal principal (never a client claim), the exact host resolution the sibling
   `FieldFletchingRecipeGate` uses.
3. Reads server-observed eligibility from the produced recipe's shared data
   (`m_maxStackSize <= 1` non-stackable **and** `m_useDurability` durable).
4. Locates the just-produced instance, and on an eligible, not-already-valid item
   stamps the deterministic Workmanship onto its real `m_customData` via
   `Features/Crafting/ItemDataMetadataAccessor`, then **explicitly dirties
   persistence** by invoking `Inventory.Changed()`.

The integrity key is a durable, server-owned per-world file
(`Features/Crafting/WorkmanshipIntegrityKeyFile`, mirroring the accepted
`PilotKeyRingFile`), armed in `FoundationalRuntimeBootstrap` after the Local
progression runtime composes and disarmed on ZNet teardown. Additive per ADR-0006 —
only our own domain-prefixed keys on one existing instance's dictionary; no prefab
cloning. Both net48 Release builds compile 0 warnings / 0 errors.

## Honesty note — logs-green is never playable

The QA-proven issuance topology is a **listen-host / dedicated-host crafter**, where
the server integrity key and the composed progression stores both exist so a real
crafted item receives its Workmanship stamp and re-validates through save/transfer.
Authoritative server→client Workmanship replication for a **pure remote crafter**
(who holds neither the key nor the stores) is the documented follow-up — the same
host-first-then-pure-client-delivery shape the accepted T021 effective-Level-3 and
T026 personal-effect-delivery remediations followed after their initial host-first
cut. The in-world joined-client issuance/transfer frame is to be run by the paired
T024 independent verifier on that topology; this record states exactly what is
proven (durable data-layer issuance + tamper/transfer semantics, headless and
host-authoritative) versus deferred (pure-client GPU-visible last mile).
