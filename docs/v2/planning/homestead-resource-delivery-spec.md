---
title: "Homestead loyalty, upkeep, and Resource Delivery — feature specification"
status: proposed
purpose: Normative product and acceptance contract for Stone loyalty Connections, participation multipliers, Stone Stock, and Resource Delivery.
---

# Feature Specification: Homestead loyalty, upkeep, and Resource Delivery

**Branch:** `spec/homestead-resource-delivery`
**Created:** 2026-07-16
**Status:** S1 Design → proposed S2 Spec; independently verified package required before Daniel approval
**Input:** Daniel-approved Resource Delivery decision synthesis and ubiquitous language from the `Core Stone — Mirrored AP Resource Yields` design thread.

> **Maturity:** This package specifies a buildable technical slice and playtest hypothesis. It is not final economy
> tuning, final quest content, production migration compatibility, task authorization, or implementation authorization.
> It extends the accepted Homestead progression package rather than replacing its identity, relationship, receipt,
> Tree, and authority substrate.

## Proposed supersession boundary

Until this package is approved and implemented, the accepted Homestead progression docs and current runtime remain
current truth. This proposal intentionally changes or extends five areas:

1. Otherwise-authorized Personal AP awards gain a per-account participation/Connection multiplier before whole-number flooring; the multiplier does not create a new AP source or actor authorization.
2. Resource Delivery is a new Stone-owned node outcome that may be authored in Foundational, Profession, or Martial Trees.
3. Stone-wide outcomes advance directly from eligible-account contribution; Mirrored Stone AP is not their currency,
   threshold, or source.
4. The Stone gains one virtual Stockpile receiving both upkeep donations and generated deliveries.
5. Account-level state expands beyond authority/grouping/audit to include Connection maturity and per-Stone
   participation eligibility. It still owns no AP/BP wallet, node purchase, or Stone outcome; character and Stone
   ownership remain unchanged.

A later behavior PR MUST reconcile this package, every affected accepted Homestead progression authority, code, tests,
and runtime conformance together. This docs-only proposal does not alter shipped behavior.

### Package-closed choices awaiting Daniel's package approval

The design conversation directly accepted the loyalty links, maturity bands/grace, `0×/1×/2×` participation, parallel
outcomes, AP multiplier, any-Tree Bonded-BP Resource Delivery, Humble 10 Wood + 10 Stone / 24-hour fixture, shared
Stone Stock, and active-Bond/delegated withdrawal. To make the package buildable without another interview, this spec
also proposes:

- world/product-scoped Connection identity across qualifying Stones;
- final-link confirmation is bound to the preparing Account/Character and target relationship; only that same currently
  authorized relationship holder may confirm, and token possession grants no authority;
- reconcile-before-mutation effective-time semantics;
- weekly upkeep uses a rolling seven-day expiry from accepted donation completion rather than a calendar reset;
- one Stone-wide menu whose owner-role Bond selects two options, with a deterministic default pair;
- first Level-2 donation pool: `20 Wood`, `20 Stone`, or `10 Wood + 10 Stone`; default pair = `20 Wood` and `20 Stone`;
- first daily-practice fixture: five distinct, server-observed eligible Foundational placements in the Stone Area;
- any active Bond may develop the Foundational Humble node; its provisional cost is 1 BP;
- active-Bond withdrawal plus owner-role-Bond grant/revoke delegation using one canonical grantee record; no expiry in slice;
- accepted committed-Tree revoke behavior (delete node development, no BP refund, recommit starts fresh) applies to Resource Delivery;
- partial requested Stock quantities with all-or-nothing fulfillment;
- provisional Level-2 Stock capacity `16 item kinds / 1,000 total / 500 per item`;
- retained Mirrored AP compatibility telemetry until a separate removal refactor.

These are proposed S2 fixture choices, not claims that Daniel stated them verbatim. Package approval ratifies them for
this technical slice; later playtest tuning remains data.

## Problem Statement

