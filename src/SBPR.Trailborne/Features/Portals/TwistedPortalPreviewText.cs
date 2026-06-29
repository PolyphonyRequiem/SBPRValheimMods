// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal look-to-aim FOOD-IMPACT PREVIEW TEXT
//  (engine-free formatter)
// ----------------------------------------------------------------------------
//  Card     : t_d9ea1b2c (L3) — overlay selection-highlight + food-impact preview.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §7 (interactive overlay),
//             §5 / Beat 3 (the read-only PreviewJump readout, "the impact to food").
//  Design   : Daniel 2026-06-27 — "a visual indicator shows the target clearly, as
//             well as the impact to food."
//
//  WHAT THIS IS. The PURE formatting of the read-only food-impact preview that the
//  overlay renders on the AIMED destination label (Beat 3). Given the numbers the
//  non-mutating TwistedPortalEnergy.PreviewJump already computes (belly range, the
//  shortfall berries needed, the berries the player holds, reachability), it returns
//  the two short lines drawn under the aimed rune label: the belly range, and a
//  one-line verdict (in range / need N berries / too far). "Keep it legible, not a
//  tutorial wall" (card) — two lines, no numbers the player can't act on.
//
//  🔴 COLORBLIND-SAFE BY CONSTRUCTION (Daniel is colorblind — never gate meaning on
//  hue). Every verdict is stated IN WORDS ("in range" / "need 3 berries" / "too far"),
//  not by a red/green tint. The overlay's highlight COLOUR is a secondary cue only;
//  this text (plus the fact the preview block appears under exactly one label) is the
//  primary, hue-independent "this is the selected destination + here's the cost" signal.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The text rules are pure string logic — the
//  same shape the repo CI-gates headless (the TwistedPortalOverlayModel / AimPickMath /
//  PortalEnergyMath link-compile precedent, tests/SBPR.Trailborne.Tests.csproj). Keeping
//  it free of UnityEngine / Valheim types lets tests/TwistedPortalPreviewTextTests.cs
//  pin the wording (singular "1 berry" vs plural, the three verdict branches, the
//  belly-range line) under net8.0 in CI, so a careless edit to the readout trips CI
//  instead of shipping a confusing label. The ENGINE side (calling PreviewJump, reading
//  the live belly, applying the highlight material/scale) lives in TwistedPortalOverlay.cs.
//
//  Clean-side (ADR-0001): all SBPR-authored logic; references no vanilla or third-party
//  type. Reuses TwistedPortalOverlayModel.FormatDistance (same engine-free assembly) so
//  distances read identically to the rest of the overlay ("142m" / "1.4km", invariant).
// ============================================================================

using System.Globalization;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// Pure formatter for the look-to-aim food-impact preview block (spec §5 / Beat 3). The overlay
    /// computes the read-only <c>TwistedPortalEnergy.JumpPreview</c> for the aimed destination and
    /// hands its primitive fields here; this returns the two short lines (belly range + verdict) that
    /// render under the aimed rune label. Engine-free + CI-gated (AT-FOOD-PREVIEW's text contract).
    /// </summary>
    public static class TwistedPortalPreviewText
    {
        /// <summary>
        /// Build the food-impact preview block for the aimed destination label: a belly-range line
        /// plus a one-line verdict, joined by a newline (so it appends cleanly under the rune+distance
        /// lines on the billboarded label). The three verdict branches mirror the cost model:
        ///   • <paramref name="bellyCovers"/> → "in range" (the belly food alone covers the jump; the
        ///     player arrives weaker in proportion to distance, but spends zero berries).
        ///   • shortfall + <paramref name="reachable"/> → "need N berries (have M)" (the jump burns the
        ///     shortfall in Bukeberries and arrives food-empty; the player holds enough).
        ///   • shortfall + not reachable → "too far: N berries (have M)" (more berries than held).
        /// Reachability is stated in WORDS, never by colour (Daniel is colourblind) — the overlay's
        /// highlight tint is a secondary cue only.
        /// </summary>
        public static string BuildFoodPreview(
            float distanceMeters,
            float bellyRangeMeters,
            bool bellyCovers,
            int berriesNeeded,
            int berriesHeld,
            bool reachable)
        {
            string belly = "belly " + TwistedPortalOverlayModel.FormatDistance(bellyRangeMeters);

            string verdict;
            if (bellyCovers)
                verdict = "in range";
            else if (reachable)
                verdict = "need " + Berries(berriesNeeded) + " (have " + Count(berriesHeld) + ")";
            else
                verdict = "too far: " + Berries(berriesNeeded) + " (have " + Count(berriesHeld) + ")";

            return belly + "\n" + verdict;
        }

        /// <summary>"1 berry" vs "N berries" — singular/plural so the readout never reads "1 berries".</summary>
        private static string Berries(int n) =>
            n == 1 ? "1 berry" : Count(n) + " berries";

        /// <summary>Invariant-culture integer (so a comma-decimal locale never mangles the count).</summary>
        private static string Count(int n)
        {
            if (n < 0) n = 0;
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
