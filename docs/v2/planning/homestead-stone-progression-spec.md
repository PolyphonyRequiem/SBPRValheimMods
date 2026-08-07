---
title: "Homestead Stone progression S2 — feature specification"
status: accepted
purpose: Normative product and acceptance contract for the confirmed Niflheim Homestead progression technical proof.
---

# Feature Specification: Homestead Stone progression S2

**Feature branch:** `feat/niflheim-homestead-stone`
**Created:** 2026-07-13
**Status:** Accepted by Daniel after cluster review and independent verification; the separately authorized task decomposition was accepted via PR #297, while implementation still requires separate authorization
**Input:** Confirmed Niflheim Stone-progression Wayfinder handoff, especially ticket 0011's corrected representative-slice contract and ticket 0012's ratified framework handoff.

> **Maturity:** This is a buildable technical-proof contract and playtest hypothesis. It is not final
> balance, final content, launch-family rollout, persistence-compatibility freeze, or release ratification.
> It specifies progression beside—not instead of—the existing
> [`homestead-stone-v1-impl-spec.md`](homestead-stone-v1-impl-spec.md) placement/identity substrate.

## Problem statement

The Homestead Stone currently has a real world object, deterministic placement, and stable host-zone
identity, but no authoritative progression model. Niflheim needs one bounded proof that a persistent
Stone can support relationships, separate personal and Stone ledgers, Governor-selected Trees,
Stone-owned cultivation, character-owned purchases, durable outcomes, recovery, and future remote
management without pretending to solve every Stone family or the still-undecided Stone-level advancement
mechanism.

The proof must be hostile-client-safe and replay-safe. A client profile, portable character file,
Valheim PlayerID, live scene object, or ZDO network owner cannot be treated as progression authority.

## User scenarios and testing

### User Story 1 — Relate to one authoritative Homestead and earn AP safely (Priority: P1)

As a bonded owner or attuned participant, I want my Stone relationship tied to my authenticated account, acting
character, and stable Stone identity; as an attuned participant, I want my AP activity credit bound the same
way so reconnects, sibling characters, and retries cannot duplicate or steal progression.

**Why this priority:** Every later mutation depends on trustworthy principals and one atomic receipt.
This is the P0 feasibility gate, not optional infrastructure.

**Independent test:** On a preconfigured Historical/Active Stone-Level-2 test Homestead, create one Bond and one
Attunement for characters on different authenticated accounts. Place one eligible Foundational catalog
piece in the Stone Area and verify exactly one receipt credits N Personal AP, N Cumulative AP Earned,
and N Mirrored Stone AP through retry, reconnect, and restart.

**Acceptance scenarios:**

1. **Given** no active character from the account is related to the Stone, **when** the server accepts a
   valid Bond or Attunement, **then** it persists the acting character's relationship and reserves the
   `(account, Stone identity)` active-character index.
2. **Given** one sibling character is actively Bonded or Attuned, **when** another sibling attempts either
   relationship, **then** the server rejects it without changing either aggregate.
3. **Given** the active relationship is ordinarily released, **when** a sibling later forms a relationship,
   **then** it succeeds while the former character's retained progression remains character-owned.
4. **Given** an eligible Foundational placement inside the Stone Area, **when** its operation is accepted,
   **then** Personal AP, Cumulative AP Earned, and Mirrored Stone AP each increase by exactly N atomically.
5. **Given** the same operation ID is replayed after timeout, reconnect, or restart, **when** it is handled
   again, **then** the prior result is returned and no balance or history changes twice.
6. **Given** an excluded, unknown, outside-area, or unauthenticated placement, **when** credit is requested,
   **then** no receipt is committed.

---

### User Story 2 — Commit and develop real Tree choices (Priority: P2)

As the Homestead's authorized Governor, I want to commit one Profession and one Martial Tree into authored
Stone Facets and spend my Stone-wide personal BP across my Responsibility Range so that the
Homestead develops through deliberate, legible choices.

**Why this priority:** It proves the shared Facet/Tree grammar without inventing Stone-level advancement.