Homestead progression currently proves authenticated relationships, AP/BP ledgers, Tree development, node outcomes,
and crash-safe receipts, but it does not answer how sustained player loyalty produces shared community or owner
benefits. Mirrored Stone AP accumulates without a gameplay consumer. A naive AP-to-item conversion would add an
opaque intermediary currency, reward AFK behavior, and blur personal progression with Stone-owned outcomes.

Players need a legible loop in which real Stone relationships mature, meaningful upkeep keeps participation active,
and all applicable Stone outcomes advance together. Governors need authored choices over acceptable upkeep without
being able to demand arbitrary resources. Stone-owned donations and generated resources need one authoritative,
capacity-bounded store with safe withdrawal, replay, restart, and overflow behavior.

## Solution

Introduce one world/product-scoped account-pair **Connection** that matures while the accounts share a qualifying
Bonded↔Attuned or Bonded↔Bonded Stone link. Each related account maintains per-Stone weekly upkeep and optional daily
practice. Participation (`0× / 1× / 2×`) combines with the strongest applicable Connection maturity (`1.0×–1.5×`)
to produce one effective multiplier, capped by the authored bands at `3×`.

The multiplier advances every applicable Stone outcome in parallel and multiplies that account's otherwise-authorized
Personal AP award; it does not create a new AP source. Resource Delivery is one Stone-wide outcome composed from
Bonded-BP-developed Resource Delivery nodes in any Tree.
Completion atomically deposits the authored bundle into a virtual Stone Stockpile. Upkeep donations enter the same
Stockpile. Active Bonds and explicit delegated withdrawal permissions may withdraw Stone Stock.

The first fixture is the Stone-Level-2 Foundational **Humble Homesteader's Bundle**: a 1-BP active-Bond development
that adds 10 Wood and 10 Stone to each completed delivery at a 24 baseline-contribution-hour threshold.

## User Stories

1. As a Bonded owner, I want loyal participation to produce Stone-owned benefits, so that maintaining a real community has visible value.
2. As an Attuned participant, I want my Stone relationship to mature across continued shared stakes, so that long-lived loyalty is rewarded.
3. As either side of a qualifying relationship, I want my Connection age to survive ordinary character changes, so that maturity belongs to our accounts rather than one avatar.
4. As a player removing a final qualifying link, I want a clear warning and reconnection grace, so that an accidental release does not silently destroy months of maturity.
5. As a returning player, I want reconnecting within 72 hours to preserve my attained tier, so that brief relationship repairs are forgiving.
6. As a participant, I want one weekly Stone upkeep obligation, so that normal contribution requires meaningful current investment.
7. As an engaged participant, I want daily practice to double my contribution temporarily, so that active play matters without streak escalation.
8. As an offline player with current participation, I want eligible contribution to continue while away, so that the system rewards relationships rather than AFK clients.
9. As a player with several qualifying links at one Stone, I want only my strongest Connection multiplier applied once, so that multi-Governor Stones do not multiply me unfairly.
10. As a player earning AP, I want the same loyalty multiplier to improve my Personal AP award, so that paying upkeep also advances my own node choices.
11. As a Governor, I want all applicable Stone outcomes to advance together, so that I do not manage a production-allocation queue.
12. As a Bonded player, I want to buy Resource Delivery nodes with BP in any eligible Tree, so that different Homestead identities can author useful bundles.
13. As an Attuned player, I want Resource Delivery nodes to remain Stone-owned rather than personal AP purchases, so that contribution and ownership stay distinct.
14. As a Bonded player, I want new Resource Delivery nodes to enrich future bundles without slowing delivery, so that purchasing an unlock is never a trap.
15. As a Stone participant, I want upkeep donations retained as Stone Stock, so that the weekly obligation transfers useful value into the community.
16. As an authorized selector, I want to choose a small menu from authored level-appropriate donations, so that the Stone can shape upkeep without arbitrary demands.
17. As an active Bond, I want to withdraw Stone Stock, so that Stone-owned donations and deliveries can be used for Homestead purposes.
18. As a delegated helper, I want explicit revocable withdrawal permission, so that a Governor can authorize collection without granting a Bond.
19. As a donor or claimant, I want deposits and withdrawals to be atomic, so that capacity, retries, and inventory overflow never destroy or duplicate items.
20. As an operator, I want durable receipts and bounded audit views, so that restart, replay, and abuse reports are diagnosable without raw-identity leakage.
21. As a content author, I want donation recipes, objectives, BP prices, bundles, thresholds, capacity, and multipliers in versioned data, so that tuning does not require policy rewrites.
22. As a player, I want the Humble Homesteader's Bundle to be predictable, so that its 10 Wood + 10 Stone delivery can be planned around.

