---
status: current
---

# RD-T005 (Tracer 2) — Weekly/daily participation and AP multiplier

Machine manifest for the RD-T005 Tracer-2 implementation: account–Stone
Participation (rolling seven-day weekly upkeep, rolling 24-hour daily practice,
durable five-distinct-Foundational-placement cycle), the `0×/1×/2×` tiers, the
multiplier-aware Personal AP award, and the combined fifth-placement
AP-before-practice receipt order.

Governing artifacts:
`docs/v2/planning/homestead-resource-delivery-{spec,plan,data-model,contracts}.md`
(spec RD-006 / RD-009 / RD-019; data-model Aggregates 2 & 5; contracts
§SubmitUpkeepDonation / RecordDailyPractice / RecordApActivity). Named acceptance:
`AT-RD-006`, `AT-RD-009`, `AT-RD-019`.

## What shipped

- `Domain/ResourceDelivery/AccountStoneParticipationAggregate.cs` — the pure
  per-`(account, Stone)` aggregate. Owns the rolling seven-day weekly expiry, the
  rolling 24-hour daily window, and the single durable daily cycle
  (`DailyCycleStatus` None→Open→Completed). `ReconcileTo` closes an expired daily
  window under the prior state before any same-time mutation; `RecordWeeklyCompletion`
  refreshes the rolling expiry without stacking; `RecordDailyPractice` counts five
  distinct physical instances, ignores active-window prebuild, and opens a fresh
  zero-progress cycle only after expiry is reconciled. `TierAt` derives `0×/1×/2×`
  (2× requires current weekly upkeep AND a current daily window).
- `Domain/ResourceDelivery/ApMultiplierPolicy.cs` — the exact
  `floor(base × participation × maturity)` award with a SINGLE final floor over exact
  rational maturity; a `0×`/non-contributing account is a recorded no-award. Emits the
  Mirrored telemetry delta equal to the final award. Never widens source authorization.
- `Application/ResourceDelivery/AccountStoneParticipationRegistry.cs` — the durable,
  event-sourced coordinator (framed, CRC-checked, fsync'd journal keyed by operation
  id, mirroring `StoneConnectionSourceRegistry`). `RecordCombinedPlacement` enforces
  the fixed order: snapshot/compute the AP subresult from the PRE-practice tier, then
  apply the placement — so the fifth placement uses the prior `0×/1×` tier and `2×`
  begins only afterward. Committed operations replay their recorded terminal result
  verbatim; a reused id with a different binding is an `OperationConflict`.

## Acceptance coverage (engine-free `xUnit`)

`tests/ResourceDeliveryParticipationTests.cs` (26 tests):

- **AT-RD-006** — no-upkeep `0×`; weekly upkeep `1×` up to the exclusive seven-day
  expiry; weekly + daily `2×` for exactly 24 hours; daily-current-but-weekly-expired
  collapses to `0×` (daily never yields `2×` alone); repeated weekly refreshes the
  expiry without stacking; renewal after expiry preserves the lapse.
- **AT-RD-019** — five distinct placements complete the cycle once; duplicate physical
  instance counts once; active-window evidence does not prebuild the next cycle; a
  fresh zero-progress cycle opens only after expiry reconciliation; server-observed
  evidence is the only path (no client-callable objective completion); restart
  rehydrates exact cycle + weekly state; replay is idempotent; conflicting op id
  rejects; the fifth combined placement uses the prior tier and only later events see
  `2×`.
- **AT-RD-009** — single final floor after full multiplication (distinguished from a
  per-factor floor); Mirrored telemetry equals the final award; `0×` is a recorded
  no-award; missing qualifying Connection awards zero; the multiplier never widens
  source authority (absent relationship → zero); combined-placement replay (live and
  post-restart) returns the recorded ordered result verbatim; weekly replay returns
  the recorded expiry.

## Honesty note

Per AGENTS.md ("logs green ≠ playable"): verified by the engine-free unit suite
(1126/1126 green under warnings-as-errors: 1100 pre-existing + 26 new) plus a clean
net48 build of both `SBPR.Niflheim.HomesteadStones` and the mod assembly. This proves
the domain/application participation + AP-multiplier invariants and their
restart/replay recovery; it does not prove joined-client Resource Delivery gameplay,
which does not exist yet (item movement is gated behind Gate B / Tracer 3). BP is
never multiplied by this slice, and the Mirrored projection remains compatibility
telemetry equal to the floored award.