**Independent test:** Starting with a preconfigured Stone-Level-2 Homestead and empty Profession/Martial Facets,
commit Cooking or Crafting to the Profession Facet and Archer or Warrior to the Martial Facet. Earn BP through
aligned activity, spend it developing eligible nodes and offerings across responsible Trees, and raise Cooking
Tree Level 1→2 when cumulative qualifying BP development crosses its provisional threshold.

**Acceptance scenarios:**

1. **Given** an empty authored Stone Facet and an authorized Governor, **when** a revision-checked
   `CommitTreeToFacet` operation selects an eligible Tree of the matching category, **then** exactly that Tree
   becomes Committed and starts at its authored initial state.
2. **Given** an unauthorized actor, stale revision, occupied Facet, ineligible Tree, category mismatch, or replay
   with conflicting payload, **when** commitment is attempted, **then** it fails atomically and does not alter
   Stone Level.
3. **Given** eligible aligned activity from any Committed Tree in the Governor's Responsibility Range, **when**
   BP credit is accepted, **then** it increases that character's one personal BP balance for this Stone.
4. **Given** BP earned from one responsible Tree, **when** the Governor spends it on another responsible Tree,
   **then** the spend is valid; no source-Tree wallet or Cultivation Target exists.
5. **Given** sufficient BP and Active Stone Level 2, **when** accepted Cooking node/Offering development makes
   cumulative qualifying BP investment cross its configurable threshold, **then** Cooking Tree Level advances
   from 1 to 2 exactly once; no separate Tree-level meter or direct-invest command exists.
6. **Given** BP is applied to an executable cultivatable node, **when** the operation succeeds, **then** node
   development and cumulative Tree investment advance in the same accepted mutation. A completed Local Node
   activates for the Stone; a completed personal node becomes Offered to eligible attuned players.
7. **Given** Savor the Hearth is cultivated, **when** it activates, **then** it is Stone-owned Local state and
   never appears as a personal AP purchase or Attunement Tier Access requirement.

---

### User Story 3 — Inspect, purchase, and derive Tree-specific access (Priority: P3)

As an attuned participant, I want to inspect exact node requirements and spend Stone-wide Personal AP on
eligible personal nodes so that I choose my own progression without a hidden target or sibling-Tree tax.

**Why this priority:** It proves character authorship and the distinction between Tree state, Stone cap,
and per-Tree derived Attunement Tier Access.

**Independent test:** Use bonded BP to develop Cooking's personal offerings, then have an attuned player purchase
Field Prep and Iron Stomach with AP, derive
Cooking Attunement Tier Access 2 only after Cooking Tree Level and Active Stone Level both permit it, then
purchase Swift Preparation. Leave Archer/Warrior incomplete and Watchful Cook unavailable to prove they do
not block Cooking.

**Acceptance scenarios:**

1. **Given** the participant may inspect a Stone, **when** the read model is requested, **then** every node
   reports stable identity/version, outcome type, first-build status, AP/BP price, Tree/Stone level gates,
   prior-Offered-Set gate, other requirements, and actionable state.
2. **Given** sufficient Personal AP and all current requirements, **when** `PurchaseNode` is accepted, **then**
   Personal AP is debited once, the purchase record and Offered-Set provenance persist, and activation is
   derived rather than copied into a second mutable ledger.
3. **Given** Cooking Tree Level 2 and Active Stone Level 2 but only one Level-1 personal Offered Node acquired,
   **when** Swift Preparation is requested, **then** it remains ineligible.
4. **Given** Field Prep and Iron Stomach are acquired from the versioned Cooking Level-1 Offered Set and all
   other requirements pass, **when** access is derived, **then** Cooking Attunement Tier Access is 2.
5. **Given** unfinished sibling Trees, Local Savor, or unavailable Watchful Cook, **when** Cooking access is
   derived, **then** none grants or blocks that Tree-specific result.
6. **Given** a Character Effect's supplying relationship becomes inactive, **when** activation is re-derived,
   **then** the Character Effect dormants without deleting the purchase; Permanent Effects remain active.

---

