---
title: "Niflheim account identity pilot — breach-response runbook"
status: proposed
purpose: Rehearsed, executable breach-response runbook for the IAP-013 destructive privacy lifecycle. Names the responsible human and gives concrete operator steps to contain, preserve scoped evidence, rotate secrets, scope impact, restore/reset safely, record the timeline, and escalate legal-notification judgment to a human. The software never claims a breach is automatically reportable.
---

# Breach-response runbook — Niflheim account identity pilot (IAP-013)

**Responsible operator:** the named pilot ops owner (see the privacy inventory `OperatorContact` in
[`../planning/account-identity-pilot-data-model.md`](../planning/account-identity-pilot-data-model.md)).
This human owns the decision timeline and every escalation below. There is no automated actor for these
steps.

**Contracts:** [`../planning/account-identity-pilot-contracts.md`](../planning/account-identity-pilot-contracts.md)
(§Breach-response runbook contract).
**Operator control runbook:** [`account-identity-pilot-operator-runbook.md`](account-identity-pilot-operator-runbook.md).

> **Golden rule (unchanged):** every step runs through a shipped operator command or the OS-scoped
> bootstrap utility. **Never hand-edit the account journal** — it is a framed CRC journal; a manual edit
> corrupts the tail and quarantines records on the next boot.

> **Software boundary (read first):** this pilot's software **does not** decide whether a breach is legally
> reportable or non-reportable. It contains, preserves scoped evidence, and records a timeline. The
> reportability assessment is escalated to human legal judgment — the runbook makes that explicit at
> step 7.

## 0. Declare the incident

- The responsible operator declares a suspected/confirmed breach and starts a written decision timeline
  (append-only notes with UTC timestamps). This timeline is the audit trail for every step below.

## 1. Stop new admission (containment)

- **Stop new admission** immediately so no fresh account/session can bind while the incident is open:
  - `ClosePilot` moves the pilot `Active -> Closing`; after it commits, enrollment and new admission
    reject and remaining sessions are server-closed (contracts §ClosePilot). Use this when the whole
    pilot is implicated.
  - For a single implicated account, `DisablePilotAccount` closes its admission + live session without
    touching the rest of the pilot.
- Verify: a join attempt after this step rejects (`PilotClosed` / `AccountDisabled`).

## 2. Preserve only scoped incident evidence (expiring hold)

- Preserve the minimum needed to investigate — never "hold everything forever":
  - `SetRetentionHold` with a **bounded scope** (a `DataArtifactId` or account/receipt correlation), a
    **reason** (the incident id), and a **strict expiry**. A global/indefinite hold is rejected by
    construction (`RetentionHoldInvalid`).
- A held scope is skipped by `RunPilotRetentionPurge` until the hold expires or is released, so evidence
  is not purged out from under the investigation. When the investigation closes, `ReleaseRetentionHold`
  resumes ordinary purge eligibility.

## 3. Rotate / revoke affected secrets

- **Rotate** the lookup-key epoch if key material may be exposed: a full pilot reset (`ResetPilotData`
  whole-fixture path) retires the current key epoch and opens a fresh active epoch, so old bindings can
  never resolve again (AT-AIP-FULL-RESET-ROTATES-KEY). If only credentials are implicated, delete the
  affected accounts (`DeletePilotAccount` + completion), which revokes each linked credential and
  allowlist entry.
- Key retirement is blocked until a version census proves zero live entries on the retiring version, or an
  explicit affected-account/fixture reset completes (contracts §RunLookupKeyVersionCensus).

## 4. Determine which internal accounts / data categories / recipients were affected

- Use `GetPilotAccountSummary` (internal `AccountId` only) and `RunLookupKeyVersionCensus` to scope
  **affected** internal accounts and data categories **without** exposing raw subjects or HMACs.
- Record the affected internal account ids, data categories, and any known recipients in the decision
  timeline. Recipients are a human-recorded fact, not a software inference.

## 5. Restore or reset safely (no time travel)

- **Restore or reset** only through the durable, receipted paths:
  - Recovery is journal replay + terminal-record projection + quarantine. There is **no point-in-time
    restore** and no silent repair. A `Deleted`/`Quarantined` account never travels back to a live state
    (AT-AIP-NO-TIME-TRAVEL).
  - For durable ambiguity, `Quarantine` marks the account for an explicit operator decision; it admits
    nothing until the operator explicitly deletes or resets it (AT-AIP-QUARANTINE).
  - For incompatible/contaminated fixtures, `ResetPilotData` (scoped or whole-fixture) is the only clean
    path; whole-fixture reset emits a selector-free `PilotPurgeCertificate` as bounded proof.

## 6. Record the decision timeline

- Keep appending to the written timeline started at step 0: every containment, hold, rotation, scope
  finding, and restore/reset decision with its UTC timestamp and the operator who made it. This is the
  durable record of what was decided and when.

## 7. Escalate notification assessment to human legal judgment

- **Legal** notification/reportability is a human decision. The responsible operator escalates the
  assessment (regulator/user notification, timelines, thresholds) to the appropriate human legal owner.
- The software **does not** classify the breach as reportable or non-reportable automatically, and this
  runbook makes no such claim. Attach the decision timeline and the scoped evidence hold to the
  escalation.

## Rehearsal

- This runbook is rehearsed as part of `AIP-SC-007` (each destructive operation has a named automated test
  and operator runbook proof). The named acceptance for the runbook itself is `AT-AIP-BREACH-RUNBOOK`.
