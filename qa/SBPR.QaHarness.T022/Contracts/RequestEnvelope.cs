// The request envelope (ADR-0009 §3.2) — the authenticated wrapper every control
// request carries. Engine-free value object; the helper's dispatcher (a later card)
// receives one of these off the wire, the RequestAdmission gate here validates it.
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// One control request: the authenticated envelope plus the typed verb args.
    /// <c>{nonce, seq, expiry, HMAC, role, worldUid, capabilityVerb, requestId}</c>
    /// (ADR-0009 §3.2). The server additionally binds the actual delivering peer at a
    /// later layer; that binding is out of M1 scope (no channel exists yet).
    /// </summary>
    public sealed class RequestEnvelope
    {
        public string? Nonce { get; }
        public long Seq { get; }
        public long ExpiryUnixMs { get; }
        public string? Hmac { get; }
        public string? Role { get; }
        public long WorldUid { get; }
        public string? Verb { get; }
        public string? RequestId { get; }

        /// <summary>Named typed argument values for the verb (name -> value).</summary>
        public IReadOnlyDictionary<string, object?> Args { get; }

        public RequestEnvelope(
            string? nonce, long seq, long expiryUnixMs, string? hmac,
            string? role, long worldUid, string? verb, string? requestId,
            IReadOnlyDictionary<string, object?>? args)
        {
            Nonce = nonce;
            Seq = seq;
            ExpiryUnixMs = expiryUnixMs;
            Hmac = hmac;
            Role = role;
            WorldUid = worldUid;
            Verb = verb;
            RequestId = requestId;
            Args = args ?? new Dictionary<string, object?>();
        }
    }
}
