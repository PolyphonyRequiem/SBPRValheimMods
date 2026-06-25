---
title: "Twisted Portal cost model — food-as-fuel (Portal Energy v3 tuning baseline)"
status: accepted
purpose: "Locks the v3 Twisted Portal travel-cost model: NO key trinket. Teleport range is gated by the food in the player's belly — Portal Energy (PE) = remaining-food-minutes x a stat-derived tier, summed across the belly's food slots; a jump spends PE as food-time, so distance both costs provisioning AND lands you depleted. Tier is computed from a food's total stat budget (Health+Stamina+Eitr), tier = round(clamp(total/30, 1, 5) * 2) / 2, snapped to 0.5 rungs, range [1.0, 5.0]. This is the authoritative stat-fallback so vanilla and modded/unaccounted foods slot in with zero hand-authoring; an optional explicit override registry may pin individual foods later. Feasts run on a separate normalized ~28 m range clock (their real 50 m buff timer is untouched) so they land slightly under personal crafted meals for travel. Daniel locked this 2026-06-24 as a tuning baseline (maths may be retuned; the architecture is fixed). SUPERSEDES the trinket-key/durability charge economy in nomap.md §7 and the food-charged-key premise in docs/v3/planning/twisted-portal-impl-spec.md (resolves that spec's open decision #2, 'charge economy')."
owner: Daniel (design authority); Starbright (synthesis + grounding)
supersedes_partial:
  - "docs/design/nomap.md §7 (:146, :154-158) — the 'charged accessory burns durability per teleport, food restores durability, bukeberries purge-accelerate' trinket-key economy is REPLACED by food-as-fuel. No SBPR_TwistedKey item; no EatFood/RemoveOneFood durability hooks for travel charge. The portal class, rune-name pairing, NoPortals bypass, and through-terrain overlay from §7 are UNAFFECTED."
  - "docs/v3/planning/twisted-portal-impl-spec.md — the food-charged TRINKET key premise and its open decision #2 ('charge economy'); the cost model is now belly-food PE, not key durability. Range-query, rune-name pairing, and NoPortals-bypass architecture in that spec remain valid."
---

# Twisted Portal cost model — food as fuel

> **Status: ACCEPTED — Daniel locked the architecture 2026-06-24 as a tuning
> baseline.** The *shape* (food-as-fuel, stat-derived tier, PE = minutes × tier,
> feasts normalized + slightly under) is fixed. The *numbers* (the `/30` divisor,
> the `[1,5]` clamp, the feast `28 m` cap, eitr weighting) are explicit tuning
> knobs that may be retuned without reopening the design. The buildable *how* —
> exact vanilla hooks for reading per-slot food time, the teleport-cost debit —
> graduates to `docs/v3/planning/twisted-portal-impl-spec.md` once this is built
> against. All food numbers below are pulled from the SBPR wiki corpus
> (`~/valheim/sbpr-corpus`), not estimated.

## 1. The core idea — provisioning is the gate, not a key

There is **no key trinket** (a permissions system may come later; it is a
distinct concern). How far you can teleport is determined by **the food in your
belly**. Each active food slot contributes **Portal Energy (PE)**:

```
PE(slot)   = remaining_minutes(slot) × tier(food)
PE(player) = Σ PE(slot)   over all active food slots
```

A teleport **spends PE**, debited as **food-time removed from your belly**,
scaled by jump distance. Two consequences fall out of this for free, and both
are load-bearing design, not side effects:

- **Cook better → travel farther.** Eat-to-survive *becomes* fast-travel; the
  cooking loop and the travel loop fuse into one. Very Viking: you pack
  provisions for the journey.
- **Distance = how depleted you arrive.** Because a jump eats food-time, a long
  haul pushes your foods toward empty — so you land with buffs bottoming out,
  weak. Short hops stay cheap in both range and readiness; long hauls cost you
  both. The arrival penalty is built in, not bolted on.

## 2. Tier — derived from total stats (the stat fallback IS the system)

Tier is a multiplier in **[1.0, 5.0]** computed from a food's **total stat
budget** = Max Health + Max Stamina + Eitr:

