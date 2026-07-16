---
title: "Homestead Stone progression S2 — command, receipt, and read-model contracts"
status: accepted
purpose: Define the server-authoritative application boundary consumed by world adapters, the temporary local panel, and the future Stones UI.
---

# Homestead Stone progression S2 — contracts

**Feature spec:** [`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md)
**Logical state:** [`homestead-stone-progression-data-model.md`](homestead-stone-progression-data-model.md)

## Contract principles

- Commands mutate authoritative state; queries return projections; world adapters submit validated evidence.
- The authenticated connection supplies account authority. Payload identity is a claim to compare, not authority.
- Every mutation carries an operation ID and expected revisions and returns a stable recorded result.
- Validation completes before commit. Failure changes nothing.
- Cross-aggregate operations are journaled/recoverable and acknowledged only when replay can converge.
- Commands use stable IDs and versions. Display names are never contract identity.
- The temporary in-world panel and future remote Stones UI call the same progression commands.
- World evidence remains local and server-validated even when the resulting progression selection is remote.

Names below are directional API semantics, not mandatory C# method, RPC, or transport names.

## Common command envelope

```text
ProgressionCommandEnvelope
  operationId
  commandType
  claimedAccountId
  claimedCharacterId
  stoneId
  expectedStoneRevision?       # required for Stone mutations
  expectedCharacterRevision?   # required for character mutations
  expectedAuthorityRevision?   # required for relationship/index mutations
  contentRegistryVersion
  payload
```

The transport attaches server-observed connection/session identity and request correlation outside the
payload. The handler MUST compare the authenticated principal to the claimed principal and reject any
mismatch.

### Common successful result

```text
ProgressionCommandResult
  operationId
  receiptId
  outcome = Applied | Replayed | NoOp
  stoneRevision?
  characterRevision?
  authorityRevision?
  resultCode
  changedEntityIds[]
  balanceDeltas[]
  readModelInvalidationToken
  auditCorrelationId
```

### Common rejected result

```text
ProgressionRejection
  operationId
  rejectionCode
  messageKey
  currentStoneRevision?
  currentCharacterRevision?
  currentAuthorityRevision?
  failedRequirementIds[]
  retryable
  auditCorrelationId?
```

A rejection is not a receipt-bearing mutation. An idempotency conflict is auditable but changes no gameplay
state.

## Relationship commands

### `CreateBond`

**Caller:** local relationship flow or preconfigured-test harness; authenticated character.
**Payload:** relationship offer/version, requested Responsibility Range, test-fixture authorization when applicable.

**Validates:**

- authenticated account/character match;
- Stone exists and is a bondable Homestead;
- Bond Slot capacity;
- no active relationship by this or a sibling character at this Stone;
- requested Responsibility Range is authored and available;
- expected character/authority/Stone revisions.

**Commits atomically/recoverably:**

- character-owned Bond record;
- account–Stone active-character index;
- authored owner/governor role and Responsibility Range;
- receipt/audit provenance.

It does not create Stone ownership by inference for non-Settlement families and grants no AP/BP by itself.

### `CreateAttunement`

For this Homestead proof, use the same active sibling-exclusivity rule as `CreateBond`, consume an Attunement
Slot, and grant no cultivation authority. The proof participant must be on a different authenticated account
from the bonded owner. The exclusivity rule is variant-authored rather than universal: Community Stone
Attunement permits sibling characters, while Community Bond remains account-exclusive for now.

### `ReleaseRelationship`

**Payload:** `relationshipId`, expected status.
**Commits:** mark relationship released/inactive and clear any applicable account–Stone active-character index
in one recoverable operation.

- **Attunement release:** preserve Personal/Cumulative AP, purchases, Permanent Effects, Progression Keys, and
  choices; re-derive relationship-supplied Character Effects as dormant.
- **Voluntary Bond release:** preserve personal BP and Stone-owned Facet/Tree development. If no authorized
  Governor remains, dormant affected Facets: stop Local Effects and new BP development and deactivate supplied
  Character Effects. Create no AP/BP refund or cooldown. A later valid Bond restores eligible governance; each
  Governor retains their own BP balance.

## Evidence and credit contracts

Evidence submission endpoints are internal server/application contracts. They are not client-callable award
commands.

### `RecordFoundationalPlacement`

```text
FoundationalPlacementEvidence
  operationId                 # stable for this authoritative placement event
  authenticatedActor
  stoneId
  stablePieceId
  pieceInstanceProvenance
  serverObservedPosition
  insideStoneArea
  placementOutcome
  foundationalCatalogVersion
```

**Validates:** active Attunement, authenticated actor, exact Stone Area, stable piece membership, explicit
exclusions, current-build definition, placement success, repetition policy, replay state, and the deliberately
low current Foundational AP value. Tree commitment never disables this baseline source.

**Accepted AP receipt:**

```text
ApActivityReceipt
  personalApDelta = N
  cumulativeApDelta = N
  mirroredStoneApDelta = N
  sourceActivityId
  sourceTreeId = FoundationalTree
  evidenceDigest
```

The three deltas commit as one logical operation. Partial Personal/Cumulative/Mirrored results are invalid.

**Live runtime seam (T009R, 2026-07-15).** `RecordFoundationalPlacement` is fed on the authoritative
server by `Application/Runtime/FoundationalPlacementRuntime`, which turns one server-observed
`FoundationalPlacementObservation` (Stone, acting account/character derived from the authenticated
connection context — never client payload, stable piece id resolved via the version-pinned
`FoundationalPrefabMap`, physical-instance provenance, `StoneAreaMembership` result, success state, and
catalog version) into `FoundationalPlacementEvidence`, passes it through the hardened
`FoundationalPlacementAdapter`, and calls the existing `ProgressionCommandPipeline`. The operation id is
derived deterministically from the physical-instance provenance so re-observation/retry/restart converges
on the one recorded receipt. Authorization is the relationship-backed `RelationshipPlacementAuthorizer`
only; there is no permissive/test authorizer or client-authoritative fallback in production. The
net48-only `Features/Progression/FoundationalPlacementObserver` (a `Player.PlacePiece` postfix, server-
gated) supplies the observation; `FoundationalRuntimeBootstrap` composes the durable
`FoundationalProgressionServer` under a stable world-scoped server-owned path with startup rehydration.

**Dedicated-server ingress (T009R2, 2026-07-15).** The T009R `Player.PlacePiece` postfix is the
**listen-host** path only: on a listen/singleplayer host the placing player's `PlacePiece` runs on the
server, so that seam already carries a server-authoritative placement. A joined **dedicated**-server
client's build, however, never runs `PlacePiece` on the server — it replicates to the server as a ZDO —
so the server-gated postfix emits **zero** receipts for it. T009R2 adds a dedicated ingress that closes
this gap without ever trusting the client:

- The placing **client** fires a routed notice (`ZRoutedRpc`, method `SBPR_Niflheim_FoundationalPlacedNotice`)
  carrying ONLY an opaque physical-instance pointer (the placed piece's ZDOID string). The notice is a
  pointer, never authority.
- The **server** handler (`Features/Progression/DedicatedPlacementIngressObserver`, registered only where
  `IsServer()`) derives the sender principal from the **authenticated** routed sender peer — never the
  payload — and hands the opaque key to the engine-free
  `Application/Runtime/DedicatedPlacementIngress`.
- The ingress **independently re-derives** every credit-bearing fact from the server's own ZDO store via
  `IServerPlacedInstanceSource` (production: `ZdoServerPlacedInstanceSource` over `ZDOMan`): authoritative
  **existence** (a fabricated/stale key → `NoSuchInstance`), exact **prefab → stable catalog identity**
  (re-resolved through the version-pinned `FoundationalPrefabMap`), **creator/actor binding** (the ZDO's
  recorded creator MUST equal the authenticated sender principal, else `CreatorMismatch`), **position →
  Stone Area** membership (from the ZDO transform), **success/current-world** state (a resolvable resident
  ZDO is a materialized success), **exclusions/version** (enforced by the shared adapter), and the stable
  physical-instance **repetition key** (the ZDOID). It then routes the reconstructed
  `FoundationalPlacementObservation` through the **same** `FoundationalPlacementRuntime` — adapter →
  relationship-backed pipeline → durable receipt — so listen-host and dedicated paths share ONE
  server-validation core.
- **Startup/replication safety:** ingress is notice-driven, never a ZDO scan. A booting or replicating
  server generates no notice, so no previously-loaded piece is ever awarded — the vanilla distinction
  between "a client just placed this" (a live notice) and "the server loaded/replicated an existing ZDO"
  (no notice). Duplicate/replayed notices for one instance converge on the single receipt (deterministic
  ZDOID-derived operation id); a conflicting reuse of a credited instance rejects at the receipt layer.
  There is no client-authoritative fallback.

**Runtime corrections (T009R3, 2026-07-16).** Three runtime blockers in the T009R2 cut are corrected;
the revalidation core above is unchanged.

- **Live placement hook.** The placed instance is captured from the private static `Player.m_placed`
  list vanilla populates from the instantiated object, NOT the `Player.PlacePiece` `piece` argument (that
  is the build ghost/prefab, with no world ZDO or stamped creator). `Player.PlacePiece` returns `void`, so
  a reached postfix is itself the success signal (vanilla only calls it from `TryPlacePiece`'s success
  branch) — there is no `bool` result. `Features/Progression/PlacedPieceCapture.cs` reads the placed
  `Piece` from `m_placed`.
- **Authenticated creator identity.** Vanilla stamps a placed piece's creator with
  `Piece.SetCreator(Player.GetPlayerID())`, and `GetPlayerID()` returns the character ZDO's
  `ZDOVars.s_playerID` — a game-minted profile id, NOT the platform id in `peer.m_characterID.UserID`. The
  server resolves the authenticated sender's CHARACTER ZDO (from `peer.m_characterID`) and reads that same
  server-owned `s_playerID`, rendering it into the shared `ServerCreatorIdentity` principal space the ZDO's
  recorded creator also renders to, so the ingress's creator==sender binding compares two server-derived
  `s_playerID` values. The acting character id is the stable character ZDOID, never the mutable player
  name. Reconnect-stable: a new session's character ZDOID differs but the `s_playerID` is durable.

**Live relationship establishment (T009R3, 2026-07-16).** `RecordFoundationalPlacement` requires an active
Attunement (or Bond), but the live `FoundationalProgressionServer` boots with empty character/authority
projections — nothing in a real session could establish one. `RelationshipProvisioningIngress`
(`Application/Runtime`) is the smallest server-authoritative seam: it seeds an ABSENT character aggregate
(never overwriting existing progression) and drives the shipped `RelationshipCommandHandler` (the same
handler that boot-rehydrates the relationship journal) with a SERVER-DERIVED subject. It is restricted to a
playtest path: the net48 `Features/Progression/RelationshipProvisioningAdmin` registers its routed RPC ONLY
when the server-owned config flag `Progression.EnableAdminRelationshipProvisioning` is true (default false),
and even then accepts only an authenticated Valheim ADMIN sender (peer host on the server admin list — the
same gate as `RPC_Save`). The subject account (creator principal) and target Stone are re-derived from the
sender's server-owned character ZDO; there is no permissive authorizer, client-supplied identity, or
fabricated projection mutation. Disabled outside the playtest path (flag off ⇒ the handler is never
registered).

### `RecordAlignedActivity`

Used by server adapters for eligible Cooking, Crafting, Archer, or Warrior activity.

```text
AlignedActivityEvidence
  operationId
  authenticatedActor
  stoneId
  activityDefinitionId/version
  observedEventType
  exact source item/recipe/station/target/projectile identifiers as applicable
  server attribution and outcome
  Stone Area result if required
  committedTreeContext[]
```

The content definition determines whether the event awards:

- AP to an attuned character: N Personal + N Cumulative + N Mirrored Stone AP; and/or
- BP to a bonded character: N to that character's one Stone-wide personal BP balance.

No evidence record creates a source-Tree AP/BP wallet or Cultivation Target. Uncommitted optional candidates
cannot authorize activity credit; the protected Foundational family remains an ongoing low-value AP source.

## Facet and Tree-development commands

### `CommitTreeToFacet`

**Payload:** `facetId`, `treeId`, `treeVersion`, `paletteVersion`.

**Validates:** authenticated Governor, Responsibility Range, matching Facet category, empty Facet, eligible
candidate/current palette, Active Stone Level capacity, expected revision, no conflicting commitment, replay binding.

**Commits:** one Committed Tree with initial authored Tree Level, zero cumulative BP development, and node state
plus commitment provenance. It does not debit BP, alter Stone Level, purchase a node, or grant an effect.

### `ApplyBPToNode`

**Payload:** `treeId/version`, `nodeId/version`, BP amount or authored increment.

**Validates:** Governor and Responsibility Range, current commitment, node is developable and not unavailable,
Tree/Stone level requirements, personal BP, current-build definitions, revisions, and the provisional
successive-unlock cost step.

**Commits:** one BP debit, one node-development delta, and the same delta in cumulative qualifying Tree
investment. Crossing the configured cumulative threshold may advance Tree Level if Active Stone Level permits.
A completed Local Node may change the derived Local Effect; a completed Character/Permanent/Key node becomes
Offered to eligible attuned players. Neither creates a personal purchase.

### `SetSettlementLocalPolicy`

**Payload:** policy = `Everyone | Attuned | Private`, allowlist revision/list when Private.

**Validates:** Homestead owner authority, expected Stone revision, valid authenticated allowlist principals,
policy schema/version.

**Commits:** the single Settlement-wide policy used by all active Local Effects. There is no node-specific
override. Runtime eligibility is re-derived for current occupants. Placement capabilities still require
ordinary build Permission independently.

### `RevokeTree`

**Payload:** `facetId`, expected `treeId/version`, revocation reason code.

**Validates:** authorized Governor, Responsibility Range, optional Committed Tree (never Foundational), exact
Facet/Tree/version, expected revisions, no conflicting in-flight mutation.

**Atomic/recoverable result:**

- delete the Stone-owned commitment, cumulative BP development, node development, Local Nodes, and personal-node offerings;
- refund no BP;
- remove each affected refundable Character-Effect purchase;
- credit its AP value to that character's `StoneId + FacetId` Facet Credit;
- preserve Permanent Effects and Progression Keys with their provenance and no refund;
- vacate the Facet and record all affected character/Stone revisions.

A large fan-out may use a journaled multi-phase physical implementation, but its externally visible outcome
must be one convergent operation. Partial revocation is never exposed as success.

## Personal progression commands

### `PurchaseNode`

**Payload:** `treeId/version`, `nodeId/version`, expected `OfferedSetId/version`, payment source preference
(`PersonalAP` or matching `FacetCredit` where allowed).

**Validates:**

- authenticated character and active Attunement;
- current Committed Tree and content version;
- node is personal, Offered, executable, and not already acquired;
- Tree Level and Active Stone Level;
- same-Tree Attunement Tier Access derived from prior Offered-Set purchases;
- AP price and selected permitted balance;
- all authored objective/key/other requirements;
- expected Stone/character revisions.

**Commits:** one debit, one purchase, exact Offered-Set provenance, and one receipt. Then re-derive activation.
It does not store Attunement Tier Access or active-effect state as mutable ledgers.

### `ChooseWeaponDisciplineSkill`

**Payload:** `nodeId/version`, selected skill stable ID, choice-catalog version.

**Validates:** Weapon Discipline purchased/eligible, at least two authored choices in the current catalog,
selected skill offered, no prior committed choice for this grant identity, revisions, operation replay.

**Commits:** one permanent choice and one cap-provider provenance record. It cannot be spent twice and cannot
raise every melee cap.

## Read contracts

### `GetStoneProgressionView`

**Input:** authenticated caller, `StoneId`, optional known revision/token.
**Output:** the `ProgressionReadModel` defined in the data model, filtered only for legitimately private data.

Required sections:

- Stone identity/family/variant and fixture maturity;
- current revisions and registry versions;
- caller relationship, Responsibility Range, Facet use, active sibling conflict if actionable;
- Historical/Active Stone Level;
- Foundational Tree/catalog summary;
- Stone Facets, candidate palettes, commitments, Tree Levels, cumulative BP development and node development;
- Personal AP, Cumulative AP, personal BP, and Facet Credit for the caller;
- each node's exact outcome, status, price, requirements, Offered-Set/Tier state, and rejection reasons;
- Settlement-wide Local policy and separate Permission caveat;
- durable outcomes and choices;
- command affordances as hints only.

The server must revalidate commands even if the view reported an operation as available.

### `GetRelationshipPortfolio`

Future Stones-UI-shaped query returning all Stones related to the authenticated character plus compact
revisions/status and links/keys for full `GetStoneProgressionView` queries. This proof needs only enough shape
to demonstrate that the current Homestead commands are not bound to a nearby panel.

## Effect delivery contracts

These are derived-provider contracts, not direct ledger writes.

### Cooking

- `SavorTheHearthProvider`: policy-eligible occupant + inside this Stone Area + active Local Node
  ⇒ food timers consume elapsed time at factor 0.5. Exit/policy loss restores factor 1 immediately. No item/stat
  mutation or retroactive duration.
- `CookingCraftPolicy`: Field Prep eligibility plus normal Cooking skill XP, speed, and bonus-output behavior for
  unchanged Boar Jerky/Queen's Jam recipes through Bushcraft.
- `FoodRefreshThresholdProvider`: Iron Stomach supplies threshold 0.75, highest applicable provider wins; three
  slots and normal food debit remain.
- `MenuCraftDurationProvider`: Swift Preparation supplies factor 1/3 after vanilla Cooking-skill adjustment for
  eligible menu-crafted food only.

### Crafting

- `EffectiveStationLevelProvider`: Refined Workshop supplies +1 for eligible portable-item operations inside the
  active Homestead; real observed station level remains unchanged and visible.
- `WorkmanshipIssuanceProvider`: active Masterwork may issue one deterministic property on an eligible exact
  non-stackable durable output.
- `DurabilityIssuanceProvider`: acquired Built to Last supplies the configured maximum-durability property on
  future eligible outputs after relationship loss as well.
- Both item providers bind a server-validated `ItemProvenanceId`, survive upgrade/transfer where valid, explicitly
  dirty persistence, and degrade tampered/unknown metadata to vanilla behavior.

### Archer

- `PracticeRangeProvider`: inside the active Homestead, eligible users with ordinary build Permission receive the
  exact Archery Target placement and Practice Arrow recipe capability.
- `BushcraftRecipeProvider`: active Field Fletching I exposes unchanged Wood Arrows through Bushcraft.
- `ProjectileRecoveryProvider`: Fletcher's Habit makes one authoritative terminal-impact decision for one exact
  consumed eligible arrow; deterministic Practice Range return suppresses this roll.

### Warrior

- `LocalPlacementProvider`: T.W.I.G. Training grants exact T.W.I.G. placement inside the Homestead and remains
  Permission-gated.
- `EquipDurationProvider`: Ready Hands modifies copied queued equip and unequip durations for authored eligible
  melee weapons only; no shared prefab mutation.
- `SkillCapProvider`: Weapon Discipline supplies the one selected authored cap tier, highest-wins.

## Rejection vocabulary

Stable machine codes are part of the contract; localized text is presentation.

| Code | Meaning |
|---|---|
| `Unauthenticated` | No trusted connection principal |
| `PrincipalMismatch` | Claimed account/character differs from authenticated principal |
| `StoneNotFound` | Stable Stone identity is absent or unavailable |
| `CharacterNotFound` | Server-owned character subject unavailable |
| `SiblingCharacterActive` | Another character on this account holds Bond or Attunement here |
| `RelationshipRequired` | Required active Bond/Attunement missing |
| `RelationshipConflict` | Requested relationship conflicts with current state |
| `RelationshipCapacityExceeded` | No matching Bond/Attunement Slot |
| `Unauthorized` | Caller lacks owner/Governor/participant authority |
| `OutsideResponsibilityRange` | Governor cannot mutate this Tree/node |
| `StaleStoneRevision` | Stone snapshot changed |
| `StaleCharacterRevision` | Character snapshot changed |
| `StaleAuthorityRevision` | Account–Stone index changed |
| `OperationConflict` | Operation ID reused with different binding/payload |
| `ContentVersionMismatch` | Definition/catalog/Offered Set is stale or unknown |
| `FacetOccupied` | Stone Facet already has a Committed Tree |
| `FacetCategoryMismatch` | Tree does not fit the requested Facet |
| `TreeNotEligible` | Candidate absent from the current Facet palette |
| `TreeNotCommitted` | Operation requires a current commitment |
| `ProtectedTree` | Foundational Tree cannot be revoked |
| `ActiveStoneLevelTooLow` | Stone cap blocks the operation |
| `TreeLevelTooLow` | Tree has not reached required level |
| `PriorOfferedSetIncomplete` | Same-Tree prior personal Offered Nodes incomplete |
| `NodeUnavailable` | Authored but first-build-unavailable |
| `NodeNotOffered` | Node is Local, unavailable, or not in caller's Offered Set |
| `AlreadyAcquired` | Unique purchase already exists |
| `InsufficientPersonalAP` | Personal AP cannot fund purchase |
| `InsufficientFacetCredit` | Matching Facet Credit insufficient or wrong Facet |
| `InsufficientBP` | Caller-owned Stone-wide BP insufficient |
| `RequirementNotMet` | Authored non-price requirement failed; include IDs |
| `PermissionDenied` | Ordinary build/access Permission failed in addition to Local policy |
| `EvidenceInvalid` | Server event/source/area/outcome validation failed |
| `EvidenceIneligible` | Valid event is outside current Foundational/Committed activity set |
| `ItemProvenanceInvalid` | Item capability receipt missing, tampered, duplicated, or unknown to the current build |
| `ChoiceAlreadyCommitted` | Permanent choice cannot be spent again |
| `RecoveryRequired` | Invariant/journal state requires operator reconcile/quarantine |

## Notification contract

After a committed operation, publish a bounded invalidation/event containing stable entity IDs, new revisions,
and result code. Do not broadcast entire character ledgers or trust notification order as authority. Clients
that miss or reorder notifications fetch the current read model.

## Security and hostile-client contract

The verifier must attempt:

- account/character/Stone substitution;
- forged PlayerID or client profile balance;
- replay before/after acknowledgement and after restart;
- operation-ID collision with a different payload;
- stale revision races from two clients;
- negative/overflow amount and unauthorized cross-character BP spend;
- purchase of Local/unavailable/unoffered nodes;
- remote fabrication of placement/craft/combat/projectile evidence;
- tampered item property or cap choice identity;
- client refusal or disconnect during each mutation phase.

Every attempt must either return the prior recorded result or reject without gameplay mutation.

## Contract-test minimum

Before an implementation tracer is accepted, tests cover:

1. one success, every named rejection, and exact revision/result behavior;
2. same-operation replay, conflicting replay, and process-kill recovery;
3. two-client race on the same expected revision;
4. save/reload, relog, server restart, and explicit reset of incompatible unreleased test data;
5. relationship loss/rejoin and active sibling exclusivity;
6. aggregate invariants and derived-view rebuild;
7. smallest joined-client/in-world evidence for each of all 13 executable nodes; one representative proof for
   a multi-node Tree tracer is insufficient.
