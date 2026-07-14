---
status: current
---

# Gate A re-verification harness (T003)

Out-of-process attack harness. **Link-compiles the remediated T002 slice** (commit
`45a30b41`) via MSBuild `SrcRoot` — no copy, no fork. Requires only engine-free source
files, so it builds into a plain net8 console app.

## Build

```
export VALHEIM_MANAGED=<...>/valheim-managed   # satisfies repo Directory.Build.props only
dotnet build GateAHarness.csproj -c Release \
  -p:SrcRoot=<repo>/src/SBPR.Niflheim.HomesteadStones
```

## Run

- `race_attack.sh` — two-client CAS race across separate processes (defect 1 regression probe).
- `crash_attack.sh` — real SIGKILL after each of the 4 durable boundaries + fresh-process recovery.
- Boot-balance: `GateAHarness.dll boot-balance <journal>` — reads balances without resubmitting
  and compares to journal truth (defect 2 regression probe).

## Modes

`child-crash <journal> <opId> <boundary>` · `recover <journal> <opId>` ·
`race-child <journal> <opId> <expectedStoneRev>` · `boot-balance <journal>`

## Files

- [Program.cs](Program.cs) — harness driver
- [GateAHarness.csproj](GateAHarness.csproj) — link-compile project
- [crash_attack.sh](crash_attack.sh) · [race_attack.sh](race_attack.sh)
- [transcript-crash.md](transcript-crash.md) · [transcript-race.md](transcript-race.md) · [transcript-boot.md](transcript-boot.md)