```
tier(food) = round( clamp(total_stats / 30, 1.0, 5.0) × 2 ) / 2
```

- **`/30`** sets the slope: ~30 stat-points per whole tier.
- **`clamp(…, 1, 5)`** sets the floor (raised from an earlier 0.5 — a 0.5 floor
  made forage a 10× penalty vs the 5× the rest of the ladder uses; punishingly
  steep) and the ceiling.
- **`round(… × 2)/2`** snaps to **0.5 rungs**, so foods fall into legible travel
  *classes* (round PE numbers, modded food visibly joins a vanilla rung) instead
  of 60 unique noisy values.

**Why stats, not hand-authored complexity:** making stats the driver means the
fallback *is* the system — every vanilla food's tier is computed, and every
modded/unaccounted food gets the identical treatment for **zero hand-authoring**.
This is the "stat-based fallback for modded foods" requirement, generalized into
the primary rule. An **optional explicit override registry** (`foodID → tier`)
may later pin individual foods that the formula mis-prices, but it is not
required for the system to function — the default needs no maintenance.

**Consequence Daniel chose knowingly:** under stat-tiering, a *powerful but
simple* late-game cooked meat (e.g. Cooked bonemaw, 120 stats → tier 4.0,
PE 100) out-travels a mid-game crafted pie. This rewards **progression** (better
food = farther) over **cooking effort** (a chef out-traveling a lazy player).
The progression reading is the locked intent.

## 3. Portal Energy ranking — personal foods (full belly)

PE at full remaining duration, sorted high→low. `tier = round(clamp(stats/30,1,5)×2)/2`.

```
Food                   Stats  Dur  Tier     PE
Marinated greens         143   30   5.0  150.0
Piquant pie              140   30   4.5  135.0
Roasted crust pie        134   30   4.5  135.0
Seeker aspic             127   30   4.0  120.0
Mashed meat              134   25   4.5  112.5
Sparkling shroomshake    135   25   4.5  112.5
Serpent stew             106   30   3.5  105.0
Blood pudding            100   30   3.5  105.0
Lox meat pie              99   30   3.5  105.0
Honey glazed chicken     106   30   3.5  105.0
Meat platter             106   30   3.5  105.0
Misthare supreme         113   25   4.0  100.0
Mushroom omelette        113   25   4.0  100.0
Yggdrasil porridge       120   25   4.0  100.0
Cooked bonemaw meat      120   25   4.0  100.0
Fiery svinstew           127   25   4.0  100.0
Spicy marmalade          120   25   4.0  100.0
Scorching medley         127   25   4.0  100.0
Sizzling berry broth     127   25   4.0  100.0
Frosted sweetbread        86   30   3.0   90.0
Salad                    106   25   3.5   87.5
Stuffed mushroom         112   25   3.5   87.5
Cooked serpent meat       93   25   3.0   75.0
Wolf skewer               86   25   3.0   75.0
Eyescream                 86   25   3.0   75.0
Bread                     93   25   3.0   75.0
Fish wraps                93   25   3.0   75.0
Sausages                  73   25   2.5   62.5
Turnip stew               73   25   2.5   62.5
Wolf jerky                66   30   2.0   60.0
Cooked volture meat       94   20   3.0   60.0
Cooked asksvin tail       94   20   3.0   60.0
Deer stew                 60   25   2.0   50.0
Minced meat sauce         53   25   2.0   50.0
Carrot soup               60   25   2.0   50.0
Onion soup                80   20   2.5   50.0
Cooked chicken meat       80   20   2.5   50.0
Cooked seeker meat        80   20   2.5   50.0
Cooked hare meat          80   20   2.5   50.0
Boar jerky                46   30   1.5   45.0
Cooked bear meat          53   20   2.0   40.0
Cooked fish               60   20   2.0   40.0
Black soup                67   20   2.0   40.0
Muckshake                 66   20   2.0   40.0
Cooked wolf meat          60   20   2.0   40.0
Cooked lox meat           66   20   2.0   40.0
Magecap                   75   15   2.5   37.5
Cooked deer meat          47   20   1.5   30.0
Onion (raw)               53   15   2.0   30.0
Cloudberries              53   15   2.0   30.0
Fiddlehead                60   15   2.0   30.0
Honey                     43   15   1.5   22.5
Carrot (raw)              42   15   1.5   22.5
Jotun puffs               50   15   1.5   22.5
Grilled neck tail         33   20   1.0   20.0
Mushroom                  30   15   1.0   15.0
Yellow mushroom           40   10   1.5   15.0
Royal jelly               30   15   1.0   15.0
Raspberries               27   10   1.0   10.0
Blueberries               33   10   1.0   10.0
```

