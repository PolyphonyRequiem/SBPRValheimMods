using BepInEx;
using BepInEx.Logging;

namespace SBPR.QaHarness.T022
{
    /// <summary>
    /// SBPR.QaHarness.T022 — QA-only, fail-closed BepInEx test-helper (ADR-0009).
    ///
    /// <para>
    /// <b>M1 UPDATE — contracts compiled in, still INERT at runtime.</b> This plugin
    /// now compiles the engine-free QA contract core (<c>Contracts/*.cs</c>: the verb
    /// catalog, capability-manifest parser, and the fail-closed arming + request-
    /// admission decision). Those are pure decision logic exercised by the
    /// <c>qa/tests-core</c> xUnit suite; <b>nothing here invokes them at runtime yet</b>.
    /// The plugin's entire runtime behavior remains: log a conspicuous DISARMED banner
    /// in <see cref="Awake"/>, then return. There is still no channel to receive an arm
    /// manifest (the loopback TCP / per-peer ZRpc dispatcher lands in M2), so the gate
    /// cannot be driven and the helper cannot arm. It registers <b>no</b> command verb,
    /// Harmony hook, socket, ZRpc, file write, timer, and performs no game mutation.
    /// </para>
    ///
    /// <para>
    /// The real subsystem is delivered across later, separately-reviewed cards:
    /// M1 engine-free contracts + fail-closed arming gate; M2 loopback TCP/JSON
    /// client channel + no-host-listener per-peer ZRpc + single-slot dispatcher;
    /// M3 vanilla-only fixture verbs + owned-resource ledger + cleanup; M4
    /// actions/observation/tamper + receipt hash chain + adversarial hardening;
    /// M5 the external Python runner + deterministic QA-overlay packaging. Until
    /// those land and are reviewed, this assembly can do nothing but announce that
    /// it is disarmed.
    /// </para>
    ///
    /// <para>
    /// <b>Trust boundary (ADR-0009 §4, never crossed):</b> even when armed in a
    /// future milestone, the harness may only synthesize ordinary allowlisted
    /// <i>vanilla</i> prerequisites in a disposable world. It may never mint,
    /// sign, or grant product identity/entitlement/AP/BP/ownership/signatures/
    /// snapshots/journals/caches, and it may never emit a product AT verdict —
    /// only the external runner declares PASS/FAIL.
    /// </para>
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        /// <summary>
        /// Fixed BepInPlugin GUID for the QA helper (ADR-0009 §1). Deliberately
        /// in the <c>net.danielgreen.sbpr.qa.*</c> namespace — distinct from every
        /// product GUID family (<c>net.danielgreen.sbpr.trailborne</c>,
        /// <c>net.danielgreen.sbpr.niflheim.homesteadstones</c>) — so it can never
        /// be mistaken for or collide with a shipped product plugin.
        /// </summary>
        public const string PluginGuid = "net.danielgreen.sbpr.qa.harness.t022";

        /// <summary>Human-readable plugin name for the BepInEx log.</summary>
        public const string PluginName = "SBPR QA Harness T022";

        /// <summary>
        /// M0 skeleton version. This helper is versioned/packaged only through the
        /// separate QA overlay (ADR-0009 §7), never the product modpack.
        /// </summary>
        public const string PluginVersion = "0.0.0";

        /// <summary>
        /// BepInEx lifecycle entry point. INERT: emits the disarmed banner and
        /// returns. No arming, no verb registration, no hook, no I/O. Do not add
        /// any behavior here in M0 — new capability belongs in a later milestone's
        /// reviewed card, behind the fail-closed arming gate (M1).
        /// </summary>
        private void Awake()
        {
            ManualLogSource log = Logger;
            log.LogWarning("╔══════════════════════════════════════════════════════════════╗");
            log.LogWarning("║  SBPR.QaHarness.T022 — DISARMED (default-disabled QA helper)   ║");
            log.LogWarning("║  ADR-0009 M0 inert skeleton: NO verbs, NO hooks, NO channel,   ║");
            log.LogWarning("║  NO fixtures, NO actions, NO mutation. It cannot arm.         ║");
            log.LogWarning("║  QA-ONLY — never shipped in the product modpack.             ║");
            log.LogWarning("╚══════════════════════════════════════════════════════════════╝");
            // Intentionally nothing else. The helper does not arm in M0.
        }
    }
}
