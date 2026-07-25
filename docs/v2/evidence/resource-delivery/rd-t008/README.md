---
status: current
---

# RD-T008 (Tracer 3) — human orientation

Human-readable companion to the [machine manifest](index.md) for the RD-T008
Tracer-3 **donation menu and Stone Stock** implementation.

## Why this exists

Tracer 2 turned loyalty Connections into a participation economy that scales AP.
Tracer 3 delivers the first real Stone Stock loop: an owner-role Bond picks the two
donation options a Stone will accept (or the authored default pair materializes when
upkeep is needed first), a player donates one option, and the exact authored item
vector moves once from the player's server-observed inventory into one durable virtual
Stockpile with provenance. It also delivers the delegated-withdrawal permission
lifecycle — the canonical grant/revoke record that Tracer 5's withdrawal will honor.
It reuses the Gate B transaction discipline (RD-T006) but is the first product slice,
not a disposable spike.

## What a reviewer should confirm

1. **The Level-2 menu is exact and owner-role gated.** The candidate pool is exactly
   `20 Wood`, `20 Stone`, `10 Wood + 10 Stone`; the default pair is `20 Wood` +
   `20 Stone`. Only an active Bond carrying the server-authored owner role may select,
   selecting exactly two distinct current options. Selection locks for the level; the
   default is authored content (materialized once, idempotently), not a random choice.
2. **A donation transfers once or not at all.** The server resolves the exact authored
   vector — the client submits no quantities. A valid donation debits the player and
   credits the one Stockpile exactly once with donation provenance; a replayed op id
   never double-transfers. An unselected option, missing items, a full/over-capacity
   deposit, a stale Stock revision, or a current pending generated delivery's capacity
   priority each change nothing.
3. **One durable Stockpile, reconstructed on restart.** Balances/revisions/menu project
   from the durable, terminal-bearing journal only; a fresh registry over the same
   journal and opening balances reconstructs the exact state, and a committed op
   replays rather than re-applying. Configured capacity overrides still gate deposits
   by the same non-negative kinds/total/per-item invariant scan.
4. **One canonical permission, complete revocation, non-transitive authority.** Exactly
   one `(StoneId, grantee)` record with at most one active generation. Duplicate grants
   against an active record reject (no fork); regrant after revocation increments
   generation; revocation removes all current delegated authority; a stale-revision
   grant/revoke rejects; a reused op id with a different grantee is a conflict. A
   grantee holds only withdrawal authority — it can never grant onward — and another
   grantee's permission implies nothing. State and generation survive restart.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." Verified by the engine-free unit suite
(1154/1154 green under warnings-as-errors: 1126 pre-existing + 28 new) plus a clean
net48 Release build of the HomesteadStones library and the mod assembly. This proves
the donation-menu, Stockpile, and delegated-permission domain/application invariants
and their restart/replay recovery; it does NOT prove joined-client Resource Delivery
gameplay, which does not exist yet. Generated-delivery deposit and the withdrawal
item-move are later tracers. Opening a reviewable PR and stopping at review.
