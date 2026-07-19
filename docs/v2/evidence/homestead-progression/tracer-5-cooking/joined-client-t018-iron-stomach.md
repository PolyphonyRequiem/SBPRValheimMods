---
status: current
---

# T018 Iron Stomach — node-own JOINED-CLIENT live proof (Cooking node 3 of 4)

- Task: `t_6b73a3de` (QA continuation 3) — close the T018 Iron Stomach node-own
  live invariants for PR #378. Continuation of `t_0867cafd` / `t_0b072a31`.
- Acceptance under test: `AT-IRON-STOMACH-75` (spec §US4 sc1 "Iron Stomach
  permanently permits food refresh/replacement at 75% remaining").
- Branch/head proved: `feat/hs-t018-iron-stomach` @ **c547532** (PR #378).
- Raw capture: `capture/t018-iron-stomach-nodeown-live-20260719-102248.log`.

## Verdict: **FAIL — decision-grade product defect** (in-world refresh does not happen in the 0.5..0.75 band)

The `CanEat` **gate** is correctly raised to 0.75, but the actual in-world food
**refresh** in the 0.5..0.75 band silently no-ops, because vanilla
`Player.EatFood` re-checks an unpatched inner 0.5 guard. The acceptance criterion
is **not met on a live host**. This was not caught by CI because the shipped 14
unit tests + the `Player.CanEat` postfix only exercise the gate, never the
vanilla `EatFood` refresh path — the classic "logs green ≠ playable" gap.

## Topology (node-own, not reasoned)

- Host: the GABS-owned graphical QA client (PID 486027, cgroup
  `app.slice/gabs.service`, parent `gabs server`) hosting the isolated world
  **`T018IronQA`** with character `Developer` (Odev). `IsServer=True`,
  local playerID **974561124**, scene `main`. Production Niflheim (2456) and
  Heistan (2466) untouched throughout.
- **Byte-identical head proof:** the `SBPR.Niflheim.HomesteadStones.dll` loaded
  in the live client is md5 `e3e4d19aab5167c7d7a14fd13d021de2`, identical to a
  fresh Release build of the PR #378 worktree @ c547532. The live process runs
  the exact reviewed assembly.
- Instrument: a throwaway compiled BepInEx-free helper `SBPR.QADiag.T018`
  (references the shipped SBPR public surface + base-game types) invoked via ONE
  tiny `run_script` (`Assembly.LoadFrom(...).GetMethod("Run").Invoke`) so the
  heavy establishment + live gate sweep never compiles through the in-client
  Mono evaluator (the path that wedged the main thread in three prior runs). It
  establishes a durable Iron Stomach purchase into the LIVE composed
  `LocalProgressionServer.Characters`, binds the acting session
  (`BoundSessions.Bind("player:974561124", …)`, BoundCount=1), then drives the
  REAL `Player.CanEat` / `Player.EatFood` gates and reads back the live food
  slots + health/stamina/eitr. Food slots restored afterward.

## What was OBSERVED (live)

| Phase | Check | Result |
|-------|-------|--------|
| A DORMANT | provider Acquired=False, Threshold=0.5 | PASS |
| A DORMANT | live `CanEat` sweep: False ≥0.51, True ≤0.50 (exact vanilla 0.5, fails closed) | PASS |
| B ACTIVE | durable purchase established; provider Acquired=True, Threshold=0.75 | PASS |
| B ACTIVE | live `CanEat` sweep: **False 0.80/0.76, True 0.75/0.74/0.60/0.51/0.50/0.49** (boundary-inclusive 0.75, deny-above) | PASS |
| C SLOTS | 3 distinct foods filled; `CanEat(4th distinct)=False`, `EatFood(4th)=False`, count stays 3 (no fourth slot) | PASS |
| D DEBIT/DUR | `CanEat(0.60)=True` but **`EatFood=False`, food NOT refreshed** (m_time stays 540/900; health/stam unchanged) | **FAIL** |
| E CONTROL | same `EatFood` at 0.40 (vanilla band) DOES refresh (m_time→899≈900) → Phase-D FAIL is a real seam gap, not a harness artifact | PASS |

So three of the four T018 items — the raised **CanEat threshold** (0.75,
boundary-inclusive, deny-above, durable/host-authoritative, fails-closed
dormant), the **three food slots / no fourth slot**, and the **single-slot**
refresh shape — hold live. The fourth — an actual **refresh** at up to 75%
remaining that applies the food's normal debit/stats/duration — does **not**.

## Root cause

`Features/Cooking/IronStomachRefreshGate.cs` is a Harmony **postfix on
`Player.CanEat(ItemDrop.ItemData, bool)` only**. Vanilla, however, decides the
refresh in two independent places:

1. `Player.CanEat` — the outer "may I (re-)eat this?" gate. **Patched.** Now
   returns true down to 0.75 remaining for a durable-Iron-Stomach local occupant.
2. `Player.EatFood` (decomp `assembly_valheim` 17462) — after its own internal
   `CanEat` check it loops the food slots and, for a matching food, gates the
   real refresh on **`food2.CanEatAgain()`** (decomp 15335) ==
   `m_time < m_foodBurnTime / 2f` — the hardcoded **0.5** threshold. **Not
   patched.** In the 0.5..0.75 band this returns false, so `EatFood` returns
   without resetting `m_time/health/stamina/eitr`.

Because the real consume path `Humanoid.ConsumeItem` (decomp 20930) calls
`EatFood(item)` and then `inventory.RemoveOneItem(item)` **unconditionally**, a
player in the 0.5..0.75 band who eats a matching food **loses the item from
inventory but gets no refresh** — strictly worse than a no-op.

## Fix direction (spec + code + SpecCheck move together — AGENTS.md)

The seam must also raise the refresh threshold inside the **actual refresh
path**, not just the gate: e.g. a transpiler/prefix on `Player.EatFood` (or a
patch of `Player.Food.CanEatAgain`) that, for a durable-Iron-Stomach local
occupant, permits and performs the in-band refresh (reset m_time/health/stamina/
eitr) — while still preserving the three-slot cap and touching nothing else. The
spec and `tests/NiflheimIronStomachTests.cs` must add an **EatFood-level**
acceptance test (a gate-only test cannot catch this class of defect). This is a
focused engineer remediation, tracked as a separate card; PR #378 must not merge
as-is.
