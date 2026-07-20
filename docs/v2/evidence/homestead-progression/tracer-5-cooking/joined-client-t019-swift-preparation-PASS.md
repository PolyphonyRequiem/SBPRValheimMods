---
status: current
---

# T019 Swift Preparation — node-own joined-client real-transpiled-path artifact (Tracer-5, Cooking 4/4)

- Task: `t_6460227c` (LIVE QA CONTINUATION 4). Parent remediation `t_9a0f054c`.
- PR: #394. Exact head under test: **37711cb34ef7cb641c18ac5f636034b72e3d9a62**
  (`fix(cooking): T019 remediation — resolve selected Recipe via Traverse.Property not Field`).
- Acceptance: `AT-SWIFT-MENU-ONLY`, `AT-COOKING-TIER2`, `AT-NO-COOKING-COMPLETION`
  (spec §US4 sc1 — eligible menu-crafted food takes 1/3 of the vanilla skill-adjusted
  menu-craft duration, applied strictly AFTER the vanilla Cooking-skill adjustment).

## Verdict: PASS — decision-grade

The 1/3 Swift Preparation menu-craft effect is delivered in-world on the REAL, live,
SBPR-transpiled `InventoryGui.UpdateRecipe` path for an eligible active Cooking Tier-2
host occupant, and every non-eligible / non-owner / dormant / non-menu path is left at
the exact vanilla skill-adjusted duration. Completion count is unchanged (one craft →
one completion). This is not a provider call, not IL inspection, not a synthetic timer:
the instrument drives the private `InventoryGui.UpdateRecipe(player, dt)` — which SBPR
has transpiled — and reads back the shipped `num5` from `m_craftProgressBar.m_maxValue`,
i.e. the exact value the vanilla completion comparison (`m_craftTimer >= num5`) uses.

## Environment / ownership (re-proven at execution)

- Isolated task-owned graphical client `valheim.x86_64 -console`, pid 816135, user
  `polyphonyrequiem`, launched 2026-07-19 22:39 by prior T019 QA. GABS-controlled,
  unambiguously task-owned; no user-owned graphical session present. Adopted; no
  production Niflheim/Heistan/HomesteadT009L touched.
- `isServer=True`, scene `main` (authoritative host occupant — the proven topology for
  a personal Character Effect; a personal-effect remote-client replication channel is a
  separate follow-up, exactly as the sibling Cooking seams documented).
- Deployed `SBPR.Niflheim.HomesteadStones.dll` md5 `c80e50a49b88e30beb4e001bc7b2b84b`
  — **byte-identical** to a fresh `-c Release` build of workspace head 37711cb. The
  running client is the remediated code, verified, not an assumption.
- `InventoryGui.UpdateRecipe` transpilers = `[net.danielgreen.sbpr.niflheim.homesteadstones]`
  — the method under test is genuinely SBPR-patched at runtime.

## Raw capture (SBPR.QADiag.T019 @ v3, monotonic-authority fix)

```
==================== QADiag T019 REAL-TRANSPILED-PATH (@37711cb PR#394) ====================
host pid=2749052395 isServer=True scene=main
InventoryGui.UpdateRecipe transpilers = [net.danielgreen.sbpr.niflheim.homesteadstones]  (EXPECT contains sbpr niflheim)
SEEDED host state (rev=639201239969514860). REAL gate ResolveActiveForLocalOccupant() = True  (EXPECT True)
food recipe = BlackSoup @ cookStation piece_cauldron skill=105 ; tool recipe = ArmorBerserkerChest
A ELIGIBLE+ACTIVE : base=6.000 vanillaAdj=6.000 -> barMax(REAL)=2.000 EXPECT 2.000 (adj*1/3) -> PASS
B ELIGIBLE+DORMANT: gateActive=False barMax(REAL)=6.000 EXPECT 6.000 (vanilla adj) -> PASS
C ELIGIBLE+UNBOUND: gateActive=False barMax(REAL)=6.000 EXPECT 6.000 (vanilla adj) -> PASS
D INELIGIBLE-nonfood: barMax(REAL)=6.000 EXPECT 6.000 (vanilla adj) -> PASS
E INELIGIBLE-nonCook: station=piece_workbench skill=107 barMax(REAL)=6.000 EXPECT 6.000 (vanilla adj) -> PASS
F COMPLETION-COUNT: gate=True realBarMax=2.000 EXPECT 2.000(adj*1/3) shortened=True timerAfter1=-1.000(<0=>completed once) timerAfter2=-1.000(<0=>no double) -> PASS
restored InventoryGui selected recipe + station + craft fields
==================== QADiag T019 VERDICT: PASS ====================
```

