---
status: current
---

# RD-T004 (Tracer 1) — Qualifying loyalty sources: relationship integration evidence

Baseline: fix-forward over merged PR #349 (`4a5b66d`), which shipped the RD-T004
Tracer-1 coordinator (`QualifyingSourceRule` + `StoneConnectionSourceRegistry`)
**standalone**. Independent review found the coordinator was never driven by the
real relationship command path and was incorrect at three lifecycle edges. This
change closes those gaps against the plan's Tracer-1 work shape ("integrate source
add/remove with Bond/Attunement command receipts") and named acceptance `AT-RD-002`.

Governing artifacts: `docs/v2/planning/homestead-resource-delivery-{spec,plan,data-model,contracts}.md`
(spec RD-002 / RD-004; contracts §"Relationship-to-Connection integration").

## Defects fixed

1. **Standalone coordinator → real command integration.**
   `RelationshipCommandHandler.ApplyProjections` now drives the matching Connection
   source transition (`ActivateRelationship`/`ReleaseRelationship`) for every
   committed `CreateBond`/`CreateAttunement`/`ReleaseRelationship`. Because
   `ApplyProjections` runs only **after** the relationship terminal boundary is
   fsync'd (live commit) **and** on boot rehydration, the account-pair Connection is
   a recoverable projection of the committed relationship journal — acknowledgement
   follows a durable source transition, not a standalone side call. The coordinator
   is composed into `FoundationalProgressionServer.Create` (new
   `connection-sources.journal`, product scope `SBPR.Trailborne`).

2. **Expired-grace reconnect restored frozen maturity → resets age.**
   `StoneConnectionSourceRegistry.AddSourceTo` reconciles an **expired** grace before
   resuming, so a reconnect after the 72h grace has elapsed starts a fresh Active
   segment (age zero) instead of restoring the frozen tier. A within-grace reconnect
   still resumes the frozen age (the reconcile is idempotent for non-expired grace).

3. **Grace-expiry reset was not durable → journaled.**
   `ReconcileGraceExpiry` now appends a framed, crc-checked grace-reset record
   (`CGRR`) keyed by identity + expiry, so a restarted process replays the terminal
   `Reset` instead of restoring the pre-reset frozen grace maturity.

4. **Replay recomputed/emptied the affected set → exact persisted result.**
   Each committed activate/release persists its exact ordered affected-Connection-key
   set with the journal event; a replay returns that recorded set verbatim (`Replayed`
   outcome) — never a recomputation against the current roster and never an empty
   release set after the participant is already gone. The set is rebuilt on restart.

## Named acceptance

| id | claim | artifact |
|----|-------|----------|
| AT-RD-002 | Only Bonded↔Attuned/Bonded↔Bonded pairs source a Connection through a real relationship lifecycle; command integration, expired-grace reset, durable restart, and exact replay all hold | `RelationshipCommandHandler` + `StoneConnectionSourceRegistry` + `ResourceDeliveryQualifyingSourcesTests` + `ResourceDeliveryRelationshipIntegrationTests` |

## Verification

- Engine-free seams link-compile into the net8 test project under
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Full suite: **1087/1087 passed** (1080 pre-existing + 7 new integration
  regressions); no existing test changed or regressed.
- net48 mod assembly: `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c
  Release` → **0 warnings, 0 errors** (clean build).
- `docs-lint`: OK. `docs-freshness`: advisory pass.

## Boundary

This closes the Tracer-1 relationship integration for `AT-RD-002`. No item movement,
donation, Stock, or gameplay is enabled — the coordinator remains a server-side
projection. RD-T005 stays blocked until this is merged and independently verified.
Merge remains Daniel's; writer ≠ verifier.
