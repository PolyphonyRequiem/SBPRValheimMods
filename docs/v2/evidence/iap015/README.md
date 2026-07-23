---
status: current
---

# IAP-015 dedicated joined-client account-identity pilot — evidence (human orientation)

Human-readable orientation for the IAP-015 EXECUTE evidence collected under this folder.
Companion to the [machine index](index.md).

## What IAP-015 is

The IAP-015 journey exercises the dedicated joined-client account-identity pilot end to end
against the accepted operator-command-surface candidate (squash-merge `e1bec2d7`, PR #416):
first authenticated Steam join → opaque account/character mint → reconnect → second sequential
profile → concurrent same-account session rejection → server restart durability → live-admin
inspect/disable → operator-runbook walk. The preflight specification and the journey→acceptance
matrix live in the runbook
[`../../runbooks/account-identity-pilot-dedicated-qa-manifest.md`](../../runbooks/account-identity-pilot-dedicated-qa-manifest.md).

## What is here today

- [`iap015-execute-attempt-20260723.md`](iap015-execute-attempt-20260723.md) — EXECUTE **attempt 8**
  (2026-07-23). The first EXECUTE after PR #416 merged to `main`. It cleared the missing-operator-
  surface wall from attempt 7 and then hit, and empirically documented, a **crossplay/parity
  topology block**: the parity-staged standing `-crossplay` Niflheim is not joinable by the
  direct-Steam modded GUI client (`5003` transport timeout, zero server-side admission activity),
  while the only join-capable topology (an isolated non-crossplay lane) was a throwaway, not the
  parity-staged artifact. That block is a topology decision, not a QA failure.

## How the block was resolved

The topology block is resolved by Option (a): a dedicated, non-crossplay, parity-staged Niflheim
clone stood up and verified by the completed environment card `t_52f12248` (READY_FOR_EXECUTE),
with the six preflight gates settled in the runbook §0.4. The isolated fixture reuses the
UID-preserved writable `niflheim` clone (seed `ForTheWort`) with a pristine pilot store, the
validated valbot QA Steam identity as isolated admin (raw id withheld), and the `ForTheWort_QA` /
`ForTheWort_QA2` local profiles only. Standing Niflheim is never modified.

## What a reviewer should confirm

1. **The attempt-8 record is a topology block, not a self-resolvable QA step** — the `5003`
   timeout is pre-handshake with zero admission activity, and standing Niflheim ran
   `-crossplay -preset hardcore`.
2. **No residue** — attempt 8 left zero server-side mutation (connection died pre-handshake) and
   removed its local `ForTheWort_QA.fch` profile; standing services stayed up with DLL parity
   `e6daaaf71265…`.
3. **The forward path is grounded, not guessed** — the resolution and every gate disposition cite
   the completed env card `t_52f12248` / its `ENVIRONMENT-REPORT.md`, not human input.

## Honesty note

Per AGENTS.md ("logs green ≠ playable"): the attempt-8 document records a **blocked** execution,
not a passing joined-client journey. The harness half of the concurrent-session-reject acceptance
PASSed (exact-binary `qa-split-session-harness`, 18/18) and the six live joined-GUI acceptance
tests are specified but not yet executed against the parity-staged fixture. This folder is the
evidence trail; it does not by itself assert the live journey passed.
