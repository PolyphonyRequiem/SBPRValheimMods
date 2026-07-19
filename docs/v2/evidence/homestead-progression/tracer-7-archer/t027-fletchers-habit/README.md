---
status: current
---

# T027 — Fletcher's Habit: exact-arrow terminal-impact recovery evidence

Node: Archer / Fletcher's Habit (Permanent Effect, personal Offered, executable).
Acceptance: `AT-FLETCHER-HIT-LIFECYCLE`, `AT-FLETCHER-NO-DUP`.

## What this node is (and is NOT)

Fletcher's Habit is the **FIRST personal Permanent Effect** to reach runtime — a
different node *shape* again from both its Archer siblings:

| | T025 Practice Range | T026 Field Fletching I | T027 Fletcher's Habit |
|---|---|---|---|
| Outcome type | Local Effect (Stone-cultivated) | Character Effect (personal Offered) | **Permanent Effect (personal Offered)** |
| Ownership/activation | Settlement Local policy AND build Permission | purchase AND active relationship | **purchase (developed) — durable, relationship-INDEPENDENT** |
| On relationship loss | dormant | dormant | **STILL OWNED (persists)** |
| Derivation | `LocalEffectActivationView` | `DerivedActivationView` (Active) | `DerivedActivationView` (**developed + purchased**) |
| Effect | Archery Target placement + Practice Arrow recipe | Wood Arrow recipe station-free | **one authoritative recovery chance for one exact eligible fired arrow** |

