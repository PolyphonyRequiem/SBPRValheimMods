using System;
using System.Reflection;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T016 remediation (rebased onto the merged shared Local Effect runtime, PR #368) — the net48-ONLY
    /// live delivery seam that drives the shipped <see cref="SavorTheHearthProvider"/> into the vanilla
    /// food-timer drain path. This is the hook the QA FAIL (t_0fb85725) named as absent: a Harmony patch on
    /// <c>Player.UpdateFood(float dt, bool forceUpdate)</c> that scales ONLY the elapsed food-drain slice by
    /// the derived factor.
    ///
    /// AUTHORITY (post-rebase): the active/dormant decision is owned ENTIRELY by the reviewed shared
    /// substrate. This seam no longer carries any family-local activation state — it asks the authoritative
    /// <see cref="LocalActivationService"/> (composed on the host by FoundationalRuntimeBootstrap into
    /// <see cref="Features.Progression.LocalProgressionObserver.Server"/>) for the local occupant's current
    /// per-occupant <see cref="LocalActivationSnapshot"/>, and translates its Savor active-state into the
    /// drain factor via the engine-free <see cref="SavorFoodDrainResolver"/>. Everything the factor depends
    /// on — developed node, committed Tree, governance presence, Settlement policy eligibility, in-Area
    /// occupancy — is decided by the substrate off committed state, never fabricated here.
    ///
    /// Vanilla mechanic (decomp assembly_valheim :17526): UpdateFood accumulates <c>dt</c> into the private
    /// <c>m_foodUpdateTimer</c>; each time that timer crosses 1s it subtracts a fixed 1f from every active
    /// <c>Player.Food.m_time</c>. So the food-drain RATE is governed entirely by how fast that timer accrues
    /// elapsed time. To drain 50% slower we make the food-update timer accrue at factor speed while leaving
    /// everything else (regen timer, stat recompute, m_time itself) untouched.
    ///
    /// The seam (prefix, local player only): pre-adjust <c>m_foodUpdateTimer</c> by <c>-dt*(1-factor)</c>.
    /// Vanilla's own <c>m_foodUpdateTimer += dt</c> then nets to <c>+dt*factor</c> for the food-drain slice.
    /// The separate <c>m_foodRegenTimer += dt</c> (healing) still gets the full dt, so ONLY food drain is
    /// slowed — no stat/regen side effect. Nothing rewrites stored <c>m_time</c> (no retroactive duration),
    /// and when the factor returns to 1 the adjustment is 0, so exit/dormancy restores normal drain on the
    /// very next tick with zero carried state (AT-SAVOR-AREA-EXIT). forceUpdate ticks pass dt=0 → the
    /// adjustment is 0 → forced stat refreshes are never scaled.
    ///
    /// Scope (logs-green ≠ playable): it runs where the food simulation runs (the local player) AND the
    /// authoritative server context is present (listen host / singleplayer host, where both the Foundational
    /// and Local progression runtimes are composed locally). On a pure dedicated CLIENT the server runtime
    /// is not composed locally, so this seam reads no authoritative snapshot and the factor is 1 — driving
    /// the server-derived factor down to a dedicated client (via the shared server→client delivery channel +
    /// the client cache) is deferred future work, exactly like the T009R2 dedicated-ingress split.
    ///
    /// References UnityEngine/Valheim (Player, Traverse) → net48-only, not link-compiled into net8. The one
    /// gameplay DECISION lives in the engine-free <see cref="SavorFoodDrainResolver"/>, which IS unit-tested;
    /// this class only reads engine facts, fetches the authoritative snapshot, and applies the adjustment.
    /// </summary>
    [HarmonyPatch]
    internal static class SavorFoodTimerObserver
    {
        // The engine-free translator (pure). Stateless — safe as a shared singleton.
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

                // The authoritative Local progression runtime + the Foundational runtime (Stone Areas +
                // bound sessions) must both be composed here — i.e. this is the authoritative host. On a
                // pure dedicated client neither is composed locally → no authoritative snapshot → factor 1.
                var local = Features.Progression.LocalProgressionObserver.Server;
                var foundational = Features.Progression.FoundationalPlacementObserver.Server;
                if (local == null || foundational == null) return;

                var snapshot = ResolveLocalSnapshot(__instance, local, foundational);
                double factor = Resolver.DrainFactor(snapshot);
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

        /// <summary>Fetch the local occupant's authoritative per-occupant Local Effect read model from the
        /// composed <see cref="LocalActivationService"/>. Resolves the local player's BOUND INTERNAL
        /// principal (the same identity space placement authorizes under), its server-owned world position →
        /// Stone Area occupancy, and its committed relationship activity, then asks the substrate to derive
        /// the snapshot. Returns null when the occupant is bound to no session, stands in no Area, or the
        /// substrate cannot resolve authority — every one of which yields factor 1 (fail closed).</summary>
        private static LocalActivationSnapshot? ResolveLocalSnapshot(
            Player player,
            Application.Activation.LocalProgressionServer local,
            FoundationalProgressionServer foundational)
        {
            // Resolve the local player's bound internal principal by its durable player:<s_playerID> key —
            // the SAME peer key admission binds under and placement resolves (FoundationalPlacementObserver).
            long localPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(localPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal) ||
                string.IsNullOrEmpty(principal.Account.Value) ||
                string.IsNullOrEmpty(principal.Character.Value))
                return null;   // no bound internal session → fail closed

            var occupant = new AccountId(principal.Account.Value);
            var character = new CharacterId(principal.Character.Value);

            // Server-owned occupancy: the local host's own transform is server truth. Resolve which Stone
            // Area (if any) it stands in; outside every Area → no snapshot → factor 1.
            Vector3 pos = player.transform != null ? player.transform.position : Vector3.zero;
            if (!foundational.StoneAreas.TryResolve(pos.x, pos.z, out var stoneId))
                return null;

            // The occupant's own committed relationship activity to THIS Stone (server-derived, not a claim).
            bool hasRelationship = foundational.Authority.GetAuthority(occupant, stoneId).HasActive(character);

            // Compose the authoritative presence (owner + Stone-wide Governor presence are DERIVED from
            // committed state inside ComposePresence) and derive the per-occupant snapshot. Fetch (not
            // Publish) — this is a read; it never bumps the delivery sequence.
            var presence = local.ComposePresence(stoneId, occupant, character, hasRelationship, insideStoneArea: true);
            return local.Activation.Fetch(stoneId, presence);
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
