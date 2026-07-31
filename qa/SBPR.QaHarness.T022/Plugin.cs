using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;
using SBPR.QaHarness.T022.Runtime;
using UnityEngine;

namespace SBPR.QaHarness.T022
{
    /// <summary>
    /// SBPR.QaHarness.T022 — QA-only, fail-closed BepInEx test-helper (ADR-0009).
    ///
    /// <para>
    /// <b>M2R UPDATE — real fail-closed runtime control plane.</b> The plugin is still
    /// DEFAULT-DISABLED: <see cref="Awake"/> logs a DISARMED banner and returns. It attempts
    /// to arm only when the runner has placed an explicit local bootstrap document (path in
    /// the <c>SBPR_QA_T022_BOOTSTRAP</c> env var — never inferred) AND the world has loaded
    /// AND the assembly-drift guard passes AND the exact M1 arming AND-gate accepts (exact
    /// disposable world UID+name, hard production deny, role/actor, six hashes, nonce/expiry,
    /// capability manifest, HMAC secret). Only then does it attach the <see cref="ControlPlaneComponent"/>
    /// pump: a real loopback TcpListener (client role, 127.0.0.1 only, operator-token gated,
    /// bounded framing, single-slot dispatcher) or the per-peer ZRpc responder (server role,
    /// NO host listener). Until every gate holds it performs ZERO I/O and ZERO game mutation.
    /// </para>
    ///
    /// <para>
    /// M2R execution scope is status/ping/reject only — no fixtures, actions, observation, or
    /// M3 wiring. An admitted mutating verb is acknowledged not-implemented; the runtime never
    /// mutates game state in this milestone. The external Python runner remains the sole
    /// scenario state machine and PASS/FAIL composer (ADR-0009 §6). Secrets are held only in
    /// memory and never logged.
    /// </para>
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "net.danielgreen.sbpr.qa.harness.t022";
        public const string PluginName = "SBPR QA Harness T022";
        public const string PluginVersion = "0.2.0";

        /// <summary>Env var the runner sets to the absolute path of its local arm-bootstrap JSON. Absent => disarmed.</summary>
        public const string BootstrapEnvVar = "SBPR_QA_T022_BOOTSTRAP";

        private Harmony? _harmony;
        private Harmony? _autoJoinHarmony;
        private Harmony? _readinessHarmony;
        private ControlPlaneComponent? _component;
        private bool _armAttempted;

        private void Awake()
        {
            LogDisarmedBanner();

            // M6-JOIN3: headless auto-join hook. Independent of the world-load arm gate below,
            // because it must fire at the PRE-JOIN character-select screen (before ZNet.World
            // exists) to supply the "Start" click vanilla otherwise waits on. Default-disabled:
            // TryInstall no-ops unless SBPR_QA_CONNECT is present for this launch.
            _autoJoinHarmony = new Harmony(PluginGuid + ".autojoin");
            FejdStartupAutoJoin.TryInstall(_autoJoinHarmony, Logger);

            // Default-disabled: with no explicit bootstrap signal the helper does nothing further.
            string? bootstrapPath = Environment.GetEnvironmentVariable(BootstrapEnvVar);
            if (string.IsNullOrEmpty(bootstrapPath))
            {
                Logger.LogInfo("SBPRQA: no bootstrap signal; remaining DISARMED (default-disabled).");
                return;
            }

            // The world is not loaded at Awake; defer the arm attempt to a delayed pump that
            // re-checks each frame until EITHER readiness source is present (or gives up silently).
            //
            // Install the client-role readiness patch NOW (spec §4), gated by the same bootstrap
            // presence that guards the deferrer: on a joining client ZNet.World never becomes non-null
            // via the server path, so the arm gate would spin until the frame budget expires and NEVER
            // reach TryArm. The Player.OnSpawned postfix must already be live to catch the client's
            // spawn — the event that makes the client readiness source report ready. Registering only
            // when a bootstrap is present preserves "no bootstrap => zero hooks" (a disarmed default
            // launch installs no readiness patch). Dedicated Harmony id so OnDestroy can UnpatchSelf it.
            _readinessHarmony = new Harmony(PluginGuid + ".readiness");
            _readinessHarmony.PatchAll(typeof(PlayerOnSpawnedReadinessPatch));

            var deferrer = gameObject.AddComponent<ArmDeferrer>();
            deferrer.Begin(this, bootstrapPath!);
        }

