# niflheim-progression-spike — Gate-A executable spike (T001)

Disposable spike harness. It selects and **proves** the two blocking Gate-A
mechanisms for the Homestead Stone progression S2 slice, before any gameplay
node work:

- **Authenticated principal:** server-derived platform id (candidate **A**)
  indirected through a server-owned platform-id → AccountId map (candidate **E**).
  The transport attributes the peer out-of-band; client payload identity is a
  *claim to compare*, never authority.
- **Durable transaction/receipt:** append-only write-ahead journal with an fsync
  (`FileStream.Flush(true)`) at every durable boundary (candidate **1**). The
  journal *is* the transaction; the Stone and character aggregate writes are
  idempotent projections rebuilt from the durable journal, so a crash between the
  two separately-saved aggregates cannot leave a partial result.

Rejected alternatives (eliminated on grounded facts, see
`docs/v2/planning/homestead-stone-progression-research.md`): identity B
(`m_uid` session id), C (character ZDOID as account), D (`serverSyncedPlayerData`
claim bag); receipt 3 (whole-JSON rewrite), 4 (ZDO piggyback). Candidate 2
(SQLite) is held in reserve — escalate only if the journal exposes a
recovery/lookup gap under load.

## net48 constraint

The **harness** targets net8.0 (headless CI utility, like the sibling tools). The
**mechanism core** — `DurableJournal.cs`, `PrincipalResolver.cs`,
`OperationPipeline.cs` — is deliberately written against the net48-safe API subset
only (each file's header documents the audit), so the selected mechanism is
directly consumable by the net48 `SBPR.Niflheim.HomesteadStones` runtime that T002
will build.

## Run

```bash
dotnet run -c Release --project tools/niflheim-progression-spike
```

Exit code 0 = all acceptance tests pass. Set `SPIKE_EVIDENCE_DIR=<dir>` to also
write `gate-a-spike-run.txt`.

## Acceptance tests proven

- **AT-P0-IDENTITY** — server-derived principal binds; a hostile client whose
  authenticated socket is `attacker` but whose payload claims `owner` is rejected
  as `PrincipalMismatch`; payload without an authenticated connection is
  `UnauthenticatedPeer`; a hostile submission is non-mutating (no journal written).
- **AT-P0-CRASH-EACH-WRITE** — for **every** durable boundary N ∈ {1,2,3,4} a
  **real child process** applies the operation and is hard-killed
  (`Environment.Exit(137)`) right after boundary N. The parent then re-submits the
  same `operationId` and recovers exactly one terminal result (+1 Personal AP,
  +1 Cumulative AP, +1 Mirrored Stone AP); a second re-submit returns `Replayed`
  with identical balances. Conflicting `operationId` reuse rejects as
  `OperationConflict`.
- **AT-P0-RECOVERY-REPORT** — a partial (no-terminal) durable state reports
  `QUARANTINE` (operator decides; nothing is guessed); a recovered state reports
  `RECOVERABLE` with the one true balance, re-derived from the journal only.

## What this spike deliberately does NOT do

No gameplay Trees or nodes. No production relationship bypass. No claim that any
runtime feature is playable. It proves the substrate; T002 builds the real
vertical slice on it.
