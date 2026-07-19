using System;
using System.Reflection;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T016 remediation — the net48-ONLY live delivery seam that drives the shipped
    /// <see cref="SavorTheHearthProvider"/> into the vanilla food-timer drain path. This is the hook the
    /// QA FAIL (t_0fb85725) named as absent: a Harmony patch on <c>Player.UpdateFood(float dt, bool
    /// forceUpdate)</c> that scales ONLY the elapsed food-drain slice by the derived factor.
    ///
    /// Vanilla mechanic (decomp assembly_valheim :17526): UpdateFood accumulates <c>dt</c> into the
    /// private <c>m_foodUpdateTimer</c>; each time that timer crosses 1s it subtracts a fixed 1f from every
    /// active <c>Player.Food.m_time</c>. So the food-drain RATE is governed entirely by how fast that timer
    /// accrues elapsed time. To drain 50% slower we make the food-update timer accrue at factor speed while
    /// leaving everything else (regen timer, stat recompute, m_time itself) untouched.
    ///
    /// The seam (prefix, local player only): pre-adjust <c>m_foodUpdateTimer</c> by <c>-dt*(1-factor)</c>.
    /// Vanilla's own <c>m_foodUpdateTimer += dt</c> then nets to <c>+dt*factor</c> for the food-drain slice.
    /// The separate <c>m_foodRegenTimer += dt</c> (healing) still gets the full dt, so ONLY food drain is
    /// slowed — no stat/regen side effect. Nothing rewrites stored <c>m_time</c> (no retroactive duration),
    /// and when the factor returns to 1 the adjustment is 0, so exit/dormancy restores normal drain on the
    /// very next tick with zero carried state (AT-SAVOR-AREA-EXIT). forceUpdate ticks pass dt=0 → the
    /// adjustment is 0 → forced stat refreshes are never scaled.
    ///
    /// Authority / scope: the factor is derived from SERVER-OWNED facts only — the local player's
    /// server-observed world position resolved against the composed server's Stone Area membership, and the
    /// established ACTIVE Savor context (SavorProvisioningAdmin). It runs where the food simulation runs
    /// (the local player) AND the authoritative server context is present (listen host / singleplayer host,
    /// where <c>FoundationalPlacementObserver.Server</c> is composed). On a pure dedicated CLIENT the server
    /// is not composed locally, so the factor is 1 — pushing the server-derived factor down to a dedicated
    /// client is deferred future work, exactly like the T009R2 dedicated-ingress split. This is the smallest
    /// correct seam that makes the in-area 0.5 / exit 1.0 proof observable on a joined listen-host client.
    ///
    /// References UnityEngine/Valheim (Player, Traverse) → net48-only, not link-compiled into net8. Every
    /// gameplay DECISION lives in the engine-free <see cref="SavorFoodDrainResolver"/>, which IS unit-tested;
    /// this class only reads engine facts and applies the timer adjustment.
    /// </summary>
    [HarmonyPatch]
    internal static class SavorFoodTimerObserver
    {
        // The engine-free resolver (pure). Stateless — safe as a shared singleton.
        private static readonly SavorFoodDrainResolver Resolver = new SavorFoodDrainResolver();

        // Cached reflection handle for the private float Player.m_foodUpdateTimer (decomp :15600).
        private static FieldInfo? _foodUpdateTimer;
        private static bool _foodUpdateTimerResolved;

        [HarmonyPatch(typeof(Player), "UpdateFood")]
        [HarmonyPrefix]
        private static void OnUpdateFood(Player __instance, float dt, bool forceUpdate)
        {
            try
            {
                // forceUpdate ticks carry no elapsed slice (dt is 0 on those calls); never scale a forced
                // stat refresh. Only the local player's own food simulation is scaled.
                if (dt <= 0f || forceUpdate) return;
                if (__instance == null || __instance != Player.m_localPlayer) return;

                var server = Features.Progression.FoundationalPlacementObserver.Server;
                if (server == null) return;   // no composed authoritative server context here → factor 1

                // Server-observed occupant facts. Under the default Everyone Local policy the account /
                // owner / relationship facts do not change eligibility, so a stable local subject is
                // sufficient for the proof; the resolver still derives the full T014 view for fidelity.
                Vector3 pos = __instance.transform != null ? __instance.transform.position : Vector3.zero;
                var occupant = new SavorOccupant(
                    new AccountId("local-occupant"),
                    isOwner: false,
                    hasActiveRelationship: false,
                    x: pos.x, z: pos.z);

                double factor = Resolver.DrainFactor(server.StoneAreas, server.SavorContexts, occupant);
                if (factor >= 1.0) return;   // full drain → nothing to adjust (fast, common path)

                // Slow the food-drain slice ONLY: pre-remove the (1-factor) portion of dt from the
                // food-update timer so vanilla's own `+= dt` nets to `+= dt*factor`. The regen timer keeps
                // the full dt (healing unaffected); stored m_time is never touched (no retroactive rewrite).
                float removed = dt * (float)(1.0 - factor);
                if (removed <= 0f) return;
                AdjustFoodUpdateTimer(__instance, -removed);
            }
            catch (Exception ex)
            {
                // Never let effect delivery destabilize the food/stat path.
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Savor food-timer observation threw: " + ex);
            }
        }

        /// <summary>Add <paramref name="delta"/> to the private <c>Player.m_foodUpdateTimer</c>. Reached via
        /// cached reflection (the repo's private-field idiom). Fail-soft: if the field can't be resolved the
        /// food drain is simply unscaled this tick (never a crash).</summary>
        private static void AdjustFoodUpdateTimer(Player player, float delta)
        {
            if (!_foodUpdateTimerResolved)
            {
                _foodUpdateTimerResolved = true;
                _foodUpdateTimer = AccessTools.Field(typeof(Player), "m_foodUpdateTimer");
                if (_foodUpdateTimer == null)
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Could not resolve Player.m_foodUpdateTimer — Savor food-timer "
                        + "slowing will not apply (decomp drift? re-check :15600). No crash; drain stays vanilla.");
            }
            if (_foodUpdateTimer == null) return;
            float current = (float)(_foodUpdateTimer.GetValue(player) ?? 0f);
            _foodUpdateTimer.SetValue(player, current + delta);
        }
    }
}
