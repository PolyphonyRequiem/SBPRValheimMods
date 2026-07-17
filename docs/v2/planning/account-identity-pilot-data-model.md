---
title: "Niflheim cooperative-pilot account identity — data model"
status: proposed
purpose: Define pilot account, credential, character-binding, session, audit, privacy, and recovery state with stable internal identities.
---

# Niflheim cooperative-pilot account identity — data model

**Normative spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md)
**Research:** [`account-identity-pilot-research.md`](account-identity-pilot-research.md)

## Modeling rules

1. Provider subjects authenticate credentials; they never become gameplay identity.
2. `AccountId` and `CharacterId` are opaque server-minted identities.
3. Raw provider/profile subjects are transient boundary facts (identity adapter or protected allowlist provisioning). Niflheim persistent lookup uses versioned keyed HMAC only; upstream vanilla world facts are inventoried separately.
4. Durable allowlist/account/credential/character/lifecycle state has one journal owner and rebuildable projections; the process-local admission index is its own ephemeral authority.
5. Every accepted durable lifecycle mutation is revisioned, idempotent, auditable, and recoverable; admission leases are explicitly ephemeral.
6. Display names, IPs, tokens, profile claims, scene objects, session handles, ZDOIDs, and client payloads are never durable identity.
7. Closed-pilot retention and deletion are state transitions, not best-effort filesystem cleanup.
8. Incompatible unreleased pilot data may be explicitly reset; no silent reinterpretation or production migration exists.

## Identity vocabulary

| Identity | Logical shape | Scope and rule |
|---|---|---|
| `AccountId` | CSPRNG-minted opaque server identifier with ≥128 bits entropy | Account authority/grouping/audit; never provider-derived |
| `CredentialBindingId` | random server identifier | One durable provider credential binding lifecycle |
| `AllowlistEntryId` | random server identifier | Stable operator-safe selector for one pre-account enrollment record |
| `ProviderKey` | configured provider namespace + backend/issuer identity | Distinguishes subjects from different providers/configurations |
| `SubjectLookupHmac` | full HMAC-SHA-256 over canonical `credential-v1 + provider + issuer/backend + subject` bytes | Persistent account lookup; personal/pseudonymous data, not public identity |
| `LookupKeyVersion` | configured HMAC-key identifier | Permits bounded active/previous-key resolution and lazy re-key |
| `CharacterId` | CSPRNG-minted opaque server identifier with ≥128 bits entropy | Owns gameplay progression within one account |
| `ProfileSubjectHmac` | full HMAC-SHA-256 over canonical `profile-v1 + AccountId + server-observed s_playerID` bytes | Finds a pilot character selected by a Valheim profile; domain-separated and never cross-account identity |
| `SessionId` | random process/session identifier | In-memory active connection only; not durable identity |
| `AccountOperationId` | caller/server unique id bound to operation and internal principal | Idempotency key for account lifecycle mutation |
| `AccountReceiptId` | server-issued durable result identity | Audit/replay identity for accepted account mutation |
| `RetentionHoldId` | random server identifier | One scoped, reasoned, expiring retention exception |
| `PilotId` | random server identifier | One bounded pilot lifecycle and closure deadline |
| `DataArtifactId` | random server identifier | One export/backup/journal/log artifact tracked for purge evidence |

## Aggregate 0 — PilotAllowlistEntry

One pre-account enrollment record per approved credential lookup. It is created only after the playtester receives and acknowledges the named pilot disclosure. The acknowledgement is transparency evidence, not an assertion that consent is the legal basis for core authentication.

```text
PilotAllowlistEntry
  schemaVersion
  allowlistEntryId
  providerKey
  subjectLookupHmac
  lookupKeyVersion
  status = Active | Superseded | Revoked | Purged
  revision
  noticeVersion
  noticeAcknowledgedAt
  createdByAuthority = LiveAdminAccount | LocalServiceOwner
  createdByAccountId?      # present for live-admin path only
  createdReceiptId
  supersededById?
  revokedReceiptId?
  purgeEligibleAt?
```

### Invariants

