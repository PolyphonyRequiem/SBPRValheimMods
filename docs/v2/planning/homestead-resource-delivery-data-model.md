---
title: "Homestead loyalty, upkeep, and Resource Delivery — data model"
status: proposed
purpose: Define identities, authoritative aggregates, invariants, transitions, recovery, and derived views for the proposed feature.
---

# Homestead loyalty, upkeep, and Resource Delivery — data model

**Companion spec:** [`homestead-resource-delivery-spec.md`](homestead-resource-delivery-spec.md)
**Research boundary:** [`homestead-resource-delivery-research.md`](homestead-resource-delivery-research.md)

## Modeling rules

1. Account-pair loyalty, per-account Stone participation, character AP, and Stone-owned outcomes remain distinct state owners.
2. Persist accepted facts, timestamps, selections, cursors, balances, receipts, and provenance; derive current multipliers and eligibility.
3. Client clocks, client completion claims, display names, live objects, and network ownership are never authority.
4. Every mutation is authenticated, revision-checked, idempotent, auditable, and recoverable.
5. Completed bundles and deposits snapshot content identity; later content or Tree state never rewrites history.
6. No floating-point accumulation is authoritative. Multipliers and outcome progress use exact fixed-point or rational units.
7. Unknown or stale current-build references reject. Unreleased incompatible fixtures may be explicitly reset; production migration is deferred.

This is a logical model, not a prescribed C# class or storage engine.

## Stable identities

| Identity | Logical shape | Scope/rule |
|---|---|---|
| `ProductScope` | stable authored product/runtime discriminator | Prevents another product sharing Account IDs from joining this Connection graph |
| `ConnectionId` | canonical unordered `(WorldId, ProductScope, lower AccountId, higher AccountId)` | One account-pair loyalty aggregate; self-pairs reject |
| `ConnectionSourceId` | `(StoneId, lower RelationshipId, higher RelationshipId, sourceVersion)` | One qualifying Stone role-pair source; stable through replay |
| `ParticipationId` | `(WorldId, ProductScope, AccountId, StoneId)` | One account's upkeep/practice state at one Stone |
| `ObjectiveDefinitionId/Version` | stable authored content identity | Weekly upkeep or daily practice definition |
| `DonationMenuId/Version` | `(StoneId, StoneLevel, catalogVersion)` | One selected/default acceptable-option menu for a Stone Level |
| `DonationOptionId/Version` | stable authored recipe identity | Exact required item vector and eligibility |
| `ResourceDeliveryNodeId/Version` | node identity in the existing Tree registry | Stone-owned BP-developed bundle contribution |
| `DeliveryCycleId` | Stone-scoped monotonic cycle identity | One Resource Delivery completion/pending/deposit lifecycle |
| `BundleSnapshotId` | `(DeliveryCycleId, contentVersion)` | Exact composed items and source nodes at completion |
| `StockpileId` | `StoneId` | One Stone Stockpile |
| `StockOperationId` | existing operation identity bound to exact payload | Donation, generated deposit, permission, or withdrawal idempotency |
| `ConfirmationDecisionId` | preparation `OperationId` + release-intent digest | Durable non-gameplay decision linking final-link preparation to confirmation |
| `WithdrawalPermissionId` | `(StoneId, grantee AccountId)` | One canonical current delegated-authority record; generation/revision carry history |

Existing `WorldId`, `StoneId`, `AccountId`, `CharacterId`, `RelationshipId`, `TreeId/Version`, `NodeId/Version`,
`OperationId`, and `ReceiptId` retain their accepted meanings.

## Aggregate 1 — ConnectionAggregate

One server-owned aggregate per `ConnectionId`.

### State

| Field group | Required logical state |
|---|---|
| Envelope | schema version, ConnectionId, revision, created/updated receipt provenance |
| Accounts | canonical internal AccountId pair; no raw provider subjects in presentation |
| Sources | active `ConnectionSourceId` set with Stone, relationship IDs, role pairing, activation provenance |
| Lifecycle | `Active`, `Grace`, or `Reset`; active-since/current-segment timestamp; frozen accumulated duration |
| Grace | final-link removal receipt/time, grace expiry, warning/confirmation provenance |
| History | last add/remove/reconnect/reset results sufficient for audit and replay |

### Invariants

