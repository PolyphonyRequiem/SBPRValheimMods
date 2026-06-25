// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal proximity-overlay MODEL (engine-free)
// ----------------------------------------------------------------------------
//  Card     : t_e732bd8b (C3) — the on-step through-terrain portal-name overlay.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §7 (the overlay).
//  Design   : docs/design/nomap.md §7 ("on-step shows visible portal names").
//
//  This is the PURE selection/formatting logic behind the on-step overlay: which
//  nearby Twisted Portals get a floating label this refresh (nearest-N within the
//  overlay radius, optionally skipping unnamed ones), and how each label's text
//  reads (rune name + a formatted distance). It is the POLICY; the RENDER (the
//  world-space Canvas + UI.Text + the through-terrain ZTest-Always material + the
//  ZDO walk that feeds it) lives in TwistedPortalOverlay.cs.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The overlay itself is the single most
//  visual deliverable in the feature — "does it read right through terrain" is a
//  GPU-client eyeball that CANNOT be verified headless (the card says so). But the
//  selection + text math CAN be: it is exactly the pure-logic shape the repo's test
//  suite gates headlessly (the SunstoneHaloGeometry / DiscRingGeometry /
//  LensHandoffDecision link-compile precedent — tests/SBPR.Trailborne.Tests.csproj).
//  Keeping it free of UnityEngine / Valheim types lets tests/TwistedPortalOverlayModelTests.cs
//  assert the nearest-N cap, the radius filter, the unnamed-skip, and the distance
//  formatting under net8.0 in CI — so the parts that aren't an eyeball don't regress.
//
//  Clean-side (ADR-0001): all SBPR-authored logic; references no vanilla or
//  third-party type. The through-terrain LABEL idiom referenced from
//  BugattiBoys/PortalIndicator is reproduced from Unity uGUI primitives only (in
//  the render file) — zero gameplay code read or copied (AT-VANILLA-ONLY).
// ============================================================================

using System.Collections.Generic;
using System.Globalization;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// One overlay candidate's pure, engine-free facts: how far the portal is from the player
    /// (metres) and whether it carries a rune name. The render side keeps the parallel world
    /// position + rune string; the model only needs these two numbers to decide inclusion + order.
    /// </summary>
    public readonly struct OverlayCandidate
    {
        /// <summary>Metres from the local player to this Twisted Portal.</summary>
        public readonly float Distance;

        /// <summary>False for an unnamed portal (empty <c>sbpr_rune_name</c>) — it can never pair,
        /// so it is shown only when <c>includeUnnamed</c> is set (it's informational, not a destination).</summary>
        public readonly bool HasRune;

        public OverlayCandidate(float distance, bool hasRune)
        {
            Distance = distance;
            HasRune = hasRune;
        }
    }

    /// <summary>
    /// Pure selection + label-text math for the on-step Twisted Portal overlay (spec §7).
    /// Engine-free (no UnityEngine / Valheim refs) so tests/TwistedPortalOverlayModelTests.cs can
    /// gate the nearest-N / radius / unnamed-skip / distance-format contract headless in CI. The
    /// render loop (TwistedPortalOverlay) reads the live ZDO set + camera and only consumes what
    /// this returns; the POLICY lives here, the RENDER lives in engine code.
    /// </summary>
    public static class TwistedPortalOverlayModel
    {
        // ── Defaults (single source of truth; Plugin binds live ConfigEntry mirrors so Daniel
        //    converges the feel on a joined client without a rebuild — the banner-windsock pattern
        //    the Sunstone/Compass overlays use. ?.Value-read so a no-Plugin unit context falls back
        //    to these consts). All flagged for the in-game eyeball pass (AT-OVERLAY). ──
        public const int   DefaultMaxLabels      = 12;     // horde guard / readability cap (pooled nearest-N)
        public const float DefaultOverlayRadius   = 300f;   // metres; the design's 300 m reach (best-effort, §2 client-window-limited)
        public const float DefaultProximityRange  = 3f;     // metres to a portal that toggles the overlay visible (spec §7.3)
        public const float DefaultLabelScale      = 1.2f;   // world-metres the label maps to (eyeball tunable)
        public const float DefaultLabelHeight     = 3.5f;   // metres above the portal the label floats (clears the ~3 m ring)
        public const bool  DefaultShowUnnamed     = true;   // show unnamed portals with a placeholder (informational)
        public const bool  DefaultShowDistance    = true;   // append a formatted distance under the rune
        public const bool  DefaultThroughTerrain  = true;   // the ZTest-Always trick (the headline; Daniel A/B's it in-game)

        /// <summary>Shown in place of a rune for an unnamed portal (informational — it can't pair until named).</summary>
        public const string UnnamedPlaceholder = "(unnamed)";

        /// <summary>
        /// Format a distance for a label: whole metres under 1 km (<c>"142m"</c>), one-decimal
        /// kilometres at/above 1 km (<c>"1.4km"</c>). Invariant-culture so a comma-decimal locale
        /// never turns <c>1.4km</c> into <c>1,4km</c>. Negative inputs clamp to <c>"0m"</c>.
        /// </summary>
        public static string FormatDistance(float meters)
        {
            if (meters < 0f) meters = 0f;
            if (meters < 1000f)
            {
                int m = (int)(meters + 0.5f);
                return m.ToString(CultureInfo.InvariantCulture) + "m";
            }
            float km = meters / 1000f;
            return km.ToString("0.0", CultureInfo.InvariantCulture) + "km";
        }

        /// <summary>
        /// Build one floating label's text: the rune name (or <see cref="UnnamedPlaceholder"/> when
        /// the portal is unnamed / blank), optionally with a formatted distance on a second line.
        /// Two short lines read cleanly on a billboarded world-space label (rune on top, range below).
        /// </summary>
        public static string BuildLabel(string rune, bool hasRune, float meters, bool showDistance)
        {
            string name = hasRune && !string.IsNullOrEmpty(rune) ? rune : UnnamedPlaceholder;
            if (!showDistance) return name;
            return name + "\n" + FormatDistance(meters);
        }

        /// <summary>
        /// Pick which candidates get a label this refresh and in what order: every portal within
        /// <paramref name="radius"/> metres (and, when <paramref name="includeUnnamed"/> is false,
        /// only the named ones), sorted nearest-first, capped at <paramref name="maxLabels"/>.
        /// Writes the chosen indices (into <paramref name="candidates"/>) into <paramref name="into"/>,
        /// which is cleared first. Stable: ties on distance keep input order. Pure — no allocation
        /// beyond the caller's reused <paramref name="into"/> list (the render side reuses it).
        /// </summary>
        public static void SelectNearest(
            IReadOnlyList<OverlayCandidate> candidates,
            float radius,
            int maxLabels,
            bool includeUnnamed,
            List<int> into)
        {
            into.Clear();
            if (candidates == null || candidates.Count == 0 || maxLabels <= 0) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                OverlayCandidate c = candidates[i];
                if (c.Distance > radius) continue;            // outside the overlay reach
                if (!c.HasRune && !includeUnnamed) continue;  // unnamed skipped unless asked for
                into.Add(i);
            }

            // Nearest-first; stable tie-break on original index (so the cap keeps the closest N
            // deterministically across refreshes — no flicker between two equidistant portals).
            into.Sort((a, b) =>
            {
                int cmp = candidates[a].Distance.CompareTo(candidates[b].Distance);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            if (into.Count > maxLabels)
                into.RemoveRange(maxLabels, into.Count - maxLabels);
        }
    }
}
