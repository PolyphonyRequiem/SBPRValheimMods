---
status: current
---

# RD-T005 (Tracer 2) — human orientation

Human-readable companion to the [machine manifest](index.md) for the RD-T005
Tracer-2 **weekly/daily participation and AP multiplier** implementation.

## Why this exists

Tracer 1 derived which account pairs form qualifying loyalty Connections. Tracer 2
turns that into the actual participation economy: paying weekly upkeep and doing
daily practice earns a per-account `0×/1×/2×` participation tier, which combines with
the strongest Connection maturity to scale an account's otherwise-authorized Personal
AP. It delivers the account–Stone Participation aggregate end to end (spec RD-006 /
RD-009 / RD-019) without moving any items — item movement stays gated behind Gate B /
Tracer 3.

## What a reviewer should confirm

1. **Rolling windows are exact and exclusive.** Weekly upkeep is current for exactly
   seven days; the daily window for exactly 24 hours. `2×` requires BOTH current — a
   still-running daily window with lapsed weekly upkeep is `0×`, not `2×`.
2. **The daily cycle is one durable five-instance counter.** Five distinct physical
   Foundational placements complete it once; duplicates count once; evidence during
   the completed window does not pre-build the next cycle; a fresh zero-progress cycle
   opens only after expiry is reconciled.
3. **AP floors once.** The award is `floor(base × participation × maturity)` with a
   single final floor over exact rational maturity — never a per-factor rounding.
   Cumulative/Mirrored equal the floored award; BP is untouched; a `0×` or
   non-contributing account is a recorded no-award.
4. **The multiplier never widens authorization.** The AP source's own
   actor/relationship check runs upstream; this slice only scales an award the source
   already authorized. Absent relationship / missing qualifying Connection → zero.
5. **The fifth-placement order is correct.** For a placement that is also an AP
   source, the combined operation computes the AP subresult against the PRE-practice
   tier, then applies practice — so the fifth placement uses the prior `0×/1×` tier
   and `2×` applies only to subsequent events. Replay (live and post-restart) returns
   the recorded ordered result verbatim.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." Verified by the engine-free unit suite
(1126/1126 green under warnings-as-errors: 1100 pre-existing + 26 new) plus a clean
net48 build of the HomesteadStones and mod assemblies. This proves the domain /
application participation + AP-multiplier invariants and their restart/replay
recovery; it does not prove joined-client Resource Delivery gameplay, which does not
exist yet. Opening a reviewable PR and stopping at review.
