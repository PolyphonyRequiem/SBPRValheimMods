using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.ReloadHarness
{
    /// <summary>
    /// Niflheim 0003 — QA-ONLY live cold-reload CAPTURE observer (net48, graphical client).
    ///
    /// SCOPE HONESTY (read first): this class is the runtime BOOTSTRAP that arms the engine-free capture
    /// core (Domain/ReloadHarness) inside a REAL Valheim client, on ONE boot, and writes ONE bounded
    /// primitive-fact capture to disk. It is INERT by default: it only arms when
    /// <see cref="EnableCaptureHarness"/> is true (set in Plugin.Awake from a server-owned config flag that
    /// defaults false) AND the engine-free <see cref="HomesteadReloadArmingGate"/> approves a valid QA-only
    /// manifest supplied entirely out-of-band by the OPERATE-staged fixture files. A compiled class is not
    /// enough — the Harmony patch below is what welds it into ZoneSystem, and
    /// <see cref="HomesteadReloadHarnessConformance"/> proves that weld exists at boot.
    ///
    /// This observer captures the facts of exactly ONE boot (PRE or POST, taken from the manifest). It does
    /// NOT save the world, does NOT terminate the client, and does NOT compare PRE vs POST — the external
    /// controller/runbook drives the two boots and the comparison. Building/compiling/registering this does
    /// NOT prove live reload, persistence, deployment, or playability.
    /// </summary>
    [HarmonyPatch]
    internal static class HomesteadReloadCaptureObserver
    {
        // Mirror the production placement constants so the harness runs the SAME selector config the shipped
        // HomesteadStoneWorldPlacement uses — never a divergent copy.
        private const string SelectorVersion = "niflheim-homestead-playtest-v1";
        private const float MinimumDistance = 128f;
        private const double Density = 0.40;

        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index),
            StringComparer.Ordinal);

        /// <summary>Master arm flag, set from Plugin.Awake off a server-owned config flag defaulting to false.
        /// False ⇒ the capture path is dead code even though the patch is woven.</summary>
        internal static bool EnableCaptureHarness { get; set; }

        /// <summary>The QA-only manifest, loaded out-of-band from the OPERATE fixture. Null ⇒ refuse to arm.</summary>
        internal static HomesteadReloadHarnessManifest? Manifest { get; set; }

        /// <summary>Directory the single boot capture is written to (OPERATE-scoped disposable evidence dir).</summary>
        internal static string CaptureOutputDir { get; set; } = string.Empty;

        /// <summary>Build/artifact provenance hashes the controller injects (source/product/harness).</summary>
        internal static HomesteadReloadProvenance Provenance { get; set; } =
            new HomesteadReloadProvenance(string.Empty, string.Empty, string.Empty);

        /// <summary>The save receipt for this boot (present only on a POST boot after a real save).</summary>
        internal static HomesteadReloadSaveReceipt SaveReceipt { get; set; } = HomesteadReloadSaveReceipt.None;

        private static ZoneSystem? capturedFor;

        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        [HarmonyPostfix]
        private static void OnZoneSystemStart(ZoneSystem __instance)
        {
            if (!EnableCaptureHarness) return;   // inert in normal product use
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (ReferenceEquals(capturedFor, __instance)) return;

            var arming = HomesteadReloadArmingGate.Evaluate(Manifest, ResolveExpectedFixtureUid());
            if (!arming.IsArmed)
            {
                Plugin.Log.LogError(
                    "[Niflheim/ReloadHarness] REFUSED to arm capture: "
                    + string.Join(" | ", arming.Refusals));
                return;
            }

            capturedFor = __instance;
            __instance.StartCoroutine(CaptureLoop(__instance));
        }

        [HarmonyPatch(typeof(ZoneSystem), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZoneSystemDestroyed(ZoneSystem __instance)
        {
            if (ReferenceEquals(capturedFor, __instance)) capturedFor = null;
        }

        private static long ResolveExpectedFixtureUid() => Manifest?.ExpectedWorldUid ?? 0L;

        private static System.Collections.IEnumerator CaptureLoop(ZoneSystem zoneSystem)
        {
            var deadline = Time.realtimeSinceStartup + (float)(Manifest?.ReadinessWaitSeconds ?? 0.0);
            while (!zoneSystem.LocationsGenerated)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Plugin.Log.LogError(
                        "[Niflheim/ReloadHarness] Bounded readiness wait elapsed before Locations generated; capture aborted.");
                    yield break;
                }
                yield return new WaitForSeconds(1f);
            }

            HomesteadReloadCapture capture;
            try
            {
                capture = BuildCapture(zoneSystem);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"[Niflheim/ReloadHarness] Capture build failed: {exception}");
                yield break;
            }

            try
            {
                WriteCapture(capture);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"[Niflheim/ReloadHarness] Capture write failed: {exception}");
                yield break;
            }
        }

        /// <summary>Enumerate the live ZoneSystem's eligible Meadows Locations into the SAME
        /// <see cref="HomesteadCandidate"/> shape the production placement builds, then hand them to the
        /// engine-free <see cref="HomesteadReloadCaptureBuilder"/>, which runs the shipped selector. The harness
        /// never reimplements selection and never projects two snapshots from one literal.</summary>
        private static HomesteadReloadCapture BuildCapture(ZoneSystem zoneSystem)
        {
            var worldUid = ResolveWorldUid();
            var candidates = BuildCandidates(zoneSystem);
            var reconciliation = BuildReconciliationReceipt(worldUid);
            var phase = Manifest?.ExpectedWorldUid == worldUid
                ? ResolvePhaseFromSession()
                : throw new HomesteadReloadCaptureException(
                    $"Live world UID {worldUid} != manifest fixture UID {Manifest?.ExpectedWorldUid}; refusing capture.");

            var session = new HomesteadReloadSession(
                bootId: Guid.NewGuid().ToString("N"),
                sessionId: ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture),
                processId: System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                bootGeneration: phase == HomesteadReloadPhase.Pre ? 1L : 2L);

            return HomesteadReloadCaptureBuilder.Build(
                phase,
                worldUid,
                SelectorVersion,
                MinimumDistance,
                Density,
                candidates,
                reconciliation,
                session,
                Provenance,
                phase == HomesteadReloadPhase.Post ? SaveReceipt : HomesteadReloadSaveReceipt.None,
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }

        private static HomesteadReloadPhase ResolvePhaseFromSession()
        {
            // The controller stamps the intended phase via an env/config token on each boot; default PRE.
            var token = Environment.GetEnvironmentVariable("NIFLHEIM_RELOAD_HARNESS_PHASE");
            return string.Equals(token, "POST", StringComparison.OrdinalIgnoreCase)
                ? HomesteadReloadPhase.Post
                : HomesteadReloadPhase.Pre;
        }

        private static List<HomesteadCandidate> BuildCandidates(ZoneSystem zoneSystem) =>
            zoneSystem.m_locationInstances
                .Where(pair => pair.Value.m_location != null && EligibleHosts.Contains(pair.Value.m_location.m_prefabName))
                .Select(pair => new HomesteadCandidate(
                    pair.Value.m_location.m_prefabName,
                    pair.Key.x,
                    pair.Key.y,
                    pair.Value.m_position.x,
                    pair.Value.m_position.z,
                    Math.Max(2f, pair.Value.m_location.m_exteriorRadius)))
                .ToList();

        /// <summary>Read the authoritative reconciliation receipt from the resident Stone ZDOs: each resident
        /// Stone's full stable ZDO id + host zone, flagged selected (kept) vs removed. This is the same resident-
        /// ZDO enumeration the production ReconcileStoneAreas performs, surfaced as engine-free facts.</summary>
        private static List<HomesteadReloadReconcileEntry> BuildReconciliationReceipt(long worldUid)
        {
            var entries = new List<HomesteadReloadReconcileEntry>();
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return entries;

            var worldIdentity = HomesteadWorldIdentity.FromUid(worldUid);
            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(
                Features.HomesteadStone.HomesteadStoneRegistrar.PrefabName, found, ref index)) { }

            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                int zoneX = zdo.GetInt(Features.HomesteadStone.HomesteadStoneData.LocationZoneXKey, int.MinValue);
                int zoneZ = zdo.GetInt(Features.HomesteadStone.HomesteadStoneData.LocationZoneZKey, int.MinValue);
                string zdoWorld = zdo.GetString(Features.HomesteadStone.HomesteadStoneData.WorldIdentityKey, string.Empty);
                string prefab = zdo.GetString(Features.HomesteadStone.HomesteadStoneData.HostPrefabKey, string.Empty);
                bool keyed = zoneX != int.MinValue && zoneZ != int.MinValue;
                // A Stone bound to a DIFFERENT world identity, or unkeyed, is a removed/stale entry, never selected.
                bool removed = !keyed ||
                    (!string.IsNullOrEmpty(zdoWorld) && !string.Equals(zdoWorld, worldIdentity, StringComparison.Ordinal));
                var zdoId = zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture) + ":" +
                    zdo.m_uid.ID.ToString(CultureInfo.InvariantCulture);
                entries.Add(new HomesteadReloadReconcileEntry(
                    zdoId, prefab, keyed ? zoneX : 0, keyed ? zoneZ : 0, removed));
            }
            return entries;
        }

        private static long ResolveWorldUid()
        {
            var world = ZNet.instance?.GetWorldUID() ?? 0L;
            return world;
        }

        private static void WriteCapture(HomesteadReloadCapture capture)
        {
            if (string.IsNullOrWhiteSpace(CaptureOutputDir))
                throw new HomesteadReloadCaptureException("No capture output dir configured; refusing to write.");
            Directory.CreateDirectory(CaptureOutputDir);
            var phase = capture.Phase == HomesteadReloadPhase.Pre ? "pre" : "post";
            var path = Path.Combine(CaptureOutputDir, $"homestead-reload-capture-{phase}.txt");
            File.WriteAllText(path, capture.ToCanonicalText(), new UTF8Encoding(false));
            Plugin.Log.LogInfo(
                $"[Niflheim/ReloadHarness] Wrote {phase.ToUpperInvariant()} capture: "
                + $"candidates={capture.CandidateCount} assigned={capture.AssignedCount} "
                + $"hosts={capture.Hosts.Count} -> {path}");
        }
    }

    /// <summary>
    /// Niflheim 0003 — startup conformance for the QA-only cold-reload capture harness. Mirrors the operator
    /// surface conformance pattern: walks Harmony's registry after the harness PatchAll and proves the capture
    /// observer produced at least one WOVEN patch owned by this mod, so "compiled but never registered" is a
    /// LOUD boot error instead of silent dead code. It does NOT prove the harness ran or that reload was proven.
    /// </summary>
    internal static class HomesteadReloadHarnessConformance
    {
        internal static void Verify(string ownerId)
        {
            try
            {
                var woven = CollectWovenPatchClasses(ownerId);
                bool captureObserver = woven.Contains(typeof(HomesteadReloadCaptureObserver));

                Plugin.Log.LogInfo(
                    "[Niflheim/ReloadHarness] Capture harness conformance: capture-observer="
                    + (captureObserver ? "WOVEN" : "MISSING")
                    + ", armed=" + (HomesteadReloadCaptureObserver.EnableCaptureHarness ? "true" : "false") + ".");

                if (!captureObserver)
                    Plugin.Log.LogError(
                        "[Niflheim/ReloadHarness] ✗ CAPTURE HARNESS DEAD — HomesteadReloadCaptureObserver produced no "
                        + "woven patch. Did Plugin.Awake() forget harmony.PatchAll(typeof(HomesteadReloadCaptureObserver))? "
                        + "The QA-only cold-reload capture path is NON-FUNCTIONAL until fixed.");
                else
                    Plugin.Log.LogInfo(
                        "[Niflheim/ReloadHarness] ✓ Capture harness woven (registration present; inert unless QA-enabled).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/ReloadHarness] Capture harness conformance check threw: " + ex);
            }
        }

        private static HashSet<Type> CollectWovenPatchClasses(string ownerId)
        {
            var woven = new HashSet<Type>();
            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                if (target == null) continue;
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;

                ReadOnlyCollection<Patch>[] buckets =
                {
                    info.Prefixes, info.Postfixes, info.Transpilers,
                    info.Finalizers, info.ILManipulators,
                };
                foreach (ReadOnlyCollection<Patch> bucket in buckets)
                {
                    if (bucket == null) continue;
                    foreach (Patch p in bucket)
                    {
                        if (p == null || p.owner != ownerId) continue;
                        Type? declaring = p.PatchMethod?.DeclaringType;
                        if (declaring != null) woven.Add(declaring);
                    }
                }
            }
            return woven;
        }
    }
}
