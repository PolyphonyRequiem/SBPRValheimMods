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

            // T029 — the Warrior T.W.I.G. Training Local placement gate runtime. The listen-host observer
            // gates a server-run T.W.I.G. (TrainingDummy) placement through the shipped LocalPlacementProvider
            // (FR-016 effect-active / Settlement-policy / build-Permission AND) and UNDOES it on refusal; the
            // dedicated ingress observer does the same for a joined dedicated-server client (client notice →
            // server-side ZDO revalidation → undo on refusal). Both resolve the Warrior gate off the composed
            // FoundationalPlacementObserver.Server; disarmed on a pure client.
            harmony.PatchAll(typeof(Features.Progression.WarriorTwigPlacementObserver));
            harmony.PatchAll(typeof(Features.Progression.WarriorTwigDedicatedIngressObserver));

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

            // T021 remediation 2 — isolated-QA Local-node development seam. DISABLED by default: the direct
            // per-peer handler is only registered when this server-owned flag is ON, and even then only an
            // authenticated Valheim ADMIN sender is accepted. It is the sibling of the relationship seam
            // above: it DEVELOPS a Stone-cultivated Local node (Refined Workshop) through the accepted
            // Facet-commit / node-development handlers so the Local Effect can actually reach Active at
            // runtime before a joined-client proof (the ingress the T021 joined-client rerun found missing).
            // Never a shipping gameplay command; never client-open; production fails closed.
            Features.Progression.LocalProgressionProvisioningAdmin.EnableProvisioning = Config.Bind(
                "Progression", "EnableAdminLocalNodeProvisioning", false,
                "Isolated-QA ONLY. When true, server admins may develop a Homestead Local node (e.g. Refined "
                + "Workshop) for themselves via the SBPR_Niflheim_ProvisionLocalNode direct RPC so the Local "
                + "Effect can be proven Active on a joined client. Server-owned; not client-settable. Leave "
                + "false on any non-QA server.");
            harmony.PatchAll(typeof(Features.Progression.LocalProgressionProvisioningAdmin));
            harmony.PatchAll(typeof(Features.Progression.LocalProgressionProvisioningConsole));

            // T016 shared runtime substrate — the BOUNDED server→client Local Effect activation delivery
            // transport (per-peer request/snapshot ZRpc). The T016 PR (#368) shipped this observer class but
            // never installed its Harmony patches, so the channel was dead: server never registered the
            // request handler and clients never received snapshots. Registering it here is what actually makes
            // the replicated activation read model reach a joined client — the precondition for every
            // gameplay-family consumer (Refined Workshop below, and the later Savor/Practice/T.W.I.G.).
            harmony.PatchAll(typeof(Features.Progression.LocalActivationDeliveryObserver));

            // T026 remediation — the BOUNDED server→client PERSONAL Character-Effect activation delivery
            // transport (per-peer request/snapshot ZRpc). The Local transport above carries Stone-owned LOCAL
            // snapshots only, so Field Fletching I (a personal Character Effect) had no server→client read
            // model and a pure joined client always failed closed. This transport delivers the per-(occupant,
            // character) personal read model (purchase record + active relationship, via DerivedActivationView)
            // so the Field Fletching recipe gate below can craft on a real joined client. Identity is the
            // transport-authenticated bound principal; the client authors nothing.
            harmony.PatchAll(typeof(Features.Progression.PersonalActivationDeliveryObserver));

            // T021 Refined Workshop — the CLIENT-side consumer that wires the shipped, engine-free
            // EffectiveStationLevelProvider into the vanilla crafting runtime. It postfixes the
            // Player.RequiredCraftingStation level gate (rescuing an eligible-portable level-only shortfall
            // with the effective +1 when the Refined Workshop Local Effect is active for the local occupant,
            // read from the replicated activation cache — fail closed) and the InventoryGui requirement UI
            // (recoloring the required-level text when the +1 satisfies it, so real vs +1 is visible). Every
            // decision routes through the same pure provider; structure/build gates are never eligible ops.
            harmony.PatchAll(typeof(Features.Progression.RefinedWorkshopStationLevelPatch));

            // T022-RT — Crafting / Masterwork runtime seam. On the authoritative host, a postfix on
            // InventoryGui.DoCrafting stamps one deterministic, server-keyed-integrity-protected Workmanship
            // Property onto a freshly crafted/upgraded eligible non-stackable durable item while the
            // crafter's personal Masterwork Character Effect is active, explicitly dirtying persistence. The
            // stamp rides ItemData.m_customData through clone/inventory/container transfer and survives a
            // preserving upgrade; a hand-edited/foreign/partial stamp degrades to vanilla. Consumes the
            // shipped, unit-tested WorkmanshipIssuanceProvider + WorkmanshipCodec. Armed with the durable
            // integrity key by the runtime bootstrap below.
            harmony.PatchAll(typeof(Features.Crafting.MasterworkIssuanceObserver));

            // T022 remediation — the REAL upgrade carry-forward seam (AT-ITEM-UPGRADE-PRESERVE). Vanilla
            // InventoryGui.DoCrafting's upgrade branch REMOVES the exact source instance and creates a fresh
            // prefab-backed replacement with an empty custom-data map, destroying the source's server-signed
            // Workmanship stamp. This highest-priority prefix/postfix pair CAPTURES the complete signed stamp map
            // off the upgrade source before vanilla removes it and RESTORES it byte-for-byte onto the fresh
            // replacement at the same grid position — same prov_id/token/property, no re-mint/reissue, quality
            // still rises. Runs before the issuance/delivery postfixes so they see an already-valid stamp and
            // no-op (no duplicate grant). Consumes the engine-free, unit-tested Capture/Restore primitives.
            harmony.PatchAll(typeof(Features.Crafting.MasterworkUpgradePreservationObserver));

            // T022 remediation (t_cdc76200) — the DEDICATED-server joined-client Workmanship delivery
            // transport. The host-only observer above cannot issue on an isolated dedicated server (headless
            // server has no local crafter; a pure joined crafter is unarmed/keyless), so this per-peer ZRpc
            // channel makes issuance authoritative AND client-delivered WITHOUT shipping the raw key: a joined
            // crafter requests issuance, the server re-derives entitlement + mints + SIGNS, the client writes
            // the signed bytes; and a client can ask the server to VALIDATE a stamp it read keylessly. Server
            // registers the request handlers + client the reply handlers on ZNet.OnNewConnection; the client
            // send is a DoCrafting postfix (no-op on the host). Consumes the engine-free, unit-tested
            // WorkmanshipDeliveryService + codec.
            harmony.PatchAll(typeof(Features.Crafting.MasterworkDedicatedDeliveryObserver));

            // T022 remediation R4 (t_4ce3873a) — the isolated-QA Masterwork OWNERSHIP provisioning seam. The
            // direct per-peer handler is only registered when this server-owned flag is ON, and even then only
            // an authenticated Valheim ADMIN sender is accepted. It drives the accepted develop+offer+purchase
            // handlers so a joined admin can acquire an ACTIVE PURCHASED Masterwork personal node — the missing
            // runtime caller that left IsMasterworkActive always false and the genuine four-AT run unreachable.
            // It never mints Attunement or AP: the subject must already hold a Bond + Attunement (sbpr_provision)
            // and earned Personal AP. Never a shipping gameplay command; never client-open; production fails
            // closed (default false).
            Features.Crafting.MasterworkOwnershipProvisioningAdmin.EnableProvisioning = Config.Bind(
                "Crafting", "EnableAdminMasterworkOwnershipProvisioning", false,
                "Isolated-QA ONLY. When true, server admins may acquire an active purchased Masterwork personal "
                + "node for themselves via the SBPR_Niflheim_ProvisionMasterworkOwnership direct RPC (accepted "
                + "develop+offer+purchase handlers) so Masterwork issuance can be proven on a joined client. "
                + "Requires a prior Bond + Attunement and earned Personal AP. Server-owned; not client-settable. "
                + "Leave false on any non-QA server.");
            harmony.PatchAll(typeof(Features.Crafting.MasterworkOwnershipProvisioningAdmin));
            harmony.PatchAll(typeof(Features.Crafting.MasterworkOwnershipProvisioningConsole));

            // T022 remediation — the in-world PRESENTATION seam. Postfixes the static ItemDrop.GetTooltip to
            // append one deterministic "Workmanship: Masterwork" line ONLY when the stamp on that exact
            // instance is confirmed genuine: validated directly under the composed key on the host, or against
            // the server-delivered verdict cache on a pure client (requesting a verdict once per COMPLETE signed-
            // stamp fingerprint, so a post-validation tamper that mutates a signed field misses the cache and is
            // re-validated fail-closed; rendering nothing for absent/malformed/tampered/unconfirmed). This is the joined-client visible
            // artifact and the client-side tamper-degrade. Additive (ADR-0006); mutates nothing.
            harmony.PatchAll(typeof(Features.Crafting.MasterworkWorkmanshipTooltip));

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
            // yield/authority is authored or mutated. The gate reads the authoritative host projection on a
            // listen-host, or the server-stamped personal snapshot (PersonalActivationDeliveryObserver
            // transport above) on a pure joined client, and fails closed when neither is present.
            harmony.PatchAll(typeof(Features.Archer.FieldFletchingRecipeGate));

            // T016 — Savor the Hearth live food-timer delivery seam. The net48 Player.UpdateFood prefix
            // scales ONLY the elapsed food-drain slice for the local player by the shipped
            // SavorTheHearthProvider factor (0.5 active / 1.0 otherwise), reading the established active
            // Savor context off the composed server. The playtest-gated admin seam (config flag +
            // Valheim-admin, DISABLED by default) establishes/clears that context at the sender's current
            // Stone Area so a joined listen-host client can prove in-area 0.5 / exit 1.0. Neither the factor
            // nor the context is client-authored; the observer no-ops when no server context is composed.
            harmony.PatchAll(typeof(Features.Cooking.SavorFoodTimerObserver));
            Features.Cooking.SavorProvisioningAdmin.EnableSeam = Config.Bind(
                "Cooking", "EnableSavorPlaytestSeam", false,
                "Playtest ONLY. When true, server admins may establish/clear an active Savor the Hearth Local "
                + "context at their current Homestead Stone Area via the sbpr_savor console command so live "
                + "food-timer slowing (factor 0.5) can be proven in a joined client. Server-owned; not "
                + "client-settable. Leave false on any non-playtest server.");
            harmony.PatchAll(typeof(Features.Cooking.SavorProvisioningAdmin));
            harmony.PatchAll(typeof(Features.Cooking.SavorProvisioningConsole));

            // T017 — Field Prep personal Character Effect. The net48 station-gate seam that exposes the
            // unchanged vanilla Boar Jerky / Queen's Jam recipes through Bushcraft (station-free) while the
            // effect is active for the local occupant, reading the authoritative host projection through the
            // shipped pure CookingCraftPolicy. Postfix on Player.RequiredCraftingStation only; fails closed
            // off-host / outside any Stone Area / without an active purchase (see FieldPrepRecipeGate).
            harmony.PatchAll(typeof(Features.Cooking.FieldPrepRecipeGate));

            // T018 — Iron Stomach personal Permanent Effect. The net48 food-eat seam that raises the food
            // refresh/replacement threshold from the vanilla 0.5 to 0.75 (refresh at 75% remaining) for the
            // local occupant, reading the authoritative host projection through the shipped pure
            // FoodRefreshThresholdProvider keyed on the character's durable purchase. Postfix on
            // Player.CanEat only; rescues a same-food refresh refusal in the 0.5..0.75 band, never the
            // three-slot cap; fails closed off-host / without a durable Iron Stomach purchase (see
            // IronStomachRefreshGate). Permanent Effect ⇒ no relationship/policy/Stone-Area conjunct.
            harmony.PatchAll(typeof(Features.Cooking.IronStomachRefreshGate));

            // T019 — Swift Preparation personal Character Effect (the sole executable Tier-2 Cooking node).
            // The net48 menu-craft-timer seam that multiplies the vanilla Cooking-skill-ADJUSTED menu-craft
            // duration of an eligible menu-crafted food by 1/3 for the local occupant, reading the
            // authoritative host projection through the shipped pure MenuCraftDurationProvider (purchase +
            // active relationship via T004 DerivedActivationView). Transpiler on InventoryGui.UpdateRecipe
            // scaling the num5 local at the SetMaxValue site — strictly AFTER vanilla skill adjustment; both
            // the progress-bar max and the completion check read that same local. Ineligible crafts and a
            // dormant/unpurchased effect keep the full vanilla duration; fails closed off-host (see
            // SwiftPreparationCraftTimer). Character Effect ⇒ no Local policy / build Permission conjunct.
            harmony.PatchAll(typeof(Features.Cooking.SwiftPreparationCraftTimer));

            Log.LogInfo("[Niflheim.HomesteadStones] Harmony patches installed.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
