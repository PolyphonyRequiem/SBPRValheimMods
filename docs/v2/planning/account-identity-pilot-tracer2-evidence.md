---
title: "IAP-005 — Tracer 2: minted characters and single-session admission evidence"
status: proposed
purpose: Executable Tracer-2 evidence for the Niflheim cooperative-pilot account identity — server-minted, account-scoped opaque CharacterId derived from a domain-separated profile HMAC of the server-observed nonzero s_playerID, one pending-or-active admission lease reserved before character mint, race-safe/matching-session-released/stale-disconnect-safe leases, cross-account isolation, previous-key profile re-key in place, and the vanilla creator-evidence bridge to the internal character. Engine-free CLEAN-side core.
---

# IAP-005 — Tracer 2: minted characters and single-session admission evidence

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) (AIP-FR-009..013; AIP-FR-003/004 for characters)
**Plan:** [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → "Tracer 2 — Character selection + one-session admission"
**Data model:** [`account-identity-pilot-data-model.md`](account-identity-pilot-data-model.md) → Aggregate 3 "PilotCharacterBinding", Derived index 2 "ProfileLookupIndex", "Ephemeral index — AccountAdmissionIndex"
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md) → "Profile adapter port", "BeginPilotAdmission", "ResolveOrCreatePilotCharacter", "ActivatePilotSession", "ClosePilotSession", "Creator evidence bridge"

