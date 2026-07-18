---
status: current
---

# Tracer 3 (T011) — out-of-process persistence repro

Independent verification harness for the T010 Facet-commitment slice. It link-compiles
the **shipped** engine-free source (no copy, no fork) — the same `../src` file set the
net8 test project compiles — into a net8 console app, and drives it through **real OS
process death** to prove the one dimension the in-process xUnit restart test cannot: the
writer process is genuinely dead (SIGKILL, no managed unwind) before the reader boots and
reconstructs state from the fsync'd journal.

## What it proves

- `commit-kill` — a child process commits Cooking into the Profession Facet through the
  shipped `FacetCommandHandler` (which fsyncs its intent + committed journal boundaries
  inside `Handle`), then `SIGKILL`s its own pid. Exit 137, no `finally`, no graceful close.
- `recover` — a fresh process constructs a new handler over the **same journal only**.
  `FacetCommandHandler`'s constructor rehydrates the Stone projection from journal truth.
  The harness reports the reconstructed commitment, then resubmits the same operation id
  and confirms it **Replays** rather than double-committing.

Exactly-once persistence across real death = one Committed Tree, revision advanced once,
same-op resubmission returns the recorded terminal result.

## Run it

```
cd docs/v2/evidence/homestead-progression/tracer-3/repro
./crash_recover.sh
```

Requires the build SDK env (`VALHEIM_MANAGED` / `BEPINEX_CORE`) only for the main repo
build; this harness is engine-free and builds with the plain .NET 8 SDK.

Captured output: [transcript-crash-recover.md](transcript-crash-recover.md).

## Files

- `Tracer3Harness.csproj` — link-compiles the shipped slice (SrcRoot → `src/SBPR.Niflheim.HomesteadStones`).
- `Program.cs` — `commit-kill` and `recover` modes.
- `crash_recover.sh` — drives child death then recovery over a shared temp journal.
- `transcript-crash-recover.md` — captured run against `6d0adc2`.
