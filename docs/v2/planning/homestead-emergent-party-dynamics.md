---
title: "Homestead emergent party dynamics — brainstorm"
status: idea
purpose: >
  Explore whether useful co-play emerges from existing Homestead primitives
  (relationships, permissions, Responsibility Range, beneficiary policy) before
  any formal Party aggregate is introduced. Grounded input for the Party/Guild
  wayfinder (kanban t_33147639). No implementation, no formal Guild design here.
author: architect
card: t_b7ea5c03
grounds_against:
  - homestead-stone-progression-data-model.md
  - homestead-stone-progression-spec.md
---

# Homestead emergent party dynamics — brainstorm

**Daniel's prompt (verbatim, 2026-07-16):** "Discuss how natural party dynamics
might work in this system through combination of multiple homesteads, attunement,
permissions, etc."

**Reading rule for this doc.** Every load-bearing claim is either cited to the
substrate (`data-model.md` / `spec.md` with the field or FR that backs it) or
marked **[OPEN → Daniel]** / **[OPEN → RE]**. Where I recommend, the recommendation
is labelled **[ARCHITECT LEAN]** and is *not* a lock. Nothing here mutates a spec.

> **⚠️ SUPERSESSION NOTICE (2026-07-21) — read before acting on any recommendation below.**
> This brainstorm's central active recommendation — *"natural party = the shared-Stone
> cohort as a derived read-model; start with Option 1 (derived view + named grants); do
> NOT build a Party object/god-object"* (see Decision cluster A, §Option 1, and the
> §closing "Definition/Start derived" bullets) — has been **SUPERSEDED by explicit owner
> decision (Daniel, 2026-07-16)**. Daniel settled that **a formal Party object IS required**;
> the derived shared-Stone cohort (plus Homestead ownership/Attunement and later Friends)
> is a **discovery/suggestion input**, not Party identity. The settled Party model is
> ephemeral (logout drops membership; unclean disconnect held for a reconnect-grace
> timeout), capped at **3 characters (configurable)** and dissolving at one member,
> **all-peers with no leader**, with **forced removal by majority excluding the target**,
> and with **Discord integration shelved** to a future horizon. The authoritative statement
> of the settled model lives in **`party-guild-wayfinder.md` §0.5** — treat that as current.
> The historical exploration below is **preserved unchanged for evidence and attribution**;
> where it says "derive, don't build a Party object," read it as the superseded prior lean,
> not current policy. The still-valid safety constraints it carries (permission-by-intersection
> never union, no pooled character AP/BP, CharacterId-scoped membership, target-driven Stone
> authority) remain in force.

---

## 0. What the substrate already gives us (grounding)

Before inventing anything, these are the primitives that exist and are *accepted*
(data-model.md status: accepted). A social system must compose from these or
consciously add to them.

| Primitive | What it is | Cite |
|---|---|---|
| `CharacterId` owns gameplay progression | AP/BP, purchases, durable outcomes, relationships belong to the **character**, never the account | data-model §Aggregate 3 inv. "Gameplay progression belongs to the character, not the account" |
| `AccountId` is authority/grouping/audit only | Not progression ownership; used for the sibling-exclusivity index | data-model §Stable identity: `AccountId` "Authority/grouping/audit only" |
| Relationship = one Bond **or** Attunement, one character ↔ one Stone | `RelationshipId` is the edge; capacity is Bond Slots / Attunement Slots on the character | data-model §Aggregate 3 Capacity; `RelationshipId` row |
| Bond = governance; Attunement = participation | Bonded Governor commits/develops Trees; Attuned character purchases Offered nodes, earns AP | spec FR-010 (Governor BP), FR-020 (attuned AP purchase) |
| **Responsibility Range** bounds what a Governor can develop | BP is spendable only "across every Committed Tree in that Governor's Responsibility Range" | spec FR-010 |
| Account–Stone sibling exclusivity | At most one character per account actively holds a relationship to one Homestead Stone; **Community** differs (Attunement no sibling exclusivity; Community Bond still account-exclusive) | data-model §Aggregate 2 invariants |
| Settlement beneficiary policy (Everyone / Attuned / Private) | ONE Settlement-wide policy governs all Local Effects; Private uses an explicit allowlist | spec FR-016 |
| Local placement needs policy **AND** ordinary build Permission | Two independent gates, not one | spec FR-016; data-model §spec "Private policy and ordinary build access disagree" |
| Durable outcomes survive relationship loss | Permanent Effects + Progression Keys persist across release/revocation | data-model §Aggregate 3 invariants |

