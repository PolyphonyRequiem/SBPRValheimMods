// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens → minimap handoff DECISION (engine-free)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §0/§3
//  Design   : docs/design/sunstone-lens-minimap-handoff.md (ACCEPTED, PR #214)
//  Card     : t_91e86951 (graduated from design card t_3129842a)
//
//  The PURE truth table for "given the live world state + the gated config knob,
//  which surface draws the Lens threat overlay, and is the ring's content visible?"
//  Daniel gated all three knobs 2026-06-20:
//    • MinimapHandoffMode = DiscWhenBound (ring = fallback-only; default)
//    • blip representation = dots + aggro-tint  (BlipStyle, separate enum)
//    • UNIVERSAL rule: ANY minimap present (SBPR carry-disc OR the vanilla corner
//      minimap in nomap-OFF) gets the handoff; the ring renders only when NO
//      minimap exists at all.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The surface cascade + the
//  MinimapHandoffMode interaction is the load-bearing decision of the whole card,
//  and it is exactly the kind of pure boolean logic that the repo's test suite can
//  gate headlessly (the DiscRingGeometry / BoundedMapMath / MapCaptionText
//  link-compile precedent — tests/SBPR.Trailborne.Tests.csproj). Keeping it free of
//  UnityEngine / HarmonyLib / Valheim types lets tests/LensHandoffDecisionTests.cs
//  assert the FULL truth table under net8.0, so a future edit that silently makes
//  the ring inert (the #209-adjacent failure class) or breaks the universal-rule
//  handoff fails CI instead of shipping. The overlay's Update() reads the live
//  game state (Minimap.instance, CartographyViewer.IsMinimapBound, the config
//  enum), reduces it to the two booleans this file consumes, and then renders —
//  the POLICY lives here; only the STATE READ + the RENDER live in the engine code.
//
//  Clean-side (ADR-0001): all SBPR-authored decision logic; references no vanilla
//  or third-party type. (The MinimapHandoffMode/BlipStyle enums live here too so
//  the Config bind in Plugin.cs and the engine-free test share one definition.)
// ============================================================================

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Replace-vs-supplement knob (design §4, Daniel-gated default <see cref="DiscWhenBound"/>).
    /// Live-tunable via Config so Daniel can flip the feel on a joined client (the banner-windsock
    /// pattern the ring's other knobs already use). NOTE the value name <c>DiscWhenBound</c> is a
    /// slight misnomer under the universal rule — it means "hand off whenever ANY minimap is
    /// present," not only the SBPR carry-disc. The name is kept stable so a Config key Daniel may
    /// already have bound does not churn (a rename to <c>MinimapWhenPresent</c> is a cosmetic
    /// follow-up, design §4 note).
    /// </summary>
    public enum MinimapHandoffMode
    {
        /// <summary>Ignore every minimap; the ring always renders (the escape hatch / today's pre-handoff behaviour).</summary>
        RingOnly,
        /// <summary>DEFAULT (Daniel). When a minimap is present the ring hides and the minimap surface shows threats; otherwise the ring is the fallback.</summary>
        DiscWhenBound,
        /// <summary>Ring AND the minimap surface both show threats whenever a minimap is present.</summary>
        Both,
    }

    /// <summary>
    /// How a threat draws on the minimap surfaces (design §3.3 geometry / Knob 2, Daniel-gated
    /// default <see cref="Dots"/>). Live Config enum so Daniel can compare in-game. The RING surface
    /// is unaffected — it always draws the full trophy art; this only styles the two minimap hosts,
    /// where every threat sits in the inner ~48% of the disc and trophy art is ~48 px-from-centre small.
    /// </summary>
    public enum BlipStyle
    {
        /// <summary>DEFAULT (Daniel). A small aggro-tinted dot — legible at minimap scale.</summary>
        Dots,
        /// <summary>The creature trophy sprite + aggro tint (richer, smaller-read).</summary>
        Trophy,
    }

    /// <summary>
    /// Which surface the Lens detection feed renders onto this tick. The two minimap surfaces are
    /// MUTUALLY EXCLUSIVE by vanilla construction — vanilla <c>Minimap.SetMapMode</c> forces
    /// <c>MapMode.None</c> whenever <c>Game.m_noMap</c> is set (decomp Minimap.SetMapMode), so the
    /// SBPR carry-disc (nomap-ON) and the vanilla corner minimap (nomap-OFF) can never both be live.
    /// </summary>
    public enum LensSurface
    {
        /// <summary>No minimap present anywhere → the camera-relative trophy ring is the fallback.</summary>
        Ring,
        /// <summary>nomap-ON + a local map bound + imprinted → the SBPR player-centred carry-disc.</summary>
        SbprDisc,
        /// <summary>nomap-OFF → the vanilla corner minimap (custom overlay, north-up).</summary>
        VanillaMinimap,
    }

    /// <summary>
    /// The resolved per-tick render plan: which booleans the overlay's Update() acts on after it has
    /// read the live world state. <see cref="RingContentVisible"/> drives the ring's <c>_content</c>
    /// SetVisible (NEVER the host — the #209 dead-Update-pump invariant, AT-LENS-DISC-PUMP).
    /// <see cref="FeedMinimap"/> is whether the active minimap surface should be fed blips this tick.
    /// </summary>
    public readonly struct LensRenderPlan
    {
        /// <summary>Whether the ring's <c>_content</c> child should be visible (its trophies drawn).</summary>
        public readonly bool RingContentVisible;
        /// <summary>Whether the active minimap surface (disc or vanilla) should receive the threat feed.</summary>
        public readonly bool FeedMinimap;
        /// <summary>Which minimap surface is the feed target when <see cref="FeedMinimap"/> is true.</summary>
        public readonly LensSurface MinimapTarget;

        public LensRenderPlan(bool ringContentVisible, bool feedMinimap, LensSurface minimapTarget)
        {
            RingContentVisible = ringContentVisible;
            FeedMinimap = feedMinimap;
            MinimapTarget = minimapTarget;
        }
    }

    /// <summary>
    /// Pure decision logic for the Sunstone Lens → minimap handoff. Engine-free (no UnityEngine /
    /// Valheim refs) so tests/LensHandoffDecisionTests.cs can gate the FULL truth table headless in
    /// CI — the load-bearing policy of card t_91e86951 cannot silently regress.
    /// </summary>
    public static class LensHandoffDecision
    {
        /// <summary>
        /// Resolve which surface is live from the two world facts the overlay reads each tick:
        /// <paramref name="sbprDiscBound"/> = <c>CartographyViewer.IsMinimapBound</c> (an SBPR carry-disc
        /// is showing — true only in nomap-ON + a local map bound + imprinted), and
        /// <paramref name="vanillaMinimapShowing"/> = the vanilla corner minimap is up
        /// (<c>Minimap.instance.m_mode == MapMode.Small</c>; true only in nomap-OFF, since
        /// <c>SetMapMode</c> forces None under <c>Game.m_noMap</c>). The SBPR disc wins the (impossible
        /// by construction, but defended) tie so the richer surface is preferred. No minimap → the ring.
        /// </summary>
        public static LensSurface ResolveSurface(bool sbprDiscBound, bool vanillaMinimapShowing)
        {
            if (sbprDiscBound) return LensSurface.SbprDisc;
            if (vanillaMinimapShowing) return LensSurface.VanillaMinimap;
            return LensSurface.Ring;
        }

        /// <summary>
        /// Apply the gated <see cref="MinimapHandoffMode"/> to a resolved surface, yielding the render
        /// plan. The truth table (design §4 / impl-spec §3.1):
        ///
        ///   mode \ surface     | Ring (no minimap)        | SbprDisc / VanillaMinimap
        ///   -------------------|--------------------------|----------------------------------------
        ///   RingOnly           | ring shows               | ring shows  (minimap suppressed)
        ///   DiscWhenBound (def)| ring shows               | ring HIDES; minimap shows threats
        ///   Both               | ring shows               | ring shows AND minimap shows threats
        ///
        /// The ring is the fallback whenever no minimap is present (surface == Ring) regardless of mode.
        /// When a minimap IS present, the ring's content visibility and whether the minimap is fed are
        /// the two independent outputs the overlay acts on. This is the single highest-value invariant
        /// of the card; it is asserted exhaustively in tests/LensHandoffDecisionTests.cs.
        /// </summary>
        public static LensRenderPlan Resolve(LensSurface surface, MinimapHandoffMode mode)
        {
            // No minimap anywhere → the ring is the only surface, every mode (the fallback).
            if (surface == LensSurface.Ring)
                return new LensRenderPlan(ringContentVisible: true, feedMinimap: false, minimapTarget: LensSurface.Ring);

            // A minimap is present (SBPR disc OR vanilla). The mode decides the split.
            switch (mode)
            {
                case MinimapHandoffMode.RingOnly:
                    // Escape hatch: ignore the minimap, keep the ring. Minimap NOT fed.
                    return new LensRenderPlan(ringContentVisible: true, feedMinimap: false, minimapTarget: surface);

                case MinimapHandoffMode.Both:
                    // Supplement: ring stays AND the minimap is fed.
                    return new LensRenderPlan(ringContentVisible: true, feedMinimap: true, minimapTarget: surface);

                case MinimapHandoffMode.DiscWhenBound:
                default:
                    // Default (Daniel): hand off — ring hides, the minimap surface shows threats.
                    return new LensRenderPlan(ringContentVisible: false, feedMinimap: true, minimapTarget: surface);
            }
        }

        /// <summary>
        /// One-shot convenience: resolve the surface from the two world facts AND apply the mode.
        /// Equivalent to <c>Resolve(ResolveSurface(...), mode)</c>; the overlay calls this each tick.
        /// </summary>
        public static LensRenderPlan Resolve(bool sbprDiscBound, bool vanillaMinimapShowing, MinimapHandoffMode mode)
            => Resolve(ResolveSurface(sbprDiscBound, vanillaMinimapShowing), mode);
    }
}
