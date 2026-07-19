---
title: "Niflheim cooperative-pilot account identity — contracts"
status: proposed
purpose: Define provider-adapter, account/character admission, session, operator, privacy, receipt, read, and rejection contracts for the closed pilot.
---

# Niflheim cooperative-pilot account identity — contracts

**Feature spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md)
**Logical state:** [`account-identity-pilot-data-model.md`](account-identity-pilot-data-model.md)

## Contract principles

- Provider adapters authenticate transient credentials; the account application owns durable identity.
- Payload identity is compared or ignored, never authority.
- Authenticated raw provider/profile subjects terminate at the identity adapter; the only other raw-provider boundary is protected allowlist provisioning. Neither enters gameplay commands, Niflheim durable receipts/subsystem logs, or exports. Upstream vanilla logs/world facts are inventoried separately.
- Every accepted durable lifecycle mutation is revisioned, idempotent, auditable, and recoverable; process-local admission leases follow their explicit non-durable contract.
- Queries return bounded projections; they never expose HMACs or secrets.
- Account lifecycle operator commands reuse the existing live-server admin gate; only allowlist provision/revoke has the separately scoped local-service-owner bootstrap path.
- Join/account work is event-driven and indexed; no provider network call or account-journal scan occurs during gameplay.
- Names are directional semantics, not mandatory method/RPC names.

## Provider adapter port

```text
IPilotCredentialProvider
  ProviderKey ConfiguredProvider
  TryResolveAuthenticatedPeer(transportPeer) -> VerifiedProviderPrincipal | rejection
```

`VerifiedProviderPrincipal` is transient:

```text
providerKey
rawSubject
transportHandle
observedAt
```

### Provider requirements

- The adapter runs only on the server against the configured transport backend.
- `rawSubject` comes from the authenticated peer/socket/session, never client payload.
- Empty/anonymous/unsupported/ambiguous subjects reject.
- Gate 0 proves reconnect/restart stability and exact provider namespace.
- Gate 0 proves how an operator obtains/provisions the exact HMAC-allowlist subject without relying on ordinary logs or command-line arguments.
- The adapter must not log or serialize `rawSubject`.
- The pilot composes exactly one provider adapter. Dynamic provider registration is absent.

## Profile adapter port

```text
IPilotProfileSubjectSource
  TryResolveAuthenticatedProfile(transportPeer) -> VerifiedProfileSubject | rejection

VerifiedProfileSubject
  playerId              # server-observed nonzero s_playerID; transient
  creatorSubject        # adapter-only canonical creator fact
```

The profile source reads the authenticated peer's server-owned character ZDO. The client may choose a local profile, but it may not supply the admitted account, character, or creator binding.

## Admission orchestration

### `ResolveOrCreatePilotAccount`

**Caller:** dedicated/listen-server connection admission after transport authentication.

**Inputs:** transient verified provider principal, HMAC-only allowlist snapshot, required disclosure version, `AccountOperationId`.

**Validates:**

- provider equals configured Gate-0-proven namespace;
- subject nonempty and canonicalizable;
- an active HMAC allowlist entry exists and records acknowledgement of the required disclosure version;
- HMAC key version is available;
- no conflicting active binding;
- account status permits admission;
- operation replay/conflict state.

**Commits when new:**

- `PilotAccountRecord`;
- `CredentialBindingRecord` under current HMAC key version;
- `AccountCreated` + `CredentialBound` terminal receipt.

If enrollment matched a previous-key allowlist entry, the same account-creation transaction also creates its current-key replacement and supersedes the old entry; no account may be born linked only to a retiring key version.

**Commits when a previous-key account resolves:** one terminal transaction creates the current-key allowlist and credential replacements, updates their linkage/account membership, supersedes both old records, preserves notice acknowledgement, and returns a stable replay result. No half-rekeyed state is acknowledged.

**Returns:**

```text
PilotAccountResolution
  outcome = Resolved | Created | Replayed
  accountId
  accountRevision
  credentialBindingId
  resultCode
  accountReceiptId?
```

The raw subject is discarded after lookup/commit and never returned.

### `ProvisionPilotAllowlistEntry`

**Caller:** either (a) an administrator authenticated by the existing live-server Valheim admin gate over the direct per-peer authority seam, or (b) the server-host service owner through an allowlist-only local bootstrap utility protected by OS account/file ownership. The local utility fails closed when its key/data paths are accessible beyond the service account (for example, broader than `0600` on Linux), and cannot inspect/export/disable/delete/reset accounts, change retention, or invoke gameplay commands. The target subject is data, never actor authority.

**Inputs:** transient provider subject supplied through protected no-echo UI/stdin (never command-line arguments or ordinary chat/log commands), configured provider key, notice version, acknowledgement timestamp, `AccountOperationId`.

