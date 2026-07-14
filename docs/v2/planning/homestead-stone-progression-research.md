---
title: "Homestead Stone progression S2 — research and spike boundary"
status: accepted
purpose: Ground the progression specification against the current Homestead runtime, confirmed Niflheim research, vanilla seams, and unresolved implementation risks.
---

# Homestead Stone progression S2 — research

**Date:** 2026-07-13
**Companion spec:** [`homestead-stone-progression-spec.md`](homestead-stone-progression-spec.md)

## Research verdict

The confirmed Homestead progression slice is coherent and technically plausible, but it is not safe to
start with gameplay nodes. The current runtime proves Stone placement, persistent ZDO identity, and
server-side world reconciliation only. It does **not** provide authenticated account/character authority,
a server-owned character progression aggregate, atomic cross-aggregate receipts, a command/read-model
application seam, or recovery across separately saved world and character state.

Therefore the first implementation gate remains the Niflheim ticket-0010 P0 spike: bind one authenticated
account/character to the connection and atomically/idempotently commit one AP activity receipt across
Personal AP, Cumulative AP Earned, Mirrored Stone AP, and provenance. No Tree or node implementation may
bypass that gate.

## Sources consulted

| Source | Role in this package |
|---|---|
| Niflheim `wayfinder-stone-progression/framework-handoff.md` | Confirmed owning-repository handoff, boundaries, tracer order, maturity |
| Niflheim ticket `0011-choose-vertical-slice.md` | Exact corrected journey, 20-node roster, gameplay acceptance, decomp/corpus findings |
| Niflheim ticket `0012-ratify-framework-handoff.md` | Daniel-confirmed reconciliation and supersessions |
| Niflheim `UBIQUITOUS_LANGUAGE.md` | Canonical domain terms and state rules |
| [`homestead-stone-v1-impl-spec.md`](homestead-stone-v1-impl-spec.md) | Current placement/identity substrate and explicit non-progression boundary |
| Current `src/SBPR.Niflheim.HomesteadStones/` working tree | What actually exists now |
| [`../../design/constitution.md`](../../design/constitution.md) and ADR-0005 | SBPR-native Spec Kit workflow and gates |
| GitHub Spec Kit `spec-template.md`, `plan-template.md`, and `tasks-template.md` from `github/spec-kit` `main` | Upstream artifact shape, adapted into the semver docs tree per ADR-0005 |

The Niflheim Wayfinder already performed the vanilla wiki/decomp grounding for recipe baselines, skill
seams, cooking timers, item-instance custom data, projectile recovery, equip durations, and save behavior.
This package preserves those findings and identifies where an implementation spike must re-verify the live
build before code is written. It does not copy or commit decompiled game source.

## Current repository reality

### Existing and reusable

- `src/SBPR.Niflheim.HomesteadStones/` is a net48 BepInEx/HarmonyX sibling plugin with warnings as errors.
- The Homestead Stone gameplay root is a persistent `ZNetView` registered additively.
- Stable Stone identity is already stamped as world identity plus the host Location's explicit
  `(zoneX, zoneZ)` fields under `niflheim.homestead.*`.
- Server-side placement waits for generated Locations, globally enumerates Stone ZDOs, stamps assignment
  metadata, and reconciles stale/duplicate world instances independently of client scene visibility.
- `HomesteadStoneData.ResourceOwnerKey` is reserved, but intentionally does not define account authority.
- The current integration spec explicitly excludes progression purchases/effects and final claim/account
  policy. This progression package deepens that substrate rather than rewriting it.

### Not present

A source scan found no implementation of:

- authenticated account-to-connection or account-to-character binding;
- Bond, Attunement, relationship slots, or active sibling exclusivity;
- Personal AP, Cumulative AP Earned, Mirrored Stone AP, personal BP, or Facet Credit;
- content definitions for Foundational/Profession/Martial Facets, Trees, and nodes;
- revisioned commands, operation receipts, or a Stone progression read model;
- Tree commitment, BP-driven node development/Tree advancement, personal Offering/purchase, or revocation;
- a server-owned character progression store independent of portable `.fch` authority;
- recovery tooling for interrupted world/character mutations;
- progression UI, remote or local.

The current branch also contains uncommitted Homestead integration work. This specification pass changes
planning documents only and must not be mistaken for shipped runtime behavior.

## Technical decisions carried forward

### R-001 — Extend the existing Stone identity

