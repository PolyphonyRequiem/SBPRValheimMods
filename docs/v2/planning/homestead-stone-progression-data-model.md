---
title: "Homestead Stone progression S2 — data model"
status: accepted
purpose: Define authoritative aggregates, stable identities, invariants, transitions, and derived views for the Homestead progression proof.
---

# Homestead Stone progression S2 — data model

**Companion spec:** [`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md)
**Research boundary:** [`homestead-stone-progression-research.md`](homestead-stone-progression-research.md)

## Modeling rules

1. Persist earned, selected, and provenance state; derive activation.
2. Separate world-owned Stone state from character-owned progression and account-owned authority indexes.
3. Use stable IDs and enough current-build schema/content identity to prevent misbinding.
4. Every mutation is revision-checked, idempotent, auditable, and recoverable.
5. Display names, scene objects, prefab network owners, and client state are never domain identity.
6. Unknown same-build references reject clearly. Production content migration is deferred; incompatible
   unreleased test data may be reset rather than silently reinterpreted.

This document defines logical contracts, not a storage engine or C# type layout.

## Stable identity vocabulary

| Identity | Logical shape | Scope and rule |
|---|---|---|
| `WorldId` | authenticated server/world identity | Prevents portable character state from authorizing another world |
| `StoneId` | `WorldId + host zoneX + host zoneZ` for this Homestead proof | Extends the current D3 host-Location identity; family/variant are versioned attributes, not identity inputs; no ZDOID or minted parallel GUID |
| `AccountId` | authenticated account-provider subject | Authority/grouping/audit only; not gameplay progression ownership |
| `CharacterId` | server-bound character subject within `AccountId` | Owns gameplay progression; never accepted from an unauthenticated payload |
| `RelationshipId` | stable relationship record key | One Bond or Attunement between one character and one Stone |
| `FacetId` | stable authored Stone-Facet key, such as Homestead Profession or Martial | Replaceable Tree position, not a relationship or equipment slot |
| `TreeId` / `TreeVersion` | stable content key and version | Display name changes do not change identity |
| `NodeId` / `NodeVersion` | stable content key and version | Carries outcome type and first-build status |
| `OfferedSetId` / `OfferedSetVersion` | exact personal Offered Nodes for one Tree level/content view | Preserves same-build Tier-Access inputs; production migration is deferred |
| `CatalogId` / `CatalogVersion` | Foundational pieces, Tree palettes, activities, properties, eligible skills/items | Each catalog is one source of truth, not duplicated UI logic |
| `OperationId` | caller-generated unique id bound to principal, command, Stone, and payload digest | Same ID/same request returns the recorded result; conflicting reuse rejects |
| `ReceiptId` | server-issued durable mutation/audit identity | Links all aggregate deltas and provenance for one accepted operation |
| `ItemProvenanceId` | stable issuance record for one exact item capability | Never reconstructed from prefab/display name alone |

## Aggregate 1 — StoneProgressionAggregate

One authoritative world-scoped aggregate per `StoneId`.

### State

| Field group | Required logical state |
|---|---|
| Envelope | schema version, `StoneId`, aggregate revision, created/updated provenance |
| Classification | family = Settlement, variant = Homestead, content-registry version |
| Levels | Historical Stone Level, Active Stone Level; preconfigured tests begin at 2/2 |
| Foundation | protected Foundational `TreeId/version`, Foundational construction `CatalogId/version`, exclusions |
| Tree palette | current eligible candidate definitions for each authored Facet |
| Facets | each `FacetId`, category, occupancy, commitment provenance |
| Committed Trees | `TreeId/version`, Facet, commit operation/actor, Tree Level, cumulative qualifying BP investment |
| Development | per-node BP progress/cost step, developed/Offered/active state, source operations |
| Local state | activated Local Nodes and one Settlement-wide beneficiary policy; Private allowlist where used |
| Stone ledger | Mirrored Stone AP and its receipt history/provenance |
| Lifecycle | explicit revocation provenance, dormancy/checkpoint state when applicable, quarantine notices |
| Recovery | last applied receipt/result references and invariant scan status |

### Required invariants

- `0 ≤ ActiveStoneLevel ≤ HistoricalStoneLevel`.
- The preconfigured proof begins at Historical/Active 2; no operation in this package raises Stone Level.
- The Foundational Tree is present from level 1, protected, occupies no Facet, and cannot be revoked.
- Exactly one Profession and one Martial Facet exist in the proof fixture.
- A Facet is empty or occupied by exactly one candidate eligible for its category/current palette.
- One `TreeId` cannot occupy two Facets on the same Stone unless a later content contract explicitly
  permits it; the proof catalog does not.
- `TreeLevel ≤ ActiveStoneLevel` for active behavior. Historical completed Tree Level is not silently deleted.
- No separate direct Tree-level meter exists. Cumulative qualifying Tree investment equals accepted BP spent
  developing eligible nodes/offerings under the current proof definitions.
- Every developed node belongs to the current Committed Tree definition; completing a Local Node activates
  Stone-local state, while completing a personal node makes it Offered to eligible attuned players.
- Local Nodes are Stone-owned and have no character purchase record.
- Mirrored Stone AP equals the sum of accepted mirrored deltas after receipt reconciliation; it is
  not inferred from current Personal AP balances and is never debited or applied to a threshold/Facet in this proof.
- No Cultivation Target, source-Tree BP wallet, personal AP wallet, or personal purchase is stored here.

## Aggregate 2 — AccountStoneAuthorityIndex

One server-owned authority record per `(AccountId, StoneId)`.

### State

- a set of active reservations, each holding one character's active relationship at this Stone:
  - reserving `CharacterId`;
  - relationship type (Bond or Attunement);
  - active `RelationshipId`;
  - activation receipt provenance;
- index revision and last-release receipt provenance;
- audit metadata for rejected sibling attempts.

The index is a single authoritative account–Stone active-character reservation index that MAY hold
multiple character entries, governed by variant-authored cardinality policy (design call 2026-07-15).

### Invariants

- For Homesteads, at most one character from an account may actively hold either relationship to one
  Stone: the reservation set holds at most one entry.
- This index is policy-driven. Community Stone Attunement does NOT use sibling-character exclusivity:
  multiple sibling characters on the same account may be simultaneously active, each represented as its
  own reservation entry in this authoritative index and in derived activation. Community Bond remains
  account-exclusive for now (no sibling of any kind may hold a Community Bond reservation).
- Community activation is derived ONLY from this index — never through a second authority path outside it.
- A character cannot evade the invariant by holding Bond and Attunement simultaneously through separate
  rows; the single index is the one gate for both.
- Ordinary release removes ONLY that character's reservation, and only after the relationship mutation is
  durably recoverable; sibling reservations are untouched.
- Retained purchases, AP/BP, Permanent Effects, Progression Keys, or dormant history do not keep a
  reservation occupied.
- This index owns no gameplay balance or outcome.

## Aggregate 3 — CharacterProgressionAggregate

One server-owned aggregate per world/product-scoped `(AccountId, CharacterId)`. Stone-linked state is nested or
indexed by stable `StoneId`; exact physical persistence is left to Gate A.

### State

| Field group | Required logical state |
|---|---|
| Envelope | schema version, account/character IDs, world/product scope, aggregate revision |
| Capacity | Bond Slots, Attunement Slots, current relationships |
| Relationships | per-Stone Bond/Attunement records, status, responsibility range, provenance |
| AP | Personal AP and Cumulative AP Earned per Stone |
| BP | one personal BP balance per bonded Stone; no Tree/source/target binding |
| Revocation refund | appended purchase-cancellation entries naming the purchase each reverses; the refunded value returns as ordinary Stone-wide Personal AP |
| Purchases | personal node purchase records keyed by Stone/Tree/node/version, AP source, refundable/durable outcome class |
| Offered provenance | exact Offered-Set IDs/versions acquired per Tree level for Tier Access |
| Durable outcomes | Permanent Effects and Progression Keys keyed by stable identity |
| Skill-cap choices | stable choice records, including Weapon Discipline and cap-provider provenance |
| Item references | issuance/provenance references needed for audit; the item instance carries validated portable metadata |
| Recovery | applied operation/receipt results, quarantine notices, last invariant scan |

### Required invariants

- Gameplay progression belongs to the character, not the account.
- Personal AP and Cumulative AP Earned are never negative; Cumulative AP never decreases in this proof.
- Personal BP is never negative and can be spent only by that bonded character within their current
  Responsibility Range.
- Personal AP is Stone-wide. It has no source-Tree restriction or Personal Target.
  - **Implementation note (T022 split-ledger fix):** Personal AP has exactly ONE earning authority — the
    receipt-derived `ICharacterApStore` credited by `OperationReceiptStore.SubmitFoundationalAp` on every
    valid Foundational placement. Purchasing MUST observe that same balance. The character aggregate's
    `CharacterStoneRecord.PersonalAp` field is NOT an independent balance: the spendable balance a purchase
    reads is DERIVED as `earned(ICharacterApStore) − Personal-AP already spent by that character's committed
    purchases at the Stone` (both idempotent receipt/journal projections — no second synchronization ledger,
    no double-credit, fail-closed to `InsufficientPersonalAP` on over-spend). `PurchaseCommandHandler` is
    composed with the shared earn ledger (`LocalProgressionServer.CharacterApStore`, sourced from the
    Foundational runtime) so earned placement AP is authoritatively visible to Masterwork purchase. Restart
    is safe with no fabricated migration: both terms rehydrate from their durable journals at server boot.
    The legacy pure-domain seam (no `ICharacterApStore`) still reads the aggregate's `PersonalAp` verbatim.
- A revocation refund returns ordinary Stone-wide Personal AP, spendable on any Facet. No Facet-keyed balance
  separate from Personal AP exists.
- A node purchase is unique by character, Stone, stable node identity/version, and authored repeatability.
- Local Nodes never appear in purchases or Offered-Set provenance.
- Character Effects may dormant, but their purchase records persist except when explicit Tree revocation removes
  a refundable Character-Effect purchase.
- Permanent Effects and Progression Keys survive relationship loss and Tree revocation.
- Tier Access is not stored as independent progress. It is derived from current Stone/Tree caps plus persisted
  purchases and Offered-Set provenance.

## Aggregate 4 — ContentRegistry

Immutable definitions selected by the current proof build. Stable IDs prevent same-build misbinding. Production
migration/grandfathering/retirement semantics are deferred; incompatible unreleased fixtures may be reset.

### Core definitions

- Stone family and Homestead variant.
- Foundational Tree and construction catalog. The Foundational construction catalog is a stable
  `CatalogId/version` (`HomesteadFoundationalConstruction` v1, tag `v1`) owning an authored member roster
  of basic-piece **stable ids** plus an explicit **exclusion** set; an excluded stable id is never a
  credit-eligible member even when placeable. Provisional proof roster (design call 2026-07-15,
  explicitly configurable, not a final content lock): members `foundation_wood_floor`,
  `foundation_wood_wall`, `foundation_wood_pole`, `foundation_wood_beam`, `foundation_wood_roof`,
  `foundation_wood_stair`, `foundation_wood_door`, `foundation_wood_stakewall`; exclusions
  `foundation_workbench`, `foundation_forge`. Membership is by exact stable id + current-build version,
  never a display name or a "closest" rebind — a non-member, an excluded id, or a stale catalog version
  is an out-of-build reference that earns no receipt.
- Stone Facets and Tree palette.
- Tree definitions: category, levels, cumulative BP thresholds, escalating unlock-cost policy, activity families.
- Node definitions: stable ID/version, Tree level, outcome type, first-build status, AP/BP price, development
  requirements, personal requirements, effect parameters, refund/durability class.
- Activity definitions: actor role, source family, event provenance requirements, AP/BP award policy.
- Outcome registries: eligible pieces, recipes, stations, items, properties, arrows, melee weapons, skills.
- Rejection/localization keys and read-model labels.

### Fixed first-build roster

| Tree | Level | Node | Outcome | Ownership | Status |
|---|---:|---|---|---|---|
| Cooking | 1 | Savor the Hearth | Local Effect | Stone cultivated | executable |
| Cooking | 1 | Field Prep | Character Effect | personal Offered | executable |
| Cooking | 1 | Iron Stomach | Permanent Effect | personal Offered | executable |
| Cooking | 2 | Swift Preparation | Character Effect | personal Offered | executable |
| Cooking | 2 | Watchful Cook | Character Effect | none while unavailable | unavailable |
| Crafting | 1 | Refined Workshop | Local Effect | Stone cultivated | executable |
| Crafting | 1 | Masterwork | Character Effect | personal Offered | executable |
| Crafting | 1 | Built to Last | Permanent Effect | personal Offered | executable |
| Crafting | 1 | Measured Cuts | Character Effect | none while unavailable | unavailable |
| Crafting | 1 | Artisan's Counter | Local Effect | none while unavailable | unavailable |
| Archer | 1 | Practice Range | Local Effect | Stone cultivated | executable |
| Archer | 1 | Field Fletching I | Character Effect | personal Offered | executable |
| Archer | 1 | Fletcher's Habit | Permanent Effect | personal Offered | executable |
| Archer | 1 | Steady Aim | Character Effect | none while unavailable | unavailable |
| Archer | 1 | Bowyer's Lore | Permanent Effect | none while unavailable | unavailable |
| Warrior | 1 | T.W.I.G. Training | Local Effect | Stone cultivated | executable |
| Warrior | 1 | Ready Hands | Character Effect | personal Offered | executable |
| Warrior | 1 | Weapon Discipline | Permanent Effect | personal Offered | executable |
| Warrior | 1 | Shrug It Off I | Character Effect | none while unavailable | unavailable |
| Warrior | 1 | Heavy Hands | Character Effect | none while unavailable | unavailable |

**Arithmetic invariant:** 20 authored nodes = 13 executable + seven unavailable. Of the executable nodes,
12 are Level 1 and Swift Preparation is the sole executable Level-2 node. “Unavailable” is a runtime/prototype
status, not a name-only placeholder: the normative capability and boundary for each unavailable node is authored
in the feature specification's User Story 4 and remains part of its versioned content definition.

### Provisional first-build prices and requirements

These are **provisional proof-only playtest values** (design call 2026-07-14), explicitly configurable and
**not** final balance or compatibility locks. They exist so the read model (`GetStoneProgressionView`) and its
T006 verification can report *exact* prices/requirements per node without pretending the numbers are final.

- Every executable node has authored BP development price = 1.
- Every executable **personal** node (Character/Permanent Effect, personal Offered) has authored AP purchase
  price = 1.
- Local (Stone-cultivated) nodes have **no** AP purchase price and remain Stone-cultivated BP-only outcomes.
- Unavailable nodes have **no** AP/BP price and continue rejecting development/purchase/Offering/activation.
- Requirements are only the already-accepted gates: committed Tree, current content/version, Active Stone
  Level ≥ node level, Tree Level ≥ node level, and the relevant relationship/authority/Responsibility Range;
  personal nodes additionally require active Attunement + Offered status. The registry surfaces the
  relationship/authority/Responsibility-Range gate as two explicit boolean flags (development authority +
  Responsibility Range), true for every executable node and false for unavailable nodes; live authority
  state is supplied by T007.
- **Swift Preparation** additionally requires Cooking Tree Level 2, Active Stone Level 2, and acquisition of
  both prior-Level-1 personal Cooking Offered Nodes — **Field Prep** and **Iron Stomach**. Savor the Hearth is
  Local and is **not** part of that personal prior-Offered Set.
- No additional objective/key/item requirements in this proof build.

## Aggregate 5 — OperationReceiptStore

A server-owned durable operation/result journal. It may be implemented with a database, append-only log, or
other mechanism only after Gate A proves recovery behavior.

### Receipt fields

- `ReceiptId`, `OperationId`, operation type, payload digest;
- authenticated `AccountId`, acting `CharacterId`, `StoneId`;
- expected and committed Stone/character/account-index revisions;
- content/schema versions consulted;
- validated source provenance (piece/item/projectile/station/event/objective as applicable);
- debit/credit and state-transition deltas;
- item/cap-choice issuance references where applicable;
- terminal result and stable rejection/result code;
- recovery phase and timestamps in the selected server clock domain;
- audit correlation and operator notes/quarantine link.

### Idempotency invariant

- Same operation ID + same authenticated principal + same Stone + same operation type + same payload digest
  returns the recorded terminal result.
- Same operation ID with any conflicting binding rejects as `OperationConflict`.
- A result is acknowledged only when replay after process death can converge to that exact terminal result.

### Boot rehydration invariant

- The store persists the receipt identity fields (authenticated `AccountId`, acting `CharacterId`, `StoneId`)
  on every durable boundary record, not only the payload/binding digests.
- At construction (server boot / fresh process) the store replays the durable journal and rebuilds every
  Stone and character projection balance AND its optimistic-concurrency revision from journal truth, before
  any new operation is admitted. Only committed (terminal-bearing) operations project; a partial, non-terminal
  operation is quarantined, never counted, so it cannot inflate a balance or a revision.
- Consequently two separate processes cannot both commit distinct operations against expected revision 0, and
  the authoritative read state never reports Mirrored AP 0 while durable journal truth is non-zero. The journal
  remains the single authority; the projections are reconciled onto it and never become a second source of truth.

## DerivedActivationView

A read-only projection derived from current aggregate snapshots and registry definitions.

For each node/outcome it derives:

- authored, visible, Offered, developable, unavailable, purchased, developed;
- Tree Level and Active Stone Level gates;
- prior-level same-Tree Offered-Set acquisition;
- AP/BP affordability;
- relationship/responsibility and Settlement-policy eligibility;
- active, dormant, durable, quarantined, or invalid state;
- actionable rejection reasons.

No result from this view is persisted as an independently mutable authority. Runtime status effects, recipe
visibility, placement capabilities, skill-cap providers, item properties, and timing modifiers are delivery
mechanisms refreshed from this view.

**Implementation (shared Local Effect runtime substrate, `t_02c13405`).** The per-occupant Local delivery is a
fresh derivation, never a stored ledger: `Application/Activation/LocalActivationService.Derive` builds a
`LocalActivationSnapshot` from `LocalEffectActivationView` each time, and the only state it keeps is a
monotonic per-occupant delivery sequence (delivery ordering metadata, not gameplay authority). A restart
re-derives identical active/dormant status from the durable Stone journals; the sequence resetting cannot
resurrect a stale effect because the derivation, not the sequence, is authoritative.

## ProgressionReadModel

One Stone-identity projection suitable for the temporary local panel and future Stones UI.

It includes:

- Stone identity, family/variant, aggregate/content revisions;
- relationship, owner/governor role, Responsibility Range, and active sibling exclusivity status;
- Historical/Active Stone Level and preconfigured-test marker;
- Foundational Tree/catalog summary;
- Stone Facets, palettes, commitments, Tree Levels, cumulative BP development, and node development/offerings;
- Personal AP, Cumulative AP, and personal BP for the caller;
- exact node definitions and derived statuses;
- Local beneficiary policy and ordinary Permission note;
- durable-outcome summaries;
- available command affordances plus stable rejection reasons.

The projection never contains a client-authoritative ready flag. Commands revalidate current state.

## State transitions

### Form relationship

1. Authenticate account and acting character.
1a. Confirm SERVER-SIDE that the acting character is inside the target Stone's Area (ADO #138). The check runs
   after the idempotency lookup — so a committed operation still replays its recorded terminal result even if the
   actor has since walked away — and before any state load or journal write, so a `NotAtStone` rejection changes
   nothing durable. Applies to Bond and Attunement formation only.
2. Load character aggregate and account–Stone index.
3. Validate slot capacity, family contract, no active sibling, and no conflicting relationship.
4. Commit relationship plus active index under one recoverable receipt.
5. Re-derive activation and return new revisions.

### Release relationship

1. Validate current relationship and authority.
2. Persist inactive/released relationship state.
3. Clear account–Stone active index in the same recoverable operation.
4. For Attunement release, preserve AP, purchases, Offered provenance, Permanent Effects, and Progression Keys;
   dormant relationship-supplied Character Effects.
5. For voluntary Bond release, preserve personal BP and Stone-owned Facet/Tree development. If no authorized
   Governor remains, dormant affected Facets: stop Local Effects/new BP development and deactivate supplied
   Character Effects. Create no refund or cooldown; a later valid Bond restores eligible governance.

### Credit Foundational AP

1. Adapter supplies authenticated actor, exact placed piece stable ID, Stone Area result, operation ID, and
   placement provenance.
2. Validate active Attunement, Foundational catalog membership/exclusion, anti-repetition policy, and the
   deliberately low current Foundational AP value. Tree commitment never disables this source.
3. Atomically add N Personal AP, N Cumulative AP, N Mirrored Stone AP, and provenance.
4. Record one receipt/result; retry is read-only.

### Commit Tree

1. Validate Governor authority and Responsibility Range.
2. Validate expected Stone revision, Facet category/emptiness, candidate palette and current-build definitions.
3. Persist commitment with initial Tree Level/progress and provenance.
4. Do not change Stone Level, personal balances, or purchase state.

### Credit and spend BP on node development

- Credit one personal Stone-wide BP balance from an eligible aligned activity associated with a Committed Tree
  in the Governor's Responsibility Range.
- Applying BP to an eligible node debits BP and advances that node's development plus the Tree's cumulative
  qualifying investment in one mutation.
- Completing a Local Node activates Stone-owned Local state; completing a personal node makes it Offered.
- Crossing the configurable cumulative threshold may advance Tree Level if Active Stone Level permits it.
- Successive unlock costs may increase under the provisional data-defined curve. No separate Tree-level spend,
  source-Tree wallet, or target exists.

### Purchase personal node

1. Validate Attunement, expected Stone and character revisions, Tree commitment, node Offered status, same-Tree
   Tier Access, Active Stone Level, Tree Level, Personal-AP price, and other requirements.
2. Debit the allowed balance exactly once.
3. Persist purchase and Offered-Set provenance.
4. Derive activation from current state; do not write an active-effect ledger.

### Revoke Tree

Two steps, not one button. Step one computes and presents the loss and mutates nothing; step two is the
atomic/recoverable operation below. Abandoning step one is not a rollback — nothing was written.

1. Validate Governor authority, expected revisions, Facet, and current commitment.
2. Delete Stone commitment, cumulative BP development, node development, Local Nodes, and personal-node offerings.
3. Refund no BP.
4. For every affected character, append a cancellation entry naming each reversed refundable Character-Effect
   purchase. The purchase record is never removed; the derivation of spendable Personal AP excludes cancelled
   purchases, so the refunded AP returns in full as ordinary Stone-wide Personal AP with no stored balance and
   no second ledger. The same cancellation appended twice refunds exactly once.
5. Preserve Permanent Effects, Progression Keys, and their purchase records with
   no AP refund.
6. Vacate the Facet and record complete provenance.
7. A replacement commitment starts Stone-owned progress at zero and buys nothing automatically.

### Dormancy

Dormancy is modeled but decay-driven rollback is outside this proof. Dormancy is a DERIVED per-occupant
projection (`LocalEffectActivationView`), never a stored active-effects ledger: a missing authorized
Governor, the owning Tree no longer committed, or an Active Stone Level below the node's authored level
dormants a developed Local Effect while retaining its Stone-owned development; relationship release/rejoin
and a Settlement-policy change during occupancy re-derive active/dormant deterministically from the same
persisted Stone with zero writes, and restart rebuilds the identical result. If a preconfigured test or
later tracer enters dormancy:

- keep the Tree Committed;
- create no AP refund;
- retain completed Tree levels/nodes and character progression;
- permit only authored unfinished node-development regression to recorded checkpoints;
- re-derive active outcomes from Active Stone Level and current requirements.

## Validation and recovery

On load and after every accepted mutation:

- validate aggregate revisions and schema versions;
- validate all stable content references;
- validate ledger non-negativity and receipt sums;
- validate Facets, commitments, Tree/Stone level caps, development ownership, and purchase ownership;
- compare account–Stone index to active relationship records;
- rebuild the derived view;
- quarantine contradictory or unknown records and report them; never guess which side of an interrupted
  mutation is correct without the receipt journal.

A repair tool may replay a committed receipt, rebuild a projection, release an orphaned authority index, or
quarantine invalid transaction state. It may not invent a purchase, balance, item property, or relationship.
