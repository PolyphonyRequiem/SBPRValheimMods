---
title: "IAP-009 — Operator foundation: bootstrap, inspect, disable, drain evidence"
status: proposed
purpose: Executable evidence for the minimum operator-control foundation over the verified account store — OS-scoped local allowlist bootstrap, authenticated live-admin inspect/disable, per-account mutation fence + drain barrier, deterministic session close/kick, and delete-drain allowlist revocation. Engine-free CLEAN-side core; no second admin identity path is exposed to remote gameplay payloads.
---

# IAP-009 — Operator foundation: bootstrap, inspect, disable, drain evidence

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) (AIP-FR-019, AIP-FR-020; partial AIP-FR-016/024)
**Plan:** [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → "Tracer 4 — Operator/privacy lifecycle" (control subset)
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md) → "Operator commands" (GetPilotAccountSummary, DisablePilotAccount, DeletePilotAccount), "ProvisionPilotAllowlistEntry" (local path), "RevokePilotAllowlistEntry", "ClosePilotSession"

> **Scope:** This is the operator-CONTROL foundation only. It delivers inspect, disable, the per-account
> mutation fence/drain barrier, deterministic session close/kick, and delete-with-allowlist-revocation over
> the Tracer-1 account store. It deliberately does **not** yet deliver the full privacy lifecycle
> (player-safe export, retention purge, backup purge, incident-hold expiry, whole-fixture reset, artifact
> catalog, pilot closure) — those remaining Tracer-4 acceptance IDs land in a later pass. The dedicated
> joined-client operator proof is IAP-010 (`qa-playtest`); independent adversarial verification is IAP-011
> (`reviewer-adversarial`).

## Source (engine-free CLEAN-side; ships under net48, link-compiled under net8)

| File | Role |
|---|---|
| `Application/Accounts/AccountMutationFence.cs` | Per-account fence + operator drain barrier. Gameplay and operator mutations for one account serialize through the same gate; a bounded drain that times out leaves the account untouched (recoverable). Never mutates durable state itself. |
| `Application/Accounts/PilotSessionRegistry.cs` | Process-local one-session-per-account registry. Deterministic operator close (`CloseForAccount` returns the exact transport handle to close) + the stale-disconnect guard (`CloseMatching` only removes an exact account/session/handle match, so a late close cannot tear down a newer admission). Non-durable, cleared on restart (AIP-FR-016). |
| `Application/Accounts/OperatorAdminGate.cs` | Live-admin authority gate. Authorizes ONLY a server-observed authenticated admin host (normalized by the shipped engine-free `VanillaAdminIdentity` against the server's own adminlist). There is no constructor/parameter path that accepts a client-supplied admin claim — no second admin identity path is exposed to remote gameplay payloads (AIP-FR-019). |
| `Application/Accounts/OperatorAccountService.cs` | Authenticated inspect / disable / delete-drain over the account store. Inspect returns a bounded subject-free summary; disable/delete acquire the fence, drain in-flight mutations, atomically commit the lifecycle change, then deterministically server-close the session. Delete additionally revokes every linked credential (and any legacy linked allowlist entry). The still-present revoked credential trips the wound-down re-admission barrier so a same-subject re-join cannot auto-create a fresh account. |
| `Application/Accounts/PilotAccountService.cs` (+`RevokeAllowlistEntry`) | Allowlist-only revoke by opaque id (no raw subject/HMAC selector), used by both the live-admin and local-bootstrap paths. |
| `Features/PilotIdentity/LocalAllowlistBootstrap.cs` | OS-scoped local allowlist bootstrap CORE: owner-only key path (broader than 0600 fails closed), protected no-echo stdin only (argv/env/chat refused before any HMAC), allowlist-provision/revoke ONLY (every account-lifecycle verb rejected out-of-scope), redacted subject-free output. Cannot become a remote/admin backdoor. |

Executable evidence: `tests/NiflheimOperatorControlTests.cs` (7 tests). `dotnet test` green
(851/851 total suite), net8 `TreatWarningsAsErrors` clean; the mod project builds under net48 with the
Valheim SDK, **0 warnings / 0 errors**.

## Acceptance coverage

| Acceptance ID | Where proven |
|---|---|
| `AT-AIP-ADMIN-INSPECT` | admin-gated summary carries internal ids + coarse status + provider CLASS only; a mechanical check proves no raw subject / HMAC appears in any field |
| `AT-AIP-ADMIN-DISABLE` | admin-gated `Active → Disabled`, atomic + durable across reboot; post-disable admission rejects `AccountDisabled`; idempotent replay |
| `AT-AIP-DISABLE-CLOSES-SESSION` | disable server-closes the exact live session (deterministic transport handle) AFTER the durable commit, so a delayed network close cannot reopen authority |
| `AT-AIP-LOCAL-BOOTSTRAP-SCOPE` | owner-only path + no-echo stdin + allowlist-only verb; argv/env/chat channels refused; every account-lifecycle verb rejected out-of-scope; group-readable path fails closed; output redacts the subject |
| `AT-AIP-NONADMIN-REJECT` | non-admin and unauthenticated operator attempts reject with a stable subject-free code and cause NO mutation (proven Active before and after, including a reboot replay) |
| `AT-AIP-MUTATION-FENCE` | a disable draining an in-flight mutation waits for it then commits; a failed drain (bounded timeout, stuck mutation) aborts WITHOUT mutating — the account stays Active and recoverable |
| `AT-AIP-DELETE-DRAIN-BARRIER` | delete drains, commits `DeletionPending` + revokes every linked credential (and any legacy linked allowlist entry) in one terminal transaction, closes the session; a same-subject re-join cannot recreate the account — the still-present revoked credential trips the wound-down re-admission barrier (rejected `AccountDeletionPending`, no auto-create) |

## What is NOT claimed here

- Player-safe export, retention/backup purge, incident-hold expiry, whole-fixture reset, artifact catalog,
  pilot closure, and the breach runbook's remaining automated proofs — later Tracer-4 pass.
- Live joined-client behavior — logs/tests green here does NOT prove a joined client experiences the
  disable/kick. That is IAP-010's job (dedicated QA server).
- Any accepted-Homestead identity-row supersession — unchanged from Tracer 1; lands with the receipt scrub.

## Verification commands

```bash
# engine-free suite (net8, TreatWarningsAsErrors)
dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release      # 851/851

# shipped mod (net48, 0 warnings) — requires the Valheim SDK (scripts/setup.sh)
dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release
```

The operator runbook lives at
[`../runbooks/account-identity-pilot-operator-runbook.md`](../runbooks/account-identity-pilot-operator-runbook.md).