Use the existing world-scoped host-zone identity as the Homestead progression key. Do not mint a parallel
progression Stone, use a live GameObject reference, substitute a ZDOID/network owner, or move ownership into
WorldZones. WorldZones remains a read-only region-fact provider for later region-scale scenarios.

### R-002 — Separate authority from progression ownership

The authenticated **account** owns connection authority, character grouping, audit, and variant-authored anti-
abuse policy. Homesteads use a unique `(account, Stone) → active character` index; Community Attunement is the
explicit exception and permits sibling characters, while Community Bond remains account-exclusive. The
**character** owns gameplay progression. Receipts bind both. No account-wide gameplay wallet is introduced.

A Valheim PlayerID or portable character save is insufficient authentication. The exact account provider is
an unresolved P0 implementation choice and must be proven before a production-shaped mutation endpoint is
accepted.

### R-003 — Separate authoritative aggregates

Use a world-scoped Stone aggregate and server-owned character aggregate, plus an account exclusivity index,
versioned content registry, operation-receipt/audit store, and derived activation view. Do not collapse these
into one UI blob or persist active effects as a second truth.

### R-004 — Journal cross-aggregate mutations

Vanilla world ZDO state and character profile state save separately; they cannot provide an atomic transaction
by themselves. AP mirroring, revocation refunds, and item issuance therefore require a
server-owned idempotency/result record and recovery protocol. Acknowledgement occurs only after the accepted
operation is durably recoverable.

This decision fixes the required property—atomic/idempotent convergence—but does not prematurely select a
file format, database, or transaction engine. That selection belongs to Gate A's spike.

### R-005 — Commands and read models are application seams

World adapters report server-validated facts. They do not mutate ledgers directly. Explicit application
commands load aggregates, authenticate, validate revisions/content/requirements, commit one operation, and
publish a bounded result. Both the temporary local panel and future Stones UI consume these same semantics.

### R-006 — Keep content data-defined and stable-ID-based

Facet, Tree, node, activity, recipe, piece, and property identity comes from current-build definitions with
stable IDs. UI strings and prefab display names are presentation only. Production migration, grandfathering,
and retired-content policy are deferred while the proof is unreleased; incompatible test data may be reset.

### R-007 — Extract shared libraries only after a second consumer

The first proof belongs in `SBPR.Niflheim.HomesteadStones`. The architecture should expose deep domain modules
and narrow Valheim adapters, but it should not create a speculative cross-mod progression framework before a
second Stone runtime actually needs one.

## Confirmed vanilla/repository seams to re-verify during implementation

| Capability | Grounded seam | Required proof before shipping |
|---|---|---|
| Persistent Stone state | Persistent ZDO fields plus global prefab enumeration already used by the Homestead runtime | Current schema, duplicate identity, restart, receipt recovery, explicit incompatible-test-data reset |
| Character custom state | Vanilla character custom data/unique keys persist, but the file is portable and not account authority | Server-owned authority, world/product scoping, reconnect and world-transfer behavior |
| Routed server operations | Existing SBPR routed request/reply patterns show full-world server queries are possible | Auth binding, revision/CAS, hostile-client attempts, bounded notifications |
| Cooking menu duration | Menu crafting uses a distinct timer from slotted world cooking | Swift Preparation affects only eligible menu-craft duration after vanilla skill adjustment |
| Field Prep | Stationless recipe eligibility is a real surface | Shared Cooking-aware Bushcraft policy, normal Cooking XP/speed/bonus output, unchanged recipe inputs/yields |
| Food replacement | Vanilla uses a remaining-duration threshold | Iron Stomach changes only the configured threshold and preserves three slots, debit, stats, and duration |
| Workmanship metadata | Exact `ItemData.m_customData` survives clone/inventory/drop/container transfer | Upgrade forwarding, tamper validation, dirty/save signaling, non-stackable restriction, safe fallback |
| Skill caps | Skill gain and the hard cap have multiple use/display/death paths | Weapon Discipline's selected cap provider: UI, gain, death loss, save/restart, values ≤100 |
| Arrow recovery | Projectile can retain exact consumed ItemData and spawn it at terminal impact | Authority, one-result guarantee, water/shield/miss/TTL/multishot cases, target-return exclusion |
| Weapon switching | Equip and unequip are queued actions with copied per-action duration | Both halves, cancellation/attack behavior, exact melee registry, armor/tool/bow/reload exclusions |
| Local placement | Piece/area/permission hooks exist | One Settlement policy plus ordinary Permission AND-gate; overlap and relationship changes |

## Mandatory spikes and research questions