> **Scope:** This is the Tracer-2 character-selection + single-session admission vertical slice on top of
> the merged IAP-003 Tracer-1 account foundation (PR #330). It maps the server-observed nonzero
> `s_playerID` through a domain-separated profile HMAC to a server-minted, account-scoped opaque
> `CharacterId`; reserves exactly one pending/active admission per account BEFORE character mint; and
> bridges vanilla `s_creator` evidence to the internal character. It does **not** migrate gameplay
> receipts or remove `PlatformId` from durable digests — that is Tracer 3, where the accepted Homestead
> identity-row supersession is reconciled and stamped. This tracer is engine-free + net48-compile
> evidence; "logs green ≠ playable" still holds for the end-to-end journey (final gate).

## Source (engine-free CLEAN-side; ships under net48, link-compiled under net8)

| File | Role |
|---|---|
| `Domain/Accounts/PilotAccountIdentifiers.cs` | Adds opaque `PilotCharacterId` + ephemeral `SessionId` value objects and their 128-bit CSPRNG mints (`OpaqueIdMint.NewCharacterId`/`NewSessionId`) |
| `Adapters/Identity/PilotProfileSubject.cs` | Transient `VerifiedProfileSubject` — server-observed nonzero `s_playerID` + transport handle; memory-only, never serialized/logged; zero/negative rejects |
| `Persistence/Accounts/PilotAccountStore.cs` | Adds `PilotCharacterProjection` + account character-membership, the account-scoped `ProfileLookupIndex` (`(AccountId, profileHmac) → CharacterId`, active only), the `char`/`char-status`/`char-rekey`/`acct-add-char` journal deltas, character reindex, and profile-HMAC key-version census |
| `Application/Accounts/AccountAdmissionIndex.cs` | Ephemeral, process-local one-lease-per-account index — atomic reservation, idempotent same-session, matching-session release, stale-disconnect safety, restart-cleared |
| `Application/Accounts/PilotCharacterAdmissionService.cs` | `BeginAdmission` (reserve sole lease before mint), `ResolveOrCreateCharacter` (mint/resolve/lazy previous-key re-key, lease-gated, idempotent), `ActivateSession`, `CloseSession`, and the `ResolveCreatorCharacter` bridge |

Executable evidence: `tests/NiflheimPilotCharacterSessionTests.cs` (16 tests). `dotnet test` green
(859/859 total suite), net8 `TreatWarningsAsErrors` clean; both the mod's `SBPR.Niflheim.HomesteadStones`
project and `SBPR.Trailborne` build under net48 with the Valheim SDK, **0 warnings / 0 errors**.

## Acceptance coverage

| Acceptance ID | Where proven |
|---|---|
| `AT-AIP-PROFILE-MINT` | first profile mints one opaque 128-bit account-scoped `CharacterId` (not `s_playerID`); account membership updated |
| `AT-AIP-CHARACTER-MEMBERSHIP-ATOMIC` | crash after Intent leaves no partial character and no membership entry; reboot quarantines the Intent-only txn; retry mints exactly one |
| `AT-AIP-PROFILE-RECONNECT` | same profile after logout/restart resolves the same character (index rehydrated from journal), no new mint |
| `AT-AIP-PROFILE-RENAME` | reconnect under a different transport handle/session ZDOID (and no name input at all) resolves the same character |
| `AT-AIP-NAME-NONAUTHORITY` | the service consumes only `s_playerID` — there is deliberately no display-name/ZDOID input path, so names cannot be authority (same test) |
| `AT-AIP-CROSS-ACCOUNT-BLOCK` | the same numeric `s_playerID` under another account mints its own distinct character; account-scoped index proves account A cannot resolve B's character |
| `AT-AIP-PROFILE-PREVIOUS-KEY-REKEY` | previous-key profile match resolves and re-keys the SAME `CharacterId` in place under the active key (higher revision, one record); persists across restart |
| `AT-AIP-ONE-SESSION` | a second sibling profile of the same account rejects as `AccountAlreadyConnected` BEFORE any character mint; sequential admission works after close |
| `AT-AIP-ADMISSION-LEASE-RACE` | 32 concurrent reservations for one account → exactly one wins; one live lease |
| `AT-AIP-STALE-DISCONNECT` | a late disconnect carrying an older session/transport cannot close a newer admission's lease; the newer session's own close still works |
| `AT-AIP-CREATOR-BRIDGE` | object whose `s_creator` matches the peer's `s_playerID` resolves to the internal `CharacterId`; a different creator rejects `CreatorMismatch`; no world object resolves an account directly |

Additional guards in the same suite: zero `s_playerID` rejects `ProfileSubjectInvalid` before mint;
mint without the matching pending lease rejects `AdmissionLeaseMismatch`; two sequential distinct sibling
profiles mint two distinct characters (two membership rows); same-operation-id mint replays idempotently;
and a mechanical PII scan proves a distinctive raw `s_playerID` never lands on the account journal (profile
HMAC only).

## Design notes

- **Account-scoped profile identity.** The profile HMAC input is `(profile-v1, AccountId, s_playerID)`
  (the Tracer-1 `LookupKeyRing.ProfileHmacActive`), and the `ProfileLookupIndex` key repeats the
  `AccountId`. Both the hash preimage and the index key are account-scoped, so a foreign account's
  identical `s_playerID` cannot resolve another account's character by construction — `AT-AIP-CROSS-ACCOUNT-BLOCK`
  is a structural property, not a runtime check that could be bypassed.
- **Ordering fence.** `BeginAdmission` reserves the sole `PendingAdmission` lease immediately after
  account resolution and before profile lookup/character mint; `ResolveOrCreateCharacter` refuses to mint
  unless the caller holds the account's matching pending lease. A second connection therefore rejects at
  the lease, never touching durable character state (AIP-FR-013).
- **Leases are deliberately non-durable.** The `AccountAdmissionIndex` writes no journal record, no
  receipt, no revision. Its guarantees are process-local: atomic reservation, idempotent same-session
  retry, matching-`(AccountId, SessionId, transportHandle)` release, and restart-cleared. Durable
  character mutation is the only journaled part.
- **In-place profile re-key.** A previous-key profile match revises the same `PilotCharacterBinding`
  record (`char-rekey` delta: drop old index key, write current HMAC/version, increment revision, retain
  `CharacterId`); no superseded character record is created (data-model.md Aggregate 3 invariant).

## What is deliberately NOT claimed

- No gameplay-receipt migration; `PlatformId` still appears in the existing Homestead receipt path until
  Tracer 3, which is where the accepted Homestead data-model identity rows are reconciled and stamped.
- No operator/privacy lifecycle (disable/export/delete/purge/retention) — that is Tracer 4.
- No live dedicated joined-client proof (final gate). This tracer is engine-free + net48-compile evidence;
  "logs green ≠ playable" still holds for the end-to-end journey.