### User Story 4 — Prove the four accepted Tree identities (Priority: P4)

As a player, I want each offered Tree to deliver a distinct Local, Character, and Permanent outcome so that
Tree commitment is a meaningful play choice rather than a renamed ledger test.

**Why this priority:** The framework is not proven if its content branches are fake or diagnostic-only.

**Independent test:** Across at least three preconfigured test Homesteads, execute the 12 accepted Level-1 nodes plus
Cooking's Level-2 Swift Preparation while the seven authored-but-unavailable nodes reject every AP/BP and
activation path.

**Acceptance scenarios:**

1. **Cooking:** Savor the Hearth drains active food timers 50% slower for eligible characters inside the
   Stone Area; Field Prep exposes unchanged Boar Jerky and Queen's Jam recipes through the shared Cooking-aware
   Bushcraft policy; Iron Stomach permanently permits food refresh/replacement at 75% remaining; Swift
   Preparation makes eligible menu-crafted food take one-third of the vanilla skill-adjusted duration.
2. **Crafting:** Refined Workshop grants +1 effective Workbench level only for portable-item production,
   upgrade, and repair: a qualifying real Level-2 Workbench may perform an eligible effective-Level-3
   operation while the UI distinguishes real level from the `+1`; the same real station without the Local
   Effect cannot. It does not unlock building pieces/permissions, affect structure production, mutate the real
   observed level, or satisfy a Stone-level place-state objective. Masterwork issues one deterministic visible
   validated Workmanship Property on an eligible non-stackable durable item while active; Built to Last
   permanently improves maximum durability on future eligible outputs with exact-item provenance.
3. **Archer:** Practice Range locally unlocks the vanilla Archery Target plus 100 Practice Arrows for 8 Wood,
   with 0 ammo damage but retained bow damage and vanilla target return; Field Fletching I exposes unchanged
   Wood Arrows through Bushcraft while active; Fletcher's Habit permanently gives one configurable,
   authoritative terminal-impact recovery chance for one exact eligible arrow instance.
4. **Warrior:** T.W.I.G. Training locally unlocks the unchanged vanilla T.W.I.G.; Ready Hands shortens both
   queued equip and unequip durations for eligible melee weapons only while active; Weapon Discipline grants
   one permanent, idempotent choice among at least two authored melee skill-cap tiers.
5. **Unavailable nodes:** Watchful Cook, Measured Cuts, Artisan's Counter, Steady Aim, Bowyer's Lore, Shrug It
   Off I, and Heavy Hands remain visibly unavailable, accept no AP/BP, cannot activate, and do not count as
   Offered Nodes. Their authored capabilities and boundaries are still normative:
   - **Watchful Cook (Cooking Level 2, Character Effect):** meat or fish this character places on a slotted
     fire Cooking Station stops at Done instead of advancing to Burnt/Coal. It does not accelerate cooking.
     A future implementation requires per-slot authenticated account/character identity and a protection flag
     snapshotted at insertion; a future Stones-UI toggle affects later placements only. Exact toggle/persistence
     mechanics wait for its later implementation.
   - **Measured Cuts (Crafting Level 1, Character Effect):** eligible portable-item recipes consume fewer
     materials through one canonical server-computed requirement vector shared by preview, affordability, and
     debit. Every positive requirement retains an authored floor (default at least one), exclusions are
     data-defined, and no random refund or client-side cost calculation is permitted. Reduction and eligibility
     remain tuning.
   - **Artisan's Counter (Crafting Level 1, Local Effect):** a local selling surface lists one exact eligible
     item instance at an explicit asking price, exposes its real quality/durability/crafter/Workmanship
     Properties, and atomically transfers the item and proceeds through server-owned escrow. It is not vanilla
     trader reconstruction, an item-name order, or an infinite NPC currency faucet. Expiry, taxes, offline
     proceeds, cancellation, and market topology remain future economy design.
   - **Steady Aim (Archer Level 1, Character Effect):** reduces drawn-bow stamina drain while the character is
     not moving; camera, aim, and reticle movement remain free. Movement thresholds, forced/platform motion,
     dodge/jump semantics, reduction value, and later bow-redesign interactions remain future work.
   - **Bowyer's Lore (Archer Level 1, Permanent Effect):** permanently teaches an authored catalog of special
     bow and arrow recipes at their appropriate ordinary stations or Bushcraft tiers. Exact items, recipes,
     properties, and progression levels remain future content; this is portable learned knowledge, not a local
     workshop.
   - **Shrug It Off I (Warrior Level 1, Character Effect):** while active, accelerates recovery only from
     conditions explicitly classified as Minor Wounds. It is not generic health regeneration or healing-mead
     amplification. Wound taxonomy, clocks, stacking, death interaction, rate, and later Major-Wound ranks
     remain future injury-system work.
   - **Heavy Hands (Warrior Level 1, Character Effect):** while active, offsets a configurable portion of the
     negative movement modifier contributed by an authored registry of heavy two-handed melee weapons, never
     armor or other gear penalties. Any future alternate attack sequence is weapon-authored later; no moveset
     change executes in this proof.