- A Connection has exactly two distinct accounts in canonical order.
- Every active source resolves to one current Bonded↔Attuned or Bonded↔Bonded pair at one Stone.
- Attuned↔Attuned and all social/indirect edges are invalid sources.
- Several sources maintain one Connection; source count does not multiply maturity.
- `Active` requires at least one source and advances age from server time.
- `Grace` requires zero sources, freezes age, and has one 72-hour expiry.
- Adding a valid source during Grace clears grace and resumes from frozen age.
- Grace expiry resets accumulated age to zero and records terminal provenance.
- Negative elapsed time contributes zero and emits an operator-visible clock anomaly; client time never advances age.

### Derived maturity

| Accumulated connected duration | Multiplier |
|---|---:|
| `<1 day` | `1.0×` |
| `1–<7 days` | `1.1×` |
| `7–<30 days` | `1.2×` |
| `30–<60 days` | `1.3×` |
| `60–<90 days` | `1.4×` |
| `≥90 days` | `1.5×` |

## Aggregate 2 — AccountStoneParticipationAggregate

One server-owned status aggregate per `ParticipationId`. It owns eligibility timestamps and objective receipts, not a
wallet or character progression.

### State

| Field group | Required logical state |
|---|---|
| Envelope | schema version, ParticipationId, revision, current catalog version |
| Weekly upkeep | latest accepted option/definition/version, completion time, expiry, donation receipt |
| Daily practice | durable cycle ID/status, opened time, distinct physical-instance set/progress, latest completion time, rolling 24-hour expiry, evidence receipts |
| Relationship view token | last relationship/Connection revisions used for current contribution projection |
| Recovery | last applied operation/result and quarantine notices |

### Invariants

- Weekly upkeep is current for a rolling seven days from accepted completion.
- Daily practice is current for 24 hours from accepted completion, but produces `2×` only while weekly upkeep is current.
- While daily practice is current, further placement evidence does not pre-build the next cycle. After expiry is
  reconciled, the next eligible event opens a fresh zero-progress cycle with an empty distinct-instance set.
- Each physical Foundational placement instance counts at most once in one cycle; partial progress and instance IDs are
  durable across restart.
- Repeated weekly completion refreshes its latest timestamp; it does not stack another multiplier. Before replacing
  old weekly state, the owning command MUST reconcile `[lastCursor, receivedServerTime)` under the old completion/
  expiry so lapse and renewal intervals remain recoverable without retaining a full event timeline.
- Same-server-time mutations are ordered by durable receipt sequence; zero elapsed time exists between them.
- Participation status does not itself prove a current Stone relationship or qualifying Connection; those are revalidated.
- Character switching does not duplicate account participation. AP remains credited only to the authenticated acting Character.

### Derived participation multiplier

| Condition | Multiplier |
|---|---:|
| weekly upkeep expired/missing | `0×` |
| weekly upkeep current, daily practice expired/missing | `1×` |
| weekly upkeep current and daily practice current | `2×` |

## Aggregate 3 — StoneOutcomeAggregate extension

Extend the existing Stone aggregate with Stone-owned Resource Delivery and donation-selection state.

### Donation menu

| Field | Rule |
|---|---|
| Stone Level/content version | Menu belongs to one exact level/catalog view; preconfigured Level-2 fixtures start uninitialized and consume the same selection/default transition |
| Candidate pool | At least two distinct authored level-appropriate options contributed by applicable Trees/content; Level-2 Humble pool = `20 Wood`, `20 Stone`, `10 Wood + 10 Stone` |
| Selected options | Exactly two distinct current options; Level-2 default pair = `20 Wood` and `20 Stone` |
| Authority provenance | Selecting authenticated active Bond carrying the authored owner role, or deterministic default operation, plus receipt |
| Lifecycle | Stable for that Stone Level; replacement only by a later accepted level/menu transition |

An option is a versioned positive item-count vector. Arbitrary client-authored item IDs, quantities, or display names reject.

### Resource Delivery node state

A Resource Delivery node is Stone-owned and records:

- owning Tree/node/version and current status;
- authored BP cost, accumulated node-development progress, and authority requirements;
- authored bundle contribution item vector;
- acquisition operation/actor and BP debit provenance;
- activation/dormancy status; committed-Tree revocation is a deletion event, not a retained purchased flag.

