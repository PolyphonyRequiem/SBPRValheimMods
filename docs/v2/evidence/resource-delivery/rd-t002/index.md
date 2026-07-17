---
status: current
---

# RD-T002 (Gate A) — Deterministic time and recoverable fan-out evidence

Baseline: implementation of the Resource Delivery plan's **Gate A** on top of the
merged M0 conformance guard (RD-T001, PR #328, `main` @ `ace8484`). This proves
the two load-bearing engine-free mechanisms — exact offline interval reconciliation
and a relationship/AP/outcome mutation converging across projections — **before**
any item movement or gameplay content. **No Resource Delivery gameplay is enabled**;
these are pure domain/application seams link-compiled into the net8 test project.

Governing artifacts: `docs/v2/planning/homestead-resource-delivery-{spec,plan,data-model,contracts}.md`.

## Named acceptance closed

| id | claim | artifact |
|----|-------|----------|
| AT-RD-001 | Canonical account-pair identity accepts either order, rejects self/unauthenticated pairs, remains world/product-scoped | `ConnectionId.TryCreate` + `ResourceDeliveryGateATests` (AtRd001_*) |
| AT-RD-003 | Boundary-time tests select exactly the six approved maturity multipliers (rational, floored once) | `ConnectionMaturity.ForAccumulatedSeconds` / `MaturityMultiplier` + `AtRd003_*` |
| AT-RD-004 | Preparation replays the exact principal/target-bound durable challenge across restart; fresh-ID confirmation rejects token-bearing principal substitution and lost authority, supports delayed/competing confirmation, freezes confirmation-time age, starts a full 72h grace, and makes consumption+release+receipt atomic across crash/restart | `FinalLinkHandshakeStore` (Prepare/Confirm) + `ResourceDeliveryFinalLinkTests` |
| AT-RD-005 | Solo/stale/grace-only/`0×` accounts contribute zero; both eligible sides contribute once; several links/Governors pick one strongest multiplier | `ContributionRule.Evaluate` + `AtRd005_*` |
| AT-RD-007 | Reconcile-before-mutation preserves expired/renewed intervals; offline and arbitrary online partitions match across multiple cycles, residual progress, same-time ordering, and pending-capacity boundaries | `IntervalReconciler.Reconcile` + `AtRd007_*` |

`AT-RD-022` (full cross-feature process-death/replay convergence) remains **open**
until every later mutation family exists (per plan Gate A exit).

## What the slice establishes

1. **Canonical Connection identity + exact maturity (AT-RD-001/003).**
   `ConnectionId` is the canonical unordered `(WorldId, ProductScope, lowAccount,
   highAccount)`; either argument order yields the same identity, self/empty subjects
   reject, and world/product scoping keeps distinct graphs apart. Maturity is a
   rational numerator/denominator band table (`1.1× = 11/10`), never a float, tested
   at every band boundary.

2. **Connection lifecycle (Active/Grace/Reset).** `ConnectionAggregate` freezes age
   on final-source removal, enters a 72h frozen grace, resumes the frozen age on
   reconnect-in-grace, resets to zero on expiry, and never advances age on a
   backward clock. Snapshot codec round-trips every authoritative field.

3. **Complete-contributor iff-rule + strongest-link-once (AT-RD-005).**
   `ContributionRule` contributes iff active Stone relationship **and** a qualifying
   (Active + sourced-at-this-Stone) Connection **and** nonzero participation, and
   selects the single strongest maturity across several links/Governors once. Solo
   Bond, grace-only, wrong-Stone, and `0×` all contribute nothing with a stable
   reason code.

4. **Reconcile-before-mutation integrator (AT-RD-007).** `IntervalReconciler`
   integrates a piecewise-constant rate schedule into the delivery meter with exact
   whole-unit math: one offline jump equals arbitrary online partitions across
   multiple cycles, residual carry, expired→renewed intervals, dormancy, same-time
   zero-length no-ops, and the pending-capacity latch (later time discarded, not
   banked).

5. **Recoverable final-link fan-out (AT-RD-004).** `FinalLinkHandshakeStore` is a
   framed, crc-checked append-only journal (mirroring `OperationReceiptStore`). A
   preparation stores a durable non-mutating decision and replays the exact challenge
   across restart; confirmation is a fresh-ID atomic commit of challenge consumption +
   release + grace + receipt. The full rejection/convergence matrix is proven:
   principal substitution, lost authority, stale set, competing confirmation (one
   winner, loser gets `Consumed` with the winning correlation), delayed confirmation
   (full 72h from confirmation time), op-id reuse conflict, and crash-before-commit
   leaving the challenge unconsumed.

## Verification

- Engine-free seams (`src/SBPR.Niflheim.HomesteadStones/Domain/ResourceDelivery/*`,
  `.../Application/ResourceDelivery/FinalLinkHandshakeStore.cs`) link-compile into the
  net8 test project under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Full suite: **874/874 passed** (819 pre-existing + 55 new Gate-A tests); no existing
  Homestead/Resource-Delivery test changed or regressed. The shipped 20/13/7 roster
  and the RD-T001 guard are untouched — Gate A adds no gameplay.
- `docs-lint`: OK (156 docs). `git diff --check`: clean.
- The net48 mod assembly build requires the Valheim managed assemblies (supplied by
  CI, not present in this worktree); the slice adds no engine surface, so the net8
  clean compile is the applicable local proof.

## Boundary

This closes AT-RD-001/003/004/005/007 at the Gate-A boundary. Tracers 1–5 and Gates
B/C remain unstarted; `AT-RD-022` stays open. No item movement, donation, Stock, node
development, or gameplay is enabled. Merge remains Daniel's; independent verification
(writer ≠ verifier) is required before approval.
