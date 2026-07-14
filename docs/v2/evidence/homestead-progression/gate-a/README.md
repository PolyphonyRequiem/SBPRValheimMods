---
status: current
---

# Gate A — independent verification evidence (T003)

Verifier: `reviewer-adversarial` (non-author of T002/T003 implementation). Verdict recorded by a non-author, per Phase 0 exit criteria.

## Verdict: PASS

Re-verification of the Homestead Foundational-AP slice **after** the Gate-A remediation
(PR #302, merge commit `45a30b41a62f3a4ff18ed9b58ea381834dbf029c` on `main`, fix commit
`d4e1ddc`). All four Gate-A attack classes were reproduced out-of-process against the
remediated shipped code; the two blocking defects from attempt 1 are independently
confirmed fixed, and no regression was found in crash recovery, hostile-principal
rejection, or replay idempotency.

## What changed since the attempt-1 FAIL

Attempt 1 (`aa47f19`) FAILED the merged T002 slice on two defects:

1. **CAS unsound across restart** — revision counters counted only ops seen by the live
   in-memory instance; a fresh process returned revision 0 regardless of durable committed
   ops, so two separate processes could each commit against `expectedStoneRevision:0` and
   both Apply.
2. **Committed AP invisible after restart** — the authoritative read path was an
   un-warmed in-memory cache; a booted server reported Mirrored AP 0 while journal truth
   was non-zero.

The remediation (`OperationReceiptStore` ctor now calls `RehydrateFromJournal()`, replaying
only committed/terminal-bearing operations and re-feeding the Stone/character projection
sinks with set-to-total, idempotent keys) makes the durable journal the single authority
that the projections and CAS revisions are rebuilt from at boot. Receipt records now carry
Base64-encoded identity fields (AccountId/CharacterId/StoneId); legacy 8-field records still
parse and are skipped by rehydration (cannot be keyed) rather than silently mis-projected.

## Method

Out-of-process harness (`repro/`) **link-compiles the remediated slice** from
`45a30b41` (no copy, no fork) into a net8 console app and attacks the dimensions the
in-process suite cannot:

- **Real OS process death:** a child `fsync`s each of the 4 durable boundaries, then
  `SIGKILL`s its own PID (exit 137, no managed unwind). A fresh process recovers from the
  fsync'd journal only.
- **Two-client race across SEPARATE processes:** each client boots a fresh server over a
  shared journal and commits a distinct op against an observed stone revision — the real
  multiplayer server-restart condition.
- **Boot-balance integrity:** a fresh process reads balances/revisions WITHOUT resubmitting,
  compared against journal truth.

## Results

| Attack | Attempt 1 | This re-verify |
|---|---|---|
| Hostile principal (acct/char/unauth) | PASS | PASS |
| Same-op replay / conflicting binding | PASS | PASS |
| Real SIGKILL after boundaries 1–4 + recovery | PASS | PASS — converges to exactly 1/1/1, stable receipt |
| Two-client race, separate processes | **FAIL** | **PASS** — 2nd client at stale rev rejected `StaleStoneRevision` |
| Server-restart balance integrity | **FAIL** | **PASS** — `BOOT_MIRRORED == JOURNAL_TRUTH_MIRRORED == 2`, `BOOT_STONE_REV=2` |

### Key transcripts (full logs in `repro/`)

Race (`repro/transcript-race.md`):
```
Client A commits expecting stoneRev 0 -> op-A OUTCOME=Applied STONE_REV=1
Client B (separate fresh process) expecting stoneRev 0 -> op-B OUTCOME=Rejected CODE=StaleStoneRevision STONE_REV=1
Client B2 (fresh process) refetches stoneRev 1 -> op-B2 OUTCOME=Applied STONE_REV=2
```

Boot-balance (`repro/transcript-boot.md`):
```
BOOT_MIRRORED=2   BOOT_STONE_REV=2   JOURNAL_TRUTH_MIRRORED=2
```

Crash/recovery (`repro/transcript-crash.md`): all four children died by real SIGKILL
(exit 137); every fresh-process recovery converged to mirrored=1 personal=1 cumulative=1
with the one stable receipt `99e8c99e...`; pre-terminal crashes recovered from `Quarantine`,
the post-terminal crash `Replayed`.

## In-process suite (remediated commit `45a30b41`)

- net48/net8 test project: **606/606 pass**, 0 failed.
- `NiflheimProgressionRehydrationTests`: **6/6 pass** (red-first fresh-process/post-restart tests).
- `docs-lint`: OK — 129 docs (pre-scaffold count).

## Acceptance criteria

- [x] Every attack returns the recorded prior result or rejects with zero gameplay mutation.
- [x] No partial Personal/Cumulative/Mirrored AP result, guessed repair, or ambiguous
      acknowledged state — pre-terminal crashes quarantine with no partial mutation.
- [x] Evidence names exact revisions/receipts (commit `45a30b41`, fix `d4e1ddc`, receipt
      digest prefixes only) with no secrets or raw PII.
- [x] PASS recorded by a non-author; does NOT imply gameplay nodes are authorized.

## Residual trust (recorded explicitly, per Daniel's security-posture call)

This proves server-authoritative durability invariants (authenticated principal, revision/CAS,
exactly-once durable progression, permissions) and meaningful anomaly/recovery logging. It does
NOT prove exhaustive anti-cheat, nor that all client-observed gameplay facts are unforgeable.
A first-party server-side-character authority layer remains a separate future platform seam,
not baked into Homestead progression. PASS unblocks T004+; it does not authorize gameplay nodes.

## Provenance

- Under review: `main` @ `45a30b41` (PR #302 merged), fix commit `d4e1ddc`.
- Parent T002 landed via PR #301, merge `a184f93` (independently confirmed present on `main`).
- Harness built against the remediated `SrcRoot`; binary at
  `~/.hermes/kanban/workspaces/t_11ce6067/gatea-harness/bin/Release/net8.0/GateAHarness.dll`.
