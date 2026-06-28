// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal server-authoritative DIRECTORY (L2)
//  The routed-RPC long-range candidate set: client asks the SERVER for the
//  within-range slice of the full Twisted-Portal set, server walks its complete
//  ZDO set and replies, client caches it for the aim-pick. Spec §2 (REQUIRED).
// ----------------------------------------------------------------------------
//  Card     : t_ccb454f8 (L2). Depends on L1's frozen TwistedPortalCandidates.Gather seam.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §2 (now REQUIRED) + §4.4a.
//
//  THE PROBLEM L2 FIXES (grounded against the decomp this pass).
//  On a dedicated server a CLIENT holds only the ZDOs in its active sector window
//  (ZDOMan.CreateSyncList → FindSectorObjects, ~64-128 m: ZoneSystem.m_zoneSize=64,
//  m_activeArea=1). So the L1 staging walk (TwistedPortalCandidates.Gather running
//  GetAllZDOsWithPrefabIterative on the LOCAL peer) can only ever see — and let you
//  aim at / travel to — portals within that window. A destination past it is invisible
//  to the picker. The SERVER, by contrast, loads EVERY ZDO in the world into
//  m_objectsBySector / m_objectsByOutsideSector (ZDOMan.Load :64701-64713), so the
//  SAME walk run ON THE SERVER returns the full set.
//
//  THE FIX. A client→server→client routed-RPC round-trip:
//    1. The stepping client periodically InvokeRoutedRPC's a REQUEST to the SERVER
//       (its position + the reach it wants).
//    2. The SERVER runs GetAllZDOsWithPrefabIterative over ITS full set, slices to
//       the within-range rows (TwistedDirectoryModel.BuildSlice), encodes them into a
//       ZPackage, and InvokeRoutedRPC's the RESPONSE back to the requesting sender.
//    3. The client decodes the rows into its DIRECTORY CACHE. TwistedPortalCandidates.Gather
//       reads that cache (unioned with the local-window walk for instant on-placement feel),
//       so the aim-pick + commit reach destinations past the client window (AT-PICK-LONGRANGE).
//
//  WHY THE SERVER, NOT THE ZDO OWNER (the one place this diverges from the spec's
//  literal "SurveyorTableTag owner-routed-RPC" wording — grounded, deliberate).
//  ZNetView.InvokeRPC(string) routes to the ZDO's OWNER (decomp :70027). A Twisted
//  Portal near the player is very often owned by the PLAYER's own peer on a dedicated
//  server — so an owner-route would loop straight back to the limited client window,
//  resolving nothing. The world-global query must hit the one peer that holds the whole
//  world: the SERVER. The exact in-game vanilla precedent for "client asks the server to
//  search its full world set and reply to me" is ZoneSystem.RPC_DiscoverClosestLocation /
//  RPC_DiscoverLocationResponse (decomp :84128/:84799): a GLOBAL routed RPC auto-targeted at
//  the server (ZRoutedRpc.InvokeRoutedRPC(string,...) :70673), the server replying to `sender`
//  (:84783/:84791), the response handler registered on BOTH sides and the request handler gated
//  on IsServer() (:84126). We mirror that shape exactly. (Same ZRoutedRpc transport the spec's
//  SurveyorTableTag InvokeRPC ultimately rides — :70022 → ZRoutedRpc.InvokeRoutedRPC — just
//  targeted at the server peer instead of a ZDO owner, which is the correct target here.)
//
//  HOST / SINGLEPLAYER. On a listen-server / singleplayer host the requester IS the
//  server, so InvokeRoutedRPC self-dispatches the request inline (decomp :70701 — when
//  targetPeerID == m_id it HandleRoutedRPC's locally without a socket hop) and the response
//  likewise self-dispatches. No special-casing needed: the same round-trip degenerates to a
//  synchronous in-process call, and the cache is populated identically.
//
//  Clean-side (ADR-0001): base-game ZRoutedRpc / ZNet / ZDOMan / ZDO / ZPackage / ZDOID +
//  our own TwistedDirectoryModel only. The routed-RPC directory is OUR code (the cartography
//  RPC precedent is also ours). No third-party code read or copied.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The L2 server-authoritative Twisted-Portal directory (spec §2). Owns the routed-RPC round-trip
    /// (request→server walk→response) and the client-side cache that <see cref="TwistedPortalCandidates.Gather"/>
    /// reads so the look-to-aim picker reaches destinations past the client's ~64-128 m ZDO window.
    ///
    /// Registration is done ONCE per session by <see cref="TwistedPortalDirectoryBootstrap"/> (a Game.Start
    /// postfix) on BOTH sides — the response handler everywhere, the request handler only where IsServer()
    /// (the ZoneSystem.RPC_Discover* precedent). This class is otherwise stateless engine glue + a static cache.
    /// </summary>
    public static class TwistedPortalDirectory
    {
        // ── Routed-RPC method names (hashed by ZRoutedRpc.Register; LOCK — a rename desyncs a mixed
        //    DLL session). Distinct, SBPR-prefixed so they can never collide with a vanilla or peer-mod
        //    routed RPC on a shared world. The request carries (originX, originZ, requestedRadius); the
        //    response carries a single encoded ZPackage blob of rows (TwistedDirectoryModel framing). ──
        public const string RpcRequest  = "SBPR_TwistedDirReq";
        public const string RpcResponse = "SBPR_TwistedDirResp";

        // ── Client-side directory cache (the long-range candidate set). Populated by the response
        //    handler, read by Gather. Guarded by a tiny lock so the (single-threaded Unity) read in
        //    Gather and the RPC-thread write never tear a half-updated list — cheap, uncontended. ──
        private static readonly object _cacheLock = new object();
        private static readonly List<TwistedDestination> _cache = new List<TwistedDestination>();
        private static bool _haveCache;
        private static float _lastResponseTime = -999f;

        // Reusable server-side scratch (the server answers requests on the main thread serially; one set
        // of buffers is fine and allocation-free across requests). Never touched on a pure client.
        private static readonly List<ZDO> _srvZdoScratch = new List<ZDO>();
        private static readonly List<DirectoryRow> _srvAllRows = new List<DirectoryRow>();
        private static readonly List<DirectoryRow> _srvSlice = new List<DirectoryRow>();

        private static bool _loggedFirstServe;
        private static bool _loggedFirstCache;

        /// <summary>
        /// Register the directory's routed RPCs. Called once from <see cref="TwistedPortalDirectoryBootstrap"/>.
        /// Idempotent within a session via <paramref name="alreadyRegistered"/> (Game.Start can re-fire on a
        /// world reload; ZRoutedRpc.Register would otherwise throw on a duplicate method-hash add). The RESPONSE
        /// handler registers on every peer (the client needs it); the REQUEST handler only where IsServer() —
        /// only the server can answer it (the ZoneSystem.RPC_Discover* gate, decomp :84126).
        /// </summary>
        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/TwistedPortal] Directory Register: ZRoutedRpc.instance is null (too early?); " +
                    "skipping — the look-to-aim picker will fall back to the local-window candidate set until it registers.");
                return;
            }

            // The response handler lives on BOTH sides (a host is also a client of its own server).
            rpc.Register<ZPackage>(RpcResponse, RPC_DirectoryResponse);

            // The request handler is the server's job only. A pure client never answers it.
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            if (isServer)
                rpc.Register<Vector3, float>(RpcRequest, RPC_DirectoryRequest);

            Plugin.Log.LogInfo(
                $"[Trailborne/TwistedPortal] Server-authoritative directory RPCs registered (response on all peers; " +
                $"request {(isServer ? "REGISTERED (this peer is the server)" : "skipped (client peer)")}). Spec §2.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // CLIENT SIDE — request the slice, read the cache.
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fire a directory REQUEST to the server for the within-<paramref name="radius"/>-metres slice
        /// around <paramref name="playerPos"/>. The 2-arg ZRoutedRpc.InvokeRoutedRPC auto-targets the
        /// server peer (decomp :70673) and self-dispatches inline on a host (:70701). No-op (logs once)
        /// when the routed RPC isn't up yet. Called on a throttle by <see cref="TwistedPortalCandidates"/>.
        /// </summary>
        public static void RequestSlice(Vector3 playerPos, float radius)
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            // The server clamps the radius into its accepted band; we send the raw ask.
            rpc.InvokeRoutedRPC(RpcRequest, playerPos, radius);
        }

        /// <summary>
        /// Copy the cached long-range directory into <paramref name="into"/> (cleared first). Returns
        /// true when a server response has ever landed (so the caller knows the cache is authoritative
        /// rather than empty-because-never-answered). Thread-safe against the RPC write.
        /// </summary>
        public static bool CopyCache(List<TwistedDestination> into)
        {
            into.Clear();
            lock (_cacheLock)
            {
                if (!_haveCache) return false;
                for (int i = 0; i < _cache.Count; i++) into.Add(_cache[i]);
                return true;
            }
        }

        /// <summary>True when a server response has landed within the last <paramref name="staleSeconds"/>
        /// seconds — the picker prefers the fresh authoritative set, else falls back to the local window.</summary>
        public static bool HasFreshCache(float staleSeconds)
        {
            lock (_cacheLock)
            {
                return _haveCache && (Time.time - _lastResponseTime) <= staleSeconds;
            }
        }

        /// <summary>The client RESPONSE handler: decode the server's slice blob into the directory cache.
        /// Registered on every peer. <paramref name="sender"/> is the server's peer id (unused — we trust
        /// the routed transport's targeting; a future hardening could assert sender == server peer).</summary>
        private static void RPC_DirectoryResponse(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var decoded = new List<TwistedDestination>();
                if (!DecodeRows(pkg, decoded))
                {
                    Plugin.Log.LogWarning(
                        "[Trailborne/TwistedPortal] Directory response had an unrecognized wire version; ignoring " +
                        "(mixed-DLL session? the picker falls back to the local-window set).");
                    return;
                }

                lock (_cacheLock)
                {
                    _cache.Clear();
                    _cache.AddRange(decoded);
                    _haveCache = true;
                    _lastResponseTime = Time.time;
                }

                if (!_loggedFirstCache)
                {
                    _loggedFirstCache = true;
                    Plugin.Log.LogInfo(
                        $"[Trailborne/TwistedPortal] Directory cache populated from the server: {decoded.Count} portal(s) " +
                        "in range (server-authoritative — reaches past the ~64-128 m client window, AT-PICK-LONGRANGE).");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] Directory response decode failed (ignored): {e.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // SERVER SIDE — walk the full set, slice, reply to the requester.
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The server REQUEST handler: walk the FULL Twisted-Portal ZDO set (the server holds every ZDO
        /// in the world), slice to the within-range rows around the requester's <paramref name="origin"/>,
        /// encode them, and reply to <paramref name="sender"/>. Registered only where IsServer(). The walk
        /// is GetAllZDOsWithPrefabIterative on the SERVER's ZDOMan — the same API L1 used client-side, but
        /// here it returns the complete set (decomp :65497 over m_objectsBySector populated by Load).
        /// </summary>
        private static void RPC_DirectoryRequest(long sender, Vector3 origin, float requestedRadius)
        {
            try
            {
                var zdoMan = ZDOMan.instance;
                if (zdoMan == null) return;

                // 1) Walk the server's FULL set (drain the paged iterator — false until exhausted).
                _srvZdoScratch.Clear();
                int index = 0;
                while (!zdoMan.GetAllZDOsWithPrefabIterative(TwistedPortal.PortalPieceName, _srvZdoScratch, ref index)) { }

                // 2) Flatten each ZDO into a wire row (censor the rune on read — the core's ReadRuneName
                //    precedent — so a label can never carry un-filtered UGC even from a legacy ZDO).
                _srvAllRows.Clear();
                foreach (var z in _srvZdoScratch)
                {
                    if (z == null) continue;
                    string raw = z.GetString(SBPR_TwistedPortal.ZdoRuneName, string.Empty);
                    string rune = string.IsNullOrEmpty(raw)
                        ? string.Empty
                        : CensorShittyWords.FilterUGC(raw, UGCType.Text, 0L);
                    Vector3 p = z.GetPosition();
                    Quaternion q = z.GetRotation();
                    _srvAllRows.Add(new DirectoryRow(
                        p.x, p.y, p.z, q.x, q.y, q.z, q.w, rune, z.m_uid.UserID, z.m_uid.ID));
                }

                // 3) Slice to the within-range rows (pure policy, clamps the radius server-side).
                float applied = TwistedDirectoryModel.BuildSlice(_srvAllRows, origin.x, origin.z, requestedRadius, _srvSlice);

                // 4) Encode + reply to the requester only (InvokeRoutedRPC(sender, ...) — the
                //    RPC_DiscoverClosestLocation reply shape, decomp :84791).
                var blob = EncodeRows(_srvSlice);
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcResponse, blob);

                if (!_loggedFirstServe)
                {
                    _loggedFirstServe = true;
                    Plugin.Log.LogInfo(
                        $"[Trailborne/TwistedPortal] Directory served (server-authoritative): {_srvSlice.Count}/{_srvAllRows.Count} " +
                        $"Twisted Portal(s) within {applied:F0} m of the requester — the full-world walk L2 added (spec §2).");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] Directory request handling failed (ignored): {e.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // WIRE FORMAT — version int + count + flat rows. The TwistedDirectoryModel.WireVersion guards
        // a mixed-DLL session. Plain ZPackage primitives (decomp :70297/:70327/:70337/:70342).
        // ════════════════════════════════════════════════════════════════════════════════════════

        private static ZPackage EncodeRows(IReadOnlyList<DirectoryRow> rows)
        {
            var pkg = new ZPackage();
            pkg.Write(TwistedDirectoryModel.WireVersion);
            pkg.Write(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                DirectoryRow r = rows[i];
                pkg.Write(r.Px); pkg.Write(r.Py); pkg.Write(r.Pz);
                pkg.Write(r.Rx); pkg.Write(r.Ry); pkg.Write(r.Rz); pkg.Write(r.Rw);
                pkg.Write(r.Rune ?? string.Empty);
                pkg.Write(r.IdUser);
                pkg.Write(r.IdId);
            }
            return pkg;
        }

        /// <summary>Decode an encoded slice blob into <paramref name="into"/> as engine <see cref="TwistedDestination"/>
        /// rows. Returns false (and leaves <paramref name="into"/> as-far-as-read) on a wire-version mismatch so a
        /// mixed-DLL session degrades to the local window instead of misreading bytes.</summary>
        private static bool DecodeRows(ZPackage pkg, List<TwistedDestination> into)
        {
            int version = pkg.ReadInt();
            if (version != TwistedDirectoryModel.WireVersion) return false;

            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                float px = pkg.ReadSingle(), py = pkg.ReadSingle(), pz = pkg.ReadSingle();
                float rx = pkg.ReadSingle(), ry = pkg.ReadSingle(), rz = pkg.ReadSingle(), rw = pkg.ReadSingle();
                string rune = pkg.ReadString();
                long idUser = pkg.ReadLong();
                uint idId = pkg.ReadUInt();

                bool hasRune = !string.IsNullOrEmpty(rune);
                into.Add(new TwistedDestination(
                    new Vector3(px, py, pz),
                    new Quaternion(rx, ry, rz, rw),
                    rune, hasRune,
                    new ZDOID(idUser, idId)));
            }
            return true;
        }
    }
}