- Raw provider subject is never persisted in the allowlist.
- `(ProviderKey, SubjectLookupHmac)` is unique among active entries.
- Account creation requires an active entry with the current required notice version.
- Revocation prevents first account creation; it does not silently delete an already-created account.
- Allowlist entries follow the same configured closed-data purge and bounded key-version rules as credential bindings.
- Provisioning accepts a raw subject only transiently, computes the HMAC, writes the entry, and redacts command/log output.
- Revoke/re-key/purge address `AllowlistEntryId`, require expected revision + operation ID, and return a durable receipt.
- Re-key creates a current-key entry with a new `AllowlistEntryId`, atomically marks the previous entry `Superseded`, and preserves notice acknowledgement provenance.

## Transient verified principal

`VerifiedProviderPrincipal` exists only during account admission:

```text
VerifiedProviderPrincipal
  providerKey
  rawSubject          # memory only; never serialized/logged
  transportHandle
  observedAt
```

The provider adapter produces it from server-owned transport facts. It is converted immediately to `SubjectLookupHmac` and discarded after account/session resolution.

`VerifiedProfileSubject` likewise carries the server-observed nonzero `s_playerID` only long enough to compute an account-scoped `ProfileSubjectHmac` and verify creator evidence.

## Aggregate 1 — PilotAccountRecord

One authoritative aggregate per `AccountId`.

### State

```text
PilotAccountRecord
  schemaVersion
  accountId
  status = Active | Disabled | DeletionPending | Deleted
  revision
  createdReceiptId
  disabledReceiptId?
  deletionReceiptId?
  deletionRequestedAt?
  purgeReceiptId?
  purgeEligibleAt?
  credentialBindingIds[]
  characterIds[]
  noticeVersion
  noticeAcknowledgedAt
  retentionPolicyVersion
  createdAt
  updatedAt
```

### Invariants

- `AccountId` is minted independently of provider/profile facts.
- At least one active credential exists for an `Active` pilot account.
- `Disabled` admits no new session but retains state for export/operator decision.
- `DeletionPending` admits no session or gameplay mutation.
- `Deleted` has no resolvable credential/profile index and no ordinary player-linked projection.
- Character and credential membership changes only through account lifecycle operations.
- No display name, email, IP, provider subject, token, avatar, Discord field, or HMAC secret is stored here.
- A longer retention policy cannot apply to the account until its corresponding notice version is acknowledged and recorded; the prior policy remains controlling meanwhile.

## Aggregate 2 — CredentialBindingRecord

One authoritative record per `CredentialBindingId`.

### State

```text
CredentialBindingRecord
  schemaVersion
  credentialBindingId
  allowlistEntryId
  accountId
  providerKey
  subjectLookupHmac
  lookupKeyVersion
  status = Active | Revoked | Superseded | Purged
  revision
  createdReceiptId
  lastVerifiedAt?       # coarse timestamp; update may be batched/lazy
  supersededById?
  revokedReceiptId?
  purgeEligibleAt?
```

### Invariants

- Active uniqueness is `(ProviderKey, SubjectLookupHmac)` across the pilot store.
- One active binding resolves exactly one account.
- Raw subject is never present.
- A previous-key binding may be superseded only after the same authenticated subject resolves under an explicitly configured previous key and a new active-key binding commits atomically.
- Revocation removes the credential index before acknowledgement.
- This pilot does not add a second provider credential, merge accounts, or reassign a binding between accounts.

## Aggregate 3 — PilotCharacterBinding

One authoritative record per `CharacterId`.

### State

```text
PilotCharacterBinding
  schemaVersion
  characterId
  accountId
  profileSubjectHmac
  lookupKeyVersion
  status = Active | Tombstoned | Purged
  revision
  createdReceiptId
  tombstoneReceiptId?
  purgeEligibleAt?
```

### Invariants

- Active uniqueness is `(AccountId, ProfileSubjectHmac)`.
- `s_playerID` is not persisted raw and is not `CharacterId`.
- The same numeric/profile-shaped value under another account cannot resolve this character because the HMAC input is account-scoped.
- Display-name change does not change the binding.
- A live placement creator check may compare server-observed `s_playerID` to the authenticated peer, then resolve the verified profile subject to this internal `CharacterId` before domain mutation.
- Gameplay progression continues to key by `(AccountId, CharacterId)`.
- Character transfer and player self-delete are outside this pilot.
- Profile HMAC re-key is an in-place revision of this `CharacterId` record: atomically remove the previous index key, write the current-key HMAC/version, increment revision, and retain the same `CharacterId`; no second/superseded character-binding record exists.

