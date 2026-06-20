// ============================================================================
//  Trailborne — Cartography threat-marker SEAM (IThreatMarkerProvider + registry)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §4
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §3 (ACCEPTED, PR #214)
//  Card     : t_91e86951
//
//  The pull-seam by which the SBPR carry-disc (MapSurface) shows TRANSIENT threat
//  markers without Cartography knowing what produces them. It mirrors EXACTLY the
//  inversion the disc already uses for survey pins: MapSurface.RebuildOverlay pulls
//  WorldPins.CollectInDiscPins(origin, radius) each rebuild; here it additionally
//  pulls ThreatMarkers.Collect(origin, radius) from every registered provider.
//
//  DEPENDENCY ARROW: Sunstone → (registers into) → Cartography.ThreatMarkers.
//  Cartography is UNAWARE Sunstone exists — it iterates IThreatMarkerProvider and
//  sees DiscThreatMarker structs, never a Lens. The Sunstone side owns a small
//  adapter (SunstoneLensHudOverlay's provider) that holds the latest swept blips
//  and hands them out as DiscThreatMarkers on demand. This is the design's
//  "the disc gains its first non-cartography consumer + a new seam"
//  (map-provider-model.md).
//
//  WHY A SEPARATE DiscThreatMarker (not Sunstone's ThreatBlip): so Cartography has
//  ZERO compile dependency on Features/Sunstone. The Sunstone provider adapts
//  ThreatBlip → DiscThreatMarker at the boundary. (Same direction-of-dependency
//  hygiene the WorldPins → Cartography.SurveyPin seam already keeps.)
//
//  Clean-side (ADR-0001): all SBPR-authored; no vanilla or third-party type.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// The disc-side render model for one threat marker — Cartography's OWN type, deliberately
    /// distinct from Sunstone's <c>ThreatBlip</c> so Cartography depends on nothing in
    /// Features/Sunstone. A provider adapts its own blip type into this at the seam.
    /// </summary>
    public readonly struct DiscThreatMarker
    {
        /// <summary>World position (X,Z used for the disc projection + clip; Y carried as authored).</summary>
        public readonly Vector3 WorldPos;
        /// <summary>Aggro tint the marker draws with (owned by our overlay layer — survives vanilla's pin colour stomp).</summary>
        public readonly Color Tint;
        /// <summary>Icon sprite, or null → the disc draws a tinted dot (BlipStyle.Dots).</summary>
        public readonly Sprite? Icon;
        /// <summary>Star-pip count (reserved for richer disc rendering; the v1 disc draws a dot/icon only).</summary>
        public readonly int Stars;

        public DiscThreatMarker(Vector3 worldPos, Color tint, Sprite? icon, int stars)
        {
            WorldPos = worldPos;
            Tint = tint;
            Icon = icon;
            Stars = stars;
        }
    }

    /// <summary>
    /// A transient (per-rebuild, non-persisted) threat-marker source. Cartography pulls from every
    /// registered provider each <c>MapSurface.RebuildOverlay</c>; it never knows what the provider IS.
    /// Mirrors the <c>WorldPins.CollectInDiscPins</c> pull model: the source of truth lives on the
    /// producer side, the disc just asks "any threats in range right now?" and renders the answer.
    /// </summary>
    public interface IThreatMarkerProvider
    {
        /// <summary>
        /// Append the provider's threats within <paramref name="radius"/> m of <paramref name="origin"/>,
        /// already render-derived, into <paramref name="into"/>. Appends nothing when the feature is
        /// inert (e.g. the Lens isn't worn/charged, or the handoff mode suppresses the disc). MUST NOT
        /// clear <paramref name="into"/> (the registry owns the clear) and MUST NOT throw out (the
        /// registry guards, but be a good citizen).
        /// </summary>
        void CollectThreatBlips(Vector3 origin, float radius, List<DiscThreatMarker> into);
    }

    /// <summary>
    /// The static registry of <see cref="IThreatMarkerProvider"/>s — the seam itself. Mirrors how
    /// <c>WorldPins</c> is a static seam the disc pulls from. Providers register in their own
    /// bootstrap; <c>MapSurface.RebuildOverlay</c> calls <see cref="Collect"/> once per disc rebuild.
    /// Client-only in practice (the disc only exists on a client), but the registry itself is inert
    /// and safe to touch anywhere.
    /// </summary>
    public static class ThreatMarkers
    {
        private static readonly List<IThreatMarkerProvider> _providers = new List<IThreatMarkerProvider>();

        /// <summary>Register a provider (idempotent — a double-register is a no-op).</summary>
        public static void Register(IThreatMarkerProvider provider)
        {
            if (provider == null) return;
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }

        /// <summary>Unregister a provider (safe if it was never registered).</summary>
        public static void Unregister(IThreatMarkerProvider provider)
        {
            if (provider == null) return;
            _providers.Remove(provider);
        }

        /// <summary>True if any provider is registered (lets the disc skip the clear+iterate when empty).</summary>
        public static bool HasProviders => _providers.Count > 0;

        /// <summary>
        /// Clear <paramref name="into"/> and collect every registered provider's threats within
        /// <paramref name="radius"/> m of <paramref name="origin"/>. Each provider is guarded so one
        /// throwing provider cannot break the disc rebuild (the survey pins already rendered, and the
        /// other providers still run).
        /// </summary>
        public static void Collect(Vector3 origin, float radius, List<DiscThreatMarker> into)
        {
            into.Clear();
            for (int i = 0; i < _providers.Count; i++)
            {
                try { _providers[i].CollectThreatBlips(origin, radius, into); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/ThreatMarkers] provider failed (skipped this rebuild): {e.Message}");
                }
            }
        }
    }
}
