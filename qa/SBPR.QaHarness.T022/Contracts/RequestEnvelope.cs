// The request envelope (ADR-0009 §3.2) — the authenticated wrapper every control
// request carries. Engine-free value object; the helper's dispatcher (a later card)
// receives one of these off the wire, the RequestAdmission gate here validates it.
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// One control request: the authenticated envelope plus the typed verb args.
    /// <c>{nonce, seq, expiry, HMAC, role, worldUid, verb, requestId, connectionGeneration}</c>
    /// (ADR-0009 §3.2, §5.1). The server binds the ACTUAL delivering peer at the channel
    /// layer and validates <see cref="ConnectionGeneration"/> against the current bound
    /// connection — a request carrying a stale (pre-reconnect) generation is rejected even
    /// if its HMAC verifies. The generation is part of the authenticated HMAC input, so it
    /// cannot be tampered without invalidating the signature.
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

        /// <summary>
        /// The connection generation this request claims (ADR-0009 §5.1). A monotonically
        /// increasing counter the server bumps every time a peer (re)binds. Required and
        /// strictly positive; the server rejects a request whose claimed generation is not
        /// the current bound one (StaleGeneration). Included in the canonical HMAC input.
        /// </summary>
        public long ConnectionGeneration { get; }

        /// <summary>Named typed argument values for the verb (name -> value).</summary>
        public IReadOnlyDictionary<string, object?> Args { get; }

        public RequestEnvelope(
            string? nonce, long seq, long expiryUnixMs, string? hmac,
            string? role, long worldUid, string? verb, string? requestId,
            long connectionGeneration,
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
            ConnectionGeneration = connectionGeneration;
            Args = args ?? new Dictionary<string, object?>();
        }
    }
}