## Derived index 1 — CredentialLookupIndex

Boot-rehydrated map:

```text
(ProviderKey, SubjectLookupHmac) -> CredentialBindingId -> AccountId
```

- Contains only terminal active bindings.
- Built before join admission opens.
- Lookup never scans the journal.
- May hold entries for active and configured previous key versions during rotation.

## Derived index 2 — ProfileLookupIndex

Boot-rehydrated map:

```text
(AccountId, ProfileSubjectHmac) -> CharacterId
```

- Contains only terminal active character bindings.
- Does not expose profile subjects or compare across accounts.

## Ephemeral index — AccountAdmissionIndex

Process-local map:

```text
AccountId -> (SessionId, phase = PendingAdmission | Active, CharacterId?, transportHandle, admittedAt)
```

### Invariants

- At most one pending-or-active entry exists per account.
- A `PendingAdmission` lease is reserved atomically immediately after account resolution and before profile lookup or character minting.
- Only the holder of that exact lease may resolve/mint a character and promote the entry to `Active`.
- Disconnect/timeout removes only the matching session ID.
- Server restart clears the index; durable account/character state remains.
- Leases are intentionally non-durable and carry no journal revision/receipt. Their guarantees are process-local atomic reservation, idempotent same-session retry, matching-session release, timeout, and no durable character mutation by a losing lease.
- No IP/provider subject is stored in the durable account journal through this index.

## Aggregate 4 — PilotAccountJournal

A server-owned append-only journal owning account/credential/character lifecycle truth.

### Record envelope

```text
PilotAccountJournalRecord
  schemaVersion
  recordType
  transactionId
  accountOperationId
  phase = Intent | Committed
  bindingDigest           # internal IDs + operation payload digest only
  payloadDigest
  changes[]               # complete logical allowlist/account/credential/character/lifecycle deltas
  accountId?
  allowlistEntryId?
  credentialBindingId?
  characterId?
  pilotId?
  dataArtifactId?
  expectedRevisions[]
  committedRevisions[]
  resultCode
  occurredAt
  operatorAccountId?
  retentionHoldId?
```

### Invariants

- Records use length + CRC framing and fsync at durable mutation boundaries.
- Same operation ID + same internal binding/payload returns the recorded result.
- Conflicting reuse rejects without mutation.
- Only terminal `Committed` records project into allowlist/account/credential/character/lifecycle indexes.
- One terminal record carries the complete logical `changes[]` set for account+credential creation, character+membership creation, re-key, deletion fence, closure, and purge. Projections apply all deltas or none; no independently durable partial projection is acknowledged.
- Torn/incomplete tails quarantine; repair never invents identity or state.
- Binding/payload digests contain no raw provider/profile subject and no unkeyed hash derived from one.
- Boot replay completes before account admission.

## Aggregate 5 — PilotLifecycleAndArtifactCatalog

This small durable catalog makes closure and purge deadlines observable instead of inferring them from files.

```text
PilotLifecycleRecord
  schemaVersion
  pilotId
  status = Active | Closing | Purged
  revision
  startedAt
  endedAt?
  purgeDueAt?
  closureReceiptId?
  purgeReceiptId?

PilotDataArtifactRecord
  schemaVersion
  dataArtifactId
  artifactType = AccountJournal | GameplayJournal | WorldSave | SecurityLog | Export | Backup | QuarantineReport | ResetAudit
  accountIds[]?          # internal selectors; removed when the scoped purge completes
  keyVersions[]
  storageLocator         # operator-only logical locator, never exported to players
  createdAt
  expiresAt
  status = Active | PurgePending | Purged
  revision
  createdReceiptId
  purgedReceiptId?
  purgeEvidenceDigest?

PilotPurgeCertificate
  schemaVersion
  pilotId
  purgeReceiptId
  purgedArtifactIds[]
  completedAt
  evidenceDigests[]
```

### Invariants

