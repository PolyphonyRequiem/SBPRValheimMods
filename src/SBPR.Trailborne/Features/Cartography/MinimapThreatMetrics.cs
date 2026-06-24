// ============================================================================
//  Trailborne — shared Sunstone minimap threat-blip RENDER METRICS (card t_bc017af4)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md ("Blip representation")
//  Parent    : t_aae4fd20 (Daniel 2026-06-24: "minimap icons notably too small, ~75% larger")
//
//  The single home for the on-minimap Sunstone threat-blip render ratios, read by
//  BOTH minimap surfaces — the SBPR carry-disc (MapSurface, nomap-ON) and the
//  vanilla corner overlay (SunstoneMinimapThreatLayer, nomap-OFF). Before this card
//  each surface carried its OWN copy of the size consts (BlipPx/ThreatBlipPx = 14f,
//  the 0.6 rim multiplier, the 7f pip size), so a change to one could silently
//  desync the other. Single-homing them here makes parity a property of construction.
//
//  ── WHY CARTOGRAPHY, NOT SUNSTONE ───────────────────────────────────────────────
//  Cartography is the LOWER layer both surfaces already depend on
//  (SunstoneMinimapThreatLayer.cs calls Cartography.BoundedMapMath.ClampToRimPx),
//  and the seam mandates a one-way arrow: Sunstone → Cartography, never the reverse
//  (IThreatMarkerProvider.cs: "DEPENDENCY ARROW: Sunstone → Cartography. Cartography
//  is UNAWARE Sunstone exists."). Homing the shared const here lets each surface read
//  it without MapSurface (Features.Cartography) ever taking a reference into
//  Features/Sunstone — which would invert that arrow. It also lands in the engine-
//  free tier (no UnityEngine / Harmony / Valheim refs), so the test project
//  link-compiles it and the ratios are CI-pinned (MinimapThreatMetricsTests).
//
//  Clean-side (ADR-0001): SBPR-authored constants only; no vanilla or third-party type.
// ============================================================================

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Shared render metrics for Sunstone threat blips on BOTH minimap surfaces
    /// (the SBPR carry-disc in <c>MapSurface</c> + the vanilla corner overlay in
    /// <c>SunstoneMinimapThreatLayer</c>). Homed in Cartography — the lower layer both
    /// surfaces already depend on (<c>SunstoneMinimapThreatLayer</c> calls
    /// <c>Cartography.BoundedMapMath</c>) — so neither surface inverts the blessed
    /// Sunstone→Cartography arrow (<c>IThreatMarkerProvider.cs</c>). Engine-free →
    /// link-compiled into the test project so the ratios are CI-pinned.
    /// </summary>
    public static class MinimapThreatMetrics
    {
        /// <summary>Default on-minimap blip size (px). Was 14f; Daniel LOCKED 24f on 2026-06-24
        /// ("both scaled to 24px") — his clean-number rounding of the "~75% larger" ask (24/14 ≈ +71%).</summary>
        public const float DefaultBlipPx = 24f;   // Daniel-locked 2026-06-24 (≈+71%, his round-number for "~75%")

        /// <summary>Star-pip size as a fraction of the resolved blip px (was MinimapPipPx/BlipPx = 7/14).</summary>
        public const float PipToBlipRatio = 0.5f;

        /// <summary>Off-edge rim-indicator size multiplier (UNCHANGED value; just single-homed now).</summary>
        public const float RimScale = 0.6f;
    }
}