**Effect:** compute the current-key HMAC, mint `AllowlistEntryId`, commit the versioned `PilotAllowlistEntry` + receipt, redact raw input/output, and return only `AllowlistEntryId`, revision, result code, and receipt/correlation IDs. This acknowledgement proves notice delivery only; it does not select a lawful basis.

### `RevokePilotAllowlistEntry` / `PurgePilotAllowlistEntry`

**Caller:** same live-admin or scoped local-service-owner authority as provisioning; the local path remains allowlist-only.

**Inputs:** `AllowlistEntryId`, expected revision, operation ID. No raw subject/HMAC selector.

**Effect:** commit revisioned `Revoked` or artifact-backed terminal `Purged` result. Deletion of a linked account uses the combined deletion transaction rather than an independent revoke.

### `RunLookupKeyVersionCensus` / `RetireLookupKeyVersion`

The census returns aggregate counts by key version across allowlist, credential, profile, export, journal-generation, and backup artifacts without HMACs. Retirement/second rotation rejects unless the retiring version count is zero or an explicit affected-account/fixture reset has completed; successful retirement commits a receipt before old operational/backup key material is destroyed.

### `BeginPilotAdmission`

**Caller:** connection admission immediately after account resolution.

**Inputs:** resolved `AccountId`, opaque transport handle, server-minted `SessionId`.

**Effect:** atomically reserve process-local `PendingAdmission` in `AccountAdmissionIndex` before profile lookup or character mutation. Same-session retry returns the same lease; another session rejects as `AccountAlreadyConnected`. Timeout/failure releases only the matching lease. The lease has no journal revision/receipt and is cleared on restart; durable mutation begins only after the winning lease is established.

### `ResolveOrCreatePilotCharacter`

**Caller:** admission orchestration after account resolution and server profile observation.

**Inputs:** `AccountId`, matching pending `SessionId`, transient verified profile subject, `AccountOperationId`.

**Validates:** caller holds the account's matching pending lease, nonzero `s_playerID`, active account, active/previous profile HMAC key availability, replay/conflict, no cross-account record reuse.

**Commits when new:** `PilotCharacterBinding`, account character-membership update, `CharacterCreated` receipt. A previous-key match revises that same binding in place: remove old index key, write current HMAC/version, increment revision, retain `CharacterId`; no superseded character-binding record exists.

**Returns:** internal `CharacterId`, revisions, result code. No raw/profile HMAC returns.

### `ActivatePilotSession`

**Inputs:** resolved account/character, opaque transport handle, matching pending `SessionId`.

**Validates:** account active; matching pending lease still exists; transport still authenticated; character belongs to account.

**Effect:** atomically promotes the matching pending lease to active before gameplay admission.

**Rejections:** `AdmissionLeaseMismatch`, `AccountDisabled`, `CharacterNotOwned`, `TransportLost`.

### `ClosePilotSession`

Removes only a pending/active entry whose account + session ID + transport handle match. A stale disconnect cannot close a newer admission/session.

## Gameplay principal contract

After admission:

```text
PilotSessionPrincipal
  accountId
  characterId
  sessionId
```

Gameplay commands and world adapters receive this internal principal. The provider subject, `ProviderKey`, raw `s_playerID`, and profile HMAC are absent.

### Creator evidence bridge

For a placement or other creator-bearing world fact:

1. read the placed object's server-owned `s_creator`;
2. compare it to the authenticated peer's server-observed `s_playerID` in the existing creator fact space;
3. resolve that verified profile fact through `ProfileLookupIndex`;
4. submit the internal `CharacterId` to the domain command.

No world object may resolve an account directly, and no raw `s_playerID` may become durable `CharacterId`.

### Progression receipt correction

Every existing progression binding/receipt that currently includes `AuthoritativePrincipal.PlatformId` must change to internal values only:

```text
principalBindingDigest = Digest(accountId, characterId)
```

Provider subject and any plain/truncated hash of it are forbidden. This change must preserve same-operation replay results under the new unreleased proof schema; incompatible old fixtures may be explicitly reset.

> **Implemented (IAP-007 Tracer 3, t_c8c96581).** `OperationReceiptStore.SubmitFoundationalAp` now
> computes `Digest(accountId|characterId)` for the durable principal binding; `AuthoritativePrincipal`
> and `AuthenticatedConnection` no longer carry `PlatformId`, and `PrincipalResolver` no longer performs
> any provider lookup (it binds the bound-internal session principal off the connection). The gameplay
> principal handed to commands/adapters is `PilotSessionPrincipal` (accountId/characterId/sessionId).
>
> **Wired live (IAP-007W, t_9b479948).** The bound-internal principal is now actually PUBLISHED into the
> live path: on successful account+character session activation the server binds
> `player:<s_playerID>` → `PilotSessionPrincipal` into `BoundSessionPrincipalIndex`
> (`BoundSessionAdmission` / `LiveSessionAdmission`), and matching close/disconnect removes it
> session-qualified (a stale disconnect cannot evict a newer bind). Both ingress shapes resolve that bound
> principal from the server-observed peer key; an unbound peer credits nothing (fail closed) rather than
> falling back to a provider/platform subject.

