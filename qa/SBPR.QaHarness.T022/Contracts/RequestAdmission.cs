// Per-request admission against an ArmedState (ADR-0009 §3.2, §5.1). This is the
// second fail-closed gate: after a successful arm, every inbound request envelope is
// validated here before it could ever reach an executor (executors land in later
// cards). AND-composed, fixed-order, deterministic RejectReason. Stateful for
// sequence/idempotency: it remembers (requestId -> seq/receipt) so a replay returns
// the cached decision instead of re-executing, and a sequence regression / conflicting
// re-use of a live requestId is rejected.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>Outcome of admitting one request. On replay, <see cref="IsReplay"/> is true and the original reason is echoed.</summary>
    public sealed class AdmitDecision
    {
        public bool Admitted => Reason == RejectReason.None && !IsReplay;
        public RejectReason Reason { get; }
        public bool IsReplay { get; }

        /// <summary>The resolved catalog verb when admitted; null otherwise.</summary>
        public CapabilityVerb? Verb { get; }

        private AdmitDecision(RejectReason reason, bool isReplay, CapabilityVerb? verb)
        {
            Reason = reason;
            IsReplay = isReplay;
            Verb = verb;
        }

        public static AdmitDecision Reject(RejectReason reason) => new(reason, false, null);
        public static AdmitDecision ReplayOf(RejectReason originalReason) => new(originalReason, true, null);
        public static AdmitDecision Accept(CapabilityVerb verb) => new(RejectReason.None, false, verb);
    }

    /// <summary>
    /// Stateful, single-armed-run request admission. NOT thread-safe by itself — the
    /// dispatcher (a later card) owns a single-slot main-thread queue, so admission is
    /// serialized there. In M1 this is the pure decision logic exercised by tests.
    /// </summary>
    public sealed class RequestAdmission
    {
        private readonly ArmedState _armed;

        // Idempotency ledger: requestId -> (seq, first decision reason). A repeat of an
        // identical (requestId, seq) returns the cached decision (replay); a differing
        // seq under the same requestId is a conflict.
        private readonly Dictionary<string, (long Seq, RejectReason Reason)> _seen = new(StringComparer.Ordinal);

        // Highest sequence admitted so far — a strictly-monotonic requirement guards
        // against out-of-order / rewound sequences (ADR-0009 §3.2).
        private long _highestSeq = long.MinValue;

        public RequestAdmission(ArmedState armed)
        {
            _armed = armed ?? throw new ArgumentNullException(nameof(armed));
        }

        /// <summary>Admit one request envelope against the armed run and current time.</summary>
        public AdmitDecision Admit(RequestEnvelope? env, long nowUnixMs)
        {
            // 0. Envelope well-formed.
            if (env == null) return AdmitDecision.Reject(RejectReason.MalformedEnvelope);
            if (string.IsNullOrEmpty(env.RequestId) || string.IsNullOrEmpty(env.Verb) ||
                string.IsNullOrEmpty(env.Nonce) || string.IsNullOrEmpty(env.Hmac) ||
                string.IsNullOrEmpty(env.Role))
                return AdmitDecision.Reject(RejectReason.MalformedEnvelope);

            // 1. Idempotency / replay: a previously-seen requestId.
            if (_seen.TryGetValue(env.RequestId!, out var prior))
            {
                // Same seq => genuine replay: return the cached decision, never re-run.
                if (prior.Seq == env.Seq) return AdmitDecision.ReplayOf(prior.Reason);
                // Same requestId, different seq => conflict.
                return AdmitDecision.Reject(RejectReason.SequenceConflict);
            }

            // 2. Nonce must match the armed run.
            if (!string.Equals(env.Nonce, _armed.Nonce, StringComparison.Ordinal))
                return Remember(env, RejectReason.BadNonce);

            // 3. Role must match the armed role.
            if (!HarnessRoleParser.TryParse(env.Role, out var reqRole) || reqRole != _armed.Role)
                return Remember(env, RejectReason.RoleMismatch);

            // 4. World UID must match.
            if (env.WorldUid != _armed.World.WorldUid)
                return Remember(env, RejectReason.RequestWorldMismatch);

            // 5. Verb must be a known catalog verb.
            var verb = VerbCatalog.Get(env.Verb);
            if (verb == null) return Remember(env, RejectReason.UnknownVerb);

            // 6. Verb must be in this run's capability manifest.
            if (!_armed.Capability.Permits(env.Verb))
                return Remember(env, RejectReason.OutOfManifest);

            // 7. Verb must be legal for the armed role (defense in depth vs the manifest).
            if (!verb.AllowsRole(_armed.Role))
                return Remember(env, RejectReason.RoleMismatch);

            // 8. Typed argument bounds. Every declared arg must be present + in bounds;
            //    no undeclared args allowed.
            if (!ArgsInBounds(verb, env.Args))
                return Remember(env, RejectReason.OutOfBoundsArg);

            // 9. Request expiry.
            if (env.ExpiryUnixMs <= nowUnixMs)
                return Remember(env, RejectReason.RequestExpired);

            // 10. Sequence must be strictly greater than the highest admitted.
            if (env.Seq <= _highestSeq)
                return Remember(env, RejectReason.SequenceConflict);

            // 11. HMAC must verify over the canonical authenticated fields.
            string canonical = RequestHmac.CanonicalString(
                env.Nonce!, env.Seq, env.ExpiryUnixMs, env.Role!, env.WorldUid, env.Verb!, env.RequestId!);
            string expected = RequestHmac.Compute(_armed.HmacSecret, canonical);
            if (!RequestHmac.Verify(expected, env.Hmac!))
                return Remember(env, RejectReason.BadHmac);

            // Admitted. Record and advance the sequence high-water mark.
            _seen[env.RequestId!] = (env.Seq, RejectReason.None);
            _highestSeq = env.Seq;
            return AdmitDecision.Accept(verb);
        }

        /// <summary>Record a terminal decision for idempotency, then return it.</summary>
        private AdmitDecision Remember(RequestEnvelope env, RejectReason reason)
        {
            _seen[env.RequestId!] = (env.Seq, reason);
            return AdmitDecision.Reject(reason);
        }

        private static bool ArgsInBounds(CapabilityVerb verb, IReadOnlyDictionary<string, object?> args)
        {
            // Every declared argument must be present and in bounds.
            foreach (var decl in verb.Args)
            {
                if (!args.TryGetValue(decl.Name, out var value)) return false;
                if (!decl.IsInBounds(value)) return false;
            }
            // No undeclared arguments permitted (closed schema).
            if (args.Count != verb.Args.Count) return false;
            return true;
        }
    }
}