---

### User Story 5 — Release, revoke, recover, and operate remote-shaped commands (Priority: P5)

As a returning player or Governor, I want durable earned outcomes, honest relationship release/revocation, and reusable
Stone-identity commands so that restart, relationship change, replacement Trees, and the future Stones UI do
not corrupt or silently reinterpret progression.

**Why this priority:** It closes the technical proof across lifecycle boundaries without building the final UI.

**Independent test:** Release and restore one Attunement, voluntarily release and restore one Bond, revoke a
Committed Tree, commit a replacement, restart the server, and issue one authorized progression selection through
the same Stone-identity command seam from a non-proximate test client.

**Acceptance scenarios:**

1. **Given** an attuned player releases Attunement, **when** the operation commits, **then** their Attunement
   Slot and applicable account index are released, AP and purchases persist, Character Effects dormant, and
   Permanent Effects and Progression Keys remain active. Reattunement restores eligible Character Effects
   without repurchase when the supplying Tree remains valid.
2. **Given** a bonded Governor voluntarily releases Bond and no authorized Governor remains, **when** release
   commits, **then** their Bond Slot and applicable account index are released, personal BP persists but cannot
   be spent while unbonded, Stone-owned Facet/Tree development persists in dormancy, Local Effects and new BP
   development stop, and no refund or cooldown is created. A later valid Bond restores eligible governance.
3. **Given** an authorized Governor revokes a Committed Tree, **when** the
   atomic operation commits, **then** Stone-owned commitment, cumulative BP development, node development,
   Local Nodes, and personal-node offerings are deleted with no BP refund.
4. **Given** revocation removes refundable Character-Effect purchases, **when** it commits, **then** each AP
   value is returned to that character as ordinary Stone-wide Personal AP, in full; Permanent Effects and
   Progression Keys survive without refund.
5. **Given** a replacement Tree is committed, **when** the character inspects it, **then** nothing is purchased
   automatically; recommitting the old Tree does not restore removed purchases. Refunded Personal AP is
   Stone-wide and may be deliberately spent on any Facet.
6. **Given** relog or server restart under the same proof build, **when** state reloads, **then** authoritative
   earned state and provenance persist, current-build references validate, and active effects are re-derived.
7. **Given** an authorized remote-shaped command with current revisions and already-satisfied world
   requirements, **when** it is submitted away from the Stone, **then** the server can execute it without
   proximity; no remote command can fabricate placement, presence, cooking, crafting, or combat evidence.

## Edge cases

- A timeout occurs after commit but before acknowledgement: replay returns the recorded result without a second
  debit, credit, level, refund, property, or choice.
- Stone and character persistence are interrupted at different points: recovery uses the receipt/audit journal
  and reports ambiguity for operator repair rather than guessing from whichever save is newer.
- The same operation ID is reused with a different principal, Stone, command type, or payload: reject as an
  idempotency conflict.
- Two valid actors race the same Stone revision: one commits; the other receives a stale-revision rejection and
  current revision, with no partial mutation.