## Implementation Decisions

- **One loyalty aggregate:** Connection is one unordered, world/product-scoped account pair with one accumulated age, lifecycle state, revision, and provenance. Several Stone-derived qualifying links may keep it active.
- **Stone stakes only:** Bonded↔Attuned and Bonded↔Bonded links qualify. Attuned↔Attuned, Friendship, Party, Guild, Discord, co-presence, suggestions, and transitive links do not.
- **Frozen grace:** final-link removal enters a 72-hour grace. Maturity does not advance during grace; reconnection resumes preserved age; expiry resets it. A release affecting several account-pair Connections uses a two-operation handshake: the preparation operation terminates with one durable non-gameplay decision/challenge covering the complete ordered affected set, preview tiers/ages, authenticated preparing AccountId/CharacterId, target RelationshipId, and release-authority revision. Confirmation uses a fresh operation ID, requires that exact principal to remain the active authorized relationship holder, and grants no authority from token possession. It revalidates the set/revisions, reconciles age through confirmation time, freezes that authoritative age, and sets expiry exactly 72 hours later. Challenge consumption, relationship release, all Connection transitions, and the confirmation receipt commit together, so replay/restart cannot consume without release or release twice.
- **Per-account contribution:** an account contributes if and only if it has an active Bond or Attunement to the Stone, at least one qualifying Connection at that Stone, and nonzero current participation. It contributes at most once using the strongest applicable maturity multiplier. A solo Bond contributes nothing; in Bonded↔Attuned and Bonded↔Bonded pairs both currently participating sides contribute.
- **Two participation gates:** an accepted weekly donation starts/refreshes a rolling seven-day upkeep expiry. Current weekly upkeep enables `1×`; current weekly upkeep plus daily practice completed within its rolling 24-hour window enables `2×`; missing/expired weekly upkeep means `0×`.
- **Offline reconciliation:** outcome progress is derived from elapsed intervals and durable timestamps/receipts. Before any mutation changes eligibility, rate, bundle composition, dormancy, or capacity, reconcile `[lastCursor, receivedServerTime)` under the old state, then apply the mutation at that time. Same-time operations use durable receipt order. Threshold excess carries into additional cycles/residual progress while capacity allows; once pending capacity blocks delivery, later delivery time is not banked. No per-frame or always-running production timer is authoritative.
- **Exact outcome math:** outcome progress uses exact fixed-point/rational arithmetic over interval boundaries. Personal AP computes the full multiplier and floors once to a whole award. BP is not multiplied.
- **Parallel outcomes:** every applicable Stone outcome consumes the same contribution stream independently; there is no shared spend, active-project selector, or allocation queue.
- **Stone-owned node family and roster:** Resource Delivery is a Stone-owned outcome. Bonded actors acquire it through the accepted incremental `ApplyBPToNode` grammar. Profession/Martial BP deltas also increase cumulative owning-Tree investment and may cross Tree Level; Foundational BP deltas increase only Foundational node progress. Completion activates the node. Humble costs 1 BP. Appending Humble changes the first-slice roster from `20 = 13 executable + 7 unavailable` to `21 = 14 executable + 7 unavailable`; manifests/read models/conformance must move together.
- **Any-Tree authoring:** Resource Delivery nodes may belong to Foundational, Profession, or Martial Trees. Committed Trees use ordinary development authority and Responsibility Range. Foundational uses any active Bond, no Facet/Responsibility Range, positive BP increments capped at remaining authored cost, and no cumulative committed-Tree investment.
- **One composed delivery:** one Stone-wide Resource Delivery meter snapshots the bundle composed from all currently active completed Resource Delivery nodes at completion.
- **Enrichment never slows:** bundle nodes change contents, not the base delivery threshold, unless an explicit speed effect or server tuning says otherwise.
- **One Stone Stockpile:** upkeep donations and generated delivery items become fungible Stone Stock after atomic deposit, with source provenance retained for audit. The provisional Stone-Level-2 capacity is 16 distinct item kinds, 1,000 total units, and 500 units per item; all three values are server-configurable playtest data.
- **Atomic capacity:** donations reject before player debit when the complete donation cannot fit. Generated delivery remains pending and pauses further delivery when the complete bundle cannot fit.
- **Withdrawal authority:** any active Bond or explicit delegated withdrawal permission may withdraw. Only the active Bond carrying the server-authored owner role may grant/revoke delegation; Responsibility Range does not apply. Permission changes use the canonical grantee record and expected revision, so concurrent owner-role Bonds serialize with one winner. Requests may name partial quantities, but each requested vector succeeds completely or not at all after inventory-fit validation.
- **Curated upkeep menu:** at a new Stone Level, the active Bond carrying the authored owner role selects two acceptable weekly donation options from a versioned pool containing at least two distinct valid options. If no owner-role Bond selects before upkeep is needed, the deterministic default pair materializes. The Level-2 Humble pool is `20 Wood`, `20 Stone`, and `10 Wood + 10 Stone`; default pair = `20 Wood` and `20 Stone`. A contributor completes either selected option. The preconfigured Level-2 fixture begins uninitialized and exercises this same selection/default transition; this package does not define Stone-level advancement.
- **Objective seam and first practice:** weekly upkeep and daily practice use authenticated, server-observed receipts. The first daily fixture has one durable cycle per account/Stone: while no daily completion is current, five distinct physical eligible Foundational catalog placements inside the Stone Area complete it; each instance counts once. Completion closes the cycle and starts the rolling 24-hour `2×` window. Events during that window do not pre-build the next cycle; after expiry/reconciliation, the next eligible event opens a fresh zero-progress cycle. For a placement that is also an AP source, one combined terminal operation evaluates otherwise-authorized AP against the pre-placement participation tier, then records practice progress; therefore the fifth placement uses the prior `0×/1×` tier and `2×` applies only to subsequent events and elapsed time. Quest/commission presentation is replaceable and outside the domain contract.
- **Lifecycle and capacity priority:** committed-Tree revocation preserves the accepted delete/no-BP-refund rule: reconcile to revocation time, delete that Tree's Resource Delivery development/acquisition, and require fresh development after recommitment. Foundational Humble is unaffected by committed-Tree revocation. Future bundle composition changes only; Stone-wide in-progress meter progress persists (freezing if the bundle becomes empty), while pending bundles and deposited Stock never mutate. Pending generated delivery has first claim on newly freed capacity: capacity-releasing withdrawal MUST attempt it idempotently before a later donation may consume that capacity.
- **Mirrored AP compatibility:** the first behavior slice MUST continue writing Mirrored Stone AP as receipt-compatible telemetry equal to the actual floored Personal/Cumulative AP award. Resource Delivery never reads it. Removal or conversion to a derived statistic is a separate reconciled refactor.