## Durable lifecycle mutation envelope

```text
PilotLifecycleCommand
  accountOperationId
  commandType
  targetAccountId?
  targetAllowlistEntryId?
  targetPilotId?
  targetDataArtifactId?
  expectedRevisions[]
  payload
```

Caller context attaches either authenticated live-admin/session authority or the allowlist-only local service-owner authority outside the payload. Payload actor claims never grant authority.

### Common success

```text
PilotAccountResult
  accountOperationId
  accountReceiptId
  outcome = Applied | Replayed | NoOp
  committedRevisions[]
  resultCode
  changedInternalIds[]
  auditCorrelationId
```

### Common rejection

```text
PilotAccountRejection
  accountOperationId
  rejectionCode
  currentRevision?
  retryable
  auditCorrelationId?
```

A rejection changes nothing.

## Operator commands

### `GetPilotAccountSummary`

**Caller:** authenticated server administrator.
**Selector:** internal `AccountId` only. Raw-subject lookup is deliberately absent from ordinary inspect; pre-account work uses the protected allowlist provisioning surface.
**Returns:** `PilotAccountSummary` only. No raw subject, HMAC, secret, token, unrelated account, or private evidence.

### `DisablePilotAccount`

Authenticate through the existing live-server admin gate, acquire the per-account mutation fence, wait for any already-committing transaction, then atomically commit `Active -> Disabled` and close future admission/gameplay commit. Release the fence and server-close the active session; delayed network close cannot reopen durable authority.

### `ExportPilotAccount`

Builds `PlayerSafeAccountExport` from internal account/character/gameplay projections. Before success it catalogs the export as a `PilotDataArtifactRecord` with expiry. Output path/access is operator-controlled and follows the source-data deadline or an earlier expiry.

### `DeletePilotAccount`

Authenticate administrator, acquire the per-account mutation fence, and wait for any already-committing transaction. One terminal transaction commits `DeletionPending`, revokes every linked credential and `AllowlistEntryId`, and closes future admission/gameplay commit. Then server-close the session and schedule cataloged data/export/backup purge by `purgeEligibleAt`. Account-scoped compaction must preserve journal invariants and emit evidence; otherwise invoke explicit whole-pilot reset. A tombstone or opaque ID alone is not reported as purge, and the revoked allowlist prevents immediate account recreation.

### `ResetPilotData`

**Payload:** named internal scope, reason, expected revisions.
**Use:** incompatible unreleased fixtures or explicit pilot reset only.
**Effect:** receipted projection/index reset. It never chooses a source by newest timestamp and never invents ownership. Whole-pilot reset purges cataloged old artifacts, emits a selector-free `PilotPurgeCertificate`, retires/destroys old lookup keys, and generates a fresh active key before enrollment reopens.

### `SetRetentionHold` / `ReleaseRetentionHold`

Administrator-only. Scope and expiry required. Global indefinite holds reject. Release resumes ordinary purge eligibility.

### `RunPilotRetentionPurge`

Processes:

- ordinary security logs older than configured `SecurityLogRetentionDays` (shipped default 14);
- closed account/pilot data older than configured `ClosedDataRetentionDays` (shipped default 30) unless a valid scoped hold exists;
- every due `PilotDataArtifactRecord`, including backups, exports, journals, world fixture, logs, and expiring quarantine records.

Durable proof-class artifacts carry no retention deadline (`expiresAt <= 0`) and are treated as never-expiring: retention purge preserves them (e.g. the `ResetAudit` a scoped reset emits to prove removal). Zero/unset on an artifact's `expiresAt` means "no deadline / never sweep", never "already due" — a proof that a scoped reset happened must outlive the sweep.

Each artifact reaches `Purged` only with a terminal receipt and artifact-specific evidence digest; aggregate counts alone are insufficient. Whole-fixture reset emits a new clean `PilotPurgeCertificate` with artifact IDs/evidence digests but no account/character/provider/profile selectors. Returns counts/evidence IDs by category, not player/provider identifiers.

### `ClosePilot`

Acquire the global pilot mutation fence, reject new enrollment/admission, and wait for already-committing gameplay/account transactions. One terminal transaction commits `PilotLifecycleRecord.Active -> Closing`, `endedAt`, policy version, and derived `purgeDueAt`; all later gameplay commit rejects and remaining sessions are server-closed. The pilot reaches `Purged` only after every due catalog artifact has terminal evidence.

