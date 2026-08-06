---
status: current
---

# Tracer 4 — independent verification evidence (T015)

Verifier: `reviewer-adversarial` (non-author of the T012–T014 shared progression
grammar). Verdict recorded by a non-author, per the per-task definition of done
(tasks.md).

## Verdict: PASS

Independent verification of **Tracer 4 — shared BP / purchase / policy progression
grammar** (the T012 BP-development, T013 Offering/purchase/Tier-Access, and T014
Settlement Local policy + relationship dormancy slices) against authoritative
`origin/main` merge commit
`51e59dc3d145183326ccd29a79988180a2a5120c`. The worktree
`verify/t015-shared-grammar` shares this exact base (`HEAD == origin/main ==
51e59dc`, clean tree). Every required independent proof, every named stable
rejection, and the current-main regression class were independently re-executed
against the shipped code; no product defect was found.

## Commits under verification

The shared grammar merged in three reviewed PRs, all present at `51e59dc`:

| slice | feature commit | merge |
|---|---|---|
| T012 — one Stone-wide BP balance, node development = cumulative Tree investment, Cooking 1→2, escalating config | `5bc47b9` | PR #359 (`affb983`) |
| T013 — personal Offering, AP/Facet-Credit purchase provenance, derived same-Tree Tier Access | `ff2c897` | PR #360 (`3dbb17e`) |
| T014 — one Settlement-wide Local policy composed with build Permission, relationship/policy/Stone/Tree dormancy | `96209c1` | PR #361 (`51e59dc`) |

Shipped source under verification (engine-free, `SBPR.Niflheim.HomesteadStones`):
`Domain/CharacterProgression/BondPower.cs`,
`Domain/StoneProgression/TreeDevelopment.cs`,
`Domain/CharacterProgression/NodePurchases.cs`,
`Domain/Activation/DerivedActivationView.cs` (`LocalEffectActivationView` +
`SettlementLocalPolicy`), and the receipt-backed command handlers in
`Application/Commands/` (`ActivityCommands`, `DevelopmentCommands`,
`PurchaseCommands`, and the Local policy handler).

## Required independent proof — how each claim was verified

The three shared-grammar test classes (`NiflheimBpDevelopmentTests`,
`NiflheimPurchaseTierTests`, `NiflheimLocalPolicyDormancyTests` — **69 tests**)
link-compile the SHIPPED source directly into the net8 test host, so every claim
below is **real code execution** of the production transition, not a simulation.

| # | required proof | verified by | file:line |
|---|---|---|---|
| 1 | one personal Stone-wide BP balance, not shared, not Tree-bound | credit once, spend across Cooking AND Warrior from the same balance; second Governor's balance untouched | `NiflheimBpDevelopmentTests.cs:217,236` |
| 2 | cross-Tree BP spend under Governor Responsibility Range | out-of-range authority stub rejects `OutsideResponsibilityRange`; in-range cross-Tree spend applies | `NiflheimBpDevelopmentTests.cs:229,445` |
| 3 | node development and cumulative Tree investment are ONE accepted mutation | one accepted BP delta advances node progress AND equal cumulative Tree investment atomically | `NiflheimBpDevelopmentTests.cs:251` |
| 4 | Cooking Tree advances 1→2 through configurable cumulative investment, no direct Tree-level meter | crossing the configured cumulative threshold advances exactly once; sub-threshold never moves the level; no direct level command exists (only the cumulative path) | `NiflheimBpDevelopmentTests.cs:276,290,344` |
| 5 | Offering and personal AP/Facet-Credit purchase ownership/provenance [^superseded-t5] | completed personal node becomes Offered and an eligible Attuned buys it with PersonalAP; Facet-Credit payment debits the matching Facet Credit; one debit + one purchase record | `NiflheimPurchaseTierTests.cs:277,490,302` |
| 6 | same-Tree Tier Access and Swift prior-Offered-Set exclusions | Tier Access is DERIVED (never stored) from prior same-Tree Offered purchases + caps; Swift Preparation rejects `PriorOfferedSetIncomplete` until its prior set is held; sibling-Tree/Local purchases neither grant nor block | `NiflheimPurchaseTierTests.cs:346,370,394` |
| 7 | Local nodes never offered/purchased | Local node completes but `NodeOffered==false`; purchase of a Local (or unavailable) node rejects `NodeNotOffered` | `NiflheimBpDevelopmentTests.cs:593`; `NiflheimPurchaseTierTests.cs:246,259` |
| 8 | one Settlement-wide Local policy composed with ordinary Permission | one policy (Everyone/Attuned/Private) governs every Local Effect with no per-effect override; placement requires BOTH policy eligibility AND independent build Permission | `NiflheimLocalPolicyDormancyTests.cs:150,173,202` |
| 9 | relationship/policy/Stone/Tree dormancy and rejoin re-derived without an active-state ledger | missing Governor / relationship release / Active-Stone-Level below node level / Tree revoked / rejoin all re-derive active↔dormant from the SAME persisted Stone with zero writes; development is never deleted | `NiflheimLocalPolicyDormancyTests.cs:355,370,388,399,413,424` |
| 10 | hostile identity, stale/concurrent revisions, same/conflicting replay, content mismatch, restart/recovery, every named stable rejection | see the rejection matrix below | (per row) |

### Named stable-rejection matrix (all independently re-executed)