- An incompatible content definition appears during development: stable IDs prevent same-build misbinding and
  the pre-release test fixture is reset explicitly; this proof does not invent production migration policy.
- Local beneficiary policy changes during area occupancy: re-derive eligibility from the new Settlement-wide
  policy; never store one personal Local-Effect purchase.
- Private policy and ordinary build access disagree: a local placement capability requires both the Local policy
  and ordinary build Permission.
- Active Stone Level is below a requested Tree Level: investment may persist only according to an explicitly
  authored checkpoint contract; it cannot activate above the current cap.
- An invalid, tampered, duplicate, missing, or current-build-unknown item-property receipt appears: grant vanilla behavior only
  and keep the instance auditable/quarantinable.
- Practice Range target return and Fletcher's Habit both encounter the same shot: target return wins its
  deterministic path and the permanent recovery roll does not run.
- A released character retains Permanent Effects and Progression Keys: these do not keep the account's active-character
  exclusivity reservation alive.

## Requirements

### Identity, authority, and fixture requirements

- **FR-001:** The system MUST key every progression operation and read by stable world-scoped Stone identity;
  for the current Homestead substrate this is the world identity plus host Location zone coordinate.
- **FR-002:** The server MUST bind the authenticated connection to an account and acting character and MUST NOT
  trust client-supplied Valheim PlayerID, portable `.fch` state, scene ownership, or ZDO network ownership as
  progression authority.
- **FR-003:** For Homesteads, the server MUST enforce at most one active Bond or Attunement per `(account, Stone
  identity)` across sibling characters; ordinary release MUST clear only that active exclusivity. This is a
  variant-authored policy, not a universal Stone invariant: Community Stone Attunement permits sibling
  characters, while Community Bond remains account-exclusive for now. Bond Slots and Attunement Slots MUST be
  the character-wide relationship scarcity mechanism; this proof MUST NOT add a separate node/Tree portfolio cap.
- **FR-004:** The proof MUST use at least three preconfigured test Homesteads at Historical/Active Stone
  Level 2 and one bonded-owner character plus one attuned character from different authenticated accounts.
  Provisioning MUST NOT be presented as a Stone-level advancement mechanism.
- **FR-005:** Every fixture MUST contain one protected, system-authored Homestead Foundational Tree from Stone
  Level 1, occupying no Stone Facet and owning one stable-ID basic-piece catalog.
- **FR-006:** The optional palette and Facets MUST be data-defined: Cooking/Crafting for one Profession Facet
  and Archer/Warrior for one Martial Facet, with exactly one Committed Tree per Facet.

### Ledger and transaction requirements

- **FR-007:** Every mutation MUST carry authenticated principal, Stone identity, expected revision, operation ID,
  typed payload, and content/schema version context; the server MUST revalidate all current requirements.
- **FR-008:** The Foundational activity family MUST remain an eligible, deliberately low-value AP source
  throughout the Homestead's life. Activities associated with current Committed Trees MAY add stronger sources;
  uncommitted optional candidates MUST NOT authorize AP. An accepted AP activity receipt MUST atomically add
  exactly N Personal AP, N Cumulative AP Earned, N Mirrored Stone AP, and provenance. Mirrored Stone AP MUST
  only accumulate in this proof; it has no spend, threshold, or Facet operation yet.
- **FR-009:** Personal AP MUST be one Stone-wide character balance and MUST NOT be bound to a source Tree or
  Personal Target; a revocation refund MUST return ordinary Stone-wide Personal AP and MUST NOT introduce a
  Facet-keyed balance separate from it.
- **FR-010:** BP MUST be one personal balance per bonded character per Stone, spendable across every Committed
  Tree in that Governor's Responsibility Range; different Governors MUST NOT share balances.
- **FR-011:** BP MUST be spent developing eligible nodes and offerings; accepted spends MUST also contribute to
  cumulative Tree investment. No separate direct Tree-level spend, command, or meter may exist. Successive
  unlock costs and Tree-level thresholds MUST remain configurable playtest data, and no named/all-node checklist
  may be a universal Tree-level gate.
