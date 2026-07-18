---
status: current
---

# RD-T006 (Gate B) — Server inventory transaction evidence

Baseline: implementation of the Resource Delivery plan's **Gate B — Server inventory
transaction** spike on top of the merged Gate A (RD-T002, PR #331, `main` @ `b908488`).
This proves the load-bearing player-inventory ↔ Stone-Stock transaction seam — exact
authored-vector resolution, full debit/credit fit, stale revisions, replay, disconnect,
and process death converging to **one transfer or no transfer** — **before** the
item-moving Tracer commands exist. **No Resource Delivery gameplay is enabled**; the
harness is a disposable spike substrate link-compiled into the net8 test project.

Governing artifacts: `docs/v2/planning/homestead-resource-delivery-{spec,plan,data-model,contracts}.md`.

## Gate B exit (plan §"Gate B — Server inventory transaction")

> The same operation converges to one transfer or no transfer with no duplicated or
> lost player or Stone items. Tracer 3 and Tracer 5 item-moving commands may not start
> before this exit passes.

Gate B is a **spike gate**, not a named-acceptance closure: it claims no `AT-RD-*`
product acceptance (those are closed by the Tracers that build on this seam). It
proves the mechanism the plan requires before those Tracers may begin.

## What the spike establishes

| requirement | proof |
|---|---|
| Exact debit requires no trusted client quantity | `SubmitDonation` resolves the item vector from the server-authored option id; `Donation_ServerResolvesExactAuthoredVector_ClientSuppliesNoQuantity`, `Donation_UnknownOption_Rejected_NoMutation` |
| Full debit/credit fit requires no trusted client fit claim | insufficient source (`Donation_InsufficientPlayerItems_*`, `Withdrawal_StockLacksFullVector_*`), over-capacity deposit (`Donation_ExceedsStockCapacity_*`), player cannot fit (`Withdrawal_PlayerInventoryCannotFit_*`) — all non-mutating |
| Stale revisions never duplicate or lose resources | `Donation_StaleInventoryRevision_*`, `Donation_StaleStockRevision_*`, `SecondOp_WithCurrentRevisions_Applies` |
| Conflicting replay never duplicates | `Replay_SameOpAndBinding_ReturnsRecordedResult_NoDoubleTransfer`, `Replay_AcrossRestart_*`, `ConflictingBinding_UnderSameOpId_Rejected` |
| Disconnect / process death converge to one transfer or none | `CrashBeforeCommit_LeavesNoTransfer_ResumeCompletesExactlyOnce` (Theory over intent/source/destination boundaries), `CrashAfterCommit_RecoversTransfer_ReplayReturnsWinner`, `Withdrawal_CrashBeforeCommit_*` |
| Success acknowledged only after the terminal operation is recoverable | balances/revisions project from durable, terminal-bearing records only; a partial op projects nothing |
| Conservation | `DonateThenWithdraw_ConservesTotalUnits` |

## Design — reuse of the proven Gate-A mechanism

`StockTransactionHarness` (`src/SBPR.Niflheim.HomesteadStones/Application/ResourceDelivery/`)
is the same framed, crc-checked, append-only journal used by `OperationReceiptStore`
and `FinalLinkHandshakeStore`:

1. **The journal is the transaction.** Both the player inventory and the Stone Stock
   are idempotent projections rebuilt from durable records; only fully-**committed**
   (terminal-bearing) operations contribute, and each boundary's signed delta is
   applied exactly once (deduped by phase).
2. **Both ledgers commit together.** A reader only ever observes balances derived from
   a terminal-bearing record. There is no window where the source ledger moved but the
   destination did not — a crash before the terminal record projects neither delta.
3. **One transfer machine, two directions.** Donation debits player / credits Stock;
   withdrawal is the mirror. Fit is checked against the destination's capacity policy
   (Stock: provisional Level-2 16 kinds / 1,000 units / 500 per item; player: carry).
4. **Process death is real.** An `IStockCrashInjector` throws after a chosen durable
   boundary; recovery constructs a fresh harness over the same journal and the same
   seeded opening balances — exactly the Gate-A restart simulation.

## Verification

- Engine-free seam (`.../Application/ResourceDelivery/StockTransactionHarness.cs`)
  link-compiles into the net8 test project under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Full suite: **1041/1041 passed** (1022 pre-existing + 19 new Gate-B tests); no
  existing Homestead/Resource-Delivery test changed or regressed. The shipped 20/13/7
  roster and the RD-T001 guard are untouched — Gate B adds no gameplay.
- The net48 mod assembly build requires the Valheim managed assemblies (supplied by CI,
  not present in this worktree); the spike adds no engine surface, so the net8 clean
  compile is the applicable local proof.

## Boundary

This satisfies the Gate B exit and unblocks Tracer 3 / Tracer 5 for a later authorized
build. It closes no `AT-RD-*` id and enables no donation, withdrawal, Stock, node
development, or joined-client gameplay. Gate C (capacity, pending priority, dormancy)
remains a separate later spike. Merge remains Daniel's; independent verification
(writer ≠ verifier) is required before approval.
