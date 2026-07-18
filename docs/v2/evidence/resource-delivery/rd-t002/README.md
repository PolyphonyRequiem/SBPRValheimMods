---
status: current
---

# RD-T002 (Gate A) — human orientation

This is the human-readable companion to the [machine manifest](index.md) for the
Resource Delivery **Gate A** slice: deterministic time and recoverable fan-out.

## Why this exists

Before any item movement or gameplay content, Gate A proves the two load-bearing
engine-free mechanisms the whole feature rests on:

- **Exact offline interval reconciliation** — one large offline jump produces the
  *terminally identical* result to arbitrary smaller online partitions, across
  multiple delivery cycles, residual carry, expired→renewed intervals, dormancy, and
  the pending-capacity latch.
- **Recoverable relationship/AP/outcome fan-out** — the final-link release handshake
  (prepare → confirm) couples challenge consumption, relationship release, every
  affected Connection's grace transition, and the confirmation receipt under one
  atomic recovery boundary that survives restart and competing confirmations.

Alongside those, Gate A lands canonical world/product-scoped Connection identity,
the exact six-band maturity table, the Connection Active/Grace/Reset lifecycle, and
the complete-contributor iff-rule with strongest-link-once selection.

## What a reviewer should confirm

1. **No gameplay is enabled.** Every file is engine-free and link-compiled into the
   net8 test project; the shipped 20/13/7 roster and the RD-T001 guard are untouched.
   Gate A adds pure domain/application seams, not runtime behavior.
2. **Identity is canonical.** `(A,B)` and `(B,A)` are the same Connection; self-pairs
   and unauthenticated subjects reject; world/product scoping keeps graphs distinct.
3. **Maturity is exact.** Bands are rational numerator/denominator pairs (no float),
   tested at every boundary time; the multiplier floors once in the AP math (RD-009,
   proven at the M0 guard).
4. **The iff-rule holds.** Solo Bond, grace-only Connection, wrong-Stone Connection,
   and `0×` participation all contribute nothing; both eligible sides contribute once;
   several links/Governors pick a single strongest multiplier.
5. **Reconciliation converges.** The offline-jump-equals-online-partitions property is
   asserted directly, including the pending-capacity latch where later wall time is
   discarded rather than banked.
6. **The handshake is recoverable.** Preparation replays the exact challenge across a
   restart; a fresh-ID confirmation applies atomically; principal substitution, lost
   authority, stale set, op-id reuse, and competing confirmations all behave per spec,
   and a crash before commit leaves the challenge unconsumed.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." Gate A is verified by an engine-free unit
suite (874/874 green under warnings-as-errors: 819 pre-existing + 55 new). It proves
the *domain/application* invariants; it does not prove any joined-client Resource
Delivery gameplay, because none is implemented yet (`AT-RD-022` stays open). The net48
mod assembly build needs Valheim managed assemblies supplied by CI, not this worktree —
Gate A adds no engine surface, so the net8 compile is the applicable local proof.