- **FR-012:** The system MUST use optimistic revisions and idempotent recorded results so crash, reconnect,
  duplicate delivery, and concurrent commands converge without partial or repeated effects.

### Tree, node, and activation requirements

- **FR-013:** Tree commitment MUST validate authority, Facet category, palette version, occupancy, and Stone
  cap and MUST NOT mutate Stone Level.
- **FR-014:** Attunement Tier Access MUST be derived per Tree from acquired prior-level personal Offered Nodes and
  capped by that Tree's Tree Level and Active Stone Level; sibling-Tree progress and Cumulative AP Earned MUST
  neither grant nor block it, and it MUST NOT be stored as independent mutable XP.
- **FR-015:** Local Nodes MUST be developed Stone-owned state and MUST NOT enter personal Offered Sets, AP
  purchases, or Tier Access calculations.
- **FR-016:** One Settlement-wide beneficiary policy MUST govern all active Local Effects with no per-effect
  override: Everyone (default) means everyone in the Stone Area; Attuned means the owner plus currently
  attuned characters; Private means the owner plus the explicit Settlement allowlist. Local placement
  capabilities MUST additionally pass ordinary build Permission.
- **FR-017:** The content registry MUST author exactly 20 current nodes, mark 13 as executable and seven as
  unavailable, and expose status honestly through the read model.
- **FR-018:** Unavailable nodes MUST reject AP/BP, activation, and Offered-Set membership; fake diagnostic effects
  are prohibited.
- **FR-019:** Active outcomes MUST be derived from earned/selected state, relationship, Stone/Tree state,
  requirements, policy, and dormancy; no independently mutable active-effects ledger may exist.
- **FR-020:** Bonded Governor BP development MUST activate completed Local Nodes once for the Stone and make
  completed Character/Permanent/Key nodes Offered. Only an eligible attuned player may spend Personal AP to
  acquire those personal nodes; bonding alone MUST NOT grant personal AP-purchase authority. This proof MUST
  contain no Tree-completion state or Completion Bonus.

### Lifecycle, content, and interface requirements

- **FR-021:** Tree revocation MUST atomically apply the exact reset/refund contract in User Story 5 and MUST NOT
  refund BP or remove Permanent Effects or Progression Keys.
- **FR-022:** Current-build records MUST carry stable IDs sufficient to prevent same-build misbinding. Production
  migrations, grandfathering, retired-content handling, and compatibility guarantees are deferred; incompatible
  unreleased fixtures MAY be reset explicitly rather than silently reinterpreted.
- **FR-023:** Save/reload, relog, restart, Attunement loss/rejoin, voluntary Bond release/rejoin, and interrupted mutation MUST
  have deterministic reconciliation behavior and an auditable result.
- **FR-024:** The system MUST expose one Stone-identity read model and explicit server commands reusable without
  semantic change by a temporary local panel and future remote Stones UI.
- **FR-025:** Remote progression commands MUST revalidate current authority, relationship, responsibility,
  balances, requirements, content version, and revisions. They MUST NOT manufacture local activity evidence.
- **FR-026:** Every one of the 13 executable nodes MUST receive its own smallest joined-client or in-world proof
  of its user-visible effect, paired with automated contract/domain coverage. One representative proof for a
  multi-node Tree tracer is insufficient; logs alone are insufficient.
- **FR-027:** Spec, code, tests, and the owning runtime drift manifest or equivalent MUST change together once
  implementation begins.

### Key entities

- **Stone Identity:** Stable world-scoped key for one Homestead Stone.
- **Stone Aggregate:** Authoritative Stone-owned level, Stone Facets/palette, Tree development, Local, Mirrored AP, and
  provenance state.
- **Account–Stone Active-Character Index:** Variant-authored authority/anti-abuse constraint, not gameplay progression.
- **Character Progression Aggregate:** Character-owned relationships, relationship slots, AP/BP, purchases,
  durable outcomes, and choices, keyed by Stone where applicable.
- **Content Registry:** Current-build stable-ID definitions for Facets, Tree palettes, Trees, nodes, activities,
  catalogs, and properties.
