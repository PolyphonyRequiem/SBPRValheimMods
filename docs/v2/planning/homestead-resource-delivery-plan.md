---
title: "Homestead loyalty, upkeep, and Resource Delivery — implementation plan"
status: proposed
purpose: Plan dependency-ordered technical tracers and verification gates; no tasks or implementation are authorized.
---

# Implementation Plan: Homestead loyalty, upkeep, and Resource Delivery

**Branch:** `spec/homestead-resource-delivery`
**Date:** 2026-07-16
**Spec:** [`homestead-resource-delivery-spec.md`](homestead-resource-delivery-spec.md)

> **Stop gate:** This package is approval-gated documentation. Even after independent verification and Daniel's
> approval, task decomposition and runtime implementation each require separate authorization. No tasks artifact is
> part of this PR.

## Summary

Deepen the existing Homestead progression command/receipt/read-model architecture with one account-pair loyalty
Connection, per-account Stone participation, exact offline contribution reconciliation, multiplier-aware AP awards,
Stone-owned Resource Delivery nodes, one composed delivery meter, one virtual Stone Stockpile, and explicit stock
withdrawal authority.

The work is organized as vertical tracers around one application/receipt seam. Gate A proves deterministic elapsed
reconciliation and cross-aggregate convergence before item movement or gameplay content. The first complete content
fixture is the Stone-Level-2 Humble Homesteader's Bundle.

## Technical context

- **Runtime:** C# net48 BepInEx/HarmonyX plugin; engine-free domain/application seams link into the current test project.
- **Existing substrate:** authenticated account/character resolution, stable Stone identity, Bond/Attunement,
  revisioned commands, append-only receipt journal, AP projections, content registry, and Stone read model.
- **Storage:** existing receipt journal remains transaction authority; new Connection, Participation, Outcome, and
  Stock projections require a proven recoverable fan-out before gameplay acknowledgement.
- **Time:** server-authoritative wall time for offline intervals, monotonic process time where available, explicit
  backward-clock anomaly handling, no client time.
- **Inventory:** exact server-observed debit/credit and capacity fit; no client-authoritative vectors.
- **Performance:** event/load/checkpoint reconciliation; no per-frame world scans or one timer per player/node.
- **Scale:** current Homestead proof scale; clustered high-population optimization and final capacity values are deferred.

## Constitution check

| Article | Plan response | Status |
|---|---|---|
| Spec-first | Proposed package precedes behavior; implementation must reconcile affected accepted docs/code/tests together | PASS |
| Runtime conformance | New outcome/content/contract shapes require conformance checks before implementation completion | REQUIRED |
| Corpus-first | No new vanilla content claim beyond user-authored Wood/Stone fixture; item/runtime seams re-verify live symbols during implementation | PASS |
| Clean-room | Uses existing SBPR and vanilla seams only | PASS |
| Writer ≠ verifier | Fresh separate verifier must PASS current package before approval-ready publication | REQUIRED GATE |
| Daniel controls landing | PR remains open; no auto-merge, tasks, or implementation | PASS |
| Incremental delivery | Gate A + five tracers with exact named acceptance | PASS |
| Semver docs tree | Five proposed artifacts plus README/index entries under `docs/v2/planning/` | PASS |
| ADR-0005 adaptation | No `specify` CLI, `.specify/`, `specs/`, or tasks artifact | PASS |

No constitutional violation is requested.

## Package structure

```text
homestead-resource-delivery-spec.md
homestead-resource-delivery-research.md
homestead-resource-delivery-data-model.md
homestead-resource-delivery-contracts.md
homestead-resource-delivery-plan.md
```

The accepted Homestead progression package remains the substrate. This package carries explicit proposed-supersession
boundaries; it does not overwrite accepted truth before approval/implementation.

## Architecture decisions

### A1 — One command/receipt architecture

Relationship-source transitions, participation receipts, AP awards, node BP development, Stock deposits, generated
delivery, permissions, and withdrawals all use the existing authenticated/revisioned/idempotent application boundary.
There is no independent Resource Delivery database API or adapter-to-ledger write.

### A2 — Pure elapsed reconciler

One engine-free deterministic reconciler integrates contribution over server-time intervals split at every relevant
boundary. It receives snapshots and returns transitions/deltas; it does not read Unity state or persist directly.

