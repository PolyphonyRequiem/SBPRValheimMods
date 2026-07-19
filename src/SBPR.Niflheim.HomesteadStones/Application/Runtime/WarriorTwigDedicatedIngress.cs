using System;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T029 remediation — the engine-free DEDICATED-server ingress for the Warrior T.W.I.G. Training gate.
    // Direct analogue of DedicatedPlacementIngress (Foundational), but it GATES/UNDOES rather than credits.
    //
    // Why it exists: a joined DEDICATED-server client's build never runs Player.PlacePiece on the server
    // (that seam is the listen-host WarriorTwigPlacementObserver). The placed T.W.I.G. replicates to the
    // server as a ZDO. Without this ingress the dedicated-client placement would be entirely ungated. Like
    // the Foundational ingress, the trust model is: the client may send a NOTICE that only IDENTIFIES a
    // candidate placed instance (an opaque ZDOID string); every gating-bearing fact is independently
    // re-derived by the server from its OWN authoritative ZDO store (IServerPlacedInstanceSource) —
    // prefab, creator, position — plus the server-composed relationship/session/policy/governance state the
    // shared gate consumes. The notice is a pointer, never authority.
    //
    // Build Permission is a net48 concern (vanilla PrivateArea.CheckAccess at a world position), so the
    // caller supplies it via a delegate keyed by the server-owned position; the ingress never trusts a
    // client permission claim.
    //
    // Outcome semantics: when the resolved instance is the exact T.W.I.G. and the gate REFUSES, the outcome
    // carries RequiresUndo=true and the InstanceKey the net48 pump must destroy (server-side ZDO destroy,
    // which replicates the removal to the client). An admitted T.W.I.G. is left standing. A non-T.W.I.G.
    // instance, an unresolved ZDO, or a creator mismatch are all left untouched (never destroyed).
    //
    // net48 audit: value objects + engine-free gate/instance-source interfaces only. No net5+ surface, no
    // UnityEngine/Valheim, so it link-compiles into the net8 test project and every branch is unit-tested.
    public sealed class WarriorTwigDedicatedIngress
    {
        private readonly WarriorLocalPlacementGate _gate;
        private readonly IServerPlacedInstanceSource _instances;

        public WarriorTwigDedicatedIngress(WarriorLocalPlacementGate gate, IServerPlacedInstanceSource instances)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        }

        /// <summary>Ingest one dedicated-server T.W.I.G. placement notice. <paramref name="peerKey"/> is the
        /// server-owned durable player:&lt;s_playerID&gt; subject the net48 layer read off the
        /// TRANSPORT-AUTHENTICATED sender (never a payload); <paramref name="candidateInstanceKey"/> is the
        /// opaque ZDOID pointer the notice carried; <paramref name="buildPermissionAt"/> answers the vanilla
        /// ward/build-Permission question at a server-owned world position (supplied by the net48 layer).
        ///
        /// Every credit/gating-bearing fact is re-derived server-side. The creator binding is enforced (the
        /// resolved ZDO's server-recorded creator MUST equal the authenticated sender), so a client cannot
        /// force-undo a piece it did not place. The gate itself then decides admit/refuse from the shared
        /// grammar.</summary>
        public WarriorTwigIngressResult Ingest(
            string peerKey, string candidateInstanceKey, Func<double, double, bool> buildPermissionAt)
        {
            if (buildPermissionAt == null) throw new ArgumentNullException(nameof(buildPermissionAt));

            if (string.IsNullOrEmpty(candidateInstanceKey))
                return WarriorTwigIngressResult.NotResolved("MissingInstanceKey");

            // Authoritative existence: the physical instance must resolve in the SERVER's own ZDO store.
            if (!_instances.TryResolve(candidateInstanceKey, out var facts) || !facts.Exists)
                return WarriorTwigIngressResult.NotResolved("NoSuchInstance");

            // Creator / actor binding: the ZDO's server-recorded creator (rendered into the shared
            // player:<s_playerID> space) MUST equal the authenticated sender. A mismatch means the sender
            // did not place this piece (or is spoofing) — do NOT act on it.
            if (string.IsNullOrEmpty(peerKey) ||
                string.IsNullOrEmpty(facts.CreatorPrincipal) ||
                !string.Equals(facts.CreatorPrincipal, peerKey, StringComparison.Ordinal))
                return WarriorTwigIngressResult.NotResolved("CreatorMismatch");

            bool hasBuildPermission = buildPermissionAt(facts.X, facts.Z);

            var outcome = _gate.Admit(peerKey, facts.PrefabName, facts.X, facts.Z, hasBuildPermission);
            return WarriorTwigIngressResult.Resolved(candidateInstanceKey, outcome);
        }
    }

    /// <summary>The result of one Warrior dedicated-ingress attempt: either the instance did not resolve
    /// (still replicating, fabricated, or creator-mismatched — the net48 layer keeps polling until a
    /// deadline, then drops), or it resolved and the gate decided.</summary>
    public readonly struct WarriorTwigIngressResult
    {
        private WarriorTwigIngressResult(bool resolved, string instanceKey, string unresolvedReason,
            WarriorPlacementGateOutcome outcome)
        {
            IsResolved = resolved;
            InstanceKey = instanceKey ?? string.Empty;
            UnresolvedReason = unresolvedReason ?? string.Empty;
            Outcome = outcome;
        }

        /// <summary>True when the server resolved the physical instance and ran the gate. False when the ZDO
        /// was absent / the notice was malformed / the creator did not bind (the pump retries or drops).</summary>
        public bool IsResolved { get; }

        /// <summary>The physical-instance key that resolved (only meaningful when <see cref="IsResolved"/>).</summary>
        public string InstanceKey { get; }

        /// <summary>Why the instance did not resolve (NoSuchInstance is retryable until deadline; the rest
        /// are terminal). Empty when resolved.</summary>
        public string UnresolvedReason { get; }

        /// <summary>The gate decision when resolved. Only meaningful when <see cref="IsResolved"/>.</summary>
        public WarriorPlacementGateOutcome Outcome { get; }

        /// <summary>The instance did not resolve because the ZDO has not replicated yet — the pump keeps
        /// polling this entry until its deadline (never a terminal drop by itself).</summary>
        public bool IsAwaitingReplication =>
            !IsResolved && string.Equals(UnresolvedReason, "NoSuchInstance", StringComparison.Ordinal);

        /// <summary>True when the resolved instance is the exact T.W.I.G. and the gate refused it — the net48
        /// layer must destroy <see cref="InstanceKey"/> so the ungated dedicated-client build does not stand.</summary>
        public bool RequiresUndo => IsResolved && Outcome.RequiresUndo;

        internal static WarriorTwigIngressResult NotResolved(string reason) =>
            new WarriorTwigIngressResult(false, string.Empty, reason, default);

        internal static WarriorTwigIngressResult Resolved(string instanceKey, WarriorPlacementGateOutcome outcome) =>
            new WarriorTwigIngressResult(true, instanceKey, string.Empty, outcome);

        /// <summary>One-line, PII-free operator rendering.</summary>
        public string ToOperatorLine() =>
            IsResolved
                ? "[warrior-twig-dedicated] " + Outcome.ToOperatorLine()
                : $"[warrior-twig-dedicated] unresolved={UnresolvedReason} instance={InstanceKey}";
    }
}
