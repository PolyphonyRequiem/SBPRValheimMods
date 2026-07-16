---
title: "Party & Guild wayfinder — formal social-system horizon"
status: idea
purpose: >
  Chart the way from the current Homestead substrate + emergent-party findings to
  a buildable formal Party (temporary) and later Guild (durable) social system,
  as a map of investigation tickets and Daniel decision clusters. Two distinct
  horizons; Guild is NOT "Party but persistent." No implementation, no spec lock.
author: architect
card: t_33147639
depends_on:
  - homestead-emergent-party-dynamics.md   # t_b7ea5c03 (parent) — grounded inputs
grounds_against:
  - homestead-stone-progression-data-model.md
  - homestead-stone-progression-spec.md
  - homestead-stone-progression-contracts.md
consumed_by:
  - t_213d4aaa   # SYNTHESIS — future Stone systems briefing/roadmap
---

# Party & Guild wayfinder — formal social-system horizon

**Daniel's prompt (verbatim, 2026-07-16):** "We should probably start discussing
how party and later guild systems might be put into play."

**Reading rule (inherited from the parent brainstorm).** Every load-bearing claim
is either cited to the substrate (`data-model.md` / `spec.md` / `contracts.md` with
the field or FR) or to the parent brainstorm (`emergent-party` with its cluster),
or it is marked **[OPEN → Daniel]** / **[OPEN → RE]**. Recommendations are labelled
**[ARCHITECT LEAN]** and are *not* locks. Nothing here mutates a spec or authorizes
a card. This is a **wayfinder map**: a shared list of investigation tickets ordered
by dependency and risk, plus the decision clusters that gate them.

**Scope discipline (from the card + parent).** This card does **not** invent Party
or Guild aggregates ahead of the two inherited non-negotiables. It charts *what we
still don't know*, in what order it must be resolved, and where Daniel must choose —
one cluster at a time.

---

## 0. What we inherit (do not re-litigate)

