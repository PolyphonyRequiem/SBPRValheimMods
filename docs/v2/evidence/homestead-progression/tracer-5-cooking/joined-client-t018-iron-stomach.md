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
  `4e82326612bda16e21a903440f6d5748`.

## Verdict: PASS (provider + delivery-seam layer verified) — in-world refresh-at-75% last mile REASONED, to be observed at the QA/T020 rerun

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

### Delivery-seam wiring (`IronStomachRefreshGate`, net48)
The net48 seam that makes the raised threshold manifest on a joined client is a
Harmony **postfix on `Player.CanEat(ItemDrop.ItemData, bool)`** (armed in
`Plugin.cs` via `harmony.PatchAll(typeof(Features.Cooking.IronStomachRefreshGate))`).
Decomp basis (vanilla is fair game to read/adapt, AGENTS.md / ADR-0001): vanilla's
per-food refresh predicate is `Player.Food.CanEatAgain()` == `m_time <
m_foodBurnTime / 2f` (remaining fraction < 0.5, where `m_time` counts down from
`m_foodBurnTime` — verified in `assembly_valheim.dll` `Player.UpdateFood`, which
decrements `food.m_time -= 1f` each tick). The postfix:
- rescues a vanilla FALSE to TRUE **only** when the refusal was caused by an
  ALREADY-PRESENT matching food whose remaining fraction is at/below the Iron
  Stomach threshold (0.75) — i.e. exactly the 0.5..0.75 "refresh at 75%
  remaining" band;
- never overrides a vanilla PASS, never touches the `m_foods.Count >= 3`
  three-slot "different food, slots full" refusal (slots preserved), and mutates
  no `m_time`/`m_health`/`m_stamina`/`m_eitr` (debit/stats/duration run entirely
  in vanilla `EatFood`);
- resolves the acquired verdict from the authoritative **host projection**
  (`LocalProgressionObserver.Server`'s character store, keyed to the bound
  internal principal), routing through the shipped pure provider — no
  client-supplied claim is trusted.

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