**Critical starting fact:** there is **no Party aggregate, roster, leader, or guild
object today** (card body; confirmed — no such aggregate in data-model §Aggregates
1–5). The task is explicitly *not* to smuggle one in. So the honest first question
is not "how do we build parties" but "**what co-play already falls out of these
edges, and where does the absence of a roster actually hurt?**"

---

## Decision cluster A — What is a "natural party" here?

**Terms first.**
- **Co-presence:** two characters in the same Stone Area / world zone at the same
  time. Transient, spatial, derivable from position. Requires **zero** new state.
- **Relationship graph:** the set of Bond/Attunement edges a character holds across
  Stones. Durable, already persisted (data-model §Aggregate 3 Relationships).
- **Shared-Stone set:** characters who hold a relationship to the *same* Stone.
  Derivable by querying the `AccountStoneAuthorityIndex` / relationship records for
  a `StoneId`. **Durable, but derived — no roster is stored.**
- **Shared objective:** a soft/social concept (a quest, a build project). Today it
  has no representation at all.

**The candidate definitions of "natural party":**

1. **Co-present players** — everyone standing in the same area now. *This is
   Valheim's vanilla "nearby players" and needs no SBPR state.*
2. **Shared-Stone cohort** — everyone attuned/bonded to Stone X. This is the one
   that is *already durable and already queryable* from the substrate.
3. **Relationship-overlap web** — characters who share ≥1 Stone with me, plus
   characters who share a Stone with *them* (transitive). Growth here is the
   danger zone (see Cluster C).
4. **Explicit invitation group** — a named, opt-in roster. **This is the thing that
   does not exist and is exactly what the Party/Guild wayfinder (child card) must
   decide whether to add.**

**[ARCHITECT LEAN]** The useful "natural party" that already exists is **#2, the
shared-Stone cohort**, viewed as a **derived read-model, not a stored aggregate.**
The Stone *is* the party object we already have. Two players "party up" in the
emergent sense precisely by both holding a relationship to a common Stone — that
is what a Homestead already means. Everything else (co-presence, invitation groups)
either needs no state or is deferred to the child card.

**[OPEN → Daniel]** Do you want "natural party" to mean the shared-Stone cohort
(my lean), OR do you already picture something closer to an explicit invite group
that just hasn't been drawn yet? Your answer decides whether the child card starts
from "derive a view" or "design an aggregate."

---

## Decision cluster B — Behaviors that need NO party state vs. behaviors that demand durable identity

The cheap win is separating these two piles honestly. Padding the "needs state"
pile is how you accidentally build a guild system three cards early.

### B1. Needs NO new state (derive or already exists)

| Behavior | Why it's free | Cite / mechanism |
|---|---|---|
| Fighting together in an area | Vanilla co-presence; combat is not Stone-scoped | vanilla |
| Benefiting from a Stone's Local Effects | Already governed by beneficiary policy; "Everyone/Attuned" already includes co-present or attuned players | spec FR-016 |
| Seeing who shares my Stone | Query relationship records for `StoneId` → derived cohort view | data-model §Aggregate 2 (index is per `(AccountId, StoneId)`) |
| A helper earning AP at a Stone they're attuned to | Attunement already grants AP-purchase + Foundational AP credit | spec FR-020; data-model §Credit Foundational AP |
| Durable "we are allied Homesteads" *felt* by players | Emergent from overlapping attunements + social agreement | no state needed |