        /// <summary>
        /// The single arm entry point, invoked by the deferrer once ZNet.World is loaded. Runs the
        /// full fail-closed gate chain; on any failure it logs the reason and stays disarmed.
        /// </summary>
        internal void TryArm(string bootstrapPath)
        {
            if (_armAttempted) return;
            _armAttempted = true;

            // 1. Bootstrap document must exist and parse.
            string text;
            try { text = File.ReadAllText(bootstrapPath); }
            catch (Exception) { Logger.LogWarning("SBPRQA: bootstrap unreadable; staying DISARMED."); return; }

            ArmBootstrap boot = ArmBootstrapParser.Parse(text);
            if (!boot.Ok || boot.Manifest == null)
            {
                Logger.LogWarning($"SBPRQA: bootstrap parse failed ({boot.Reason}); staying DISARMED.");
                return;
            }

            // 2. Assembly / MVID drift guard (PR #408 binding pins) — fail closed on mismatch.
            //    Route the observer's version-read diagnostics through the logger: a silent
            //    empty version read here (Invoke arity bug) masqueraded as a real observation and
            //    cost the entire M6-PIN chain (M6-OBSERVER). Warnings surface it next time.
            DriftCheck drift = AssemblyDriftGuard.Check(
                GameAssemblyProbe.Read(msg => Logger.LogWarning(msg)));
            if (!drift.Ok)
            {
                Logger.LogWarning($"SBPRQA: assembly drift ({drift.Reason}); staying DISARMED.");
                return;
            }

            // 3. Observed world + hashes from the live process. Readiness is now role-symmetric
            //    (spec-role-split-arm-gate.md §4 G2 / AC2): arm may proceed when EITHER the server
            //    source (ZNet.World loaded) OR the client source (!IsServer && spawned local player)
            //    reports ready. A joining client never satisfies the server world-load path via the
            //    host branch, so without this it would false-fail here. The world-identity read below
            //    still uses ZNetWorldIdentitySource: on a joining client ZNet.World is populated from
            //    network data, so WorldUid/WorldName are readable on both roles — widening the
            //    readiness check does not break the subsequent ArmingGate world-identity match.
            var worldSource = new ZNetWorldIdentitySource();
            bool serverReady = ((IServerReadinessSource)worldSource).Ready; // ZNet.World != null
            bool clientReady = new ZNetClientReadinessSource().Ready;       // !IsServer && spawned && m_localPlayer != null
            if (!serverReady && !clientReady)
            {
                Logger.LogWarning("SBPRQA: neither server nor client readiness holds at arm; staying DISARMED.");
                return;
            }
            var observedWorld = new WorldIdentity(worldSource.WorldUid, worldSource.WorldName ?? string.Empty);

            // 4. The fail-closed arming AND-gate. Observed hashes are supplied by the runner in
            //    the bootstrap (the helper trusts the runner's pinned set; drift on the live game
            //    assembly is separately guarded in step 2).
            long now = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
            var policy = BuildPolicy(boot.Manifest);
            var observedHashes = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in boot.Hashes) observedHashes[kv.Key] = kv.Value;
            ArmDecision decision = ArmingGate.Evaluate(boot.Manifest, observedWorld, observedHashes, policy, now);
            if (!decision.Armed || decision.State == null)
            {
                Logger.LogWarning($"SBPRQA: arming gate refused ({decision.Reason}); staying DISARMED.");
                return;
            }

