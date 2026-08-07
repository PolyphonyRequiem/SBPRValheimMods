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
- **the acting character is standing at the Stone, checked by the SERVER** (ADO #138): the server resolves the
  acting character's own world position and confirms it lies inside the TARGET Stone's Area; a client claim is
  never accepted, an unknown position or an unregistered Area fails closed, and standing inside a *different*
  Stone's Area does not authorize this one. Rejects `NotAtStone` with no mutation;
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

Attunement formation is a proximate act on the same server-checked terms as `CreateBond` (ADO #138): requesting
it requires the acting character to actually be at that Stone, and the server decides that from its own position
and Stone Area facts.

For this Homestead proof, use the same active sibling-exclusivity rule as `CreateBond`, consume an Attunement
Slot, and grant no cultivation authority. The proof participant must be on a different authenticated account
from the bonded owner. The exclusivity rule is variant-authored rather than universal: Community Stone
Attunement permits sibling characters, while Community Bond remains account-exclusive for now.

### `ReleaseRelationship`

**Payload:** `relationshipId`, expected status.

Release is deliberately **not** proximity-gated (ADO #138). Releasing is not the proximate act, and gating it
would strand a character who released away from the Stone.

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

**Delimiter-safe framing generalised to every command journal (ADO #127, 2026-08-04).** The T009L2
Blocker-2 fix above was applied to `RelationshipCommandHandler` only. The six sibling handlers — a
duplicated copy of the same protocol in each — kept writing `OperationId` and `ResultCode` raw, so the
identical defect remained live in six journals:

| Handler | file | journal |
|---|---|---|
| `ActivityCommandHandler` | `ActivityCommands.cs` | `aligned-activity.journal` |
| `FacetCommandHandler` | `FacetCommands.cs` | `facet-commit.journal` |
| `LocalPolicyCommandHandler` | `LocalPolicyCommands.cs` | (Settlement local policy) |
| `DevelopmentCommandHandler` | `DevelopmentCommands.cs` | `node-development.journal` |
| `PurchaseCommandHandler` | `PurchaseCommands.cs` | `node-purchase.journal` |
| `WeaponDisciplineCommandHandler` | `PurchaseCommands.cs` | (weapon-discipline choices) |

This is data LOSS, not a recovery inconvenience: the progression projection stores are in-memory only
(`InMemoryCharacterApStore`, `InMemoryStoneAggregateStore`, `InMemoryAccountStoneAuthorityStore`) and are
rebuilt from these journals at server boot, so the journal IS the authoritative save. Every `StoneId` is
`world|zoneX|zoneZ` by construction (`ProgressionIdentity.FromHostZone`), so an operation id composed from
one embeds `|` as its NORMAL shape — not as hostile input.

All six now apply the same invariant as the relationship handler: EVERY free-text field is base64-encoded
on write and decoded symmetrically in `ParseRecord`, keeping the field count exact for ANY operation id;
digest fields (hex), boolean fields (`0`/`1`) and integer fields cannot contain `|` and stay raw. Each
handler's parse is wrapped in the same `FormatException`/`OverflowException` guards, so a malformed record
is rejected honestly as unparsed rather than throwing. Named acceptance `AT-JOURNAL-DELIMITER-SAFE` covers
all six with a piped operation id round-tripping write → `RehydrateFromJournal` → identical state.

Two adjacent invariants are now pinned explicitly:

- `AT-JOURNAL-NO-CROSS-CONTAMINATION` — the framing is fail-closed at the FRAME layer and fail-honest at
  the RECORD layer, deliberately. A torn/CRC-invalid frame truncates the read there (an append-only log
  with a corrupt length prefix cannot be resynchronised without guessing at durable data), while a
  well-framed but content-malformed record is skipped individually and the surrounding valid records still
  replay. Both halves are now tested rather than assumed.

  **Coverage extended to all seven handlers (ADO #129).** As landed with #127 this invariant was
  exercised for exactly ONE of the seven command handlers — `RelationshipCommandHandler`, the same lone
  sibling that had received the #127 delimiter fix first. That is the fix-inheritance gap: the handler
  with the test is the handler with the fix, and the other six were merely presumed correct. #129 closes
  it. `AT-TORN-FRAME-ALL-SEVEN` now asserts the full contract against every handler, through one shared
  test harness (`tests/JournalCorruptionHarness.cs`) so the seven cannot drift apart:

  | Handler | record tag | fields | torn tail | CRC-invalid frame | malformed content | covered since |
  |---|---|---|---|---|---|---|
  | `RelationshipCommandHandler` | `RELREC` | 14 / 16 | ✅ | ✅ | ✅ | pre-#129 (restated via harness) |
  | `LocalPolicyCommandHandler` | `LOCALPOLICYREC` | 11 | ✅ | ✅ | ✅ | ADO #129 |
  | `FacetCommandHandler` | `FACETREC` | 12 | ✅ | ✅ | ✅ | ADO #129 |
  | `ActivityCommandHandler` | `ACTIVITYREC` | 13 | ✅ | ✅ | ✅ | ADO #129 |
  | `PurchaseCommandHandler` | `PURCHASEREC` | 15 | ✅ | ✅ | ✅ | ADO #129 |
  | `WeaponDisciplineCommandHandler` | `WEAPDISCREC` | 14 | ✅ | ✅ | ✅ | ADO #129 |
  | `DevelopmentCommandHandler` | `DEVELOPREC` | 19 | ✅ | ✅ | ✅ | ADO #129 |

  Sub-acceptances: `AT-TORN-FRAME-PRIOR-RECORDS-SURVIVE` (a committed op written BEFORE the corruption
  rehydrates into identical state, per handler) and `AT-TORN-FRAME-NO-THROW` (no handler throws on any
  corruption shape).

  **Result: all six previously-uncovered handlers were already correct. No production behaviour was
  changed by #129.** The shared protocol was already right in each copy; what was missing was the proof.
  The deliverable is six handlers now provably correct where before they were presumed correct.

  Two properties of the tests are worth recording, because the first draft got them wrong and passed
  anyway. (a) Corruption spliced PAST the last committed record is invisible — with the CRC check
  deleted the reader simply hands the garbage payload to `ParseRecord`, which rejects it regardless, so
  the observable outcome is unchanged. The frame-layer shapes are therefore spliced BETWEEN two committed
  records and the test asserts the SECOND is UNREACHABLE; that unreachability is the observable
  consequence of fail-closed truncation and is what makes the assertion bite. (b) A short-record test
  whose filler fields fail base64 or integer parsing never reaches the missing field, so the field-count
  guard looks redundant; the filler must be valid as BOTH (`"1234"`) for the guard to be the thing under
  test. Verified by `scripts/ado129-mutation-evidence.py`, which deletes the CRC term and the
  field-count guard from each of the six handler files in turn: 12/12 mutations turn the suite RED.

  **Not proven:** these are unit-level assertions over SYNTHETICALLY corrupted journal files. No live
  mid-write process kill on a running dedicated server was reproduced, so this does not prove the live
  boot path behaves identically end to end.

  **Adjacent, deliberately out of scope.** Three further journal writers exist outside
  `Application/Commands/` — `Application/Receipts/OperationReceiptStore.cs` (`foundational-ap.journal`),
  `Persistence/Accounts/PilotAccountStore.cs` (`pilot-account.journal`), and
  `Application/ResourceDelivery/StoneConnectionSourceRegistry.cs` (`connection-sources.journal`). They
  were audited during #127 and were already encoding their free-text fields correctly. `OperationReceiptStore`
  additionally carries its own torn-frame coverage in `tests/NiflheimProgressionRecoveryTests.cs`. "All
  seven" means the seven command handlers; extending this harness to the other three needs a new card.
  Extracting the shared journal PROTOCOL into production code is ADO #128, now DECIDED and landed —
  see the dedicated section after the back-compat note below. The shared harness above is TEST-only and carries none of the
  correlated-failure risk that decision had to price.

- `AT-ZDO-DERIVED` — `ZdoStoneProgressionStore` projects the Mirrored Stone AP scalar onto the world
  Stone's ZDO. That is the only place progression data lands in vanilla persistence and hence the only seam
  where journal-vs-world drift could occur. It remains OWNER-ONLY, accumulate-only, and DERIVED: rebuilding
  the sink from the journal alone reproduces the exact total with no ZDO read in the path, so a stale,
  absent, or tampered ZDO cannot change the answer.

**Back-compat:** base64-encoding changes the on-disk bytes, so pre-fix journals do not round-trip. This
costs nothing, because every pre-fix journal on the QA box is ALREADY unparseable by its own handler —
210/210 records across the live and archived QA journal sets fail their handler's strict field-count check
(e.g. `ACTIVITYREC` records carrying 15 fields where the parser demands 13). There is no corpus of
readable pre-fix journals to preserve. This fix PREVENTS future corruption; it does not repair journals
already written with raw pipes, and no such recovery is claimed.

**Shared durable framing extracted — `CommandJournalFraming` (ADO #128, 2026-08-05).**

The frame format that all seven command handlers wrote — `[int32 payloadLength][uint32 crc32(payload)]
[payload UTF-8]`, appended with `fs.Flush(true)` and read with truncate-at-first-damage — existed as six
byte-for-byte identical private copies (verified: after comment stripping, `Append`, `ReadDurable`,
`Encode`, `Decode`, `Digest`, `BuildCrcTable`, and `Crc32` hash-identical across `PurchaseCommands.cs`,
`DevelopmentCommands.cs`, `RelationshipCommands.cs`, `FacetCommands.cs`, `LocalPolicyCommands.cs`, and
`ActivityCommands.cs`; the only variation was `LocalPolicyCommands`' cosmetic `foreach` vs indexed `for`
in `Crc32`). They now share one module,
`src/SBPR.Niflheim.HomesteadStones/Application/Commands/CommandJournalFraming.cs`.

**Why, and what was priced against it.** The duplication is a LOCALITY failure, not an aesthetic one: it
is the mechanism by which a fix lands on one sibling and misses the rest. ADO #127 is the proof — the
delimiter fix landed in `RelationshipCommands` and left latent total data loss in six handlers — and
#125/#126 are the same shape one level up. The counter-argument is real and was weighed: seven
independent recovery paths mean a defect in one cannot corrupt another, and shared code converts "one
feature loses data" into "all progression loses data". That risk is bounded here by what was deliberately
NOT shared:

- **Each handler still owns its own journal FILE.** Shared code, INDEPENDENT durable state. Corrupting
  one handler's journal cannot affect another's rehydration; `AT-TORN-FRAME-ALL-SEVEN` continues to
  assert this per handler. There is no shared-file or shared-stream API and none may be added.
- **The record layout stays in the handler** — field set, field count, and record tag (`LOCALPOLICYREC`,
  `FACETREC`, …). Those genuinely differ per handler; the ADO #127 delimiter-safety invariant is enforced
  at that layer via `Encode`. The extracted layer is delimiter-agnostic and moves opaque bytes.
- **Replay/conflict detection, the domain transition, and the authority policy stay in the handler**
  behind their existing interfaces (`IGovernorAuthorityPolicy`, `IBondAuthorityPolicy`,
  `IHomesteadOwnerAuthority`, `IGovernorDevelopmentAuthority`).

**`AT-EXTRACT-BYTE-IDENTICAL`.** The journals ARE the save, so the extraction is not accepted on a green
suite — a single changed byte would silently orphan every existing player's progression at the next boot.
`tests/NiflheimCommandJournalFramingOracleTests.cs` holds a frozen, INDEPENDENT transcription of the
pre-extraction format (`LegacyFraming`, captured at `448d081`) and asserts byte-for-byte file equality
against `CommandJournalFraming` over an adversarial corpus: pipes (the #127 shape — a `StoneId` is
`world|zoneX|zoneZ` by construction), empty and whitespace strings, non-ASCII and astral-plane emoji,
embedded newlines and NUL, every code point below the surrogate range, and a payload larger than one disk
page. It further asserts BIDIRECTIONAL compatibility — the extracted reader reads legacy-written bytes
(the upgrade path) and the legacy reader reads extracted-written bytes (the rollback path) — plus
multi-frame append order, torn-tail truncation, and CRC-invalid-frame unreachability. 36 assertions.

`LegacyFraming` must never be refactored to delegate to `CommandJournalFraming`; that would make the
oracle vacuously self-comparing. It is deliberately written in the pre-extraction style.

**Mutation evidence.** Mutating `CommandJournalFraming` and re-running the oracle: swapping the
length/CRC write order → 21/36 RED; truncating the digest to 9 bytes instead of 8 → 10/36 RED; deleting
the CRC verification from the read → 1/36 RED; perturbing the CRC polynomial `0xEDB88320` → 22/36 RED.

**One mutation SURVIVES and is recorded honestly:** changing `fs.Flush(true)` to `fs.Flush()` keeps all
36 GREEN. An in-process unit test cannot observe an OS write barrier — the bytes sit in the page cache
either way, and only real power loss distinguishes them. The fsync is protected by code review and an
in-file comment, NOT by a test. A green oracle run is not evidence that durability survived.

**Result: no production behaviour changed.** 513 lines deleted against 165 added across the six files;
the six handlers' bodies are untouched apart from thin private forwarders, so call sites did not move.
Full suite 1688/1688 PASS (1652 baseline + 36 oracle), both net48 Release builds 0 warnings / 0 errors.

**Not proven:** the oracle is unit-level, over synthetic files. It does not prove any handler is wired
correctly on a live dedicated server, and it does not prove the fsync. Logs green is not playable.

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
- append, for each affected refundable Character-Effect purchase, a cancellation entry naming the purchase it
  reverses — never remove the purchase record, which stays in the durable journal as history;
- return each reversed purchase's AP value in full to that character as ordinary Stone-wide Personal AP; the
  derivation of spendable Personal AP excludes cancelled purchases, so no stored balance and no second ledger
  are introduced. Appending the same cancellation twice refunds exactly once;
- preserve Permanent Effects and Progression Keys with their provenance and no refund;
- vacate the Facet and record all affected character/Stone revisions.

Before the Governor confirms, revocation states how much node development / Bond Power will be lost:
revocation is a two-step act (compute and present the loss, then confirm), not a single button.

**Step one — `PreviewRevocation`.** Same payload, same validation, same authority: an unauthenticated,
non-Governor, out-of-range, stale, protected-Tree, or uncommitted request is refused at the warning rather
than being shown a loss it could never confirm. It returns the Tree Level, cumulative Bond Power, per-node
development progress, and the destroyed node list, plus the per-character Personal-AP refunds the confirm
would issue. It writes nothing — no journal record, no projection, no cancellation — so abandoning it is not
a rollback; there is nothing to roll back. The loss presented is computed by the same function the confirm
step uses over the same state, so the number shown is the number destroyed. A preview also reports the Stone
revision it was computed against; passing that back as `expectedStoneRevision` on confirm fails closed
(`StaleStoneRevision`) if the Stone moved, so a Governor cannot confirm a warning that has gone stale.

**Step two — `RevokeTree`.** Re-validates from current authoritative state; the preview is advisory and is
never accepted as a token of authority. The complete reversal set is decided before any of it is written and
is named in the one committed record, so a physically multi-append fan-out is externally one convergent
operation: replay after a crash re-derives exactly that set, and cancellation is idempotent by construction
(reversals collect into a set, never a sum), so a refund converges to exactly once.

A large fan-out may use a journaled multi-phase physical implementation, but its externally visible outcome
must be one convergent operation. Partial revocation is never exposed as success.

## Personal progression commands

### `PurchaseNode`

**Payload:** `treeId/version`, `nodeId/version`, expected `OfferedSetId/version`, payment source preference
(`PersonalAP` — the only fundable source; the retired `FacetCredit` value is rejected `PaymentSourceRetired`).

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
- Personal AP, Cumulative AP, and personal BP for the caller;
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
  slots and normal food debit remain.
- `MenuCraftDurationProvider`: Swift Preparation supplies factor 1/3 after vanilla Cooking-skill adjustment for
  eligible menu-crafted food only. **Implemented (T019, `Adapters/Cooking/MenuCraftDurationProvider.cs`):** a
  pure `ResolveDuration(...)` reads the T004 `DerivedActivationView` active bit for the personal
  `SwiftPreparation@1` Character-Effect node (purchase record AND active relationship; no second ledger) and,
  while active, returns the supplied vanilla skill-adjusted duration multiplied by 1/3. The factor is applied
  strictly AFTER the vanilla Cooking-skill adjustment (the input IS the post-skill value) and ONLY to an
  eligible menu-crafted food — `ClassifyCraft` requires output-is-food AND the active station's crafting skill
  == Cooking (`Skills.SkillType.Cooking`) AND the menu-craft path; non-food, non-Cooking-station, and non-menu
  crafts keep the full vanilla duration. It never completes a craft (a positive base stays strictly positive and
  strictly shorter) and never fabricates one (a non-positive base returns unchanged), and it mutates no recipe,
  item, station, or shared prefab. **Live-wired (T019, net48):** `Features/Cooking/SwiftPreparationCraftTimer`
  transpiles `InventoryGui.UpdateRecipe`, scaling the `num5` menu-craft-duration local in place at the
  `GuiBar.SetMaxValue` call site — both the progress-bar max and the completion comparison read that same local,
  so the whole craft shortens by exactly the provider's factor. It reads the authoritative host projection
  (composed `LocalProgressionObserver.Server` stores) and fails closed off-host / outside every Stone Area /
  without an active purchase, exactly like the sibling Field Prep / Iron Stomach seams; a personal-effect client
  delivery channel is a follow-up.

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
  non-stackable durable output. **Implemented (T022, `Adapters/Crafting/WorkmanshipIssuanceProvider.cs` +
  `Domain/CharacterProgression/ItemProvenance.cs`):** Masterwork is a personal Character Effect, so its
  active/dormant status derives through the shipped T004 `DerivedActivationView` (purchase record for
  `Masterwork@1` at this Stone AND an active relationship; no second ledger). While active, the provider's
  `Decide(...)` issues ONE deterministic visible Workmanship Property (`Workmanship=Masterwork`; no RNG, no
  tier catalog — the final catalog is deferred) on an eligible output (`WorkmanshipCodec.IsEligible`:
  non-stackable AND durable), returning stable outcomes `Issue`/`EffectNotActive`/`IneligibleItem`/
  `AlreadyStamped` (idempotent — never overwrites an existing valid stamp). The engine-free `WorkmanshipCodec`
  stamps/reads/validates the property onto an item's `m_customData` behind an abstract metadata surface and
  protects it with a server-held HMAC-SHA-256 integrity token over the canonical, length-framed IMMUTABLE
  fields ONLY (schema, issuing node, `ItemProvenanceId`, crafter, item type, property). Because the token
  excludes mutable per-instance facts (quality/durability/stack), a stamp that is carried onto the upgraded/
  transferred instance keeps validating; a hand-edited/forged/foreign-key/partial/unknown-schema/lifted-and-
  pasted stamp reads `Tampered` and degrades to vanilla. **Upgrade carry-forward is EXPLICIT, not incidental
  (T022 remediation, t_8311fdd3):** vanilla `InventoryGui.DoCrafting`'s upgrade branch REMOVES the exact source
  instance and `AddItem`-creates a FRESH prefab-backed replacement with an EMPTY `m_customData` — so a stamp is
  NOT preserved for free. `Features/Crafting/MasterworkUpgradePreservationObserver` (highest-priority prefix/
  postfix on `DoCrafting`) captures the complete server-signed Workmanship map off the source before vanilla
  removes it (`WorkmanshipCodec.CaptureStamp`) and restores it byte-for-byte onto the fresh replacement at the
  same grid position (`WorkmanshipCodec.RestoreStamp`) — same `prov_id`, token, and property tuple, quality
  still rises, NO re-mint/reissue under a new provenance id; it runs before the issuance/delivery postfixes so
  they observe an already-valid stamp and no-op (no duplicate grant). A non-upgrade craft, a vanilla/unstamped
  source, or an inventory-full/error path carries nothing. **Live-wired (net48):** `Features/Crafting/
  MasterworkIssuanceObserver` postfixes `InventoryGui.DoCrafting` on the authoritative host, resolves the
  crafter's Masterwork activation from the composed server stores, stamps the exact provenance onto the
  just-produced eligible item via `ItemDataMetadataAccessor`, and explicitly dirties persistence
  (`Inventory.Changed()`). The durable server integrity key (`WorkmanshipIntegrityKeyFile`) is armed in the
  runtime bootstrap; issuance fails closed with no key/server. **Dedicated-server joined-client delivery
  (T022 remediation, t_cdc76200):** the host-only observer cannot issue on an isolated dedicated server (the
  headless server has no local crafter and a pure joined crafter is unarmed/keyless), so
  `Features/Crafting/MasterworkDedicatedDeliveryObserver` adds a bounded per-peer ZRpc channel that makes
  issuance authoritative AND client-delivered **without ever shipping the raw integrity key**: a joined crafter
  sends server-observed produced-item facts, the server re-derives entitlement from its own stores keyed by the
  requesting peer's BOUND INTERNAL principal — never the payload. **Identity space (T022 dedicated-ISSUE fix,
  t_33cc8c05):** that principal is resolved the SAME way the listen-host issuance seam
  (`MasterworkIssuanceObserver.ResolveHostMasterworkActive`) and the purchase path (`sbpr_master buy` →
  `MasterworkOwnershipProvisioningAdmin`) resolve it — the transport-authenticated peer's durable `s_playerID` is
  rendered to a `ServerCreatorIdentity.CharacterSubject` peer key and looked up in
  `FoundationalProgressionServer.BoundSessions` to get the server-minted internal `(AccountId, CharacterId)`.
  The character/authority stores are keyed by that internal identity, so binding instead to the RAW transport
  facts (`AuthenticatedSenderBinder`: platform/socket host as account, `player:<s_playerID>` as character) would
  query the stores under keys the accepted purchase never wrote and the server would REFUSE to sign for the very
  crafter whose Masterwork purchase it just accepted (the reproduced dedicated-server `AT-MASTERWORK-ISSUE`
  failure). An unbound peer fails closed. The server then mints + SIGNS the stamp through the engine-free
  `Application/Crafting/WorkmanshipDeliveryService`, and the client writes the exact signed bytes via
  `WorkmanshipCodec.WriteSigned` (byte-identical to a host stamp). A joined receiver VALIDATES a stamp it read
  keylessly (`WorkmanshipCodec.TryReadRaw`) by relaying the fields+token for the server to check under its key
  (`WorkmanshipCodec.Validate`), caching the Valid/Tampered verdict (`WorkmanshipVerdictCache`).
  **The verdict cache is keyed by the COMPLETE signed-stamp fingerprint (`WorkmanshipCodec.Fingerprint` — every
  signed field AND value), NOT the provenance id (T022 remediation, t_8311fdd3):** the earlier prov-id-only key
  let a post-validation tamper reuse a stale Valid — after a transferred item validated, changing `prop_value`
  while retaining `prov_id`/token left the cached Valid reusable and the tooltip skipped revalidation. Binding
  the verdict to the fingerprint closes that: the instant any signed field changes the fingerprint changes, the
  cache MISSES, and the presentation seam fails closed and requests a fresh server verdict for the mutated bytes
  (which the server rejects) — it never renders using the stale Valid.
  `Features/Crafting/MasterworkWorkmanshipTooltip` postfixes `ItemDrop.ItemData.GetTooltip` to render the one
  deterministic `Workmanship: Masterwork` line only for a confirmed-valid stamp — validated under the composed
  key on the host, or against the fingerprint-keyed server verdict cache on a pure client — so a forged/foreign/
  unconfirmed/mutated stamp degrades to a plain vanilla tooltip on the joined client. The four ATs (`AT-MASTERWORK-ISSUE`,
  `AT-ITEM-UPGRADE-PRESERVE`, `AT-ITEM-TRANSFER`, `AT-ITEM-TAMPER-DEGRADE`) are therefore reachable on the
  dedicated-server + genuine-joined-client topology, not host-only.
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
| `NotAtStone` | Server-resolved acting-character position is not inside the target Stone's Area (Bond/Attunement formation only) |
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
| `PaymentSourceRetired` | Payment source requested is the retired Facet Credit; only `PersonalAP` funds a purchase |
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

### Masterwork ownership provisioning (T022 remediation R4)

The accepted T022 Masterwork node (`Crafting / Masterwork@1`, a personal `CharacterEffect`) issues a Workmanship
Property only while it is ACTIVE for the crafter — which the shipped gate (`WorkmanshipIssuanceProvider.IsMasterworkActive`
→ `DerivedActivationView`) derives from a personal **purchase record** for Masterwork at the Stone AND an active
relationship. At PR #392 head that active-purchased state was **structurally unreachable** at runtime:
`LocalProvisioningIngress.PurchaseNode` had zero runtime callers and the Local develop seam only develops
Stone-cultivated Local nodes, so no joined principal could ever acquire a Masterwork purchase record and
`IsMasterworkActive` was always false. R4 closes it with the smallest QA-only ownership seam, through the SAME
accepted, receipt-backed handlers — no gameplay shortcut, no progression redesign, production fails closed:

- `LocalNodeProvisioningDriver.ProvisionOffered` develops a personal **Offered** node to completion (so it is
  Offered/purchasable) via the identical accepted commit Tree → credit BP → `ApplyBPToNode` chain the Local path
  uses — the only difference is the authored ownership the driver accepts (`PersonalOffered` vs `StoneCultivated`);
  a wrong-ownership node rejects `NotAnOfferedNode` / `NotALocalNode`.
- `LocalProvisioningIngress.OfferMasterwork` (Governor half) seeds the bare Stone envelope when absent and drives
  `ProvisionOffered` for Masterwork under the caller's active **Bond**; idempotent replay re-develops nothing.
- `LocalProvisioningIngress.BuyMasterwork` (buyer half) routes the Masterwork purchase through the accepted
  `PurchaseCommandHandler`, so the active-**Attunement** authority (Bond alone rejects `RelationshipRequired`),
  the Personal-AP debit (an unfunded buyer rejects `InsufficientPersonalAP`), the not-yet-Offered gate
  (`NodeNotOffered`), and one-purchase idempotency (replay returns the recorded terminal result — a single purchase
  record, a single AP debit) are all a real reachable caller. `OwnMasterwork` composes both halves for a
  two-subject QA subject.
- The net48 seam is `Features/Crafting/MasterworkOwnershipProvisioningAdmin.cs`: a DIRECT per-peer `ZRpc` handler
  (`SBPR_Niflheim_ProvisionMasterworkOwnership`) + the `sbpr_master offer|buy` console command, registered ONLY when
  the server-owned `Crafting.EnableAdminMasterworkOwnershipProvisioning` flag is true (default false) AND the
  transport-authenticated sender is a normalized server ADMIN. Identity is the peer's bound-internal principal
  (never the routed sender / a client claim); the Stone is resolved from the peer's server-owned character ZDO
  position. The two halves are separate because the reservation model allows one character only ONE active
  relationship per Stone (develop needs a Bond, purchase needs an Attunement), so the genuine two-client QA matrix
  runs `offer` as the Governor and `buy` as the attuned buyer. It never mints Attunement or AP — the subject must
  already hold the relationship (via `sbpr_provision`) and earned Personal AP (real Foundational placement). Outside
  that gate the handler is never registered or rejects — production fails closed. No provisional activation, no
  direct purchase/node-state write, no second ledger; Masterwork's exact dedicated-server entitlement and
  key-never-on-wire issuance contracts are unchanged.

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
