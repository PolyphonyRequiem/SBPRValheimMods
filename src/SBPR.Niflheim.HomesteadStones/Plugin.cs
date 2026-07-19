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

            // R5/R6 acceptance — bounded startup drift assertions, and R6 (Blocker 7): the result is
            // LOAD-BEARING. When Verify() is false, realization is disabled: the placement loop will not run
            // its create/reconcile patches, so a drifted game update degrades to "no realization + a loud
            // error" instead of seating Stones against renamed/removed engine callsites.
            Features.HomesteadStone.HomesteadStoneWorldPlacement.RealizationEnabled =
                Features.HomesteadStone.HomesteadRuntimeDriftCheck.Verify();

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

            // IAP-007W — live session admission. Composes the shipped account+character admission stack
            // (Tracer 1/2) on the authoritative server and reconciles it against the connected-peer set on
            // the ZDOMan.Update cadence: a peer whose server-observed profile s_playerID + authenticated
            // socket resolve is admitted end-to-end and its BOUND INTERNAL principal is PUBLISHED into the
            // Foundational runtime's BoundSessionPrincipalIndex; a disconnected peer's session is closed
            // (lease released + session-qualified unbind). This is what makes the placement observer/ingress
            // resolve a real bound principal instead of always failing closed. Server-gated; identity is
            // 100% server-observed off the transport-authenticated peer, never a client payload.
            harmony.PatchAll(typeof(Features.PilotIdentity.PilotSessionLifecycleObserver));

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

            // T016 shared runtime substrate — the BOUNDED server→client Local Effect activation delivery
            // transport (per-peer request/snapshot ZRpc). The T016 PR (#368) shipped this observer class but
            // never installed its Harmony patches, so the channel was dead: server never registered the
            // request handler and clients never received snapshots. Registering it here is what actually makes
            // the replicated activation read model reach a joined client — the precondition for every
            // gameplay-family consumer (Refined Workshop below, and the later Savor/Practice/T.W.I.G.).
            harmony.PatchAll(typeof(Features.Progression.LocalActivationDeliveryObserver));

            // T021 Refined Workshop — the CLIENT-side consumer that wires the shipped, engine-free
            // EffectiveStationLevelProvider into the vanilla crafting runtime. It postfixes the
            // Player.RequiredCraftingStation level gate (rescuing an eligible-portable level-only shortfall
            // with the effective +1 when the Refined Workshop Local Effect is active for the local occupant,
            // read from the replicated activation cache — fail closed) and the InventoryGui requirement UI
            // (recoloring the required-level text when the +1 satisfies it, so real vs +1 is visible). Every
            // decision routes through the same pure provider; structure/build gates are never eligible ops.
            harmony.PatchAll(typeof(Features.Progression.RefinedWorkshopStationLevelPatch));

            // T025-RT — Archer / Practice Range runtime seam. Registers the Practice Arrow item + its
            // 100-for-8-Wood recipe, wires the deterministic vanilla target return (ArrowPractice added to
            // ArcheryTarget.m_returnAmmo), and adds the exact vanilla piece_ArcheryTarget build piece to
            // the Hammer table. The per-attempt placement capability AND (active Local Effect AND ordinary
            // build Permission, spec FR-016) is enforced by the placement gate. 0 ammo damage is data-
            // driven (zero-damage Ammo item) so the bow's own draw damage is retained with no patch.
            harmony.PatchAll(typeof(Features.Archer.ArcherContentRegistrar));
            harmony.PatchAll(typeof(Features.Archer.ArcheryTargetPlacementGate));

            // T026 — Archer / Field Fletching I runtime seam. A personal Character Effect that, while
            // active for the acting occupant (purchase record + active relationship, via the shipped
            // BushcraftRecipeProvider), exposes the UNCHANGED vanilla Wood Arrow recipe through Bushcraft —
            // i.e. makes ArrowWood craftable without its ordinary station. Exposure only: no recipe input/
            // yield/authority is authored or mutated. The gate reads the authoritative host projection and
            // fails closed on a pure client (no personal-effect delivery channel exists yet — follow-up).
            harmony.PatchAll(typeof(Features.Archer.FieldFletchingRecipeGate));

            Log.LogInfo("[Niflheim.HomesteadStones] Harmony patches installed.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
