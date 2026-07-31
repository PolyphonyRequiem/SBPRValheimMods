// ============================================================================
//  QA-M5-BIND — the client action/observation EXECUTOR bridge (ADR-0009 §3.1,
//  §3.2, §4, §6). The missing wire between M2R's control plane and M4-BIND's
//  adapters.
// ----------------------------------------------------------------------------
//  M4-BIND (#425) landed GameActionAdapter / GameObservationAdapter against the
//  real PR408 vanilla seams. M2R (#413) landed the fail-closed ControlPlaneRuntime.
//  Neither card connected them, so every client verb admitted through the whole
//  security gate and then returned NotImplementedInMilestone — the adapters were
//  compiled, unit-tested, and unreachable. This bridge is that connection, and it
//  is the exact client-side analogue of Fixtures/FixtureVerbExecutorBridge.cs on
//  the server side.
//
//  WHAT IT DOES NOT DO (the firewall, ADR-0009 §4/§6):
//   * It performs NO admission. It is invoked only after the shared runtime applied
//     M1 admission (nonce/role/world/catalog/manifest/typed args/expiry/seq/HMAC)
//     and took the single-slot dispatcher slot. Handles() is asked only about a verb
//     that ALREADY admitted, so this can never widen the verb surface.
//   * It emits NO verdict. It maps an adapter's RedactedReceipt to a mechanical
//     Ok/Rejected plus a bounded descriptive status token. PASS/FAIL composition
//     stays with the external runner.
//   * It claims NO product state. A Craft receipt says the harness DROVE the
//     product's issuance seam and OBSERVED a result — never that it minted a stamp.
//   * Tamper cannot add or copy a signature: the operation is fixed to Replace and
//     TamperOperation has no add member, so the degrade-only firewall is structural.
//
//  ARGUMENT COERCION (fail-closed): the verb catalog types `itemSlot` as a bounded
//  STRING (it is an opaque slot token on the wire) while IActionAdapter takes an
//  int index. Every coercion here is strict — a non-numeric / negative / oversized
//  slot refuses the primitive with NO game call rather than defaulting to slot 0.
//
//  MATURITY (truthful): this compiles against the live assembly and is exercised
//  headlessly through the engine-free seam. Whether a real joined client executes
//  these legs in-world is the separate operator-authorized M6 live run; nothing on
//  this card observes an in-world result.
using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Evidence;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Maps an admitted client verb to its M4-BIND adapter call and reports the observed
    /// result as a bounded status token. One instance per armed client run.
    /// </summary>
    internal sealed class ClientActionExecutorBridge : IClientActionVerbExecutor, IAdapterRequestContextSource
    {
        // Conservative slot bound: the catalog caps the slot token at 32 chars; a live
        // Valheim inventory is far smaller than this. A slot outside [0, SlotIndexMax]
        // refuses rather than clamping (fail-closed).
        private const int SlotIndexMax = 255;

        private readonly IActionAdapter _actions;
        private readonly IObservationAdapter _observations;

        // Optional product admin relay. Null = the sbpr_master verb refuses, so an operator who
        // has not explicitly wired the relay cannot accidentally drive the product's admin path.
        private readonly IProductAdminRelay? _adminRelay;

        // The ambient receipt identity for the in-flight primitive. The control plane sets this
        // immediately before each Execute call and the adapters read it back through
        // IAdapterRequestContextSource. Safe as simple mutable state: the caller is the
        // single-threaded main-thread pump and only one primitive is ever in flight.
        private AdapterRequestContext _current =
            new AdapterRequestContext("unknown", "Client", 0, string.Empty, 0, 0, 0);

        AdapterRequestContext IAdapterRequestContextSource.Current => _current;

        private static readonly HashSet<string> HandledVerbs = new(StringComparer.Ordinal)
        {
            "Craft", "UpgradeItem", "DropItem", "PickUpNearest", "TamperField",
            "ReadInventory", "ReadItem", "ReadTooltip", "ReadWorldName", "ReadWorldUid",
            "sbpr_master",
        };

        public ClientActionExecutorBridge(
            IActionAdapter actions, IObservationAdapter observations,
            IProductAdminRelay? adminRelay = null)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _observations = observations ?? throw new ArgumentNullException(nameof(observations));
            _adminRelay = adminRelay;
        }

        public bool Handles(string? verb) => verb != null && HandledVerbs.Contains(verb);

        public ClientVerbOutcome Execute(
            string verb, IReadOnlyDictionary<string, object?> args,
            string requestId, string nonce, long seq, long worldUid, long nowUnixMs)
        {
            if (verb == null) return ClientVerbOutcome.Refused("no-verb");
            args ??= EmptyArgs;

            // Make this request's identity ambient for the adapter's receipt. Connection
            // generation is 0 on the client channel (there is no per-peer generation there),
            // matching ControlReceipt's documented client-side value.
            _current = new AdapterRequestContext(
                requestId, "Client", worldUid, nonce, seq, 0, nowUnixMs);

            try
            {
                switch (verb)
                {
                    case "Craft":
                        return Map(verb, _actions.Craft(Str(args, "recipeName"), Str(args, "station")));

                    case "UpgradeItem":
                    {
                        if (!TrySlot(args, "itemSlot", out int slot))
                            return ClientVerbOutcome.Refused("bad-slot");
                        if (!TryInt(args, "targetQuality", out long quality))
                            return ClientVerbOutcome.Refused("bad-quality");
                        return Map(verb, _actions.UpgradeItem(slot, (int)quality));
                    }

                    case "DropItem":
                    {
                        if (!TrySlot(args, "itemSlot", out int slot))
                            return ClientVerbOutcome.Refused("bad-slot");
                        return Map(verb, _actions.DropItem(slot));
                    }

                    case "PickUpNearest":
                    {
                        if (!TryDouble(args, "radius", out double radius))
                            return ClientVerbOutcome.Refused("bad-radius");
                        return Map(verb, _actions.PickUpNearest(Str(args, "itemName"), radius));
                    }

                    case "TamperField":
                    {
                        if (!TrySlot(args, "itemSlot", out int slot))
                            return ClientVerbOutcome.Refused("bad-slot");
                        // Operation is fixed to Replace: the catalog carries no operation argument,
                        // and TamperOperation has no add member, so the degrade-only firewall holds
                        // structurally regardless (ADR-0009 §4, threat T5). The adapter re-applies
                        // TamperPolicy (allowlisted key on an exact tracked throwaway only).
                        return Map(verb, _actions.TamperField(slot, Str(args, "field"), TamperOperation.Replace));
                    }

                    case "ReadInventory":
                        return Map(verb, _observations.ReadInventory());

                    case "ReadItem":
                    {
                        if (!TrySlot(args, "itemSlot", out int slot))
                            return ClientVerbOutcome.Refused("bad-slot");
                        return Map(verb, _observations.ReadItem(slot));
                    }

                    case "ReadTooltip":
                    {
                        if (!TrySlot(args, "itemSlot", out int slot))
                            return ClientVerbOutcome.Refused("bad-slot");
                        return Map(verb, _observations.ReadTooltip(slot));
                    }

                    case "ReadWorldName":
                        return Map(verb, _observations.ReadWorldName());

                    case "ReadWorldUid":
                        return Map(verb, _observations.ReadWorldUid());

                    case "sbpr_master":
                    {
                        // Relay the PRODUCT's own admin verb. The harness grants nothing: the
                        // product re-checks its seam config, the transport-authenticated peer,
                        // admin membership, the bound principal, and the Bond/Attunement/AP
                        // preconditions server-side before acting (ADR-0009 §4).
                        if (_adminRelay == null)
                            return ClientVerbOutcome.Refused("no-admin-relay");
                        if (!TryInt(args, "discriminator", out long discriminator))
                            return ClientVerbOutcome.Refused("bad-discriminator");

                        var relay = _adminRelay.Invoke(discriminator);

                        // DELIVERED IS NOT APPLIED. The product handler is fire-and-forget, so a
                        // successful relay proves only that the invoke was sent. Whether
                        // entitlement actually moved is visible ONLY in the server log, and the
                        // runner must correlate there before treating the leg as satisfied.
                        return relay.WasDelivered
                            ? ClientVerbOutcome.Ran(relay.Status)
                            : ClientVerbOutcome.Refused(relay.Status);
                    }

                    default:
                        return ClientVerbOutcome.Refused("unhandled-verb");
                }
            }
            catch (Exception ex)
            {
                // Fail-closed: an adapter fault is a refusal with a bounded token, never an
                // exception escaping into the pump (which would kill the control plane) and
                // never a success claim. The exception TYPE only — no message, which could
                // carry a raw value the receipt firewall would otherwise redact.
                return ClientVerbOutcome.Refused("adapter-fault:" + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Map an adapter receipt to the mechanical outcome. Only ReceiptOutcome.Ok counts as
        /// executed; Busy/Timeout/Cancelled/Rejected are refusals with no success claim.
        /// </summary>
        private static ClientVerbOutcome Map(string verb, RedactedReceipt receipt)
        {
            if (receipt == null) return ClientVerbOutcome.Refused("no-receipt");
            string token = Token(verb, receipt);
            return receipt.Outcome == ReceiptOutcome.Ok
                ? ClientVerbOutcome.Ran(token)
                : ClientVerbOutcome.Refused(token);
        }

        /// <summary>
        /// A bounded, descriptive status token. Observed FACTS only — the receipt firewall
        /// already rejected any verdict-shaped key, and raw values were redacted to digests
        /// upstream, so this reports the mechanical outcome plus the verb.
        /// </summary>
        private static string Token(string verb, RedactedReceipt receipt)
        {
            string outcome = receipt.Outcome.ToString().ToLowerInvariant();
            return receipt.Outcome == ReceiptOutcome.Rejected
                ? string.Concat(verb, ":", outcome, ":", receipt.RejectReason.ToString())
                : string.Concat(verb, ":", outcome);
        }

        private static readonly Dictionary<string, object?> EmptyArgs = new(StringComparer.Ordinal);

        private static string Str(IReadOnlyDictionary<string, object?> args, string key)
            => args.TryGetValue(key, out var v) && v is string s ? s : string.Empty;

        /// <summary>Strict slot coercion: the wire carries an opaque bounded string token.</summary>
        private static bool TrySlot(IReadOnlyDictionary<string, object?> args, string key, out int slot)
        {
            slot = -1;
            if (!args.TryGetValue(key, out var v)) return false;
            if (v is string s)
            {
                if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                    return false;
                slot = parsed;
            }
            else if (v is long l) slot = (int)l;
            else if (v is int i) slot = i;
            else return false;

            if (slot < 0 || slot > SlotIndexMax) { slot = -1; return false; }
            return true;
        }

        private static bool TryInt(IReadOnlyDictionary<string, object?> args, string key, out long value)
        {
            value = 0;
            if (!args.TryGetValue(key, out var v)) return false;
            if (v is long l) { value = l; return true; }
            if (v is int i) { value = i; return true; }
            return false;
        }

        private static bool TryDouble(IReadOnlyDictionary<string, object?> args, string key, out double value)
        {
            value = 0;
            if (!args.TryGetValue(key, out var v)) return false;
            if (v is double d) { value = d; return true; }
            if (v is long l) { value = l; return true; }
            if (v is int i) { value = i; return true; }
            return false;
        }
    }
}
