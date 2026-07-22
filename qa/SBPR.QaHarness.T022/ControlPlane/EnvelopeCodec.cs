// Envelope codec + control receipt (ADR-0009 §3.2, §6) — M2R runtime wiring.
//
// Bridges the wire (JSON frame payload) to the engine-free contract core: parse an
// inbound control frame into a RequestEnvelope for RequestAdmission, and serialize a
// primitive receipt back out. Engine-free (System.* only) — link-compiled into the
// helper and the xUnit suite from one source.
//
// Receipts are DESCRIPTIVE primitive facts only (ADR-0009 §6): they never carry a
// product PASS/FAIL. In M2R the only executable verbs are status/ping/reject — no
// fixture, no action, no observation of product state — so a receipt reports the
// admission/transport outcome and a bounded status payload, nothing more.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>Outcome classes a receipt can carry — purely transport/admission level in M2R.</summary>
    public enum ControlOutcome
    {
        /// <summary>The request was admitted and the (M2R status/ping) primitive completed.</summary>
        Ok = 0,
        /// <summary>The request was refused at admission (see the RejectReason string).</summary>
        Rejected = 1,
        /// <summary>The request was refused at the transport layer (frame/bind/peer/dispatcher).</summary>
        TransportRejected = 2,
        /// <summary>The verb parsed and admitted but is not executable in M2R (no fixtures/actions yet).</summary>
        NotImplementedInMilestone = 3,
    }

    /// <summary>The result of decoding a wire frame into an envelope.</summary>
    public sealed class EnvelopeDecode
    {
        public RequestEnvelope? Envelope { get; }
        public bool Ok => Envelope != null;

        private EnvelopeDecode(RequestEnvelope? env) { Envelope = env; }

        public static EnvelopeDecode Fail() => new(null);
        public static EnvelopeDecode Success(RequestEnvelope env) => new(env);
    }

    /// <summary>Parses the bounded control-envelope JSON shape into a RequestEnvelope; serializes receipts.</summary>
    public static class EnvelopeCodec
    {
        /// <summary>
        /// Decode a frame payload (JSON) into a RequestEnvelope. The shape is a flat object:
        /// {nonce, seq, expiry, hmac, role, worldUid, verb, requestId, connectionGeneration,
        /// args:{...}} where args is a single nested flat object of scalar values. Missing/
        /// mis-typed required scalars fail; a missing or non-positive connectionGeneration is
        /// a decode failure (fail-closed — the server never admits a generation-less request).
        /// The presented operator token is carried OUT-of-band (bind policy), not in the envelope.
        /// </summary>
        public static EnvelopeDecode Decode(string? payload)
        {
            if (!MiniJson.TryParse(payload, out var obj)) return EnvelopeDecode.Fail();

            obj.TryGetString("nonce", out var nonce);
            obj.TryGetString("hmac", out var hmac);
            obj.TryGetString("role", out var role);
            obj.TryGetString("verb", out var verb);
            obj.TryGetString("requestId", out var requestId);
            if (!obj.TryGetLong("seq", out var seq)) return EnvelopeDecode.Fail();
            if (!obj.TryGetLong("expiry", out var expiry)) return EnvelopeDecode.Fail();
            if (!obj.TryGetLong("worldUid", out var worldUid)) return EnvelopeDecode.Fail();
            // Connection generation is REQUIRED and strictly positive on the wire (ADR-0009 §5.1):
            // a missing or non-positive value is a malformed frame, never a default-0 that would
            // silently sidestep the stale-generation defense.
            if (!obj.TryGetLong("connectionGeneration", out var connectionGeneration) ||
                connectionGeneration <= 0)
                return EnvelopeDecode.Fail();

            var args = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (obj.TryGetObject("args", out var argsObj))
            {
                foreach (var kv in argsObj.Scalars)
                    args[kv.Key] = kv.Value.AsArgValue();
            }

            var env = new RequestEnvelope(
                nonce, seq, expiry, hmac, role, worldUid, verb, requestId, connectionGeneration, args);
            return EnvelopeDecode.Success(env);
        }

        /// <summary>Serialize a primitive receipt as a compact JSON object (descriptive facts only, no verdict).</summary>
        public static string EncodeReceipt(ControlReceipt r)
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"requestId\":\"").Append(MiniJson.EscapeString(r.RequestId)).Append("\",");
            sb.Append("\"verb\":\"").Append(MiniJson.EscapeString(r.Verb)).Append("\",");
            sb.Append("\"outcome\":\"").Append(r.Outcome.ToString()).Append("\",");
            sb.Append("\"reason\":\"").Append(MiniJson.EscapeString(r.Reason)).Append("\",");
            sb.Append("\"role\":\"").Append(MiniJson.EscapeString(r.Role)).Append("\",");
            sb.Append("\"worldUid\":").Append(r.WorldUid.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"seq\":").Append(r.Seq.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"ts\":").Append(r.TsUnixMs.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"connectionGeneration\":").Append(r.Generation.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"status\":\"").Append(MiniJson.EscapeString(r.Status)).Append("\"");
            sb.Append('}');
            return sb.ToString();
        }
    }

    /// <summary>
    /// A primitive control receipt (ADR-0009 §6): descriptive facts only, never a product verdict.
    /// In M2R it reports the admission/transport outcome and a bounded status string.
    /// </summary>
    public sealed class ControlReceipt
    {
        public string RequestId { get; }
        public string Verb { get; }
        public ControlOutcome Outcome { get; }
        public string Reason { get; }
        public string Role { get; }
        public long WorldUid { get; }
        public long Seq { get; }
        public long TsUnixMs { get; }
        public string Status { get; }

        /// <summary>
        /// The server's CURRENT bound connection generation at receipt time (ADR-0009 §5.1).
        /// 0 on the client channel (no per-peer generation there). The authorized runner reads
        /// this from each receipt to form its next request's connectionGeneration, and a
        /// reconnect advances it so pre-reconnect envelopes reject as StaleGeneration.
        /// </summary>
        public long Generation { get; }

        public ControlReceipt(
            string requestId, string verb, ControlOutcome outcome, string reason,
            string role, long worldUid, long seq, long tsUnixMs, string status,
            long generation = 0)
        {
            RequestId = requestId ?? string.Empty;
            Verb = verb ?? string.Empty;
            Outcome = outcome;
            Reason = reason ?? string.Empty;
            Role = role ?? string.Empty;
            WorldUid = worldUid;
            Seq = seq;
            TsUnixMs = tsUnixMs;
            Status = status ?? string.Empty;
            Generation = generation;
        }

        /// <summary>Return a copy of this receipt stamped with the given current generation.</summary>
        public ControlReceipt WithGeneration(long generation) =>
            new(RequestId, Verb, Outcome, Reason, Role, WorldUid, Seq, TsUnixMs, Status, generation);
    }
}