### A3 — Explicit state ownership

- Connection aggregate owns account-pair source set, age, and grace.
- Account–Stone Participation owns weekly/daily completion and expiry.
- Character aggregate owns Personal/Cumulative AP and BP.
- Stone outcome state owns menu, active Resource Delivery nodes, delivery cursor, and pending bundle.
- Stone Stockpile owns item counts, capacity, and delegated withdrawal permissions.
- Receipt store owns operation/result/recovery truth.

### A4 — Data owns tuning

Content versions own age bands, grace duration, objective definitions, donation pools/defaults, node BP prices, bundle
contents, thresholds, capacity policy, and multiplier policy. Code owns identity, authority, atomicity, replay,
no-stacking, and lifecycle invariants. First-slice authority is exact domain policy: owner-role Bond selects menu and
manages delegation; any active Bond develops Foundational Humble or withdraws; committed-Tree development retains
Responsibility Range. Expected revisions serialize multi-Bond races.

### A5 — Snapshot at irreversible boundaries

AP receipts snapshot multiplier inputs/result. Delivery completion snapshots exact bundle/source versions. Donations and
withdrawals snapshot exact item vectors. Later content, relationship, or Tree changes never reinterpret terminal results.

### A6 — One Stockpile, no physical authority

Donations and generated delivery share one virtual Stone Stockpile. World containers may become presentation later but
cannot become source of truth, overflow path, or recovery mechanism.

## High test seams

The feature should need only these high seams:

1. existing relationship command lifecycle, extended with Connection projections;
2. one server-observed objective-progress/completion adapter;
3. one pure contribution reconciler behind application commands;
4. one Stone Stockpile command family using the server inventory transaction adapter;
5. existing Stone progression read model, extended.

New lower-level seams require evidence that none of these can own the behavior safely.

## Delivery order

### M0 — Current-truth and conformance guard

**Goal:** encode the proposed supersession boundary and prevent accidental implementation against Mirrored AP or Local
beneficiary semantics.

**Work shape:**

- register proposed content/contract shapes in docs and future conformance design;
- preserve current AP source authorization and require first-slice Mirrored telemetry equal to actual floored award until a separate removal PR;
- confirm docs/task/code boundaries before runtime work.

**Named acceptance:** `AT-RD-023`, `AT-RD-024`.

**Exit:** current truth and proposed authority are mechanically distinguishable; no task/runtime files exist in the
specification PR.

### Gate A — Deterministic time and recoverable fan-out

**Goal:** prove the two load-bearing mechanisms before item or gameplay content: exact offline interval reconciliation
and one relationship/AP/outcome mutation converging across several projections.

**Work shape:**

- canonical Connection identity and exact maturity arithmetic;
- canonical ordered multi-Connection two-operation handshake: durable non-gameplay preparation decision bound to preparing principal/target authority, fresh-ID same-principal confirmation, preview-only tier/age, and confirmation-time 72-hour grace;
- pure reconcile-before-mutation boundary integrator with renewal, same-time ordering, multiple cycles, and residual progress;
- complete contributor iff-rule and strongest-link-once selection;
- process-death/retry harness over Connection, Participation, AP, and Stone outcome projections.

**Named acceptance:** `AT-RD-001`, `AT-RD-003`, `AT-RD-004`, `AT-RD-005`, `AT-RD-007`.

**Exit:** preparation replay returns the exact principal/target-bound challenge after restart; token-bearing principal
substitution and lost authority reject; authorized fresh-ID delayed confirmation preserves confirmation-time age, starts
a full 72-hour grace, and atomically couples consumption+release+receipt across crashes/competing confirmations. Online
partitions and offline jump are terminally identical across renewal/multiple-cycle/dormancy boundaries, and relationship
fan-out converges to one complete result. Full cross-feature `AT-RD-022` remains open until every later mutation family exists.

### Tracer 1 — Qualifying loyalty sources

**Goal:** derive only Bonded↔Attuned and Bonded↔Bonded account-pair sources from real relationship lifecycle.

**Work shape:**

- integrate source add/remove with Bond/Attunement command receipts;
- reject Attuned↔Attuned and all social/transitive edges;
- maintain one Connection across several Stones/sources.

