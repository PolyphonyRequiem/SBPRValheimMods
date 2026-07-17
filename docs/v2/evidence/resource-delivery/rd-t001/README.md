---
status: current
---

# RD-T001 (M0) — human orientation

This is the human-readable companion to the [machine manifest](index.md) for the
Resource Delivery M0 current-truth and conformance guard.

## Why this exists

The Resource Delivery package (merged spec PR #327) is a *proposed* superseding
slice. Before anyone writes behavior for it, RD-T001 draws a hard, machine-checked
line between two things that are otherwise easy to confuse:

- **Shipped current truth** — the live Homestead proof: `20` authored nodes =
  `13` executable + `7` unavailable, with Mirrored Stone AP kept as
  receipt-compatible telemetry.
- **The proposed Resource Delivery target** — `21` authored = `14` executable +
  `7` unavailable, reached by appending the Foundational **Humble Homesteader's
  Bundle** (1 BP). This target is authored as data and tagged
  *proposed / not-yet-implemented*; no runtime path treats it as live.

## What a reviewer should confirm

1. The guard adds **no gameplay**. The live roster is still `20/13/7`; a test
   (`AtRd024_GuardEnablesNoBehavior_LiveRosterStaysShipped`) fails if that ever
   stops being true.
2. The first-slice **Mirrored telemetry invariant** (RD-023) is locked: the
   telemetry delta equals the *actual floored* Personal/Cumulative award, flooring
   once after full multiplication, and rejects pre-floor / doubled / spurious
   values.
3. The **same-PR reconciliation obligations** a later behavior slice must honor
   (catalog roster, roster-invariant validator, data-model/spec docs, telemetry,
   tests + joined-client evidence) are enumerated in code, not left to memory.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." This guard is verified by an engine-free
unit suite (819/819 green under warnings-as-errors). It proves the *guard's*
invariants; it does not prove any Resource Delivery gameplay, because none is
implemented yet. The net48 mod assembly build needs Valheim managed assemblies
supplied by CI, not this worktree — the guard adds no engine surface, so the net8
compile is the applicable local proof.
