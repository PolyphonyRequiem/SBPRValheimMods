---
status: current
---

# RD-T004 (Tracer 1) — human orientation

Human-readable companion to the [machine manifest](index.md) for the RD-T004
Tracer-1 **relationship-to-Connection integration** fix-forward over merged PR #349.

## Why this exists

PR #349 shipped the Tracer-1 qualifying-source coordinator
(`QualifyingSourceRule` + `StoneConnectionSourceRegistry`) but wired it to nothing:
the real `RelationshipCommandHandler` never drove it, and three lifecycle edges were
wrong. Independent review flagged it as standalone and incorrect. This change makes
the coordinator a recoverable projection of the committed relationship journal and
fixes the edges, per plan Tracer-1 ("integrate source add/remove with Bond/Attunement
command receipts") and `AT-RD-002`.

## What a reviewer should confirm

1. **Integration is in the same logical transaction.** The source transition runs in
   `RelationshipCommandHandler.ApplyProjections`, which fires only after the
   relationship terminal boundary is durable (live commit) and on boot rehydration —
   so a joined session's Bond/Attunement/Release advances the account-pair Connection,
   and a restart reconstructs it from the relationship journal alone.
2. **Expired-grace reconnect resets age.** Reconnecting a pair after its 72h grace has
   fully elapsed starts a fresh Active segment (age zero); a within-grace reconnect
   still resumes the frozen age.
3. **Grace-expiry reset is durable.** The Grace→Reset transition is journaled (`CGRR`
   record) so a restart replays the reset instead of restoring frozen maturity.
4. **Replay returns the exact original result.** Replaying a committed activate/release
   returns the persisted ordered affected-set verbatim — never a recomputation and
   never an empty release set.

## Honesty note

Per AGENTS.md: "logs green ≠ playable." Verified by the engine-free unit suite
(1087/1087 green under warnings-as-errors: 1080 pre-existing + 7 new integration
regressions) plus a clean net48 mod-assembly build. It proves the domain/application
integration and recovery invariants; it does not prove joined-client Resource Delivery
gameplay, which does not exist yet. RD-T005 stays blocked until this merges and is
independently verified.
