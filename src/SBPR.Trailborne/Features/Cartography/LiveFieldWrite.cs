// ============================================================================
//  Trailborne v2 cartography — the LIVE FIELD-WRITE axis (the real fix for issue 5)
// ----------------------------------------------------------------------------
//  live-update-cartography-impl-spec.md §2/§3/§6 (design map-provider-model.md
//  §3.2a/§4.0a/§5/§6, PR #266, Daniel 2026-06-24). Card t_9c54d492.
//
//  WHAT THIS DOES (the bug it fixes — issue 5, recurring playtest):
//    "local map(s) data don't update while travelling when the cartographer's
//     tools are equipped."
//  It never worked because NOTHING wrote a Local Map's blob except LocalMap.Imprint
//  (a birth gesture at the Table). This class is the missing WRITE path: while the
//  Cartographer's Kit is worn, on a ~2 s sub-throttle, it stamps the revealed ground
//  into EVERY carried, imprinted Local Map whose 1000 m region contains the player —
//  by OR-merging the windowed global fog into each map's stored SurveyData blob and
//  rewriting m_customData[sbpr_map_blob] (LocalMap.WriteSurveyBlob, §2.4). The blob
//  is the truth the viewer already re-reads every 0.25 s (LocalMapController), so a
//  grown blob auto-reflects on the disc + full view with NO render change (§7).
//
//  THE TWO AXES (design §3.2a) — this class owns ONLY the WRITE axis:
//    • RENDER (disc + full view): keyed on EQUIP, set size 1 — LocalMapController._provider.
//    • WRITE  (receives field survey): keyed on HOLD + Kit, set size PLURAL — THIS CLASS.
//  The write-set is read DIRECTLY from inventory each tick; it does NOT consult the
//  render-provider (§4 independence) — an unequipped carried in-region map is written
//  exactly like the equipped one.
//
//  PERF MODEL — direct-blob-mutation with a dirty-check (§3, LOCKED). The blob is
//  sub-KB (≈125 B raw, "well under 1 KB" compressed — design §3.3), the cadence is
//  low (new ground at most every m_exploreInterval = 2 s, only inside a map's disc),
//  and the §2.3 MergeFrom(out changed) dirty-check makes standing still / re-covering
//  known ground / Kit-off cost ZERO reserializes. No RAM working-set: the blob being
//  the single source of truth is exactly why the viewer auto-reflects with no code,
//  and a dropped/traded/dead map's item-ZDO already carries the accumulated work with
//  no flush hook (§3 point 5). The in-RAM working-set is the DOCUMENTED DEFERRED
//  optimization — reach for it ONLY if a future finer resolution or pathological
//  carried-map counts make per-tick serialization measurable in a profile (§3).
//
//  Client-only by construction: reads Minimap.m_explored (client-only) and writes the
//  local player's own carried items' m_customData (rides the local profile save — no
//  networked ZDO while carried, design §3.1 finding 1). The dedicated server has no
//  Minimap, so Tick bails at the null check.
//
//  Clean-side (ADR-0001): reads the base-game Minimap fog + own math/storage. Additive
//  (ADR-0006). logs-green ≠ playable — Daniel verifies AT-LIVE-* in-game.
// ============================================================================

