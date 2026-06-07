using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Trailblazing
{
    /// <summary>
    /// CLIENT-COSMETIC placement-ripple sizing (FEATURE: scale placement ground-ripple
    /// with terrain-op magnitude — Request 1, spike
    /// docs/investigations/2026-06-07-terrain-placement-ripple-magnitude-spike.md).
    ///
    /// THE BUG: while aiming to place a build piece, Valheim shows an animated ground
    /// ring — the placement marker (<c>Player.m_placementMarkerInstance</c>, a clone of
    /// <c>Player.m_placeMarker</c>), whose ring is a <c>CircleProjector</c> with a
    /// CONSTANT <c>m_radius</c> (5 m on this build). <c>Player.UpdatePlacementGhost</c>
    /// repositions the marker every frame but NEVER sizes it from the piece's terrain
    /// op. Vanilla never needed to (Hoe/Cultivator are all ~2 m). Our Trailblazer's
    /// Spade registers path/replant ops at 1.5 / 3 / 5 m, so a wide op previews a narrow
    /// ring and a narrow op previews a wide one — the ripple misleads the player about
    /// the affected area. The op already APPLIES at its true radius; this is preview
    /// accuracy only.
    ///
    /// THE FIX: a Harmony POSTFIX on <c>Player.UpdatePlacementGhost</c> (the method that
    /// already positions the marker each frame). After vanilla runs, if the active ghost
    /// is one of OUR spade ops, size the marker's CircleProjector to that op's effect
    /// radius; otherwise restore the marker to its captured vanilla default so a prior
    /// SBPR placement can't leave a widened ring on a subsequent vanilla Hoe.
    ///
    /// CLEAN-ROOM: every vanilla surface touched here was verified by name + shape
    /// against the shipped <c>assembly_valheim.dll</c> via a MetadataLoadContext probe
    /// (no decompiled IronGate source read) —
    ///   • <c>Player.UpdatePlacementGhost(bool)</c> — private instance, void. Target.
    ///   • <c>Player.m_placementMarkerInstance</c> — private GameObject. Reflected.
    ///   • <c>Player.m_placementGhost</c> — private GameObject. Reflected.
    ///   • <c>Player.m_localPlayer</c> — public static Player.
    ///   • <c>CircleProjector.m_radius</c> (public float),
    ///     <c>CircleProjector.m_nrOfSegments</c> (public int).
    /// The effect radius itself is NOT re-derived from the ghost's TerrainModifier (the
    /// spike's "max of enabled radii" mirror referenced m_raise/m_raiseRadius, which the
    /// probe confirmed do NOT exist on this build); it is read from the op-registration
    /// table via <see cref="Trailblazing.TryGetSpadeOpRadius"/> — the same widths that
    /// were applied to the ops at registration, so the preview matches the real effect.
    ///
    /// GATING: client-cosmetic, so it gates on PIECE IDENTITY (our op names), NOT the
    /// server-sanity doctrine — we must never resize a vanilla placement. The patch is
    /// inert on a dedicated server (no local Player, no placement marker, so the body
    /// early-outs) — it cannot be proven headless; in-world eyeball is the acceptance
    /// check (see the v1 playtest note).
    /// </summary>
    [HarmonyPatch]
    public static class PlacementMarkerRadiusPatch
    {
        // Private Player fields reached via cached reflection (AccessTools.FieldRef so the
        // read is a fast delegate, not a per-frame reflective Invoke). Resolved once at
        // type-load; if a future game build renames either field the ref is null and the
        // postfix early-outs (logged once) rather than throwing every frame.
        private static readonly AccessTools.FieldRef<Player, GameObject>? MarkerRef =
            TryFieldRef("m_placementMarkerInstance");
        private static readonly AccessTools.FieldRef<Player, GameObject>? GhostRef =
            TryFieldRef("m_placementGhost");

        private static AccessTools.FieldRef<Player, GameObject>? TryFieldRef(string field)
        {
            try
            {
                if (AccessTools.Field(typeof(Player), field) == null)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Ripple] Player.{field} not found via reflection; " +
                        "placement-ripple scaling disabled (vanilla field renamed?).");
                    return null;
                }
                return AccessTools.FieldRefAccess<Player, GameObject>(field);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Ripple] FieldRef '{field}' failed: {e.Message}");
                return null;
            }
        }

        // Captured vanilla defaults of the SHARED marker's CircleProjector, so we can
        // restore the ring after sizing it for one of our ops (the marker instance is
        // reused across ALL placements — Gotcha 2 in the spike). Captured lazily the
        // first time we see an unmodified projector, keyed by instance so a marker
        // re-instantiation (scene reload) re-captures cleanly.
        private static CircleProjector? capturedProjector;
        private static float defaultRadius;
        private static int defaultSegments;
        private static bool warnedNoProjector;

        // Segment density for a widened ring so it doesn't look sparse: ~8 segments per
        // metre, floored at the vanilla-ish 20. Cheap, purely visual.
        private const float SegmentsPerMetre = 8f;
        private const int MinSegments = 20;

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            // Client-only by construction: only the local player aims placements. On a
            // dedicated server m_localPlayer is null and this never does work.
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (MarkerRef == null || GhostRef == null) return;

            GameObject marker;
            GameObject ghost;
            try
            {
                marker = MarkerRef(__instance);
                ghost = GhostRef(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Ripple] field read failed: {e.Message}");
                return;
            }

            // No marker active (e.g. nothing being placed, or a ground-only piece that
            // vanilla hid) → nothing to size. We do NOT force-show a hidden marker.
            if (marker == null || !marker.activeInHierarchy) return;

            var proj = marker.GetComponentInChildren<CircleProjector>(includeInactive: true);
            if (proj == null)
            {
                if (!warnedNoProjector)
                {
                    warnedNoProjector = true;
                    Plugin.Log.LogWarning(
                        "[Trailborne/Ripple] placement marker has no CircleProjector; " +
                        "ripple scaling skipped (vanilla marker structure changed?).");
                }
                return;
            }

            // Capture the vanilla defaults the first time we meet this projector instance
            // (before we've ever written it), so we have a faithful value to restore to.
            EnsureCaptured(proj);

            float radius = 0f;
            bool ours = ghost != null
                        && Trailblazing.TryGetSpadeOpRadius(ghost.name, out radius)
                        && radius > 0f;
            if (ours)
            {
                int segments = Mathf.Max(MinSegments, Mathf.RoundToInt(radius * SegmentsPerMetre));
                if (!Mathf.Approximately(proj.m_radius, radius) || proj.m_nrOfSegments != segments)
                {
                    proj.m_radius = radius;
                    proj.m_nrOfSegments = segments;
                }
            }
            else
            {
                // Not one of our ops: restore the captured vanilla ring so we never leave
                // a widened/contracted radius on a subsequent vanilla placement.
                RestoreDefaults(proj);
            }
        }

        private static void EnsureCaptured(CircleProjector proj)
        {
            if (ReferenceEquals(capturedProjector, proj)) return;
            capturedProjector = proj;
            defaultRadius = proj.m_radius;
            defaultSegments = proj.m_nrOfSegments;
        }

        private static void RestoreDefaults(CircleProjector proj)
        {
            if (!ReferenceEquals(capturedProjector, proj)) return; // never captured → leave as-is
            if (!Mathf.Approximately(proj.m_radius, defaultRadius) || proj.m_nrOfSegments != defaultSegments)
            {
                proj.m_radius = defaultRadius;
                proj.m_nrOfSegments = defaultSegments;
            }
        }
    }
}
