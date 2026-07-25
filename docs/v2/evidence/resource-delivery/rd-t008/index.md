---
status: current
---

# RD-T008 (Tracer 3) — Donation menu and Stone Stock

Machine manifest for the RD-T008 Tracer-3 implementation: the first real Stone Stock
vertical slice — the authored Level-2 donation candidate pool and owner-role-selected
/ default-materialized Donation Menu, atomic idempotent donation into one durable,
capacity-bounded virtual Stockpile with provenance, and the canonical delegated-
withdrawal permission lifecycle.

Governing artifacts:
`docs/v2/planning/homestead-resource-delivery-{spec,plan,data-model,contracts}.md`
(spec RD-013 / RD-014 / RD-017 / RD-018; data-model Aggregates 3 & 4; contracts
§SelectDonationMenu / §SubmitUpkeepDonation / §Grant/RevokeStockWithdrawalPermission).
Named acceptance: `AT-RD-013`, `AT-RD-014`, `AT-RD-017`, `AT-RD-018`. Built on the
merged Gate B transaction seam (RD-T006) and Tracer 2 (RD-T005).

## What shipped

- `Domain/ResourceDelivery/DonationMenu.cs` — the pure, engine-free authored donation
  content and selection rules. `DonationOption` is a versioned strictly-positive item
  vector (never a client-authored id/quantity/name). `DonationCandidatePool.Level2Humble()`
  is the exact Level-2 pool `20 Wood`, `20 Stone`, `10 Wood + 10 Stone` with the
  authored default pair `20 Wood` + `20 Stone`. `DonationMenuSelection.TrySelect` gates
  owner-role authority, exact level/pool version, two-distinct-current-options, and
  pool membership; `MaterializeDefault` yields the deterministic authored default pair.
- `Domain/ResourceDelivery/StockWithdrawalPermission.cs` — the pure canonical
  `(StoneId, grantee AccountId)` permission value. Generation carries history: first
  grant → gen 1 Active; revoke → Revoked (all current delegated authority removed);
  regrant after revocation → gen+1 Active. A grant against an already-active record —
  including same payload — rejects `AlreadyActive`; one canonical record never forks.
- `Application/ResourceDelivery/StoneStockRegistry.cs` — the durable, event-sourced
  coordinator (framed, CRC-checked, fsync'd, append-only journal keyed by operation id,
  mirroring `AccountStoneParticipationRegistry` / `StockTransactionHarness`). Owns ONE
  virtual Stockpile plus the menu selection and per-grantee permission projections, all
  reconstructed by replaying committed events. `SelectDonationMenu` /
  `MaterializeDefaultIfNeeded` own the menu; `SubmitUpkeepDonation` resolves the exact
  authored vector server-side, debits the server-observed inventory and credits the
  Stockpile with donation provenance exactly once (or changes nothing); `Grant/Revoke
  StockWithdrawalPermission` own the permission lifecycle; `IsWithdrawalAuthorized` is
  the non-transitive Bond-or-active-permission predicate the withdrawal tracer gates on.

## Acceptance coverage (engine-free `xUnit`)

`tests/ResourceDeliveryStoneStockTests.cs` (28 tests):

- **AT-RD-018** — the Level-2 pool is the three exact recipes with the `20 Wood` +
  `20 Stone` default; owner-role selection of two distinct options is stable and locks;
  a selection without owner role / of identical options / of an unknown option / at a
  stale menu revision rejects with no mutation; the authored default materializes once
  idempotently when upkeep is needed before selection; either selected option satisfies
  upkeep.
- **AT-RD-013** — a valid donation reads back from one durable virtual Stockpile;
  restart over the same journal reconstructs the exact Stock / inventory / menu /
  revisions and a committed donation replays without double-transfer; a configured
  capacity override still gates deposits by the same invariant scan.
- **AT-RD-014** — a valid donation transfers exactly once and records donation
  provenance; a replayed op id does not transfer again; an unselected option, missing
  items, full capacity, a stale Stock revision, and a pending-delivery capacity
  reservation each change nothing; clearing the pending delivery lets the same donation
  proceed.
- **AT-RD-017** — owner-role grant creates gen 1 Active; a grant without owner role,
  a duplicate grant against an active record, and a stale-revision grant all reject
  with no mutation; revocation removes all current delegated authority; revoking an
  inactive record rejects; regrant after revocation increments generation; permission
  survives restart with exact generation/state and replays; delegation is non-transitive
  (a grantee cannot grant onward) and another grantee's permission implies no authority;
  a reused grant op id with a different grantee is an `OperationConflict`.

## Honesty note

Per AGENTS.md ("logs green ≠ playable"): verified by the engine-free unit suite
(1154/1154 green under warnings-as-errors: 1126 pre-existing + 28 new) plus a clean
net48 Release build of both `SBPR.Niflheim.HomesteadStones` and the mod assembly. This
proves the domain/application donation-menu, Stockpile, and permission invariants and
their restart/replay recovery; it does not prove joined-client Resource Delivery
gameplay. Generated-delivery deposit and the withdrawal item-move are later tracers;
this slice models the pending-delivery capacity reservation (so donations correctly
reject `PendingDeliveryPriority`) and the non-transitive withdrawal-authority predicate
Tracer 5 will gate on, but performs no generated deposit or withdrawal transfer itself.
