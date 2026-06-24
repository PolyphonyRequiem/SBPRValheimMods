// ============================================================================
//  Trailborne — Cartography vanilla LOCATION/POI derive seam (card t_b5e535b0)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v2/planning/cartography-impl-spec.md §2M
//  Group-2 follow-up to the Group-1 boss/Hildir pin work (PR #263).
//
//  The live, server-global re-derive of vanilla location/POI icons (Haldor's
//  vendor, StartTemple, Hildir's camp, BogWitch, discovered POIs, and any
//  modded flagged location) for the SBPR local map — the SAME set the vanilla
//  minimap shows, so this is PARITY, not new information.
//
//  WHY A SIBLING OF ThreatMarkers (not a synthesized SurveyPin): this layer
//  re-derives a transient icon every rebuild and persists NOTHING. It mirrors
//  the threat-marker seam exactly (live collect → icon-only render → cleared
//  next rebuild), which buys four properties BY CONSTRUCTION:
//    • icon-only       — the collector returns only (pos, icon); there is no
//                        label channel, so K-C (no text) is structural.
//    • non-deletable   — these are NOT SurveyPins, never enter SurveyData.Pins,
//                        never serialize → SurveyData.WireVersion STAYS 1; the
//                        renderer sets raycastTarget=false so a TableEdit eraser
//                        click cannot hit one.
//    • atlas-safe      — the renderer uses Image.sprite + preserveAspect (Unity-
//                        native atlas handling), so the §2K.7 uvRect-crop concern
//                        never arises here.
//    • zero Group-1 conflict — touches none of ResolvePinSprite / SpawnPinMarker
//                        / CollectShareablePins / RemovePinNear.
//
//  K-A DERIVE PIPELINE (all-public, no reflection) — mirrors vanilla exactly:
//    1. ZoneSystem.GetLocationIcons(dict) fills pos→prefabName. This IS the
//       data-driven auto-icon set: the SERVER already filters
//       m_iconAlways || (m_iconPlaced && m_placed) inside it
//       (assembly_valheim.decompiled.cs:98066-98083); a CLIENT returns the RPC
//       cache (the identical server set). So we consume the filtered set
//       directly — we do NOT enumerate location names, read the flags, or
//       hardcode any location list. Modded flagged locations come for free.
//    2. Resolve the sprite by mirroring private Minimap.GetLocationIcon
//       (subsystems/Minimap.cs:1183-1193) against the PUBLIC, prefabName-keyed
//       Minimap.m_locationIcons table (Minimap.cs:235, List<LocationSpriteData>;
//       struct { public string m_name; public Sprite m_icon; } :113-118). NOTE
//       this is a DIFFERENT table from Group-1's PinType-keyed m_icons.
//       A struct miss yields default → m_icon == null → we skip that location,
//       exactly as vanilla's UpdateLocationPins only adds a pin if((bool)icon).
//    3. Guard Minimap.instance / ZoneSystem.instance null → empty; wrap in
//       try/catch + LogWarning like the WorldPins live-collect in RebuildOverlay.
//
//  Derive is GLOBAL here (GetLocationIcons has no radius concept); the
//  table-window 1000 m bound clip is applied by MapSurface.RebuildOverlay via
//  the SAME BoundedMapMath.InDisc the survey pins use — single clip site,
//  parity with the survey-pin loop.
//
//  CLEAN-SIDE (ADR-0001): every vanilla type touched (Minimap, ZoneSystem,
//  Sprite, LocationSpriteData) is base-game and fair to read/adapt. The vanilla
//  GetLocationIcon body was read and reproduced against the public table; no
//  copyrighted source is committed. This file is all SBPR-authored.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// One transient (per-rebuild, non-persisted) vanilla location/POI marker for the local
    /// map: a world position and the vanilla location icon to draw there. Deliberately icon-only
    /// — there is no label field, which is what makes the icon-only policy (K-C) structural.
    /// </summary>
    public readonly struct LocationMarker
    {
        /// <summary>World position of the location (X,Z used for the disc projection + clip).</summary>
        public readonly Vector3 WorldPos;
        /// <summary>The vanilla location icon (non-null by construction — null-icon locations are skipped at collect).</summary>
        public readonly Sprite Icon;

        public LocationMarker(Vector3 worldPos, Sprite icon)
        {
            WorldPos = worldPos;
            Icon = icon;
        }
    }

    /// <summary>
    /// Live, server-global derive of the vanilla auto-icon location set for the SBPR local map.
    /// Mirrors the <c>WorldPins.CollectInDiscPins</c> / <c>ThreatMarkers.Collect</c> pull idiom:
    /// the source of truth is the vanilla pipeline, and the overlay just asks "what locations
    /// does the server know right now?" each rebuild and renders the answer. Persists nothing
    /// (these are not SurveyPins), so the wire format never changes. Client-only in practice
    /// (the local map only exists on a client); safe to call anywhere — it no-ops without a
    /// Minimap / ZoneSystem instance.
    /// </summary>
    public static class LocationPins
    {
        // Reused across rebuilds to avoid per-rebuild GC churn (mirrors WorldPins' scratch
        // buffers). Single-threaded UI use — the modal and disc MapSurfaces call Collect
        // sequentially on the main thread, never concurrently.
        private static readonly Dictionary<Vector3, string> _iconScratch =
            new Dictionary<Vector3, string>();

        /// <summary>
        /// Clear <paramref name="into"/> and fill it with every server-known location that
        /// resolves a vanilla icon. GLOBAL (no bound clip — the caller applies the table-window
        /// <see cref="BoundedMapMath.InDisc"/> clip, parity with the survey pins). No-ops to an
        /// empty result without a live Minimap / ZoneSystem. Never throws out (guarded like the
        /// WorldPins live-collect).
        /// </summary>
        public static void Collect(List<LocationMarker> into)
        {
            into.Clear();
            var mm = Minimap.instance;
            var zs = ZoneSystem.instance;
            if (mm == null || zs == null) return; // headless / pre-Hud — nothing to render onto.

            try
            {
                _iconScratch.Clear();
                // The vanilla auto-icon set: server-filtered (m_iconAlways || (m_iconPlaced &&
                // m_placed)) or the client's RPC cache of that same set. We consume it directly.
                zs.GetLocationIcons(_iconScratch);

                foreach (var kv in _iconScratch)
                {
                    // Mirror Minimap.GetLocationIcon: a struct-Find miss → default → null icon →
                    // skip (vanilla's UpdateLocationPins only pins if ((bool)locationIcon)). The
                    // `is { }` pattern narrows to a non-null Sprite without a null-forgiving cast.
                    if (ResolveIcon(mm, kv.Value) is { } icon)
                        into.Add(new LocationMarker(kv.Key, icon));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/Cartography] LocationPins: live location-icon derive failed: {e.Message}");
            }
        }

        /// <summary>
        /// Mirror of private <c>Minimap.GetLocationIcon(string)</c> against the PUBLIC,
        /// prefabName-keyed <c>Minimap.m_locationIcons</c> table: first row whose
        /// <c>m_name</c> equals the prefab name wins; a miss (or a missing/null table) returns
        /// null. Plain loop rather than List.Find — no per-call closure allocation, and it
        /// reads the same as the vanilla foreach it reproduces.
        /// </summary>
        private static Sprite? ResolveIcon(Minimap mm, string prefabName)
        {
            var table = mm.m_locationIcons;
            if (table == null) return null;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].m_name == prefabName)
                    return table[i].m_icon;
            }
            return null;
        }
    }
}
