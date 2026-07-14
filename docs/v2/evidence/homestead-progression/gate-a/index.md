---
status: current
---

# Gate A evidence — machine manifest (T003 re-verify)

Verdict: **PASS**. Verifier: `reviewer-adversarial` (non-author). Commit under review:
`main` @ `45a30b41` (PR #302), fix `d4e1ddc`.

| id | claim | artifact |
|----|-------|----------|
| A1 | Hostile principal rejected, zero mutation | in-process suite (`NiflheimProgressionContractTests`) |
| A2 | Same-op replay returns recorded terminal result | in-process suite + `repro/transcript-crash.md` |
| B1 | Real SIGKILL after each durable boundary; fresh process recovers to exactly 1/1/1 | `repro/transcript-crash.md` |
| C1 | Two-client race, separate processes: stale-rev commit rejected `StaleStoneRevision` | `repro/transcript-race.md` |
| C2 | CAS revision rehydrated from journal at boot (`BOOT_STONE_REV=2`) | `repro/transcript-boot.md` |
| D1 | Committed AP visible after restart: `BOOT_MIRRORED == JOURNAL_TRUTH_MIRRORED` | `repro/transcript-boot.md` |
| E1 | Full suite 606/606, rehydration tests 6/6, docs-lint OK | build/test logs (this run) |

- [README.md](README.md) — full analysis and PASS verdict
- [repro/](repro/index.md) — harness sources, attack scripts, captured transcripts