- **Operation Receipt:** Idempotent audit record binding principal, Stone, command/payload, revisions, deltas,
  provenance, and result.
- **Derived Activation View:** Recomputed effective outcomes; never an independent source of truth.
- **Progression Read Model:** Player-readable Stone view with exact status and rejection reasons.

## Success criteria

- **SC-001:** The P0 AP receipt passes crash/retry injection at every write boundary with exactly one Personal AP,
  Cumulative AP, and Mirrored AP result and no ambiguous silent repair.
- **SC-002:** The preconfigured suite executes 13 real nodes—12 Level-1 nodes plus Swift Preparation—and all seven
  unavailable nodes reject every purchase/cultivation/activation path.
- **SC-003:** Cooking Tree Level advances 1→2 when cumulative qualifying BP-driven node/Offering development
  crosses its configurable threshold, with no separate level meter; a subset of current nodes may suffice.
- **SC-004:** Cooking Tier Access 2 is granted only by the same-Tree prior Offered Set plus Tree/Stone caps and is
  unaffected by sibling Trees, Local Nodes, or unavailable nodes.
- **SC-005:** Attunement release/rejoin and voluntary Bond release/rejoin preserve the exact character/Stone state
  defined in User Story 5, dormancy is derived correctly, no refund/cooldown is invented, and governance restores safely.
- **SC-006:** Tree revocation/replacement produces no BP refund, no automatic replacement purchase, a correct
  full per-character Personal AP refund, and survival of Permanent Effects and Progression Keys.
- **SC-007:** Every mutation boundary is replay-safe and race-safe: no negative/double balance, split receipt,
  double level, duplicate property, duplicate cap choice, or partial revocation.
- **SC-008:** At least one progression selection executes through the reusable Stone-identity command/read-model
  seam without proximity while every world-activity requirement remains server-validated and local.
- **SC-009:** Automated tests, repository docs lint, link checks, table checks, and `git diff --check` pass; each
  of all 13 executable nodes later receives joined-client/in-world evidence before being called playable.

## Assumptions and dependencies

- The current `src/SBPR.Niflheim.HomesteadStones/` placement/identity slice remains the owning runtime and its
  world-scoped host-zone identity remains the progression Stone identity substrate.
- Authentication and atomic cross-aggregate receipts are not shipped capabilities; they are the first mandatory
  spike/gate.
- Initial AP/BP prices, escalating unlock-cost curves, Tree-level thresholds, repetition controls, activity granularity, effect factors,
  property values, and eligible registries are configurable playtest data unless this spec fixes a proof value.
- World and character persistence cannot be assumed atomic. The plan must supply a server-owned journal/receipt
  reconciliation path before gameplay mutations depend on both.
- This package uses SBPR's ADR-0005 adaptation: Spec Kit vocabulary/templates in `docs/v2/planning/`, not the
  `specify` CLI, `.specify/`, or `specs/NNN-feature/` layout.

## Explicitly out of scope

- Actual Stone-level advancement; Workbench/Chopping Block Stone-level gates; Mirrored Stone AP thresholds.
- Level 3+, AP upkeep, decay-driven dormancy rollback, bonded upkeep, automatic unbonding, and bond cooldowns.
- Finished Stones UI, remote discovery, navigation, notifications, and presentation polish.
- Daily quest rotation and final AP/BP economy or anti-repetition tuning.
- Context-generated Tree palettes, catalog rerolls, or Trees outside the fixed four-Tree proof.
- No executable Level-2 node for Crafting, Archer, or Warrior.
- No final Workmanship catalog, material-efficiency economy, market topology, special bow/arrow catalog, final bow
  redesign, wound taxonomy, heavy-weapon movesets, universal melee-cap increase, or final skill-cap ladder.
- Wyrd reset/reservation, Community boundary enforcement, region-scale slots, Guild/Wild, party/territory/ACL,
  portal storms, worldgen, or clustered-50-player proof.
- Completion Bonuses, Tree-completion state, and the shelved Cooking 20→35 cap reward.
- Production persistence-compatibility freeze, migration ratification, release packaging, or implementation.