            // 5. Gate passed. Attach the pump and (server role) install the Harmony ZRpc bridge.
            _component = gameObject.AddComponent<ControlPlaneComponent>();
            var authority = new ZNetServerAuthorityRecheck();

            if (decision.State.Role == HarnessRole.Server)
            {
                // M3R: build the REAL vanilla fixture executor for the server role. The engine-free
                // executor composes the crash-safe owned-resource ledger + the additive vanilla seam
                // behind its own execution-time authority gate; the durable snapshot lives under the
                // BepInEx config dir scoped to this world so a crash recovers only THIS run's ids.
                // The factory receives the responder's SHARED delivering-peer state so the fixture
                // gate binds the same peer + connection generation the control plane validated.
                var fixtureSeam = new ZNetVanillaFixtureSeam();
                var fixtureWorld = new SeamFixtureWorld(fixtureSeam);
                var fixtureAuthority = new ZNetServerAuthoritySource();
                string snapshotDir = Path.Combine(Paths.ConfigPath, "sbpr-qa-t022-fixtures",
                    decision.State.World.WorldUid.ToString(System.Globalization.CultureInfo.InvariantCulture));

                // The disposable world uid + armed run nonce every spawned fixture's durable ownership
                // marker is stamped with and crash recovery is scoped to (ADR-0009 §5.4 repair).
                var fixtureRunContext = new FixtureRunContext(decision.State.World.WorldUid, decision.State.Nonce);

                _component.Configure(decision.State, boot.OperatorToken, boot.LoopbackPort, authority,
                    peerState =>
                    {
                        var executor = new ServerFixtureExecutor(
                            fixtureAuthority, peerState, fixtureWorld,
                            fixtureId => new LedgerSnapshotStore(Path.Combine(snapshotDir, fixtureId + ".ledger")),
                            fixtureRunContext);
                        return new FixtureVerbExecutorBridge(executor);
                    },
                    Logger);

                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(ZNetOnNewConnectionPatch));
                QaServerRpcBridge.Arm(_component, Logger);
            }
            else
            {
                // M5-BIND: build the REAL client action/observation executor. This is the wire the
                // M4-BIND card left unconnected — without it every client verb (Craft/UpgradeItem/
                // DropItem/PickUpNearest/TamperField and the Read* family) admitted through the full
                // security gate and then returned NotImplementedInMilestone, which is why no
                // automated T022 leg could ever execute and every proof to date required a human at
                // the game console.
                //
                // Every game-touching call is routed through the EXISTING single-slot
                // ControlDispatcher (strict depth 0), on the helper's own main-thread pump, taking
                // no ScriptTools/ValBridge lock (ADR-0009 §5.2, AT-QA-NO-SCRIPTTOOLS-LOCK). The
                // adapters keep their own firewalls: tamper is degrade-only behind TamperPolicy,
                // craft/upgrade DRIVE the product's issuance seam and OBSERVE the result, and every
                // receipt is verdict-free (ADR-0009 §4/§6).
                var actionDispatcher = new ControlDispatcher(maxQueueDepth: 0);
                var invoker = new DispatcherMainThreadInvoker(
                    actionDispatcher, () => (long)(Time.realtimeSinceStartupAsDouble * 1000.0));
                var itemLedger = new RunItemLedger();

                ClientActionExecutorBridge? clientExecutor = null;
                clientExecutor = new ClientActionExecutorBridge(
                    new GameActionAdapter(invoker, new DeferredContextSource(() => clientExecutor!), itemLedger),
                    new GameObservationAdapter(invoker, new DeferredContextSource(() => clientExecutor!)),
                    // M5-SEED: the product admin relay. It grants nothing — it invokes the
                    // product's OWN per-peer admin RPC (the same one its console command uses,
                    // so no Terminal/ScriptTools lock is taken) and the product independently
                    // re-checks seam config + admin + bound principal + Bond/Attunement/AP
                    // server-side. The `sbpr_master` verb still has to be in the run's
                    // capability manifest to be reachable at all.
                    new ZNetProductAdminRelay());

                _component.Configure(
                    decision.State, boot.OperatorToken, boot.LoopbackPort, authority,
                    null, clientExecutor, Logger);
            }
        }

        // The gate consults its OWN baked-in production deny list; the allowlist is exactly the
        // one disposable world the manifest pins (UID+name), so a stray entry can't re-admit prod.
        private static WorldPolicy BuildPolicy(ArmManifest manifest)
            => new WorldPolicy(manifest.World != null ? new[] { manifest.World } : Array.Empty<WorldIdentity>());

        private void OnDestroy()
        {
            try { QaServerRpcBridge.Disarm(); } catch (Exception) { /* best effort */ }
            try { _harmony?.UnpatchSelf(); } catch (Exception) { /* best effort */ }
            try { _autoJoinHarmony?.UnpatchSelf(); } catch (Exception) { /* best effort */ }
            try { _readinessHarmony?.UnpatchSelf(); } catch (Exception) { /* best effort */ }
            try { FejdStartupAutoJoin.Reset(); } catch (Exception) { /* best effort */ }
            // Reset the spawned-player flag so a re-entered session starts clean (spec §4).
            try { PlayerOnSpawnedReadinessPatch.Reset(); } catch (Exception) { /* best effort */ }
            _harmony = null;
            _autoJoinHarmony = null;
            _readinessHarmony = null;
        }

        private void LogDisarmedBanner()
        {
            ManualLogSource log = Logger;
            log.LogWarning("╔══════════════════════════════════════════════════════════════╗");
            log.LogWarning("║  SBPR.QaHarness.T022 — DISARMED (default-disabled QA helper)   ║");
            log.LogWarning("║  ADR-0009 M2R: arms ONLY under full fail-closed gate + explicit║");
            log.LogWarning("║  bootstrap. No bootstrap => NO channel, NO hook, NO mutation.  ║");
            log.LogWarning("║  QA-ONLY — never shipped in the product modpack.             ║");
            log.LogWarning("╚══════════════════════════════════════════════════════════════╝");
        }
    }

    /// <summary>
    /// Defers the arm attempt until ZNet.World is loaded, re-checking each frame up to a bounded
    /// number of frames, then self-destructs. Keeps Awake side-effect-free (world isn't loaded yet).
    /// </summary>
    internal sealed class ArmDeferrer : MonoBehaviour
    {
        private Plugin? _plugin;
        private string _bootstrapPath = string.Empty;
        private int _framesLeft = 60 * 60 * 20; // ~20 min at 60fps upper bound; harmless if world never loads

        // Constructed once and held (not per-frame new): the two role-named arm-time readiness
        // sources. G1 proceeds to TryArm when EITHER reports ready (spec-role-split-arm-gate.md §4 G1):
        // a host/dedicated reaches TryArm via the server world-load source; a joining client reaches it
        // via the client source once Player.OnSpawned has fired — and never both arm-PATHs, per AC3
        // (the armed role is decided by the signed manifest at TryArm, not by which source tripped).
        private readonly IServerReadinessSource _serverReady = new ZNetWorldIdentitySource();
        private readonly IClientReadinessSource _clientReady = new ZNetClientReadinessSource();

        internal void Begin(Plugin plugin, string bootstrapPath)
        {
            _plugin = plugin;
            _bootstrapPath = bootstrapPath;
        }

        private void Update()
        {
            if (_plugin == null) { Destroy(this); return; }
            if (_framesLeft-- <= 0) { Destroy(this); return; }
            if (!_serverReady.Ready && !_clientReady.Ready) return; // keep waiting for either role's readiness
            _plugin.TryArm(_bootstrapPath);
            Destroy(this);
        }
    }
}