**Named acceptance:** `AT-RD-002`.

**Exit:** real relationship create/release/reconnect drives exact Connection source state without introducing a social graph.

### Tracer 2 — Weekly/daily participation and AP multiplier

**Goal:** prove authenticated objective completion, `0×/1×/2×` participation, multiplier-aware whole AP awards, and
unmultiplied BP.

**Work shape:**

- Participation aggregate, reconcile-before-refresh, rolling seven-day weekly expiry, and rolling 24-hour daily boundaries;
- exact first daily fixture: durable per-account/Stone cycle counts five distinct physical eligible Foundational placements, closes for 24 hours, then resets after expiry reconciliation;
- combined placement receipt fixes AP-before-practice order: the fifth placement uses the prior tier and only later work sees `2×`;
- AP receipt snapshot of otherwise-authorized source, base/tier/maturity/final floor;
- replay after participation/maturity changes without widening AP source authority.

**Named acceptance:** `AT-RD-006`, `AT-RD-009`, `AT-RD-019`.

**Exit:** one participant moves through paused/normal/double states using the exact daily fixture; AP and Cumulative
awards are exact, source-authorized, replay-safe, and prove the fifth placement at the prior tier followed by `2×` on
the next event.

### Gate B — Server inventory transaction (blocks Tracer 3)

**Goal:** prove exact player-inventory debit/credit plus Stone Stock mutation can produce one recoverable terminal result.

**Spike work:** exact authored vector resolution, full-fit validation, stale revision/replay, disconnect, and process
death at every debit/deposit/credit boundary. This gate uses a disposable Stock harness; it does not claim product
acceptance before the Tracer commands exist.

**Exit:** same operation converges to one transfer or no transfer with no duplicated/lost player or Stone items.
Tracer 3 and Tracer 5 item-moving commands may not start before this exit passes.

### Tracer 3 — Donation menu and Stone Stock

**Goal:** select two curated upkeep options, atomically donate one option into one durable Stockpile, and prove delegated
withdrawal authority lifecycle.

**Work shape:**

- exact Level-2 pool (`20 Wood`, `20 Stone`, `10 Wood + 10 Stone`), default pair (`20 Wood`, `20 Stone`), and owner-role Bond selection authority;
- server inventory debit plus Stock deposit under one receipt;
- Stock capacity policy and invariant scan;
- one canonical permission record per grantee; owner-role grant/revoke, duplicate/stale-race rejection, revocation/regrant generations, no expiry.

**Named acceptance:** `AT-RD-013`, `AT-RD-014`, `AT-RD-017`, `AT-RD-018`.

**Exit:** valid donation transfers once; every invalid/full/stale/replayed path preserves both inventories; delegation is
explicit and non-transitive.

### Gate C — Capacity, pending priority, and dormancy (blocks Tracer 4)

**Goal:** prove one deterministic capacity policy before generated resources can enter Stock.

**Spike work:** fully fitting and non-fitting bundles; immutable pending snapshot; pending-first retry after withdrawal;
blocked later donation; multiple cycles/residual progress; empty-bundle dormancy; progress freeze/resume.

**Exit:** pending content remains exact, priority/order is deterministic, no paused time is banked, and sibling outcomes
continue. Tracer 4 may not start before this exit passes.

### Tracer 4 — Resource Delivery and Humble Homesteader

**Goal:** incrementally BP-develop Resource Delivery nodes in the supported Tree classes, preserve committed-Tree
investment semantics, compose one bundle, advance all outcomes, and deposit/pause/resume the Humble fixture correctly.

**Work shape:**

- explicit Resource Delivery outcome/ownership plus first-slice conformance `21 = 14 executable + 7 unavailable`;
- incremental committed-Tree BP delta + equal cumulative investment, and Foundational 1-BP/no-Tree-investment branch;
- one composed meter and immutable completion snapshot;
- 10 Wood + 10 Stone / 24 baseline contribution-hour fixture;
- pending-capacity priority plus progress freeze/resume and empty-bundle lifecycle behavior;
- committed-Tree revoke deletes Resource Delivery development with no refund; recommit starts fresh; Foundational unaffected.