Foundational nodes require any active Bond but no Facet/Responsibility Range. Positive BP increments are capped at the
remaining authored cost, advance only Foundational node progress, and create no committed-Tree investment. The Humble
fixture costs 1 BP. Profession/Martial nodes require the owning committed Tree and ordinary development authority/
Responsibility Range; every accepted BP delta advances node progress and the same delta in cumulative owning-Tree
investment, including ordinary Tree-Level threshold crossing. Completion—not the first partial spend—activates the node.
Committed-Tree revocation reconciles first, then deletes that Tree's Resource Delivery node progress/activation with no
BP refund, matching accepted node-development lifecycle. Recommitment creates fresh undeveloped state. Foundational
nodes are not part of committed-Tree revocation.

### Resource Delivery meter

| Field | Rule |
|---|---|
| Threshold | versioned exact contribution units; Humble fixture = 24 baseline contribution-hours |
| Progress cursor | exact accumulated contribution units and last reconciled server time |
| Contributor cursor set | last accounted participation/Connection revisions per eligible Account |
| Status | `Accruing`, `PendingCapacity`, or `Dormant` |
| Pending bundle | optional immutable BundleSnapshotId, item vector, source node/version set, completion receipt/time |
| Last deposit | cycle/deposit receipt and resulting Stockpile revision |

### Invariants

- One Stone has one Resource Delivery meter.
- Every eligible Account contributes once using its current participation multiplier and strongest applicable Connection multiplier.
- Other Stone outcomes consume the same contribution stream independently.
- Adding a Resource Delivery node changes future bundle contents, never threshold, unless an explicit speed effect exists.
- Completion snapshots the exact active node contributions before any deposit attempt.
- `PendingCapacity` banks no additional Resource Delivery time/progress; sibling outcomes continue.
- While capacity allows, one reconciliation may complete/deposit several cycles and MUST carry threshold excess into
  the next cycle as residual progress.
- Capacity release MUST attempt the immutable pending bundle idempotently before a later donation may consume newly
  freed capacity. Successful deposit resumes at the deposit effective time; paused wall time is not banked.
- If no active Resource Delivery node contributes a non-empty bundle, status is `Dormant`: reconcile to the change
  time, freeze/preserve in-progress progress, complete no empty cycle, and resume preserved progress on reactivation.
- Committed-Tree revocation deletes that Tree's development/acquisition without refund and changes future composition
  only. Recommitment starts fresh. Pending snapshots and deposited Stock never mutate.

## Aggregate 4 — StoneStockpileAggregate

One authoritative virtual store per `StockpileId`.

### State

| Field group | Required logical state |
|---|---|
| Envelope | schema version, StoneId, revision, content/item-catalog version |
| Capacity policy | maximum distinct item kinds, maximum total units, optional authored per-item caps; provisional Stone-Level-2 defaults = 16 kinds / 1,000 total / 500 per item |
| Stock | stable item identity → non-negative quantity |
| Provenance | bounded per-operation donation/generated/withdrawal receipts plus aggregate audit counters |
| Delegation | canonical WithdrawalPermission record per grantee: generation, revision, `Active`/`Revoked`, grant/revoke owner-role provenance; no expiry in slice |
| Recovery | last terminal result, invariant scan, quarantine notices |

### Invariants

- Stock quantities and capacities are non-negative.
- One complete deposit vector either fits and applies once or changes nothing.
- One requested withdrawal vector either exists, fits the authenticated player's inventory, and applies once or changes nothing.
- A request may choose partial quantities from available Stock; the requested vector itself is atomic.
- Active Bond authorizes withdrawal without inferred Governor Responsibility Range.
- Delegation is explicit, Stone-scoped, non-transitive, and revalidated at withdrawal.
- Only an active Bond carrying the server-authored owner role may grant/revoke; Responsibility Range does not apply.
- One canonical `(StoneId, grantee AccountId)` record exists with at most one active generation. Regrant after revocation
  increments generation; active duplicate/different-payload grants reject rather than fork authority.
- Expected revision serializes concurrent owner-role changes; stale change rejects. Revocation deactivates the canonical
  record and therefore all current delegated authority for that grantee. This slice has no permission expiry.
- Attunement, group membership, or another grantee never implies delegation.
- No physical chest, world drop, client inventory prediction, or UI cache is authoritative Stock.

## Aggregate 5 — CharacterProgressionAggregate extension

Personal AP and Cumulative AP Earned remain character-owned per Stone.

An otherwise-authorized AP award receipt additionally snapshots:

- AP source definition/version and the accepted source-specific actor/relationship authorization decision;
- authored base AP and content version;
- ParticipationId/revision and selected `0×/1×/2×` tier;
- chosen ConnectionId/revision and maturity band;
- multiplier-policy version;
- final whole-number award after one floor;
- required first-slice Mirrored telemetry delta equal to the recorded final award.

