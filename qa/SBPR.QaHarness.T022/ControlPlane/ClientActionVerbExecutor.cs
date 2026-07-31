// The CLIENT-role action/observation executor seam (ADR-0009 §3.1, §3.2, §4, §6).
//
// This is the exact client-side analogue of IServerFixtureVerbExecutor (ServerRpcResponder.cs):
// the engine-free contract by which an ADMITTED client verb is actually executed, instead of
// being acknowledged NotImplementedInMilestone and dropped.
//
// WHY THIS EXISTS: M4-BIND (#425) landed the live GameActionAdapter / GameObservationAdapter
// against the PR408 vanilla seams, and M2R landed the fail-closed ControlPlaneRuntime — but
// nothing connected them. Every client verb (Craft/UpgradeItem/DropItem/PickUpNearest/
// TamperField and all five Read*) admitted through the full security gate and then returned
// NotImplementedInMilestone, so the adapters were compiled, unit-tested, and unreachable. That
// is why every T022 leg proven to date required a human typing at the game console.
//
// FAIL-CLOSED POSITION IN THE CHAIN: this seam is reached ONLY after the shared runtime has
// already applied M1 admission (nonce / role / world / catalog / manifest / typed args / expiry
// / sequence / HMAC) and taken the single-slot dispatcher slot. It performs NO admission of its
// own and CANNOT widen the verb surface: a null executor reproduces the exact prior M2R
// behaviour, and Handles() is consulted only for a verb that already admitted.
//
// FIREWALL (ADR-0009 §4/§6): the executor returns descriptive primitive facts only. It has no
// PASS/FAIL representation — the outcome type below carries a mechanical Ok/Rejected plus a
// bounded status token, exactly as FixtureVerbOutcome does on the server side. Verdict
// composition remains the external runner's sole authority.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>
    /// The mechanical result of executing one admitted client action/observation verb.
    /// Mirrors <see cref="FixtureVerbOutcome"/>. Carries no verdict — only whether the
    /// primitive ran and a bounded descriptive status token for the receipt.
    /// </summary>
    public sealed class ClientVerbOutcome
    {
        /// <summary>True iff the primitive actually ran against the game; false iff a gate refused it.</summary>
        public bool Executed { get; }

        /// <summary>A bounded, descriptive status token for the receipt (e.g. "craft-observed:quality=1").</summary>
        public string Status { get; }

        public ClientVerbOutcome(bool executed, string? status)
        {
            Executed = executed;
            Status = status ?? string.Empty;
        }

        /// <summary>The primitive ran; <paramref name="status"/> describes the observed result.</summary>
        public static ClientVerbOutcome Ran(string status) => new(true, status);

        /// <summary>The primitive did not run (busy / timeout / adapter gate refused). No game side effect.</summary>
        public static ClientVerbOutcome Refused(string status) => new(false, status);
    }

    /// <summary>
    /// The result of relaying the product's own admin verb.
    /// </summary>
    /// <remarks>
    /// CRITICAL SEMANTICS: <see cref="Delivered"/> means the invoke reached the product's RPC —
    /// NOT that the product authorized or applied anything. The product's handler is
    /// fire-and-forget (void), so the outcome is observable ONLY in the server log. A caller
    /// that treats a delivered relay as proof of entitlement is manufacturing a green.
    /// </remarks>
    public sealed class AdminRelayResult
    {
        /// <summary>True iff the invoke was delivered to the product. NOT an authorization result.</summary>
        public bool WasDelivered { get; }

        /// <summary>A bounded descriptive token for the receipt.</summary>
        public string Status { get; }

        private AdminRelayResult(bool delivered, string status)
        {
            WasDelivered = delivered;
            Status = status ?? string.Empty;
        }

        public static AdminRelayResult Delivered(string status) => new(true, status);
        public static AdminRelayResult Refused(string status) => new(false, status);
    }

    /// <summary>
    /// Relays the product's OWN Masterwork ownership admin command (ADR-0009 §4).
    ///
    /// The harness NEVER grants entitlement: it asks the product to run its own admin path, and
    /// the product independently re-checks its seam config, the transport-authenticated peer,
    /// admin membership, the bound principal, and the Bond/Attunement/AP preconditions before
    /// doing anything. This seam holds no key and can construct no product state.
    /// </summary>
    public interface IProductAdminRelay
    {
        /// <summary>
        /// Invoke the product admin path with the bounded discriminator
        /// (<see cref="VerbCatalog.AdminOffer"/> / <see cref="VerbCatalog.AdminBuy"/>).
        /// </summary>
        AdminRelayResult Invoke(long discriminator);
    }

    /// <summary>
    /// Executes an admitted CLIENT verb (Craft / UpgradeItem / DropItem / PickUpNearest /
    /// TamperField / ReadInventory / ReadItem / ReadTooltip / ReadWorldName / ReadWorldUid)
    /// through the real vanilla seams behind the single-slot main-thread invoker.
    ///
    /// Engine-free interface: the engine-bound implementation (ClientActionExecutorBridge in
    /// Runtime/) composes the M4-BIND GameActionAdapter / GameObservationAdapter. A runtime with
    /// no executor injected reports these verbs as not-implemented, exactly as M2R did.
    /// </summary>
    public interface IClientActionVerbExecutor
    {
        /// <summary>True iff <paramref name="verb"/> is a client verb this executor handles.</summary>
        bool Handles(string? verb);

        /// <summary>
        /// Run the admitted client verb. The request already passed M1 admission and single-slot
        /// dispatch, so this only maps the verb to its adapter call and reports the observed
        /// result. The implementation routes every game-touching call through the main-thread
        /// invoker (taking no ScriptTools/ValBridge lock, ADR-0009 §5.2) and never claims the
        /// harness produced product state.
        ///
        /// The receipt-identity primitives (<paramref name="requestId"/>, <paramref name="nonce"/>,
        /// <paramref name="seq"/>, <paramref name="worldUid"/>) are passed explicitly because the
        /// adapter method signatures carry no envelope; the implementation makes them the ambient
        /// context for the in-flight primitive so each emitted receipt is correlatable by the
        /// runner. They are plain primitives, so this interface stays engine-free.
        /// </summary>
        ClientVerbOutcome Execute(
            string verb, IReadOnlyDictionary<string, object?> args,
            string requestId, string nonce, long seq, long worldUid, long nowUnixMs);
    }
}
