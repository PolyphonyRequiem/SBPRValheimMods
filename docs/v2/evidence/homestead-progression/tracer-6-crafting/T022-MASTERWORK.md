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

## Live wiring (net48, host-authoritative + dedicated-server joined-client delivery)

`Features/Crafting/MasterworkIssuanceObserver` postfixes `InventoryGui.DoCrafting`
(decomp `assembly_valheim` :42523) on the **authoritative host**:

1. Fails closed unless the durable server integrity key is armed **and** the
   composed `LocalProgressionObserver.Server` is present (a pure remote client
   issues nothing on THIS path).
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

### Dedicated-server joined-client delivery (T022 remediation, t_cdc76200)

The host-only path above is a **listen-host** intersection: it needs BOTH the armed
key AND `player == Player.m_localPlayer`. On an isolated dedicated-server topology
neither the headless server (no local crafter) nor a pure joined crafter (unarmed,
keyless) qualifies — which is exactly the gap the joined-client QA (`t_997667c4`)
proved. The remediation adds an authoritative, client-delivered channel that **never
ships the raw integrity key**:

- `Features/Crafting/MasterworkDedicatedDeliveryObserver` — a bounded per-peer ZRpc
  transport (mirroring the accepted `PersonalActivationDeliveryObserver`). A joined
  crafter's `DoCrafting` postfix sends server-observed produced-item facts (Stone id,
  item type, eligibility, already-stamped hint) + a correlation id. The SERVER
  authenticates the peer by the delivering `ZRpc`, re-derives that peer's bound
  internal principal + Masterwork activation from its own composed stores, mints a
  server-owned provenance id, decides + **signs** through the engine-free
  `Application/Crafting/WorkmanshipDeliveryService`, and replies with the stamp fields
  + the pre-computed HMAC token. The client writes the exact bytes via
  `WorkmanshipCodec.WriteSigned` — the persisted stamp re-validates **byte-identically**
  to a host stamp. For validation, a client reads a stamp keylessly
  (`WorkmanshipCodec.TryReadRaw`), relays fields+token, and the server answers
  Valid/Tampered (`WorkmanshipCodec.Validate`), cached in `WorkmanshipVerdictCache`.
- `Features/Crafting/MasterworkWorkmanshipTooltip` — postfixes the static
  `ItemDrop.ItemData.GetTooltip` (decomp :58293) to append the one deterministic
  `Workmanship: Masterwork` line **only** for a confirmed-valid stamp: validated under
  the composed key on the host, or against the server verdict cache on a pure client
  (requesting a verdict once per provenance id, rendering nothing until it lands).
  A forged / foreign-key / hand-edited / unconfirmed stamp degrades to a plain vanilla
  tooltip on the joined client.

Proven by `tests/NiflheimMasterworkClientDeliveryTests.cs` (13 tests, all green,
red-first observed by corrupting the server signature → 6 of 13 fail):

| Claim | Acceptance |
|-------|------------|
| Active Masterwork: server mints+signs for a pure joined crafter; the client writes it and it re-validates | `AT-MASTERWORK-ISSUE` |
| The client-written signed stamp is byte-identical to a host-stamped one | `AT-MASTERWORK-ISSUE` |
| Inactive / ineligible / already-stamped: server refuses, client leaves the item vanilla | `AT-MASTERWORK-ISSUE` |
| Client-written stamp keeps validating after a preserving upgrade | `AT-ITEM-UPGRADE-PRESERVE` |
| A receiving client validates a transferred stamp via the server (keyless read → verdict) | `AT-ITEM-TRANSFER` |
| Hand-edited / foreign-key stamp gets a Tampered verdict; the cache fails closed | `AT-ITEM-TAMPER-DEGRADE` |
| An unconfirmed provenance id fails closed in the verdict cache | `AT-ITEM-TAMPER-DEGRADE` |
| The raw integrity key never appears on any serialized wire message | security invariant |

The integrity key is a durable, server-owned per-world file
(`Features/Crafting/WorkmanshipIntegrityKeyFile`, mirroring the accepted
`PilotKeyRingFile`), armed in `FoundationalRuntimeBootstrap` after the Local
progression runtime composes and disarmed on ZNet teardown (which also clears the
client verdict cache). Additive per ADR-0006 — only our own domain-prefixed keys on
one existing instance's dictionary; no prefab cloning. Both net48 Release builds
compile 0 warnings / 0 errors; the full suite is **1399/1399** (1386 baseline + 13
new delivery tests).

## Honesty note — logs-green is never playable

The prior cut proved a **listen-host / dedicated-host crafter** only. The T022
remediation (t_cdc76200) closes the pure-remote-crafter gap: issuance is now
authoritative and client-delivered on an isolated dedicated-server topology, and a
joined receiver can validate a stamp without ever holding the key. What remains
DEFERRED to the paired live QA rerun (`t_997667c4`) is the **GPU-visible last mile**:
this record proves the durable data-layer + transport semantics headless (both
Release builds 0w/0e, 1399/1399, red-first observed), but the in-world joined-client
observation of all four ATs is the live artifact the QA card must capture on the
dedicated-server + genuine-joined-client topology before merge.