Replay uses the recorded final award. It never recomputes against later participation, maturity, or content state. BP
credit and BP balances remain unmultiplied.

## Aggregate 6 — ContentRegistry extensions

The registry authors and versions:

- Connection age bands and 72-hour grace policy;
- participation tier policy and objective definitions, including first daily practice = five distinct physical eligible Foundational placements inside the Stone Area;
- donation option pools/default pairs by Stone family/level and applicable Tree context, including the exact Level-2 Humble pool/default;
- Resource Delivery node outcome, BP costs/progress, bundle contributions, and exact first-slice roster `21 = 14 executable + 7 unavailable`;
- Resource Delivery threshold and any explicit speed effects;
- Stockpile capacity policy;
- permission display/localization data (not grant/revoke authority);
- multiplier/floor policy and stable rejection/localization keys;
- Humble Homesteader fixture.

Tunable values are data. Identity, exact first-slice operation authority (owner-role menu/delegation, any-Bond
Foundational development/withdrawal, committed-Tree Responsibility Range), atomicity, replay, ownership, and
no-stacking rules are domain-code invariants; content cannot redefine them.

## Aggregate 7 — operation results, receipts, and audit

Every mutation binds operation ID, command type, authenticated principal, Stone, exact payload digest, expected revisions,
schema/content versions, and one stable terminal result.

- Applied/NoOp mutations own a successful receipt and replay that receipt.
- Ordinary rejection remains non-receipt-bearing gameplay state.
- A final-link preparation that needs confirmation owns a durable, non-mutating `ConfirmationRequired` decision keyed by
  its preparation operation ID and release-intent digest. It stores the authenticated preparing AccountId/CharacterId,
  target RelationshipId/release-authority revision, and exact signed challenge/bindings for replay after restart.
- Confirmation is a distinct mutation operation referencing the preparation decision. Its successful journal transaction
  atomically stores challenge consumption, winning confirmation operation/receipt, relationship release, source removals,
  and grace transitions. Consumption never commits ahead of gameplay state.
- Same confirmation operation replay returns the winning receipt. A different operation using a consumed challenge cannot
  mutate and receives the winning receipt correlation. Changed-payload reuse of either operation ID conflicts.

Required result/receipt families:

- qualifying link add and release preparation decision;
- confirmed release/remove/grace/reconnect/reset;
- donation menu selection/default;
- upkeep donation + weekly completion;
- daily-practice completion;
- Resource Delivery BP development/progress/completion;
- elapsed contribution reconciliation/AP award;
- generated bundle completion/deposit/pending release;
- withdrawal permission grant/revoke;
- Stone Stock withdrawal.

A successful terminal receipt may cover several aggregate projections. No success is acknowledged until every required
projection and challenge-consumption outcome is durably recoverable. The non-mutating preparation decision changes no
gameplay projection and is not called a receipt. Append-only journals are not deletion mechanisms; this slice promises
recovery and fixture reset, not selective historical purge.

## Derived Stone read model

For an authenticated caller, extend the current Stone-identity view with:

- caller's active relationship and withdrawal authority;
- caller's weekly/daily status and expiries;
- caller's strongest Connection maturity tier and final-link grace deadline, without exposing the other account's raw identity;
- caller's effective multiplier and actionable reason when contribution/AP is paused;
- Donation Menu options and weekly completion state;
- Resource Delivery node definitions, BP price, authority, active/dormant state, and bundle contribution;
- composed next bundle, threshold/progress, status, pending reason, and last delivery time;
- Stock counts, capacity, and requested-withdraw feasibility;
- bounded audit/rejection information appropriate to caller authority.

The read model is a projection, never an independent state authority.

## Transitions

### Add qualifying loyalty link

1. Validate and build the underlying Bond/Attunement relationship transition without acknowledging it.
2. Resolve every affected account role pair from the proposed authoritative relationship state.
3. Add stable ConnectionSourceIds for qualifying pairings and resume/create canonical Connections as needed.
4. Reconcile affected contribution cursors to received server time under prior state.
5. Atomically commit one recoverable terminal result across relationship and every Connection projection; acknowledge only afterward.

### Remove qualifying loyalty links

1. `ReleaseRelationship` preparation operation resolves the relationship release's canonical ordered set of every
   affected Connection/source transition.