### B2. DEMANDS durable identity / roster / leadership (i.e., real new state)

| Behavior | Why it can't be derived | Consequence |
|---|---|---|
| "Invite this specific stranger to my group for the next 2 hours" | No edge exists to hang a transient membership on; co-presence isn't consent | needs a **transient membership record** (Party horizon, child card) |
| Group-scoped loot/credit split | Requires an authoritative "who is in the group" at the moment of the award | needs roster + award policy |
| Leadership / who can kick / who can invite | Requires a role field on a membership record | needs role state |
| Cross-Stone shared project with pooled contribution | Pooling AP/BP across characters violates character-ownership unless modeled as a *separate* project ledger | needs a **new project aggregate**, NOT a raid on character wallets (see Cluster D) |
| Persistent "these players are a named team" | This is a **Guild** by another name — do not build it here | deferred to child card, Guild horizon |

**[ARCHITECT LEAN]** The B1/B2 split says the emergent layer can carry a
surprising amount of co-play with **zero** new durable state, *as long as* we resist
two temptations: (a) turning co-presence into consent, and (b) pooling
character-owned progression. Both are covered below.

---

## Decision cluster C — Permission composition across multiple Stones (the escalation trap)

This is the highest-risk cluster and the one most likely to leak a real exploit.

**The setup.** A character can hold relationships to multiple Stones (Attunement
Slots > 1; a character bonded to Stone A can be attuned to Stone B). Each Stone has
its own beneficiary policy, its own Permission note, its own Governor. So a single
character walks around carrying a *bundle* of per-Stone grants.

**The trap: union-of-privilege.** If "party" ever means "we merge our permissions,"
then two players each holding a narrow grant could compose into a broad one, or a
guest could ride a member's grant into a Stone that never invited them. That is a
privilege-escalation bug, and the substrate already legislates against its root:

- Permission is **per-Stone**, gated by relationship-to-*that*-Stone. Local
  placement needs *that Stone's* policy AND *that Stone's* build Permission
  (spec FR-016). There is no global permission.
- The beneficiary policy is **Settlement-wide but Stone-local** — one policy per
  Stone, no per-effect or cross-Stone override (spec FR-016).
- Authority is validated per operation against *current* state, per Stone
  (spec FR-025 "revalidate current authority, relationship, responsibility…").

**[ARCHITECT LEAN — the load-bearing rule for the whole social system]:**
**Permission never composes by union. It is always resolved per-(character, Stone,
action) at the moment of the action, against that Stone's own policy.** A party or
guild may *carry* a grant token, but the token is an *input* to that Stone's
resolver, never a *replacement* for it. The Stone remains the sole authority over
its own Facets, Local state, and build rights. This preserves the substrate's
"Stone owns its domain" boundary and makes "guest rides a member's grant" impossible
by construction: the guest has no relationship-to-that-Stone, so the resolver denies,
regardless of party membership.

**Concretely, three composition rules to carry to the child card:**
1. **Intersection, not union.** If an action touches Stone X, only the actor's own
   grant *for Stone X* matters. Group membership adds nothing to that resolution.
2. **Grants are delegated, explicit, and revocable — never transitive.** A Governor
   may *explicitly* extend a build/participation grant to a named character (this is
   a Stone-owned act with provenance, like relationship formation). It does not flow
   automatically to that character's friends, party, or guild.
