---
status: current
---

# T018 Iron Stomach — food refresh-threshold proof (Cooking node 3 of 4)

- Task: `t_0b072a31` — T018 [US4] Implement Iron Stomach as a durable
  refresh-threshold provider. Acceptance: `AT-IRON-STOMACH-75`.
- Branch: `feat/hs-t018-iron-stomach` (off `origin/main@d5256ac`, which already
  contains the merged T017 Field Prep Cooking policy `d5256ac` and the T016
  Cooking adapter surface).
- Safety: pre-work check for a user-owned graphical `valheim.x86_64` found NONE
  (only the persistent dedicated `valheim_server.x86_64` infra + a Steam desktop
  with no game running). No user session altered.
- Fresh net48 DLL md5 (this run, HomesteadStones):
  `2095d1241191a171ac1f4d7ff1766fc3` (remediation two-seam build; the original
  single-seam PR #378 DLL was `4e82326612bda16e21a903440f6d5748`).

## Verdict: PASS (provider + corrected two-seam delivery layer verified) — in-world refresh-at-75% last mile REASONED, to be observed at the QA/T020 rerun

> **Superseded delivery-seam note:** the ORIGINAL single-seam cut (CanEat postfix
> only, PR #378 head `c5475327`) FAILED node-own live QA — see the
> "Remediation: EatFood inner-guard" section below, which is the current,
> corrected two-seam (CanEat postfix + EatFood prefix) state.

Iron Stomach is a personal **Permanent Effect** that, once durably acquired,
permanently raises the vanilla food refresh/replacement threshold from 50%
remaining to **75% remaining** (spec §US4 sc1), while preserving the three food
slots and the normal food debit, stats, and duration. Being a Permanent Effect
it **survives relationship loss and Tree revocation** (data-model.md
§CharacterProgression) — the raised threshold keys on the character's durable
purchase record ALONE, with no relationship / Settlement-policy / build-Permission
/ Stone-node-development conjunct.

This run verifies the layers a headless box can decisively prove, and states
honestly which last mile is client-only.

## Remediation: EatFood inner-guard (t_854b4ed8) — supersedes PR #378

The first cut (PR #378, exact head `c5475327`) FAILED node-own live QA
(`t_6b73a3de`; raw capture
`docs/v2/evidence/homestead-progression/tracer-5-cooking/capture/t018-iron-stomach-nodeown-live-20260719-102248.log`).

### Defect
The shipped seam patched ONLY the outer `Player.CanEat`. Vanilla
`Player.EatFood` INDEPENDENTLY re-checks `Player.Food.CanEatAgain()`
(`m_time < m_foodBurnTime / 2f`, i.e. remaining `< 0.5`) inside its same-food
branch. So with a durable Iron Stomach active at ~60% remaining:

- `Player.CanEat` was rescued to **True** (the postfix), so
  `Player.CanConsumeItem` passed and `Humanoid.ConsumeItem` DEBITED the item via
  `inventory.RemoveOneItem`;
- but `Player.EatFood` re-checked `CanEatAgain()` at the hardcoded 0.5, returned
  **False**, and did NOT refresh `m_time`/health/stamina/eitr.

Net: the food was consumed with no refresh — a no-loss violation. The 40%
control refreshed correctly (vanilla already permits `< 0.5`). Acceptance
`AT-IRON-STOMACH-75` was therefore unplayable as shipped.

### Fix
- **Kept** the `Player.CanEat` postfix — it correctly unlocks the
  `CanConsumeItem` entry only in the 0.5..0.75 band and does NOT rescue above
  0.75, so a too-fresh food is denied at `CanConsumeItem` and no item is ever
  debited.
- **Added** a `Player.EatFood` **prefix** that, when the durable Iron Stomach
  projection reports the matching same-food is in the raised, inclusive
  `[0.5, 0.75]` band, performs EXACTLY the refresh vanilla runs below 0.5 (reset
  the matching slot's `m_time`/`m_health`/`m_stamina`/`m_eitr` from the item,
  then `UpdateFood(0f, forceUpdate: true)`), reports success, and skips vanilla
  so the single `ConsumeItem` debit proceeds exactly once. Below 0.5 (vanilla
  refreshes), above 0.75 (vanilla denies), the new-food/three-slot path, and the
  fail-closed no-purchase case all pass through to unchanged vanilla.
- Both patches delegate the band decision to the single engine-free authority
  `FoodRefreshThresholdProvider.DecideEat(...)` → `IronStomachEatDisposition`.

### Preserved invariants
Deny above 75%; dormant/non-owner/pure-client vanilla 0.5 (fail-closed);
exactly three food slots / no fourth (the new-food and most-depleted paths are
untouched — the prefix mutates only an ALREADY-PRESENT matching slot); normal
one-item debit; normal health/stamina/eitr/duration reset (the identical fields
vanilla's below-0.5 branch writes); Permanent-Effect durability through
relationship loss / Tree revocation / restart (keyed on the durable purchase
alone).

### Red-first tests (the layer the shipped suite missed)
`tests/NiflheimIronStomachTests.cs` gains **8** inner-guard tests (22 total) that
exercise the real `DecideEat` decision path, not only the provider/`CanEat`
logic: the 60% rescue (the exact live failure), the 40% control pass-through, the
without-Iron-Stomach fail-closed pass-through, the above-0.75 vanilla-deny
pass-through, the exact band boundaries (inclusive 0.5 floor and 0.75 ceiling),
the no-matching-food slots-untouched case, the restart-durable rescue, and
capability/character overload agreement.

### Gates (this run)
- Full suite: **1451 / 1451**.
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0w / 0e**; net48
  `SBPR.Trailborne` Release: **0w / 0e**.
- Stone-content workbench: **59 / 59**.
- `python3 scripts/docs-lint.py`: **OK — 214 docs checked**.
- `git diff --check`: **clean**. SpecCheck recipe manifest: **unchanged**.

Supersedes PR #378. The in-world 75%-refresh proof (host occupant observing a
food become re-eatable at up to 75% remaining, with the item actually refreshed
and debited once, while a non-acquiring occupant cannot) is the fresh serialized
`qa-playtest` continuation on isolated GABS topology — this record marks the
corrected code + tests + docs landed under review, not gate sign-off.

## What was VERIFIED

### Build + suite (this run)
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full test suite: **1379 / 1379 passed** (baseline 1365 + 14 new Iron Stomach
  provider tests).
- `python3 scripts/docs-lint.py`: **OK — 204 docs checked**.
- `git diff --check`: **clean**.
- SpecCheck recipe manifest: **unchanged** (Iron Stomach ships no SBPR recipe).

### Pure provider layer (`FoodRefreshThresholdProvider`, 14 tests)
The engine-free provider is the single authority for the threshold decision, and
its behavior is pinned by `tests/NiflheimIronStomachTests.cs`:
- A durably-acquired Iron Stomach (purchase record, outcome class
  `PermanentEffect`, exact `IronStomach@1` node identity) raises the threshold to
  **0.75**; without it the threshold is the vanilla baseline **0.5**.
- "Highest applicable provider wins" is a MAXIMUM composition: 0.5 ⊔ 0.75 = 0.75,
  and a stronger baseline (0.9) is never lowered by the 0.75 candidate; the
  no-candidate case returns the safe 0.5 floor, never a fabricated grant.
- Refresh is permitted at exactly **75% remaining** (boundary-inclusive) and
  denied just above it; in the 0.5..0.75 band only Iron Stomach refreshes, below
  0.5 both do, above 0.75 neither does.
- **Durability** (the load-bearing Permanent-Effect property): the raised
  threshold survives relationship loss (the provider takes no authority argument
  at all), survives a serialized-restart round-trip of the character aggregate,
  and survives Tree revocation of development (the provider reads no Stone
  aggregate — there is no development conjunct to lose).
- A same-Stone Field Prep **Character-Effect** purchase is never mistaken for
  Iron Stomach (exact node-identity + outcome-class match).
- The three food slots (`FoodSlots == 3`) and normal debit/stats/duration
  (`PreservesNormalDebitStatsDuration == true`) are preserved untouched.

### Delivery-seam wiring (`IronStomachRefreshGate`, net48) — corrected two-seam
The net48 seam that makes the raised threshold manifest on a joined client is
TWO coordinated Harmony patches (armed in `Plugin.cs` via
`harmony.PatchAll(typeof(Features.Cooking.IronStomachRefreshGate))`).
Decomp basis (vanilla is fair game to read/adapt, AGENTS.md / ADR-0001): vanilla's
per-food refresh predicate is `Player.Food.CanEatAgain()` == `m_time <
m_foodBurnTime / 2f` (remaining fraction < 0.5, where `m_time` counts down from
`m_foodBurnTime` — verified in `assembly_valheim.dll` `Player.UpdateFood`, which
decrements `food.m_time -= 1f` each tick), and this SAME predicate is re-checked
independently inside `Player.EatFood`'s same-food branch. The seams:
- **`Player.CanEat` postfix** — rescues a vanilla FALSE to TRUE **only** when the
  refusal was caused by an ALREADY-PRESENT matching food whose remaining fraction
  is in the 0.5..0.75 band, unlocking the `Player.CanConsumeItem` entry. Above
  0.75 it does NOT rescue, so `CanConsumeItem` denies and no item is debited.
- **`Player.EatFood` prefix** — the inner guard. Because vanilla `EatFood`
  re-checks `CanEatAgain()` at 0.5, rescuing `CanEat` alone left `EatFood`
  refusing the refresh above 50% while `ConsumeItem` debited the item anyway
  (the PR #378 defect). The prefix performs EXACTLY vanilla's below-0.5 same-food
  refresh (reset the matching slot's `m_time`/`m_health`/`m_stamina`/`m_eitr`
  from the item, `UpdateFood(0f, forceUpdate: true)`) when the durable Iron
  Stomach projection reports the food in the inclusive `[0.5, 0.75]` band, then
  skips vanilla and reports success so the one-item debit proceeds exactly once.
- both never override a vanilla PASS below 0.5, never touch the `m_foods.Count
  >= 3` three-slot / new-food / most-depleted paths (slots preserved), and mutate
  only the ALREADY-PRESENT matching slot (debit/stats/duration otherwise vanilla);
- both resolve the acquired verdict from the authoritative **host projection**
  (`LocalProgressionObserver.Server`'s character store, keyed to the bound
  internal principal) via the shipped pure `FoodRefreshThresholdProvider` — no
  client-supplied claim is trusted, fail-closed on any resolution gap.

## Honest scope — what is NOT yet observed in-world here

Iron Stomach is a personal Permanent Effect, and the bounded server→client
delivery transport that Savor / Practice Range / Refined Workshop use carries
LOCAL-effect snapshots only — there is not yet a personal-effect replication
channel. So the seam reads the authoritative projection where it EXISTS
in-process: on the authoritative **host** (listen-server / singleplayer host) the
composed server holds the character store and the gate resolves the durable
purchase directly. On a **pure remote client** the server runtime is null and the
gate **fails closed** (foods keep the vanilla 0.5 threshold) rather than inventing
an unauthenticated grant. The proven topology for T018 is therefore the host
occupant; a personal-effect client delivery channel is a separate follow-up,
exactly as the sibling Field Prep / Field Fletching / Refined Workshop seams
documented their host-only scope.

The in-world last mile — a host occupant with a durable Iron Stomach observing a
food become re-eatable at up to 75% remaining while a non-acquiring occupant
cannot — is the node's own joined-client artifact, to be captured at the
independent Tracer-5 verification (T020) on the isolated throwaway-server topology
the sibling Cooking nodes used. This box marks code + tests + docs landed under
review with the seam armed, not gate sign-off.

## Spec/code synchronization (AGENTS.md "the one rule")

- `docs/v2/planning/homestead-stone-progression-tasks.md` — T018 checkbox checked
  with a full landing note (this PR).
- No change to the data-model roster (Iron Stomach was already authored as
  `Cooking | 1 | Iron Stomach | Permanent Effect | personal Offered`), the
  contracts `FoodRefreshThresholdProvider` entry (already specified threshold
  0.75 / highest-wins / three-slots-and-debit-preserved), or the SpecCheck recipe
  manifest (no SBPR recipe). Code implements the already-locked spec; the two
  agree.
