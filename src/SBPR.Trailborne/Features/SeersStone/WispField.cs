// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone WISP FIELD (engine-side manager)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §wisp-lifecycle. The per-frame manager that,
//           WHILE the local player wears the Seer's Stone, keeps a personal set of
//           wisps over nearby ELIGIBLE objects (whitelist substrate, M1), and tears
//           them all down the instant the stone comes off.
//
//  LIFECYCLE (Daniel 2026-06-25):
//    • Wisps are PERSONAL + client-only — instantiated locally, never networked, no
//      ZDO. Another player sees nothing (they'd need their own lens).
//    • A wisp marks an object's EXISTENCE, not its current fruit: it persists while
//      the source Pickable/Location object still exists (picked-bare is fine — the
//      bush still stands), and only leaves when the object is destroyed/cleared or
//      goes out of range, or the stone is removed.
//    • Eligibility is the whitelist (ignore-unlisted): only listed prefabs get a wisp.
//
//  PERFORMANCE. The expensive part is the OverlapSphere scan; it runs THROTTLED
//  (ScanIntervalSeconds), not every frame — the Cairns.cs OverlapSphere-throttle
//  precedent. Between scans the existing wisps just orbit (cheap transform writes in
//  WispBehaviour). Wisps reconcile against a dictionary keyed by the source instance,
//  so a scan that re-sees the same patch reuses its wisp instead of respawning it.
//
//  Bootstrap: a single SeersStoneFieldHost MonoBehaviour is attached to the local
//  Player by a Player.OnSpawned-ish hook (Plugin wiring), and it owns one WispField.
//  Wisps are children of the host so they're destroyed with it on logout/death.
//
//  Clean-side (ADR-0001): SBPR-authored; reads vanilla Pickable/Location/Physics —
//  no clone, no patch (the only patch in the feature is the Alt+E input, separate file).
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Maintains the local player's personal wisp set. Driven each frame by
    /// <see cref="SeersStoneFieldHost"/>; does the throttled eligible-object scan and the
    /// spawn/despawn reconcile. Engine-side (Unity types, OverlapSphere) — the eligibility
    /// decision is delegated to the M1 whitelist, the motion to the M3a math.
    /// </summary>
    public sealed class WispField
    {
        /// <summary>How often to re-scan for eligible objects (seconds). Between scans wisps just orbit.</summary>
        public const float ScanIntervalSeconds = 1.0f;
        /// <summary>Scan radius around the player (metres). Wisps beyond this despawn; within, spawn.</summary>
        public const float ScanRadius = 60f;
        /// <summary>The wisp visual donor (the floating glow ball) — its glow subtree grafted ZNetView-free.</summary>
        private const string WispVisualDonor = "demister_ball";
        // The donor's "effects" child carries the Point light + particle glow (the wisp look) WITHOUT
        // the solid 1 m sphere mesh (the sibling child, also named "demister_ball"). Graft the effects
        // subtree so the wisp reads as a drifting light, not an opaque ball. If "effects" can't be
        // grafted we fall back to the mesh child so the wisp is at least visible (logs-green ≠ playable
        // — Daniel verifies the final look on Prime).
        private const string WispVisualChild = "effects";
        private const string WispVisualChildFallback = "demister_ball";

        // Active wisps keyed by their source object's instance id, so a re-scan reuses an existing
        // wisp for a still-present patch instead of churning it.
        private readonly Dictionary<int, WispBehaviour> _wisps = new Dictionary<int, WispBehaviour>();
        private readonly Transform _parent;          // the host transform; wisps are children
        private float _nextScanAt;
        private int _phaseSeed;                       // spreads phase offsets across wisps

        // Layer mask for the scan: pieces + items (where Pickables/Locations live). Resolved once.
        private static int _scanMask = -1;

        public WispField(Transform parent)
        {
            _parent = parent;
        }

        /// <summary>Live wisp count (diagnostics / tests-via-host).</summary>
        public int ActiveCount => _wisps.Count;

        /// <summary>
        /// Per-frame tick from the host. <paramref name="worn"/> = is the stone currently worn.
        /// When false, all wisps are torn down immediately (the "take it off, the world goes quiet"
        /// rule). When true, the throttled scan reconciles the wisp set against nearby eligible objects.
        /// </summary>
        public void Tick(Vector3 playerPos, bool worn)
        {
            if (!worn)
            {
                if (_wisps.Count > 0) ClearAll();
                return;
            }

            if (Time.time < _nextScanAt) return;
            _nextScanAt = Time.time + ScanIntervalSeconds;
            Rescan(playerPos);
        }

        /// <summary>Destroy every wisp (stone removed, host teardown).</summary>
        public void ClearAll()
        {
            foreach (var w in _wisps.Values)
                if (w != null) Object.Destroy(w.gameObject);
            _wisps.Clear();
        }

        /// <summary>
        /// The throttled reconcile: find eligible objects in range, spawn wisps for new ones, drop
        /// wisps whose source is gone or out of range. Keyed by source instance id for O(1) reuse.
        /// </summary>
        private void Rescan(Vector3 playerPos)
        {
            if (_scanMask < 0)
                _scanMask = LayerMask.GetMask("piece", "piece_nonsolid", "item", "Default", "Default_small", "static_solid");

            var seen = new HashSet<int>();

            // ── Pickables (and their vegetation) via OverlapSphere ───────────────────────────
            var hits = Physics.OverlapSphere(playerPos, ScanRadius, _scanMask, QueryTriggerInteraction.Collide);
            foreach (var col in hits)
            {
                if (col == null) continue;
                var pickable = col.GetComponentInParent<Pickable>();
                if (pickable == null) continue;

                var prefab = StripClone(pickable.gameObject.name);
                if (!SeersStoneWhitelist.IsEligible(prefab)) continue;

                int id = pickable.gameObject.GetInstanceID();
                seen.Add(id);
                if (!_wisps.ContainsKey(id))
                    SpawnWisp(id, pickable.transform.position, prefab,
                              ResolveFriendlyName(pickable), WispHitKind.Pickable, boundsRadius: 2.0f);
            }

            // ── Locations (registry-scan; FindObjectsByType is the non-deprecated form) ────
            foreach (var loc in Object.FindObjectsByType<Location>(FindObjectsSortMode.None))
            {
                if (loc == null) continue;
                float d = (loc.transform.position - playerPos).magnitude;
                if (d > ScanRadius) continue;

                var prefab = StripClone(loc.gameObject.name);
                if (!SeersStoneWhitelist.IsEligible(prefab)) continue;

                int id = loc.gameObject.GetInstanceID();
                seen.Add(id);
                if (!_wisps.ContainsKey(id))
                {
                    float r = loc.m_exteriorRadius > 0f ? loc.m_exteriorRadius : 8f;
                    SpawnWisp(id, loc.transform.position, prefab, prefab, WispHitKind.Location, boundsRadius: r);
                }
            }

            // ── Despawn wisps whose source vanished or left range ────────────────────────────
            if (_wisps.Count > 0)
            {
                var toRemove = new List<int>();
                foreach (var kv in _wisps)
                {
                    if (kv.Value == null) { toRemove.Add(kv.Key); continue; }
                    if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
                }
                foreach (var id in toRemove)
                {
                    if (_wisps.TryGetValue(id, out var w) && w != null) Object.Destroy(w.gameObject);
                    _wisps.Remove(id);
                }
            }
        }

        private void SpawnWisp(int id, Vector3 centroid, string prefab, string friendlyName,
                               WispHitKind kind, float boundsRadius)
        {
            var go = new GameObject($"SBPR_Wisp_{prefab}");
            go.transform.SetParent(_parent, worldPositionStays: true);

            // Visual: graft the demister_ball glow subtree (ZNetView-free cosmetic child). Try the
            // "effects" subtree (light + particles = the wisp glow); fall back to the mesh child if
            // that fails, so the wisp is at least visible. If both fail, the wisp still exists +
            // orbits + is pinnable — just invisible (logs-green ≠ playable; Daniel verifies on Prime).
            if (!Runtime.Assets.TryGraftVisualSubtree(WispVisualDonor, WispVisualChild, go, "SBPR_WispVisual", out _))
                Runtime.Assets.TryGraftVisualSubtree(WispVisualDonor, WispVisualChildFallback, go, "SBPR_WispVisual", out _);

            var beh = go.AddComponent<WispBehaviour>();
            var p = WispMotionParams.Default(boundsRadius, phaseOffset: (_phaseSeed++ * 1.1f) % 6.2831855f);
            beh.Init(centroid, p, prefab, friendlyName, kind);
            _wisps[id] = beh;
        }

        /// <summary>Resolve a Pickable's friendly name (its hover name, sans count) for the pin label.</summary>
        private static string ResolveFriendlyName(Pickable p)
        {
            try
            {
                var n = p.GetHoverName();
                if (!string.IsNullOrEmpty(n)) return SeersStonePinDecision.CleanLabel(n);
            }
            catch { /* hover name can touch localization; fall through to prefab */ }
            return StripClone(p.gameObject.name);
        }

        private static string StripClone(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }
    }
}