## Functional Requirements

- **RD-001:** The system MUST key Connection by canonical unordered `(World/Product, AccountA, AccountB)` identity and MUST reject self-pairs and unauthenticated subjects.
- **RD-002:** Only active Bonded↔Attuned or Bonded↔Bonded role pairings through at least one shared Stone MAY maintain a qualifying loyalty link; every social, indirect, or Attuned↔Attuned edge MUST NOT.
- **RD-003:** Connection maturity MUST use the exact age bands `<1d=1.0×`, `1–<7d=1.1×`, `7–<30d=1.2×`, `30–<60d=1.3×`, `60–<90d=1.4×`, and `≥90d=1.5×`.
- **RD-004:** Removing final qualifying links MUST warn before commit and MUST enter a 72-hour frozen grace for every affected Connection. The `ReleaseRelationship` preparation operation MUST terminate without gameplay mutation in a durable `ConfirmationRequired` decision that binds its operation/intent, authenticated preparing AccountId/CharacterId, target RelationshipId/release-authority revision, canonical ordered affected source set, relationship/Connection revisions, and grace-policy version while labelling tiers/ages as issue-time previews. `ConfirmReleaseRelationship` MUST use a fresh operation ID referencing that decision, require the same authenticated principal to remain the active holder with ordinary voluntary-release authority, and treat token possession as no authority. It MUST revalidate the binding, reconcile through confirmation server time, freeze that resulting age, and set expiry exactly 72 hours later. Challenge consumption, release, all transitions, and the successful confirmation receipt MUST commit atomically. Same-operation replay after restart MUST return the exact decision/receipt; principal substitution, lost authority, or a different confirmation operation MUST NOT consume/apply twice. Reconnect-before-expiry MUST preserve age; expiry MUST reset maturity.
- **RD-005:** An account MUST contribute if and only if it has an active Bond or Attunement to the Stone, at least one qualifying Connection at that Stone, and nonzero current participation. It MUST contribute at most once using its strongest applicable multiplier; solo Bond, stale/released relationship, grace-only Connection, or `0×` participation MUST contribute nothing, while both currently participating sides of Bonded↔Attuned or Bonded↔Bonded MUST contribute.
- **RD-006:** An accepted upkeep donation MUST start/refresh a rolling seven-day expiry. Participation MUST resolve per account and Stone as `0×` when weekly upkeep is expired/missing, `1×` while weekly upkeep is current, and `2×` while weekly upkeep is current plus daily practice is within its rolling 24-hour window; calendar resets, streaks, repeated completions, and stacked tiers MUST NOT add another multiplier.
- **RD-007:** Before any mutation changes contribution eligibility/rate, bundle composition, dormancy, or capacity, the system MUST reconcile `[lastCursor, receivedServerTime)` under prior state and then apply the mutation at that time; same-time operations MUST follow durable receipt order. Offline evaluation MUST equal arbitrary online partitions, process multiple delivery cycles while capacity permits, retain threshold excess/residual progress, and discard only Resource Delivery time elapsed after `PendingCapacity` begins.
- **RD-008:** Every applicable unlocked Stone outcome MUST advance in parallel from the same eligible-account contribution stream without an allocation choice or shared spendable production currency.
- **RD-009:** For an otherwise-authorized AP-producing event, Personal AP MUST equal `floor(authoredBaseAP × participationMultiplier × strongestMaturityMultiplier)`; the multiplier MUST NOT widen that AP source's accepted actor/relationship authorization. Cumulative AP Earned MUST record the award, BP MUST remain unmultiplied, replay MUST remain idempotent, and missing qualifying Connection/current weekly upkeep MUST yield no AP award.
- **RD-010:** Resource Delivery nodes MUST be Stone-owned outcomes authorable in Foundational, Profession, or Martial Trees and developed through positive BP increments capped at remaining cost. Profession/Martial increments MUST also add the same delta to owning-Tree cumulative investment and MAY cross Tree Level; Foundational increments MUST add no committed-Tree investment and require any active Bond. Attuned Personal AP purchase MUST reject. Appending Humble MUST change first-slice conformance to `21 authored = 14 executable + 7 unavailable`.
- **RD-011:** One Stone-wide Resource Delivery meter MUST compose future bundle contents from active purchased nodes; adding a bundle node MUST NOT increase its threshold, and completion MUST snapshot exact bundle/content versions.
- **RD-012:** The first fixture MUST author the Stone-Level-2 Foundational Humble Homesteader's Bundle as a 1-BP active-Bond development producing 10 Wood + 10 Stone at a 24 baseline-contribution-hour threshold.
- **RD-013:** One virtual, server-authoritative Stone Stockpile MUST own both accepted donations and generated deliveries with version, revision, capacity, and source provenance; no world chest may be authoritative. The provisional Stone-Level-2 defaults MUST be 16 distinct item kinds, 1,000 total units, and 500 units per item, each server-configurable.
- **RD-014:** Donation deposit MUST validate authored option, current upkeep window, authenticated actor, exact item identities/quantities, revisions, and full capacity before atomically removing player items and adding Stone Stock once.
- **RD-015:** Generated delivery MUST deposit the full snapshotted bundle atomically or remain immutable and pending without loss. Pending delivery MUST have first claim on newly freed capacity; a capacity-releasing withdrawal MUST attempt it idempotently before later donations, and while pending only Resource Delivery pauses while other outcomes continue.
- **RD-016:** Stock withdrawal MUST revalidate active Bond or delegated withdrawal authority, requested quantities, Stockpile revision, and full player-inventory fit, then atomically debit Stock and credit the player or change nothing.
- **RD-017:** Delegated withdrawal permission MUST have one canonical current record per `(StoneId, grantee AccountId)`, with generation/revision history and at most one active grant. Only the active Bond carrying the server-authored owner role MAY grant/revoke without Responsibility Range. Regrant after revocation MUST increment generation; active duplicate/different-payload grants and concurrent stale changes MUST reject. Revocation MUST remove all current delegated authority for that grantee. Attunement, groups, Friendship, or another player's grant MUST NOT imply authority; expiry is not part of this slice.
- **RD-018:** Each Stone Level MUST expose a versioned authored pool containing at least two distinct valid donation options. Only the active Bond carrying the authored owner role MAY select two stable options; if no such selection exists when upkeep is needed, the authored default pair MUST materialize once. One completed selected option MUST satisfy weekly upkeep. The Level-2 Humble pool MUST be `20 Wood`, `20 Stone`, and `10 Wood + 10 Stone`, with `20 Wood` + `20 Stone` as the default pair.
- **RD-019:** Weekly upkeep and daily practice MUST enter through authenticated server-observed evidence with operation identity, objective definition/version, Stone, actor, server time, and replay-safe receipt; raw client progress claims MUST NOT award participation. The first daily fixture MUST maintain one durable cycle per account/Stone, count five distinct physical eligible Foundational placements inside the Stone Area, complete/close the cycle at five, start a rolling 24-hour expiry, ignore progress toward another cycle while current, and open a fresh zero-progress cycle only after expiry is reconciled. If the same placement is an AP source, one combined terminal operation MUST snapshot otherwise-authorized AP from the pre-practice participation state before applying practice progress; the fifth placement MUST use the prior tier and `2×` MUST begin only after that event.
- **RD-020:** Before committed-Tree revocation, the system MUST reconcile to revocation time, then preserve the accepted delete/no-BP-refund behavior for that Tree's Resource Delivery development; recommitment MUST start undeveloped and require fresh BP. Foundational Resource Delivery is unaffected by committed-Tree revocation. Future composition MUST exclude deleted/dormant nodes. Stone-wide in-progress meter progress MUST persist and freeze if no non-empty bundle remains; reactivation/new development MUST resume it, while pending bundle contents and deposited Stock/provenance remain immutable.
- **RD-021:** One Stone-identity read model MUST expose Connection tier/grace without other-account raw identity, participation status/expiry, effective multiplier, donation menu/progress, Resource Delivery progress/pending state, Stock counts/capacity, and actionable rejection reasons.
- **RD-022:** Every multi-record mutation MUST have one durable terminal receipt/result so retry, disconnect, restart, and injected failure converge without duplicated AP, contribution, donations, deliveries, permissions, or withdrawals. Final-link preparation is a durable non-receipt-bearing decision operation; confirmation is a separate receipt-bearing mutation whose challenge consumption and gameplay transitions MUST share one atomic recovery boundary.
- **RD-023:** The first behavior slice MUST retain Mirrored Stone AP as receipt-compatible telemetry equal to each actual floored Personal/Cumulative AP award, including replay/recovery. Resource Delivery MUST NOT read, debit, or threshold on Mirrored AP. Removal MUST remain a separate reconciled refactor.
- **RD-024:** Implementation MUST update affected specs, content manifests/conformance, code, automated tests, and joined-client evidence together; docs approval, task authoring, and implementation authorization remain separate gates.

