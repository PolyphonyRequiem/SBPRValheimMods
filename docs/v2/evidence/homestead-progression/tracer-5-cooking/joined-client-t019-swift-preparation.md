---
status: current
---

# T019 Swift Preparation — menu-craft duration proof (Cooking node 4 of 4, Tier-2)

- Task: `t_ff2e88c2` — T019 [US3][US4] Implement Swift Preparation and close the
  Cooking Tier-2 path. Acceptance: `AT-SWIFT-MENU-ONLY`, `AT-COOKING-TIER2`,
  `AT-NO-COOKING-COMPLETION`.
- Branch: `feat/hs-t019-swift-preparation` (off fresh `origin/main@ee59e80`, which
  already contains the merged T018 Iron Stomach two-seam fix PR #390 and the T016
  Cooking adapter surface + T017 Field Prep policy).
- Safety: pre-work check for a user-owned graphical `valheim.x86_64` found NONE
  (no running game client, no Steam desktop game session). No user session
  altered; all work is engine-free or headless build/test.

## Verdict: PASS (pure provider + Tier-2 gate + live transpiler seam verified) — in-world 1/3 menu-craft timing last mile REASONED, to be observed at the QA/T020 rerun

Swift Preparation is the sole executable **Tier-2** Cooking node, a personal
**Character Effect** that, while active (purchased AND an active relationship to
the Stone), makes an eligible menu-crafted food take **one-third of the vanilla
skill-adjusted menu-craft duration** (spec §US4 sc1). The factor is applied
strictly AFTER the vanilla Cooking-skill adjustment and ONLY to eligible
menu-crafted food; it never instant-completes a craft and never fabricates one.

This run verifies the layers a headless box can decisively prove, and states
honestly which last mile is client-only.

## What was VERIFIED

### Build + suite (this run)
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full test suite: **1476 / 1476 passed** (baseline 1455 + 21 new Swift
  Preparation tests).
- `python3 scripts/docs-lint.py`: **OK — 214 docs checked**.
- `git diff --check`: **clean**.
- SpecCheck recipe manifest: **unchanged** (Swift Preparation ships no SBPR recipe).

### Pure provider layer (`MenuCraftDurationProvider`, `AT-SWIFT-MENU-ONLY` / `AT-NO-COOKING-COMPLETION`)
The engine-free provider is the single authority for the duration decision, and
its behavior is pinned by `tests/NiflheimSwiftPreparationTests.cs`:
- An active Swift Preparation (purchase record for `SwiftPreparation@1` AND an
  active relationship, derived through the shipped T004 `DerivedActivationView`)
  multiplies the supplied vanilla skill-adjusted duration by **1/3**; a
  6.0s → 2.0s, a 1.5s → 0.5s. `ActiveDurationFactor` is exactly `1.0/3.0`.
- The factor is applied **after** the skill adjustment: the provider is handed the
  already-post-skill value and never re-derives the skill line, so two different
  skill-adjusted inputs are each independently scaled by 1/3.
- **Eligibility (`ClassifyCraft`)** gates on THREE engine-observed facts —
  output-is-food AND the active station's crafting skill == Cooking
  (`Skills.SkillType.Cooking`, 105) AND the menu-craft path — reporting a distinct
  ineligible reason otherwise (`IneligibleNonFood` / `IneligibleNonCookingStation`
  / `IneligibleNotMenuCraft`). A non-food output, a Forge/Workbench recipe, and a
  slotted world-cooking (non-menu) timer each keep the full vanilla duration.
- Dormant/unpurchased/undeveloped effects and a sibling character's active
  relationship all leave the duration unshortened; relationship loss then restore
  flips shortening on/off with **zero writes** (pure re-derivation).
- **No completion / no fabrication (`AT-NO-COOKING-COMPLETION`):** for every
  positive base the resolved duration is strictly positive AND strictly shorter
  (never instant-complete); a non-positive base (0, negative, NaN) is returned
  UNCHANGED (never conjures a craft). Swift Preparation carries no Tree-completion
  state.
- The `view` and already-resolved-`bit` overloads agree.

### Tier-2 access gate (`AT-COOKING-TIER2`, shipped T013 grammar)
Swift Preparation is the sole executable Level-2 Cooking node whose authored
prior-Offered set is exactly Field Prep + Iron Stomach (registry-pinned in the
test). The pure `NodePurchases` grammar closes the tier gate:
- With only ONE prior acquired, `PurchaseNode` rejects
  `PriorOfferedSetIncomplete`.
