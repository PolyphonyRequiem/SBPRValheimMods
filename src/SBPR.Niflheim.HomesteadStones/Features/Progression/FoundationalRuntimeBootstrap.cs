using System;
using System.Globalization;
using System.IO;
using BepInEx;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Diagnostics;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009 — engine-bound composition + startup rehydration for the live Foundational AP runtime. On
    /// the authoritative server (and only there) this builds the durable FoundationalProgressionServer
    /// under a stable, world-scoped server-owned path and arms the placement observer. On a client the
    /// patch no-ops (the observer stays disarmed).
    ///
    /// net48-only (references ZNet/UnityEngine + the ZDO-backed Stone AP sink), so it is not
    /// link-compiled into the net8 test suite; the engine-free FoundationalProgressionServer.Create it
    /// calls IS unit-tested. Rehydration is inherited from Create (the two durable journals replay onto
    /// their projections at construction), so a server restart resumes exactly the persisted state.
    /// </summary>
    [HarmonyPatch]
    internal static class FoundationalRuntimeBootstrap
    {
        private static ZNet? composedFor;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        private static void OnZNetAwake(ZNet __instance)
        {
            try
            {
                if (__instance == null || !__instance.IsServer()) return;
                if (ReferenceEquals(composedFor, __instance) && FoundationalPlacementObserver.Server != null) return;

                string durableDir = ResolveDurableDirectory(__instance);
                var server = FoundationalProgressionServer.Create(
                    durableDir,
                    familyResolver: ServerHomesteadFamilyResolver.Instance,
                    bondAuthority: ServerHomesteadBondPolicy.Instance,
                    stoneApStore: new ZdoStoneProgressionStore());

                FoundationalPlacementObserver.Server = server;
                composedFor = __instance;
                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Foundational live runtime composed (server-authoritative). " +
                    $"durable='{durableDir}' observed={server.Runtime.Log.TotalObserved} " +
                    $"rehydratedReceipts={server.Receipts.DurableOperationIds().Count}.");

                // T016 shared runtime substrate: compose the LIVE Local progression runtime over the SAME
                // durable directory + the Foundational server's shared character/authority stores and
                // relationship handler. This wires the accepted Facet/Development/LocalPolicy handlers +
                // the LocalActivationService into the live server so a Local node can actually reach
                // Developed at runtime and per-occupant activation can be derived + delivered (the whole
                // substrate the T021 investigation found was never composed). The Stone aggregate store is
                // seeded in-memory and rehydrated from the four durable progression journals at handler
                // construction; production shares it alongside the world Stone ZDO.
                try
                {
                    var stoneAggregates = new InMemoryStoneAggregateStore();
                    // T016 fix-forward (PR #368 review Blocker 1): the Homestead owner authority is DERIVED
                    // from committed Governor-bond state over the SAME shared character/authority stores the
                    // Local runtime composes onto — never the dead OwnerByStone map (which had no writer and
                    // forced every Local Effect dormant). The validated owner is the account currently holding
                    // the authorized Homestead:All Governor bond.
                    var ownerPresence = new GovernorPresenceResolver(server.Characters, server.Authority);
                    var ownerAuthority = new CommittedGovernorOwnerAuthority(ownerPresence);

                    var localServer = LocalProgressionServer.Create(
                        durableDir,
                        stones: stoneAggregates,
                        characters: server.Characters,
                        authority: server.Authority,
                        relationships: server.Relationships,
                        familyResolver: ServerHomesteadFamilyResolver.Instance,
                        governorAuthority: ServerHomesteadGovernorAuthority.Instance,
                        developmentAuthority: ServerHomesteadDevelopmentAuthority.Instance,
                        ownerAuthority: ownerAuthority,
                        // T022 split-ledger fix: share the Foundational runtime's AUTHORITATIVE Personal-AP
                        // earn ledger so Masterwork purchase reads the same balance genuine placement credits
                        // (earned − spent), instead of the character aggregate's stored-but-never-earned field.
                        characterApStore: server.CharacterApStore);

                    LocalProgressionObserver.Server = localServer;

                    // T029 — arm the Warrior T.W.I.G. placement gate against the SAME authoritative Stone
                    // aggregate store + governance resolver this Local runtime composes. This is the rebind
                    // that removed the provisional Stone-state source: the T.W.I.G. gate and a Local Effect
                    // snapshot now read one progression truth.
                    server.ArmWarriorTwig(stoneAggregates, ownerPresence);

                    // T022 — arm the Masterwork issuance seam with the durable, server-owned Workmanship
                    // integrity key over the SAME durable directory. The key protects every issued
                    // Workmanship stamp with an HMAC token so a hand-edited/foreign/partial stamp degrades to
                    // vanilla; issuance runs only on this authoritative host where both the key and the
                    // composed server stores exist.
                    var workmanshipKey = SBPR.Niflheim.HomesteadStones.Features.Crafting.WorkmanshipIntegrityKeyFile.LoadOrCreate(durableDir);
                    SBPR.Niflheim.HomesteadStones.Features.Crafting.MasterworkIssuanceObserver.Arm(workmanshipKey);

                    // T023 — arm the Built to Last seams with the SAME durable server key. The two item
                    // provenances share one secret but sign DISJOINT canonical domains ("workmanship-v1" vs
                    // "builttolast-v1") under disjoint custom-data key namespaces, so neither token can be
                    // replayed as the other and there is only one key file / rotation surface to operate.
                    SBPR.Niflheim.HomesteadStones.Features.Crafting.BuiltToLastIssuanceObserver.Arm(workmanshipKey);
                    SBPR.Niflheim.HomesteadStones.Features.Crafting.BuiltToLastMaxDurabilityPatch.ClearMemo();

                    Plugin.Log.LogInfo(
                        "[Niflheim/HomesteadStones] Local progression runtime composed (server-authoritative). " +
                        $"durable='{durableDir}' warriorTwigArmed={server.WarriorTwigGate != null}.");

                    // ADO #123 — OFFLINE operator shape report. The smallest thing that satisfies "an
                    // operator can ask": emit it once at boot, right here, where BOTH composition roots
                    // are in hand. This is a THIN CALLER of the engine-free, unit-tested
                    // OperatorShapeReport/HomesteadHandlerWiringObserver pair — this net48 side owns no
                    // logic, so nothing here is untestable. It adds NO Harmony patch class: this method
                    // already lives inside FoundationalRuntimeBootstrap, which Plugin.Awake() registers.
                    //
                    // Passing the two real roots (and no provisioning ingress, because production composes
                    // one only inside the config-gated admin seam) means every line is an OBSERVATION.
                    // A green report here proves shape, never playability — the rendered text says so.
                    try
                    {
                        var wiring = HomesteadHandlerWiringObserver.Observe(server, localServer);
                        Plugin.Log.LogInfo(OperatorShapeReport.BuildAndRender(
                            localServer.Catalog, wiring, new ReceiptRecovery(server.Receipts)));
                    }
                    catch (Exception rex)
                    {
                        // A diagnostic must never take down composition.
                        Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Operator shape report failed: " + rex);
                    }
                }
                catch (Exception lex)
                {
                    Plugin.Log.LogError("[Niflheim/HomesteadStones] Local progression composition failed: " + lex);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Foundational runtime composition failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZNetDestroyed(ZNet __instance)
        {
            if (ReferenceEquals(composedFor, __instance))
            {
                composedFor = null;
                FoundationalPlacementObserver.Server = null;
                SBPR.Niflheim.HomesteadStones.Features.Crafting.MasterworkIssuanceObserver.Disarm();
                SBPR.Niflheim.HomesteadStones.Features.Crafting.BuiltToLastIssuanceObserver.Disarm();
                SBPR.Niflheim.HomesteadStones.Features.Crafting.BuiltToLastMaxDurabilityPatch.ClearMemo();
                SBPR.Niflheim.HomesteadStones.Features.Crafting.MasterworkClientState.Clear();
                LocalProgressionObserver.Clear();
            }
        }

        /// <summary>Stable, world-scoped, server-owned durable directory for the progression journals.
        /// Lives under the BepInEx config root (a writable, server-owned location) keyed by the world's
        /// name + UID so two worlds never share journals and the same world always resolves the same
        /// path across restarts.</summary>
        private static string ResolveDurableDirectory(ZNet znet)
        {
            string worldName = SanitizeSegment(SafeWorldName(znet));
            string uid = znet.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            return Path.Combine(Paths.ConfigPath, "sbpr-niflheim-homestead", worldName + "-" + uid);
        }

        private static string SafeWorldName(ZNet znet)
        {
            try { return znet.GetWorldName() ?? "world"; }
            catch { return "world"; }
        }

        private static string SanitizeSegment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "world";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }

    /// <summary>Server-owned per-Stone family classification for the live runtime. Provisional proof
    /// policy: the shipped Homestead Stones are the Settlement/Homestead family. A production build
    /// sources this from the Stone aggregate; kept as a small seam so the engine-free relationship
    /// handler stays pure.</summary>
    internal sealed class ServerHomesteadFamilyResolver : IStoneFamilyResolver
    {
        internal static readonly ServerHomesteadFamilyResolver Instance = new ServerHomesteadFamilyResolver();

        public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
        {
            family = "Settlement";
            variant = "Homestead";
            return true;
        }
    }

    /// <summary>Server-owned Bond authority policy for the live runtime. Provisional proof policy:
    /// authorizes the authored "Homestead:All" Governor range. Never client-authored.</summary>
    internal sealed class ServerHomesteadBondPolicy : IBondAuthorityPolicy
    {
        internal static readonly ServerHomesteadBondPolicy Instance = new ServerHomesteadBondPolicy();

        public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
            out string grantedRange, out string grantedRole)
        {
            grantedRange = requestedResponsibilityRange ?? string.Empty;
            grantedRole = "Governor";
            return string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal);
        }
    }
}