## Testing Decisions

Good tests assert externally visible domain behavior through the highest existing seams: authenticated command handlers,
receipt-backed stores, read models, and the live Foundational runtime adapter. Tests MUST NOT assert private collection
layouts, timer implementation, or UI widget structure.

- Pure domain tests cover Connection link-set/fan-out transitions, age/grace bands, participation intervals, strongest-link
  selection, exact multiplier arithmetic, bundle composition, capacity, and lifecycle snapshots.
- Contract tests cover authenticated donation, objective completion, incremental BP development, permission, withdrawal,
  otherwise-authorized AP award, delivery, replay, stale revision, delayed final-link confirmation, token-bearing
  principal substitution/lost release authority, fifth-placement AP-before-practice ordering, and rejection vocabulary.
- Partition tests cover upkeep renewal after expiry, same-timestamp receipt order, multiple delivery cycles, residual
  progress, dormancy freeze/resume, and pending-capacity priority.
- Recovery tests kill/restart at every durable boundary for final-link preparation/confirmation, donation, AP+contribution,
  delivery, permission, and withdrawal; challenge consumption can never separate from release/grace receipt.
- Adapter tests prove server attribution for inventory transfer and objective completion; clients never submit trusted
  completion or item-debit facts.
- Read-model tests prove privacy filtering and actionable state without exposing another account's raw provider identity.
- Joined-client evidence proves one weekly donation, one offline-reconciled completion, one Humble bundle deposit, one
  Bond withdrawal, one delegated withdrawal, and one full-capacity rejection. Logs alone are insufficient.
