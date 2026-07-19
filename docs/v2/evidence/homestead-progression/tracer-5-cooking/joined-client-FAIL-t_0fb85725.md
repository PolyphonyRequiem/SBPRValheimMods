# T016 Savor the Hearth — Joined-client proof: **FAIL (live path absent)**

- Task: `t_0fb85725` (qa-playtest)
- PR: #364 `feat/hs-t016-savor-hearth`
- Branch head inspected: `f0c500828a5cd29cb2f14c9f562a5b3e1d349906`
- Date: 2026-07-18

## Verdict

**FAIL — not a defect in the shipped code, a wiring gap.** The requested proof
(active Savor inside the Stone Area yields cooking/food-timer factor exactly 0.5;
exit/dormancy returns factor to 1) **cannot be produced by any joined Valheim
client** because the effect is not connected to the vanilla food-timer runtime.
Deploying a licensed client was therefore not performed: there is no seam that
would move the observed factor off 1.0 in-world, so a client run would only
manufacture a false negative and burn a licensed session.

Per the honesty rule (AGENTS.md: "logs green ≠ playable"), and per the task's own
instruction — "First verify the provider is really wired into the net48 Valheim
runtime. If the live path is absent/broken, return FAIL with the exact missing
hook/call seam" — this is the required outcome.

## Evidence the live path is absent (static, reproducible)

`SavorTheHearthProvider`
(`src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/CookingProviders.cs`) is a
pure, stateless value object. Grep of the entire `src/` tree at the reviewed head:

1. **Zero production callers.** The only references to `SavorTheHearthProvider`,
   `CookingProviders`, `DrainFactor`, `ConsumeElapsed`, or the
   `Adapters.Cooking` namespace are the class's own definition and
   `tests/NiflheimSavorTheHearthTests.cs`. No file `using
   SBPR.Niflheim.HomesteadStones.Adapters.Cooking;` outside the test.

2. **No food-timer Harmony patch.** No `[HarmonyPatch]` anywhere in either
   assembly targets `Player.UpdateFood`, `Player.Food`, `GetFoods`, or the
   food-timer drain loop. The only `Player` patches in HomesteadStones are on
   `Player.PlacePiece` (placement observers). The only code that touches
   `Player.UpdateFood` at all is `TwistedPortalEnergy` in the Trailborne
   assembly (an unrelated portal-energy feature), which does not reference the
   Cooking provider.

3. **Plugin.Awake registers no Cooking patch.**
   `src/SBPR.Niflheim.HomesteadStones/Plugin.cs` `PatchAll`s only the
   HomesteadStone registrar/placement, Foundational bootstrap/observer, dedicated
   placement ingress, pilot-session lifecycle, and relationship-provisioning
   admin. Nothing arms a Cooking effect delivery.

The T016 commit message itself is honest about this: it ships only "the
engine-free Cooking effect-delivery provider surface" and explicitly states the
"Joined-client in-area/exit in-world artifact is BLOCKED … explicitly not
claimed."

## Exact missing hook / call seam (for remediation)

To make the factor observable in a joined client, a server-or-local Harmony patch
must drive the shipped provider into the vanilla food-drain path. Concretely:

- **Seam:** `Player.UpdateFood(float dt, bool forceUpdate)` (private; vanilla
  decomp ~:17526) is the per-tick loop that decrements each `Player.Food.m_time`
  by elapsed `dt`. This is the single point where a "food timer consumes elapsed
  time" and thus where `DrainFactor`/`ConsumeElapsed` must scale the slice.
- **Required wiring:** a patch that, for the local player, derives the current
  `LocalEffectActivationView` (T014) and multiplies the elapsed `dt` applied to
  active food timers by `SavorTheHearthProvider.DrainFactor(view)` (0.5 active /
  1.0 otherwise) — scaling ONLY the elapsed slice, never rewriting stored
  `m_time`, so exit/dormancy restores factor 1 on the next tick with no
  retroactive refund/clawback. The provider is already designed for exactly this
  (`ConsumeElapsed(view, elapsedSeconds)`); it simply has no caller.
- **Contract to preserve:** no item/stat/duration mutation, no retroactive timer
  rewrite, factor flips immediately on Area exit / policy loss / governance
  dormancy. All already guaranteed by the pure provider; the patch must not
  reintroduce state.

No architecture redesign is required — this is a focused delivery-seam wiring
task, deferred to `engineer-gameplay`.

## What WAS verified

- Engine-free provider grammar (0.5/1.0, no mutation, no retroactive duration) is
  green — that is reviewer card `t_656424c2`'s engine-free PASS, not re-litigated
  here.
- The gap is solely the live engine seam.