## Matrix (spec §US4 sc1 / contracts.md §Cooking)

| Case | Condition | Real barMax (num5) | Expected | Result |
|------|-----------|--------------------|----------|--------|
| A | eligible food (BlackSoup) + Cooking station (piece_cauldron, skill 105) + active Tier-2 owner | 2.000 | 6.000 × 1/3 = 2.000 | PASS — exact 1/3 |
| B | same, relationship released (dormant) | 6.000 | vanilla 6.000 | PASS |
| C | same, session unbound (non-owner) | 6.000 | vanilla 6.000 | PASS |
| D | non-food output (ArmorBerserkerChest) at cooking station | 6.000 | vanilla 6.000 | PASS |
| E | food recipe at non-cooking station (piece_workbench, skill 107) | 6.000 | vanilla 6.000 | PASS |
| F | eligible+active completion count | 1 completion at t≥2.000, no double | one craft = one completion | PASS |

- The 1/3 factor is applied strictly AFTER the vanilla skill adjustment: the instrument
  captures the live `GetSkillFactor` × `m_craftDurationSkillMaxDecrease` line into
  `vanillaAdj` and expects `vanillaAdj / 3` (skill factor never hardcoded). At this
  host's Cooking skill the adjustment is a no-op (6.000 → 6.000), so the observed 2.000
  is unambiguously the 1/3 factor, not a skill artifact.
- Completion count is unchanged vs vanilla: the shortened craft still fires `DoCrafting`
  exactly once (`m_craftTimer` → −1 sentinel) and does not re-complete on the next tick
  (panel-hidden early return at `m_craftTimer < 0`, decomp assembly_valheim:42366-42386).

## Instrument-defect notes (this run)

Two instrument (NOT product) defects were found and fixed while producing this artifact;
neither changes the product verdict:

1. **`num5` local via transpiler, read-back correctness** — confirmed the instrument
   reads `m_craftProgressBar.m_maxValue` (the same `num5` the completion comparison uses),
   not a re-derived value.
2. **Monotonic authority revision** — `PutActiveAuthority` originally applied fixed
   revisions (active=2, dormant=3). Authority projections are monotonic, so the B-case
   dormant (rev 3) permanently shadowed any later active (rev 2), pinning the gate
   dormant on re-runs and in the F-case. Fixed to a strictly-increasing revision
   (`Interlocked.Increment` off a ticks seed). The F-case now re-establishes an active
   eligible owner and MEASURES the real transpiled barMax (2.000) rather than assuming it.
   With the fix the whole A–F matrix passes in a single run.

## Clean teardown

Post-run: `InventoryGui.m_selectedRecipe` restored to `<null>` (pre-run state),
`m_craftTimer = -1` (idle vanilla). Station/craft-duration/multi-craft fields restored.
No production world touched. The isolated client's live state is byte-for-byte as found.

## Honest scope

Verified: the real transpiled menu-craft duration + gating + completion count on the
authoritative host occupant. Not covered here (unchanged, separate follow-up): a
personal-effect **remote-client** replication channel — on a pure remote client the
composed server runtime is null and the seam fails closed to full vanilla duration
(documented host-only scope, matching the sibling Field Prep / Iron Stomach / Refined
Workshop seams).
