using System;
using System.Globalization;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R2 — the DEDICATED-server ingress seam. It closes the exact gap the T009R integration review
    /// rejected: a joined dedicated-server client's build never runs <c>Player.PlacePiece</c> on the
    /// server, so the server-gated listen-host observer emits zero receipts for it. Here the CLIENT that
    /// just placed a Foundational piece fires a routed NOTICE identifying the candidate physical instance
    /// (its ZDOID); the SERVER handles that notice, derives the sender principal from the AUTHENTICATED
    /// routed sender (never the payload), and hands the opaque instance key to the engine-free
    /// <see cref="DedicatedPlacementIngress"/>, which independently re-derives every credit-bearing fact
    /// from the server's own ZDO store before routing through the SHARED validation core.
    ///
    /// Authority (never client-authoritative):
    ///   * the notice is a POINTER only — an opaque physical-instance key. The client cannot supply the
    ///     piece id, the position, the area, the success state, the catalog version, or its own identity
    ///     as authority; every one of those is re-derived server-side by the ingress.
    ///   * the sender principal comes from <c>ZRoutedRpc</c>'s authenticated <paramref name="sender"/> peer
    ///     (the server's peer table), rendered into the same <c>player:&lt;id&gt;</c> space as the ZDO's
    ///     recorded creator, so the ingress's creator==sender binding compares two SERVER-derived values.
    ///   * the request handler is registered ONLY where <c>IsServer()</c>; a pure client never answers it.
    ///
    /// Startup / replication safety: ingress is notice-driven. A server booting or replicating existing
    /// resident ZDOs generates NO notice, so no previously-loaded piece is ever awarded — this is exactly
    /// the vanilla distinction between "a client just placed this" (a live notice) and "the server loaded
    /// an existing ZDO" (no notice). Duplicate/replayed notices for one physical instance converge on the
    /// single receipt (the ingress derives a deterministic ZDOID-based operation id); a conflicting reuse
    /// of a credited instance rejects at the receipt layer.
    ///
    /// This file references UnityEngine/Valheim (ZNet, ZRoutedRpc, ZNetView, ZDO, Player, Piece), so it
    /// does NOT link-compile into the net8 test suite; the engine-free ingress + revalidation it drives is
    /// fully unit-tested. Clean-side (ADR-0001): base-game ZRoutedRpc/ZNet/ZDO/ZNetScene only.
    /// </summary>
    [HarmonyPatch]
    internal static class DedicatedPlacementIngressObserver
    {
        // Routed-RPC method name (hashed by ZRoutedRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcNotice = "SBPR_Niflheim_FoundationalPlacedNotice";

        // The current-build prefab map (used only to skip firing notices for non-Foundational builds; the
        // SERVER re-resolves the stable id authoritatively — this is a client-side spam guard only).
        private static readonly FoundationalPrefabMap PrefabMap = FoundationalPrefabMap.CurrentBuild;

        // The ZRoutedRpc instance we last registered against (rebuilt each ZNet session).
        private static ZRoutedRpc? registeredRpc;

        // Blocker 2: resolves an authenticated sender peer to its server-owned character facts
        // (s_playerID + stable character ZDOID). Overridable in principle for tests; the net48 default
        // reads ZNet's peer table + ZDOMan.
        private static IAuthenticatedSenderCharacterSource SenderSource = ZdoAuthenticatedSenderSource.Instance;

        /// <summary>Register the notice RPC. The request handler lives only on the server (it credits);
        /// the send side is armed on every peer (a client fires it for its own placements). Idempotent
        /// per ZRoutedRpc instance (a re-register within one instance throws on a duplicate method hash).</summary>
        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            if (ReferenceEquals(registeredRpc, rpc)) return;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            if (isServer)
                rpc.Register<string>(RpcNotice, RPC_PlacedNotice);

            registeredRpc = rpc;
            Plugin.Log.LogInfo(
                "[Niflheim/HomesteadStones] Dedicated placement ingress RPC registered (notice handler " +
                (isServer ? "REGISTERED (this peer is the server)" : "skipped (client peer)") + ").");
        }

        // ── CLIENT SIDE: fire a notice for a locally successful placement ────────────────────────────

        /// <summary>Postfix on the placing client's own <c>Player.PlacePiece</c>. On a NON-server peer
        /// (a joined dedicated-server client) whose placement succeeded, fire a notice pointing at the
        /// placed piece's ZDOID so the server can revalidate + credit. On the authoritative host this
        /// no-ops — the listen-host <see cref="FoundationalPlacementObserver"/> already credits directly
        /// (both paths share the same server-validation core; we never double-fire).</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        private static void OnClientPlacePiece()
        {
            try
            {
                // On the server host, the listen-host observer handles our own placement; do not also
                // fire a redundant notice. Only a pure CLIENT peer needs this ingress.
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;

                // Blocker 1: the placed instance is captured from Player.m_placed, NOT the PlacePiece
                // `piece` argument (that is the build ghost/prefab with no world ZDO). A reached
                // PlacePiece postfix is the success signal (vanilla's TryPlacePiece only calls it on
                // success), so there is no bool result to test.
                var piece = PlacedPieceCapture.PlacedPiece();
                if (piece == null) return;

                // Client-side spam guard: only notify for pieces that map to a Foundational stable id.
                // This is NOT authority — the server re-resolves the identity authoritatively.
                string prefabName = piece.gameObject != null ? StripCloneSuffix(piece.gameObject.name) : string.Empty;
                if (string.IsNullOrEmpty(PrefabMap.ResolveStablePieceId(prefabName))) return;

                var nview = piece.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null || !zdo.IsValid()) return;   // no durable instance yet → nothing to point at

                string instanceKey = FormatInstanceKey(zdo.m_uid);
                ZRoutedRpc.instance?.InvokeRoutedRPC(RpcNotice, instanceKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Dedicated placement notice send failed (ignored): " + ex.Message);
            }
        }

        // ── SERVER SIDE: revalidate the authenticated notice, credit through the shared core ─────────

        /// <summary>The SERVER notice handler. <paramref name="sender"/> is the authenticated routed peer
        /// (server peer table), never the payload; <paramref name="instanceKey"/> is the opaque candidate
        /// pointer. The server derives the sender principal from the peer, then hands the key to the
        /// engine-free ingress which independently re-derives existence, prefab/catalog identity, creator
        /// binding, position/Stone Area, success, version, and the repetition key before crediting.</summary>
        private static void RPC_PlacedNotice(long sender, string instanceKey)
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

                // Derive the sender principal from the AUTHENTICATED sender peer (server-owned). Blocker 2:
                // the principal MUST be the sender's character s_playerID (what vanilla stamps as the
                // placed ZDO's creator), NOT peer.m_characterID.UserID (the platform id). The character
                // ZDO is resolved server-side and its s_playerID + stable character ZDOID are bound here.
                // The ingress rejects CreatorMismatch when the pointed-at instance was not created by this
                // authenticated sender.
                if (!TryResolveSenderPrincipal(sender, out string senderPrincipal, out string senderCharacter))
                    return;

                var ingress = server.CreateDedicatedIngress(ZdoServerPlacedInstanceSource.Instance);
                var outcome = ingress.Ingest(senderPrincipal, senderCharacter, instanceKey);

                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + outcome.ToOperatorLine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Dedicated placement ingress threw: " + ex);
            }
        }

        /// <summary>Resolve the authenticated routed sender peer into the shared creator-principal space.
        /// Blocker 2: the placing player's creator identity is the character ZDO's server-owned
        /// <c>s_playerID</c> (what vanilla stamps via <c>SetCreator(GetPlayerID())</c>), NOT the platform
        /// id in <c>peer.m_characterID.UserID</c>. The net48 <see cref="ZdoAuthenticatedSenderSource"/>
        /// resolves the sender's character ZDO and reads that server-owned <c>s_playerID</c> plus the
        /// STABLE character ZDOID; the engine-free <see cref="AuthenticatedSenderBinder"/> renders both
        /// into the principal space the placed ZDO's creator is compared in. Reconnect-stable: a new
        /// session's different character ZDOID still yields the same <c>s_playerID</c> principal.</summary>
        private static bool TryResolveSenderPrincipal(long sender, out string principal, out string character)
        {
            principal = string.Empty;
            character = string.Empty;

            if (!SenderSource.TryResolveSender(sender, out var senderCharacter))
                return false;

            return AuthenticatedSenderBinder.TryBind(senderCharacter, out principal, out character);
        }

        internal static string FormatInstanceKey(ZDOID id) =>
            id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }

    /// <summary>
    /// T009R2 — registers the dedicated placement ingress RPC once ZRoutedRpc is up. ZRoutedRpc is null
    /// at ZNet.Awake and is rebuilt each session, so registration rides <c>Game.Start</c> (the same seam
    /// the Trailborne portal directory uses). Idempotent per ZRoutedRpc instance.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class DedicatedPlacementIngressBootstrap
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { DedicatedPlacementIngressObserver.Register(); }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Dedicated ingress RPC registration failed: " + ex);
            }
        }
    }
}
