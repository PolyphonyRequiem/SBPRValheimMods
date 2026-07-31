// The helper's OWN main-thread pump component (ADR-0009 §3.2, §5.2; PR #408 §3.2) — M2R.
//
// A dedicated MonoBehaviour the Plugin attaches AFTER the fail-closed arming gate passes.
// Its Update() is the single-threaded execution context for the control plane:
//   • Client role: drains the loopback TcpListener's inbound queue (LoopbackControlServer.
//     PumpOnce) and hands each request to the shared ControlPlaneRuntime; the socket accept
//     loop runs on a background thread and only ENQUEUES — no game API is ever touched off
//     the main thread, and this pump shares NO Terminal/ScriptTools/ValBridge lock.
//   • Server role: only advances the responder clock here; server requests arrive via the
//     per-peer ZRpc bridge (QaServerRpcBridge) which is itself invoked on the game's main
//     thread by ZRpc dispatch.
// On disable/destroy it unbinds the socket and logs a conspicuous DISARMED banner. It holds
// the operator token / hmac secret only in memory and never logs them.
using System;
using BepInEx.Logging;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>The armed control-plane pump. Created only under a full arming-gate pass.</summary>
    internal sealed class ControlPlaneComponent : MonoBehaviour
    {
        private ManualLogSource? _log;
        private ArmedState? _armed;
        private ControlPlaneRuntime? _runtime;           // client role
        private LoopbackControlServer? _loopback;         // client role
        private ServerRpcResponder? _serverResponder;     // server role

        /// <summary>The server-side responder, exposed so the ZRpc bridge can route inbound calls.</summary>
        internal ServerRpcResponder? ServerResponder => _serverResponder;

        /// <summary>True once Configure has armed this component.</summary>
        internal bool IsArmed { get; private set; }

        /// <summary>
        /// Arm the component for a run. For a Client role it starts the loopback listener on the
        /// bootstrap port; for a Server role it prepares the per-peer responder (no listener).
        /// Called exactly once, on the main thread, after ArmingGate accepted.
        /// </summary>
        internal void Configure(
            ArmedState armed, string operatorToken, int loopbackPort,
            IServerAuthorityRecheck serverAuthority, ManualLogSource log)
            => Configure(armed, operatorToken, loopbackPort, serverAuthority, null, log);

        /// <summary>
        /// Arm the component for a run. For a Client role it starts the loopback listener on the
        /// bootstrap port; for a Server role it prepares the per-peer responder (no listener),
        /// optionally with an M3R fixture executor that runs bounded vanilla fixtures behind the
        /// execution-time authority gate. <paramref name="fixtureExecutorFactory"/> receives the
        /// SHARED delivering-peer state the responder binds, so the fixture gate and the control
        /// gate validate against the same bound peer + connection generation. Called exactly once,
        /// on the main thread, after ArmingGate accepted.
        /// </summary>
        internal void Configure(
            ArmedState armed, string operatorToken, int loopbackPort,
            IServerAuthorityRecheck serverAuthority,
            Func<DeliveringPeerState, IServerFixtureVerbExecutor>? fixtureExecutorFactory,
            ManualLogSource log)
            => Configure(armed, operatorToken, loopbackPort, serverAuthority, fixtureExecutorFactory, null, log);

        /// <summary>
        /// Arm the component for a run, additionally supplying the CLIENT action/observation
        /// executor. <paramref name="clientActionExecutor"/> is the M5-BIND wire between the
        /// control plane and the M4-BIND adapters: without it an admitted client verb is merely
        /// acknowledged NotImplementedInMilestone (the historical M2R behaviour, which is why no
        /// automated leg could ever execute). Passing null preserves that behaviour exactly.
        /// </summary>
        internal void Configure(
            ArmedState armed, string operatorToken, int loopbackPort,
            IServerAuthorityRecheck serverAuthority,
            Func<DeliveringPeerState, IServerFixtureVerbExecutor>? fixtureExecutorFactory,
            IClientActionVerbExecutor? clientActionExecutor,
            ManualLogSource log)
        {
            _armed = armed ?? throw new ArgumentNullException(nameof(armed));
            _log = log;

            if (armed.Role == HarnessRole.Client)
            {
                _runtime = new ControlPlaneRuntime(armed, clientActionExecutor);
                _loopback = new LoopbackControlServer(operatorToken);
                _loopback.Start(loopbackPort);
                Banner("ARMED", clientActionExecutor != null
                    ? $"role=Client loopback=127.0.0.1:{_loopback.BoundPort} + M5 action/observation execution"
                    : $"role=Client loopback=127.0.0.1:{_loopback.BoundPort}");
            }
            else
            {
                // One delivering-peer state shared by the responder (it binds the peer on connect)
                // and the fixture executor's execution-time gate, so both see the same generation.
                var peerState = new DeliveringPeerState();
                IServerFixtureVerbExecutor? fixtureExecutor = fixtureExecutorFactory?.Invoke(peerState);
                _serverResponder = new ServerRpcResponder(armed, serverAuthority, fixtureExecutor, peerState);
                Banner("ARMED", fixtureExecutor != null
                    ? "role=Server per-peer-ZRpc (NO host listener) + M3R fixtures"
                    : "role=Server per-peer-ZRpc (NO host listener)");
            }
            IsArmed = true;
        }

        private void Update()
        {
            if (!IsArmed || _armed == null) return;
            long now = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
            if (_armed.Role == HarnessRole.Client && _runtime != null && _loopback != null)
            {
                _runtime.Tick(now);
                // Drain a bounded number of inbound requests per tick to keep the frame responsive.
                for (int i = 0; i < 4; i++)
                {
                    bool processed = _loopback.PumpOnce(payload =>
                        EnvelopeCodec.EncodeReceipt(_runtime.Handle(payload, now)));
                    if (!processed) break;
                }
            }
            else if (_armed.Role == HarnessRole.Server && _serverResponder != null)
            {
                _serverResponder.Tick(now);
            }
        }

        private void OnDestroy() => Teardown();
        private void OnDisable() => Teardown();

        private void Teardown()
        {
            if (!IsArmed) return;
            try { _loopback?.Stop(); } catch (Exception) { /* best effort */ }
            _loopback = null;
            IsArmed = false;
            Banner("DISARMED", "control plane torn down; socket unbound");
        }

        private void Banner(string state, string detail)
        {
            var log = _log;
            if (log == null) return;
            log.LogWarning("╔══════════════════════════════════════════════════════════════╗");
            log.LogWarning($"║  SBPR.QaHarness.T022 — {state,-9} (QA-only fail-closed helper)   ");
            log.LogWarning($"║  {detail}");
            log.LogWarning("║  QA-ONLY — never shipped in the product modpack.             ║");
            log.LogWarning("╚══════════════════════════════════════════════════════════════╝");
        }
    }
}
