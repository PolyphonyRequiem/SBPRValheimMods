using System;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Pure (Unity-free) caption/biome text composition for the two cartography surfaces
    /// (local-map-biome-indicator-impl-spec §3, disc-name-hint-impl-spec §3.4). Deliberately
    /// references no UnityEngine / Valheim type so the rich-text assembly + the literal-leak
    /// guard can be exercised headless — the same link-compile pattern as
    /// <see cref="SBPR.Trailborne.Features.Signs.SignHoverHintText"/> and
    /// <see cref="BoundedMapMath"/>.
    ///
    /// The Unity-bound adapter (<c>MapSurface.UpdateCaption</c> / <c>CurrentBiomeNameOrNull</c>)
    /// does all the live work — read <c>Player.GetCurrentBiome()</c>, build the <c>$biome_*</c>
    /// token, call <c>Localization.Localize</c> — then hands the already-localized strings here
    /// for the pure string assembly. Both the shipped surface and the self-test compile THIS
    /// source, so the asserted stack shape + None/unlocalized omission can never drift from what
    /// ships.
    /// </summary>
    public static class MapCaptionText
    {
        /// <summary>
        /// True when a <c>Localize("$biome_xxx")</c> result is an UNRESOLVED biome token — i.e. the
        /// value Localization hands back unchanged when no key matched (the literal "$biome_xxx"),
        /// or an empty string. The caller treats this as "no biome name" → omit the line / hide the
        /// label so a raw <c>$biome_*</c> token never leaks to the player (the 2026-06-05 sign-bug
        /// class; biome-indicator-impl-spec §3.5). The <c>Heightmap.Biome.None</c> guard lives on
        /// the Unity side (no <c>$biome_none</c> token exists); this catches every other unmapped
        /// or future-enum value generically.
        /// </summary>
        public static bool IsUnresolvedBiomeToken(string? localized)
        {
            return string.IsNullOrEmpty(localized)
                   || localized!.StartsWith("$biome_", StringComparison.Ordinal);
        }

        /// <summary>
        /// Compose the under-disc caption stack as a single multi-line rich-text string, top→bottom:
        /// <c>name / biome / hint</c> (biome-indicator-impl-spec §3.1). Each of <paramref name="name"/>
        /// and <paramref name="biome"/> is conditionally included — pass null/empty to OMIT that line
        /// (no empty row, no stray newline). The <paramref name="hint"/> line is ALWAYS present (the
        /// static <c>[M] Read map</c>), so the stack never collapses to nothing.
        ///
        /// All four name×biome combinations degrade cleanly:
        /// <list type="bullet">
        ///   <item><b>name + biome:</b> <c>name \n biome \n hint</c> — the full 3-line stack.</item>
        ///   <item><b>name, no biome:</b> <c>name \n hint</c> — byte-identical to the PR #205
        ///     two-line caption (AT-CAPTION-NAME-HINT-INTACT regression when biome unavailable).</item>
        ///   <item><b>no name, biome:</b> <c>biome \n hint</c> — a pre-naming imprint that knows its
        ///     biome.</item>
        ///   <item><b>no name, no biome:</b> <c>hint</c> — the bare hint, today's no-name fallback.</item>
        /// </list>
        /// Font sizes are passed in (disc-name-hint §3.4 calibration knobs) and emitted as per-span
        /// <c>&lt;size&gt;</c> tags — the single <c>Text</c> renders all three rows at their own size.
        /// </summary>
        public static string ComposeDiscCaption(
            string? name, string? biome, string hint, int nameSize, int biomeSize, int hintSize)
        {
            string s = string.Empty;
            if (!string.IsNullOrEmpty(name))
                s += "<size=" + nameSize + ">" + name + "</size>\n";
            if (!string.IsNullOrEmpty(biome))
                s += "<size=" + biomeSize + ">" + biome + "</size>\n";
            s += "<size=" + hintSize + ">" + hint + "</size>";
            return s;
        }
    }
}