2. If no final source is removed, that preparation operation may apply the ordinary release directly. Otherwise it
   commits no gameplay state and stores one terminal `ConfirmationRequired` decision keyed by preparation operation ID,
   exact release-intent digest, authenticated preparing AccountId/CharacterId, target RelationshipId/release-authority
   revision, set/source IDs, relationship/Connection revisions, and grace-policy version. Replaying the same preparation
   operation after restart returns the same decision/challenge.
3. `ConfirmReleaseRelationship` uses a fresh confirmation operation ID and references the exact preparation operation,
   decision, token, release-intent digest, and target RelationshipId. Reusing the preparation operation ID with
   confirmation payload conflicts.
4. Require authenticated confirming AccountId/CharacterId to equal the preparing principal, then revalidate that same
   Character is still the active holder with ordinary voluntary-release authority at the bound authority revision.
   Token possession alone grants nothing; principal substitution or lost authority rejects without gameplay mutation.
5. Validation also rejects changed membership/order/revisions/policy, a mismatched decision/intent/target, or a challenge
   consumed by another confirmation operation. Ordinary age advancement alone does not stale the challenge.
6. Reconcile every still-bound Active Connection and affected Stone contribution cursor through confirmation
   `receivedServerTime` under prior state.
7. In one recoverable journal transaction, record challenge consumption/winning confirmation receipt and commit the
   relationship release plus every source removal. Connections retaining another source remain Active; every zero-source
   Connection freezes the reconciled confirmation-time age and enters Grace with expiry exactly
   `receivedServerTime + 72 hours`.
8. A crash before that transaction leaves the challenge unconsumed. A crash after it rehydrates release, grace, consumption,
   and receipt together. Same confirmation operation replay returns the receipt; a different operation cannot apply twice.
9. Expiry resets age through an idempotent server transition; reconnect-before-expiry resumes it.

### Select donation menu

1. Reconcile Stone outcomes to received server time under the prior menu/content state.
2. Authenticate an active Bond carrying the server-authored owner role for the current Stone Level.
3. Validate a candidate pool containing at least two distinct options, exact pool/version, and two distinct selections.
4. For Level 2, the pool is `20 Wood`, `20 Stone`, `10 Wood + 10 Stone`; default = `20 Wood` + `20 Stone`.
5. Commit selected menu; conflicting replay or later same-level reroll rejects.
6. If no selection exists when needed, materialize the authored deterministic default pair once.

### Deposit weekly upkeep

1. Reconcile `[lastCursor, receivedServerTime)` under the old participation/Stock capacity state.
2. Authenticate Account/Character and current Stone relationship/qualifying Connection.
3. Validate current menu, selected option/version, weekly window, inventory facts, revisions, and Stock capacity.
4. Reject while a pending generated delivery owns first priority on newly freed capacity.
5. Atomically debit the complete player item vector and credit Stone Stock.
6. Commit the refreshed weekly completion/rolling seven-day expiry effective at received server time and one terminal receipt.

### Record daily practice progress

1. Reconcile `[lastCursor, receivedServerTime)` under the old daily completion/expiry.
2. Validate server-observed placement evidence and the AP source's ordinary authorization independently.
3. For the first fixture's combined placement operation, snapshot/compute AP from the pre-practice participation state;
   source-ineligible AP yields no award but does not suppress otherwise-valid practice evidence.
4. If the prior completion is still current, record an idempotent no-progress practice subresult.
5. If it expired, close/reset the old cycle and let the first eligible event open a fresh zero-progress cycle.
6. Require a distinct physical eligible Foundational placement inside the Stone Area; duplicate instance in this cycle is a no-op.
7. Persist progress/instance provenance. At the fifth distinct instance, close the cycle and start rolling 24-hour expiry
   after the AP subresult at the same timestamp/next receipt sequence; only later events/intervals see `2×`.
8. Atomically commit the ordered AP/no-AP and practice subresults under one terminal receipt; replay returns that order.

### Reconcile contribution and AP

1. Load current relationship, Participation, Connection, Stone outcome, content, and cursor revisions.
2. Integrate `[lastCursor, receivedServerTime)` under the prior state, splitting at every expiry/maturity/lifecycle/
   capacity boundary; same-time mutations are applied afterward in durable receipt order.
3. Apply only Accounts satisfying the complete eligibility rule, once each, to every applicable Stone outcome.
4. Complete/deposit multiple delivery cycles while capacity permits and retain threshold excess as residual progress;
   when pending capacity begins, discard later Resource Delivery time while sibling outcomes continue.
5. For an otherwise-authorized AP activity event, snapshot source authorization and multiplier inputs and floor once;
   the multiplier never creates new source authority.