The parent brainstorm (`homestead-emergent-party-dynamics.md`, PR #314, `status:
idea`) already resolved the *emergent* layer and handed forward hard constraints.
This wayfinder **starts from these as settled inputs**, not open questions:

| # | Inherited input | Source |
|---|---|---|
| I1 | "Natural party" = the **shared-Stone cohort**, a *derived* read-model, not a stored aggregate. The Stone is the party object we already have. | emergent §Cluster A [LEAN] |
| I2 | The **one thing** that genuinely needs new durable state is a **consent-to-*person*** primitive (transient invite/accept). Everything else derives. | emergent §B2, §"two structural options" |
| I3 | **Permission never composes by union.** It resolves by **intersection per-(character, Stone, action)** at the moment of the action, against that Stone's own policy. A party/guild may *carry* a grant token but never *replace* the Stone's resolver. | emergent §Cluster C [LEAN] |
| I4 | **No group credit.** Character AP/BP is never pooled, transferred, or read by a group. A pooled resource, if ever built, is a *separate* project ledger funded by explicit contribution — never a raid on character/Stone wallets. | emergent §Cluster D [LEAN] |
| I5 | **Membership keys on `CharacterId`**, respecting account sibling exclusivity per Stone (one active character per account per Homestead Stone). | emergent §E-note; data-model §Aggregate 2 |
| I6 | **Context is target-driven**, not an ambient "active group" mode: the *target* of an action carries its context. | emergent §Cluster F [LEAN] |
| I7 | Threat sweep **T1–T8** is satisfiable by existing per-op revalidation (spec FR-025) + receipts/anomaly log (data-model §Aggregate 5) *provided* I3 and I4 hold. | emergent §"exploit sweep" |

Several of I1/I3/I4/I5/I6 still carry **[OPEN → Daniel]** confirmations in the
parent (its Clusters A/C/D/E/F). Those five are re-listed here as **gating decisions
G-A…G-F** because the formal design cannot start until they land. This wayfinder
adds the *formal-system* unknowns on top of them.

---

## 1. Destination

**Two distinct buildable horizons, charted but NOT locked:**

- **Horizon A — Temporary Party:** a bounded, server-authoritative *consent-to-person*
  layer that survives reconnect and expresses "invite this specific player to
  cooperate for a while" — the single capability the emergent layer proved it cannot
  fake (emergent §scenario matrix row 7). Everything a Party can derive, it derives
  (I1); it stores only the minimum consent/roster/lifecycle state.

- **Horizon B — Durable Guild:** a persistent social *identity* with roster, roles,
  succession, and audit, whose relationship to Stones is an **explicit, bounded
  affiliation** — never a global permission that overrides per-Stone governance (I3).
  Guild is a *different shape* from Party: Party is transient consent between people;
  Guild is durable identity + governed affiliation to Stones. **The boundary between
  them is itself a deliverable (see §4).**

The map is "done" when: (a) the five gating decisions G-A…G-F have Daniel answers;
(b) every frontier ticket below is either resolved or consciously deferred; and
(c) a minimal vertical-slice recommendation for Horizon A exists with an explicit
"what waits for Community/Settlement Stones" list. **We are nowhere near done —
this session charts the map; it does not resolve tickets.**

---

## 2. Ubiquitous-language glossary

Distinct nouns, sharp ownership. Ambiguity here is how Guild silently becomes
"Party but persistent."

| Term | Definition | Owns | Lifetime | NOT |
|---|---|---|---|---|
| **Co-presence** | characters in the same Stone Area / zone now | nothing (spatial, derived) | instantaneous | consent; membership |
| **Shared-Stone cohort** | characters holding a relationship to the same Stone | derived from relationship records | as long as the relationships exist | a roster; a party |
| **Party** | transient, opt-in, character-keyed membership set with optional leader; a *consent-to-person* object | its own roster + lifecycle + (optional) leader role | minutes–hours; dissolves on empty/expiry | a permission grant; a wallet; durable identity |
| **Named grant** | an explicit, revocable, Stone-owned authorization from a Governor to a *named* character | the **Stone** (provenance like relationship formation) | until revoked or relationship lost | transitive; party-scoped; a role |
| **Guild** | durable social identity: roster + roles/ranks + succession + audit | its own aggregate (identity, roster, roles) | indefinite; survives all members offline | a Stone owner by default; a permission source |
| **Guild affiliation** | an explicit, bounded link between a Guild and a Stone (owned / hosted / allied / none) | a *link record* whose permission semantics are still I3-bounded | until dissolved | a global override of per-Stone governance |
| **Role / Rank** | a named capability set *within* a Party or Guild (invite, kick, promote, manage affiliation) | the Party/Guild aggregate | membership-scoped | a Stone build permission; cross-Stone authority |
| **Permission** | authority to act on a *specific Stone* | the **Stone**, per-(char, Stone, action) | per-operation, revalidated | ever a union across members or groups (I3) |
| **Responsibility** | the *Governor's* Responsibility Range bounding BP spend | the character↔Stone Bond | per-Bond | a guild-wide budget |
| **Fellowship / Alliance** | a *social* fact of cooperating Homesteads (mutual named grants + overlapping attunements) | **nobody** — deliberately unmodeled | social | an aggregate ([LEAN]: leave social, emergent §Cluster G) |

**[ARCHITECT LEAN]** Keep **Fellowship/Alliance as a non-noun** — a described social
pattern, not a stored object — unless a ticket proves a concrete need. Modeling it is
the classic third-card-early guild trap. (emergent §Cluster G.) It earns a glossary
row only to *name the thing we are refusing to build*.

---

## 3. Gating decisions (must land before any formal ticket starts)

These are the parent's five reserved Daniel questions, promoted to **gates** because
the formal Party/Guild design forks on each. Present **one cluster at a time**.

- **G-A (definition fork).** Confirm "natural party" = shared-Stone cohort (derived),
  so Horizon A *adds only* the consent-to-person layer on top — vs. Daniel already
  picturing an explicit invite group as the primary object. *Decides whether Party is
  "derive + thin consent record" or "design a full aggregate from scratch."*
  [LEAN: derived base + thin consent record.]
- **G-C (named grants now?).** Allow Governors to issue explicit, named, revocable
  cross-Stone guest grants at the emergent layer now, or defer all cross-Stone access
  to the formal card. *If "now," the named-grant primitive (I3 rule 2) exists before
  Party and Party never needs to carry permissions at all.* [LEAN: allow now.]
- **G-D (pooled ledger?).** Is co-located individual earning enough for the
  shared-project fantasy, or does Daniel want a **pooled fundable project ledger**
  (separate from character AND Stone wallets)? *This is the single biggest scope fork
  for BOTH horizons — a Guild "treasury/project" only exists if this is "yes."*
  [LEAN: individual earning is enough until a concrete need appears.]
- **G-E (character-keyed membership).** Confirm membership keys on `CharacterId`
  respecting account sibling exclusivity per Stone. [LEAN: strong yes — I5.]
- **G-F (context resolution).** Target-drives-context (clean, exploit-resistant) vs.
  a player-selected "active group" selector (familiar to MMO players). *UX-vs-safety;
  affects every group-scoped action's API shape.* [LEAN: target-driven; an active-group
  selector, if wanted, is UI sugar over target-driven resolution, never an authority.]

**Dependency note.** G-A, G-E, G-F gate **Horizon A** tickets. G-C gates the named-grant
tickets (which both horizons reuse). G-D gates the **treasury/project** tickets in both
horizons and is the only gate that can *expand* scope materially.

---

## 4. The Party↔Guild boundary (explicit — a required deliverable)

The card is emphatic: **do not make Guild "Party but persistent."** Here is the
load-bearing distinction, stated as invariants the two designs must not blur:

| Axis | Party (Horizon A) | Guild (Horizon B) |
|---|---|---|
| **Essence** | consent *between people*, transient | durable *identity*, persistent |
| **Exists when empty?** | No — dissolves when last member leaves/expires | Yes — survives all members offline; identity persists |
| **Primary state** | roster + lifetime + optional leader | identity + roster + roles/ranks + succession + audit |
| **Relationship to Stones** | none — carries no Stone authority (I3) | explicit, bounded *affiliation* link (owned/hosted/allied/none) |
| **Permissions** | never a source; derives/target-driven (I6) | never a global override; affiliation is still I3-intersection-bounded |
| **Progression** | never pools AP/BP (I4) | never pools AP/BP; a *guild project ledger* (if G-D=yes) is a separate funded aggregate |
| **Leadership** | optional leader role, transient | roles/ranks + **succession** (guild must survive its founder) |
| **Audit** | receipts for invite/accept/kick/expire | durable audit log (roster/role/affiliation changes) |
| **Failure mode if blurred** | leader role becomes a mini-permission source | affiliation becomes the one global guild-permission that overrides Stones (T1 at guild scale) |

**[ARCHITECT LEAN]** The clean seam: **Party = a lifecycle over a membership set;
Guild = an identity that *owns* memberships and *declares* affiliations.** A Guild is
not a long-lived Party because a Party has no identity independent of its current
members, no roles that outlive a session, and no relationship to Stones. If we ever
find ourselves adding "persistence" and "roles" and "a Stone link" to Party, we have
built a Guild and should stop and design it as one.

---

## 5. The map — investigation tickets

Ordered by dependency and risk. **Frontier** = open, unblocked, unclaimed. Each ticket
is one ~1-session question. `blocked_by` uses gate ids (G-*) and ticket ids (W-*).
Nothing here is claimed or resolved in this charting session.

### 5.1 Horizon A — Temporary Party

| id | title | type | blocked_by | risk | question |
|---|---|---|---|---|---|
| **W-A1** | Party membership record shape | grilling+prototype | G-A, G-E | med | What is the minimal server-authoritative membership record? Fields, keys (CharacterId), max size, and the sibling-exclusivity guard (I5). Derives cohort/co-presence; stores only consent. |
| **W-A2** | Invite / accept consent protocol | prototype | W-A1 | **high** | The consent-to-person handshake: offer → opt-in accept, never auto-join, carries **no** grant (T5). Idempotent/revisioned like relationship formation (contracts). Anti-spam/anti-harassment seams. |
| **W-A3** | Leaderless vs leader; role capability set | grilling | W-A1, G-F | med | Is there a leader at all? If so, what can a leader do (invite/kick/disband) and is it a *role field* on the record? Must NOT become a permission source (§4). |
| **W-A4** | Membership lifetime, disconnect/rejoin, expiry, dissolution | grilling+prototype | W-A1 | med | Lifetime rules: idle expiry, disconnect grace + rejoin window, empty-party dissolution, explicit leave/kick. Recovered via receipt journal (spec FR-012/FR-023) on crash/reconnect. |
| **W-A5** | Multi-membership + target-driven context resolution | grilling | W-A1, G-F | **high** | When a character is in multiple groups/Stones, how does a group-scoped action pick its context? Confirm target-driven (I6); define the API shape at the action site; reject ambient union (T8). |
| **W-A6** | Party-scoped shared objectives, presence, map/pins | research+grilling | W-A1 | med | What group features are in-scope (shared presence UI, objective marker) vs routed out? **Pin-sharing is a SEPARATE subsystem** (docs/design/pin-sharing.md) — route out, do not model here (emergent §Cluster D [OPEN→RE]). |
| **W-A7** | Loot / credit / effects under a Party | grilling | W-A1, **G-D** | **high** | Confirm NO group credit (I4). Local Effects beneficiary set is Stone policy only; membership adds nothing (T6, FR-016). If G-D=yes, a *separate* funded project ledger — never character/Stone wallets. |
| **W-A8** | Friendly-fire / PvP scoping under a Party | grilling | — | med | PvP/friendly-fire is **not modeled in the Homestead substrate at all** (emergent [OPEN→Daniel]). Needs its own scoping decision before a Party can claim to govern it. |
| **W-A9** | Temporary permissions vs named grants — which primitive? | grilling+prototype | G-C, W-A1 | **high** | Does "temporary party permission" collapse entirely into the emergent **named-grant** primitive (I3 rule 2), or is a distinct party-scoped temp permission needed? [LEAN: named grants subsume this; Party carries no permission.] |
| **W-A10** | Party server event/receipt seams | research | W-A2, W-A4 | med | Enumerate the authoritative server events (invite/accept/leave/kick/expire/dissolve) and their receipt rows; confirm reuse of the existing OperationReceiptStore idempotency (parent yield-node card A) rather than a new journal. |
| **W-A11** | Horizon-A minimal vertical slice recommendation | grilling | W-A1…W-A10 | med | Assemble the smallest shippable Party after Homestead S2: which tickets are in the first slice, which defer. **Required card deliverable.** |

### 5.2 Horizon B — Durable Guild

| id | title | type | blocked_by | risk | question |
|---|---|---|---|---|---|
| **W-B1** | Guild identity + roster aggregate shape | grilling+prototype | Horizon A settled | med | Durable identity (name, id, founding), character-keyed roster (I5), lifetime independent of member presence. Distinct aggregate from Party (§4). |
| **W-B2** | Roles / ranks capability model | grilling | W-B1 | **high** | Roles as *explicit grants within the guild* (invite, kick, promote, manage affiliation, manage project). Ranks ≠ Stone permissions (§4). No transitive union (I3). |
| **W-B3** | Invitations / applications lifecycle | prototype | W-B1, W-A2 | med | Two directions: guild invites a player; player applies to guild. Reuse the consent-to-person handshake (W-A2) shape. Opt-in both sides. |
| **W-B4** | Leadership succession + guild dissolution | grilling | W-B1, W-B2 | **high** | The guild must survive its founder. Succession rules (elected/appointed/automatic), and clean dissolution (roster teardown, affiliation release, audit finalization). |
| **W-B5** | Guild↔Stone affiliation model | grilling+prototype | W-B1, **G-C** | **CRITICAL** | The four affiliation shapes (guild-owned Stone / affiliated independent Homesteads / shared Community-Settlement Stone / no ownership). Each affiliation is I3-intersection-bounded; **no global guild permission** overriding per-Stone governance. |
| **W-B6** | Permissions as explicit grants, not transitive union | grilling | W-B5, G-C | **CRITICAL** | Enforce I3 at guild scale: an affiliation is an *input* to a Stone resolver, never a replacement. A guild role never auto-grants build/participation on an affiliated Stone; grants stay explicit + revocable + per-Stone. |
| **W-B7** | Cross-Stone governance boundaries | grilling | W-B5, W-B6 | **high** | Where does guild governance stop and Stone governance begin? A Governor's Responsibility Range (FR-010) is never widened by guild membership; Bond/Attunement slots (data-model) are never bypassed by a guild role. |
| **W-B8** | Guild projects / resources / progression | grilling | **G-D**, W-B1 | **high** | Only if G-D=yes: a guild project/treasury ledger funded by explicit contribution, that **never reads or writes** character AP/BP or Stone Mirrored AP (I4, T3). Guild progression ≠ character progression ≠ Stone Tree development. |
| **W-B9** | Guild audit + anomaly log | research | W-B1, W-B2 | med | Durable audit for roster/role/affiliation/project changes. High-value enforcement + meaningful anomaly logs over exhaustive anti-cheat (card constraint). Reuse Aggregate-5 receipt/anomaly infra. |
| **W-B10** | Expulsion, account/character switching, membership edge cases | grilling | W-B1, G-E | med | Expulsion (vs voluntary leave), what happens to a member's guild standing when they switch characters/accounts (I5), sibling-character interaction with a single guild membership. |
| **W-B11** | Cross-world / cross-product scope; migration; conflict | research+grilling | W-B1 | **high** | Does a Guild span worlds/products? Migration of guild state across versions; conflict handling on concurrent edits (revisioned like contracts). This is where durability bites hardest. |
| **W-B12** | Horizon-B "what must wait for Community/Settlement Stones" | grilling | W-B5 | med | Explicitly list which Guild capabilities *depend on* Community/Settlement Stones existing (shared-Stone governance, guild-owned communal Stone) and therefore cannot ship until those land. **Required card deliverable.** |

### 5.3 Wiring notes

- **Everything in 5.1 blocks everything in 5.2** at the coarse level: the card says
  learn Party before Guild, and Guild reuses Party's consent handshake (W-A2 → W-B3)
  and its no-union/no-credit discipline. W-B1's `blocked_by` "Horizon A settled" means
  the W-A11 slice recommendation is delivered and Daniel has reacted, not that all of A
  is *built*.
- **G-C is the pivot primitive.** If Daniel says "named grants now" (G-C=yes), the
  named-grant record exists at the emergent layer and *both* W-A9 and W-B6 largely
  reduce to "reuse the named grant, bounded per-Stone." If G-C=no, W-A9 and W-B5/W-B6
  each have to invent their own cross-Stone access story — much larger surface.
- **G-D is the only scope-expander.** With G-D=no, W-A7 and W-B8 shrink to "confirm no
  pooling." With G-D=yes, both grow a whole project-ledger sub-design (a new aggregate,
  contribution flows, its own receipts) — this roughly doubles the Guild surface.

---

## 6. Architecture shapes (2–3, honest trade-offs)

State-ownership and lifecycle at low resolution. **Not a choice made here** — options
for the tickets to evaluate.

### Shape 1 — Derived-first, thin consent (minimum viable social)

- **Party** = shared-Stone cohort (derived, I1) + co-presence, PLUS one thin
  server-authoritative **membership record** that stores *only* consent (who accepted,
  optional leader, lifetime). Permissions: none carried — named grants (G-C) handle
  host→helper; everything else target-driven (I6).
- **Guild** = deferred entirely; "guild feeling" achieved by a persistent shared
  Community/Settlement Stone + named grants + social alliance (unmodeled).
- **State added:** one small Party record family + its receipts. No Guild aggregate.
- **Pros:** smallest surface; impossible to escalate by construction (I3/I4 hold
  trivially); ships right after S2; nothing to migrate. Matches emergent §Option-1 lean.
- **Cons:** no durable guild identity, no roles/succession, no guild project. If Daniel
  genuinely wants named persistent teams, this defers that indefinitely.
- **Lifecycle:** Party created on first accept → mutated on join/leave/kick → dissolved
  on empty/expiry; recovered from receipts on crash. Cohort view is stateless.

### Shape 2 — Two bounded aggregates (Party now, Guild later, sharply separated)

- **Party** as Shape 1's thin record. **Guild** as a *separate durable identity
  aggregate* (roster + roles + succession + audit) whose only Stone relationship is an
  explicit **affiliation link** that is I3-intersection-bounded. Guild project ledger
  is its own aggregate, present only if G-D=yes.
- **State added:** Party record + Guild identity aggregate + affiliation link records
  (+ optional project ledger). Each with sharp ownership, per the substrate's
  many-small-aggregates style (data-model).
- **Pros:** supports named persistent teams, roles, succession — the real Guild
  fantasy — while the §4 boundary keeps Guild from becoming "Party but persistent."
  Affiliation-as-input (not override) preserves per-Stone governance (I3).
- **Cons:** real surface area; the affiliation resolver (W-B5/W-B6) is the CRITICAL
  risk — one sloppy "guild members can build on affiliated Stones" line reintroduces
  T1 at guild scale. Needs Community/Settlement Stones for the richest affiliations
  (W-B12). More to migrate (W-B11).
- **Lifecycle:** Guild identity persists indefinitely; roster/roles mutate under
  audit; affiliations attach/detach per Stone; succession fires on leader loss;
  dissolution tears down roster + releases affiliations + finalizes audit.

### Shape 3 — Unified "social group" aggregate with a persistence flag (REJECTED baseline)

- One `SocialGroup` aggregate; a boolean/enum flips it between "transient party" and
  "durable guild." Roles, affiliations, and (maybe) a ledger all hang off the one object.
- **Why it's here:** it's the tempting shortcut, and naming it lets us reject it on
  the record.
- **Pros:** one code path; superficially "simple."
- **Cons:** **this is exactly the "Guild = Party but persistent" the card forbids.**
  A shared object with a mode flag inevitably grows a "group permission" that the flag
  makes global (T1), blurs the §4 essence distinction, and couples Party's high-churn
  lifecycle to Guild's durability/migration needs. The substrate's own style is many
  small sharply-owned aggregates, not one god-object with a mode bit.
- **[ARCHITECT LEAN]** **Do not adopt Shape 3.** It is documented only to be refused.

### [ARCHITECT LEAN] Recommendation to carry into the tickets

**Ship Shape 1 first** (thin consent Party after S2), and **treat Shape 2's Guild as a
bounded, separately-designed add** gated on G-D and on Community/Settlement Stones —
never as a persistence flag on the Party. The evidence: the emergent card already
proved 6 of 7 co-play scenarios need no aggregate at all; the 7th (consent-to-person)
is Shape 1's thin record; and *durable identity* is a genuinely different concern that
earns its own aggregate only when Daniel wants named persistent teams with roles and
succession. Shape 3 is the anti-pattern the card exists to prevent.

---

## 7. Abuse / threat model (formal layer, extends emergent T1–T8)

The emergent T1–T8 sweep still applies. The formal layer adds threats that only exist
once *durable identity* and *roles* exist:

| # | Threat | Vector | Mitigation (grounded) | Gate/ticket |
|---|---|---|---|---|
| P1 | Guild affiliation as global permission | "guild members may build on any affiliated Stone" | Affiliation is an *input* to the per-Stone resolver, never a replacement; I3 intersection holds at guild scale | W-B5, W-B6 |
| P2 | Role escalation | a low rank grants itself invite/promote | Roles are explicit capability sets on the guild aggregate, server-validated per action; no self-promotion path | W-B2 |
| P3 | Succession hijack | founder leaves, attacker seizes leadership | Deterministic succession rule + audit; no unaudited leadership transfer | W-B4, W-B9 |
| P4 | Guild treasury as wallet-raid | project ledger reads character/Stone AP/BP | Ledger is fund-by-explicit-contribution only; never reads/writes character or Stone wallets (I4, T3) | W-B8, G-D |
| P5 | Invite/application spam & harassment | mass invites or applications | Rate-limit + opt-in both sides + anomaly log; consent-to-person carries no grant (T5) | W-A2, W-B3 |
| P6 | Sibling/account switch to dodge expulsion | expelled member rejoins via sibling character | Membership character-keyed but expulsion recorded at a level that respects account identity for enforcement (I5) | W-B10 |
| P7 | Migration/version conflict corruption | concurrent guild edits across worlds/versions | Revisioned, idempotent edits (contracts pattern); conflict resolution + migration policy | W-B11 |
| P8 | Cross-Stone governance leak | guild role widens a Governor's Responsibility Range | Responsibility Range (FR-010) and Bond/Attunement slots are never widened by guild role | W-B7 |

**[ARCHITECT LEAN]** Consistent with the card constraint: **high-value enforcement +
meaningful anomaly logs over exhaustive anti-cheat.** P1/P4/P8 are the load-bearing
"cannot be allowed to happen by construction" class (enforce hard, per-op). P5/P6 are
the "log and rate-limit, don't gold-plate" class. None require new anti-cheat machinery
beyond the substrate's per-op revalidation (FR-025) + receipts/anomaly log (Aggregate 5).

---

## 8. Vertical-slice recommendation & sequencing

**[ARCHITECT LEAN] — provisional, gated on Daniel's G-A…G-F and on S2 shipping.**

1. **After Homestead S2 ships and is playtested** (not before — this is backlog).
2. **First slice = Shape 1 thin-consent Party**, built from W-A1, W-A2, W-A4, W-A5,
   W-A10 (record, consent handshake, lifecycle, target-driven context, receipts).
   Leader role (W-A3) optional in-slice per G-F/W-A3. **No permissions carried**
   (W-A9 → reuse named grants if G-C=yes).
3. **Defer within Party:** W-A6 pins (route to pin-sharing subsystem), W-A7 pooled
   loot (only if G-D=yes), W-A8 PvP (needs its own scoping decision first).
4. **Guild (Horizon B) waits for two things:** (a) Daniel wanting durable named teams,
   and (b) **Community/Settlement Stones existing** — because the richest affiliations
   (guild-owned communal Stone, shared Settlement Stone) have no substrate to attach to
   until then (W-B12). Guild identity + roster (W-B1) *could* precede Community Stones;
   affiliation (W-B5) largely cannot.
5. **What NOT to build yet:** Shape 3 unified aggregate (ever); any pooled ledger unless
   G-D=yes; Alliance/Fellowship as an object; PvP governance under a party; guild
   permissions that flow transitively; cross-world guild scope before W-B11 is resolved.

---

## 9. Daniel decision points (present one cluster at a time)

**Cluster 1 — the five inherited gates (must land first).** G-A definition fork,
G-C named grants now, G-D pooled ledger, G-E character-keyed membership, G-F context
resolution. (Details §3; leans stated.)

**Cluster 2 — Party shape.** Given the gates: is Shape 1 (thin-consent Party) the
right first social system after S2, with named grants (not party permissions) as the
cooperation primitive? Leader role in-slice or deferred?

**Cluster 3 — Guild appetite & timing.** Do you want durable named teams (roles,
succession, audit) at all, and if so, is it acceptable that the richest guild↔Stone
affiliations wait for Community/Settlement Stones? Is the §4 boundary (Guild ≠ Party
but persistent) the one you want enforced?

**Cluster 4 — the scope-expander.** G-D again, in its guild form: do you want a guild
project/treasury ledger (a whole extra aggregate), or is co-located individual earning
enough at guild scale too?

**Cluster 5 — routed-out concerns.** PvP/friendly-fire scoping (needs its own decision,
W-A8); pin-sharing (separate subsystem, W-A6 [OPEN→RE]); cross-world/cross-product guild
scope (W-B11).

---

## 10. Items routed to RE / other subsystems

- **[OPEN → RE]** Party/Guild scoping of **map pins** belongs to the cartography/
  pin-sharing subsystem (`docs/design/pin-sharing.md`), not the social aggregate. Do not
  model pin-sharing inside a Party/Guild ticket. (Inherited from emergent §Cluster D.)
- **[OPEN → Daniel]** **PvP / friendly-fire** is not represented in the Homestead
  substrate at all; it needs its own scoping decision before any Party/Guild can claim to
  govern it. (Inherited from emergent §Cluster D.)
- **[OPEN → RE]** **Community / Settlement Stone** mechanics are referenced by W-B5/W-B12
  but are their own future substrate (data-model marks Community Attunement/Bond policy but
  Settlement-Stone-as-guild-home is not designed). The guild-affiliation tickets depend on
  that design landing first.

---

## 11. Handoff

- **To the synthesis card (t_213d4aaa):** this wayfinder is one of four future-system
  tracks. Its headline for the roadmap: **Party is a small thin-consent record after S2;
  Guild is a separate durable-identity aggregate that mostly waits for Community/Settlement
  Stones; the boundary between them (§4) is a hard invariant; the two scope-expanders are
  G-D (pooled ledger) and G-C (named grants now).** The contradiction the synthesis must
  surface: the **resource-yield track** (t_6153a995) and the **social-permission track**
  (this + t_b7ea5c03) both touch "who may benefit from a Stone's output" — reconcile that
  a *yield claim* is Stone-owned and per-relationship, while a *party/guild* never widens
  the beneficiary set (I3/I4, T6). Do not average them: yields flow by relationship, not by
  group membership.
- **No cards created, no specs touched, no code.** Per the card's standing instruction
  (worker comment) this stays research/wayfinding; brief Daniel, do not auto-create
  implementation cards without a later explicit decision.
