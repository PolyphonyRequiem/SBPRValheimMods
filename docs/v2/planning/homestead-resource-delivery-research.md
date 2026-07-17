---
title: "Homestead loyalty, upkeep, and Resource Delivery — research"
status: proposed
purpose: Ground the proposed feature against the accepted Homestead progression package, current source, test seams, and unresolved implementation risks.
---

# Homestead loyalty, upkeep, and Resource Delivery — research

**Date:** 2026-07-16
**Companion spec:** [`homestead-resource-delivery-spec.md`](homestead-resource-delivery-spec.md)

## Research verdict

The design is coherent on the accepted Homestead identity, relationship, Tree, BP, command, and receipt substrate.
It is not a small content-only addition: current source has no account-pair Connection, participation objective,
Resource Delivery outcome, Stone Stockpile, delegated withdrawal authority, or multiplier-aware AP receipt.

The safest implementation shape is one vertical extension of the existing command/receipt/read-model pipeline. The
feature MUST NOT become independent timers, a second progression store, or direct adapter writes. The first gate is a
pure deterministic reconciliation spike proving elapsed-time integration across participation, Connection-age,
capacity, and restart boundaries while preserving existing receipt idempotency.

## Sources consulted

| Source | Role |
|---|---|
| Daniel-approved Resource Delivery decision synthesis, 2026-07-16 | Product decisions and first Humble fixture |
| Daniel-approved Homestead loyalty/Resource Delivery ubiquitous language | Canonical terms and corrected distinctions |
| [`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md) | Accepted relationship, AP/BP, Tree, node, lifecycle, and interface authority |
| [`homestead-stone-progression-data-model.md`](homestead-stone-progression-data-model.md) | Existing aggregate boundaries, stable identities, and invariants |
| [`homestead-stone-progression-contracts.md`](homestead-stone-progression-contracts.md) | Existing authenticated command, evidence, receipt, and read seams |
| [`homestead-stone-progression-plan.md`](homestead-stone-progression-plan.md) | Existing tracer order and writer≠verifier gates |
| [`../../decisions/0005-spec-kit-adoption-v2.md`](../../decisions/0005-spec-kit-adoption-v2.md) | SBPR-native Spec Kit shape and approval separation |
| [`../../design/constitution.md`](../../design/constitution.md) | Spec-first, runtime conformance, writer≠verifier, and evidence principles |
| Current `origin/main` at package authoring (`dcc7c2f`) | Current source/test reality; docs-only base |

## Current accepted behavior

### Existing and reusable

- Stable `WorldId`, `StoneId`, `AccountId`, `CharacterId`, `RelationshipId`, Tree/node/content versions, operation IDs,
  receipt IDs, and revisioned aggregate patterns already exist.
- Bond/Attunement lifecycle and the account–Stone active-character authority index are implemented behind
  authenticated commands and durable receipts.
- A Bond carries a server-authored owner/governor role and Responsibility Range. Attunement carries no cultivation
  authority.
- Foundational placement has a real joined-runtime observation path through the same adapter/command/receipt flow used
  by tests.
- The append-only AP journal is authoritative; character AP and world Mirrored AP are idempotent projections replayed
  from committed records on restart.
- The current content registry is engine-free and versioned. It already separates Tree identity, node outcome,
  ownership, pricing, requirements, and status.
- Existing domain, contract, live-runtime, and restart/rehydration tests are the correct prior art.

### Missing

Source inspection found no implementation of:

- an account-pair Connection aggregate or qualifying-link projection;
- maturity bands, final-link warning, or 72-hour frozen grace;
- weekly upkeep, daily practice, participation expiry, or elapsed offline contribution;
- a generic Stone outcome contribution stream;
- Resource Delivery node outcome/bundle definitions;
- Stone Stockpile, donation deposit, delivery deposit, or stock withdrawal;
- delegated withdrawal permissions;
- a Stone-level donation menu;
- AP multipliers or floor-after-full-multiplier receipt fields;
- a Resource Delivery/Stock read model.

## Exact current-code constraints

### Mirrored AP is coupled to the proof, not gameplay

The AP journal and recovery view currently carry fixed integer Personal, Cumulative, and Mirrored deltas. The
Mirrored projection is written to a Stone ZDO and also supplies the AP-operation-derived Stone revision. Recovery and
negative-ledger checks inspect it. No gameplay node, threshold, Facet, or public progression read uses its value.

**Consequence:** Resource Delivery must not read Mirrored AP. Removing Mirrored AP in the same slice would require
relocating revision and changing proven receipt/recovery contracts. The smallest safe slice retains it as compatibility
telemetry while introducing no new consumer.

### AP awards are integer and currently fixed

Character AP stores and operation projections use integer totals. The current Foundational receipt hard-codes the
proof's low award. The accepted contracts describe Foundational AP as Attunement-authorized, while the current live
relationship authorizer accepts either active Bond or Attunement; this pre-existing discrepancy is not ratified here.
The proposed larger authored awards and whole-number floor fit the integer representation, but the receipt must record
enough inputs/provenance to replay the same result after participation or Connection state later changes.

**Consequence:** preserve each source definition's accepted actor/relationship authorization and apply the multiplier
only after it passes. A later behavior PR must reconcile the existing Bond-vs-Attunement discrepancy explicitly rather
than using this feature as implicit authorization. Snapshot base award, selected participation tier, selected maturity
band, effective multiplier version, and final floored award in the accepted receipt. Replay returns the recorded award;
it never recomputes against current state.

### Node outcome/ownership enums are closed

Current node outcomes are Local Effect, Character Effect, and Permanent Effect. Ownership is Stone Cultivated,
Personal Offered, or unavailable. Recovery explicitly quarantines a Stone-cultivated Local node appearing as a
personal purchase.

**Consequence:** Resource Delivery needs an explicit Stone-owned outcome/acquisition classification. Do not overload
Local Effect beneficiary policy or smuggle the node into personal purchase records. Humble appends one executable
Foundational node, so the behavior PR must supersede first-slice conformance from `20 = 13 executable + 7 unavailable`
to `21 = 14 executable + 7 unavailable` across registry, read model, tests, and runtime manifest. Profession/Martial
Resource Delivery uses accepted incremental node-development + equal cumulative Tree-investment semantics;
Foundational development advances node progress only. Accepted committed-Tree revocation also remains: delete that
Tree's node development with no BP refund, recommit starts fresh, and Foundational nodes are unaffected; pending bundles
and deposited Stock remain immutable.

### Relationship authority is character/Stone; Connection is account-pair

Existing relationships are character↔Stone records inside account/character authority. The new Connection is a
world/product-scoped account pair maintained by derived role-pair sources across Stones.

**Consequence:** Connection is its own aggregate/projection with stable pair identity and source references. It must not
move character-owned AP/BP or Stone relationships into an account-wide wallet.

## High-level test seams

Use one application seam per behavior class, all behind the existing authenticated envelope and receipt store:

1. **Relationship lifecycle seam:** after a Bond/Attunement mutation commits, derive qualifying account-pair link
   additions/removals and one Connection transition in the same recoverable logical operation.
2. **Participation evidence seam:** server adapters submit one typed objective-completion fact; application policy
   validates current authored definition and commits the participation receipt.
3. **Contribution reconciliation seam:** one pure deterministic service integrates elapsed intervals and returns
   Stone-outcome deltas. AP remains event-awarded through its existing source-specific receipt seam, which only reads
   the current multiplier after its ordinary actor authorization passes.
4. **Stone Stock seam:** donation, generated delivery, permission, and withdrawal commands share one revisioned
   Stockpile aggregate and atomic result vocabulary.
5. **Read seam:** extend the Stone-identity progression view rather than building a Resource Delivery-only UI store.

These are logical command families over one command/receipt architecture, not five independent persistence systems.

## Mandatory spikes before gameplay implementation

### P0 — Deterministic elapsed reconciliation

Prove a pure interval integrator that splits at:

- weekly-upkeep expiry;
- 24-hour daily-practice expiry;
- Connection maturity boundaries;
- final-link grace start/expiry/reconnect;
- node activation/dormancy/version changes;
- Stockpile capacity becoming full/available;
- Resource Delivery completion.

**Exit:** online incremental evaluation and one offline jump produce byte-equivalent terminal state and receipts across
clock boundaries, restart, upkeep expiry→renewal, same-time operation ordering, several delivery cycles, residual
progress, dormancy freeze/resume, and pending-capacity start.

### P1 — Relationship fan-out transaction

One relationship release can remove qualifying links to several accounts; several final sources can start grace. Prove
one two-operation handshake: `ReleaseRelationship` preparation terminates in a durable non-gameplay decision that binds
its operation/intent, authenticated preparing AccountId/CharacterId, target RelationshipId/release-authority revision,
every affected Connection/source/revision, and the grace-policy version while presenting tier/age only as issue-time
previews; `ConfirmReleaseRelationship` uses a fresh operation ID, requires that same principal to remain the active
authorized holder, and treats token possession as no authority.
Confirmation must tolerate ordinary age advancement, reconcile to confirmation time, freeze that age, and start a full
72-hour grace. Challenge consumption, relationship release, all transitions, and confirmation receipt commit in one
recovery boundary; changed set/revision/policy/intent must reject.

**Exit:** preparation replay after restart returns the exact principal/target-bound challenge; delayed confirmation,
principal-substitution/lost-authority rejection, mixed final/non-final fan-out, stale-set, competing confirmation IDs,
and injected failure before/after the atomic confirmation boundary converge to exactly one relationship result and
complete Connection source/grace state. No token grants bearer authority, no token is consumed without release, no
release applies twice, no maturity is lost, no grace is shortened, and no unlisted/partial warning survives.

### P2 — Inventory transaction adapter

Prove server-owned exact-item count/debit and player inventory-fit/credit for donation and withdrawal without trusting
client quantities or creating partial effects.

**Exit:** replay, stale revision, insufficient items, inventory full, disconnect, and process death never duplicate or
lose player or Stone resources.

### P3 — Stock capacity and pending delivery

Prove the chosen virtual capacity model with a fully fitting bundle, non-fitting bundle, later withdrawal, mandatory
pending-first retry, blocked later donation, and resumed delivery. Also prove empty-bundle dormancy and preserved
in-progress state. No physical chest is authoritative.

**Exit:** pending content/version snapshot remains exact; sibling outcomes continue while Resource Delivery pauses.

## Security and abuse analysis

| Threat | Required control |
|---|---|
| Client claims quest/damage/donation completion | Server-observed evidence and exact authored objective version |
| Alt accounts inflate one contributor | One contribution per authenticated account/Stone; strongest link once |
| Large Guild/Friend graph creates maturity | Only Stone role-pair links qualify |
| Release/reconnect resets or farms age | Frozen 72-hour grace, stable source IDs, revisioned transitions |
| Replayed upkeep refreshes indefinitely | Operation-bound receipt and recorded completion time |
| Governor demands impossible tax | Selection only from authored level-appropriate pool; deterministic fallback |
| Donation dupes items | Validate/debit/deposit atomically through one receipt |
| Delegated claimant drains after revoke | Current permission revalidated at withdrawal; revocation revisioned |
| Full Stockpile deletes generated items | Complete bundle remains pending; no partial/world-drop path |
| Node revocation mutates old stock | Snapshot completed bundle; deposited Stock is independent of later node state |
| AP state changes before retry | Receipt snapshots award inputs/result; retry never recomputes |

## Performance and operational reality

- Reconcile on relevant load/read/mutation/withdrawal and bounded scheduled checkpoints, not per frame.
- Store timestamps and cursors sufficient to jump across long offline intervals without replaying every hour.
- Bound per-Stone contributor and outcome work to current relationship/source sets; do not scan the world or every
  account.
- Log stable operation/result/rejection codes and pseudonymous internal IDs, not raw provider subjects.
- Expose operator census for pending deliveries, quarantined stock, grace Connections, and receipt recovery.

## Proposed supersessions and retained truth

| Existing authority | Proposed change | Remains true until behavior PR |
|---|---|---|
| AP receipt adds equal fixed N Personal/Cumulative/Mirrored | Otherwise-authorized awarded N becomes multiplier-derived and recorded; source actor authorization is unchanged; first slice MUST mirror the actual floored award as telemetry | Existing fixed triple, source authorization, and tests |
| AccountId is authority/grouping/audit only | Account pair owns Connection maturity and account–Stone participation eligibility, but no wallet/purchase/outcome | Accepted character/Stone progression ownership remains; new account status is an explicit proposed extension |
| Mirrored AP has no spend/threshold | Resource Delivery still never reads it; later removal is separate | Accumulate-only proof semantics |
| Node outcomes/ownership exclude Resource Delivery; roster is 20/13/7 | Add explicit Stone-owned Resource Delivery outcome and Humble node, yielding 21/14/7; preserve incremental committed-Tree investment semantics | Current enum/roster until behavior PR |
| Local nodes are the only Stone-owned developed outcomes | Resource Delivery becomes another Stone-owned class without Local beneficiary policy | Existing Local behavior |
| No Stone Stockpile | Add one virtual store for donations and deliveries | No current stock/withdraw behavior |

The later behavior PR must stamp reconciliation pointers into every affected accepted document; this proposed package
alone does not silently rewrite accepted truth.

## Assumptions and deferred scope

- Account identity remains the existing authenticated world/product-scoped subject; provider migration is deferred.
- Connection maturity is world/product-scoped and shared across qualifying Stones in that scope, not across servers.
- Weekly upkeep uses a rolling seven-day expiry from accepted donation completion; it is not a calendar-week reset.
- A versioned objective provider exists even if final quest presentation is built later.
- The first daily-practice fixture is exactly five distinct physical eligible Foundational catalog placements inside
  the Stone Area. One durable per-account/Stone cycle accumulates instance IDs until completion, ignores next-cycle
  progress during its active 24-hour window, and opens fresh after expiry reconciliation. A placement that is also an
  AP source uses one combined receipt: AP snapshots the pre-practice tier, then practice progress applies, so the fifth
  placement never boosts itself and `2×` begins with subsequent events/elapsed time. Final broader daily content remains
  a content pass.
- The Level-2 donation pool is `20 Wood`, `20 Stone`, and `10 Wood + 10 Stone`; default pair is `20 Wood` + `20 Stone`.
  Only an active Bond carrying the server-authored owner role may select the pair; otherwise the default materializes.
- Any active Bond may incrementally develop the Foundational Humble node; cost is 1 BP and creates no committed-Tree investment.
- Any active Bond may withdraw Stock. Only an active owner-role Bond may grant/revoke delegation; one canonical record
  per grantee uses revisioned generations, has no expiry in this slice, and Responsibility Range does not apply.
- Stone-level advancement remains outside this package; the preconfigured Level-2 fixture starts with an uninitialized
  menu and proves the same authorized selection/default transition consumed by later level changes.
- The first slice uses provisional Stone-Level-2 Stock capacity defaults of 16 distinct item kinds, 1,000 total units,
  and 500 units per item; all remain server-configurable playtest data.
- No task decomposition or runtime implementation is authorized by this research.