| rejection | slice | file:line |
|---|---|---|
| `PrincipalMismatch` (hostile identity claim) | BP / purchase / policy | `Bp:468`, `Pur:416`, `Pol:339` |
| `Unauthorized` / `Unauthenticated` (Attuned-only credit, non-owner policy, no-auth peer) | BP / policy | `Bp:430`, `Pol:236,328` |
| `OutsideResponsibilityRange` | BP | `Bp:445` |
| `StaleStoneRevision` / `StaleCharacterRevision` / `StalePolicyRevision` | all three | `Bp:481`, `Pur:430`, `Pol:249,260` |
| concurrent expected-revision CAS succeeds when current | policy | `Pol:276` |
| `OperationConflict` (same op id, conflicting payload) | all three | `Bp:529`, `Pur:319`, `Pol:316` |
| replay idempotency (same op → recorded result, no double apply) | all three | `Bp:515,543`, `Pur:302`, `Pol:286,303` |
| `ContentVersionMismatch` (stale palette/tree/Offered-Set expectation) | purchase | `Pur:442` |
| `NodeUnavailable` / `NodeNotOffered` / `TreeMismatch` / `TreeNotCommitted` / `AlreadyDeveloped` / `AlreadyAcquired` / `InsufficientBp` / `InsufficientPersonalAP` / `BpDeltaInvalid` / `RelationshipRequired` | BP / purchase | `Bp:399,409,419,481,502,375,491`; `Pur:266,292,333,480` |
| restart/recovery (fresh handler rehydrates from journal, resubmit replays) | all three | `Bp:557`, `Pur:524`, `Pol:437` |

## Adversarial red-first mutation probes

To confirm the acceptance tests are load-bearing (not vacuous), three
production invariants were temporarily broken; the intended tests failed, then
the source was reverted and re-verified green:

| mutated invariant | file | test that caught it | result |
|---|---|---|---|
| Active-Stone-Level cap on Tree advancement (`ActiveStoneLevel >= targetLevel`) | `TreeDevelopment.cs:253` | `Tree_does_not_advance_when_active_stone_level_too_low_for_target` | RED as expected |
| Governance dormancy conjunct (`!authorizedGovernorPresent`) | `DerivedActivationView.cs:276` | `Missing_authorized_governor_dormants_all_local_effects_without_deleting_development` | RED as expected |
| Swift prior-Offered-Set gate (`PriorOfferedSet` loop) | `NodePurchases.cs:178` | `Swift_preparation_requires_prior_offered_set` | RED as expected |

All three reverted cleanly; `git diff` is empty at completion.

## Builds, full suite, docs

- `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release`: **0 warnings, 0 errors**.
- `dotnet build src/SBPR.Trailborne -c Release`: **0 warnings, 0 errors**.
- Full test suite: **1195 / 1195** passed (shared-grammar subset: 69 / 69).
- `python3 scripts/docs-lint.py`: **OK — 179 docs**.
- `git diff --check`: clean.

## Engine-free vs real-runtime honesty

The entire `SBPR.Niflheim.HomesteadStones` progression grammar is engine-free:
no `UnityEngine`, `BepInEx`, `ZNetView`, `HarmonyLib`, or Valheim type appears in
the tested source, and the test project link-compiles that source into a net8
host. **Every claim above is real code execution of the shipped transition — not
a mock.** What is verified is the pure domain + application grammar: BP
credit/debit, node development + cumulative Tree advancement, Offering/purchase
provenance, derived Tier Access, the Settlement Local policy value object +
owner-only command, and the pure dormancy re-derivation.

Two dimensions are explicitly **NOT** claimed here:

1. **Restart/recovery is in-process journal rehydration**, not an out-of-process
   `SIGKILL`. A fresh handler over the same journal + a fresh store rebuilds the
   projection and replays the original operation as a no-op — real code, but the
   writer never actually dies mid-write. (The Tracer 3 evidence carries the
   genuine out-of-process death harness for the commit path; this tracer did not
   add one for the BP/purchase/policy journals.)
2. **No gameplay-family / "playable" claim.** The net48 build compiles clean, but
   nothing here proves a joined Valheim client can issue the BP-develop, purchase,
   or set-policy RPC over the live transport, or that a Local Effect is delivered
   in-world. The command handlers are transport-agnostic by design; the net48
   ingress/ZDO seam that would carry a real client operation is a later node task,
   not this tracer.

- [machine manifest](index.md)

[^superseded-t5]: **Provenance annotation (ADO #132, 2026-08-06) — the PASS stands; this row's middle clause
    describes a SUPERSEDED contract.** Row 5's first and last clauses ("a completed personal node becomes
    Offered and an eligible Attuned buys it with PersonalAP"; "one debit + one purchase record") are
    unaffected and still hold. Only the middle clause — the `NiflheimPurchaseTierTests.cs:490` citation,
    "Facet-Credit payment debits the matching Facet Credit" — verifies a payment contract Daniel has since
    withdrawn: Facet Credit does not exist, and a Tree-revocation refund now returns ordinary Stone-wide
    Personal AP (ADO #106, #132). This is **annotated, not re-run**: the code Tracer 4 verified has not
    changed, and the code it did not verify (revocation) still does not exist. The asserting test has been
    replaced in the same commit by one asserting the corrected rule. Rows 1–4 and 6–13 are untouched.
    Independent verification of the corrected rule belongs to Tracer 9 / T033, where it was always scheduled.
