using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Crafting;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Crafting;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 remediation — the net48 DEDICATED-server Masterwork Workmanship ISSUANCE + VALIDATION delivery
    /// transport. This is the channel the T022 joined-client QA (t_997667c4) proved missing: the shipped
    /// listen-host-only <see cref="MasterworkIssuanceObserver"/> requires BOTH the armed server integrity key
    /// AND the crafter to be <c>Player.m_localPlayer</c>, so on an isolated dedicated-server topology neither
    /// actor can issue — the headless server has no local crafter and the pure joined crafter is unarmed/
    /// keyless. And the server-only key never reaching a client meant a joined receiver could not even
    /// VALIDATE a stamp. This transport closes both WITHOUT ever shipping the raw integrity secret, mirroring
    /// the accepted <see cref="PersonalActivationDeliveryObserver"/> shape:
    ///
    ///   * ISSUANCE — a joined crafting client that just crafted an eligible item sends a bounded request
    ///     carrying ONLY server-observed produced-item facts (Stone id, item type, eligibility, already-
    ///     stamped hint) + a correlation id. The SERVER authenticates the peer by the delivering <c>ZRpc</c>
    ///     (never the payload), re-derives that peer's BOUND INTERNAL principal + Masterwork activation from
    ///     its OWN composed stores, mints a server-owned provenance id, decides + SIGNS through the engine-
    ///     free <see cref="WorkmanshipDeliveryService"/>, and replies with the stamp FIELDS + the pre-computed
    ///     token. The client writes the exact bytes via <see cref="WorkmanshipCodec.WriteSigned"/> — the
    ///     persisted stamp re-validates byte-identically to a host-stamped one. The key never crosses the wire.
    ///
    ///   * VALIDATION — a client that reads a stamp keylessly (<see cref="WorkmanshipCodec.TryReadRaw"/>)
    ///     relays the fields + token; the server validates under its key and replies Valid/Tampered. The
    ///     presentation seam records the verdict so a legitimate/transferred/upgraded stamp shows confirmed
    ///     and a forged/foreign one degrades to vanilla.
    ///
    /// Trust model matches the sibling ingress/delivery observers exactly: identity is the transport-
    /// authenticated bound principal, entitlement is re-derived server-side, and the integrity key is used
    /// only inside the server process. A hostile client's issuance request can at most ask the server to issue
    /// onto an item the server INDEPENDENTLY confirms its OWN active Masterwork entitles (fail closed
    /// otherwise); its validation request can at most learn whether a stamp it already holds is genuine.
    ///
    /// On the authoritative LISTEN-host the sibling <see cref="MasterworkIssuanceObserver"/> already stamps
    /// directly, so the client-side send here no-ops there (guarded on <c>!IsServer()</c>).
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, Player, InventoryGui, Recipe, ItemDrop, Inventory) →
    /// net48-only, NOT link-compiled into net8. The engine-free service/contract/codec it drives are fully
    /// unit-tested. Clean-side (ADR-0001): base-game transport types only; no other mod code.
    /// </summary>
    [HarmonyPatch]
    internal static class MasterworkDedicatedDeliveryObserver
    {
        // Direct per-peer ZRpc method names (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcIssuanceRequest = "SBPR_Niflheim_WorkmanshipIssuanceRequest";
        internal const string RpcIssuanceGrant = "SBPR_Niflheim_WorkmanshipIssuanceGrant";
        internal const string RpcValidationRequest = "SBPR_Niflheim_WorkmanshipValidationRequest";
        internal const string RpcValidationVerdict = "SBPR_Niflheim_WorkmanshipValidationVerdict";

        private static readonly WorkmanshipIssuanceProvider Provider =
            new WorkmanshipIssuanceProvider(new Domain.Content.HomesteadProgressionCatalog());
        private static readonly WorkmanshipDeliveryService Service = new WorkmanshipDeliveryService(Provider);

        // Client-side pending issuance correlations: correlation id → the exact produced ItemData the client
        // is awaiting a grant for. Bounded; cleared on teardown. Only ever touched on the client main thread.
        private static readonly Dictionary<string, ItemDrop.ItemData> PendingIssuance =
            new Dictionary<string, ItemDrop.ItemData>(StringComparer.Ordinal);
        private static long _correlationCounter;

        // ── SERVER SIDE: register the request handlers on every new connection ────────────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection_Server(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcIssuanceRequest, RPC_IssuanceRequest);
                peer.m_rpc.Register<string>(RpcValidationRequest, RPC_ValidationRequest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship delivery server handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>SERVER handler: mint + sign a Workmanship for the requesting joined crafter. <paramref name="rpc"/>
        /// is the ACTUAL transport connection — the principal + activation are re-derived server-side; the
        /// client's payload supplies only the produced-item facts. Fail closed with a no-write grant.</summary>
        private static void RPC_IssuanceRequest(ZRpc rpc, string payload)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                var key = MasterworkIssuanceObserver.Armed;
                var server = LocalProgressionObserver.Server;
                if (key == null || server == null || string.IsNullOrEmpty(payload)) return;

                var request = WorkmanshipIssuanceRequest.Deserialize(payload);

                if (!ResolveRequesterActivation(znet, rpc, server, request.StoneId, out string crafterAccount, out bool active))
                {
                    rpc.Invoke(RpcIssuanceGrant,
                        WorkmanshipIssuanceGrant.Refused(request.CorrelationId, WorkmanshipIssuanceOutcomeCode.Unresolved).Serialize());
                    return;
                }

                var provenanceId = MintProvenanceId(crafterAccount, request.ItemType);
                var grant = Service.Issue(active, crafterAccount, provenanceId, request, key);
                rpc.Invoke(RpcIssuanceGrant, grant.Serialize());

                if (grant.ShouldWrite)
                    Plugin.Log.LogInfo(
                        "[Niflheim/Crafting] Masterwork delivered Workmanship grant for '" + request.ItemType +
                        "' provId=" + grant.Stamp.ProvenanceId.Value + " crafter=" + crafterAccount + " (joined client).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/Crafting] Workmanship issuance request threw: " + ex);
            }
        }

        /// <summary>SERVER handler: validate a client-presented stamp under the server key and reply the
        /// verdict. The client learns only Valid/Tampered; the key stays server-side.</summary>
        private static void RPC_ValidationRequest(ZRpc rpc, string payload)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                var key = MasterworkIssuanceObserver.Armed;
                if (key == null || string.IsNullOrEmpty(payload)) return;

                var request = WorkmanshipValidationRequest.Deserialize(payload);
                var verdict = Service.Validate(request, key);
                rpc.Invoke(RpcValidationVerdict, verdict.Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/Crafting] Workmanship validation request threw: " + ex);
            }
        }

        /// <summary>Re-derive the requesting peer's internal crafter account + Masterwork activation from the
        /// composed server stores, keyed by the transport-authenticated bound principal (never the payload).
        /// The Stone id is the client's "which Stone am I asking about" hint; the server only reports active
        /// when THAT (occupant, character) genuinely holds active Masterwork there. Fail closed on any absent
        /// fact.</summary>
        private static bool ResolveRequesterActivation(
            ZNet znet, ZRpc rpc, LocalProgressionServer server, StoneId stoneId,
            out string crafterAccount, out bool active)
        {
            crafterAccount = string.Empty;
            active = false;

            var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
            if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts)
                || !AuthenticatedSenderBinder.TryBind(senderFacts, out string account, out string character))
                return false;
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(stoneId.Value)) return false;

            var occupant = new AccountId(account);
            var ch = new CharacterId(character);

            var stone = server.Stones.GetStone(stoneId);
            var characterAgg = server.Characters.GetCharacter(occupant, ch);
            var authority = server.Authority.GetAuthority(occupant, stoneId);
            if (stone == null || characterAgg == null || authority == null) return false;

            crafterAccount = occupant.Value;
            active = Provider.IsMasterworkActive(stone, characterAgg, authority);
            return true;
        }

        private static ItemProvenanceId MintProvenanceId(string crafterAccount, string itemType)
        {
            long stamp = DateTime.UtcNow.Ticks;
            return new ItemProvenanceId(
                itemType + ":" + crafterAccount + ":" + stamp.ToString(CultureInfo.InvariantCulture));
        }

        // ── CLIENT SIDE: register the reply handlers on the server connection ─────────────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection_Client(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcIssuanceGrant, RPC_IssuanceGrant);
                peer.m_rpc.Register<string>(RpcValidationVerdict, RPC_ValidationVerdict);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship delivery client handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT handler: apply a server-minted, server-signed grant onto the exact awaited item and
        /// dirty persistence. A no-write grant (refusal) simply drops the pending correlation — the item stays
        /// vanilla. The client never authors the stamp; it records the exact server bytes.</summary>
        private static void RPC_IssuanceGrant(ZRpc rpc, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(payload)) return;
                var grant = WorkmanshipIssuanceGrant.Deserialize(payload);

                if (!PendingIssuance.TryGetValue(grant.CorrelationId, out var item))
                    return;                                   // unknown/expired correlation — ignore.
                PendingIssuance.Remove(grant.CorrelationId);

                if (!grant.ShouldWrite || item == null) return;

                var accessor = new ItemDataMetadataAccessor(item);

                // Idempotency: never overwrite a stamp that already validates locally is impossible (the client
                // has no key), but a well-formed present stamp means the item was already issued — the server's
                // AlreadyStamped gate would have refused, so ShouldWrite already guards this. Write the bytes.
                WorkmanshipCodec.WriteSigned(accessor, grant.Stamp, grant.Token);

                var localPlayer = Player.m_localPlayer;
                var inv = localPlayer != null ? localPlayer.GetInventory() : null;
                if (inv != null) Traverse.Create(inv).Method("Changed").GetValue();

                Plugin.Log.LogInfo(
                    "[Niflheim/Crafting] Masterwork wrote server-signed Workmanship provId=" +
                    grant.Stamp.ProvenanceId.Value + " onto '" + grant.Stamp.ItemType + "' (joined client).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship grant apply failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT handler: record a server validation verdict into the shared verdict cache so the
        /// presentation seam renders confirmed / vanilla. Never authoritative on its own — the server decided.</summary>
        private static void RPC_ValidationVerdict(ZRpc rpc, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(payload)) return;
                var verdict = WorkmanshipValidationVerdict.Deserialize(payload);
                MasterworkClientState.Verdicts.Apply(verdict);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship verdict apply failed (ignored): " + ex.Message);
            }
        }

        // ── CLIENT SIDE: request issuance for a locally-crafted eligible item ─────────────────────────

        /// <summary>Postfix on the crafting client's own <c>InventoryGui.DoCrafting</c>. On a NON-server peer
        /// (a joined dedicated-server client) that just crafted an eligible non-stackable durable item, send a
        /// bounded issuance request to the server pointing at the just-produced instance. On the authoritative
        /// host this no-ops — the listen-host <see cref="MasterworkIssuanceObserver"/> stamps directly.</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPostfix]
        private static void DoCrafting_Postfix(InventoryGui __instance, Player player)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || znet.IsServer()) return;         // host path handled by the sibling observer.
                if (__instance == null || player == null) return;
                if (player != Player.m_localPlayer) return;

                var recipe = Traverse.Create(__instance).Field("m_craftRecipe").GetValue<Recipe>();
                if (recipe == null || recipe.m_item == null) return;

                var shared = recipe.m_item.m_itemData.m_shared;
                if (shared == null) return;

                bool nonStackable = shared.m_maxStackSize <= 1;
                bool durable = shared.m_useDurability;
                if (!WorkmanshipCodec.IsEligible(nonStackable, durable)) return;

                // Which Stone Area is the local player standing in? Client-visible convenience only; the server
                // independently verifies the requester's entitlement there. Fail quietly when outside any Area.
                var stoneId = MasterworkClientStoneResolver.ResolveLocalStone();
                if (stoneId == null) return;

                var item = FindFreshProducedItem(player, recipe, out bool alreadyStamped);
                if (item == null) return;

                string itemType = StripCloneSuffix(recipe.m_item.gameObject.name);
                string correlation = itemType + ":" + (_correlationCounter++).ToString(CultureInfo.InvariantCulture);
                PendingIssuance[correlation] = item;
                TrimPending();

                var request = new WorkmanshipIssuanceRequest(
                    stoneId.Value, correlation, itemType, nonStackable, durable, alreadyStamped);

                var serverRpc = znet.GetServerRPC();
                serverRpc?.Invoke(RpcIssuanceRequest, request.Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship issuance request send failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT helper the presentation seam calls to ask the server to validate a stamp it read
        /// keylessly. Carries the complete signed-stamp <paramref name="fingerprint"/> so the returned verdict is
        /// bound to the exact bytes validated (a later mutation misses the client cache). No-ops on the host
        /// (which reads its own key directly).</summary>
        internal static void RequestValidation(in WorkmanshipStamp stamp, string token, string fingerprint)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || znet.IsServer()) return;
                var serverRpc = znet.GetServerRPC();
                serverRpc?.Invoke(RpcValidationRequest,
                    new WorkmanshipValidationRequest(stamp.ProvenanceId.Value, stamp, token, fingerprint).Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship validation request send failed (ignored): " + ex.Message);
            }
        }

        /// <summary>Find the just-produced item: the newest matching, eligible instance in the crafter's
        /// inventory that does NOT already carry a well-formed stamp (prefer the fresh one). Reports whether the
        /// selected item already carries a well-formed stamp so the request's idempotency hint is accurate.</summary>
        private static ItemDrop.ItemData? FindFreshProducedItem(Player player, Recipe recipe, out bool alreadyStamped)
        {
            alreadyStamped = false;
            var inv = player.GetInventory();
            if (inv == null) return null;
            string wanted = StripCloneSuffix(recipe.m_item.gameObject.name);

            ItemDrop.ItemData? fallback = null;
            bool fallbackStamped = false;
            var items = inv.GetAllItems();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                if (it == null || it.m_dropPrefab == null) continue;
                if (!string.Equals(StripCloneSuffix(it.m_dropPrefab.name), wanted, StringComparison.Ordinal)) continue;

                var accessor = new ItemDataMetadataAccessor(it);
                var raw = WorkmanshipCodec.TryReadRaw(accessor, out _, out _);
                bool present = raw == WorkmanshipCodec.RawReadState.Present;
                if (fallback == null) { fallback = it; fallbackStamped = present; }
                if (!present) { alreadyStamped = false; return it; }   // first unstamped match — the fresh one.
            }
            alreadyStamped = fallbackStamped;
            return fallback;
        }

        private static void TrimPending()
        {
            // Bound the pending map so a flood of crafts without replies cannot grow it without bound.
            const int cap = 64;
            if (PendingIssuance.Count <= cap) return;
            var stale = new List<string>();
            foreach (var k in PendingIssuance.Keys) { stale.Add(k); if (stale.Count > PendingIssuance.Count - cap) break; }
            foreach (var k in stale) PendingIssuance.Remove(k);
        }

        internal static void ClearClientState()
        {
            PendingIssuance.Clear();
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }

    /// <summary>Process-local client state for the Masterwork delivery transport: the bounded verdict cache the
    /// presentation seam reads. Held here (engine-bound Features namespace) so the net48 seams share one
    /// instance; cleared on ZNet teardown.</summary>
    internal static class MasterworkClientState
    {
        internal static readonly WorkmanshipVerdictCache Verdicts = new WorkmanshipVerdictCache();

        internal static void Clear()
        {
            Verdicts.Clear();
            MasterworkDedicatedDeliveryObserver.ClearClientState();
        }
    }

    /// <summary>Client-side convenience: which Homestead Stone Area the LOCAL player currently stands in, from
    /// CLIENT-VISIBLE resident Stone instances (the persistent Stone prefab replicates to every client). This
    /// is ONLY a hint for which Stone the client asks the server about; it is never authority — the server
    /// independently resolves the requesting peer's bound principal and re-derives entitlement. Mirrors the
    /// resolution the sibling Field Fletching gate uses.</summary>
    internal static class MasterworkClientStoneResolver
    {
        // The client-visible Homestead Stone Area radius mirror (see RefinedWorkshopStationLevelPatch).
        private const float AreaRadius = 20.0f;

        internal static StoneId? ResolveLocalStone()
        {
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (player == null || znet == null) return null;

            var world = new WorldId(HomesteadWorldIdentity.FromUid(znet.GetWorldUID()));
            Vector3 pp = player.transform.position;

            var stones = HomesteadStoneClientIndex.ResidentStones();
            if (stones.Count == 0) return null;

            StoneId? best = null;
            float bestSq = AreaRadius * AreaRadius;
            foreach (var s in stones)
            {
                float dx = pp.x - s.X, dz = pp.z - s.Z;
                float sq = (dx * dx) + (dz * dz);
                if (sq > AreaRadius * AreaRadius) continue;
                if (best == null || sq < bestSq)
                {
                    bestSq = sq;
                    best = StoneId.FromHostZone(world, s.ZoneX, s.ZoneZ);
                }
            }
            return best;
        }
    }
}
