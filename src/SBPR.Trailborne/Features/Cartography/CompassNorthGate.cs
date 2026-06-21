// ============================================================================
//  Trailborne v3 (Swamp) — Iron Compass → map-surface north-ring GATE (engine-free)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/iron-compass-minimap-ring-impl-spec.md §0/§5
//  Design   : docs/design/iron-compass-minimap-ring.md (GATED, PR #226)
//  Card     : t_fb53c9e4 (M1) — graduated from spec card t_ed803a83 (PR #229)
//
//  The PURE truth table for "given the live world state + the gated CompassDiscMode
//  knob, does an SBPR map surface draw the compass north ring this tick, and does
//  the HUD compass needle hide?" Daniel locked the surface policy 2026-06-20:
//    • CompassDiscMode = DiscWhenBound (default) — worn AND a surface showing →
//      the HUD needle hides and the surface ring shows; else the HUD needle.
//    • ① "the needle goes away" when a surface ring is up (HideHudNeedle).
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The CompassDiscMode policy is the
//  load-bearing decision of M1, and it is exactly the kind of pure boolean logic
//  the repo's test suite gates headlessly — the DiscRingGeometry / BoundedMapMath /
//  LensHandoffDecision link-compile precedent (tests/SBPR.Trailborne.Tests.csproj).
//  Keeping it free of UnityEngine / HarmonyLib / Valheim types lets
//  tests/CompassNorthGateTests.cs assert the FULL truth table under net8.0, so a
//  future edit that silently un-hides the needle under DiscWhenBound (the #208/#209-
//  adjacent failure class) or makes the surface ring inert fails CI instead of
//  shipping. The compass HUD's Update() reads the live game state (IsWearingCompass,
//  CartographyViewer.IsMinimapBound / modal IsActive, the config enum), reduces it
//  to the two booleans this file consumes, and then renders — the POLICY lives here;
//  only the STATE READ + the RENDER live in the engine code.
//
//  This is the STRUCTURAL TWIN of LensHandoffDecision (Sunstone Lens → minimap
//  handoff, card t_91e86951): both reduce live HUD world state to a pure render
//  plan resolved by one gated mode enum. The single shared invariant (§5): an SBPR
//  map surface renders a north marker IFF the Iron Compass is worn. The twin must
//  NOT show north; this one MUST.
//
//  Clean-side (ADR-0001): all SBPR-authored decision logic; references no vanilla
//  or third-party type. (The CompassDiscModeEnum lives here too so the Config bind
//  in Plugin.cs and the engine-free test share one definition.)
// ============================================================================

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// The §0-① HUD-needle-vs-surface-ring policy (M1), a live Config enum so Daniel can flip the
    /// feel on a joined client (the LensHandoffDecision / banner-windsock pattern). Composes with the
    /// orthogonal <c>CompassAutoNorthUp</c> bool (M2 — heading-up vs north-up), which is a separate
    /// axis (two orthogonal config entries beat a 4-way enum conflating "HUD-vs-surface" with
    /// "heading-up-vs-north-up").
    /// </summary>
    public enum CompassDiscModeEnum
    {
        /// <summary>Ignore every map surface; the HUD needle always renders (today's behaviour / escape hatch).</summary>
        HudOnly,
        /// <summary>DEFAULT (Daniel ①). Worn AND a surface showing → the HUD needle hides and the surface ring shows; else the HUD needle.</summary>
        DiscWhenBound,
        /// <summary>The HUD needle AND the surface ring both render whenever a surface is showing.</summary>
        Both,
    }

    /// <summary>
    /// The resolved per-tick render plan: which booleans the compass overlay acts on after it has
    /// read the live world state. <see cref="ShowSurfaceRing"/> feeds the iron bezel + N + ticks to
    /// the active SBPR map surface(s); <see cref="HideHudNeedle"/> hides the HUD overlay's
    /// <c>_content</c> child — NEVER the host (the #208/#209 dead-Update-pump invariant,
    /// AT-COMPASS-DISC-PUMP). When the compass is unworn the needle is already hidden by the
    /// compass's own equip-gate, so <see cref="HideHudNeedle"/> stays false there ("don't
    /// additionally force-hide; the equip state already governs it").
    /// </summary>
    public readonly struct CompassRenderPlan
    {
        /// <summary>Whether the active SBPR map surface(s) should draw the compass north ring (iron bezel + N + ticks).</summary>
        public readonly bool ShowSurfaceRing;
        /// <summary>Whether the HUD compass overlay's <c>_content</c> should be force-hidden this tick (① "the needle goes away").</summary>
        public readonly bool HideHudNeedle;

        public CompassRenderPlan(bool showSurfaceRing, bool hideHudNeedle)
        {
            ShowSurfaceRing = showSurfaceRing;
            HideHudNeedle = hideHudNeedle;
        }
    }

    /// <summary>
    /// Pure decision logic for the Iron Compass → map-surface north ring. Engine-free (no UnityEngine /
    /// Valheim refs) so tests/CompassNorthGateTests.cs can gate the FULL truth table headless in CI —
    /// the load-bearing policy of card t_fb53c9e4 cannot silently regress.
    /// </summary>
    public static class CompassNorthGate
    {
        /// <summary>
        /// Resolve the render plan from the two world facts the overlay reads each tick plus the gated
        /// mode. <paramref name="surfaceShowing"/> = an SBPR map surface is up
        /// (<c>CartographyViewer.IsMinimapBound</c> for the carry-disc OR the full-map modal is active);
        /// <paramref name="compassWorn"/> = <c>SBPR_CompassHud.IsWearingCompass</c>. The truth table
        /// (impl-spec §5 — asserted exhaustively in tests/CompassNorthGateTests.cs, AT-COMPASS-GATE):
        ///
        ///   mode \ state        | worn=false (any surface) | worn=true, no surface | worn=true, surface showing
        ///   --------------------|--------------------------|-----------------------|----------------------------
        ///   HudOnly             | ring off / needle stays  | ring off / needle     | ring OFF / needle stays (escape hatch)
        ///   DiscWhenBound (def) | ring off / needle stays  | ring off / needle     | ring ON  / needle HIDES (① the needle "goes away")
        ///   Both                | ring off / needle stays  | ring off / needle     | ring ON  / needle STAYS (both render)
        ///
        /// When <paramref name="compassWorn"/> is false the surface stays north-blind (no ring) and the
        /// HUD needle is already hidden by the compass's own equip-gate, so HideHudNeedle is false
        /// (the equip state governs it — we don't additionally force-hide). The gate only ever ADDS a
        /// hide when a surface is consuming the north payoff under DiscWhenBound. This is the single
        /// highest-value invariant of M1; the §5 shared rule (north IFF worn) reconciles it with the
        /// Sunstone twin's LensHandoffDecision.
        /// </summary>
        public static CompassRenderPlan Resolve(bool surfaceShowing, bool compassWorn, CompassDiscModeEnum mode)
        {
            // North is a property of the COMPASS, never of the surface: no compass → north-blind, every
            // mode. The HUD needle's own equip-gate already hid it, so we add no force-hide here.
            if (!compassWorn)
                return new CompassRenderPlan(showSurfaceRing: false, hideHudNeedle: false);

            // No surface to draw on → the HUD needle is the north payoff, every mode (the fallback).
            if (!surfaceShowing)
                return new CompassRenderPlan(showSurfaceRing: false, hideHudNeedle: false);

            // Worn AND a surface is showing. The mode decides the HUD-vs-surface split.
            switch (mode)
            {
                case CompassDiscModeEnum.HudOnly:
                    // Escape hatch: ignore the surface, keep the HUD needle. No surface ring.
                    return new CompassRenderPlan(showSurfaceRing: false, hideHudNeedle: false);

                case CompassDiscModeEnum.Both:
                    // Supplement: the surface ring shows AND the HUD needle stays.
                    return new CompassRenderPlan(showSurfaceRing: true, hideHudNeedle: false);

                case CompassDiscModeEnum.DiscWhenBound:
                default:
                    // Default (Daniel ①): hand off — the surface ring shows, the HUD needle "goes away".
                    return new CompassRenderPlan(showSurfaceRing: true, hideHudNeedle: true);
            }
        }
    }
}
