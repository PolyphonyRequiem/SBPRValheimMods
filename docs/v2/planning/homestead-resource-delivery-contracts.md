---
title: "Homestead loyalty, upkeep, and Resource Delivery — application contracts"
status: proposed
purpose: Define authenticated commands, server-observed evidence, durable receipts, read models, provider seams, and rejection vocabulary.
---

# Homestead loyalty, upkeep, and Resource Delivery — contracts

**Normative spec:** [`homestead-resource-delivery-spec.md`](homestead-resource-delivery-spec.md)
**Data model:** [`homestead-resource-delivery-data-model.md`](homestead-resource-delivery-data-model.md)
**Authoritative requirement/acceptance matrix:** [spec §Requirement → Acceptance Mapping](homestead-resource-delivery-spec.md#requirement--acceptance-mapping)

## Contract rules

1. Every public mutation uses the existing authenticated, revisioned, idempotent Stone-command envelope.
2. Clients request intent; server adapters supply identity, item, objective, relationship, inventory, and time facts.
3. One operation ID binds one typed command/payload and one stable terminal result. Same operation/same binding replays;
   conflicting reuse rejects.
4. Successful/NoOp mutations are receipt-bearing. Ordinary rejection remains non-receipt-bearing. The final-link handshake
   adds one durable, non-mutating `ConfirmationRequired` decision followed by a separately identified confirmation mutation.
5. No command writes ZDOs, player inventory, Character AP/BP, Connection, or Stone Stock directly outside the receipt-backed application boundary.
6. Read models expose current projections and actionable rejection reasons, never write authority.

## Common command envelope

Every client-callable command carries:

```text
operationId
stoneId
typed payload
expected relevant revisions
content/schema versions
```

The server derives and binds:

```text
authenticated AccountId
acting CharacterId
world/product scope
connection/session provenance
received server time
```

A client-supplied account, character, clock, item count, inventory-fit result, damage total, relationship role, or
objective completion is never authoritative.

## Common successful result

```text
operationId
receiptId
outcome = Applied | Replayed | NoOp
resultCode
committed revisions
bounded deltas/summary
content/schema versions
```

`Replayed` returns the prior receipt/result without re-running current eligibility.

## Common rejected result

```text
operationId
outcome = Rejected
resultCode
current relevant revisions
failed requirement/retryability summary
bounded audit correlation
```

An ordinary rejection commits no gameplay mutation and has no gameplay receipt. Bounded security/audit metadata may be
logged. Same-operation replay is bound to the same typed payload; conflicting reuse is `OperationConflict`.

## Durable non-mutating decision

The final-link confirmation handshake is the sole first-slice extension that records a non-mutating terminal decision:

```text
preparationOperationId
confirmationDecisionId
outcome = ConfirmationRequired
intentDigest
authenticated preparing AccountId/CharacterId
target RelationshipId/release-authority revision
warningToken
issuedAtServerTime
bound revisions/policy version
```

This decision is not a gameplay receipt and changes no relationship, Connection, maturity, or grace state. It is stored
in the operation-result store so the same preparation operation replays the exact challenge after disconnect or restart.
A fresh operation ID is always required for the later confirmation mutation.

## Relationship-to-Connection integration

Connection source transitions are part of the existing `CreateBond`, `CreateAttunement`, and
`ReleaseRelationship` logical transaction.

After relationship policy resolves the authoritative role:

- derive every affected account-pair source for that Stone;
- add/remove exact `ConnectionSourceId` records;
- resume frozen grace or start final-link grace as required;
- acknowledge relationship success only when Connection projections are recoverable.

### Final-link warning token

A `ReleaseRelationship` preparation operation that would remove one or more final qualifying sources does not mutate the
relationship. It records and returns one terminal `ConfirmationRequired` decision:

```text
preparationOperationId
confirmationDecisionId
warningToken
releaseIntentDigest
preparedByAccountId
preparedByCharacterId
targetRelationshipId
releaseAuthorityRevision
issuedAtServerTime
relationshipRevision
gracePolicyVersion = 72-hours-from-confirmation
affected[] ordered canonically by ConnectionId
  connectionId
  sourceIdsToRemove[]
  previewTierAtIssue
  previewConnectedAgeAtIssue
  connectionRevision
orderedSetDigest
```

The signed challenge binds the preparation operation, decision, exact release-intent digest, authenticated preparing
AccountId/CharacterId, target RelationshipId/release-authority revision, complete canonical source set,
relationship/Connection revisions, and grace-policy version. Tier and age are issue-time warning previews only; they are
not commit inputs. The challenge has no independent wall-clock expiry, so ordinary connected-age advancement alone does
not stale it. Possession of the client-carried token grants no authority.

Confirmation is a separate `ConfirmReleaseRelationship` mutation with a **fresh** `confirmationOperationId`; reusing the
preparation operation ID with the changed confirmation payload is `OperationConflict`.

```text
confirmationOperationId
preparationOperationId
confirmationDecisionId
warningToken
releaseIntentDigest
targetRelationshipId
expected release-authority/relevant revisions/policy version
```

On confirmation the server:

1. loads the durable preparation decision and rejects a mismatched preparation/decision/token/intent/target binding;
2. requires the authenticated confirming AccountId/CharacterId to equal the decision's preparing principal; a different
   token holder is `FinalLinkConfirmationPrincipalMismatch`;
3. revalidates that same Character is still the active holder of the target Bond/Attunement and still has ordinary
   voluntary-release authority at the bound release-authority revision; otherwise `RelationshipReleaseUnauthorized`;
4. rejects any changed set/order, omitted Connection, changed bound revision, changed policy version, or challenge already
   consumed by another confirmation operation;
5. reconciles every still-bound Connection through confirmation `receivedServerTime` while it is still Active;
6. freezes that confirmation-time age and sets `graceExpiresAt = receivedServerTime + 72 hours`;
7. atomically journals token consumption, relationship release, every source removal/grace transition, and the successful
   confirmation receipt before acknowledging success.

The preparation decision remains a non-gameplay operation result. Token consumption is never a separate pre-commit
mutation: a crash before the atomic confirmation commit leaves the challenge unconsumed; a crash after commit recovers
the release and receipt together. Replaying the same confirmation operation returns that recorded receipt after restart.
A different confirmation operation racing on the same challenge either loses on expected revisions or receives
`FinalLinkConfirmationConsumed` with the winning receipt correlation; it cannot apply twice. A stale/invalid confirmation
changes no gameplay state and the caller must prepare a new challenge.

This guarantees a delayed but otherwise current confirmation never loses maturity and always receives a full 72-hour
grace while preserving the accepted rule that ordinary rejections are not receipt-bearing gameplay mutations.

## `SelectDonationMenu`

**Caller:** authenticated active Bond carrying the server-authored owner role.
**Payload:** current Stone Level, candidate-pool ID/version, exactly two distinct DonationOption IDs/versions.

**Validates:**

- current active Bond carrying the server-authored owner role;
- exact Stone Level and menu/candidate-pool version;
- candidate pool contains at least two distinct valid options;
- both selected options are current, level-appropriate, applicable, and distinct;
- for Level 2, pool is `20 Wood`, `20 Stone`, `10 Wood + 10 Stone`, default pair is `20 Wood` + `20 Stone`;
- menu is not already locked for this level;
- expected Stone/menu revisions and replay binding.

**Commits:** one stable Donation Menu and selection provenance. Concurrent owner-role selections serialize by expected
revision; one wins and stale rivals reject. It spends no AP/BP and moves no items.

If upkeep is requested before a valid selection, the server MUST materialize the versioned authored default pair through
one idempotent internal operation before evaluating the donation. `DonationMenuNotSelected` is reserved for missing,
stale, or invalid authored default content; ordinary uninitialized state is not an indefinite rejection. The default is
content, not a random choice.

## `SubmitUpkeepDonation`

**Caller:** authenticated Account/Character with a current Bond or Attunement to the Stone and at least one current
qualifying Connection at that Stone.
**Payload:** Donation Menu/version, selected DonationOption/version, expected participation/Stock/player-inventory
revisions.

The client does not submit trusted item quantities. The server resolves the exact authored item vector.

**Before validation:** reconcile Stone outcomes through `[lastCursor, receivedServerTime)` under the old participation and
capacity state. A current pending generated delivery has first priority on capacity; later donations reject until it
deposits.

**Validates:**

- authenticated relationship and qualifying Connection;
- current menu and selected option;
- current item catalog and exact authored positive quantity vector;
- server-observed player inventory contains the full vector;
- weekly completion operation is not already bound differently;
- complete vector fits current Stone Stock capacity;
- expected revisions.

**Atomic/recoverable commit:**

- debit the full authored vector from player inventory;
- deposit the same vector into Stone Stock with donation provenance;
- record weekly upkeep completion and rolling seven-day expiry effective at received server time;
- record player/participation/Stock revisions and terminal result.

Any validation or write failure changes none of the three state owners. Same operation replays one result.

## `RecordDailyPractice`

Internal server/application contract used by an objective provider. It is not a client-callable award command.

```text
DailyPracticeEvidence
  operationId
  authenticatedActor
  stoneId
  objectiveDefinitionId/version
  stableFoundationalPieceId/version
  physicalInstanceId
  placementSucceeded
  insideStoneArea
  observedAtServerTime
  sourceProvenance
```

Before changing cycle progress, reconcile `[lastCursor, receivedServerTime)` under the old daily completion/expiry.
The first fixture accepts only distinct physical eligible Foundational placements inside the Stone Area.

**Validates:** current relationship and qualifying Connection; current objective definition; authoritative actor/source;
exact catalog membership/Stone Area/physical-instance uniqueness; current content; replay binding.

**Commits:**

- while a completed daily window remains current: one idempotent no-progress result;
- after expiry: first eligible event opens a fresh zero-progress cycle, then counts itself;
- before completion: persist the new distinct instance/progress and receipt;
- on the fifth distinct instance: close the cycle and start a rolling 24-hour expiry effective at received server time.

For the first fixture, a qualifying Foundational placement that is also an AP source MUST use one combined terminal
operation with this fixed order after elapsed reconciliation:

1. validate the placement evidence and the AP source's ordinary authorization independently;
2. snapshot/compute the AP subresult from the participation state that existed before this placement's practice mutation
   (an ineligible AP source yields no AP but does not suppress otherwise-valid practice evidence);
3. apply the placement to daily-cycle progress and, on the fifth distinct instance, make `2×` current immediately after
   the AP subresult at the same server timestamp/next durable receipt sequence;
4. commit the AP/no-AP subresult and practice mutation atomically under one terminal receipt.

Therefore the fifth placement never boosts its own AP: it uses the prior `0×/1×` tier. Only later events and elapsed
intervals use the new `2×` tier. Replay/restart returns that same ordered combined result.

## `ApplyBPToNode` extension

Extend the accepted BP-development command rather than inventing a second Resource Delivery wallet or spend path.

For a Resource Delivery node it additionally validates:

- outcome/ownership is Stone-owned Resource Delivery;
- active Bond;
- authored total BP cost, current progress, positive requested delta, and delta ≤ remaining cost;
- for Foundational: any active Bond, no Facet/Responsibility Range, and no committed-Tree investment;
- for Profession/Martial: committed owning Tree, development authority, and Responsibility Range;
- Stone/Tree Level and current node/content version;
- expected Stone/character revisions.

**Commits:** personal BP debit plus equal node-development delta. Concurrent developers serialize by expected Stone/
character revision; one wins and stale rivals spend nothing. Profession/Martial commits the same delta to cumulative
owning-Tree investment and ordinary Tree-Level threshold logic; Foundational commits no Tree investment. Completion at
authored cost activates the Stone-owned node and provenance. Partial progress does not activate it. It never creates a
personal Offered purchase or Attunement Tier Access credit. Humble's authored cost is 1 BP.

## `RevokeTree` extension

The accepted committed-Tree revocation contract remains authoritative. Before revocation mutates node state, reconcile
Resource Delivery through `[lastCursor, receivedServerTime)` under the old composition. The atomic revocation then:

- deletes that Tree's Resource Delivery development/activation records;
- refunds no BP;
- excludes those nodes from future bundle composition;
- preserves Stone-wide meter progress, freezing it if no non-empty bundle remains;
- preserves immutable pending bundles and deposited Stock;
- ensures recommitment starts those nodes undeveloped.

Foundational Resource Delivery is outside committed-Tree revocation. Replay/recovery returns the recorded deletion result
without resurrecting or refunding development.

## `RecordApActivity` / existing AP receipt extension

Every AP-producing adapter continues to submit server-observed activity through the existing AP command/receipt seam.
The source's existing actor/relationship authorization runs first and is not widened by this package. For an
otherwise-authorized award, application policy then requires a current qualifying Connection and current weekly
participation and resolves:

- current weekly/daily participation tier;
- current qualifying Connections at the target Stone;
- strongest maturity band once;
- authored base AP and multiplier-policy version;
- final award = floor(base × participation × maturity).

The accepted receipt snapshots all inputs and final award. Personal AP and Cumulative AP receive the final award.
BP is unchanged. The first behavior slice MUST write the same final award to the compatibility Mirrored projection as
telemetry and include it in the same receipt/recovery result. Retry returns the recorded award even when participation
later expires. Resource Delivery never reads the Mirrored total.

A `0×` result is a recorded authorized no-award result where useful for audit; it creates no AP delta and does not
advance Stone outcomes merely because an AP command was attempted.

## `ReconcileStoneContribution`

Internal application command triggered by relevant Stone load/read/mutation/withdrawal and bounded checkpoints. It is
not client-callable evidence.

**Inputs:** Stone identity, last cursor, current server time, current relationship/Participation/Connection/content/
outcome/Stock revisions.

**Behavior:**

1. integrate `[lastCursor, receivedServerTime)` under the prior state before any same-time rate/eligibility/composition/
   dormancy/capacity mutation; durable receipt order breaks same-time ties;
2. derive only Accounts satisfying active Bond/Attunement + qualifying Connection at this Stone + nonzero participation;
3. select each eligible Account's strongest current Connection once;
4. split elapsed time at participation, maturity, grace, lifecycle, and capacity boundaries;
5. apply exact contribution to every applicable Stone outcome;
6. while capacity permits, complete/deposit multiple Delivery cycles and retain threshold excess as residual progress;
7. if a non-empty bundle cannot fit, persist immutable `PendingCapacity`, freeze Resource Delivery at that effective
   time, and discard later Resource Delivery accrual while sibling outcomes continue;
8. record new cursors, revisions, cycle results, and terminal receipt.

Same cursor and inputs are idempotent. Renewal after expiry first preserves the old expired interval, then becomes
effective at received server time. Negative elapsed time contributes zero and returns a bounded clock-anomaly code.

## `GrantStockWithdrawalPermission`

**Caller:** authenticated active Bond carrying the server-authored owner role; Responsibility Range does not apply.
**Payload:** grantee AccountId selected through server-resolved identity, expected canonical permission/Stock revisions.

**Validates:** current owner-role Bond, grantee identity, same world/product, canonical `(StoneId, grantee AccountId)`
record, inactive/absent current state, revisions, and replay binding. This slice has no permission expiry. A same-operation
replay returns its recorded result; a new grant against an already-active record—including same payload—rejects
`StockPermissionAlreadyActive`. Concurrent owner-role changes serialize by expected revision; one wins and stale rivals reject.

**Commits:** create generation 1 when absent, or increment generation and reactivate after prior revocation. One canonical
record remains. It grants only Stock withdrawal. It does not grant Bond, Attunement, node development, donation-menu
selection, build rights, AP/BP, or transitive authority.

## `RevokeStockWithdrawalPermission`

**Caller:** authenticated active Bond carrying the server-authored owner role; Responsibility Range does not apply.
**Payload:** canonical `(StoneId, grantee AccountId)` identity and expected current permission/Stock revisions.

**Validates:** exact active canonical record/generation, current owner-role authority, revisions, and replay binding.

**Commits:** mark the canonical record `Revoked` with terminal provenance. Because there is one record and one active
generation, revocation removes all current delegated authority for that grantee. In-flight or later withdrawals revalidate
current state before Stock mutation; stale UI tokens and earlier generations cannot complete.

## `WithdrawStoneStock`

**Caller:** authenticated active Bond or active delegated withdrawal grantee.
**Payload:** exact stable item/quantity vector requested, expected Stock and player-inventory revisions.

**Before validation:** reconcile Stone outcomes through `[lastCursor, receivedServerTime)` under pre-withdraw capacity.

**Validates:**

- current Bond or explicit permission;
- non-transitive Stone scope;
- positive current item identities and quantities;
- Stock contains the entire requested vector;
- authoritative player inventory can accept the entire vector;
- expected revisions and replay binding.

**Atomic/recoverable commit:** debit requested Stone Stock, credit the player's inventory, record source/result and
revisions. The caller may request less than all available Stock, but the request is never auto-partially fulfilled.

After withdrawal is recoverable, application policy MUST attempt any immutable pending delivery idempotently before a
later donation may consume the freed capacity. Pending deposit success/failure and resulting cursor/Stock revisions are
recorded in deterministic receipt order; paused wall time is not banked.

## Generated delivery deposit

Resource Delivery completion is an internal operation, not a client claim.

```text
DeliveryBundleSnapshot
  deliveryCycleId
  stoneId
  threshold/content versions
  source ResourceDeliveryNode IDs/versions
  exact stable item/quantity vector
  completedAtServerTime
  contribution cursor/provenance
```

If no active node contributes a non-empty bundle, Resource Delivery is Dormant and preserves in-progress progress. If a
non-empty complete vector fits, deposit it, retain threshold excess, and continue further cycles while elapsed
contribution/capacity permit. If not, persist the immutable snapshot as pending at the exact crossing time and discard
later Resource Delivery accrual until deposit. A capacity-releasing withdrawal MUST retry this snapshot before later
donations. Every retry uses the snapshot, not current node definitions.

## Read contracts

### `GetStoneProgressionView` extension

For the authenticated caller, add:

- weekly upkeep completion/expiry and current Donation Menu options;
- daily-practice completion/expiry;
- current participation tier;
- strongest Connection maturity tier and final-link grace deadline, with other-account raw identity filtered;
- effective multiplier and reasons for `0×`/lower rate;
- Resource Delivery nodes, BP price, authority, bundle contribution, and active/dormant state;
- current composed next bundle;
- Resource Delivery progress/threshold/status/pending reason/last deposit;
- Stone Stock item counts/capacity;
- caller withdrawal authority and request feasibility;
- actionable rejection/localization keys.

### Operator diagnostics

Bounded operator views include:

- Connection counts by lifecycle/tier and grace expiries;
- stale/invalid source counts;
- Participation states and objective-definition versions;
- Resource Delivery cursors, pending bundles, and pause durations;
- Stock capacity/invariant/quarantine summaries;
- terminal receipt/recovery status and stable pseudonymous internal IDs.

Do not print raw provider subjects, item instance secrets, full player inventories, or unbounded journals.

## Provider seams

| Provider | Owns |
|---|---|
| Account/character resolver | authenticated internal AccountId and acting CharacterId |
| Relationship/authority resolver | current Bond/Attunement roles, source relationships, development/delegation authority |
| Server clock | authoritative current time and clock-anomaly policy |
| Objective evidence provider | server-observed daily-practice completion facts |
| Player inventory transaction provider | exact debit/credit and fit under one recoverable operation boundary |
| Content registry | age bands, objectives, donation pools/defaults, node/bundle/threshold/capacity policies |
| Contribution reconciler | pure exact elapsed integration and bundle completion |
| Stock persistence | versioned Stone Stockpile state/projections |
| Receipt/recovery store | operation binding, durable decisions/results, successful receipts, journal/replay/quarantine |

Libraries own these nouns and validation seams; server/product configuration owns tunable values and shipped content.

## Stable result/rejection vocabulary

| Code | Meaning |
|---|---|
| `ConnectionSelfPair` | Account pair resolves to the same account |
| `ConnectionSourceNotQualifying` | Role pairing is not Bonded↔Attuned or Bonded↔Bonded |
| `FinalLinkConfirmationRequired` | Preparation terminated with a durable non-mutating challenge; use a fresh confirmation operation ID |
| `FinalLinkConfirmationStale` | Canonical affected set/order/revision or grace-policy version changed, an affected Connection was omitted, or preparation/decision/intent/target binding mismatched |
| `FinalLinkConfirmationPrincipalMismatch` | Authenticated confirming AccountId/CharacterId differs from the preparing principal bound to the decision |
| `RelationshipReleaseUnauthorized` | Bound principal is no longer the active holder of, or authorized to voluntarily release, the target relationship |
| `FinalLinkConfirmationConsumed` | Another confirmation operation already committed this challenge; winning receipt correlation is returned |
| `WeeklyUpkeepRequired` | Participation is `0×` because weekly upkeep is not current |
| `DailyPracticeNotCurrent` | Participation remains `1×`; not an error for ordinary contribution |
| `DonationMenuNotSelected` | Authored default content is missing, stale, or invalid; ordinary uninitialized state materializes default instead |
| `DonationOptionNotAccepted` | Requested option is not in the current menu/version |
| `DonationItemsMissing` | Authoritative inventory lacks the complete donation vector |
| `StoneStockCapacityExceeded` | Complete deposit vector cannot fit |
| `ResourceDeliveryPendingCapacity` | Immutable generated bundle is waiting for capacity |
| `PendingDeliveryPriority` | Donation cannot consume capacity reserved for the current pending generated bundle |
| `ResourceDeliveryBundleEmpty` | No active Resource Delivery node contributes a non-empty bundle |
| `ResourceDeliveryBpDeltaInvalid` | Requested BP delta is non-positive or exceeds remaining node cost |
| `ResourceDeliveryNodePersonalPurchaseForbidden` | Attuned AP purchase attempted for Stone-owned node |
| `ResourceDeliveryAuthorityRequired` | Bond/development authority is absent |
| `StockWithdrawalUnauthorized` | No current Bond or active canonical delegated withdrawal permission |
| `StockPermissionAlreadyActive` | A new grant targeted a grantee whose canonical permission is already active |
| `StockQuantityUnavailable` | Stock lacks the complete requested vector |
| `PlayerInventoryCannotFit` | Player inventory cannot accept the complete requested vector |
| `ObjectiveEvidenceUntrusted` | Completion depends on client-claimed or unattributed facts |
| `ObjectiveDefinitionStale` | Objective ID/version is not current |
| `ContributionClockAnomaly` | Server time moved backward; no negative contribution applied |
| `StaleConnectionRevision` | Expected Connection revision mismatched |
| `StaleParticipationRevision` | Expected Participation revision mismatched |
| `StaleStockRevision` | Expected Stock revision mismatched |
| `OperationConflict` | Operation ID was reused with a different binding/payload |

Common accepted rejection codes for unauthenticated principal, missing Stone/Character, stale Stone/Character revision,
content mismatch, insufficient BP, outside Responsibility Range, replay, and quarantine remain in force.

## Durable versus ephemeral state

Durable: Connection sources/age/grace, Participation completions/expiries, menu selection, node development, meter cursor,
pending bundle, Stock, permissions, successful receipts, final-link preparation decisions, challenge-consumption/winning-
receipt correlation, and quarantine state.

Ephemeral: UI cache, local countdown rendering, adapter observation buffers, inventory previews, and bounded retry queues.
Restart may discard ephemeral caches but MUST replay the exact preparation decision or confirmation receipt from durable
operation results. The warning token is client-carried/signed, but its decision binding and consumption outcome are not
lost on restart.

## Contract non-goals

- No client-callable `GrantAP`, `AdvanceDelivery`, `CompleteObjective`, `SetConnectionAge`, or direct Stock mutation.
- No quest-journal API, narrative quest schema, physical chest, mail, world-drop overflow, or group-wide permission.
- No task or implementation authorization.
