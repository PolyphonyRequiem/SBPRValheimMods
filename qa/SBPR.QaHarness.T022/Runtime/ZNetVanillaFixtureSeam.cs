// Engine-bound REAL vanilla fixture seam (ADR-0009 §3.5, PR #408 VANILLA-BINDINGS.md §3.5) — M3R.
//
// ZNetVanillaFixtureSeam is the thin Valheim/Unity implementation of the engine-free
// IVanillaFixtureSeam that the owned-resource ledger drives through SeamFixtureWorld. It
// is the ONLY game-touching code in the M3R fixture slice; every invariant that keeps this
// safe is enforced ABOVE it in engine-free, headlessly-tested code (the manifest allowlist,
// the plan validator, the request mapper, the execution-time authority gate). This adapter
// therefore only has to do the bounded vanilla spawn/grant/despawn the game itself does.
//
// ADR-0006 ADDITIVE COMPLIANCE (the load-bearing point):
//   • Materials: granted by ItemDrop.DropItem of the vanilla item's OWN m_dropPrefab
//     (ObjectDB.GetItemPrefab(id) -> ItemDrop -> DropItem). That is the exact vanilla
//     "spawn a real world item" seam (§3.5) — the game clones the item's drop prefab, we
//     do not clone-and-strip anything.
//   • Stations / anchors: placed by Instantiate of the UNMODIFIED vanilla prefab read as a
//     blueprint via ZNetScene.GetPrefab (which fires no Awake). This is a genuine
//     server-authoritative spawn of the game's own prefab — the SAME additive pattern the
//     product's HomesteadStoneWorldPlacement uses (Instantiate the registered prefab, never
//     strip components off it). There is NO subtractive clone: we never Instantiate a prefab
//     to mutate it and remove parts; we place it as-is and, at cleanup, destroy it whole.
//   • Cleanup: the network-aware despawn path (ZNetView.Destroy / ZNetScene.Destroy /
//     ZDOMan.DestroyZDO) so the removal replicates and the world save carries no ZDO
//     (AT-QA-CLEANUP-NO-LEAK). We destroy ONLY the exact instance whose ZDOID the ledger
//     recorded — an unrelated object is never reachable here.
//
// The spawned-instance HANDLE the ledger stores is the object's ZDOID serialized as
// "UserID:ID" (full stable identity, never a truncated numeric — two ZDOs can share ID
// across UserIDs). IsLiveInstance/Despawn resolve that handle back to the live ZDO/GO.
//
// FIREWALL: this seam spawns ordinary allowlisted vanilla items/stations only; it mints no
// product identity/entitlement/AP/ownership/signature/verdict (ADR-0009 §4). Reflecting on
// / calling the base game is clean-room permitted (ADR-0001); no other-mod source is used.
using System;
using System.Globalization;
using SBPR.QaHarness.T022.Core.ControlPlane;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>Live Valheim implementation of the additive vanilla fixture seam (server role, post-world-load).</summary>
    internal sealed class ZNetVanillaFixtureSeam : IVanillaFixtureSeam
    {
        // Where owned fixtures are spawned relative to. The engine-free bounds already cap the
        // radius; the concrete origin is the server's own reference point (world origin here — a
        // disposable QA world — offset by the bounded radius). Kept deterministic and bounded.
        private static Vector3 OriginFor(double posRadius)
        {
            float r = (float)posRadius;
            // A single, deterministic bounded offset from world origin. QA fixtures are scaffolding
            // in a disposable world; they do not need player-relative placement, and using a fixed
            // origin keeps the seam free of GUI/Player state (server role has no local player).
            return new Vector3(r, 0f, 0f);
        }

        /// <summary>True when the named prefab exists in the live ZNetScene or ObjectDB (drift guard §3.5).</summary>
        public bool PrefabExists(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            try
            {
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefabName) != null) return true;
                if (ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(prefabName) != null) return true;
                return false;
            }
            catch (Exception)
            {
                return false; // fail closed — an unresolvable prefab is treated as absent
            }
        }

        /// <summary>
        /// Additively place an allowlisted vanilla station/anchor prefab (Instantiate of the
        /// unmodified vanilla prefab read as a blueprint — ADR-0006, no clone-and-strip). Returns
        /// the spawned instance's ZDOID handle, or empty on failure (a failure the ledger records).
        /// </summary>
        public string SpawnPrefab(string prefabName, double posRadius)
        {
            var zns = ZNetScene.instance;
            if (zns == null) return string.Empty;

            GameObject? prefab = zns.GetPrefab(prefabName); // blueprint read — fires no Awake
            if (prefab == null) return string.Empty;

            // Genuine server-authoritative spawn of the game's OWN prefab (additive; the same pattern
            // the product uses). We place it as-is and never strip components off it.
            GameObject go = UnityEngine.Object.Instantiate(prefab, OriginFor(posRadius), Quaternion.identity);
            var nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                // No networked identity to track/clean — destroy immediately and report failure.
                if (nview != null) { try { nview.Destroy(); return string.Empty; } catch (Exception) { } }
                UnityEngine.Object.Destroy(go);
                return string.Empty;
            }
            return EncodeHandle(nview.GetZDO().m_uid);
        }

        /// <summary>
        /// Grant a bounded quantity of an allowlisted vanilla item by spawning its OWN drop prefab
        /// via the vanilla ItemDrop.DropItem seam (§3.5). Returns the spawned drop's ZDOID handle.
        /// </summary>
        public string GrantItem(string itemId, long qty)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return string.Empty;

            GameObject? itemPrefab = odb.GetItemPrefab(itemId);
            var itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (itemDrop == null || itemDrop.m_itemData == null) return string.Empty;

            int amount = qty < 1 ? 1 : (qty > int.MaxValue ? int.MaxValue : (int)qty);
            // ItemDrop.DropItem Clones the item data onto a real world drop — the vanilla grant path.
            ItemDrop spawned = ItemDrop.DropItem(itemDrop.m_itemData, amount, OriginFor(0.0), Quaternion.identity);
            if (spawned == null) return string.Empty;
            var nview = spawned.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                if (nview != null) { try { nview.Destroy(); } catch (Exception) { } }
                else UnityEngine.Object.Destroy(spawned.gameObject);
                return string.Empty;
            }
            return EncodeHandle(nview.GetZDO().m_uid);
        }

        /// <summary>
        /// Network-aware despawn of the EXACT instance the ledger recorded (by ZDOID handle). Owner-
        /// claim then destroy so the removal replicates and no ZDO survives (AT-QA-CLEANUP-NO-LEAK).
        /// Returns true when an instance/ZDO existed and was removed; false when already gone (the
        /// ledger treats already-gone as idempotent success).
        /// </summary>
        public bool Despawn(string spawnedInstanceId)
        {
            if (!TryDecodeHandle(spawnedInstanceId, out ZDOID id) || id.IsNone()) return false;

            var zns = ZNetScene.instance;
            if (zns != null)
            {
                GameObject? go = zns.FindInstance(id);
                if (go != null)
                {
                    var nview = go.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid())
                    {
                        if (!nview.IsOwner()) nview.ClaimOwnership();
                        nview.Destroy();
                        return true;
                    }
                    zns.Destroy(go);
                    return true;
                }
            }

            var zdoMan = ZDOMan.instance;
            if (zdoMan != null)
            {
                var zdo = zdoMan.GetZDO(id);
                if (zdo != null && zdo.IsValid())
                {
                    if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                    zdoMan.DestroyZDO(zdo);
                    return true;
                }
            }
            return false; // already gone
        }

        /// <summary>True iff the recorded instance still resolves to a live ZDO/GameObject (crash reconcile §3.5).</summary>
        public bool IsLiveInstance(string spawnedInstanceId)
        {
            if (!TryDecodeHandle(spawnedInstanceId, out ZDOID id) || id.IsNone()) return false;
            try
            {
                var zns = ZNetScene.instance;
                if (zns != null && zns.FindInstance(id) != null) return true;
                var zdoMan = ZDOMan.instance;
                if (zdoMan != null)
                {
                    var zdo = zdoMan.GetZDO(id);
                    return zdo != null && zdo.IsValid();
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── Handle encoding: full stable ZDOID as "UserID:ID" (never truncated). ──
        private static string EncodeHandle(ZDOID id) =>
            id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

        private static bool TryDecodeHandle(string? handle, out ZDOID id)
        {
            id = ZDOID.None;
            if (string.IsNullOrEmpty(handle)) return false;
            int sep = handle!.IndexOf(':');
            if (sep <= 0 || sep >= handle.Length - 1) return false;
            if (!long.TryParse(handle.Substring(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out long user))
                return false;
            if (!uint.TryParse(handle.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint num))
                return false;
            id = new ZDOID(user, num);
            return true;
        }
    }
}
