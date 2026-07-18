---
status: current
---

# Tracer 3 evidence — machine manifest (T011)

Verdict: **PASS**. Verifier: `reviewer-adversarial` (non-author). Commit under
verification: authoritative main `6d0adc2af1693bdc33559c2773e0252177664574`.

| id | claim | artifact |
|----|-------|----------|
| T1 | Valid Profession (Cooking) + Martial (Warrior) commits persist exact authored choice, receipt-backed, revision +1 | `tests/NiflheimFacetCommitTests.cs:155,177,191` |
| T2 | Stale expected revision rejects `StaleStoneRevision`, nothing journaled | `tests/NiflheimFacetCommitTests.cs:205` |
| T3 | Occupied Facet rejects `FacetOccupied`; original commitment unchanged | `tests/NiflheimFacetCommitTests.cs:218` |
| T4 | Wrong-category tree rejects `FacetCategoryMismatch`; unknown rejects `TreeNotEligible` | `tests/NiflheimFacetCommitTests.cs:233,243` |
| T5 | Stale palette/tree version rejects `ContentVersionMismatch` | `tests/NiflheimFacetCommitTests.cs:251,261` |
| T6 | Unauthorized: Attunement-only `Unauthorized`, hostile claim `PrincipalMismatch`, outside range `OutsideResponsibilityRange` | `tests/NiflheimFacetCommitTests.cs:272,283,297` |
| T7 | Replay: same op Replays; conflicting reuse `OperationConflict`; in-process restart rehydrates | `tests/NiflheimFacetCommitTests.cs:311,327,340` |
| T8 | No mutation of Historical/Active Stone Level, Mirrored AP, foundational identity, node dev, or character AP/BP/purchases | `tests/NiflheimFacetCommitTests.cs:365` |
| T9 | Active Stone Level capacity gate rejects `ActiveStoneLevelTooLow` | `tests/NiflheimFacetCommitTests.cs:391` |
| T10 | Real SIGKILL after commit (exit 137); fresh process rebuilds exactly 1 Committed Tree at rev 6; same-op resubmit Replays | `repro/transcript-crash-recover.md` |
| T11 | Full suite 1126/1126; relationship/receipt/resource-delivery regression 199/199 | build/test logs (this run) |
| T12 | Both net48 Release builds 0 warnings/0 errors; docs-lint OK 174; `git diff --check` clean | build logs (this run) |

- [README.md](README.md) — full analysis and PASS verdict
- [repro/](repro/index.md) — harness sources, crash script, captured transcript
