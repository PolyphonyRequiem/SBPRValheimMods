---
status: current
---

# RD-T006 (Gate B) — human orientation

This is the human-readable companion to the [machine manifest](index.md) for the
Resource Delivery **Gate B** spike: the server-owned player-inventory ↔ Stone-Stock
transaction seam.

## Why this exists

Gate B is a **stop gate** in the plan: the item-moving Tracers (donation in Tracer 3,
withdrawal in Tracer 5) may not begin until this seam is proven. It answers one
question — can an exact player-inventory debit/credit plus a Stone-Stock mutation
produce **one recoverable terminal result** — before any gameplay depends on it.

Per the plan, this uses a **disposable Stock harness**. It does **not** implement the
Resource Delivery gameplay loop, the donation menu, the reconciler, capacity/pending
priority (that is Gate C), or any joined-client behaviour. It proves the transaction
mechanism only.

## What a reviewer should confirm

1. **No gameplay is enabled.** `StockTransactionHarness` is engine-free and
   link-compiled into the net8 test project. It is a spike substrate, not a shipped
   command. The Gate A slice, the 20/13/7 roster, and the RD-T001 guard are untouched.
2. **The server resolves the exact authored vector.** A donation names an *option id*;
   the harness maps it to the authored item vector server-side. The client never
   supplies a quantity, and an unknown option rejects (`DonationOptionNotAccepted`).
3. **Debit and credit both require a real fit — no trusted client claim.**
   - Insufficient source items reject (`DonationItemsMissing` / `StockQuantityUnavailable`).
   - An over-capacity deposit rejects (`StoneStockCapacityExceeded`).
   - A player inventory that cannot accept the whole withdrawn vector rejects
     (`PlayerInventoryCannotFit`).
   Every rejection changes neither ledger.
4. **Stale revisions reject pre-write.** An expected-revision mismatch on either the
   player inventory or the Stock rejects before any journal write, and returns the
   current revision the caller must refetch.
5. **Replay is exactly-once.** Same operation id + same binding returns the one
   recorded terminal result (even across a fresh process over the same journal); a
   conflicting binding under the same id is `OperationConflict`. Neither replays nor
   conflicts transfer twice.
6. **Process death converges to one transfer or none.** A crash injected at *every*
   boundary (intent, source-debit, destination-credit, terminal) is exercised: a
   crash **before** the terminal record leaves **no** transfer observable (both
   ledgers unchanged, revision 0), and resuming the same operation on a fresh process
   completes it **exactly once**; a crash **after** the terminal record recovers the
   full debit+credit together, and a same-op retry replays the winner.
7. **Conservation.** A donate-then-withdraw round trip conserves total units — nothing
   is created or destroyed.

## How it reuses the proven Gate-A mechanism

The harness is the same append-only, framed, crc-checked journal used by
`OperationReceiptStore` and `FinalLinkHandshakeStore`: **the journal is the
transaction**, and both ledgers are idempotent projections rebuilt from durable,
*terminal-bearing* records only. A partial (non-terminal) operation projects nothing,
so there is never a window where one ledger moved and the other did not. "Process
death" is simulated exactly as in Gate A — a crash injector throws after a chosen
durable boundary, and recovery constructs a fresh harness over the same journal and the
same seeded opening balances.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." Gate B is verified by an engine-free unit suite
(1041/1041 green under warnings-as-errors: 1022 pre-existing + 19 new). It proves the
*transaction* invariants of a disposable harness; it does not prove any joined-client
donation or withdrawal, because no such command is implemented yet (Tracer 3/5 remain
unstarted, `AT-RD-022` stays open). The net48 mod assembly build needs Valheim managed
assemblies supplied by CI, not this worktree — Gate B adds no engine surface, so the
net8 clean compile is the applicable local proof.