- Prior art is the existing Homestead progression domain/contract/rehydration suites and Foundational live-runtime seam;
  the feature deepens those seams rather than adding independent policy in Harmony patches.

## Requirement → Acceptance Mapping

| Requirement | Acceptance ID | Observable acceptance |
|---|---|---|
| RD-001 | AT-RD-001 | Canonical account-pair identity accepts either order, rejects self/unauthenticated pairs, and remains world/product-scoped. |
| RD-002 | AT-RD-002 | Only Bonded↔Attuned and Bonded↔Bonded links activate Connection; all excluded social/indirect edges do not. |
| RD-003 | AT-RD-003 | Boundary-time tests select exactly the six approved maturity multipliers. |
| RD-004 | AT-RD-004 | Preparation replays the exact principal/target-bound durable challenge across restart; fresh-ID confirmation rejects token-bearing principal substitution/lost authority, supports delayed/multi-Connection commit, freezes confirmation-time age, starts a full 72-hour grace, and makes consumption+release+receipt atomic across changed-set, competing-confirmation, crash, reconnect, and expiry cases. |
| RD-005 | AT-RD-005 | Solo/stale/grace-only/`0×` accounts contribute zero; both eligible sides contribute once and several links/Governors choose one strongest multiplier. |
| RD-006 | AT-RD-006 | Donation completion starts/refreshes a rolling seven-day expiry; exact expiry boundaries produce `0×/1×/2×`, while calendar reset, streak, repetition, and tier stacking do not. |
| RD-007 | AT-RD-007 | Reconcile-before-mutation preserves expired/renewed intervals; offline and arbitrary online partitions match across multiple cycles, residual progress, same-time ordering, and pending-capacity boundaries. |
| RD-008 | AT-RD-008 | One contribution advances all applicable outcomes together; a full Resource Delivery does not stop sibling outcomes. |
| RD-009 | AT-RD-009 | Otherwise-authorized AP floors once after full multiplication without widening source authority; Cumulative matches, BP is unchanged, replay is idempotent, and missing Connection/upkeep awards zero. |
| RD-010 | AT-RD-010 | Incremental BP development works in each Tree class; committed Trees gain equal cumulative investment, Foundational does not, Attuned AP purchase rejects, and conformance is 21/14/7. |
| RD-011 | AT-RD-011 | Completed Resource Delivery nodes compose one versioned bundle, enrich without slowing, and snapshot at completion. |
| RD-012 | AT-RD-012 | Active Bond spends exactly 1 BP to complete Humble; it produces exactly 10 Wood + 10 Stone after 24 baseline contribution-hours. |
| RD-013 | AT-RD-013 | Donations and deliveries read back from one durable virtual Stockpile; Level-2 defaults are 16 kinds / 1,000 total / 500 per item and configured overrides preserve provenance/capacity invariants. |
| RD-014 | AT-RD-014 | Valid donation transfers exactly once; invalid option, stale revision, insufficient items, or full capacity changes nothing. |
| RD-015 | AT-RD-015 | Full bundle deposits once or remains immutable pending; only Resource Delivery pauses, and newly freed capacity retries pending before later donations. |
| RD-016 | AT-RD-016 | Authorized full-fit withdrawal transfers requested quantities once; unauthorized/overflow/stale requests change nothing. |
| RD-017 | AT-RD-017 | Owner-role grant, duplicate/stale-race denial, revocation, generation-incrementing regrant, restart, and non-transitive denial prove one canonical permission and complete revocation. |
| RD-018 | AT-RD-018 | Level-2 candidate pool contains the three exact recipes; authorized two-option selection/default is stable and either selected option satisfies upkeep. |
| RD-019 | AT-RD-019 | One durable daily cycle counts five distinct eligible placements, completes once, ignores active-window prebuild, then resets after expiry; the fifth placement's combined receipt uses the prior AP tier and only subsequent events see `2×`; duplicates/client claims/stale definitions do not count. |
| RD-020 | AT-RD-020 | Committed-Tree revoke reconciles then deletes Resource Delivery development with no refund; recommit starts fresh, Foundational is unaffected, meter progress freezes/resumes, and pending/deposited contents remain unchanged. |
| RD-021 | AT-RD-021 | Read model exposes all actionable state and rejection reasons without leaking another account's raw identity. |
| RD-022 | AT-RD-022 | Retry and process death at each mutation boundary converge to one terminal result and no duplicates; final-link preparation decision replay and atomic confirmation consumption/release recovery are included. |
| RD-023 | AT-RD-023 | AP award/replay/restart keep Mirrored equal to the actual floored Personal/Cumulative award, while Resource Delivery progress/output remains identical across differing Mirrored values and never reads/debits it. |
| RD-024 | AT-RD-024 | Conformance/docs/tests/evidence gates pass together and repository diff contains no unauthorized task/runtime files. |

