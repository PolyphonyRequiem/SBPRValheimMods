// The fail-closed arming gate (ADR-0009 §5.1) — the load-bearing decision this card
// delivers. It is AND-composed: EVERY condition must hold or the arm is refused, and
// conditions are evaluated in a FIXED order so the surfaced RejectReason is
// deterministic. Nothing arms by default; production is hard-denied before the
// allowlist is even consulted. Engine-free — the gate takes OBSERVED world facts +
// observed hashes as inputs; a later card supplies those from the live game.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>The immutable result of a successful arm — what the dispatcher checks each request against.</summary>
    public sealed class ArmedState
    {
        public HarnessRole Role { get; }
        public string Actor { get; }
        public WorldIdentity World { get; }
        public string Nonce { get; }
        public long ExpiryUnixMs { get; }
        public CapabilityManifest Capability { get; }
        public string HmacSecret { get; }

        internal ArmedState(
            HarnessRole role, string actor, WorldIdentity world, string nonce,
            long expiryUnixMs, CapabilityManifest capability, string hmacSecret)
        {
            Role = role;
            Actor = actor;
            World = world;
            Nonce = nonce;
            ExpiryUnixMs = expiryUnixMs;
            Capability = capability;
            HmacSecret = hmacSecret;
        }
    }

    /// <summary>Outcome of an arm attempt: either an <see cref="ArmedState"/> or a <see cref="RejectReason"/>.</summary>
    public sealed class ArmDecision
    {
        public bool Armed => Reason == RejectReason.None;
        public RejectReason Reason { get; }
        public ArmedState? State { get; }

        private ArmDecision(RejectReason reason, ArmedState? state)
        {
            Reason = reason;
            State = state;
        }

        public static ArmDecision Reject(RejectReason reason) => new(reason, null);
        public static ArmDecision Accept(ArmedState state) => new(RejectReason.None, state);
    }

    /// <summary>The AND-composed, fixed-order, fail-closed arming gate.</summary>
    public static class ArmingGate
    {
        /// <summary>
        /// Evaluate an arm request. Inputs are the runner's <paramref name="manifest"/>,
        /// the world/hash facts OBSERVED from the live process, the disposable-world
        /// <paramref name="policy"/>, and the current time. Returns the first failing
        /// condition (deterministic) or an armed state.
        /// </summary>
        public static ArmDecision Evaluate(
            ArmManifest? manifest,
            WorldIdentity? observedWorld,
            IReadOnlyDictionary<string, string>? observedHashes,
            WorldPolicy policy,
            long nowUnixMs)
        {
            // 1. Default disabled — absent an explicit arm signal, nothing arms.
            if (manifest == null) return ArmDecision.Reject(RejectReason.DisabledByDefault);
            if (!manifest.Enabled) return ArmDecision.Reject(RejectReason.DisabledByDefault);

            // 2. Role must be an exact, explicit token.
            if (!HarnessRoleParser.TryParse(manifest.RoleToken, out var role))
                return ArmDecision.Reject(RejectReason.UnknownRole);

            // 3. Actor alias must be present (explicit, never inferred).
            if (string.IsNullOrWhiteSpace(manifest.Actor))
                return ArmDecision.Reject(RejectReason.MissingActor);

            // 4. World identity must be present, and observed world must match EXACTLY
            //    (UID and name — name alone is insufficient).
            if (manifest.World == null || observedWorld == null)
                return ArmDecision.Reject(RejectReason.MalformedManifest);
            if (manifest.World.WorldUid != observedWorld.WorldUid)
                return ArmDecision.Reject(RejectReason.WorldUidMismatch);
            if (!string.Equals(manifest.World.WorldName, observedWorld.WorldName, StringComparison.Ordinal))
                return ArmDecision.Reject(RejectReason.WorldNameMismatch);

            // 5. Hard production deny list — refused even if allowlisted/misconfigured.
            if (policy == null) return ArmDecision.Reject(RejectReason.MalformedManifest);
            if (policy.IsProductionDenied(observedWorld))
                return ArmDecision.Reject(RejectReason.ProductionWorldDenied);

            // 6. Disposable-world allowlist membership.
            if (!policy.IsAllowlisted(observedWorld))
                return ArmDecision.Reject(RejectReason.WorldNotAllowlisted);

            // 7. Immutable hash manifest — complete and drift-free vs observed.
            if (manifest.Hashes == null || !manifest.Hashes.IsComplete())
                return ArmDecision.Reject(RejectReason.MalformedManifest);
            if (!manifest.Hashes.MatchesObserved(observedHashes ?? new Dictionary<string, string>()))
                return ArmDecision.Reject(RejectReason.HashManifestDrift);

            // 8. Nonce present.
            if (string.IsNullOrEmpty(manifest.Nonce))
                return ArmDecision.Reject(RejectReason.MissingNonce);

            // 9. Hard expiry strictly in the future.
            if (manifest.ExpiryUnixMs <= nowUnixMs)
                return ArmDecision.Reject(RejectReason.Expired);

            // 10. HMAC secret present (required to authenticate subsequent requests).
            if (string.IsNullOrEmpty(manifest.HmacSecret))
                return ArmDecision.Reject(RejectReason.MalformedManifest);

            // 11. Capability manifest parses to a non-empty, role-appropriate, known subset.
            var reason = CapabilityManifest.TryParse(manifest.PermittedVerbs, role, out var capability);
            if (reason != RejectReason.None)
                return ArmDecision.Reject(reason);

            var state = new ArmedState(
                role, manifest.Actor!, observedWorld, manifest.Nonce!,
                manifest.ExpiryUnixMs, capability!, manifest.HmacSecret!);
            return ArmDecision.Accept(state);
        }
    }
}