### Gate A — P0 authenticated identity and atomic AP receipt

Prove all of the following before any gameplay node work:

1. A connection is bound to a stable authenticated account and one acting character.
2. A hostile client cannot substitute either identity.
3. One Foundational activity operation records input provenance and atomically converges to exactly one
   Personal AP delta, one Cumulative AP delta, and one Mirrored Stone AP delta.
4. The process is killed after each durable-write boundary; restart/retry returns one result.
5. Conflicting operation-ID reuse, stale revisions, and concurrent submissions fail without partial state.
6. Operator inspection can explain and repair/quarantine an ambiguous injected state without inventing facts.

**Decision still open:** the concrete authenticated account provider and durable transaction store. This is an
implementation spike, not a product-design question. Any candidate must satisfy the observable contract above.

### P1 unreleased content-mismatch/reset drill

- Detect a test fixture whose stable IDs/current-build definition identity no longer match.
- Reject operations rather than silently bind the state to a different Tree/node.
- Reset the disposable Homestead/character test state explicitly and rebuild the derived view.
- Do not build production migration, grandfathering, or retired-content policy in this proof.

### P1 activity-attribution adapters

For Foundational placement, Cooking, Crafting, Archer, and Warrior activity families, prove which server-side
event identifies the authenticated actor, exact source object/item, Stone Area, committed-source eligibility,
and one operation identity. Exact event granularity and anti-repetition policy remain configurable, but client
claims are never the source of truth.

### P1 outcome-specific spikes

- **Crafting item issuance:** exact-instance property/durability issuance, upgrade preservation, transfer,
  current-build tamper/unknown fallback, and explicit persistence dirtying.
- **Archer recovery:** exact-instance terminal-impact lifecycle and duplicate suppression.
- **Warrior timing/cap choice:** queued equip/unequip measurement and one irreversible idempotent cap choice.
- **Local policy:** policy transition while players are inside the Stone Area and Permission composition.

## Fixed proof values versus tuning surfaces

| Fixed by this proof | Configurable/provisional |
|---|---|
| Preconfigured Historical/Active Stone Level 2 test Homesteads | How Stone Level is ever earned |
| Profession/Martial Facets with Cooking/Crafting and Archer/Warrior palettes | Future/contextual palettes and Facets |
| 20 authored nodes; 13 executable; seven unavailable | AP/BP prices and most effect factors |
| Savor = 50% slower timer drain | Final beneficiary/balance tuning after playtest |
| Iron Stomach = refresh at 75% remaining | The mechanic itself remains replaceable before compatibility freeze |
| Swift Preparation = one-third vanilla skill-adjusted menu duration | Display name and wider Cooking progression |
| Practice Arrows = 100 for 8 Wood, 0 ammo damage | Wider ammunition registry and combat tuning |
| Field Fletching I = unchanged Wood Arrow recipe | Later Field Fletching levels |
| Refined Workshop = +1 effective Workbench level | Eligible operation/station registry expansion |

## Unsafe assumptions explicitly rejected

- “Card done” or “spec written” means behavior exists.
- ZDO network ownership equals domain ownership.
- Portable character state is authenticated account authority.
- World and character saves commit atomically.
- Client-calculated balances, requirements, timers, impacts, or item metadata are authoritative.
- Local Node cultivation is a personal AP purchase.
- Cumulative AP is a tier gate or Personal Attunement Level.
- BP is Stone-owned/shared, source-Tree-bound, target-bound, or spent into a separate Tree-level meter.
- Stone Level 2 was earned by a Workbench/Chopping Block or Mirrored AP threshold.
- Every authored node is Offered or executable.
- UI proximity is an authorization requirement.
- Runtime registration logs prove joined-client gameplay.

## Research exit criteria

The package's **specification approval gate** required:

- Daniel approval of the normative spec and unresolved-spike boundary — **recorded after cluster review**;
- an independent writer≠verifier analysis finding no contradiction with the confirmed Niflheim handoff —
  **recorded PASS after the initial six findings were corrected**;
- the data model and contracts cover every in-scope authoritative owner, mutation, rejection, replay, and lifecycle edge;
- Gate A is represented as a blocking first tracer rather than hidden setup work;
- no implementation file changed by this specification pass.

The approval gate is satisfied. Daniel separately authorized task authoring; the resulting decomposition is
proposed for review in [`homestead-stone-progression-tasks.md`](homestead-stone-progression-tasks.md). Runtime
implementation remains separately unauthorized.
