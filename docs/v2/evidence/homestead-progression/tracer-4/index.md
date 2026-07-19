---
status: current
---

# Tracer 4 evidence — machine manifest (T015)

Verdict: **PASS**. Verifier: `reviewer-adversarial` (non-author). Commit under
verification: authoritative `origin/main` merge `51e59dc3d145183326ccd29a79988180a2a5120c`
(T012 `5bc47b9` / PR #359, T013 `ff2c897` / PR #360, T014 `96209c1` / PR #361).

| id | claim | artifact |
|----|-------|----------|
| T1 | One personal Stone-wide BP balance, not shared, not Tree-bound; cross-Tree spend from one balance | `tests/NiflheimBpDevelopmentTests.cs:217,236,229` |
| T2 | Cross-Tree BP spend gated by Governor Responsibility Range (`OutsideResponsibilityRange` when denied) | `tests/NiflheimBpDevelopmentTests.cs:445` |
| T3 | Node development + equal cumulative Tree investment are one accepted mutation | `tests/NiflheimBpDevelopmentTests.cs:251` |
| T4 | Cooking Tree advances 1→2 via configurable cumulative threshold only; no direct level meter; Active-Stone-Level cap holds | `tests/NiflheimBpDevelopmentTests.cs:276,290,324,344` |
| T5 | Offering + personal AP/Facet-Credit purchase ownership/provenance; one debit + one record | `tests/NiflheimPurchaseTierTests.cs:277,490,302` |
| T6 | Same-Tree Tier Access DERIVED (never stored); Swift prior-Offered-Set exclusion; sibling/Local inert | `tests/NiflheimPurchaseTierTests.cs:346,370,394` |
| T7 | Local nodes complete but are never Offered/purchased (`NodeNotOffered`) | `tests/NiflheimBpDevelopmentTests.cs:593`; `tests/NiflheimPurchaseTierTests.cs:246,259` |
| T8 | One Settlement-wide Local policy, no per-effect override; placement = policy AND ordinary build Permission | `tests/NiflheimLocalPolicyDormancyTests.cs:150,173,202` |
| T9 | Relationship/policy/Stone/Tree dormancy + rejoin re-derived from persisted Stone, zero writes, no active-effects ledger | `tests/NiflheimLocalPolicyDormancyTests.cs:355,370,388,399,413,424,437` |
| T10 | Hostile identity / stale+concurrent revisions / same+conflicting replay / content mismatch / restart-recovery / all named rejections | rejection matrix in README (Bp/Pur/Pol file:line) |
| T11 | Adversarial red-first: 3 production invariants mutated → intended test each went RED → reverted green; `git diff` empty | README §"Adversarial red-first mutation probes" |
| T12 | Full suite 1195/1195 (shared-grammar subset 69/69); both net48 Release builds 0w/0e; docs-lint OK 179; `git diff --check` clean | build/test logs (this run) |
| T13 | Engine-free: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in tested source; net8 link-compile = real execution. Restart is in-process rehydration; NO playable/live-client claim | README §"Engine-free vs real-runtime honesty" |

- [README.md](README.md) — full analysis and PASS verdict
