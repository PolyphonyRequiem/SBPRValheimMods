// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens → minimap handoff DECISION policy
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-minimap-handoff.md (ACCEPTED — Daniel
//             gated all 3 knobs 2026-06-20)
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §3 / §AT
//  Card     : t_54c989d3 (impl) ← t_3129842a (design)
//
//  PURE, UNITY-FREE decision logic for the §4 MinimapHandoffMode load-bearer —
//  "given the configured mode and whether ANY minimap is present, does the
//  camera-relative RING show threats, and does a present minimap host them?"
//  Deliberately references NO UnityEngine / HarmonyLib / Valheim type so it
//  link-compiles into the headless xUnit suite (tests/SunstoneHandoffPolicyTests.cs)
//  exactly like BoundedMapMath / DiscRingGeometry / MapCaptionText — the SAME
//  source ships in the mod AND is asserted in CI, so the locked AT-LENS-DISC-HANDOFF
//  decision table can never silently drift.
//
//  The "is a minimap present?" PREDICATE itself reads live Unity state
//  (CartographyViewer.IsMinimapBound for the nomap-ON SBPR disc OR the vanilla
//  small minimap showing for nomap-OFF) and therefore lives in the overlay, NOT
//  here — this class only consumes the resulting bool, keeping it engine-free.
//
//  Clean-side (ADR-0001): all SBPR-authored; no game/third-party code involved.
// ============================================================================

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The §4 load-bearer (🟢 Daniel-gated default <see cref="DiscWhenBound"/>). Selects WHERE the
    /// Sunstone Lens' hostile detection renders. Live <c>Config.Bind</c> ("SunstoneLens" section) so
    /// Daniel can flip the other two values on a joined client without a rebuild (banner-windsock
    /// pattern, like <c>ShowEmptyRing</c>). The value-name <c>DiscWhenBound</c> is kept stable to
    /// avoid churning a Config key Daniel may already have bound, even though under the universal
    /// rule it means "hand off whenever ANY minimap is present," not just the SBPR disc (design §4).
    /// </summary>
    public enum MinimapHandoffMode
    {
        /// <summary>Ignore the minimap; the camera-relative ring ALWAYS renders threats (the escape hatch).</summary>
        RingOnly,

        /// <summary>
        /// 🟢 DEFAULT (Daniel 2026-06-20). When ANY minimap is present (SBPR carry-disc OR the vanilla
        /// small minimap), the ring's threat visuals HIDE and the minimap surface shows the threats;
        /// when no minimap is present at all, the ring is the fallback surface.
        /// </summary>
        DiscWhenBound,

        /// <summary>When a minimap is present, the ring AND the minimap BOTH render threats (supplement, not replace).</summary>
        Both,
    }

    /// <summary>
    /// 🟢 Blip representation on the MINIMAP surfaces (disc + vanilla overlay), Daniel-gated default
    /// <see cref="DotsAndTint"/> (§3.3 geometry: every threat sits within the inner ~48 % of the disc
    /// where trophy art is too small to read, so a tinted dot reads cleaner). Live Config enum so
    /// Daniel can A/B <c>DotsAndTint</c> vs <c>TrophyArt</c> in-game. Does NOT affect the camera-relative
    /// RING, which keeps its existing trophy+stars+tint rendering byte-unchanged (the ring's icons are
    /// full-size, not disc-tiny).
    /// </summary>
    public enum BlipStyle
    {
        /// <summary>🟢 DEFAULT — a small dot tinted by the aggro colour (yellow/orange/red). Reads cleanly at minimap scale.</summary>
        DotsAndTint,

        /// <summary>The creature's trophy sprite + star pips (the ring's representation), tinted by aggro. Daniel's A/B option.</summary>
        TrophyArt,
    }

    /// <summary>
    /// Pure decision table for the handoff (§4 / AT-LENS-DISC-HANDOFF). Two questions, both functions
    /// of (mode, anyMinimapPresent) ONLY — no Unity state, no side effects — so they are exhaustively
    /// unit-testable headless and cannot drift from the locked design.
    /// </summary>
    public static class SunstoneHandoffPolicy
    {
        // ── Daniel-gated DEFAULTS (design §9, 🟢 ANSWERED 2026-06-20). Kept HERE, in the engine-free
        //    policy, so they (a) live with the decision they parameterize and (b) are CI-assertable in
        //    the headless test suite. SunstoneLensHudOverlay's public Default* consts forward to these
        //    (a const can be initialized from another const), and Plugin.Awake binds the live Config
        //    enum off those — one source of truth, no UnityEngine dependency. ──

        /// <summary>🟢 Daniel-gated default handoff mode: <see cref="MinimapHandoffMode.DiscWhenBound"/>.</summary>
        public const MinimapHandoffMode DefaultMode = MinimapHandoffMode.DiscWhenBound;

        /// <summary>🟢 Daniel-gated default minimap blip style: <see cref="BlipStyle.DotsAndTint"/>.</summary>
        public const BlipStyle DefaultBlipStyle = BlipStyle.DotsAndTint;

        /// <summary>
        /// Does the camera-relative RING render its threat visuals this frame?
        ///   • <see cref="MinimapHandoffMode.RingOnly"/>      → always (the escape hatch).
        ///   • <see cref="MinimapHandoffMode.DiscWhenBound"/> → only when NO minimap is present (fallback).
        ///   • <see cref="MinimapHandoffMode.Both"/>          → always (supplements the minimap).
        ///
        /// 🔴 #209 NOTE: a <c>false</c> here means "suppress the ring's VISUALS," NOT "stop the
        /// detection pump." The caller hides the ring's <c>_content</c> child via the existing
        /// <c>SetVisible(false)</c> path while the overlay's <c>Update</c> pump keeps sweeping —
        /// the minimap surfaces depend on that pump staying alive for their blip feed
        /// (AT-LENS-DISC-PUMP). Deactivating the host GameObject would freeze the pump dead (the
        /// exact PR #209 / t_d5949685 bug).
        /// </summary>
        public static bool RingShowsThreats(MinimapHandoffMode mode, bool anyMinimapPresent)
        {
            switch (mode)
            {
                case MinimapHandoffMode.RingOnly:
                    return true;
                case MinimapHandoffMode.Both:
                    return true;
                case MinimapHandoffMode.DiscWhenBound:
                default:
                    return !anyMinimapPresent;
            }
        }

        /// <summary>
        /// Does a PRESENT minimap surface (SBPR disc or vanilla overlay) host threats this frame?
        ///   • <see cref="MinimapHandoffMode.RingOnly"/>      → never (threats stay on the ring).
        ///   • <see cref="MinimapHandoffMode.DiscWhenBound"/> → yes, whenever a minimap is present.
        ///   • <see cref="MinimapHandoffMode.Both"/>          → yes, whenever a minimap is present.
        ///
        /// When <paramref name="anyMinimapPresent"/> is false this is always false — there is no
        /// surface to host them, so the ring (per <see cref="RingShowsThreats"/>) is the only surface.
        /// </summary>
        public static bool MinimapShowsThreats(MinimapHandoffMode mode, bool anyMinimapPresent)
        {
            if (!anyMinimapPresent) return false;
            switch (mode)
            {
                case MinimapHandoffMode.RingOnly:
                    return false;
                case MinimapHandoffMode.DiscWhenBound:
                case MinimapHandoffMode.Both:
                    return true;
                default:
                    return true;
            }
        }
    }
}
