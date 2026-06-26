// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone WISP behaviour (engine-side MonoBehaviour)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §wisp-motion. Drives ONE wisp's transform
//           each frame off the engine-free WispMotion helix, with the ground-aware-Y
//           read (the one engine-coupled step the pure math can't do headless).
//
//  This is the thin engine half of the wisp motion: WispMotion (engine-free, unit-
//  tested) says WHERE on the helix the wisp is; this samples ground height at the
//  orbit point and writes transform.position. It also owns the wisp's lifetime —
//  the WispField manager creates/destroys these as eligible objects come and go.
//
//  PER-WEARER VISIBILITY. Wisps are personal: they exist in the world FOR the local
//  player because they wear the lens. WispField only ever instantiates wisps on the
//  local client while the stone is worn, and destroys them all when it's removed —
//  so there is no networked wisp, no ZNetView, nothing another player can see. That
//  is also what makes a face-biased anti-occlusion tweak free (no shared instance).
//
//  Clean-side (ADR-0001): SBPR-authored MonoBehaviour; reads vanilla types (Heightmap,
//  Physics) only to resolve ground height — no clone, no patch.
// ============================================================================

using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// One personal wisp orbiting one eligible object on the helix. Created by
    /// <see cref="WispField"/>; carries the source object's identity so pin-by-look (M4) can
    /// resolve what was looked at. Pure transform driver — no networking, no ZDO.
    /// </summary>
    public sealed class WispBehaviour : MonoBehaviour
    {
        private Vec3 _centroid;        // the cylinder axis (XZ from the source object, Y = base ground)
        private WispMotionParams _p;
        private float _t0;             // spawn time, so each wisp's helix has a stable origin

        /// <summary>The source object's prefab name (clone-stripped) — pin-by-look reads this.</summary>
        public string SourcePrefab { get; private set; } = "";
        /// <summary>The source object's friendly/hover name — becomes the pin label.</summary>
        public string SourceFriendlyName { get; private set; } = "";
        /// <summary>Whether the source is a Pickable (abundance pin) or a Location (DiscoverLocation).</summary>
        public WispHitKind SourceKind { get; private set; }
        /// <summary>The source world position (for the pin + the merge check).</summary>
        public Vector3 SourcePos { get; private set; }

        /// <summary>Configure the wisp at spawn. Centroid is the source's world position.</summary>
        public void Init(Vector3 centroid, WispMotionParams p, string sourcePrefab,
                         string sourceFriendlyName, WispHitKind kind)
        {
            _centroid = new Vec3(centroid.x, centroid.y, centroid.z);
            _p = p;
            _t0 = Time.time;
            SourcePrefab = sourcePrefab ?? "";
            SourceFriendlyName = sourceFriendlyName ?? "";
            SourceKind = kind;
            SourcePos = centroid;
            ApplyPosition(0f);
        }

        private void Update()
        {
            ApplyPosition(Time.time - _t0);
        }

        /// <summary>
        /// Place the wisp on its helix at elapsed time <paramref name="t"/>: horizontal offset from
        /// WispMotion (the cylinder wall), then ground height SAMPLED AT THE ORBIT POINT (the ground-
        /// aware-Y fix — on a slope the wisp tracks the terrain under its current orbit position so it
        /// never sinks into the uphill side), then the vertical sine on top.
        /// </summary>
        private void ApplyPosition(float t)
        {
            var h = WispMotion.HorizontalOffset(_p, t);
            float orbitX = _centroid.X + h.X;
            float orbitZ = _centroid.Z + h.Z;
            float groundY = SampleGround(orbitX, orbitZ, _centroid.Y);
            float height = WispMotion.VerticalHeight(_p, t);
            transform.position = new Vector3(orbitX, groundY + height, orbitZ);
        }

        /// <summary>
        /// Ground height at (x, z). Prefer the live Heightmap (cheap, no physics raycast); fall back
        /// to the centroid's Y if no heightmap is resolvable (open water / unloaded). This is the one
        /// step WispMotion can't do headless, isolated here so the geometry stays unit-tested.
        /// </summary>
        private static float SampleGround(float x, float z, float fallbackY)
        {
            if (ZoneSystem.instance != null)
            {
                // ZoneSystem.GetGroundHeight is the vanilla terrain-height query (decomp: used by
                // worldgen + placement). Returns the solid ground Y at the world XZ.
                float gh = ZoneSystem.instance.GetGroundHeight(new Vector3(x, fallbackY, z));
                if (!float.IsNaN(gh) && !float.IsInfinity(gh)) return gh;
            }
            return fallbackY;
        }
    }
}