using System;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// The live field-WRITE axis (impl-spec §2): per throttled tick, stamp the Kit-revealed
    /// ground into every carried imprinted in-region Local Map's stored blob (plural write-set),
    /// dirty-checked. Static + driven from <see cref="LocalMapController"/>'s existing poll. See
    /// the file header for the full model + the §3 perf rationale.
    /// </summary>
    internal static class LiveFieldWrite
    {
        // Shared survey radius lock (matches LocalMapController.SurveyRadiusMeters / SurveyorTableTag).
        private const float SurveyRadiusMeters = LocalMapController.SurveyRadiusMeters;

        // Sub-throttle (impl-spec §2.1): the write runs at most this often, matching vanilla's
        // m_exploreInterval (decomp Minimap :46702 = 2f) — new ground appears at most that fast, so
        // a finer cadence is wasted work. Driven from the controller's 0.25 s poll, this clock gates
        // the actual write to ~2 s. Private to the WRITE axis (independent of the render poll).
        private const float WriteIntervalSeconds = 2f;
        private static float _nextWrite;

        /// <summary>
        /// Stamp the current Kit-revealed global fog into the in-region carried imprinted maps.
        /// Called once per <see cref="LocalMapController"/> poll (after the provider state machine,
        /// §4 cold-start ordering); self-throttles to <see cref="WriteIntervalSeconds"/>. No-ops on
        /// every gate it fails (no player, no Minimap, no Kit / nomap-off no equipped map, no
        /// in-region imprinted map, nothing newly lit) — the steady state is zero writes (§3).
        /// </summary>
        public static void Tick(Player player)
        {
            if (player == null) return;

            // ── Sub-throttle to ~2 s (independent of the 0.25 s render poll). ──
            float now = Time.realtimeSinceStartup;
            if (now < _nextWrite) return;
            _nextWrite = now + WriteIntervalSeconds;

            var mm = Minimap.instance;
            if (mm == null) return;   // headless / dedicated — no personal fog to read

            // ── §6 nomap branch: decide the WRITE-SET RULE. ──────────────────────────────
            //   nomap-ON  (Game.m_noMap == true, SBPR default via NoMapEnforcer): the full §2 rule —
            //     write-set = all carried imprinted in-region maps, gated on wearing the Kit.
            //   nomap-OFF (Game.m_noMap == false, a host who lifted the NoMap key): the Kit is
            //     non-functional (design §6 item 1), so there is no Kit-gated plural set; the ONLY
            //     writeable map is the EQUIPPED one (design §6 NOTE "equipped → writes to both"),
            //     NOT Kit-gated. Either way still in-region- + IsImprinted-tested below.
            bool noMap = Game.m_noMap;
            if (noMap)
            {
                // nomap-ON: require the Kit (reuse the exact public detector the gate Prefix uses).
                if (!CartographersKit.IsWearingKit(player)) return;   // AT-LIVE-NOKIT
            }

            // ── Read the global personal fog once (shared cached-FieldInfo reader, §2.1 step 2). ──
            bool[]? explored = MinimapFog.ReadExplored(mm);
            if (explored == null) return;   // couldn't read the field — nothing to stamp this tick

            int textureSize = mm.m_textureSize;   // public (decomp :46692)
            float pixelSize = mm.m_pixelSize;     // public (:46694)
            Vector3 ppos = player.transform.position;

            // ── Build + stamp the write-set. ─────────────────────────────────────────────
            var inv = player.GetInventory();
            if (inv == null) return;

            if (noMap)
            {
                // nomap-ON plural write-set: every carried imprinted in-region map.
                foreach (var it in inv.GetAllItems())
                    StampIfInRegion(it, explored, textureSize, pixelSize, ppos);
            }
            else
            {
                // nomap-OFF degenerate write-set: just the equipped imprinted map (if any).
                var equipped = GetEquippedLocalMap(player);
                StampIfInRegion(equipped, explored, textureSize, pixelSize, ppos);
            }
        }

        /// <summary>
        /// Stamp ONE candidate map if it is one of ours, imprinted, and in-region. Windows the
        /// global fog into the map's own disc (CaptureWindow), OR-merges into its stored survey with
        /// the §2.3 dirty-check, and ONLY rewrites the blob when a cell actually flipped (§3). Pins
        /// are NOT part of the hot-path write (§2.2 — they ride the live render union + Table ingest),
        /// so we pass an empty candidate set. Idempotent: re-stamping lit cells is a no-op.
        /// </summary>
        private static void StampIfInRegion(ItemDrop.ItemData? item, bool[] explored,
                                            int textureSize, float pixelSize, Vector3 ppos)
        {
            if (!IsLocalMap(item)) return;
            if (!LocalMap.IsImprinted(item!)) return;          // unimprinted excluded for free (AT-LIVE-UNIMPRINTED)
            if (!LocalMap.TryGetBoundOrigin(item!, out var origin)) return;

            // In-region pre-filter (§2.1 step 3): skip the array build for a map whose disc the
            // player has LEFT (AT-LIVE-OUTREGION). Even without it the disc clip below would no-op
            // the map; this just avoids the windowed-fog work for far maps.
            if (!BoundedMapMath.InRegionForLiveWrite(ppos.x, ppos.z, origin.x, origin.z, SurveyRadiusMeters, pixelSize))
                return;

            // Window the GLOBAL fog into THIS map's disc. BuildWindowedFog reads
            // explored[srcY*textureSize + srcX] — the exact global array index vanilla's own fog
            // uses — at the map's grid-snapped window, so a stamped local cell IS the global cell at
            // the same world coordinate (AT-LIVE-ALIGN, §4.1 1:1 invariant) for free.
            var cap = SurveyData.CaptureWindow(
                explored, textureSize, pixelSize,
                origin.x, origin.z, SurveyRadiusMeters,
                candidatePins: Array.Empty<SurveyPin>(),   // §2.2 — pins ride the live union, not this write
                out _, out _);

            var cur = LocalMap.ReadSurvey(item!) ?? new SurveyData();   // sub-KB deserialize
            if (cur.MergeFrom(cap, out bool changed) && changed)        // §2.3 OR-merge + flip report
                LocalMap.WriteSurveyBlob(item!, cur);                   // §2.4 reserialize → m_customData (only when dirty)
        }

        /// <summary>
        /// The equipped Local Map (right hand), or null — the nomap-OFF degenerate write target
        /// (§6). Mirrors <see cref="LocalMapController"/>'s own equipped-map probe (each cartography
        /// class keeps its own tiny copy rather than introducing a shared util for one predicate).
        /// </summary>
        private static ItemDrop.ItemData? GetEquippedLocalMap(Player player)
        {
            var inv = player.GetInventory();
            if (inv == null) return null;
            foreach (var it in inv.GetEquippedItems())
                if (IsLocalMap(it)) return it;
            return null;
        }

        /// <summary>
        /// True if the item is one of OUR Local Maps — a component tag on its drop prefab, or the
        /// locked prefab name (rename-proof). Mirrors LocalMapController.IsLocalMap /
        /// SurveyorTableTag.IsLocalMap (the same 3-line idiom each cartography class keeps).
        /// </summary>
        private static bool IsLocalMap(ItemDrop.ItemData? item)
        {
            if (item?.m_dropPrefab == null) return false;
            return item.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                   || item.m_dropPrefab.name == LocalMap.LocalMapName;
        }
    }
}
