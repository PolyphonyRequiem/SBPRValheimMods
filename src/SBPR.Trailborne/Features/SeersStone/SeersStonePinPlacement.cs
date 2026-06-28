// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone PIN PLACEMENT (engine-coupled helper)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §6. The Minimap I/O + the reflection read of the
//           private pin list, shared by the wisp's vanilla Interact path (WispBehaviour).
//
//  Lifted OUT of the retired Alt+E PinByLookInput Player.Update postfix so the merge-dedup
//  snapshot (SnapshotPins → the engine-free ExistingPin list fed to SeersStonePinDecision)
//  and the Minimap.AddPin/DiscoverLocation wrapper are NOT duplicated or lost when the
//  raycast input path goes away. The pin DECISION stays in the engine-free
//  SeersStonePinDecision (CI-gated); this is only the engine-coupled placement + read.
//
//  Clean-side (ADR-0001): SBPR-authored; reads Minimap via public API + one reflection read
//  of the private m_pins list (the established SBPR idiom — Minimap exposes no pin enumerator).
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Engine-coupled pin helpers for the Seer's Stone, shared by <see cref="WispBehaviour.Interact"/>.
    /// Places the resolved <see cref="PinPlan"/> on the Minimap and snapshots the current pins for the
    /// engine-free merge check. Client-only (Minimap exists only on a client).
    /// </summary>
    internal static class SeersStonePinPlacement
    {
        /// <summary>
        /// Place the planned pin. Pickable → <c>Minimap.AddPin</c> (the abundance pin — one pin per
        /// patch, saved + persistent). Location → <c>Minimap.DiscoverLocation</c> (the vanilla
        /// "shown on map" path). No-op if the Minimap is absent (dedicated server / not yet up).
        /// </summary>
        public static void PlacePin(PinPlan plan, Vector3 pos)
        {
            if (Minimap.instance == null) return;
            if (plan.Kind == WispHitKind.Location)
                Minimap.instance.DiscoverLocation(pos, Minimap.PinType.Icon3, plan.Label, showMap: false);
            else
                Minimap.instance.AddPin(pos, Minimap.PinType.Icon3, plan.Label, save: true, isChecked: false);
            Plugin.Log.LogInfo($"[Trailborne/SeersStone] Pinned '{plan.Label}' ({plan.Kind}) at {pos}.");
        }

        // Minimap.m_pins is private — read it via reflection, the established SBPR idiom
        // (SurveyorTableTag.ReadPins). Cached FieldInfo.
        private static System.Reflection.FieldInfo? _fiPins;

        /// <summary>
        /// Snapshot existing pins as the engine-free <see cref="ExistingPin"/> list for the merge check.
        /// Never throws — a snapshot error must never block a pin (returns what it has).
        /// </summary>
        public static IReadOnlyList<ExistingPin> SnapshotPins()
        {
            var list = new List<ExistingPin>();
            try
            {
                if (Minimap.instance == null) return list;
                if (_fiPins == null)
                    _fiPins = typeof(Minimap).GetField("m_pins",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var pins = _fiPins?.GetValue(Minimap.instance) as List<Minimap.PinData>;
                if (pins == null) return list;
                foreach (var pin in pins)
                {
                    if (pin == null || pin.m_name == null) continue;
                    list.Add(new ExistingPin(pin.m_name, new Vec3(pin.m_pos.x, pin.m_pos.y, pin.m_pos.z)));
                }
            }
            catch { /* never let a snapshot error block a pin */ }
            return list;
        }
    }
}
