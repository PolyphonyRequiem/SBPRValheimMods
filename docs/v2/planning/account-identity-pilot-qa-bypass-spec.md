---
title: "T022 QA-only ephemeral account bypass — locked spec (isolated HomesteadT009L)"
status: accepted
purpose: >
  Locked specification for the T022 QA-only ephemeral account bypass: the narrowest
  test-only admission adapter that admits configured server-observed Steam peers into
  Homestead gameplay under EPHEMERAL opaque QA account/character identities on the
  isolated HomesteadT009L fixture — without a PilotAllowlistEntry and without writing
  any durable account/disclosure/credential/character record. TEST INFRASTRUCTURE,
  never production architecture. Default OFF; a conjunction of server-owned gates.
---

# T022 QA-only ephemeral account bypass — locked spec

> **⚠️ TEST INFRASTRUCTURE, NEVER PRODUCTION ARCHITECTURE.** This document specifies a
> QA-only bypass used to run canonical T022 on the isolated HomesteadT009L fixture. It is
> deliberately labelled test infrastructure everywhere it appears in code and docs. The
> production account-admission authority is unchanged: the shipped
> [`PilotAccountService`](account-identity-pilot-contracts.md) first-bind + HMAC-only
> allowlist + disclosure path.

**Scope decision (Daniel).** Supersedes the live-store provisioning gate `t_63e803b9`. Rather
than provisioning the real valbot subject into the pilot account store to run T022, add a QA-only
account bypass. Do NOT provision the account journal and do NOT touch production Niflheim / Heistan.

**Runbook:** [`../runbooks/account-identity-pilot-qa-bypass-runbook.md`](../runbooks/account-identity-pilot-qa-bypass-runbook.md)
**Core (engine-free, unit-tested):** `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/QaAccountBypass.cs`
**Live seam (net48):** `src/SBPR.Niflheim.HomesteadStones/Features/PilotIdentity/PilotSessionLifecycleObserver.cs`
**Tests:** `tests/NiflheimQaAccountBypassTests.cs`

## Goal

When and only when an explicit isolated-t009l QA configuration is active, admit configured
server-observed Steam peers into Homestead gameplay under ephemeral opaque QA account/character
identities without requiring a `PilotAllowlistEntry` or writing any account/disclosure/credential/
character record. Preserve normal account admission unchanged everywhere else.

## Safety boundary (normative)

1. **Default OFF.** Existing behavior and `NotAllowlisted` remain unchanged unless every QA gate
   passes. When the bypass is inactive, the live-admission observer's normal path is byte-for-byte
   the pre-existing one.
2. **Conjunction of explicit gates, not one boolean.** `QaAccountBypassGate.Evaluate` returns
   `None` (activate) only when ALL hold: QA-bypass enabled; exact environment tag
   `homestead-t009l`; exact `ExpectedWorldName == observed world`; exact
   `ExpectedDataRoot == observed durable root`; a non-empty, canonical (decimal), wildcard-free
   configured server-observed SteamID allowlist. Unknown / multiple-form / empty / wildcard ids
   and any production name/root/tag hard-refuse. Rejection reasons are the stable
   `QaBypassGateRejection` vocabulary (`Disabled`, `EnvironmentTagMismatch`, `WorldNameMismatch`,
   `DataRootMismatch`, `ProductionMarker`, `EmptyAllowlist`, `WildcardAllowlist`).
3. **Authority = authenticated server-observed transport principal only.** The Steam subject that
   is matched against the allowlist is the canonical subject resolved by the Gate-0
   `PilotProviderGate` off the authenticated socket host id. Client payload identity is never
   trusted; an unresolved / anonymous / unsupported provider principal refuses.
4. **Ephemeral opaque identities.** `QaEphemeralIdentityMint` issues process-local
   `PilotAccountId` + `PilotCharacterId` from `OpaqueIdMint` (≥128-bit CSPRNG, never derived from
   the Steam subject or s_playerID). Distinct Steam peers → distinct opaque accounts; distinct
   profiles of one peer → distinct opaque characters; a same-session reconnect resolves the same
   opaque ids. The mapping does not survive restart.
5. **No durable mutation, no PII in logs.** The QA admission path does not call
   `PilotAccountService` first-bind, does not create/fabricate a disclosure acknowledgement, and
   does not append an account-journal / credential / character record — it constructs over no
   durable store at all. The only marker is a subject-free `[qa-account-bypass] admitted …`
   carrying opaque ids + a result code; no raw Steam subject or HMAC is emitted.
6. **Session-fence semantics preserved.** One active/pending session per ephemeral account via the
   shipped `AccountAdmissionIndex` (a second concurrent session for the same subject rejects
   `AccountAlreadyConnected`); cleanup on disconnect; a stale disconnect for a superseded session
   cannot close a newer session (session-qualified `BoundSessionPrincipalIndex.TryUnbind`).
7. **Gameplay principal only.** The bypass publishes an ephemeral bound internal principal into the
   `BoundSessionPrincipalIndex`, which grants the Homestead gameplay principal. It does NOT grant
   Valheim admin; the t009l adminlist remains a separate exact-ID operator step.
8. **Rollback.** Disabling any QA gate restores normal `NotAllowlisted` behavior with no durable
   state to clean (all QA state is in-memory, dropped on restart and on ZNet teardown).
9. **Labelled test infrastructure.** Code, config descriptions, and docs label this test
   infrastructure, never production architecture.

## Design

The net48 `PilotSessionLifecycleObserver` composes the QA bypass at `ZNet.Awake` ONLY when the
gate passes against the server-observed isolation facts (the loaded world name + the same durable
directory the Foundational runtime composed). When composed, its `ZDOMan.Update` reconcile loop
routes each connected peer through `QaAccountBypassAdmission.Admit` (authority = the Gate-0 verified
provider principal; allowlist match on the canonical subject) instead of the normal
`LiveSessionAdmission.Admit`, and closes disconnected peers via `QaAccountBypassAdmission.Close`.
When the gate does not pass, `qaBypass` is null and the original live path runs unchanged.

The decision + admission logic lives entirely in the engine-free `QaAccountBypass.cs`
(no UnityEngine / Valheim / BepInEx), so every gate/admission/close branch is unit-tested under
net8 in `tests/NiflheimQaAccountBypassTests.cs` while the mod ships it under net48.

## Verification (all covered by `NiflheimQaAccountBypassTests`)

- disabled/default path refuses (`Disabled`); nothing composes;
- partial gate combinations refuse (tag / world / data-root / empty / wildcard / non-numeric / `0`);
- production names/roots/tags hard-refuse (`ProductionMarker`), including production observed facts;
- unresolved/anonymous/ambiguous transport principal refuses;
- configured primary and valbot server-observed ids produce distinct opaque principals;
- distinct profiles of one subject produce distinct opaque characters; same profile reconnect
  resolves the same opaque ids;
- the QA admission constructs over no durable store — no journal mutation is possible;
- no raw subject in the operator marker or result code;
- one-session fence + stale-disconnect refusal;
- a fresh mint (restart) clears the ephemeral mapping;
- warning-clean net48 build; full `dotnet test` suite green.

## Post-accept operational sequence (out of scope for the implementation card)

After an adversarial review ACCEPT: deploy only to t009l and both QA clients as required, add only
the server-observed valbot SteamID to the t009l adminlist (the separate exact-ID operator step),
then resume `t_00223a5b`. Do not merge / live-provision PR #397 as a T022 prerequisite.
