// Player.OnSpawned readiness postfix (spec-role-split-arm-gate.md §4/§5, AC2/AC7) — CLEAN side.
//
// ATTRIBUTION (finding only, no code ported): the choice to key "a local player is now spawned and
// in-world" off the vanilla Player.OnSpawned method + Player.m_localPlayer — rather than the
// server-only ZNet.World — is credited to MODSCAN-001, verified in the pinned MIT Jotunn corpus at
// ~/valheim/sbpr-corpus/jotunn-source (Jotunn / JotunnLib Team, MIT; v2.29.0 @ commit 6a2c37a):
//   JotunnLib/Managers/ItemManager.cs:88   [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned)), HarmonyPostfix]
//   JotunnLib/Managers/PieceManager.cs:126  same patch shape
//   JotunnLib/DebugUtils/DebugHelper.cs:49   same patch shape
// All three postfix Player.OnSpawned and then treat the `Player self`/`__instance` as a trusted live
// spawned local player (ItemManager/PieceManager re-run ReloadKnownRecipes at that moment). This is a
// behavioural FINDING — finding-only attribution, no Jotunn code is ported; the implementation
// below is our own clean code against vanilla members verified present at the pinned game
// (assembly_valheim @ 0.221.12): Player.OnSpawned and Player.m_localPlayer exist; the role split is
// done with !ZNet.IsServer() because IsClientInstance() does NOT exist at this pin (verified absent;
// that Jotunn spelling targets a newer build — see spec §0). No Jotunn source is copied.
using System;
using HarmonyLib;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Harmony postfix on <c>Player.OnSpawned</c> that owns a single volatile flag recording that a
    /// local player has spawned in-world. This is EVENT-DRIVEN — set once when the spawn method runs
    /// — never a per-frame poll. <see cref="ZNetClientReadinessSource"/> reads the flag (plus a live
    /// <c>Player.m_localPlayer != null</c> re-read and the role predicate) to answer client-role
    /// arm-time readiness.
    ///
    /// <para>Verified patchable at the pin: <c>Player.OnSpawned(bool)</c> is a public non-virtual
    /// instance method with a real IL body (standard postfix target). Its caller sets
    /// <c>Player.m_localPlayer</c> before invoking it, so by the time this postfix runs a live local
    /// player object exists. OnSpawned fires in every mode incl. singleplayer/host, which is exactly
    /// why the client readiness source ANDs this flag with the <c>!IsServer()</c> role predicate —
    /// the spawn signal alone would false-arm SP/host (spec AC3).</para>
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedReadinessPatch
    {
        /// <summary>
        /// Set true by the postfix when a local player spawns; read by
        /// <see cref="ZNetClientReadinessSource"/>. <c>volatile</c> because the Unity main thread is
        /// the single writer/reader — no lock needed for a single bool with one writer.
        /// </summary>
        internal static volatile bool _localPlayerSpawned;

        /// <summary>
        /// Postfix: the spawn having run is the whole signal — do NOT read args. Allocation-free and
        /// exception-free (a readiness flag must never wedge the vanilla spawn path).
        /// </summary>
        // ReSharper disable once InconsistentNaming — Harmony convention for postfix without args.
        [HarmonyPostfix]
        private static void Postfix()
        {
            _localPlayerSpawned = true;
        }

        /// <summary>Reset the flag (lifecycle tidy-up on <c>Plugin.OnDestroy</c>) so a re-entered
        /// session starts clean. Best-effort; swallows nothing it should not.</summary>
        internal static void Reset()
        {
            _localPlayerSpawned = false;
        }
    }
}
