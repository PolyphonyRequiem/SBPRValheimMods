using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Features.Progression;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    /// <summary>
    /// IAP-015 — the net48-ONLY LIVE operator command ingress. THIS is what closes the shipped-surface gap
    /// EXECUTE run 1426 exposed: the operator lifecycle cores (OperatorAccountService, PilotPrivacyService,
    /// PilotDestructionService, OperatorAdminGate) had NO net48 console/direct-peer RPC ingress, so a joined
    /// admin could not actually run open/inspect/export/disable/delete/purge on the live server.
    ///
    /// Trust model (task points 1–2; PR #338/#408 direct-RPC pattern): a joined client's console command
    /// sends a bounded request over a DIRECT per-peer <c>ZRpc</c> handler registered on that peer's OWN
    /// <c>m_rpc</c>. The server receives the REAL delivering <c>ZRpc</c>, resolves the exact authenticated
    /// <c>ZNetPeer</c> (vanilla's own <c>ZNet.GetPeer(rpc)</c> seam, reproduced over the public peer table by
    /// <see cref="ZdoAuthenticatedSenderSource.PeerForRpc"/>), and derives a
    /// <see cref="ServerObservedAdminContext"/> from THAT peer's authenticated socket host id. Authorization
    /// runs through the real server-owned <c>adminlist.txt</c> semantics (<see cref="OperatorAdminGate"/> over
    /// a live <c>ZNet.GetAdminList()</c> provider). The client payload NEVER carries authority, and a
    /// <c>ZRoutedRpc</c> sender id is never trusted — a forged/routed sender resolves to no delivering peer.
    ///
    /// Shared universe (task point 3): the ingress runs against
    /// <see cref="PilotSessionLifecycleObserver.OperatorServices"/>, the SAME shared
    /// <c>PilotAccountStore</c> / <c>PilotSessionRegistry</c> / <c>AccountMutationFence</c> / privacy +
    /// lifecycle services the live admission observer drives — so a join-created account is immediately
    /// inspectable, an operator disable actually drops the live admission session, and a restart rehydrates
    /// the same durable store.
    ///
    /// Real server-side close (task point 4): on a disable/delete that closed a session, the ingress performs
    /// the ACTUAL transport close via <c>ZNet.Disconnect(ZNetPeer)</c> on the delivering-peer table entry for
    /// the closed transport handle, so the disabled/deleted player is genuinely dropped — not merely marked.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, Terminal) → net48-only, not link-compiled into net8. The
    /// engine-free router/services it drives are fully unit-tested.
    /// </summary>
    [HarmonyPatch]
    internal static class OperatorCommandIngressObserver
    {
        // Direct per-peer ZRpc method names (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcRequest = "SBPR_Niflheim_PilotOpRequest";
        internal const string RpcReply = "SBPR_Niflheim_PilotOpReply";

        // Client console command name (the documented pilot-op vocabulary, SBPR-prefixed for console safety).
        internal const string ConsoleCommand = "sbpr_pilotop";

        // ── SERVER SIDE: register a DIRECT per-peer handler on every new connection ──────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcRequest, RPC_OperatorRequest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Operator command handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>The SERVER operator request handler. <paramref name="rpc"/> is the ACTUAL transport
        /// connection that delivered the packet — the server resolves the exact authenticated peer from it
        /// (never the payload) and derives the operator admin context from that peer's socket host id. The
        /// engine-free router does all authority/replay/scrub decisions; the reply is a bounded, subject-free
        /// wire string sent back on the same peer rpc.</summary>
        private static void RPC_OperatorRequest(ZRpc rpc, string wire)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;

                var services = PilotSessionLifecycleObserver.OperatorServices;
                if (services == null)
                {
                    // Not composed (should not happen on a live server): fail closed with a bounded reply.
                    SafeReply(rpc, OperatorWireResponse.Reject(RecoverCorrelation(wire), "unknown", "NotComposed").ToWire());
                    return;
                }

                // The AUTHENTICATED peer is the one whose m_rpc delivered this packet — the transport-bound
                // match, with NO client-supplied id. A forged/routed sender resolves to no peer → reject.
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                var op = DeriveAdminContext(peer);

                // The peer closer disconnects the delivering-peer-table entry for a closed transport handle.
                var closer = new ZNetPeerCloser(znet);
                var router = new LiveOperatorCommandRouter(services, closer);

                var response = router.Handle(op, wire, UnixNow());
                SafeReply(rpc, response.ToWire());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Operator command handler threw: " + ex);
                try { SafeReply(rpc, OperatorWireResponse.Reject(RecoverCorrelation(wire), "unknown", "InternalError").ToWire()); }
                catch { /* best effort */ }
            }
        }

        /// <summary>Derive the server-observed operator admin context from the delivering peer's authenticated
        /// socket host id. A null peer or missing socket host yields <see cref="ServerObservedAdminContext.None"/>
        /// (unauthenticated → the gate rejects). Never a client claim.</summary>
        private static ServerObservedAdminContext DeriveAdminContext(ZNetPeer? peer)
        {
            if (peer == null) return ServerObservedAdminContext.None;
            var socket = peer.m_socket;
            string? host = socket != null ? socket.GetHostName() : null;
            if (string.IsNullOrEmpty(host)) return ServerObservedAdminContext.None;
            return new ServerObservedAdminContext(host!, VanillaAdminIdentity.DefaultPlatform);
        }

        private static void SafeReply(ZRpc rpc, string wire)
        {
            try { rpc?.Invoke(RpcReply, wire); } catch { /* peer may have dropped; best effort */ }
        }

        private static string RecoverCorrelation(string? wire)
        {
            OperatorWireRequest.TryParse(wire, out _, out var corr);
            return corr;
        }

        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ── CLIENT SIDE: the console command that sends a bounded operator request ────────────────────

        /// <summary>Send an operator request on the SERVER connection as a direct per-peer invoke (NOT a
        /// routed RPC), so it lands on the server's transport-bound handler and the server authenticates the
        /// caller by the delivering ZRpc. The client cannot self-authorize.</summary>
        internal static void SendRequest(string wire, Action<string> print)
        {
            try
            {
                var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                if (serverRpc == null)
                {
                    print("sbpr_pilotop: not connected to a server.");
                    return;
                }
                serverRpc.Invoke(RpcRequest, wire);
                print("sbpr_pilotop: request sent (server admin required to take effect).");
            }
            catch (Exception ex)
            {
                print("sbpr_pilotop: send failed: " + ex.Message);
            }
        }
    }

    /// <summary>net48 implementation of <see cref="IServerPeerCloser"/>: closes the REAL delivering-peer-table
    /// entry whose transport uid matches a closed session's handle, via vanilla <c>ZNet.Disconnect(ZNetPeer)</c>
    /// (verified present in assembly_valheim 0.221.12). A stale/absent handle is a clean no-op.</summary>
    internal sealed class ZNetPeerCloser : IServerPeerCloser
    {
        private readonly ZNet _znet;
        public ZNetPeerCloser(ZNet znet) { _znet = znet; }

        public bool CloseTransport(long transportHandle)
        {
            try
            {
                if (_znet == null || !_znet.IsServer()) return false;
                var peers = _znet.GetConnectedPeers();
                if (peers == null) return false;
                foreach (var peer in peers)
                {
                    if (peer != null && peer.m_uid == transportHandle)
                    {
                        _znet.Disconnect(peer);   // real server-side socket close of the disabled/deleted player
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// IAP-015 — the CLIENT console command that invokes an operator command. A joined admin runs
    /// <c>sbpr_pilotop &lt;verb&gt; [selector]</c>; it builds a bounded wire request and sends it on the server
    /// connection as a direct notice, landing on the server's transport-bound handler. Registered once when
    /// Terminal's command table initializes. The command is inert unless the caller is a server admin — the
    /// client cannot self-authorize (the server re-derives authority from the delivering peer).
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class OperatorCommandConsole
    {
        private static bool registered;
        private static long correlationSeed;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(OperatorCommandIngressObserver.ConsoleCommand,
                    "SBPR: (admin-only) live pilot operator command — usage: sbpr_pilotop <open-pilot|inspect|export|disable|delete|purge|retention-purge|close-pilot> [selector]",
                    args =>
                    {
                        var ctx = args.Context;
                        Action<string> print = s => ctx?.AddString(s);
                        if (args.Length < 2)
                        {
                            print("sbpr_pilotop: usage: sbpr_pilotop <verb> [selector]");
                            return;
                        }
                        string verb = args[1];
                        string selector = args.Length >= 3 ? args[2] : string.Empty;

                        // Correlation + operation ids are client-minted opaque tokens; they carry NO authority.
                        long seq = System.Threading.Interlocked.Increment(ref correlationSeed);
                        string corr = "c" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string opId = "op-" + verb + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            .ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + seq;

                        // Build the bounded wire request: v1|corr|op|verb[|selector].
                        var parts = new List<string> { LiveOperatorCommandRouter.WireVersion, corr, opId, verb };
                        if (!string.IsNullOrEmpty(selector)) parts.Add(selector);
                        string wire = string.Join("|", parts);

                        if (wire.Length > LiveOperatorCommandRouter.MaxWireLength)
                        {
                            print("sbpr_pilotop: request too long.");
                            return;
                        }
                        OperatorCommandIngressObserver.SendRequest(wire, print);
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Operator console command registration failed (ignored): " + ex.Message);
            }
        }
    }

    /// <summary>
    /// IAP-015 — the CLIENT-side reply handler: registered on the client's own server-peer rpc so the
    /// server's bounded operator response is printed to the invoking admin's console. Registered once at
    /// Terminal init; server replies are subject-free wire strings.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class OperatorCommandReplyClient
    {
        [HarmonyPostfix]
        private static void Postfix(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                // Only a CLIENT registers the reply handler (it receives the server's reply on the server peer).
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(OperatorCommandIngressObserver.RpcReply, RPC_OperatorReply);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Operator reply handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_OperatorReply(ZRpc rpc, string wire)
        {
            try
            {
                // Print the bounded, subject-free reply to the local console. The client does not interpret
                // authority; it only renders what the server returned.
                var term = Console.instance != null ? (Terminal)Console.instance : null;
                term?.AddString("[sbpr_pilotop] " + (wire ?? string.Empty));
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] pilot-op reply: " + (wire ?? string.Empty));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Operator reply render failed (ignored): " + ex.Message);
            }
        }
    }

    /// <summary>
    /// IAP-015 — startup conformance diagnostic for the live operator command surface.
    ///
    /// The three operator patch classes (<see cref="OperatorCommandIngressObserver"/> — SERVER request
    /// handler; <see cref="OperatorCommandConsole"/> — CLIENT console command; <see cref="OperatorCommandReplyClient"/>
    /// — CLIENT reply handler) compile and ship even when <c>Plugin.Awake()</c> forgets to hand them to
    /// <c>harmony.PatchAll(typeof(X))</c>. When that happens the whole surface is dead code with NO build
    /// error and NO boot signal — exactly the runtime-proven defect from live smoke t_48797ca3 at 04efd544.
    ///
    /// This runs at the END of <c>Plugin.Awake()</c>, after the operator <c>PatchAll</c> calls. It walks
    /// Harmony's global registry (same technique as SBPR.Trailborne's PatchCheck) and confirms each of the
    /// three classes produced at least one WOVEN patch method owned by this mod. It emits a per-role line so
    /// the next live smoke can distinguish console-registered / server-request-bound / client-reply-bound,
    /// and ERROR-logs (fail-closed signal) naming any missing role. It does NOT prove playability — a joined
    /// admin still has to actually run a verb — it only proves the wiring is armed.
    /// </summary>
    internal static class OperatorSurfaceConformance
    {
        public static void Verify(string ownerId)
        {
            try
            {
                HashSet<Type> woven = CollectWovenPatchClasses(ownerId);

                bool serverRequest = woven.Contains(typeof(OperatorCommandIngressObserver));
                bool console       = woven.Contains(typeof(OperatorCommandConsole));
                bool clientReply   = woven.Contains(typeof(OperatorCommandReplyClient));

                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Operator surface conformance: "
                    + "console=" + (console ? "REGISTERED" : "MISSING") + ", "
                    + "server-request-handler=" + (serverRequest ? "BOUND" : "MISSING") + ", "
                    + "client-reply-handler=" + (clientReply ? "BOUND" : "MISSING") + ".");

                var missing = new List<string>();
                if (!console) missing.Add(nameof(OperatorCommandConsole) + " (sbpr_pilotop console command)");
                if (!serverRequest) missing.Add(nameof(OperatorCommandIngressObserver) + " (server request handler)");
                if (!clientReply) missing.Add(nameof(OperatorCommandReplyClient) + " (client reply handler)");

                if (missing.Count > 0)
                    Plugin.Log.LogError(
                        "[Niflheim/HomesteadStones] ✗ OPERATOR SURFACE DEAD — unregistered patch class(es): "
                        + string.Join(", ", missing)
                        + ". Did Plugin.Awake() forget harmony.PatchAll(typeof(...))? "
                        + "The live operator command surface (sbpr_pilotop) is NON-FUNCTIONAL until fixed.");
                else
                    Plugin.Log.LogInfo(
                        "[Niflheim/HomesteadStones] ✓ Operator surface armed (console + server request + client reply all woven).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Operator surface conformance check threw: " + ex);
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
