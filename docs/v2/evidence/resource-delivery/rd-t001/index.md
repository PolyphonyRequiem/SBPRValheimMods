---
status: current
---

# RD-T001 (M0) — Resource Delivery current-truth and conformance guard evidence

Baseline: implementation-side guard established on top of the merged Resource
Delivery specification package (PR #327, `main` @ `bacdc09`). This records what
the guard mechanically enforces at the M0 boundary. **No Resource Delivery
behavior is enabled by this guard** — it makes the proposed shapes distinguishable
from shipped truth and locks the first-slice telemetry invariant before behavior
work begins.

Governing artifacts: `docs/v2/planning/homestead-resource-delivery-{spec,plan,
data-model,contracts}.md`.

## Named acceptance closed (implementation baseline)

| id | claim | artifact |
|----|-------|----------|
| AT-RD-023 | First-slice Mirrored Stone AP telemetry equals the actual floored Personal/Cumulative award; floor happens once after full multiplication; equal under replay; Resource Delivery never reads/debits it | `ResourceDeliveryConformanceGuard.FlooredPersonalApAward` / `MirroredDeltaEqualsFlooredAward` + `ResourceDeliveryConformanceGuardTests` (AtRd023_*) |
| AT-RD-024 | Proposed `21 = 14 executable + 7 unavailable` roster is mechanically distinguishable from the shipped `20 = 13 + 7` truth; the proposed shape stays `ProposedNotYetImplemented`; the later same-PR reconciliation obligations are enumerated; the guard enables no behavior (live roster unchanged) | `ResourceDeliveryConformanceGuard` (`ShippedRoster`, `ProposedResourceDeliveryRoster`, `AssertProposedSupersessionShape`, `AssertShippedRosterUnchanged`, `ReconciliationObligations`) + `ResourceDeliveryConformanceGuardTests` (AtRd024_*) |

## What the guard locks

1. **Current truth vs proposed are non-confusable.** The shipped roster is
   *projected* from the live catalog constants (so it cannot silently drift from
   them); the proposed `21/14/7` target is authored as independent literals and
   tagged `ProposedNotYetImplemented`. `AssertProposedSupersessionShape` proves the
   proposed target adds exactly one authored + one executable node (the Foundational
   Humble Homesteader's Bundle, 1 BP) and holds unavailable at 7.

2. **No behavior leaked.** `AssertShippedRosterUnchanged` runs the live
   `ContentRegistryValidator.AssertRosterInvariant` and asserts the live roster is
   still exactly the shipped `20 = 13 + 7` proof (12/1 executable Level partition).
   If a future edit wired the Humble node into the live roster "just to compile,"
   this fails — RD-T001 is a guard only.

3. **Mirrored telemetry invariant.** `FlooredPersonalApAward` computes
   `floor(baseAp × participation × maturity)` with exact integer/rational math (the
   maturity band is a numerator/denominator pair, e.g. `1.1× = 11/10`), flooring
   once. `MirroredDeltaEqualsFlooredAward` proves a correct mirror equals the award
   and rejects pre-floor, doubled, and spurious-nonzero telemetry — the RD-023
   contract that the first behavior slice must preserve.

4. **Reconciliation obligations enumerated.** `ReconciliationObligations` names
   every later same-PR surface (catalog roster, roster-invariant validator,
   data-model/spec docs, Mirrored telemetry, tests + joined-client evidence) so a
   behavior slice cannot move one and forget the others (AT-RD-024 / spec RD-024).

## Verification

- Engine-free guard (`src/SBPR.Niflheim.HomesteadStones/Domain/Content/ResourceDeliveryConformanceGuard.cs`)
  link-compiles into the net8 test project under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Full suite: **819/819 passed** (798 pre-existing + 21 new RD-T001 tests); no
  existing Homestead test changed or regressed.
- The net48 mod assembly build requires the Valheim managed assemblies (supplied by
  CI, not present in this worktree); the guard adds no engine surface, so the net8
  clean compile is the applicable local proof.

## Boundary

This closes AT-RD-023 / AT-RD-024 for the **implementation baseline** only. Gate A
and the downstream tracers (RD-T002…) remain unstarted. No gameplay is changed.