The load-bearing distinction: a **Permanent Effect survives relationship loss and
revocation** (spec line 130 "Permanent Effects remain active"; spec line 260 "A
released character retains Permanent Effects and Progression Keys"; US4 sc6). So
ownership is derived from the **purchase record** (developed + purchased), NOT from
the currently-active relationship the sibling Character Effect requires. There is
still **no second active-effects ledger** — ownership is re-derived from persisted
state each call (AT-NO-ACTIVE-LEDGER, carried by T004).

While owned, a fired **eligible** arrow (`ArrowWood`) that terminally impacts a
**recoverable** surface has **one authoritative** recovery chance to respawn the
**exact consumed** arrow instance — item id, quality, variant, durability, crafter,
and custom data preserved verbatim (no substitution). This is a data-provenance
recovery, not a new-arrow spawn.

## Engine-free proof (shipped, green)

Provider: `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/ProjectileRecoveryProvider.cs`
Session: `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/ProjectileRecoverySession.cs`
Tests: `tests/NiflheimFletchersHabitTests.cs` (25 facts, all passing under the net8
link-compiled suite; full suite **1390/1390**).

### AT-FLETCHER-HIT-LIFECYCLE — one authoritative result per fired instance

`Resolve(owned, provenance, surface, targetReturnWon, roll)` makes ONE decision
with a fixed, total precedence:

1. **not owned** → `NotOwned`, vanilla behaviour, nothing recovered (proven
   including the Permanent-persistence cases: purchased-with-relationship,
   purchased-**without** relationship (still owned), sibling holds the relationship
   (still owned), not-purchased (not owned), undeveloped node (not owned));
2. **ineligible arrow** (any id ≠ `ArrowWood`) → `IneligibleArrow`, vanilla;
3. **target-return suppression** (see AT-FLETCHER-NO-DUP);
4. **non-recoverable surface** (water, miss/TTL) → `NonRecoverableSurface`,
   definitively lost, **the roll does not run even at the most favourable roll**;
5. **the one configured roll** — recoverable surfaces (solid structure, ground,
   creature, shield-blocked at-rest) roll the one configurable chance
   (`FletchersHabitContent.DefaultRecoveryChance`, half-open `roll < chance`): a
   pass recovers exactly one **exact** instance (`Recovered`, count 1, provenance
   preserved field-by-field), a fail recovers nothing (`RollFailed`).

Boundary proven: `roll == chance` fails, `roll < chance` recovers (half-open).

### AT-FLETCHER-NO-DUP — at most once per instance; multishot; target-return exclusion

- **Target-return exclusion** (spec Edge case "Practice Range target return and
  Fletcher's Habit both encounter the same shot: target return wins its
  deterministic path and the permanent recovery roll does not run"): when the T025
  `PracticeRangeProvider.ResolveTargetReturn(ArcheryTarget).TargetReturnWon` flag is
  passed, the outcome is `SuppressedByTargetReturn` with nothing recovered here —
  even with full ownership and `roll == 0`. The deterministic Practice Range return
  (vanilla `ArcheryTarget.m_returnAmmo`, T025) already returned the arrow.
- **One result per instance**: `ProjectileRecoverySession.ResolveOnce(instanceId, …)`
  resolves each fired instance exactly once; a re-entrant resolution of the SAME
  instance returns `AlreadyResolved` and mints nothing.
- **Multishot**: a volley of distinct instance ids each resolves independently — a
  3-arrow volley (2 recoverable + 1 water) recovers exactly 2 exact instances with
  zero cross-instance duplication (`session.TotalRecovered == 2`).

## Runtime seam (net48, host + pure-client authoritative)

`src/SBPR.Niflheim.HomesteadStones/Features/Archer/ProjectileRecoveryGate.cs`,
registered in `Plugin.Awake` alongside the T025/T026 Archer patches.

- **`Projectile.Setup` postfix** (decomp :2811) captures, keyed on the live
  projectile via a `ConditionalWeakTable` (GC-reclaimed, no ZDO orphan), the exact
  consumed ammo provenance — **only** for the local player's own **Wood Arrow**
  shots. Ammo id is matched by the drop-prefab name (clone-suffix stripped), never a
  display string.
- **`Projectile.OnHit` postfix** (decomp :2944) is the single terminal impact. It
  classifies the surface (water → `Water`; null collider → `LostOrExpired`; an
  `ArcheryTarget` in the hit hierarchy → target-return suppression; a `Character` →
  `Creature`; else `SolidStructure`), resolves durable ownership, asks the shipped
  pure provider the one authoritative question with a single trusted
  `UnityEngine.Random.value` draw, and on `Recovered` drops the **exact consumed
  `ItemData` once** via vanilla `ItemDrop.DropItem` — additive (ADR-0006): a fresh
  dropped instance stamped with the captured provenance, never a clone of a live
  ZNetView-bearing projectile.
- **No-duplication guard**: a per-process `ProjectileRecoverySession` keyed by the
  projectile's **ZDOID** (identical across owner/RPC observations) ensures a single
  fired instance recovers at most once and a multishot volley resolves each arrow
  independently. A per-context `Resolved` flag additionally guards a re-entrant
  `OnHit` on the same projectile object.

### Ownership source — fail closed, two authoritative paths

- **Authoritative HOST**: the composed `LocalProgressionObserver.Server` holds the
  character / authority / Stone stores. The gate resolves the acting occupant's
  bound internal principal + Stone Area membership from
  `FoundationalPlacementObserver.Server` (server-owned facts, never a client claim),
  pulls the three aggregates, and asks
  `ProjectileRecoveryProvider.OwnsFletchersHabit(stone, character, authority)` —
  **developed + purchased**, relationship-independent.
- **Pure remote CLIENT**: the gate reads **only** the server-stamped
  `PersonalActivationSnapshot`'s durable **`IsOwned`** query (its `Purchased` bit),
  from the bounded `PersonalActivationClientCache` fed by the existing
  `PersonalActivationDeliveryObserver` transport (T026), refetched for the Stone the
  shooter stands in on a bounded interval. No held snapshot, a denied snapshot, or
  an unpurchased/undeveloped row ⇒ the roll never runs (vanilla behaviour). The
  client authors no entitlement.

The Permanent-vs-Character distinction is carried on the wire itself: the same
personal snapshot answers both `IsActive` (purchase AND active relationship —
Character semantics) and the new `IsOwned` (developed AND purchased, relationship-
independent — Permanent semantics), proven in `tests/NiflheimFletchersHabitTests.cs`
(a relationship-dormant snapshot reports `IsOwned == true`, `IsActive == false`).

## Joined-client / in-world artifact — status: PENDING owner clearance

Everything server-authoritative and client-consumable is verified at the unit +
layer level: the one-authoritative-result decision, exact-provenance recovery,
water/shield/miss/TTL/multishot cases, the target-return exclusion, the no-
duplication session, and both host + pure-client durable-ownership resolution paths.

The remaining human-pixel last mile — a human on a joined GUI client firing an owned
Wood Arrow into a wall and picking up the *same* arrow, firing into water and getting
nothing, firing into a Practice Range target and seeing exactly one return (no double),
and a non-owner seeing vanilla behaviour — is **left for the independent Archer
verifier (T028)** under the task safety gate. At authoring time **no user-owned
`valheim.x86_64` GUI client is running** and production worlds are never touched;
launching a QA client is gated on owner (Daniel) not actively playing. **Logs-green
is never playability**, so that in-world smoke is honestly recorded as PENDING here,
not claimed.
