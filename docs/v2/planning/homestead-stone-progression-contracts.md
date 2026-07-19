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

**Transport-bound identity, Stone Areas, and the replication race (T009R4, 2026-07-16).** An independent
adversarial review of the T009R3 cut (PR #313, closed) found five remaining LIVE blockers. The revalidation
core and the listen-host path are unchanged; the integration edges are corrected as follows.

- **Production Stone Area registration (Blocker 1).** `FoundationalProgressionServer.StoneAreas` starts
  EMPTY and only tests called `Register(...)`, so on a real server every placement resolved
  `OutsideStoneArea` and nothing could be credited. The engine-free `StoneAreaRegistrar`
  (`Domain/StoneProgression`) reconciles the membership to exactly the CURRENT resident Stone facts —
  register new, update moved, unregister removed, idempotent per pass. The net48
  `HomesteadStoneWorldPlacement` reconcile pass enumerates resident Stone ZDOs (each carries its host-zone
  `StoneId` inputs + world-position center) and drives the registrar on startup and the periodic
  realization cadence. No test-only prepopulation ships in production.
- **Transport-bound sender identity (Blocker 2).** Vanilla `ZRoutedRpc.RoutedRPCData.m_senderPeerID` is
  serialized by the CLIENT and `RPC_RoutedRPC` never validates it against the delivering `ZRpc`, so a routed
  handler's `sender` is forgeable. High-value placement/provisioning authority now rides a DIRECT per-peer
  `ZRpc` handler registered at `ZNet.OnNewConnection`; the server resolves the exact authenticated `ZNetPeer`
  by matching `m_rpc` reference identity (vanilla's own `ZNet.GetPeer(ZRpc)` seam). From that peer it derives
  the ACCOUNT = authenticated socket host id (platform/Gate-A subject) and the CHARACTER = the character
  ZDO's durable `s_playerID` rendered as `player:<s_playerID>`. A placed piece's ZDO `s_creator` (stamped
  from the placing character's `s_playerID`) binds to the CHARACTER subject, NOT the account. A client
  payload carries only the candidate instance pointer / command discriminator; it can never choose account,
  character, Stone, position, prefab, creator, or permissions. Hostile spoof tests prove a forged peer id /
  admin identity cannot redirect authority.
- **Stable reconnect semantics (Blocker 3).** The live character ZDOID changes every session and must never
  be the durable subject. Relationships and receipts are keyed under the stable `player:<s_playerID>`
  character subject. `ProvisioningOperationBinding` derives the provisioning operation id from ALL material
  fields (account, stable character, Stone, command, requested range, world scope), so an exact retry
  replays and any changed binding is a DISTINCT operation that conflicts intentionally. Reconnect/restart
  preserves authorization rather than orphaning it.
- **Executable, correctly admin-gated provisioning (Blocker 4).** The playtest provisioning seam is now
  invokable via the client console command `sbpr_provision attune|bond` (registered on `Terminal.InitTerminal`),
  which sends the command discriminator on the server connection to the transport-bound handler. It remains
  DEFAULT OFF (`Progression.EnableAdminRelationshipProvisioning`, server-owned) and server-admin only. Admin
  identity is matched with vanilla-normalized semantics via `VanillaAdminIdentity.ListContainsId` — a
  clean-room reproduction of `ZNet.ListContainsId` (platform-qualified OR bare user id on the server's
  platform) — NOT raw `GetAdminList().Contains(host)`. It drives the shipped `RelationshipCommandHandler`;
  no permissive authorizer or projection mutation.
- **ZDO replication race (Blocker 5).** A joined client's placement notice beats ZDO replication (ZDO
  transmit happens later on the `ZDOMan.Update` cadence), so an inline ingest failed `NoSuchInstance`
  permanently. The transport-bound handler now captures the authenticated identity + physical ZDOID into the
  bounded `PendingRevalidationQueue` and defers the credit-bearing ingest. A pump on `ZDOMan.Update` retries
  the shared revalidation ONLY until the authoritative ZDO appears or a short configured deadline expires
  (default 30s), then runs the full revalidation once. Duplicate notices converge on one entry (keyed by
  character subject + ZDOID); a timeout writes no credit; the queue is bounded against spam; and because it
  is purely in-memory, a restart starts empty and never scans/awards old pieces.

**Bound-principal provisioning + delimiter-safe journal framing (T009L2, 2026-07-18).** A real
joined-GPU-client run (evidence `T009L2-FAIL.md`) proved the merged authored Stone path works but the
progression path did not: an admitted, attuned real placement failed `RelationshipRequired` with zero AP,
and a post-restart re-provision returned `Applied` instead of `Replayed`. Two integration blockers, both
now closed; the revalidation core, the authored Stone seat, and the placement architecture are unchanged.

- **Single principal space for provisioning and placement (Blocker 1).** Placement authorizes under the
  BOUND INTERNAL `(AccountId, CharacterId)` admission publishes into `BoundSessionPrincipalIndex` (IAP-007
  Tracer 3), keyed by the server-owned `player:<s_playerID>` peer key. Provisioning previously created the
  Attunement under a DIFFERENT space — the raw provider/socket account subject plus `player:<s_playerID>`
  character subject (`AuthenticatedSenderBinder`) — so the relationship the placement needed did not exist
  under the placement's identity. `RelationshipProvisioningAdmin` now reads only the peer's durable
  `s_playerID` to form the peer key, resolves the SAME bound internal principal from `BoundSessions`, and
  provisions under it. An UNBOUND peer (no admitted, activated internal session) FAILS CLOSED — no
  provider/platform fallback principal is ever derived. No raw provider/profile subject enters the gameplay
  relationship, journal, AP receipt, or operator log: the provisioning log line now carries only a
  pseudonymous `ProvisioningOperationBinding.CorrelationTag` (a short SHA-256 digest of the bound internal
  ids), never the account/character verbatim and never the provider subject or raw `s_playerID`.
- **Delimiter-safe relationship-journal framing (Blocker 2).** `ProvisioningOperationBinding.OperationId`
  legitimately embeds literal `|` (it joins material fields including a `StoneId` such as
  `uid:-898655635|3|2` and the world scope). `RelationshipCommandHandler.Record` wrote that operation id —
  and the `ResultCode` — UNENCODED into a pipe-delimited record, while `ParseRecord` required exactly 14
  fields; a real op exploded a record into 21 fields and the parser rejected every CRC-valid frame, so the
  Attunement was process-local despite fsynced writes. The framing invariant is now general: EVERY
  free-text field (operation id, result code, account, character, Stone, relationship id, snapshots) is
  base64-encoded before entering the pipe-delimited frame and decoded symmetrically, so the field count is
  exactly 14 for ANY operation id. A torn/malformed frame (bad field count, bad tag, non-base64 field, or
  an overflowing revision) is rejected honestly as an unparsed record — never partially applied — and the
  CRC-framed reader still recovers every intact committed frame before a torn tail. Restart rehydration
  recovers the committed op and an exact re-provision returns `Replayed`. This is unreleased QA state, so
  no production migration policy is introduced; the framing simply round-trips correctly from now on.

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

**Validates:** Homestead owner authority (server-validated, never client-authored — a bonded Governor or
attuned player who is not the owner is `Unauthorized`), expected Stone revision, expected policy revision
(`StalePolicyRevision` on a concurrent/replayed policy write), valid authenticated allowlist principals,
policy schema/version.

**Commits:** the single Settlement-wide policy used by all active Local Effects, with the policy revision
incremented by one. There is no node-specific override. Runtime eligibility is re-derived for current
occupants (never stored as a per-effect purchase). Placement capabilities still require ordinary build
Permission independently. The active/dormant projection is derived on demand, never a second ledger; every
reject is zero-mutation and a replayed operation returns the recorded result with no second revision bump.

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
  unchanged Boar Jerky/Queen's Jam recipes through Bushcraft. **Implemented (T017,
  `Adapters/Cooking/CookingCraftPolicy.cs`):** the shared Cooking-aware Bushcraft policy's first consumer. Field
  Prep is a personal Character Effect, so `Resolve(stone, character, authority)` derives active/dormant through the
  shipped T004 `DerivedActivationView` (a purchase record for the node at this Stone AND an active relationship —
  neither the Settlement Local policy nor build Permission is a conjunct, unlike the Local Savor/Practice Range
  gates). While active it exposes the UNCHANGED vanilla `BoarJerky` and `QueensJam` recipes through Bushcraft
  (station-free); it is an exposure gate only — `PreservesVanillaInputsYieldAuthority` and
  `PreservesNormalCookingXpSpeedBonus` are always true, so the recipes' ordinary inputs/yield/authority and the
  normal Cooking XP/craft-speed/bonus-output mechanics are untouched. Pure/no ledger: flip the relationship and
  re-derive with zero writes. **Live-wired (T017, net48):** `Features/Cooking/FieldPrepRecipeGate` postfixes
  `Player.RequiredCraftingStation` to rescue exactly those two recipes to station-free for the LOCAL occupant when
  the pure policy reports Field Prep active, reading the authoritative host projection (composed
  `LocalProgressionObserver.Server` stores) and failing closed off-host / outside every Stone Area / without an
  active purchase. A personal-effect client delivery channel is a follow-up (the bounded transport carries
  Local-effect snapshots only), exactly as the sibling Field Fletching / Refined Workshop seams documented.
- `FoodRefreshThresholdProvider`: Iron Stomach supplies threshold 0.75, highest applicable provider wins; three
  slots and normal food debit remain. **Live-wired (T018, net48):** the raise is delivered by TWO seams that
  MUST agree — a postfix on `Player.CanEat` (the outer gate) AND a transpiler on `Player.EatFood` that rewrites
  the SINGLE inner `Player.Food.CanEatAgain()` 0.5 guard (decomp 17486) to `IronStomachRefreshGate.ShouldRefreshOnEat`.
  The EatFood seam is load-bearing: `EatFood` is the ACTUAL refresh path (it resets `m_time/m_health/m_stamina/m_eitr`
  for a matching food), and `Humanoid.ConsumeItem` debits the food from inventory UNCONDITIONALLY after calling it —
  so a CanEat-only patch leaves the in-band refresh a silent no-op that consumes the item without refreshing it
  (node-own live-QA defect t_6b73a3de). `ShouldRefreshOnEat` returns vanilla's own verdict UNCHANGED (never lowered)
  and additionally permits the 0.5..0.75 in-band refresh only for a durable-Iron-Stomach LOCAL occupant, reading the
  authoritative host projection (composed `LocalProgressionObserver.Server` character store) and failing closed
  off-host / without a durable purchase. Boundary-inclusive at 0.75, deny-above, so the gate and the refresh path
  agree across the band; the three-slot cap and normal debit/stats/duration stay entirely in vanilla `EatFood`.
- `MenuCraftDurationProvider`: Swift Preparation supplies factor 1/3 after vanilla Cooking-skill adjustment for
  eligible menu-crafted food only.

### Crafting

- `EffectiveStationLevelProvider`: Refined Workshop supplies +1 for eligible portable-item operations inside the
  active Homestead; real observed station level remains unchanged and visible. **Implemented (T021,
  `Adapters/Crafting/EffectiveStationLevelProvider.cs`):** a pure `Resolve(...)` returns both the unchanged real
  level and the derived effective level; the +1 is granted only when the Refined Workshop Local Effect is
  currently active for the occupant (via `LocalEffectActivationView`) AND the operation is one of the three
  portable-item kinds (production/upgrade/repair) on an eligible portable item AND a real station is present
  (level ≥ 1). Structure production and build placement never receive it, an ineligible item never receives it,
  the +1 never conjures a station, and it never mutates the real level or satisfies a Stone-level place-state
  objective. **Live-wired (T021 remediation, net48):** the pure provider is now consumed on a joined client by
  `Features/Progression/RefinedWorkshopStationLevelPatch` — a postfix on `Player.RequiredCraftingStation` that
  rescues an eligible-portable level-only shortfall with the provider's effective level, and a postfix on
  `InventoryGui.SetupRequirementList` that recolors the required-level text to the base (satisfied) color when
  the +1 satisfies it (real vs +1 distinction; the required-level number and real station level are untouched).
  The activation bit is read exclusively from the replicated `LocalActivationClientCache` (server-stamped over
  the bounded delivery transport, now registered in `Plugin`), so the client re-derives nothing and fails closed
  outside every Stone Area / with no snapshot. The single authority is the shared boolean
  `EffectiveStationLevelProvider.Resolve(active, realLevel, operation, itemIsEligiblePortable)` overload both the
  server view path and the client patch call. Listen-host self-delivery is a follow-up (the peer-to-peer
  transport does not round-trip to the host itself); the proven effective-Level-3 topology is a dedicated server
  with a joined client.
- `WorkmanshipIssuanceProvider`: active Masterwork may issue one deterministic property on an eligible exact
  non-stackable durable output.
- `DurabilityIssuanceProvider`: acquired Built to Last supplies the configured maximum-durability property on
  future eligible outputs after relationship loss as well.
- Both item providers bind a server-validated `ItemProvenanceId`, survive upgrade/transfer where valid, explicitly
  dirty persistence, and degrade tampered/unknown metadata to vanilla behavior.

### Archer

- `PracticeRangeProvider`: inside the active Homestead, eligible users with ordinary build Permission receive the
  exact Archery Target placement and Practice Arrow recipe capability. The capability is the load-bearing AND of
  the active Practice Range Local Effect (derived through the single Settlement Local policy + relationship/
  governance/level dormancy, never a second ledger) and the occupant's ordinary build Permission — policy
  eligibility alone or build Permission alone unlocks neither. The Practice Arrow recipe is exactly 100 arrows for
  8 Wood; the Practice Arrow contributes 0 ammo damage while the fired shot retains the bow's own draw damage; and
  a practice arrow that terminally impacts the Archery Target is deterministically returned exactly once (no roll),
  which is the path a later Fletcher's Habit recovery roll must yield to. The exact vanilla build-piece prefab is
  `piece_ArcheryTarget` (capital A/T — corrected from the earlier `piece_archery_target`); the Practice Arrow item
  `ArrowPractice` is new SBPR content (not a vanilla arrow id). The net48 runtime seam
  (`Features/Archer/ArcherContent` + `ArcheryTargetPlacementGate` + `ArcherContentRegistrar`) makes this joinable:
  the Practice Arrow item/recipe are registered additively (ADR-0006), 0 ammo damage is data-driven (zero-damage
  Ammo item), the deterministic return is wired via the vanilla `ArcheryTarget.m_returnAmmo` list, and the
  placement AND is enforced by a `Player.PlacePiece` gate. That gate holds NO parallel Local-effect ledger and
  re-derives nothing: it evaluates ordinary build Permission via vanilla `PrivateArea.CheckAccess`, and reads the
  active Local Effect from the authoritative activation runtime — on the host it `Fetch`es the per-occupant read
  model from `LocalActivationService` (occupant/occupancy/governance/owner composed server-side), and on a pure
  client it consumes the server-delivered snapshot via `LocalActivationClientCache`. Both fail closed absent an
  authoritative active projection.
- `BushcraftRecipeProvider`: active Field Fletching I exposes unchanged Wood Arrows through Bushcraft.
  **Implemented (T026, `Adapters/Archer/BushcraftRecipeProvider.cs`):** a pure `Resolve(stone, character,
  authority)` returns a capability whose `WoodArrowRecipeExposed` mirrors whether the personal Field
  Fletching I Character Effect is active for the caller — derived through the shipped T004
  `DerivedActivationView` (a purchase record for `FieldFletchingI@1` at this Stone AND an active
  relationship to it; no second active-effects ledger). While active it exposes the EXACT unchanged vanilla
  Wood Arrow recipe (`ArrowWood`) made station-free (Bushcraft); it authors and mutates NOTHING about the
  recipe's ordinary inputs, yield, or authority — it is an exposure gate only (spec line 160; research.md
  defers wider ammunition/input changes to later Field Fletching levels). Dormant/unpurchased/undeveloped
  callers, and a sibling character's reservation, all expose nothing. **Live-wired (T026, net48):** the pure
  provider is consumed on the authoritative host by `Features/Archer/FieldFletchingRecipeGate` — a postfix
  on `Player.RequiredCraftingStation` that rescues the exact vanilla Wood Arrow recipe to station-free when
  the provider reports it exposed for the local occupant. **Pure-client delivery (T026 remediation,
  `t_3a899381`):** the host-only lookup was replaced by a bounded authoritative Personal Character-Effect
  delivery channel so a real joined (non-host) client can craft — the T026 review (PR #373) correctly
  refused merge while Field Fletching I was host-occupant-only. The gate now resolves exposure two ways,
  both authoritative and both fail-closed: on the authoritative HOST it reads the composed server stores
  (`LocalProgressionObserver.Server`) directly through the pure provider; on a PURE CLIENT it reads ONLY the
  server-stamped `PersonalActivationSnapshot` the server pushed into
  `LocalProgressionObserver.PersonalClientCache` over the `PersonalActivationDeliveryObserver` transport,
  requesting a fresh snapshot for the Stone the local player stands in on a bounded interval. The delivery
  substrate (`Application/Activation/PersonalActivationDelivery.cs` + `PersonalActivationService.cs` +
  `PersonalActivationClientCache.cs`, composed into `LocalProgressionServer.PersonalActivation`) derives the
  per-`(occupant, character)` read model from the authoritative Stone/character/authority aggregates via the
  same shipped `DerivedActivationView` — a purchase record AND an active relationship, per character, no
  second active-effects ledger. It preserves Personal ownership semantics: unlike the Local channel it is
  NOT gated by occupancy, the Settlement Local policy, or governor presence; the client authors no
  entitlement; stale/reordered snapshots are dropped by a monotonic delivery sequence; and relationship
  loss / disconnect / dormancy flip Active to false with zero writes (the client cache invalidates and
  clears on teardown). The server resolves the requesting peer's BOUND INTERNAL principal from the
  delivering ZRpc, never the payload, so a hostile client cannot forge whose effect it asks for or author
  an active row. Listen-host and pure-client consumers share the one provider/derivation; there is no second
  ledger on either side.
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
| `StalePolicyRevision` | Settlement Local policy revision changed under a concurrent/replayed policy write |
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

**Implementation (shared Local Effect runtime substrate, `t_02c13405`).** The bounded delivery seam is
`Application/Activation/LocalActivationDelivery.cs` + `LocalActivationService.cs` + `LocalActivationClientCache.cs`:

- `LocalActivationNotification` is the bounded invalidation event: stable `StoneId` + occupant `AccountId`, the
  new Stone and policy revisions, a monotonic per-occupant delivery `Sequence`, and a result code — never the
  full read model, never a copied active-state ledger.
- `LocalActivationSnapshot` is the per-occupant read model a client refetches. It is a pure projection of
  `LocalEffectActivationView` (Stone-owned developed state + derived active/dormant/policy-eligible per Local
  node) carrying the authoritative revisions + delivery sequence. `Denied(...)` is the fail-closed empty,
  all-inactive snapshot returned when authority is missing/stale.
- The client cache applies a snapshot only when its `Sequence ≥` the last applied one (stale/reordered
  dropped) and decides refetch from a notification whose sequence or revisions moved ahead. Clients never
  author activation. The net48 transport is `Features/Progression/LocalActivationDeliveryObserver.cs`: the
  client requests by Stone id ONLY, and the server resolves the requesting peer's identity **and** current
  position server-side from its own character ZDO (occupancy is server-owned — a client cannot forge x/z to
  claim it stands inside any Area), then derives from authoritative state and replies, failing closed when
  peer/ZDO/position authority is unavailable. The owner and Stone-wide authorized-Governor-presence facts the
  derivation consumes are themselves derived from committed relationship/authority state
  (`Application/Activation/GovernorPresenceResolver.cs`), never a separately-mutated flag, so a released
  Governor bond immediately dormants delivery and owner is never conflated with governor presence.

**Implementation (isolated-QA develop/purchase ingress, `t_79588427`).** The delivery substrate above
composes the accepted Facet/Activity/Development/LocalPolicy handlers and the `PurchaseCommandHandler` into
the live `LocalProgressionServer`, but the T021 joined-client rerun (`tracer-6-crafting/T021-JOINED-CLIENT-RERUN-FAIL.md`)
proved those handlers + `LocalNodeProvisioningDriver` had **zero runtime callers**, so a Stone-cultivated Local
node (Refined Workshop) could never reach Developed at runtime and its Local Effect could never derive Active —
the positive effective-Level-3 path was structurally unreachable. `Application/Runtime/LocalProvisioningIngress.cs`
is the smallest server-authoritative seam that closes it, mirroring `RelationshipProvisioningIngress`:

- `DevelopLocalNode` seeds ONLY the bare pre-progression Stone envelope when the Stone aggregate is absent (the
  empty owner row the accepted commands require — never a node-state write, never overwriting an existing or
  boot-rehydrated Stone), then drives `LocalNodeProvisioningDriver` (commit Tree → credit BP → develop node) to
  completion through the shipped receipt-backed handlers. A developed node survives a restart via the durable
  Facet/Development journals, never the seed.
- `PurchaseNode` routes a personal Offered-node purchase through the accepted `PurchaseCommandHandler` (its own
  durable `node-purchase.journal`), so the purchase authority (active Attunement required — Bond alone rejects
  `RelationshipRequired`), revision, and idempotency gates are a real reachable caller.
- The net48 seam is `Features/Progression/LocalProgressionProvisioningAdmin.cs`: a DIRECT per-peer `ZRpc`
  handler (`SBPR_Niflheim_ProvisionLocalNode`) + the `sbpr_develop refined` console command, registered ONLY
  when the server-owned `Progression.EnableAdminLocalNodeProvisioning` flag is true (default false) AND the
  transport-authenticated sender is a normalized server ADMIN. Identity is the peer's bound-internal principal
  (never the forgeable routed sender / a client claim); the target Stone is resolved from the peer's server-owned
  character ZDO position. Outside that gate the handler is never registered or rejects — production fails closed.
  No provisional activation, no direct node-state write, no second ledger, no bypass of Local policy/governance/
  dormancy; Refined Workshop mechanics are unchanged.

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