3. **Most-restrictive-wins on conflict.** If a character is somehow eligible under
   two contexts for the same Stone action, the *Stone's* policy decides; ambiguity
   resolves to deny + a legible rejection reason (mirrors spec's "actionable
   rejection reasons," DerivedActivationView).

**[OPEN → Daniel]** Do you want Governors to be able to issue *explicit, named,
revocable* cross-Stone guest grants at the emergent layer (rule 2), or should ALL
cross-Stone access wait for the formal Party/Guild card? (My lean: allow the
explicit named grant now — it's just "relationship formation, lighter" and it's the
honest primitive that "host invites a helper" actually needs.)

---

## Decision cluster D — Scoping AP/BP, credit, effects, claims, build rights, map knowledge, PvP

The substrate is emphatic about ownership. The social layer must not quietly break it.

| Resource | Where it lives today | Party-safe rule | Cite |
|---|---|---|---|
| **Personal AP** | character, Stone-wide | Never pooled, never transferred. A helper earns their *own* AP by their *own* attuned activity | data-model §Aggregate 3 AP; FR-020 |
| **Personal BP** | character, per bonded Stone, spendable in Responsibility Range | Never shared across Governors ("different Governors MUST NOT share balances") | spec FR-010 |
| **Cumulative AP Earned** | character | Never decreases; personal — no group aggregate | data-model §Aggregate 3 invariants |
| **Mirrored Stone AP** | Stone ledger | Stone-owned, not debited/applied in this proof; a group cannot spend it | data-model §Aggregate 1 |
| **Local Effects** | Stone-owned, beneficiary-scoped | Group membership does NOT widen the beneficiary set; policy (Everyone/Attuned/Private) still decides | spec FR-016 |
| **Build rights** | per-Stone Permission | Per-Stone, per-actor; no group-wide build grant | spec FR-016 |
| **Map knowledge / pins** | (cartography system, separate) | **[OPEN → RE]** pin-sharing is a different subsystem (docs/design/pin-sharing.md); party scoping of pins is out of this substrate | pin-sharing.md |
| **PvP / friendly-fire** | vanilla | **[OPEN → Daniel]** not modeled in Homestead substrate; needs its own call | — |

**[ARCHITECT LEAN]** The rule for progression credit is simple and defensible:
**there is no group credit. Every character earns their own AP/BP by their own
qualifying activity.** "Shared project" feeling is achieved by *co-located
individual earning* (three attuned players each earning their own AP at the same
Stone), not by a split-the-pot mechanic. If Daniel later wants a genuine pooled
project resource, that is a **new, separate project ledger** that the group *funds
by explicit contribution* — it must never read or write character AP/BP wallets or
the Stone's Mirrored AP. That keeps every substrate ownership invariant intact.

**[OPEN → Daniel]** Is "co-located individual earning" enough for the shared-project
fantasy, or do you specifically want a pooled contribution ledger (a fundable group
project distinct from character and Stone wallets)? This is the single biggest
scope fork for the child card.

---

## Decision cluster E — Lifecycle edge cases (disconnect, release, revocation, siblings, guests, abuse)

The substrate already has honest answers for the *relationship* lifecycle; a party
layer must not contradict them.

| Event | Substrate behavior (cite) | Emergent-party implication |
|---|---|---|
| Disconnect | Co-presence ends; relationships persist | If "party" == co-presence, it dissolves naturally, no cleanup. If it's an invite group, it needs a **disconnect grace/rejoin** rule (Party horizon) |
| Attunement release | AP, purchases, Offered provenance, Permanent Effects, Keys preserved; Character Effects dormant (data-model §Release relationship) | Leaving the shared-Stone cohort loses *nothing durable* — good; the cohort view just stops listing you |
| Bond (Governor) release, no Governor remains | Facets dormant, Local Effects stop, BP persists but unspendable; later Bond restores (spec US5 AC2) | A "governed by our group" project must tolerate the Governor walking away — governance is per-Stone, not group-owned |
| Tree revocation | Atomic reset/refund contract; refunded AP returns as Stone-wide Personal AP; Permanent Effects/Keys survive (spec FR-021) | Group cannot be a backdoor to dodge or share the revocation contract |
| Sibling character | One active character per account per Homestead Stone (data-model §Aggregate 2) | **Key exploit vector:** a party must be a set of **characters**, and must respect that an account can only *be present* as one sibling per Stone. See E-note below |
| Guest account | AccountId is authority/audit; unauthenticated payloads rejected for CharacterId (data-model §Stable identity) | Guests get **no** implicit Stone grant; only explicit named grants (Cluster C rule 2) |
| Malicious invitation | — (no invite system today) | The reason invitations are risky is precisely that they'd be the *first* consent primitive; must be **opt-in accept**, never auto-join, never a grant carrier |

**E-note (character vs account membership).** Because progression is
character-owned but exclusivity is account-scoped, **party membership must key on
`CharacterId`, not `AccountId`.** But any group that confers Stone-relative context
must still respect the account sibling rule per Stone. Practically: your Woodcutter
and your Warrior are two characters; they can't *both* be the account's active
relationship-holder at one Homestead Stone at once, so they can't both occupy that
Stone's cohort slot simultaneously. A party layer that forgets this creates a
double-presence exploit.

**[OPEN → Daniel]** Confirm party membership is character-keyed (my strong lean,
consistent with the whole substrate). This one bit propagates through the entire
child card.

---

## Decision cluster F — Multiple simultaneous groups; which context wins per action

A character can hold many relationships and (later) belong to many groups. So "which
context applies?" is a real question the moment any action is group-sensitive.

**[ARCHITECT LEAN]** There is **no global "active party" that flavors all actions.**
Context is resolved **per action, by the target of the action**, not by a
player-held mode switch:
- An action against **Stone X** resolves under Stone X's policy + the actor's
  relationship to X. Group membership is irrelevant to authority (Cluster C).
- An action that is **inherently group-scoped** (e.g. future group loot split)
  resolves under *the group that owns that action's context* — which, for a
  co-presence party, is unambiguous (there's one co-present group), and for an
  invite group must name its group explicitly at the action site.
- **No union across groups.** Belonging to two groups never sums privileges; it just
  means two independent memberships exist.

This dodges the classic "which guild's tax applies / which party gets the loot"
ambiguity by refusing to make actions read an ambient party mode. The *target* (a
Stone, a specific project, a specific award event) always carries its own context.

**[OPEN → Daniel]** Is target-drives-context acceptable, or do you envision a
player-selected "active group" concept (common in MMOs: you pick which guild tag /
which party is 'active')? Target-driven is cleaner and exploit-resistant; an
active-group selector is more familiar to players. This is a UX-vs-safety tradeoff.

---

## Decision cluster G — What should stay deliberately manual / social, NOT systematized

Under-building is a feature here. Things I recommend we *refuse* to model, at least
until proven necessary:

- **"Alliance" between Homesteads.** Two Governors agreeing to cooperate is a social
  fact expressed by mutual explicit grants + overlapping attunements. It does **not**
  need an Alliance object. **[ARCHITECT LEAN: leave social.]**
- **Loot/credit fairness.** Individual earning (Cluster D) is already fair. A
  split-the-pot system invites griefing and wallet-raiding. **[LEAVE SOCIAL unless
  Daniel wants the pooled project ledger.]**
- **Reputation / standing between groups.** No substrate hook; pure social.
- **Chat/voice/coordination.** Out of scope; vanilla + Discord.
- **"Who's the real leader" of an emergent cohort.** The shared-Stone cohort has
  *Governors* (Bond holders) as its only authority figures — that's leadership
  enough at the emergent layer. A separate "party leader" role is a Party-horizon
  concept, not an emergent one.

The design instinct: **systematize consent and authority; leave cooperation and
fairness social** until a concrete abuse or friction proves otherwise.

---

## The two structural options for the child card

The wayfinder (t_33147639) must choose between these. I lay out both honestly and
state my lean; the *decision* is Daniel's + the child card's.

### Option 1 — Derived-view party (no new aggregate)

"Party" is a **read-model** computed from existing edges: the shared-Stone cohort
(Cluster A #2) plus co-presence. Explicit host→guest cooperation is handled by
**explicit named revocable grants** (Cluster C rule 2), which are lightweight
Stone-owned relationship records, not a party object.

- **State added:** none for the cohort; grants are per-Stone provenance records
  (same family as relationship formation — already a modeled shape).
- **Pros:** zero new aggregate; can't drift from ownership invariants; nothing to
  reconcile on crash beyond what receipts already cover (spec FR-012/FR-023); ships
  fast; impossible to escalate privilege by construction.
- **Cons:** no transient "invite for 2 hours" membership; no group-scoped loot; no
  named team identity. Anything requiring *consent to a group* (not to a Stone) is
  unavailable.
- **What genuinely requires state that this option lacks:** transient membership
  with a leader and group-scoped awards (Cluster B2 rows 1–3).

### Option 2 — Explicit transient Party aggregate

A real **PartyAggregate**: character-keyed roster, optional leader, membership
lifetime, disconnect/rejoin grace, opt-in invite/accept, max size. Server-authoritative,
durable enough to survive reconnect, recovered via the receipt journal.

- **State added:** a new aggregate + its lifecycle transitions + receipts.
- **Pros:** supports invitations, group-scoped mechanics, named transient teams,
  leadership — the things Option 1 can't do.
- **Cons:** real surface area; must be *carefully* firewalled from permission
  composition (Cluster C) and progression ownership (Cluster D) or it becomes the
  escalation/ wallet-raid vector; it is the on-ramp to accidentally building a Guild
  ("Party but persistent" — which the child card explicitly forbids).

### [ARCHITECT LEAN] Recommendation to carry into the child card

**Start with Option 1 (derived view + explicit named grants), and treat Option 2 as
a *bounded add* only for the specific behaviors that Cluster B2 proves cannot be
derived** — i.e. transient invite membership, leader role, and (if Daniel wants it)
a fundable project ledger. Do **not** adopt Option 2 wholesale. The evidence: every
"needs state" row in B2 is a *specific* capability, and each can be a small,
independently-justified record rather than one monolithic Party god-object. This
matches the substrate's own style: many small aggregates with sharp ownership, not
one aggregate that knows everything.

The single fact that most argues for even the bounded Option 2: **there is no consent
primitive today.** Co-presence isn't consent; a shared Stone is consent *to the
Stone*, not *to a person*. If Daniel wants "invite this specific player to cooperate,"
that is the one genuinely new thing the emergent layer cannot fake — and it is
exactly the seam the Party horizon should own.

---

## Exploit / threat sweep (emergent layer)

| # | Threat | Vector | Mitigation (grounded) |
|---|---|---|---|
| T1 | Privilege escalation via union | "party merges permissions" | Intersection-only resolution per-(char, Stone, action); grants never transitive (Cluster C) |
| T2 | Guest rides a member's grant | co-presence treated as authority | Guest has no relationship-to-Stone; resolver denies regardless of party (Cluster C) |
| T3 | Wallet raid / credit theft | "shared project" reads character AP/BP | No group credit; project ledger (if any) is separate, fund-by-contribution only (Cluster D) |
| T4 | Sibling double-presence | account fields both characters into one Stone's cohort | Membership is character-keyed but respects account sibling exclusivity per Stone (Cluster E-note) |
| T5 | Malicious auto-join | invitation that carries a grant or auto-adds | Invites are opt-in accept, carry no grant, never auto-join (Cluster E) |
| T6 | Beneficiary-policy widening | party membership expands "Attuned" set | Beneficiary set is Stone policy only; membership adds nothing (Cluster D; FR-016) |
| T7 | Revocation dodge | group holds Tree state to survive Governor's revoke | Revocation is atomic per-Stone; group can't own Stone Facet state (Cluster E; FR-021) |
| T8 | Overlapping-group ambiguity | attacker picks whichever group grants more | Target-drives-context; no ambient active-group union (Cluster F) |

None of these require new anti-cheat machinery beyond the substrate's existing
per-operation revalidation (spec FR-025) and receipt/anomaly logging (data-model
§Aggregate 5) — *provided* the two lean-rules hold: intersection-only permission and
no group credit.

---

## Scenario matrix

| Scenario | What happens with Option-1 primitives | Needs Option-2? |
|---|---|---|
| **Solo Homestead** | One Bond (Governor) + optional Attunements; cohort = self. Nothing new | No |
| **Two allied Homesteads** | Overlapping attunements + mutual explicit named grants; each Stone keeps its own authority | No (alliance stays social) |
| **Roaming helper** | Attunes to the host Stone (earns own AP), OR receives an explicit named build grant from the Governor; leaves → grant revoked, durable outcomes kept | No — this is the killer case *for* explicit named grants |
| **Shared project** | Co-located individual earning; each earns own AP/BP | Only if Daniel wants a pooled fundable ledger |
| **Hostile / abusive guest** | Has no relationship + no grant → denied everything; if granted, grant is revocable + audited | No (revoke the grant) |
| **Relationship release** | Cohort view drops them; durable outcomes preserved (FR release contract) | No |
| **"Invite this stranger to my group for tonight"** | **Cannot be expressed** — no consent-to-person primitive | **YES — this is the Party horizon's reason to exist** |

The matrix makes the boundary concrete: **six of seven scenarios need no Party
aggregate.** The seventh — transient consent to a *person* (not a Stone) — is the
whole justification for the child card, and it should be built as narrowly as that
one need requires.

---

## Handoff to the Party/Guild wayfinder (t_33147639)

Grounded inputs this doc hands forward:

1. **Definition:** natural party = shared-Stone cohort (derived), NOT a stored
   aggregate — pending Daniel's Cluster-A answer.
2. **The one thing that genuinely needs new state:** a *consent-to-person* primitive
   (transient membership + opt-in invite). Everything else derives.
3. **Two non-negotiable lean-rules** the formal design must inherit:
   permission resolves by **intersection per-(char, Stone, action)**, never union;
   and there is **no group credit** — character progression is never pooled or read
   by a group.
4. **Membership keys on `CharacterId`**, respecting account sibling exclusivity per
   Stone.
5. **Context is target-driven**, not an ambient active-group mode (pending Daniel's
   Cluster-F answer).
6. **Start derived (Option 1); add only the bounded records Cluster B2 proves are
   underivable.** Do not build a Party god-object; do not let Guild become "Party but
   persistent."
7. Threat sweep T1–T8 with mitigations, all satisfiable by existing per-op
   revalidation + receipts.

---

## Questions reserved for Daniel (consolidated)

Presented one cluster at a time in the sections above; collected here for the reply.

- **A.** Is "natural party" the shared-Stone cohort (derived), or do you already
  picture an explicit invite group? (Decides derive-vs-aggregate starting point.)
- **C.** Allow Governors to issue explicit, named, revocable cross-Stone guest
  grants at the emergent layer now, or defer all cross-Stone access to the formal
  card? (My lean: allow now — it's the honest primitive "host invites a helper"
  needs.)
- **D.** Is "co-located individual earning" enough for the shared-project fantasy,
  or do you want a pooled fundable project ledger (separate from character and Stone
  wallets)? (Biggest scope fork.)
- **E.** Confirm party membership is character-keyed (strong lean).
- **F.** Target-drives-context (clean, exploit-resistant) vs. a player-selected
  "active group" (familiar to players)? (UX-vs-safety tradeoff.)

## Items routed to RE / other subsystems

- **[OPEN → RE]** Party-scoping of map pins belongs to the cartography/pin-sharing
  subsystem (docs/design/pin-sharing.md), not the Homestead substrate. Do not model
  pin sharing in the social card without that subsystem's input.
- **[OPEN → Daniel]** PvP / friendly-fire is not represented in the Homestead
  substrate at all; it needs its own scoping decision before any party can claim to
  govern it.
