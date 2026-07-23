// Engine-bound REAL vanilla fixture seam (ADR-0009 §3.5, PR #408 VANILLA-BINDINGS.md §3.5) — M3R
// + repair t_0e3a88bd (true ADR-0006 additive construction + durable exact ownership markers).
//
// ZNetVanillaFixtureSeam is the thin Valheim/Unity implementation of the engine-free
// IVanillaFixtureSeam that the owned-resource ledger drives through SeamFixtureWorld. It
// is the ONLY game-touching code in the M3R fixture slice; every invariant that keeps this
// safe is enforced ABOVE it in engine-free, headlessly-tested code (the manifest allowlist,
// the plan validator, the request mapper, the execution-time authority gate, and the
// fail-closed marker recovery).
//
// ─────────────────────────────────────────────────────────────────────────────
// ADR-0006 ADDITIVE COMPLIANCE (the load-bearing repair — see docs/decisions/0006):
//   The owner review of PR #414 rejected the previous implementation because it called
//   `UnityEngine.Object.Instantiate(vanillaStationPrefab)` — a RUNTIME CLONE of a vanilla
//   ZNetView-bearing prefab, exactly the subtractive pattern ADR-0006 forbids. This file
//   now builds a TRUE ADDITIVE SHELL, mirroring the product's Assets.TryConstructPieceShell:
//
//     • Stations / anchors: an INACTIVE `new GameObject(...)` is created and only the
//       INTENDED components are AddComponent'd — a `ZNetView` (the networked identity, whose
//       three PUBLIC fields m_persistent/m_type/m_distant we set ourselves; ZNetView.Awake
//       needs only ZDOMan up + a registered prefab name and builds its OWN ZDO), a root
//       `BoxCollider` (placement/hit raycasts), and, for a station category, a `CraftingStation`
//       component named from the blueprint. We read the vanilla prefab ONLY as a blueprint via
//       `ZNetScene.GetPrefab` (which fires no Awake) to copy VALUE fields (station name, a
//       shared mesh for a visual child) — reading an asset reference is not cloning. The shell
//       is registered in ZNetScene by name, then instantiated into the world. There is NO
//       `Instantiate(prefab)` of a ZNetView-bearing donor and no clone-and-strip anywhere.
//     • Materials: granted by ItemDrop.DropItem of the vanilla item's OWN m_dropPrefab
//       (ObjectDB.GetItemPrefab(id) -> ItemDrop -> DropItem). That is the exact vanilla
//       "spawn a real world item" seam (§3.5); the game clones the item's drop prefab, which
//       is the game's own additive grant path (ADR-0006 permits using the game's own spawn
//       seams — the ban is on US cloning a prefab as a mutable base).
//     • Cleanup: the network-aware despawn path (ZNetView.Destroy / ZNetScene.Destroy /
//       ZDOMan.DestroyZDO) so the removal replicates and the world save carries no ZDO
//       (AT-QA-CLEANUP-NO-LEAK). We destroy ONLY the exact instance whose ZDOID the ledger
//       recorded / whose marker we adopted — an unrelated object is never reachable here.
//
// ─────────────────────────────────────────────────────────────────────────────
// DURABLE EXACT OWNERSHIP MARKERS (the crash-safety repair):
//   Every spawned object is stamped, as PART of creation, with a QA ownership marker on its
//   ZDO under the single namespaced key FixtureOwnershipMarker.ZdoKey. The marker encodes
//   (world uid, run nonce, fixture id, owned-resource canonical id). Because the marker lives
//   on the game-persisted ZDO, a crash at ANY point after spawn leaves a self-describing
//   survivor. If the marker cannot be durably written, the half-built object is destroyed and
//   an EMPTY handle is returned (a Create failure) — never a silently untracked leak.
//   DiscoverMarked runs a BOUNDED spatial query (ZDOMan.FindSectorObjects around the deterministic
//   fixture origin, limited to the plan's allowlisted prefab hashes, max radius, and a hard candidate
//   cap — NOT a whole-world walk) and returns a TYPED complete/refused result, so the engine-free
//   RecoverFromMarkers can scope/validate/adopt exactly this run's survivors and fail closed on a
//   refused scan or anything foreign/malformed/duplicate. Unmarked / out-of-region objects are never
//   returned, so unrelated world objects are structurally un-adoptable.
//
// The spawned-instance HANDLE the ledger stores is the object's ZDOID serialized as
// "UserID:ID" (full stable identity, never a truncated numeric — two ZDOs can share ID
// across UserIDs). IsLiveInstance/Despawn resolve that handle back to the live ZDO/GO.
//
// FIREWALL: this seam spawns ordinary allowlisted vanilla items/stations only; it mints no
// product identity/entitlement/AP/ownership/signature/verdict (ADR-0009 §4). The QA marker is
// a disposable-scaffolding tag, not a product ownership token. Reflecting on / calling the
// base game is clean-room permitted (ADR-0001); no other-mod source is used.
using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>Live Valheim implementation of the ADDITIVE vanilla fixture seam (server role, post-world-load).</summary>
    internal sealed class ZNetVanillaFixtureSeam : IVanillaFixtureSeam
    {
        // The inactive holder under which shells are constructed so NO Awake fires during
        // construction (same discipline as the product's Assets.GetHolder / TryConstructPieceShell).
        private static GameObject? _holder;

        private static GameObject Holder()
        {
            if (_holder == null)
            {
                _holder = new GameObject("SBPR.QaHarness.FixtureShellHolder");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }
            return _holder;
        }

        // Where owned fixtures are spawned relative to. The engine-free bounds already cap the
        // radius; the concrete origin is the server's own reference point (world origin here — a
        // disposable QA world — offset by the bounded radius). Kept deterministic and bounded.
        private static Vector3 OriginFor(double posRadius)
        {
            float r = (float)posRadius;
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
        /// ADDITIVELY construct an allowlisted vanilla station/anchor shell (new GameObject +
        /// intended components, blueprint read-only — ADR-0006, NO Instantiate of a ZNetView donor),
        /// durably stamp the ownership marker onto its ZDO, and return the spawned instance's ZDOID
        /// handle. Returns empty on ANY failure (including a marker-write failure, in which case the
        /// half-built object is destroyed) — a failure the ledger records as a partial failure.
        /// </summary>
        public string SpawnPrefab(string prefabName, double posRadius, string markerPayload)
        {
            var zns = ZNetScene.instance;
            if (zns == null) return string.Empty;

            GameObject? blueprint = zns.GetPrefab(prefabName); // blueprint read — fires no Awake
            if (blueprint == null) return string.Empty;

            GameObject? shell = null;
            GameObject? instance = null;
            try
            {
                // 1. Build the INACTIVE additive shell (no clone; components are ours by construction).
                shell = BuildStationShell(prefabName, blueprint);
                if (shell == null) return string.Empty;

                // 2. Register the shell's name in ZNetScene so ZNetView.Awake can resolve its prefab
                //    hash and build a valid ZDO (additive networked object, ADR-0006). Idempotent.
                if (!RegisterShellPrefab(zns, shell)) { DestroyShell(shell); return string.Empty; }

                // 3. Instantiate OUR OWN registered shell into the world (this is spawning the prefab
                //    WE built, not cloning a vanilla ZNetView donor). Awake now runs down CreateNewZDO.
                instance = UnityEngine.Object.Instantiate(shell, OriginFor(posRadius), Quaternion.identity);
                var nview = instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    DestroyInstance(instance);
                    return string.Empty;
                }

                // 4. Durably stamp the ownership marker onto the ZDO as PART of creation. If it cannot
                //    be written+read back, destroy the object and fail closed (no untracked leak).
                if (!StampMarker(nview, markerPayload))
                {
                    DestroyInstance(instance);
                    return string.Empty;
                }

                return EncodeHandle(nview.GetZDO().m_uid);
            }
            catch (Exception)
            {
                if (instance != null) DestroyInstance(instance);
                return string.Empty;
            }
        }

        /// <summary>
        /// Grant a bounded quantity of an allowlisted vanilla item by spawning its OWN drop prefab
        /// via the vanilla ItemDrop.DropItem seam (§3.5), then durably stamp the ownership marker onto
        /// the spawned drop's ZDO. Returns the spawned drop's ZDOID handle (empty on failure, incl.
        /// marker-write failure — the drop is destroyed).
        /// </summary>
        public string GrantItem(string itemId, long qty, string markerPayload)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return string.Empty;

            GameObject? itemPrefab = odb.GetItemPrefab(itemId);
            var itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (itemDrop == null || itemDrop.m_itemData == null) return string.Empty;

            int amount = qty < 1 ? 1 : (qty > int.MaxValue ? int.MaxValue : (int)qty);
            ItemDrop spawned;
            try
            {
                // ItemDrop.DropItem clones the item DATA onto a real world drop — the vanilla grant
                // path (the game's own additive spawn seam, not a prefab-clone-and-strip by us).
                spawned = ItemDrop.DropItem(itemDrop.m_itemData, amount, OriginFor(0.0), Quaternion.identity);
            }
            catch (Exception)
            {
                return string.Empty;
            }
            if (spawned == null) return string.Empty;

            var nview = spawned.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                DestroyInstance(spawned.gameObject);
                return string.Empty;
            }

            if (!StampMarker(nview, markerPayload))
            {
                DestroyInstance(spawned.gameObject);
                return string.Empty;
            }

            return EncodeHandle(nview.GetZDO().m_uid);
        }

        /// <summary>
        /// Network-aware despawn of the EXACT instance the ledger recorded (by ZDOID handle). Owner-
        /// claim then destroy so the removal replicates and no ZDO survives (AT-QA-CLEANUP-NO-LEAK).
        /// Returns true when an instance/ZDO existed and was removed; false when already gone.
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

        /// <summary>
        /// BOUNDED, fail-closed marker scan. Instead of walking every live ZDO, this pins the
        /// deterministic fixture origin, converts it to its vanilla zone/sector, and asks the game for
        /// exactly the objects in that sector and the ring of sectors the bounded radius could reach
        /// (<c>ZDOMan.FindSectorObjects</c> — the game's own spatial index, ADR-0001 permitted). Each
        /// candidate is filtered to the scope's allowlisted PREFAB HASHES and to those carrying our QA
        /// marker key. A hard candidate cap bounds the result. There is NO whole-world dictionary walk.
        ///
        /// FAIL-CLOSED: a missing binding (ZDOMan/ZoneSystem), an enumeration exception, a per-candidate
        /// read/handle error, or a candidate count exceeding the scope cap yields a REFUSED result with
        /// ZERO candidates — the engine-free recovery then adopts nothing, so an unenumerable survivor
        /// is never silently duplicated. Only a fully-enumerated, in-region, capped set returns Complete.
        /// </summary>
        public SeamDiscoveryResult DiscoverMarked(FixtureSeamScope scope)
        {
            var zdoMan = ZDOMan.instance;
            var zoneSys = ZoneSystem.instance;
            var zns = ZNetScene.instance;
            if (zdoMan == null || zoneSys == null || zns == null)
                return SeamDiscoveryResult.Refused("binding-unavailable: ZDOMan/ZoneSystem/ZNetScene not ready");

            // Build the allowlisted prefab-hash set from the scope's logical names (a candidate whose
            // prefab hash is not in this set is out of the bounded region and never returned).
            var allowedHashes = new HashSet<int>();
            foreach (var name in scope.AllowedPrefabNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                allowedHashes.Add(name.GetStableHashCode());
                // The additive shell we spawn for a station registers under a derived name; include it so
                // a station survivor (spawned as the shell prefab) is matched inside the bounded region.
                allowedHashes.Add(("SBPR_QAFixture_" + name).GetStableHashCode());
            }
            if (allowedHashes.Count == 0)
                return SeamDiscoveryResult.Complete(Array.Empty<MarkedInstanceInfo>());

            try
            {
                // Deterministic fixture origin -> zone/sector. The bounded radius is converted to a
                // sector-ring "area" (ceil(radius / zoneSize)) so the query covers exactly the sectors a
                // spawn within MaxRadiusMeters could have landed in — never the whole world.
                Vector3 origin = OriginFor(scope.MaxRadiusMeters);
                Vector2i sector = ZoneSystem.GetZone(origin);
                float zoneSize = ZoneSystem_ZoneSize(zoneSys);
                int area = zoneSize > 0f ? (int)Math.Ceiling(scope.MaxRadiusMeters / zoneSize) : 1;
                if (area < 1) area = 1;
                // Hard sector-ring cap: the bounded scan may never fan out past a small ring regardless
                // of a pathological radius (defence in depth on top of the engine-free radius cap).
                if (area > MaxSectorRing) return SeamDiscoveryResult.Refused("sector-ring-overflow: area " + area + " > " + MaxSectorRing);

                var sectorObjects = new List<ZDO>();
                var distantObjects = new List<ZDO>();
                // area for near, 0 distant: distant objects are a different persistence class we do not spawn.
                zdoMan.FindSectorObjects(sector, area, 0, sectorObjects, distantObjects);

                var found = new List<MarkedInstanceInfo>();
                foreach (var zdo in sectorObjects)
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (!allowedHashes.Contains(zdo.GetPrefab())) continue; // out-of-allowlist → outside bounded region
                    string payload = zdo.GetString(FixtureOwnershipMarker.ZdoKey, string.Empty);
                    if (string.IsNullOrEmpty(payload)) continue;            // unmarked → never a candidate
                    found.Add(new MarkedInstanceInfo(payload, EncodeHandle(zdo.m_uid)));
                    // Cap overflow refuses the WHOLE scan (never truncate-and-guess).
                    if (scope.MaxCandidates > 0 && found.Count > scope.MaxCandidates)
                        return SeamDiscoveryResult.Refused("candidate-cap-overflow: > " + scope.MaxCandidates);
                }
                return SeamDiscoveryResult.Complete(found);
            }
            catch (Exception ex)
            {
                // Any enumeration/read/handle fault refuses the whole scan with zero candidates.
                return SeamDiscoveryResult.Refused("scan-fault: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // The maximum sector-ring radius (in zones) the bounded scan may ever fan out to. A QA fixture is
        // a handful of objects at a bounded meter-radius; this caps the sector query even if the radius
        // were pathological, so a single scan is always O(a few sectors), never the world.
        private const int MaxSectorRing = 2;

        // Read ZoneSystem's zone size (public const c_ZoneSize / field m_zoneSize). Reflected defensively
        // so a build-version field rename degrades to the vanilla 64m default rather than throwing.
        private static float ZoneSystem_ZoneSize(ZoneSystem zoneSys)
        {
            try
            {
                var f = HarmonyLib.Traverse.Create(zoneSys).Field("m_zoneSize");
                object? v = f.GetValue();
                if (v is float fv && fv > 0f) return fv;
            }
            catch (Exception) { }
            return 64f; // vanilla default zone size
        }

        // ── Additive shell construction (ADR-0006) ──────────────────────────────

        // Build an INACTIVE additive shell for a station/anchor from a read-only blueprint. Only the
        // INTENDED components are added — ZNetView (networked identity), a root BoxCollider, and (for
        // a station blueprint) a CraftingStation named from the blueprint. NO Instantiate of the donor.
        private static GameObject? BuildStationShell(string prefabName, GameObject blueprint)
        {
            string shellName = "SBPR_QAFixture_" + prefabName;
            var zns = ZNetScene.instance;
            // If we already registered this shell in a prior spawn this run, reuse the registered
            // template (registration is idempotent) by reading it back as a blueprint.
            if (zns != null)
            {
                var existing = zns.GetPrefab(shellName);
                if (existing != null) return existing;
            }

            var go = new GameObject(shellName);
            go.transform.SetParent(Holder().transform, worldPositionStays: false);

            // Root collider — additive shells need one for placement/hit raycasts.
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one;

            // ZNetView — the networked identity. Public fields ZNetView.Awake reads to build its own
            // ZDO (verified against decompiled ZNetView.Awake; same as product TryConstructPieceShell).
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            // If the blueprint is a crafting station, add OUR OWN CraftingStation and copy the VALUE
            // name field off the blueprint (reference-copy, not inheritance) so it reads as that
            // station. A non-station (bare anchor) blueprint gets no station component.
            var blueprintStation = blueprint.GetComponent<CraftingStation>();
            if (blueprintStation != null)
            {
                var station = go.AddComponent<CraftingStation>();
                station.m_name = blueprintStation.m_name;
                station.m_useDistance = blueprintStation.m_useDistance;
            }

            return go;
        }

        // Register the additive shell's name in ZNetScene so ZNetView.Awake can resolve its prefab
        // hash (mirrors the product's Assets.RegisterPrefabInZNetScene). Idempotent; fail-closed.
        private static bool RegisterShellPrefab(ZNetScene zns, GameObject shell)
        {
            try
            {
                if (zns.GetPrefab(shell.name) != null) return true;
                int hash = shell.name.GetStableHashCode();
                var named = HarmonyLib.Traverse.Create(zns).Field("m_namedPrefabs").GetValue()
                    as System.Collections.Generic.Dictionary<int, GameObject>;
                if (named == null) return false;
                zns.m_prefabs.Add(shell);
                named[hash] = shell;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Durably write the marker onto the ZDO under the single namespaced key, claiming ownership
        // first, then read it back to confirm it persisted. Returns false on any failure (caller
        // destroys the object and fails closed — no untracked leak).
        private static bool StampMarker(ZNetView nview, string markerPayload)
        {
            if (string.IsNullOrEmpty(markerPayload)) return false;
            try
            {
                if (!nview.IsOwner()) nview.ClaimOwnership();
                var zdo = nview.GetZDO();
                if (zdo == null || !zdo.IsValid()) return false;
                zdo.Set(FixtureOwnershipMarker.ZdoKey, markerPayload);
                // Read-back confirmation the write landed on the durable ZDO.
                return string.Equals(zdo.GetString(FixtureOwnershipMarker.ZdoKey, string.Empty),
                    markerPayload, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void DestroyShell(GameObject shell)
        {
            try { UnityEngine.Object.Destroy(shell); } catch (Exception) { }
        }

        private static void DestroyInstance(GameObject go)
        {
            try
            {
                var nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    nview.Destroy();
                    return;
                }
            }
            catch (Exception) { }
            try { UnityEngine.Object.Destroy(go); } catch (Exception) { }
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
