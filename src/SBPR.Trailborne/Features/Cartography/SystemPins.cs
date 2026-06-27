using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Live, viewer-local derive of the holder's vanilla SYSTEM map pins (Boss + Hildir1–3) for the
    /// SBPR local map. Mirrors the WorldPins.CollectInDiscPins pull idiom — "what system pins does the
    /// local player's vanilla pin list hold right now?" each rebuild — but its source is the PRIVATE
    /// Minimap.m_pins (read via the cached-FieldInfo reflection idiom SurveyorTableTag.ReadPins uses),
    /// because m_pins has no public accessor. Emits SurveyPins (NOT icon-only LocationMarkers) so the
    /// caller AddIfNews them into the same `rendered` list the frozen survey pins use, and they render
    /// through the EXISTING SpawnPinMarker — inheriting the §2K.7 boss icon, the §2K.2 localized label,
    /// and PinIconPx sizing, so a live boss pin is pixel-identical to a frozen one. Persists nothing
    /// (never enters SurveyData.Pins) → SurveyData.WireVersion stays 1.
    /// </summary>
    public static class SystemPins
    {
        private static FieldInfo? _fiPins;

        /// <summary>
        /// Clear <paramref name="into"/> and fill it with the local player's vanilla system pins
        /// (Boss + Hildir1–3, m_save==true) as SurveyPins. No-ops to empty without a live Minimap.
        /// Never throws out (guarded like the WorldPins live-collect). GLOBAL — the caller applies the
        /// table-window BoundedMapMath.InDisc clip (parity with the survey pins).
        /// </summary>
        public static void Collect(List<SurveyPin> into)
        {
            into.Clear();
            var mm = Minimap.instance;
            if (mm == null) return; // headless / pre-Hud — nothing to read.

            try
            {
                if (_fiPins == null)
                    _fiPins = typeof(Minimap).GetField(
                        "m_pins", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_fiPins?.GetValue(mm) is not List<Minimap.PinData> pins) return;

                foreach (var pin in pins)
                {
                    if (pin == null) continue;
                    if (!pin.m_save) continue;                                  // match CollectShareablePins
                    if (!SurveyData.IsSystemPin((int)pin.m_type)) continue;     // Boss/Hildir1–3 ONLY
                    into.Add(new SurveyPin(pin.m_name, (int)pin.m_type, pin.m_pos,
                                           pin.m_checked, pin.m_ownerID));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/Cartography] SystemPins: live system-pin derive failed: {e.Message}");
            }
        }
    }
}