**Named acceptance:** `AT-RD-008`, `AT-RD-010`, `AT-RD-011`, `AT-RD-012`, `AT-RD-015`, `AT-RD-020`.

**Exit:** the joined runtime completes and deposits one exact Humble bundle offline; a full Stockpile preserves it pending
while sibling outcomes continue.

### Tracer 5 — Withdrawal, read model, and in-world evidence

**Goal:** expose actionable state and complete Bond/delegated withdrawals with real inventory fit and joined-client proof.

**Work shape:**

- atomic requested-vector withdrawal;
- post-withdraw pending-delivery release;
- privacy-filtered Stone view and operator diagnostics;
- joined-client evidence for donation, offline completion, deposit, Bond withdrawal, delegated withdrawal, and capacity rejection.

**Named acceptance:** `AT-RD-016`, `AT-RD-021`, `AT-RD-022`.

**Exit:** a player can understand and complete the loop in-game; every relationship, participation, AP, donation,
delivery, permission, and withdrawal mutation family has passed its process-death/replay matrix, closing `AT-RD-022`.
Recovery and logs support but do not substitute for playability evidence.

## Testing strategy

### Automated

- Pure boundary tables cover every age, weekly, daily, grace, threshold, and capacity edge.
- Property-style interval partition tests compare one large offline jump with arbitrary smaller online partitions,
  including expiry→renewal, same-time receipt ordering, multiple cycles, residual progress, and dormancy freeze/resume.
- Domain tests cover qualifying-source fan-out/challenge sets, complete contributor iff-rule, no-stacking, exact menu/
  daily fixtures, bundle composition, capacity priority, and lifecycle.
- Contract tests cover every command result/rejection, stale revision, same/conflicting replay, and authority boundary.
- Process-kill tests cover each terminal mutation's durable boundaries.
- Read tests cover privacy filtering and exact actionable rejection vocabulary.

### Joined-client / in-world

At minimum capture:

1. weekly donation from a real player inventory into Stone Stock;
2. current participation and multiplier shown accurately;
3. client shutdown/offline interval/server restart followed by exact reconciliation;
4. Humble delivery of exactly 10 Wood + 10 Stone;
5. Bond withdrawal and delegated withdrawal;
6. Stock-full donation rejection and generated-delivery pending/resume;
7. final-link warning, grace, reconnect, and expiry behavior.

### Regression

- Existing Foundational placement, AP replay, relationship lifecycle, node ownership, Local Effect, BP, and restart suites remain green.
- Resource Delivery never reads Mirrored AP.
- Attuned AP purchase of Resource Delivery rejects.
- Friends/Party/Guild/co-presence never create Connection sources.
- No world chest or client clock becomes authoritative.

## Mechanical package checks

Before approval-ready publication:

- requirement IDs `RD-001..RD-024` contiguous and unique;
- exactly one requirement→acceptance row per requirement;
- normative acceptance set exactly equals this plan's named acceptance set;
- all five artifacts carry `status: proposed` frontmatter and are registered in README/index;
- no tasks artifact, code, config, secrets, or implementation card;
- relative links and markdown tables valid;
- placeholder/superseded-term scan clean;
- docs lint and `git diff --check` pass;
- fresh writer≠verifier PASS against current branch after any corrections.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Clock changes create free/lost production | server-only time, exact cursors, negative clamp+alert, partition tests |
| Relationship command acknowledges before Connection update | Gate-A cross-aggregate process-kill proof |
| Objective system expands into a second project | narrow completion evidence contract; final quest UI/content deferred |
| Stock transaction crosses non-atomic game inventory | inventory transaction spike and no acknowledgement before recoverable terminal result |
| Multi-Governor/Stone fan-out explodes contribution | one account once, strongest multiplier once, bounded current source sets |
| New node class corrupts Local/personal rules | explicit outcome/ownership/acquisition type and conformance tests |
| Mirrored AP refactor destabilizes Gate A | retain compatibility telemetry first; separate removal later |
| Spec approval mistaken for build approval | explicit stop gates; no tasks/runtime files/cards |

## Stop and handoff

This plan stops after an independently verified, approval-ready documentation package and open PR. Human product-owner
approval is the next gate. Task decomposition and implementation remain absent and separately unauthorized.