- Every pilot world-save/fixture generation, export, backup set, journal generation, security-log generation, quarantine report, and reset audit that can contain pilot-linked data is cataloged before the artifact is used or success is acknowledged. Admission fails closed if the active world fixture is not cataloged.
- `endedAt` is set only by a revisioned `ClosePilot` operation; `purgeDueAt` is derived from that timestamp and the pilot's recorded policy version.
- Purge success requires a terminal receipt plus artifact-specific evidence (absence after compaction/deletion, destroyed backup generation, or whole-fixture reset); counts alone do not prove purge.
- Immutable/unerasable backup media is not eligible for this pilot unless its lifecycle can meet the configured deadline.
- Catalog entries remove account selectors when purge completes and themselves expire under the minimal audit schedule.
- Whole-fixture reset writes a new clean `PilotPurgeCertificate` containing no `AccountId`, `CharacterId`, HMAC, provider/profile subject, or storage content; it preserves bounded proof after the old catalog/journals are destroyed.

## Audit and privacy views

### PilotAccountSummary

Operator view:

```text
accountId
status
revision
credentialClasses[]       # provider namespace + status; no subject/HMAC
characterIds[]
activeSession?             # yes/no + CharacterId only
createdAt
updatedAt
retentionState
quarantineNotices[]
```

### PlayerSafeAccountExport

```text
accountId
accountStatus
credentialClasses[]
characterIds[]
playerVisibleGameplayState[]
playerVisibleReceipts[]
retentionSchedule
```

Excluded: raw/HMAC subjects, key versions/secrets, tokens, IPs, other accounts, private operator notes, internal security heuristics.

### SecurityLogEvent

```text
timestamp
resultCode
providerKey?              # configured class only
accountId?                # only after successful resolution
characterId?
operationCorrelationId?
```

No raw subject, HMAC, token, display name, payload, or IP history is required by this contract.

## Retention model

### Ordinary periods

| Data | Ordinary retention |
|---|---|
| Authentication/security logs | configured `SecurityLogRetentionDays`; shipped pilot default 14 days |
| Active account/credential/character/gameplay state | while active pilot participation requires it |
| Closed account or ended-pilot linked data | configured `ClosedDataRetentionDays`; shipped pilot default 30 days; verified purge, with whole-fixture reset fallback |
| Eligible backups | same deadline as source data; purge must be verifiable |
| Incident hold | only explicit scoped records until stated expiry |

### RetentionHold

```text
RetentionHold
  schemaVersion
  retentionHoldId
  scope = AccountId | receipt correlation | DataArtifactId
  reason
  createdByAccountId
  revision
  createdAt
  expiresAt
  createdReceiptId
  releasedAt?
  releasedReceiptId?
```

- Holds are administrator-only, auditable, and expiring.
- A hold cannot target every account by default or omit an expiry.
- Release/purge resumes the ordinary retention transition.
- Retention values must be positive/bounded; zero never means forever.
- Increasing either ordinary retention value requires a new disclosure/retention-policy version and acknowledgement before it applies to existing accounts or new enrollment; decreasing a value may apply immediately.

## State transitions

### Provision/revoke/re-key allowlist entry

1. Either an authenticated live-server administrator submits the target provider subject as non-authoritative data through protected UI, or the server service owner invokes the allowlist-only local bootstrap utility through no-echo stdin. The local path has no account lifecycle/read capability.
2. `ProvisionPilotAllowlistEntry` computes HMAC, mints `AllowlistEntryId`, and commits the versioned entry/receipt.
3. Revoke addresses `AllowlistEntryId` + expected revision and commits `Revoked`; if the entry is linked to an account credential, ordinary revoke alone does not delete that account.
4. Re-key creates the current-version entry, updates any linked credential's `allowlistEntryId`, and marks the prior entry `Superseded` in one terminal transaction.
5. Purge removes the entry from projections/artifacts and records terminal purge evidence.

### Rotate/retire lookup key

1. Generate/configure one new active key; the former active key becomes the sole previous key.
2. Lazy login/profile use re-keys credential/allowlist records and revises profile bindings in place.
3. `RunLookupKeyVersionCensus` reports live allowlist, credential, profile, export, journal-generation, and backup artifact counts by key version without exposing HMACs.
4. A second rotation or previous-key retirement rejects while any live/artifact count remains. The operator either waits for bounded artifacts to expire, drives explicit reauthentication, or resets affected accounts/the disposable fixture.
5. `RetireLookupKeyVersion` commits only after a zero census, then destroys the retired operational/backup key material and records a receipt.

### Resolve or create pilot account