- With BOTH priors acquired at Tree Level 2 / Active Stone Level 2, `PurchaseNode`
  is `Applied`.
- Below Level 2 (even with both priors) it rejects on the level cap.
- `DeriveSameTreeTierAccess` for Cooking reaches **2** only when both priors are
  acquired, otherwise stays at Tier 1.

### Delivery-seam wiring (`SwiftPreparationCraftTimer`, net48)
The net48 seam that makes the 1/3 factor manifest on a joined client is armed in
`Plugin.cs` via `harmony.PatchAll(typeof(Features.Cooking.SwiftPreparationCraftTimer))`.
Decomp basis (vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
`InventoryGui.UpdateRecipe` (assembly_valheim :42372-42386) computes the
menu-craft duration into a LOCAL `num5`, applies the vanilla Cooking-skill
adjustment `num5 *= 1 - GetSkillFactor(station.m_craftingSkill) *
m_craftDurationSkillMaxDecrease`, feeds it to
`m_craftProgressBar.SetMaxValue(num5)`, and completes the craft when the
accumulating `m_craftTimer` reaches that same `num5`. The seam:
- is a minimal anchored **transpiler** that finds the single
  `GuiBar.SetMaxValue(num5)` call and injects a scale of the `num5` local IN PLACE
  (`num5 = ScaleMenuCraftDuration(this, num5)`) BEFORE the `ldloc` that pushes it —
  i.e. strictly AFTER the vanilla skill-adjustment line. Because both the
  progress-bar max AND the completion comparison read that same local, the whole
  menu craft is shortened by exactly the provider's factor with no control-flow
  change and no recipe / ItemDrop / station / other-craft mutation.
- classifies the selected recipe's craft from engine facts (output food flag +
  current station crafting skill) and delegates the duration to the shipped pure
  `MenuCraftDurationProvider`; ineligible crafts and a dormant effect get factor 1
  (full vanilla duration), and a non-positive duration is returned unchanged.
- resolves the active verdict from the authoritative **host projection**
  (`LocalProgressionObserver.Server`'s Stone/character/authority stores, keyed to
  the bound internal principal via `DerivedActivationView`) — no client-supplied
  claim is trusted, fail-closed on any resolution gap.

## Honest scope — what is NOT yet observed in-world here

Swift Preparation is a personal Character Effect, and the bounded server→client
delivery transport that Savor / Practice Range / Refined Workshop use carries
LOCAL-effect snapshots only — there is not yet a personal-effect replication
channel. So the seam reads the authoritative projection where it EXISTS
in-process: on the authoritative **host** (listen-server / singleplayer host) the
composed server holds the stores and the seam resolves the purchase + active
relationship directly. On a **pure remote client** the server runtime is null and
the seam **fails closed** (the craft keeps its full vanilla skill-adjusted
duration) rather than inventing an unauthenticated grant. The proven topology for
T019 is therefore the host occupant; a personal-effect client delivery channel is
a separate follow-up, exactly as the sibling Field Prep / Iron Stomach / Field
Fletching / Refined Workshop seams documented their host-only scope.

The in-world last mile — a host occupant with an active Swift Preparation
observing an eligible menu-crafted food's craft timer drop to one-third of the
vanilla skill-adjusted duration (while an ineligible craft and a non-acquiring
occupant see the full duration) — is the node's own joined-client artifact, to be
captured at the independent Tracer-5 verification (T020) on the isolated
throwaway-server topology the sibling Cooking nodes used. This box marks code +
tests + docs landed under review with the seam armed, not gate sign-off.

## Spec/code synchronization (AGENTS.md "the one rule")

- `docs/v2/planning/homestead-stone-progression-tasks.md` — T019 checkbox checked
  with a full landing note (this PR).
- `docs/v2/planning/homestead-stone-progression-contracts.md` — the
  `MenuCraftDurationProvider` conformance entry updated to record the shipped pure
  provider + net48 transpiler seam (factor 1/3 after skill, eligible menu-crafted
  food only, no completion, host-only delivery scope).
- No change to the data-model roster (Swift Preparation was already authored as
  `Cooking | 2 | Swift Preparation | Character Effect | personal Offered`, with the
  Field Prep + Iron Stomach prior-Offered set and Level-2 caps) or the SpecCheck
  recipe manifest (no SBPR recipe). Code implements the already-locked spec; the
  two agree.
