using System;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R2 — the net48-ONLY read port that supplies the engine-free
    /// <see cref="DedicatedPlacementIngress"/> with the server's AUTHORITATIVE facts about a placed
    /// physical instance, resolved from the server's own ZDO store (ZDOMan). Nothing here is
    /// client-authored: given a physical-instance key (a ZDOID string), it resolves the ZDO in the
    /// server's store and reads the prefab name, the recorded creator principal, and the world position
    /// straight off the server-owned ZDO. A key that does not resolve returns Absent (the ingress then
    /// rejects NoSuchInstance).
    ///
    /// This references UnityEngine/Valheim (ZDOMan, ZDO, ZDOID, ZNetScene, ZDOVars) and therefore does
    /// NOT link-compile into the net8 test suite; the engine-free ingress it feeds is fully unit-tested
    /// against an in-memory fake of <see cref="IServerPlacedInstanceSource"/>.
    ///
    /// Identity space: the creator principal is rendered as <c>"player:&lt;creatorId&gt;"</c> from the
    /// ZDO's recorded creator (ZDOVars.s_creator — the placing player's profile id, server-persisted).
    /// The dedicated ingress observer renders the AUTHENTICATED sender into the same space, so the
    /// ingress's creator==sender binding is a comparison of two server-derived values. This provisional
    /// mapping is the explicit live-integration seam T009L exercises against a real joined client.
    /// </summary>
    internal sealed class ZdoServerPlacedInstanceSource : IServerPlacedInstanceSource
    {
        internal static readonly ZdoServerPlacedInstanceSource Instance = new ZdoServerPlacedInstanceSource();

        /// <summary>Render a placing-player creator id into the shared server-owned principal space.
        /// Delegates to <see cref="ServerCreatorIdentity"/> so the placed ZDO's creator and the
        /// authenticated sender's character s_playerID are provably in ONE identity space.</summary>
        internal static string CreatorPrincipal(long creatorId) =>
            ServerCreatorIdentity.CreatorPrincipal(creatorId);

        /// <summary>Parse a physical-instance key ("user:id") back into a ZDOID. Returns false on any
        /// malformed key (a fabricated/garbage notice → NoSuchInstance downstream).</summary>
        internal static bool TryParseInstanceKey(string instanceKey, out ZDOID id)
        {
            id = ZDOID.None;
            if (string.IsNullOrEmpty(instanceKey)) return false;
            int sep = instanceKey.IndexOf(':');
            if (sep <= 0 || sep >= instanceKey.Length - 1) return false;
            if (!long.TryParse(instanceKey.Substring(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out long user))
                return false;
            if (!uint.TryParse(instanceKey.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint num))
                return false;
            id = new ZDOID(user, num);
            return true;
        }

        public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
        {
            facts = ServerPlacedInstanceFacts.Absent(instanceKey);

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;
            if (!TryParseInstanceKey(instanceKey, out var id) || id.IsNone()) return false;

            // Resolve the physical instance in the SERVER's own store — a fabricated or already-destroyed
            // key resolves to null and is rejected as NoSuchInstance by the ingress.
            var zdo = zdoMan.GetZDO(id);
            if (zdo == null || !zdo.IsValid()) return false;

            // Prefab identity from the server-owned ZDO (never the notice). ZNetScene maps the ZDO's
            // stable prefab hash back to the prefab name the version-pinned map keys on.
            string prefabName = ResolvePrefabName(zdo);

            // Creator principal = the ZDO's server-recorded creator, rendered into the shared space.
            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            string creatorPrincipal = creator != 0L ? CreatorPrincipal(creator) : string.Empty;

            Vector3 pos = zdo.GetPosition();

            facts = new ServerPlacedInstanceFacts(
                instanceKey, prefabName, creatorPrincipal, pos.x, pos.z, exists: true);
            return true;
        }

        private static string ResolvePrefabName(ZDO zdo)
        {
            var zns = ZNetScene.instance;
            if (zns == null) return string.Empty;
            var prefab = zns.GetPrefab(zdo.GetPrefab());
            return prefab != null ? prefab.name : string.Empty;
        }
    }
}