Both retention values must be positive and bounded; zero/unset cannot mean forever. Increasing a value requires a new disclosure/retention-policy version and acknowledgement before the longer period applies to any existing account or additional enrollment. Until then, each account's recorded prior policy remains controlling.

### Breach-response runbook contract

The operator runbook must name the responsible human and provide executable steps to: stop new admission, preserve only scoped incident evidence through an expiring hold, rotate/revoke affected secrets, determine which internal accounts/data categories/recipients were affected, restore or reset safely, record the decision timeline, and escalate notification assessment to human legal judgment. The software does not claim that a breach is reportable or non-reportable automatically.

## Query contracts

### `GetMyPilotIdentity`

Authenticated active session only:

```text
accountId
characterId
accountStatus
credentialClasses[]
characterIds[]
retentionSummary
```

It exposes no credential details beyond provider class/status needed for player understanding.

### `GetPilotPrivacyInventory`

Static/operator-readable contract listing each persisted category, purpose, responsible-human-approved pilot lawful-basis position, retention, access role, recipients, and deletion path. This is the source for the pilot disclosure and verification tests; it is not generated from arbitrary runtime reflection and does not let software select a legal basis automatically.

## Stable rejection vocabulary

| Code | Meaning |
|---|---|
| `UnauthenticatedPeer` | No server-authenticated transport subject |
| `ProviderUnsupported` | Subject belongs to a provider namespace not selected by Gate 0/config |
| `ProviderSubjectInvalid` | Empty/ambiguous/noncanonical provider subject |
| `NotAllowlisted` | Closed-pilot allowlist rejected the subject |
| `LookupKeyUnavailable` | Required HMAC key version missing; fail closed |
| `CredentialConflict` | Active provider/HMAC binding conflicts with requested creation |
| `AccountDisabled` | Account cannot open a session |
| `AccountDeletionPending` | Account is closed to admission/mutation pending purge |
| `AccountDeleted` | Account no longer resolves |
| `ProfileSubjectInvalid` | Server profile fact absent/zero/ambiguous |
| `CharacterNotOwned` | Character does not belong to resolved account |
| `AccountAlreadyConnected` | Account has another pending admission or active session |
| `AdmissionLeaseMismatch` | Caller does not hold the account's current pending admission lease |
| `TransportLost` | Authentication/connection disappeared before reservation |
| `PrincipalMismatch` | Payload claim conflicts with server-resolved internal principal |
| `StaleRevision` | Expected revision no longer current |
| `OperationConflict` | Same operation ID reused with different binding/payload |
| `StoreUnavailable` | Account journal/index not safely available |
| `RetentionHoldInvalid` | Scope/reason/expiry invalid |
| `LookupKeyRetirementBlocked` | Live/artifact census still references the retiring key version |
| `PilotClosed` | Pilot no longer admits enrollment/account creation |
| `QuarantinedState` | Durable ambiguity requires operator decision |

## Logging contract

Allowed ordinary fields:

- timestamp;
- stable result/rejection code;
- configured provider class (not subject);
- internal AccountId/CharacterId after successful resolution where operationally necessary;
- operation/audit correlation ID;
- coarse server/build version.

Forbidden fields:

- raw provider/profile subject;
- HMAC/digest used for provider lookup;
- tokens, secrets, claims, email, Discord ID, avatar, guild list;
- full payload dumps;
- persistent IP history;
- player display name as identity.

All exceptions require a separate consent/incident-evidence path outside this pilot spec.

## Performance and failure contracts

- Boot replay/index construction completes before admission opens, and the active pilot world-save/fixture must resolve to a cataloged `WorldSave` artifact; an uncataloged fixture fails admission closed.
- Account/profile resolution after boot performs bounded dictionary/index lookups, not journal scans.
- No provider HTTP/network call occurs after transport authentication or during gameplay.
- Account mutations may fsync; read/admission lookups do not append "last seen" on every request.
- If the account store or HMAC key is unavailable, admission fails closed without falling back to candidate A.
- Provider outage after an internal session opens does not affect ordinary gameplay.

## Acceptance authority

The complete normative requirement → acceptance-ID mapping is [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md#requirement-to-acceptance-coverage). Dependency/tracer ownership for every mapped ID is [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md#three-week-delivery-shape). This contracts document deliberately does not maintain a second partial alias list.

## Provider boundary for future work

A later credential provider may supply the same transient `VerifiedProviderPrincipal` only after its own spec, threat model, scopes/claims allowlist, and proof. It may not change `AccountId`, `CharacterId`, gameplay receipts, retention defaults, or elevate an association into login/recovery authority implicitly.