---
status: current
---

# repro/ — file manifest

| file | role |
|------|------|
| [Program.cs](Program.cs) | out-of-process harness driver (SIGKILL, race, boot-balance modes) |
| [GateAHarness.csproj](GateAHarness.csproj) | link-compiles the remediated slice via `SrcRoot` |
| [crash_attack.sh](crash_attack.sh) | real process-death attack across 4 durable boundaries |
| [race_attack.sh](race_attack.sh) | two-client CAS race across separate processes |
| [transcript-crash.md](transcript-crash.md) | captured crash/recovery output (PASS) |
| [transcript-race.md](transcript-race.md) | captured race output (PASS — stale rev rejected) |
| [transcript-boot.md](transcript-boot.md) | captured boot-balance output (PASS — boot == journal truth) |

See [../README.md](../README.md) for the verdict and analysis.
