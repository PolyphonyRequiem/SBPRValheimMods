using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SBPR.Niflheim.HomesteadStones
{
    /// <summary>
    /// Niflheim — Homestead Stones. The "Claim a Homestead" vertical slice
    /// (niflheim/docs/slices/claim-homestead.md). Niflheim-ONLY content — lives in the
    /// SBPR monorepo as a sibling to SBPR.Trailborne, but is NOT part of the standalone
    /// Trailborne Thunderstore mod (it only exists because the world is persistent+social;
    /// fails the "ships to a stranger's vanilla server" ownership test).
    ///
    /// STAGE (2026-07-11): first live-integration cut. Registers the additive gameplay root,
    /// accepted V12 AssetBundle visual, current Location-zone-coordinate D3 keys, and the
    /// provisional deterministic Meadows selector/seating path. Claim/account/UI and final
    /// migration/compatibility policy remain later playtest-gated slices.
    ///
    /// T009 (2026-07-15): the live Foundational AP runtime seam. On the authoritative server this
    /// plugin composes the durable FoundationalProgressionServer (Application/Runtime) and installs the
    /// engine-bound FoundationalPlacementObserver so a real successful placement flows through the
    /// shipped adapter → pipeline → durable receipt. Composition is server-gated and wired lazily from
    /// ZNet start (see Features/Progression/FoundationalRuntimeBootstrap.cs).
    /// </summary>
    [BepInPlugin(ModId, ModName, ModVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string ModId      = "net.danielgreen.sbpr.niflheim.homesteadstones";
        public const string ModName    = "SBPR Niflheim — Homestead Stones";
        // Single source of truth = <Version> in the csproj; GenerateVersionConstant emits
        // GeneratedModVersion before compile (same pattern as SBPR.Trailborne).
        public const string ModVersion = GeneratedModVersion;

        internal static ManualLogSource Log = null!;   // set in Awake
        internal static string PluginFolder = null!;    // set in Awake
        private  Harmony harmony = null!;               // set in Awake

        private void Awake()
        {
            Log = Logger;
            PluginFolder = Path.GetDirectoryName(Info.Location) ?? BepInEx.Paths.PluginPath;
            Log.LogInfo($"[Niflheim.HomesteadStones] Awake — {ModName} {ModVersion} booting (folder={PluginFolder})");

            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(Features.HomesteadStone.HomesteadStoneRegistrar));
            harmony.PatchAll(typeof(Features.HomesteadStone.HomesteadStoneWorldPlacement));

            // T009 — live Foundational AP runtime. The bootstrap Harmony patch composes the durable
            // FoundationalProgressionServer on the authoritative server and arms the placement observer.
            harmony.PatchAll(typeof(Features.Progression.FoundationalRuntimeBootstrap));
            harmony.PatchAll(typeof(Features.Progression.FoundationalPlacementObserver));

            // T009R4 — dedicated-server placement ingress (transport-bound + race-safe). A joined
            // dedicated-server client's build never runs Player.PlacePiece on the server; the client sends
            // a DIRECT per-peer notice, the server authenticates the sender by the delivering ZRpc (never a
            // forgeable routed id), captures it into a bounded pending-revalidation queue, and credits
            // through the SAME shared validation core once the ZDO replicates. Registration is per-peer
            // (ZNet.OnNewConnection) and the queue pumps on ZDOMan.Update — no separate bootstrap patch.
            harmony.PatchAll(typeof(Features.Progression.DedicatedPlacementIngressObserver));

            // T009R3 (Blocker 3) — admin/test relationship provisioning seam. DISABLED by default: the
            // routed handler is only registered when this server-owned flag is ON, and even then only an
            // authenticated Valheim ADMIN sender is accepted. It exists so a real playtest session can
            // ESTABLISH the Bond/Attunement RecordFoundationalPlacement requires (T009L). Never a shipping
            // gameplay command; never client-open.
            Features.Progression.RelationshipProvisioningAdmin.EnableProvisioning = Config.Bind(
                "Progression", "EnableAdminRelationshipProvisioning", false,
                "Playtest ONLY. When true, server admins may provision a Bond/Attunement for themselves via "
                + "the SBPR_Niflheim_ProvisionRelationship routed RPC so live Foundational AP can be proven. "
                + "Server-owned; not client-settable. Leave false on any non-playtest server.");
            harmony.PatchAll(typeof(Features.Progression.RelationshipProvisioningAdmin));
            harmony.PatchAll(typeof(Features.Progression.RelationshipProvisioningConsole));

            Log.LogInfo("[Niflheim.HomesteadStones] Harmony patches installed.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
