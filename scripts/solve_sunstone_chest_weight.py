#!/usr/bin/env python3
"""Solve the Sunstone chest DropData m_weight from Daniel's locked 15%-per-chest target.

Card t_0445f590. The vanilla swamp-surface chest (TreasureChest_swamp) uses a WEIGHT-based
DropTable sampled WITHOUT replacement (m_oneOfEach), drawing m_dropMin..m_dropMax items
(DropTable.GetDropListItems, assembly_valheim :56456). So "15%" is NOT a weight and NOT a
per-slot fraction — it is the probability a freshly-populated chest contains >=1 Sunstone.

Live table (read from the real client asset, RE t_5e9a4d49 + re-probe on card t_0445f590):
    m_dropChance = 1.0, m_dropMin = 2, m_dropMax = 3, m_oneOfEach = True
    10 entries, total weight 9.5: 9 items at weight 1.0 + WitheredBone at weight 0.5.

This script re-derives SunstoneLoot.ChestSunstoneWeight (~0.584) two independent ways —
an exact recursion and a faithful Monte-Carlo of GetDropListItems — and prints a table for
the impl spec (docs/v3/planning/sunstone-loot-economy-impl-spec.md §3). Base-game math only;
no game assets are read at runtime (the table shape is the documented constant above).

Usage:  python3 scripts/solve_sunstone_chest_weight.py
"""
import random

# Live TreasureChest_swamp DropTable shape (verified vs the real client asset).
BASE_WEIGHTS = [1.0] * 9 + [0.5]   # 9 commodity items + WitheredBone rare-tail
TOTAL_BASE = sum(BASE_WEIGHTS)     # 9.5
DROP_MIN, DROP_MAX = 2, 3          # "2-3 loot, max one of each"
TARGET_PER_CHEST = 0.15            # Daniel's lock (card t_8f39b5fc, 2026-06-18)


def p_not_picked(remaining, w, k):
    """Exact P(Sunstone never picked in k weighted draws without replacement).

    remaining: tuple of the non-Sunstone weights still in the pool.
    w: the Sunstone weight (always present until picked; we only recurse the not-picked branch).
    """
    if k == 0:
        return 1.0
    total = sum(remaining) + w
    acc = 0.0
    for i, wi in enumerate(remaining):
        rest = remaining[:i] + remaining[i + 1:]
        acc += (wi / total) * p_not_picked(rest, w, k - 1)
    # the (w/total) branch is "Sunstone picked" -> contributes 0 to "not picked".
    return acc


def per_chest_prob(w):
    """Exact per-chest P(>=1 Sunstone), averaged over uniform k in {DROP_MIN..DROP_MAX}."""
    base = tuple(BASE_WEIGHTS)
    ks = range(DROP_MIN, DROP_MAX + 1)
    return sum(1.0 - p_not_picked(base, w, k) for k in ks) / len(list(ks))


def solve_weight(target=TARGET_PER_CHEST):
    lo, hi = 0.0, 5.0
    for _ in range(100):
        mid = (lo + hi) / 2
        if per_chest_prob(mid) < target:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def monte_carlo(w, trials=2_000_000):
    """Faithful sim of DropTable.GetDropListItems for cross-validation."""
    drops = BASE_WEIGHTS + [w]
    sunstone_idx = len(drops) - 1
    hits = 0
    for _ in range(trials):
        pool = list(enumerate(drops))
        n = random.randint(DROP_MIN, DROP_MAX)
        got = False
        for _ in range(n):
            tot = sum(wt for _, wt in pool)
            r = random.uniform(0, tot)
            run = 0.0
            for j, (idx, wt) in enumerate(pool):
                run += wt
                if r <= run:
                    if idx == sunstone_idx:
                        got = True
                    pool.pop(j)   # m_oneOfEach: sample without replacement
                    break
        if got:
            hits += 1
    return hits / trials


if __name__ == "__main__":
    w = solve_weight()
    print(f"Solved m_weight = {w:.5f}  ->  exact per-chest P = {per_chest_prob(w):.5f}  "
          f"(target {TARGET_PER_CHEST})")
    print(f"Monte-Carlo check at w={w:.5f}: per-chest P = {monte_carlo(w):.5f}\n")
    print(f"{'weight':>8} {'per-chest %':>12} {'per-slot frac %':>16}")
    for ww in [0.5, 0.584, 0.65, 1.0, 1.676]:
        slot = 100 * ww / (TOTAL_BASE + ww)
        print(f"{ww:8.3f} {per_chest_prob(ww) * 100:12.2f} {slot:16.2f}")
    print("\nNote: 1.676 is the weight for a 15% per-SLOT fraction (~35% per chest) — the trap. "
          "Daniel's 15% is PER CHEST -> 0.584.")
