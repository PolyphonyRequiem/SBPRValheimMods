---
title: "IAP-012 — Tracer 4 (privacy foundation): export, retention, closure, and artifact catalog evidence"
status: proposed
purpose: Executable evidence for the Niflheim cooperative-pilot privacy foundation over the verified account foundation — player-safe export, configurable positive retention with 14/30-day defaults and re-notice on increases, scoped expiring incident holds, pilot closure timestamp/deadline, and mandatory pre-use artifact cataloging with fail-closed admission on an uncataloged world fixture. Engine-free CLEAN-side core.
---

# IAP-012 — Tracer 4: privacy foundation evidence

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) (AIP-FR-016, AIP-FR-020..025)
**Plan:** [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → "Tracer 4 — Operator/privacy lifecycle"
**Data model:** [`account-identity-pilot-data-model.md`](account-identity-pilot-data-model.md) → Aggregate 5, Retention model
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md) → `ExportPilotAccount`, `SetRetentionHold`, `RunPilotRetentionPurge`, `ClosePilot`, `GetPilotPrivacyInventory`

> **Scope:** This is the IAP-012 privacy/artifact-control vertical slice over the merged Tracer-1
> account foundation (PR #330). It delivers the eight named IAP-012 acceptance IDs listed below. It does
> **not** implement the full delete/purge combined-transaction fence (`AT-AIP-DELETE-PURGE`,
> `AT-AIP-PURGE-FALLBACK-RESET`), the admin inspect/disable surface, or the dedicated-server join proof —
> those remain the rest of Tracer 4 and the final gate. Character bindings (Tracer 2) and receipt scrub
> (Tracer 3) are also not in this slice; the export enumerates the operator-supplied player-visible
> character/gameplay rows, which are already internal-only.

## Source (engine-free CLEAN-side; ships under net48, link-compiled under net8)

| File | Role |
|---|---|
| `Domain/Accounts/PilotAccountIdentifiers.cs` | Adds opaque `PilotId`/`DataArtifactId`/`RetentionHoldId` + mints (128-bit CSPRNG) |
| `Domain/Accounts/PilotRetentionPolicy.cs` | Configurable positive/bounded retention (shipped defaults 14/30), zero/negative rejected, derived purge deadlines, `RetentionPolicyChangeGate` (decrease applies immediately, increase requires new-notice acknowledgement) |
| `Persistence/Accounts/PilotAccountStore.cs` | Extended with pilot-lifecycle / artifact-catalog / retention-hold projections + record kinds (`pilot`, `pilot-status`, `artifact`, `artifact-status`, `hold`, `hold-status`), reusing the same framed CRC journal + boot rehydration; `IsWorldFixtureCataloged` admission gate |
| `Application/Accounts/PilotPrivacyService.cs` | `OpenPilot`/`ClosePilot` (endedAt + derived purgeDueAt, enrollment closes after), `CatalogArtifact`/`RequireCatalogedWorldFixture`/`PurgeArtifact` (evidence-digest required), `SetRetentionHold`/`ReleaseRetentionHold`/`IsScopeHeld` (scoped, reasoned, expiring), `ExportAccount` (player-safe, cataloged with expiry) |

Executable evidence: `tests/NiflheimPilotPrivacyFoundationTests.cs` (10 tests). Full `dotnet test` suite
green (**854/854**), net8 `TreatWarningsAsErrors` clean. The engine-free files import only `System.*`
(+ LINQ), so they ship under net48 exactly like the Tracer-1 slice; the mod project builds under net48
with the Valheim SDK in CI.

## Acceptance coverage

| Acceptance ID | Where proven |
|---|---|
| `AT-AIP-EXPORT-SAFE` | Player export carries internal account/character/gameplay/receipt rows only; mechanical scan proves no raw subject, credential HMAC, key version, unrelated account, or secret/operator-note leaks; export is cataloged with an expiry before success |
| `AT-AIP-RETENTION-CONFIG` | Shipped defaults 14 (security log) / 30 (closed data); shorter configured periods valid; zero/negative rejected at construction (never "forever"); derived deadlines are the configured period past the close timestamp |
| `AT-AIP-RETENTION-INCREASE-RENOTICE` | Lengthening either period requires acknowledgement of a NEW notice version before it controls an existing account; a decrease/equal applies immediately; one-sided increase still requires re-notice |
| `AT-AIP-HOLD-EXPIRY` | A scoped, reasoned, expiring hold suppresses purge until its expiry then resumes automatically; explicit release resumes early; global (`*`/`all`), expiryless, and reasonless holds are rejected |
| `AT-AIP-ARTIFACT-CATALOG` | An uncataloged active world fixture fails admission **closed**; cataloging opens admission; a purged fixture fails closed again; every artifact class (world-save/journal/export/backup/log/quarantine/reset) enters the inventory; purge requires an artifact-specific evidence digest (counts alone refused); catalog is durable across restart |
| `AT-AIP-PILOT-CLOSURE-DEADLINE` | `ClosePilot` stamps `endedAt` + a **derived** `purgeDueAt` (endedAt + recorded closed-data period) and the recorded policy version; enrollment rejects after closure; the deadline survives restart (observable in the catalog, not inferred from files) |
| `AT-AIP-DISCLOSURE-COMPLETE` | Re-asserted at the IAP-012 gate against the shipped disclosure core (missing element fails) |
| `AT-AIP-DATA-INVENTORY-BASIS` | Re-asserted: a human-approved lawful-basis position is required per category and an empty inventory never passes; software selects no basis automatically |

## Honesty notes

- **Logs green ≠ playable.** This slice is engine-free CLEAN-side domain/application/persistence proven by
  `dotnet test`; it is not a joined-client proof. The dedicated-server privacy runbook execution
  (`AT-AIP-OPERATOR-RUNBOOK`) and the live journey remain the final gate.
- **Purge is a cataloged evidence transition, not filesystem cleanup.** `PurgeArtifact` refuses to mark an
  artifact `Purged` without an artifact-specific evidence digest, matching the data-model invariant that
  counts/tombstones do not prove purge. The whole-fixture reset certificate and the combined
  delete/purge fence are later Tracer-4 work, not claimed here.
- **Retention basis is authored, never auto-selected.** `RetentionPolicyChangeGate` decides *whether* a
  change may apply given the player's acknowledgement; it never picks a legal basis or a policy on the
  operator's behalf.

## Fix-forward correction (t_f6c8c748) — semantic authority, idempotency, closure, retention

An independent post-merge review of PR #336 found the privacy foundation structurally green but
semantically incomplete. The correction below preserves the model and closes the gaps without reverting.
The privacy service API changed accordingly (operator-gated, policy-driven, fenced); the updated evidence
lives in `tests/NiflheimPilotPrivacyFoundationTests.cs` and the seven regression classes in
`tests/NiflheimPrivacyRegressionTests.cs`.

| Regression class | Gap closed |
|---|---|
| R1 `ExportOwnershipFiltering` | `ExportAccount` derives characters from `acct.CharacterIds` (authoritative membership), not the caller's rows; a gameplay/receipt row referencing a character the account does not own is rejected (`ForeignCharacterRow`) — no cross-account or fabricated-row leak |
| R2 `FailClosedAdmission` | `IPrivacyAdmissionGate` wired into `LiveSessionAdmission`: a Closing/Purged/expired pilot or an uncataloged/expired/`PurgePending` world fixture rejects admission at stage `Privacy`; nothing binds |
| R3 `OperationIdempotency` | Every durable privacy mutation replays on its operationId (same id returned) and conflicts (`OperationConflict`) when an id is reused for a different mutation kind |
| R4 `CrashRecovery` | A privacy mutation that crashes after the durable Intent but before Commit quarantines on boot replay (no partial projection); a committed mutation survives restart |
| R5 `OperatorAuthority` | A non-admin caller is rejected (`Unauthorized`) with NO durable mutation (open/catalog/export) |
| R6 `RetentionPurgeSemantics` | Purge enforces due-time (before expiry → `ArtifactNotDue`), active scope holds (`ScopeHeld`), artifact-specific evidence, and double-purge rejection (`ArtifactAlreadyPurged`); a forced incident purge bypasses due-time but still needs evidence |
| R7 `PurgeCensusIdentity` | Account-scoped export artifacts record the account id, key version, and a stable receipt id; purge records the artifact selector + key version + purge receipt — all durable across restart, sufficient for a provable account-scoped purge/key census |

Verification: `dotnet test` full suite **1100/1100** green (0 warnings, net8 `TreatWarningsAsErrors`);
net48 mod build clean (0 warnings, 0 errors) against local Valheim managed assemblies; `scripts/docs-lint.py`
OK. No SpecCheck recipe-manifest impact (account-identity, not content). This card gates IAP-013, which may
not proceed until this merges and passes independent review.
