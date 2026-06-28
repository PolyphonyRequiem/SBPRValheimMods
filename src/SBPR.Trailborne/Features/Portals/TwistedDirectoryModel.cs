// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal server-authoritative DIRECTORY model
//  (engine-free selection/encoding logic behind the L2 long-range candidate set)
// ----------------------------------------------------------------------------
//  Card     : t_ccb454f8 (L2) — server-authoritative long-range candidate set.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §2 (REQUIRED) + §4.4a.
//
//  WHAT THIS IS. The PURE, UnityEngine-free half of the L2 directory round-trip:
//  the within-range SLICE policy (which of the server's full Twisted-Portal set
//  is sent back to a stepping client, §2 "RPC-pushes the within-range slice") and
//  the flat row shape the wire carries. The engine glue (the routed RPC, the ZDO
//  walk, the ZPackage read/write) lives in TwistedPortalDirectory.cs; this file is
//  what tests/TwistedDirectoryModelTests.cs gates headless under net8.0 — the
//  AimPickMath / PortalEnergyMath / TwistedPortalOverlayModel link-compile pattern
//  (the asserted behaviour IS the shipped behaviour, one copy, no fork).
//
//  WHY A SLICE AT ALL. The server holds EVERY Twisted Portal ZDO in the world
//  (ZDOMan.Load adds them all to m_objectsBySector, decomp :64701-64713) — far more
//  than a picker needs. The client only ever aims within its overlay reach, so the
//  server filters to a generous radius around the requesting player before sending
//  (bounds the packet + the client's per-frame aim-pick set). The radius is the
//  REQUESTED reach (the client's OverlayRadius, §2), clamped server-side to a hard
//  ceiling so a malformed/hostile request can't ask the server to serialize the
//  whole world.
//
//  Clean-side (ADR-0001): all SBPR-authored logic; references no vanilla or
//  third-party type. Plain structs + math only.
// ============================================================================

using System.Collections.Generic;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// One directory row as it crosses the wire and lands in the client cache: a Twisted Portal's
    /// world placement (x/y/z position + the quaternion the arrival step-out uses), its censored rune
    /// (the aim label — empty string = unnamed), and its stable ZDO identity (userId/id halves) so the
    /// origin portal can be excluded from the aim-pick and the L3 highlight can match the aimed label.
    /// Engine-free: the position/rotation are plain floats, not UnityEngine types, so the encode/decode
    /// + radius policy are CI-gated headless. <see cref="TwistedPortalDirectory"/> converts these to/from
    /// <see cref="TwistedDestination"/> at the engine boundary.
    /// </summary>
    public readonly struct DirectoryRow
    {
        public readonly float Px, Py, Pz;          // world position
        public readonly float Rx, Ry, Rz, Rw;      // world rotation (quaternion)
        public readonly string Rune;               // censored rune name; "" = unnamed
        public readonly long IdUser;               // ZDOID.UserID half
        public readonly uint IdId;                 // ZDOID.ID half

        public DirectoryRow(
            float px, float py, float pz,
            float rx, float ry, float rz, float rw,
            string rune, long idUser, uint idId)
        {
            Px = px; Py = py; Pz = pz;
            Rx = rx; Ry = ry; Rz = rz; Rw = rw;
            Rune = rune ?? string.Empty;
            IdUser = idUser;
            IdId = idId;
        }
    }

    /// <summary>
    /// Pure directory policy + framing for the L2 server-authoritative candidate set (spec §2).
    /// Engine-free so tests/TwistedDirectoryModelTests.cs can pin the radius-slice contract + the
    /// wire round-trip headless in CI. The engine side (TwistedPortalDirectory) owns the routed RPC,
    /// the ZDOMan walk, and the ZPackage marshalling; this owns the math the wire format depends on.
    /// </summary>
    public static class TwistedDirectoryModel
    {
        /// <summary>
        /// Wire-format version. Bumped if the row layout changes so a mixed-DLL session (an old client
        /// against a new server) can detect the mismatch and degrade rather than misread bytes. The
        /// engine encoder writes it as the first int; the decoder rejects an unrecognized value.
        /// </summary>
        public const int WireVersion = 1;

        /// <summary>
        /// Hard server-side ceiling (metres) on the requested slice radius. The design's overlay reach
        /// is 300 m (TwistedPortalOverlayModel.DefaultOverlayRadius); we allow generous headroom for a
        /// raised OverlayRadius knob but cap it so a malformed request can never make the server
        /// serialize the whole world. A request above this is clamped DOWN to it (never rejected — a
        /// big ask just gets the ceiling). 2000 m is ~31 zones each way: comfortably past any sane
        /// aim reach, still a bounded packet.
        /// </summary>
        public const float MaxSliceRadiusMeters = 2000f;

        /// <summary>
        /// Floor (metres) on the slice radius. Guards against a zero/negative requested radius
        /// silently producing an empty candidate set (which would look exactly like "no portals" —
        /// the §2 bug we're fixing). A degenerate request still gets a usable local slice.
        /// </summary>
        public const float MinSliceRadiusMeters = 16f;

        /// <summary>
        /// Clamp a client-requested slice radius into the server's accepted band
        /// [<see cref="MinSliceRadiusMeters"/>, <see cref="MaxSliceRadiusMeters"/>]. NaN / non-finite
        /// inputs fall back to the floor (fail-small, never serialize the world on a garbage request).
        /// </summary>
        public static float ClampSliceRadius(float requested)
        {
            // NaN-safe: the comparisons below are all false for NaN, so route it to the floor explicitly.
            if (float.IsNaN(requested) || float.IsInfinity(requested)) return MinSliceRadiusMeters;
            if (requested < MinSliceRadiusMeters) return MinSliceRadiusMeters;
            if (requested > MaxSliceRadiusMeters) return MaxSliceRadiusMeters;
            return requested;
        }

        /// <summary>
        /// True when a portal at (<paramref name="px"/>,<paramref name="pz"/>) is within
        /// <paramref name="radius"/> metres of the request origin (<paramref name="ox"/>,<paramref name="oz"/>),
        /// using PLANAR (x/z) distance — the same horizontal-reach notion the overlay + aim use (a portal
        /// up a cliff is "near" if it's near on the map). Squared-distance compare (no sqrt). The server
        /// applies this to its full set to build the within-range slice (§2).
        /// </summary>
        public static bool WithinSlice(float px, float pz, float ox, float oz, float radius)
        {
            float dx = px - ox;
            float dz = pz - oz;
            return (dx * dx + dz * dz) <= (radius * radius);
        }

        /// <summary>
        /// Server-side slice builder (pure): copy into <paramref name="into"/> every row of
        /// <paramref name="all"/> within the clamped <paramref name="requestedRadius"/> of the request
        /// origin. <paramref name="into"/> is cleared first (caller-owned reusable). Returns the clamped
        /// radius actually applied (so the engine side can log "served N within R m"). This is the
        /// "RPC-pushes the within-range slice" policy of §2, isolated for the CI fence.
        /// </summary>
        public static float BuildSlice(
            IReadOnlyList<DirectoryRow> all,
            float ox, float oz,
            float requestedRadius,
            List<DirectoryRow> into)
        {
            into.Clear();
            float r = ClampSliceRadius(requestedRadius);
            if (all == null || all.Count == 0) return r;
            for (int i = 0; i < all.Count; i++)
            {
                DirectoryRow row = all[i];
                if (WithinSlice(row.Px, row.Pz, ox, oz, r))
                    into.Add(row);
            }
            return r;
        }
    }
}