## Out of Scope

- Blacksmith, metal, non-renewable, or other bundle content beyond Humble Homesteader.
- A complete quest journal, procedural quest generator, narrative quest content, or daily-practice roster beyond the exact first fixture.
- Final AP/BP values, higher-level donation recipes, or economy ratification beyond the explicit first-fixture defaults.
- The mechanism that advances Stone Level; the preconfigured Level-2 fixture only consumes current level state.
- Party, Friendship, Guild, Discord, co-presence, or social-discovery edges as loyalty sources.
- Mirrored Stone AP removal, journal compaction, or historical data migration in this slice.
- Production account-provider migration, cross-world Connection sharing, or provider-key rotation.
- Physical world containers, world drops, mail, direct unsolicited inventory insertion, or player-to-player trading rules.
- Task decomposition, Kanban cards, runtime implementation, release, or migration guarantees.

## Further Notes

- “Tax” is player shorthand only: upkeep resources are transferred into Stone Stock, not destroyed.
- “Governor” is operation-specific authority. Use “Bonded player” when no Responsibility Range decision is involved.
- “Package” means the snapshotted Delivery Bundle before deposit; after deposit, items are fungible Stone Stock.
- Beyond the exact five-placement first daily fixture, future objective content remains data-authored. The provisional
  Archer examples discussed—100 Wood Arrows weekly and 500 bow damage daily—remain illustrative rather than first-fixture requirements.
- Approval of this package permits only the next explicit approval gate. It does not authorize tasks or implementation.