6. Commit receipt and projections; retry returns recorded result.

### Develop Resource Delivery node with BP

1. Reconcile Stone outcomes to received server time under prior node composition.
2. Authenticate active Bond.
3. Validate Tree/node/version, Stone/Tree level, remaining BP cost, current status, authority, and revisions.
4. Apply a positive BP delta no greater than remaining cost. For Profession/Martial, apply the same delta to cumulative
   owning-Tree investment and ordinary Tree-Level threshold logic. For Foundational, apply no committed-Tree investment.
5. Activate the Stone-owned node only when accumulated progress reaches authored cost.
6. Future bundle composition includes its contribution; current pending/deposited content does not change.

### Revoke committed Tree containing Resource Delivery

1. Reconcile Stone outcomes to received server time under the pre-revocation bundle.
2. Apply accepted Tree revocation atomically: delete that Tree's Resource Delivery node progress/activation and refund no BP.
3. Exclude deleted nodes from future composition; preserve current Stone-wide meter progress, freezing it if no non-empty bundle remains.
4. Preserve immutable pending bundles and deposited Stock.
5. Recommitment creates fresh undeveloped node state; prior development/activation does not resurrect. Foundational nodes are unaffected.

### Complete/deposit Resource Delivery

1. Reconciliation reaches threshold and snapshots the exact non-empty bundle.
2. If the full bundle fits, atomically deposit Stock, carry threshold excess into the next cycle, and continue completing
   further cycles while elapsed contribution and capacity permit.
3. If a bundle does not fit, persist it as immutable `PendingCapacity`, freeze Resource Delivery at that effective time,
   and discard later Resource Delivery accrual until it deposits.
4. A capacity-releasing withdrawal MUST attempt the same pending snapshot idempotently before accepting a later donation.
5. Successful pending deposit starts/resumes at deposit effective time; paused wall time is never retroactively banked.

### Withdraw Stone Stock

1. Reconcile Stone outcomes to received server time under pre-withdraw capacity.
2. Authenticate caller and revalidate active Bond or delegated permission.
3. Validate requested stable item vector, Stock revision, available quantities, and full player-inventory fit.
4. Atomically debit Stock and credit player inventory.
5. After withdrawal is recoverable, MUST attempt any immutable pending delivery idempotently before a later donation may
   consume the freed capacity; commit the resulting Stock/pending cursor state in deterministic receipt order.

## Lifecycle and recovery table

| Event | Connection/participation | Delivery/Stock |
|---|---|---|
| clean logout | durable status unchanged; offline accrual continues | unchanged |
| restart | replay receipts, then reconcile elapsed server time | pending/deposited state rehydrates exactly |
| final link removed | enter frozen 72-hour grace after warning | account stops contributing when no qualifying link |
| reconnect in grace | resume preserved age | contribution resumes if participation is current |
| grace expires | reset Connection age | no retroactive effect on existing Stock |
| weekly expiry | participation becomes `0×` | future contribution/AP pauses; Stock persists |
| daily expiry | participation falls from `2×` to `1×` if weekly current | elapsed boundary reconciles exactly |
| committed-Tree revoke | reconcile, then delete that Tree's Resource Delivery progress/activation with no BP refund; recommit starts fresh; Foundational unaffected | Stone-wide meter progress persists/freezes if empty; pending/deposited contents persist |
| Stock full | participation/other outcomes remain valid | Resource Delivery alone pauses with immutable pending bundle; pending has first claim on freed capacity |
| delegation revoked | participation unaffected | later delegated withdrawals reject |
| incompatible unreleased schema | quarantine or explicit fixture reset | never silently reinterpret IDs/counts |

## Validation and repair

Invariant scans MUST detect at least:

- invalid/self/noncanonical Connection pair;
- nonqualifying or stale Connection source;
- Active-with-zero-sources or Grace-with-sources;
- negative/future-inconsistent accumulated age or expiry;
- overlapping/stacked participation tiers;
- duplicate account contribution cursor;
- unknown/stale objective, donation option, node, bundle, or item identity;
- negative Stock, capacity overflow, partial deposit, or mismatched provenance;
- pending bundle changed by later node/content state;
- AP receipt result inconsistent with recorded base/multiplier/floor;
- unauthorized or transitive delegated permission.

Repair is receipt-derived where possible; ambiguous state quarantines. This proposed slice does not promise selective
compaction or deletion from append-only journals/backups.
