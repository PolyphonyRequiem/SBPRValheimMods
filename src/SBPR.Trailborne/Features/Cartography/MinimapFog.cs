// ============================================================================
//  Trailborne v2 cartography — shared cached-reflection reader for Minimap.m_explored
// ----------------------------------------------------------------------------
//  Extracted from SurveyorTableTag.ReadExplored (live-update-cartography-impl-spec
//  §2.1 step 2) so the TWO consumers of the personal global fog — the Table
//  contribute path (SurveyorTableTag, §3/§4) and the live field-WRITE path
//  (LiveFieldWrite, §2) — read Minimap.m_explored through ONE cached FieldInfo
//  instead of each owning its own reflection path. One source of truth for "how we
//  read the private fog," cached once per process.
//
//  Clean-side (ADR-0001): m_explored is a stable base-game field
//  (bool[m_textureSize²], decomp Minimap :46910); reflection on a private vanilla
//  field is the spike-established idiom (the same one SurveyorTableTag used for both
//  m_explored and m_pins). We read it; we never write it.
// ============================================================================

using System.Reflection;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Cached-reflection accessor for the private <c>Minimap.m_explored</c> personal-fog
    /// array. Shared by <see cref="SurveyorTableTag"/> (survey contribute) and
    /// <see cref="LiveFieldWrite"/> (the live field-write), so both read the same field
    /// through one lazily-resolved, process-cached <see cref="FieldInfo"/>.
    /// </summary>
    internal static class MinimapFog
    {
        // Resolved once, lazily, from the live Minimap type (private vanilla field).
        private static FieldInfo? _fiExplored;

        /// <summary>
        /// The live personal global-fog array (length <c>m_textureSize²</c>, true = explored),
        /// or null on a headless/dedicated context or if a future game patch ever removed the
        /// field. Pure read; never throws (a missing field yields null, which callers already
        /// treat as "no fog this tick").
        /// </summary>
        public static bool[]? ReadExplored(Minimap mm)
        {
            if (mm == null) return null;
            if (_fiExplored == null)
                _fiExplored = typeof(Minimap).GetField("m_explored", BindingFlags.Instance | BindingFlags.NonPublic);
            return _fiExplored?.GetValue(mm) as bool[];
        }
    }
}
