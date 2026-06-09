using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// CLIENT placement-VALIDATION gate (IMPL: cairn placement elevation gate — card
    /// t_aceacef6, routed from architect t_e3bb167d; spec §A2.1 "Placement elevation
    /// gate"). A cairn is a trail marker, not a buoy: it may NOT be placed with its
    /// placement origin below 2 m above sea level.
    ///
    /// LOCKED decision (Daniel, 2026-06-08, v0.2.9 playtest): "cairns should not be
    /// able to be placed at elevations under 2 m from sea level."
    ///
    /// WHY A HOOK (no pure-flag path): vanilla <c>Piece</c> flags
    /// (<c>m_waterPiece</c>/<c>m_noInWater</c>/<c>m_groundOnly</c>) express in/near
    /// water but NOT an elevation buffer ABOVE the water plane — <c>m_noInWater</c>
    /// only rejects y &lt; sea level, doing nothing for the sea-level → sea-level+2 m
    /// shallows we must exclude. So a Harmony hook is required.
    ///
    /// THE SEAM: <c>Player.UpdatePlacementGhost(bool)</c> computes the private
    /// <c>m_placementStatus</c> and ends by calling <c>SetPlacementGhostValid</c>.
    /// <c>Player.TryPlacePiece</c> RE-RUNS <c>UpdatePlacementGhost</c> then
    /// <c>switch(m_placementStatus)</c>: an <c>Invalid</c> status fires
    /// <c>Message(Center, "$msg_invalidplacement")</c> and returns false (the place is
    /// blocked, no resources consumed). So a POSTFIX here that forces
    /// <c>m_placementStatus = Invalid</c> for a too-low cairn both blocks the real
    /// place AND fires <c>$msg_invalidplacement</c> for free — no custom message. We
    /// also call the public <c>Piece.SetInvalidPlacementHeightlight(true)</c> so the
    /// preview ghost reddens THIS frame (vanilla had just set it green pre-postfix).
    ///
    /// Modeled on the shipping sibling
    /// <see cref="Features.Trailblazing.PlacementMarkerRadiusPatch"/> — a
    /// <c>[HarmonyPostfix]</c> on this EXACT method that already reflects private
    /// <c>Player</c> fields via cached <c>AccessTools.FieldRef</c> (resolved once at
    /// type-load; early-out + log if a future build renames a field). Same shape here.
    ///
    /// GATING: client placement-validation, so it gates on (a) the LOCAL player and
    /// (b) PIECE IDENTITY (our cairn names) — NOT the server-sanity doctrine. The patch
    /// is inert on a dedicated server (no local Player, so <c>UpdatePlacementGhost</c>
    /// never runs with a real instance and the body early-outs). It therefore CANNOT be
    /// proven headless; Daniel's in-world waterline test is the acceptance gate.
    ///
    /// CLEAN-ROOM: every vanilla surface touched here was verified by name + shape
    /// against the shipped <c>assembly_valheim.dll</c> via a MetadataLoadContext probe
    /// (metadata only — no game code executed, no decompiled IronGate source read) —
    ///   • <c>Player.UpdatePlacementGhost(bool)</c> — private instance, void. Target.
    ///   • <c>Player.m_placementGhost</c> — private instance GameObject. Reflected.
    ///   • <c>Player.m_placementStatus</c> — private instance Player.PlacementStatus.
    ///     Reflected READ and WRITE (the enum TYPE is public; only the field is private).
    ///   • <c>Player.PlacementStatus</c> — public nested enum; has Valid + Invalid.
    ///   • <c>Player.m_localPlayer</c> — public static Player.
    ///   • <c>Piece.SetInvalidPlacementHeightlight(bool)</c> — public instance.
    ///   • <c>ZoneSystem.instance</c> — public static. NOTE: on this build it is a
    ///     public static PROPERTY (get_instance), not a field as the design note stated;
    ///     C# <c>ZoneSystem.instance</c> binds to the getter identically, and it is
    ///     public, so we reference it directly with no reflection.
    ///   • <c>ZoneSystem.m_waterLevel</c> — public instance float (= 30 on this build).
    ///   • <c>ZoneSystem.c_WaterLevel</c> — public const float (= 30). Fallback only.
    /// </summary>
    [HarmonyPatch]
    public static class CairnPlacementGatePatch
    {
        /// <summary>
        /// Minimum clearance, in metres, the cairn's placement origin must sit ABOVE
        /// the live sea level. Single source of truth for the gate: valid iff
        /// <c>placementPoint.y &gt;= seaLevel + SeaLevelClearanceMeters</c>
        /// (≥ y32 at the default sea level of 30).
        /// </summary>
        private const float SeaLevelClearanceMeters = 2f;

        // Private Player fields reached via cached reflection (AccessTools.FieldRef so
        // each touch is a fast delegate, not a per-frame reflective Invoke). Resolved
        // once at type-load; if a future game build renames either field the ref is
        // null and the postfix early-outs (logged once) rather than throwing per frame.
        private static readonly AccessTools.FieldRef<Player, GameObject>? GhostRef =
            TryFieldRef<GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus>? StatusRef =
            TryFieldRef<Player.PlacementStatus>("m_placementStatus");

        // The four cairn prefab names (piece_sbpr_cairn_<color>), built once from the
        // Cairns feature manifest so this gate can never drift from the real piece set.
        private static readonly HashSet<string> CairnNames = BuildCairnNames();

        // One-shot guard so a reflective failure logs once, not every placement frame.
        private static bool warnedReflectionFailure;

        private static AccessTools.FieldRef<Player, T>? TryFieldRef<T>(string field)
        {
            try
            {
                if (AccessTools.Field(typeof(Player), field) == null)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/CairnGate] Player.{field} not found via reflection; " +
                        "cairn elevation gate disabled (vanilla field renamed?).");
                    return null;
                }
                return AccessTools.FieldRefAccess<Player, T>(field);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/CairnGate] FieldRef '{field}' failed: {e.Message}");
                return null;
            }
        }

        private static HashSet<string> BuildCairnNames()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var color in Cairns.Colors)
                set.Add(Cairns.CairnName(color));
            return set;
        }

        // Robust to Unity's instantiation suffix: a placement ghost GameObject is a
        // clone, so its .name is e.g. "piece_sbpr_cairn_red(Clone)". Strip from
        // "(Clone" onward before matching — same shape as Trailblazing.TryGetSpadeOpRadius.
        private static bool IsCairnGhost(string? ghostName)
        {
            if (string.IsNullOrEmpty(ghostName)) return false;
            int idx = ghostName!.IndexOf("(Clone", StringComparison.Ordinal);
            string name = (idx >= 0 ? ghostName.Substring(0, idx) : ghostName).Trim();
            return CairnNames.Contains(name);
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            // (1) Client-only by construction: only the local player aims placements.
            // On a dedicated server m_localPlayer is null and this never does work.
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (GhostRef == null || StatusRef == null) return;

            try
            {
                GameObject ghost = GhostRef(__instance);
                if (ghost == null) return; // nothing being placed → no ghost to gate

                // (2) Cairn identity gate. Markers are items (never a ghost); signs,
                // Path Lamps and Spade path ops don't match cairn names → untouched.
                if (!IsCairnGhost(ghost.name)) return;

                // (3) Only ever DOWNGRADE a Valid status. If vanilla already rated the
                // placement non-Valid it has a more specific reason — let its message
                // win; never upgrade a blocked status.
                if (StatusRef(__instance) != Player.PlacementStatus.Valid) return;

                // (4) Elevation test against the LIVE global ocean plane (fallback to the
                // compile-time const if the singleton isn't up yet). Measured at the
                // ghost's placement origin (ground-contact), not the pile top.
                float seaLevel = ZoneSystem.instance != null
                    ? ZoneSystem.instance.m_waterLevel
                    : ZoneSystem.c_WaterLevel;
                if (ghost.transform.position.y >= seaLevel + SeaLevelClearanceMeters)
                    return; // high enough → allow (unchanged)

                // (5) Too low: force Invalid so TryPlacePiece (which re-runs this method)
                // blocks the place and fires "$msg_invalidplacement", and redden the
                // preview ghost THIS frame (vanilla had set it green before our postfix).
                StatusRef(__instance) = Player.PlacementStatus.Invalid;
                var piece = ghost.GetComponent<Piece>();
                if (piece != null) piece.SetInvalidPlacementHeightlight(true);
            }
            catch (Exception e)
            {
                if (!warnedReflectionFailure)
                {
                    warnedReflectionFailure = true;
                    Plugin.Log.LogWarning(
                        "[Trailborne/CairnGate] elevation gate hit a reflective error; " +
                        $"suppressing further per-frame logs: {e.Message}");
                }
            }
        }
    }
}
