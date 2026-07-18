---
status: current
---

# repro/ — machine manifest (Tracer 3, T011)

Verdict: **PASS**. Verifier: `reviewer-adversarial` (non-author of T010). Commit under
verification: authoritative main `6d0adc2af1693bdc33559c2773e0252177664574`.

| file | role |
|------|------|
| `Tracer3Harness.csproj` | link-compiles the shipped `src/SBPR.Niflheim.HomesteadStones` engine-free slice (no copy/fork) |
| `Program.cs` | `commit-kill` (real SIGKILL after commit) + `recover` (journal-only rehydrate) |
| `crash_recover.sh` | drives child death then recovery over a shared temp journal |
| `transcript-crash-recover.md` | captured run against `6d0adc2` |

Result: child `Applied` (rev 5→6) then died `SIGKILL` exit 137; fresh process rebuilt
exactly one Committed Tree `Cooking` at rev 6; same-op resubmit `Replayed` with no
double-commit. Exactly-once persistence across real process death confirmed.
