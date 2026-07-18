---
status: current
---

# Tracer 3 — independent verification evidence (T011)

Verifier: `reviewer-adversarial` (non-author of the T010 Facet-commitment implementation).
Verdict recorded by a non-author, per the per-task definition of done (tasks.md).

## Verdict: PASS

Independent verification of **Tracer 3 — Profession/Martial Tree commitment** (T010 slice)
against authoritative main `6d0adc2af1693bdc33559c2773e0252177664574`. The worktree
`verify/t011-tree-commitment` shares this exact base (merge-base with `origin/main` ==
`6d0adc2`, clean tree). Every named acceptance case, restart persistence, no-mutation
invariant, and current-main Resource Delivery regression class was independently
re-executed against the shipped code; no product defect was found.

## Commit under verification

`6d0adc2af1693bdc33559c2773e0252177664574` (current `origin/main`). The T010
implementation is present: the pure `StoneFacets.CommitTreeToFacet` transition
(`Domain/StoneProgression/StoneFacets.cs`), the receipt-backed `FacetCommandHandler`
(`Application/Commands/FacetCommands.cs`), the engine-free Stone aggregate projection
(`Persistence/Stone/StoneAggregateStore.cs`), and the hints-only panel affordance
(`Features/Progression/HomesteadProgressionPanel.cs`).

## What was verified, and how

### 1. Acceptance surface (engine-free, real-run)

The shipped Facet slice is engine-free and link-compiles into the net8 test project, so
its full acceptance surface is exercised by **real code execution**, not simulation. The
19-test `NiflheimFacetCommitTests` suite closes every named acceptance:

| acceptance | case | file:line |
|---|---|---|
| `AT-COMMIT-PROFESSION-FACET` | commit Cooking → Profession Facet persists exact choice, receipt-backed, rev+1 | `tests/NiflheimFacetCommitTests.cs:155` |
| `AT-COMMIT-MARTIAL-FACET` | commit Warrior → Martial Facet persists exact choice | `tests/NiflheimFacetCommitTests.cs:177` |
| (both, independent) | Crafting + Archer coexist, 2 committed trees | `tests/NiflheimFacetCommitTests.cs:191` |
| `AT-COMMIT-STALE` | stale expected revision rejects `StaleStoneRevision`, nothing journaled | `tests/NiflheimFacetCommitTests.cs:205` |
| `AT-FACET-OCCUPIED` | second commit into occupied Facet rejects `FacetOccupied` | `tests/NiflheimFacetCommitTests.cs:218` |
| `AT-FACET-CATEGORY` | Cooking → Martial Facet rejects `FacetCategoryMismatch` | `tests/NiflheimFacetCommitTests.cs:233` |
| (eligibility) | unknown candidate rejects `TreeNotEligible` | `tests/NiflheimFacetCommitTests.cs:243` |
| (palette drift) | stale paletteVersion rejects `ContentVersionMismatch` | `tests/NiflheimFacetCommitTests.cs:251` |
| (tree drift) | known candidate, wrong version rejects `ContentVersionMismatch` | `tests/NiflheimFacetCommitTests.cs:261` |
| `AT-COMMIT-UNAUTHORIZED` | Attunement-only actor rejects `Unauthorized` | `tests/NiflheimFacetCommitTests.cs:272` |
| `AT-COMMIT-UNAUTHORIZED` | hostile principal claim rejects `PrincipalMismatch` | `tests/NiflheimFacetCommitTests.cs:283` |
| `AT-COMMIT-UNAUTHORIZED` | Bond present but outside Responsibility Range rejects `OutsideResponsibilityRange` | `tests/NiflheimFacetCommitTests.cs:297` |
| `AT-COMMIT-REPLAY` | same op returns recorded result, no double bump | `tests/NiflheimFacetCommitTests.cs:311` |
| `AT-COMMIT-REPLAY` | same op id, different tree rejects `OperationConflict` | `tests/NiflheimFacetCommitTests.cs:327` |
| `AT-COMMIT-REPLAY` | in-process restart rehydrates commitment from journal, resubmit Replays | `tests/NiflheimFacetCommitTests.cs:340` |
| `AT-NO-STONE-LEVEL-MUTATION` | no change to Historical/Active Stone Level, Mirrored AP, foundational identities, node dev, or character AP/BP/purchases | `tests/NiflheimFacetCommitTests.cs:365` |
| (capacity) | Active Stone Level below initial Tree Level rejects `ActiveStoneLevelTooLow` | `tests/NiflheimFacetCommitTests.cs:391` |
| (panel) | hints-only when empty, commitment when occupied, no client-authoritative flag | `tests/NiflheimFacetCommitTests.cs:406` |
| (purity) | pure transition never mutates its input aggregate | `tests/NiflheimFacetCommitTests.cs:428` |

### 2. Restart / exactly-once persistence (real out-of-process death)

The in-process restart test (`:340`) rehydrates a fresh handler in the **same** live
process. To close the dimension it cannot — a genuinely dead writer before the reader boots
— an independent out-of-process harness (`repro/`) link-compiles the shipped slice, commits
one Tree, and `SIGKILL`s its own pid (exit 137, no managed unwind). A fresh process
reconstructs the Stone projection from the fsync'd journal only:

- reconstructed exactly **one** Committed Tree (`Cooking`), Stone revision advanced
  **exactly once** (5 → 6);
- resubmitting the same operation id **Replayed** (no double-commit, revision still 6).

See [repro/transcript-crash-recover.md](repro/transcript-crash-recover.md).

### 3. No Stone Level / AP / BP / purchase mutation

`AT-NO-STONE-LEVEL-MUTATION` (`:365`) asserts a commit preserves Historical and Active
Stone Level, Mirrored AP, foundational identities, and node development on the Stone, and
leaves the entire character aggregate (personal AP/BP/purchases) structurally equal. The
pure transition (`StoneFacets.cs:239-266`) copies every non-commitment field verbatim and
only appends one `CommittedTreeRecord` at the initial Tree Level with zero cumulative BP.

### 4. Current-main Resource Delivery regression

Current main includes later Resource Delivery changes (PR #352/#355/#356) on top of the
T009/T010 slice. The full suite and the relationship/receipt/resource-delivery regression
class both pass at this exact head, so the Tree-commit authority path and delimiter-safe
relationship/receipt framing are intact under current main:

- Full suite: **1126 / 1126** passed.
- Relationship + Receipt + ResourceDelivery filter: **199 / 199** passed.

### 5. Builds, docs lint, diff check

- `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release`: **0 warnings, 0 errors**.
- `dotnet build src/SBPR.Trailborne -c Release`: **0 warnings, 0 errors**.
- `python3 scripts/docs-lint.py`: **OK — 174 docs**.
- `git diff --check`: clean.

## Engine-free vs real-runtime honesty

The Facet-commitment slice is deliberately engine-free (no UnityEngine/Valheim/BepInEx),
so every claim above is **real code execution** — the net8 test run and the out-of-process
harness both run the shipped C#, not a mock of it. What is **not** covered here (and is not
in T010/T011 scope): a joined Valheim client issuing the commit RPC over the live net48
transport. The command handler is transport-agnostic by design; the net48 ingress/ZDO seam
that would carry a real client commit is a later node task, not this tracer. No live-client
gameplay claim is made.

- [machine manifest](index.md)
- [repro/](repro/index.md) — harness sources, crash script, captured transcript
