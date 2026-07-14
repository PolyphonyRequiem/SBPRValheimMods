---
title: "Homestead Stone progression S2 — implementation plan"
status: accepted
purpose: Plan the dependency-ordered technical proof after specification approval; no implementation is authorized by this document.
---

# Implementation Plan: Homestead Stone progression S2

**Branch:** `feat/niflheim-homestead-stone`
**Date:** 2026-07-13
**Spec:** [`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md)

> **Stop gate:** Daniel approved this package after cluster review and independent verification, then separately
> authorized `tasks` authoring and accepted the audited decomposition via PR #297. No runtime implementation may
> begin without separate authorization.

## Summary

Extend the existing Niflheim-only Homestead Stone runtime from placement/identity into one bounded,
server-authoritative progression technical proof. The implementation will separate world Stone state,
account/character authority and progression state, versioned content, operation receipts, and derived
activation behind explicit Stone-identity commands/read models. It will prove a preconfigured Stone-Level-2
Homestead journey through authenticated AP mirroring, Tree commitment, one personal Stone-wide BP balance,
BP-driven Cooking node development and Tree advancement, personal purchases, 13 executable nodes,
revocation/Facet Credit, relationship release/dormancy, and recovery.

The work proceeds as dependency-linked tracer bullets. Gate A—authenticated identity plus one atomic,
idempotent AP receipt—blocks all gameplay progression work.

## Technical context

**Language/version:** C# `latest`, net48 runtime plugin; net8 testable engine-free seams where repository
conventions permit.
**Primary dependencies:** BepInEx, HarmonyX, Valheim/Unity managed assemblies; no new third-party runtime
dependency selected by this plan.
**Storage:** Existing persistent Stone ZDO substrate plus a new server-owned authoritative character/receipt
store selected only after Gate A's spike. World and character saves are not assumed atomic.
**Testing:** Existing .NET test project, engine-free domain/contract tests, process-kill/recovery harness,
repository docs lint, and joined-client/in-world acceptance per tracer.
**Target platform:** Valheim dedicated/listen server and joined modded clients; Linux is the primary live
verification host.
**Project type:** Niflheim-only sibling BepInEx plugin within the multi-project SBPR repository.
**Performance goals:** Mutations are bounded and event-driven; reads are revisioned projections; no broad
per-frame world/character scans or client polling of full ledgers.
**Constraints:** hostile-client-safe authority, replay/concurrency safety, net48, zero warnings, clean-room,
additive prefab rules, stable IDs/versioning, no second active-state truth, logs-green≠playable.
**Scale/scope:** at least three preconfigured test Homesteads, two authenticated participants per fixture, four
Tree definitions, 20 authored nodes, 13 executable outcomes. This is not the clustered-50-player proof.

## Constitution check

*Gate: pass before research and re-check before tasks.*

| Article | Plan response | Status |
|---|---|---|
| Spec-first | This S2 package is authored before **S2 progression** runtime changes; future behavior changes update spec/code/tests together | PASS |
| Runtime conformance | Progression receives a content/contract drift manifest or equivalent before implementation completion | PASS, design required |
| Corpus-first | Vanilla claims come from the confirmed corpus/decomp pass and must be re-verified against the live build per tracer | PASS |
| Clean-room | Uses vanilla game seams and SBPR source only; no other-mod source copied | PASS |
| Writer ≠ verifier | A separate agent analyzes the package before approval and later each implementation | REQUIRED GATE |
| Daniel controls landing | No auto-merge, no implementation before package approval | PASS |
| Incremental delivery | Gate A plus Tracers 1–9, each with named acceptance | PASS |
| Semver docs tree | All artifacts live in `docs/v2/planning/` with frontmatter and index entries | PASS |
| ADR-0005 adaptation | No `specify` CLI, `.specify/`, or `specs/NNN/` layout | PASS |

No constitutional violation is requested.

## Feature documentation structure

```text
docs/v2/planning/
├── homestead-stone-progression-spec.md          # normative user/behavior contract
├── homestead-stone-progression-research.md      # current-code reality and spike boundary
├── homestead-stone-progression-data-model.md    # aggregates, identities, invariants, transitions
├── homestead-stone-progression-contracts.md     # commands, receipts, reads, rejection vocabulary
├── homestead-stone-progression-plan.md          # this dependency/tracer plan
└── homestead-stone-progression-tasks.md         # accepted dependency map; not execution authorization
```

The existing [`homestead-stone-v1-impl-spec.md`](homestead-stone-v1-impl-spec.md) remains the
placement/identity substrate and is not overwritten.

## Proposed source structure

This is a planning boundary, not files created by this pass.

```text
src/SBPR.Niflheim.HomesteadStones/
├── Domain/
│   ├── Identity/              # stable IDs and authenticated principal value objects
│   ├── Content/               # immutable current-build registry definitions
│   ├── StoneProgression/      # Stone aggregate, Tree/cultivation transitions, invariants
│   ├── CharacterProgression/  # relationships, AP/BP, purchases, durable outcomes
│   └── Activation/            # pure derived view/providers
├── Application/
│   ├── Commands/              # handlers for the contracts package
│   ├── Queries/               # Stone and portfolio read models
│   └── Receipts/              # idempotency, journal, audit, recovery orchestration
├── Persistence/
│   ├── Stone/                 # existing ZDO bridge plus versioned progression fields
│   ├── Characters/            # server-owned world/product-scoped aggregate store
│   └── Recovery/              # receipt reconciliation, invariant scan, operator repair
├── Features/
│   ├── HomesteadStone/        # existing registration/placement/identity
│   └── Progression/           # temporary local presentation and feature composition
└── Adapters/
    ├── Identity/              # connection→account/character binding
    ├── Activities/            # placement/cooking/crafting/combat evidence
    ├── Cooking/
    ├── Crafting/
    ├── Archer/
    └── Warrior/

tests/
├── NiflheimProgressionDomainTests.*
├── NiflheimProgressionContractTests.*
├── NiflheimProgressionRecoveryTests.*
└── NiflheimProgressionAdapterTests.*
```

**Structure decision:** deepen the owning Homestead plugin with domain/application/persistence/adapters. Do
not place domain policy independently inside each Harmony patch, and do not extract a shared Niflheim library
until a second runtime consumer proves the seam.

## Architecture decisions

### A1 — Pure domain above narrow adapters

Tree/ledger/requirement/revocation rules must be engine-free and deterministic. Valheim adapters translate
trusted runtime facts into typed evidence and apply derived providers to game seams. This keeps content tests
fast and prevents five independent patches from becoming policy authorities.

### A2 — One command pipeline

Every mutation follows:

1. authenticate connection principal;
2. bind account and acting character;
3. load Stone/character/authority snapshots and registry versions;
4. validate revisions, authority, content, balances, and requirements;
5. reserve or find operation result;
6. commit/journal deltas recoverably;
7. rebuild/validate projections;
8. acknowledge with revisions and receipt.

There is no direct UI→ZDO write or adapter→wallet write.

### A3 — Receipt-backed cross-aggregate convergence

Gate A chooses the smallest durable mechanism that survives injected process death across Stone and character
writes. It must support idempotency-result lookup, revision/CAS, audit, replay, and operator-visible
quarantine. Simplicity wins, but a mechanism that relies on ordinary world/profile saves being atomic fails.

### A4 — Data-defined content, code-defined providers

Registry data owns IDs, versions, categories, prices, status, requirements, and tunable factors. Provider code
owns bounded behavior seams. UI and handlers consume registry definitions rather than hard-coded Tree names.

### A5 — Derived activation only

After load or mutation, compute activation from aggregate snapshots and registry definitions. Runtime status
objects and recipe/piece visibility are disposable projections. No mutable “currently active perks” ledger.

### A6 — Remote-shaped now, finished UI later

All progression selections are explicit Stone-identity commands. The initial panel may be local, but proximity
is not embedded in command semantics. Adapters continue to require local/server-observed evidence for authored
world actions.

## Delivery phases and tracer bullets

### Gate A — P0 identity plus atomic AP receipt

**Goal:** prove the authority and transaction substrate before any Tree behavior.

**Deliverables after task approval:**

- one authenticated account/character binding;
- one server-owned operation/receipt journal candidate;
- one world-scoped Stone aggregate delta and one character aggregate delta;
- one Foundational placement evidence adapter;
- crash/retry harness and operator inspection.

**Named acceptance:** `AT-P0-IDENTITY`, `AT-P0-AP-ATOMIC`, `AT-P0-REPLAY`, `AT-P0-CRASH-EACH-WRITE`,
`AT-P0-HOSTILE-PRINCIPAL`, `AT-P0-MIRRORED-ACCUMULATES-ONLY`, `AT-P0-RECOVERY-REPORT`.

**Exit:** exactly N Personal AP, N Cumulative AP, and N Mirrored Stone AP under every retry/crash case. This
phase blocks all remaining work.

### Tracer 1 — Versioned state skeleton and read model

**Goal:** load/reload Stone, policy-driven authority index, character, current-build content, receipts, and
derived view with no gameplay effect.

**Named acceptance:** `AT-STATE-ROUNDTRIP`, `AT-CONTENT-MISMATCH-REJECT`, `AT-INVARIANT-QUARANTINE`,
`AT-READMODEL-STONE-ID`, `AT-NO-ACTIVE-LEDGER`.

### Tracer 2 — Relationships and Foundational AP

**Goal:** create one Bond and one Attunement on a preconfigured Stone-Level-2 Homestead; enforce Homestead
active sibling exclusivity; route a real ongoing Foundational placement through Gate A; prove Attunement and
voluntary Bond release/rejoin semantics.

**Named acceptance:** `AT-BOND`, `AT-ATTUNEMENT`, `AT-SIBLING-EXCLUSIVE`, `AT-SEQUENTIAL-SIBLING`,
`AT-COMMUNITY-ATTUNEMENT-EXCEPTION`, `AT-FOUNDATIONAL-CATALOG`, `AT-FOUNDATIONAL-ONGOING`,
`AT-FOUNDATIONAL-EXCLUDED`, `AT-ATTUNEMENT-RELEASE`, `AT-BOND-RELEASE-DORMANCY`, `AT-RELATIONSHIP-RESTART`.

### Tracer 3 — Tree commitment into preconfigured Stone Facets

**Goal:** commit one Profession and one Martial Tree from the fixed data-defined palette without changing
Stone Level.

**Named acceptance:** `AT-COMMIT-PROFESSION-FACET`, `AT-COMMIT-MARTIAL-FACET`, `AT-COMMIT-STALE`,
`AT-FACET-OCCUPIED`, `AT-FACET-CATEGORY`, `AT-COMMIT-UNAUTHORIZED`, `AT-COMMIT-REPLAY`,
`AT-NO-STONE-LEVEL-MUTATION`.

### Tracer 4 — BP-driven node development, Tree advancement, and personal purchase grammar

**Goal:** prove one personal Stone-wide BP balance, BP-driven node development that also constitutes cumulative
Tree investment, Cooking Tree advancement 1→2 without a separate level meter, personal Offering/AP purchase,
derived same-Tree Tier Access, Local policy, and relationship dormancy.

**Named acceptance:** `AT-BP-STONE-WIDE`, `AT-BP-NOT-SHARED`, `AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT`,
`AT-NO-DIRECT-LEVEL-METER`, `AT-TREE-ADVANCE-1-2`, `AT-ESCALATING-COST-CONFIG`,
`AT-LOCAL-NOT-OFFERED`, `AT-PERSONAL-BECOMES-OFFERED`, `AT-PURCHASE-IDEMPOTENT`,
`AT-TIER-SAME-TREE`, `AT-LOCAL-POLICY`, `AT-RELATIONSHIP-DORMANCY`.

### Tracer 5 — Cooking

**Goal:** execute Savor the Hearth, Field Prep, Iron Stomach, and Swift Preparation while
Watchful Cook remains honestly unavailable.

**Named acceptance:** `AT-SAVOR-AREA-EXIT`, `AT-FIELD-PREP-COOKING-POLICY`,
`AT-IRON-STOMACH-75`, `AT-SWIFT-MENU-ONLY`, `AT-COOKING-TIER2`, `AT-NO-COOKING-COMPLETION`,
`AT-WATCHFUL-UNAVAILABLE`.

### Tracer 6 — Crafting

**Goal:** execute Refined Workshop, Masterwork, and Built to Last with exact-item authority and lifecycle;
keep Measured Cuts and Artisan's Counter unavailable.

**Named acceptance:** `AT-REFINED-REAL-VS-EFFECTIVE`, `AT-MASTERWORK-ISSUE`, `AT-BUILT-TO-LAST`,
`AT-ITEM-UPGRADE-PRESERVE`, `AT-ITEM-TRANSFER`, `AT-ITEM-TAMPER-DEGRADE`,
`AT-CRAFTING-UNAVAILABLE`.

### Tracer 7 — Archer

**Goal:** execute Practice Range, Field Fletching I, and Fletcher's Habit; keep Steady Aim and Bowyer's Lore
unavailable.

**Named acceptance:** `AT-PRACTICE-RANGE`, `AT-PRACTICE-ARROW-DAMAGE`, `AT-TARGET-RETURN`,
`AT-FIELD-FLETCHING`, `AT-FLETCHER-HIT-LIFECYCLE`, `AT-FLETCHER-NO-DUP`,
`AT-ARCHER-UNAVAILABLE`.

### Tracer 8 — Warrior

**Goal:** execute T.W.I.G. Training, Ready Hands, and Weapon Discipline; keep Shrug It Off I and Heavy Hands
unavailable.

**Named acceptance:** `AT-TWIG-LOCAL`, `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS`,
`AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE`, `AT-WARRIOR-UNAVAILABLE`.

### Tracer 9 — Revocation, cross-Tree suite, recovery, and remote-shaped command

**Goal:** exercise all branches over at least three preconfigured test Homesteads, Tree revocation/replacement
Facet Credit, Attunement/Bond release/rejoin, relog/restart, explicit incompatible-test-data reset, and one
non-proximate selection through the shared command seam.

**Named acceptance:** `AT-REVOKE-ATOMIC`, `AT-REVOKE-NO-BP-REFUND`, `AT-FACET-CREDIT`,
`AT-REPLACEMENT-NO-AUTOBUY`, `AT-DURABLE-OUTCOMES-SURVIVE`, `AT-CROSS-TREE-13`,
`AT-UNAVAILABLE-7`, `AT-REMOTE-SHAPED`, `AT-LOCAL-EVIDENCE-NOT-REMOTE`,
`AT-UNRELEASED-DATA-RESET`, `AT-RESTART-SUITE`.

## Dependency order

```text
Gate A
  └── Tracer 1
        └── Tracer 2
              └── Tracer 3
                    └── Tracer 4
                          ├── Tracer 5 Cooking
                          ├── Tracer 6 Crafting
                          ├── Tracer 7 Archer
                          └── Tracer 8 Warrior
                                all four ──┐
                                            └── Tracer 9
```

Tracers 5–8 may proceed independently only after Tracer 4's shared grammar is accepted. Each still receives a
separate implementation verifier and cannot call logs-only evidence playable.

## Testing strategy

### Highest seams

1. **Pure domain tests:** aggregate transitions, content evaluation, Tier Access, relationship release, revocation,
   idempotency decisions, invariant scans.
2. **Contract tests:** command envelopes, authorization, revisions, rejection codes, receipt deltas, query shape.
3. **Persistence/recovery tests:** current schema, restart, process-kill at every write phase, receipt
   reconciliation, explicit incompatible-test-data reset, projection rebuild.
4. **Adapter tests:** exact Valheim event attribution and provider boundaries without duplicating domain policy.
5. **Joined-client/in-world tests:** one smallest real proof for each of all 13 executable nodes, covering its
   networking, UI visibility, item/projectile lifecycle, timing, area presence, or other visible behavior. A
   single representative proof for a multi-node Tree tracer is insufficient.

### Test rules

- Write failing tests before each implementation tracer.
- Use deterministic seeds/chances/factors in tests while keeping production values configurable.
- Exercise success, every stable rejection, replay, conflicting replay, stale revision, concurrent race, hostile
  principal, relog, restart, relationship loss, and current-build content mismatch as applicable.
- Verify exact persisted provenance, not only final balances.
- Keep proof fixtures disposable and explicitly preconfigured at Stone Level 2.
- Stop any AFK/live client after evidence and verify the process is gone.

## Runtime conformance and observability

Before implementation completion, choose and document the progression equivalent of `SpecCheck`:

- expected registry version;
- exact Facet/Tree/node counts and executable/unavailable status;
- required command handlers/providers;
- stable IDs and current-build definition identity;
- startup invariant/receipt-recovery report.

Diagnostics must be config-gated, throttled where periodic, and identify the operation/Stone/character/rejection
without logging secrets or raw PII. A green startup manifest proves registration/shape, not gameplay.

## Documentation and implementation synchronization

Every tracer's code PR also updates:

- this package when implementation resolves a technical choice or exposes a contradiction;
- exact accepted/rejected status and proof evidence;
- runtime conformance manifest;
- any dataset/recipe/piece documentation affected by real content;
- README/index manifests if artifacts move.

Implementation must not silently promote provisional values into compatibility guarantees. Final lock waits for
the pre-release playtest.

## Analyze and tasks passed; implementation separately gated

A separate verifier must check:

1. every confirmed Niflheim ticket-0011/0012 decision is represented once without stale superseded mechanics;
2. spec, data model, contracts, research, and plan agree on ownership, counts, levels, ledgers, revocation, and
   exclusions;
3. Gate A blocks later mutations and is not mislabeled optional setup;
4. all links/frontmatter/tables pass repository lint and structural validation;
5. the diff contains documentation/index changes only for this package;
6. no raw Spec Kit layout/CLI artifacts were introduced.

The first independent verifier found six stale consequences; all were corrected, and a second fresh-context
review returned PASS. Daniel then approved the corrected package and later separately authorized task authoring.
The resulting [`homestead-stone-progression-tasks.md`](homestead-stone-progression-tasks.md) passed a fresh-context
audit with no blockers and was accepted by merging PR #297. No Kanban card or runtime implementation was created.
The task list is an execution map, not implementation authorization.

## Complexity tracking

None. The multiple aggregates are required by authoritative ownership and non-atomic persistence boundaries,
not speculative layering. The plan explicitly avoids a shared framework extraction, finished UI, generic quest
engine, and every non-Homestead family.