1. Provider adapter yields transient `VerifiedProviderPrincipal`.
2. Compute current-key and configured previous-key credential HMACs.
3. Validate one active allowlist entry with the required disclosure version/acknowledgement.
4. If active binding exists, load its account.
5. If no binding exists, mint `AccountId` + `CredentialBindingId`, copy the allowlist notice acknowledgement/retention-policy version into the account, and commit together. If the matched allowlist is previous-key, that same terminal transaction also creates its current-key replacement and supersedes the old entry.
6. If previous-key allowlist/credential records exist for the resolved account, one terminal transaction creates current-key replacements, updates account membership/linkage, supersedes both old records, and preserves notice acknowledgement. No half-rekeyed state is acknowledged.
7. Reject disabled/deletion-pending/deleted accounts.

### Begin account admission

1. After account resolution, mint `SessionId` and atomically reserve `PendingAdmission` for `AccountId`.
2. If any pending/active entry exists, reject before profile lookup or character mutation.
3. A failed/expired admission releases only its matching session lease.

### Resolve or create pilot character

1. Require the caller to hold the account's matching `PendingAdmission` lease, then read nonzero server-observed `s_playerID` from authenticated peer facts.
2. Compute account-scoped `ProfileSubjectHmac` under the active key and, when configured, the previous key.
3. Resolve existing character or mint one `CharacterId` + binding atomically; a previous-key match revises that same character-binding record in place under the active key without changing `CharacterId`.
4. Promote the matching lease to `Active` with `CharacterId`.
5. Return internal principal to gameplay composition; discard transient subjects.

### Disable account

1. Authenticate administrator through the existing live-server admin gate.
2. Acquire the per-account mutation fence; wait for any already-committing gameplay/account transaction to finish.
3. In one terminal transaction, commit `Disabled` under expected revision and close admission to new gameplay commits.
4. Release the fence and server-close the matching active session. The durable disabled state remains authoritative even if network close notification is delayed.
5. Preserve export/quarantine state until deletion/reset/retention decision.

### Delete/purge account

1. Authenticate administrator, acquire the per-account mutation fence, and wait for any already-committing transaction.
2. In one terminal transaction commit `DeletionPending`, revoke every linked credential and its `AllowlistEntryId`, and close admission/gameplay commit for the account.
3. Release the fence, server-close the active session, and produce any requested player-safe export before purge.
4. At or before `purgeEligibleAt`, compact/remove account-linked credential, character, account, gameplay, export, and audit records from cataloged disposable pilot artifacts. A tombstone or opaque internal ID alone does not count as purge.
5. If account-scoped removal cannot preserve/prove journal invariants, reset the whole disposable pilot fixture under the explicit reset contract.
6. Purge eligible backups, commit a terminal purge receipt/evidence, and transition the account through `Deleted` before removing its ordinary projection. The revoked allowlist prevents immediate re-creation unless an administrator explicitly re-enrolls the tester.

### Close pilot and purge artifacts

1. `ClosePilot` acquires a global pilot mutation fence, rejects new enrollment/admission, and waits for already-committing gameplay/account transactions.
2. One terminal transaction commits `Active -> Closing`, `endedAt`, policy version, and `purgeDueAt`, after which all gameplay commit rejects; then the server closes remaining sessions.
3. `RunPilotRetentionPurge` evaluates every due catalog artifact and valid hold, performs account-scoped purge or whole-fixture reset, and records evidence per artifact.
4. Only when every due artifact is `Purged` may a terminal receipt commit `PilotLifecycleRecord.status = Purged`.

### Explicit pilot reset

1. Administrator names internal account/character/pilot scope and reason.
2. Validate no active mutation/session remains.
3. Commit reset receipt.
4. Remove/reset projections and indexes; never choose state based on file recency.
5. For whole-pilot reset, purge cataloged old artifacts, emit the selector-free `PilotPurgeCertificate`, destroy/retire old lookup keys, and generate a fresh active key before any new enrollment.
6. Preserve only the minimal reset audit until retention expiry.

## Migration and recovery

- There is no legacy production migration. Existing candidate-A proof data is incompatible disposable pilot/test data unless an implementation task explicitly supplies a one-time reset/import drill.
- Account recovery is journal replay + terminal-record projection + quarantine.
- Lost HMAC keys do not trigger guessing or name matching. The operator discloses/reset behavior.
- No character/account transfer or merge exists.
- Future provider support must add credentials to the internal account model without changing existing `AccountId`/`CharacterId` or gameplay ownership.