Tier distribution (60 foods): 5.0×1 · 4.5×4 · 4.0×9 · 3.5×7 · 3.0×8 · 2.5×7 ·
2.0×13 · 1.5×6 · 1.0×5. A ~15× spread floor→ceiling (PE 10 → 150).

## 4. Feasts — separate clock, slightly under

Every vanilla feast is **50 m duration, flat** — that number encodes
*convenience* (a raid doesn't re-eat mid-defense; shareable across 10 servings),
**not** earned power. Feeding raw 50 m into `minutes × tier` would (a) hand
feasts a fuel tank 67% over the 30 m personal-food ceiling, and (b) flatten
progression, since a starter feast and an endgame feast share the same 50 m.

**Fix — two clocks that diverge only for feasts:**

- **Buff clock: 50 m, untouched.** Feasts stay excellent combat/sustain food.
- **Range clock: normalized to ~28 m** (the `FEAST_RANGE_CAP` knob), slightly
  under the 30 m personal ceiling. Feast *range* then progresses through the
  stat-derived tier ladder, not the flat duration. The depletion-coupling (§1)
  still applies — teleporting drains the range clock; you still arrive weaker.

```
Feast                            Stats  Tier  rngMin    PE
Whole roasted Meadow boar           70   2.5     28   70.0
Black Forest buffet platter         70   2.5     28   70.0
Swamp dweller's delight             70   2.5     28   70.0
Sailor's bounty                     90   3.0     28   84.0
Hearty Mountain logger's stew       90   3.0     28   84.0
Plains pie picnic                  110   3.5     28   98.0
Mushrooms galore a la Mistlands    163   5.0     28  140.0
Ashlands gourmet bowl              188   5.0     28  140.0
```

Best feast (Ashlands gourmet bowl, PE 140) stays **under** the best personal
food (Marinated greens, PE 150). So **the travel-optimal pick is always a
personal meal** — "just eat feast for travel" never wins, and feasts keep their
combat/sharing identity. "Slightly under" holds across the ladder.

## 5. Open tuning knobs (baseline locked, numbers live)

These do **not** reopen the architecture; they are dials for playtest:

1. **Eitr weighting.** Eitr counts at full weight in `total`, so mage foods rank
   highest for travel (Marinated greens is the lone 5.0). If "a mage shouldn't be
   the best traveler purely because eitr is a big number" matters, weight eitr
   below HP/Stamina. *Baseline: full weight.*
2. **Feast cap (`FEAST_RANGE_CAP`).** 28 m ≈ 7% under the 30 m ceiling (slight).
   Drop toward ~24 m for a steeper ~20% feast travel penalty. *Baseline: 28 m.*
3. **The `/30` divisor & `[1,5]` clamp.** Slope and floor/ceiling of the tier
   curve. *Baseline: `/30`, clamp [1,5].*
4. **Override registry.** If specific foods mis-price, pin them in an explicit
   `foodID → tier` table that wins over the formula. *Baseline: none needed.*

## 6. What this supersedes

This **replaces the trinket-key charge economy** described in
[`nomap.md` §7](nomap.md) (the `SBPR_TwistedKey` durability accessory, the
`EatFood`/`RemoveOneFood` charge hooks, the bukeberry purge-accelerator) and the
food-charged-key premise in
[`../v3/planning/twisted-portal-impl-spec.md`](../v3/planning/twisted-portal-impl-spec.md),
resolving that spec's open decision #2 ("charge economy"). Everything else in §7
and the impl spec — the distinct portal class, rune-name pairing, the `NoPortals`
bypass, the through-terrain name overlay, server-side pairing — is **unaffected**.
