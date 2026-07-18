using System;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R2 — the engine-free DEDICATED-server placement ingress + revalidation core.
    //
    // Why this exists (the T009R integration-review FAIL): the listen-host observer is a server-run
    // Player.PlacePiece postfix. On a listen/singleplayer host the placing player's PlacePiece runs on
    // the server, so that seam carries a genuine server-authoritative placement. But a joined DEDICATED-
    // server client's build never runs PlacePiece on the server — the placed piece replicates to the
    // server as a ZDO. The server-gated PlacePiece postfix therefore emits ZERO receipts for the exact
    // path T009L must prove. This ingress closes that gap WITHOUT trusting the client.
    //
    // The trust model (contracts.md; card T009R2 "Required correction"): a client may send a NOTICE that
    // merely IDENTIFIES a candidate placed instance (an opaque physical-instance key — a ZDOID string).
    // That notice is a POINTER, never authority. Every credit-bearing fact is independently re-derived by
    // the server from its OWN authoritative ZDO store via IServerPlacedInstanceSource:
    //   * authoritative EXISTENCE — the ZDO must resolve in the server's own store (a fabricated or
    //     already-destroyed key is rejected NoSuchInstance);
    //   * exact PREFAB → stable catalog identity — re-resolved server-side through the version-pinned
    //     FoundationalPrefabMap (the notice never carries the piece id);
    //   * CREATOR / actor binding — the ZDO's server-recorded creator principal MUST equal the principal
    //     the server derived from the AUTHENTICATED RPC sender (not the payload). A client cannot claim
    //     credit for a piece it did not create (CreatorMismatch), nor spoof another sender;
    //   * POSITION → Stone Area membership — from the ZDO's server-owned transform, never a claimed area;
    //   * SUCCESS / current-world state — a resolvable resident ZDO is a materialized successful placement;
    //   * EXCLUSIONS / catalog VERSION — enforced downstream by the shared FoundationalPlacementAdapter;
    //   * stable physical-instance REPETITION key — the ZDOID string, so the same physical piece is
    //     credited at most once across duplicate/replayed notices, retry, and restart.
    //
    // Shared server-validation core (card requirement): the ingress does NOT re-implement admission. It
    // reconstructs the SAME FoundationalPlacementObservation the listen-host observer builds and routes it
    // through the SAME FoundationalPlacementRuntime.Observe — adapter → relationship-backed pipeline →
    // durable receipt. Both host shapes converge on one validation/credit path; the ingress only adds the
    // dedicated-specific front-end (existence + creator binding) that the listen-host seam got for free
    // from running PlacePiece under the authenticated local player.
    //
    // Startup / replication safety (card requirement): ingress is NOTICE-DRIVEN, never a ZDO scan. A
    // server booting or replicating old resident ZDOs generates NO notice, so it awards nothing for
    // previously-loaded pieces — the vanilla distinction between "a client just placed this" (a live
    // client-side notice) and "the server loaded/replicated an existing ZDO" (no notice) is exactly what
    // separates a new successful placement from ordinary loading. Duplicate/replayed notices for one
    // physical instance converge on the single receipt (deterministic ZDOID-derived operation id); a
    // conflicting reuse of a credited instance rejects at the receipt layer.
    //
    // net48 audit: value objects + engine-free runtime/map/membership only. No net5+ surface, no
    // UnityEngine/Valheim, so it link-compiles into the net8 test project and every revalidation branch
    // is unit-tested against a fake IServerPlacedInstanceSource.
    public sealed class DedicatedPlacementIngress
    {
        private readonly FoundationalPlacementRuntime _runtime;
        private readonly IServerPlacedInstanceSource _instances;
        private readonly StoneAreaMembership _stoneAreas;
        private readonly FoundationalPrefabMap _prefabMap;
        private readonly IBoundSessionPrincipalSource _boundSessions;

        public DedicatedPlacementIngress(
            FoundationalPlacementRuntime runtime,
            IServerPlacedInstanceSource instances,
            StoneAreaMembership stoneAreas,
            IBoundSessionPrincipalSource boundSessions,
            FoundationalPrefabMap? prefabMap = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _stoneAreas = stoneAreas ?? throw new ArgumentNullException(nameof(stoneAreas));
            _boundSessions = boundSessions ?? throw new ArgumentNullException(nameof(boundSessions));
            _prefabMap = prefabMap ?? FoundationalPrefabMap.CurrentBuild;
        }

        /// <summary>Ingest one dedicated-server placement NOTICE. <paramref name="peerKey"/> is the
        /// server-owned durable peer key (the <c>player:&lt;s_playerID&gt;</c> character subject the net48
        /// layer read off the TRANSPORT-AUTHENTICATED sender's own character ZDO — never the forgeable
        /// routed sender id or a payload). It is used for TWO server-owned facts: (1) resolving the acting
        /// peer's BOUND INTERNAL gameplay principal (AccountId/CharacterId) published by admission
        /// (Tracer 1/2), and (2) matching the placed ZDO's server-recorded <c>s_creator</c> (vanilla stamps
        /// it from the placing character's s_playerID, so the creator subject IS this same key).
        /// <paramref name="candidateInstanceKey"/> is the opaque physical-instance pointer the notice
        /// carried (a ZDOID string). IAP-007 Tracer 3 / IAP-007W: an UNBOUND peer (no admitted, activated
        /// session) resolves to nothing and the path FAILS CLOSED (credits nothing) rather than crediting a
        /// provider/platform subject — there is no candidate-A fallback. The server independently
        /// re-derives every other credit-bearing fact from its own ZDO store, then routes the reconstructed
        /// observation (bound INTERNAL principal, never the payload) through the shared runtime.</summary>
        public DedicatedIngressOutcome Ingest(string peerKey, string candidateInstanceKey)
        {
            peerKey ??= string.Empty;

            if (string.IsNullOrEmpty(candidateInstanceKey))
                return DedicatedIngressOutcome.Rejected(DedicatedIngressRejection.MissingInstanceKey, candidateInstanceKey);

            // IAP-007W fail-closed: the acting peer MUST have an admitted, activated bound internal session.
            // An unbound peer credits nothing — the live gameplay principal is the bound internal
            // (AccountId, CharacterId), never a provider/platform subject and never the payload.
            if (string.IsNullOrEmpty(peerKey) ||
                !_boundSessions.TryResolve(peerKey, out var principal) ||
                string.IsNullOrEmpty(principal.Account.Value))
                return DedicatedIngressOutcome.Rejected(DedicatedIngressRejection.UnboundPeer, candidateInstanceKey);

            // Authoritative existence: the physical instance must resolve in the SERVER's own ZDO store.
            // A fabricated / already-destroyed key earns nothing (the notice is only a pointer).
            if (!_instances.TryResolve(candidateInstanceKey, out var facts) || !facts.Exists)
                return DedicatedIngressOutcome.Rejected(DedicatedIngressRejection.NoSuchInstance, candidateInstanceKey);

            // Creator / actor binding (Blocker 2): vanilla stamps a placed piece's ZDO s_creator with the
            // placing CHARACTER's s_playerID, rendered as the same player:<s_playerID> subject as the peer
            // key. Both are server-derived and in one space; a mismatch means the sender did not create the
            // piece (or is spoofing), and earns nothing. An empty creator is unbindable → reject.
            if (string.IsNullOrEmpty(facts.CreatorPrincipal) ||
                !string.Equals(facts.CreatorPrincipal, peerKey, StringComparison.Ordinal))
                return DedicatedIngressOutcome.Rejected(DedicatedIngressRejection.CreatorMismatch, candidateInstanceKey);

            // Exact prefab → stable catalog identity, re-resolved server-side (never from the notice). An
            // unmapped prefab resolves to empty, which the shared adapter rejects as MissingPieceIdentity.
            string stablePieceId = _prefabMap.ResolveStablePieceId(facts.PrefabName) ?? string.Empty;

            // Position → Stone Area membership from the ZDO's server-owned transform (never a claimed area).
            bool inside = _stoneAreas.TryResolve(facts.X, facts.Z, out var stoneId);

            // A resolvable resident ZDO IS a materialized successful placement; version comes from the
            // server's pinned catalog tag; provenance is the durable ZDOID so replays converge on one
            // receipt. IAP-007 Tracer 3 / IAP-007W: the ACCOUNT and CHARACTER are the BOUND INTERNAL
            // principal published by admission for this peer — never a provider/platform subject, never the
            // s_playerID, and never the payload.
            var observation = new FoundationalPlacementObservation(
                inside ? stoneId : default,
                principal.Account.Value,
                principal.Character.Value,
                stablePieceId,
                candidateInstanceKey,
                insideStoneArea: inside,
                placementSucceeded: true,
                foundationalCatalogVersion: _prefabMap.CatalogVersionTag);

            var outcome = _runtime.Observe(observation);
            return DedicatedIngressOutcome.FromRuntime(outcome);
        }
    }

    /// <summary>Server-owned read port over the authoritative placed-instance (ZDO) store. The net48
    /// layer implements this over ZDOMan (resolve a ZDOID, read its prefab hash → name, its recorded
    /// creator principal, and its world position); the net8 tests implement it in-memory. It exposes
    /// ONLY server-derived facts — nothing on it is client-authored.</summary>
    public interface IServerPlacedInstanceSource
    {
        /// <summary>Resolve one physical-instance key to the server's authoritative facts about it.
        /// Returns false (and default facts) when no such instance exists in the server's store.</summary>
        bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts);
    }

    /// <summary>The server's authoritative facts about one placed physical instance, all derived from the
    /// server-owned ZDO — never from a client notice.</summary>
    public readonly struct ServerPlacedInstanceFacts
    {
        public ServerPlacedInstanceFacts(string instanceKey, string prefabName, string creatorPrincipal,
            double x, double z, bool exists)
        {
            InstanceKey = instanceKey ?? string.Empty;
            PrefabName = prefabName ?? string.Empty;
            CreatorPrincipal = creatorPrincipal ?? string.Empty;
            X = x;
            Z = z;
            Exists = exists;
        }

        /// <summary>Stable physical-instance key (the ZDOID string), server-owned.</summary>
        public string InstanceKey { get; }

        /// <summary>Placed prefab name read from the ZDO (server-owned), mapped to a stable id by the
        /// version-pinned FoundationalPrefabMap.</summary>
        public string PrefabName { get; }

        /// <summary>The ZDO's server-recorded creator principal, in the same server-owned identity space
        /// as the authenticated sender principal. Empty when the ZDO records no creator.</summary>
        public string CreatorPrincipal { get; }

        /// <summary>Server-owned world position (X/Z) for Stone Area resolution.</summary>
        public double X { get; }
        public double Z { get; }

        /// <summary>Whether the instance actually resolves in the server's authoritative store.</summary>
        public bool Exists { get; }

        public static ServerPlacedInstanceFacts Absent(string instanceKey) =>
            new ServerPlacedInstanceFacts(instanceKey, string.Empty, string.Empty, 0.0, 0.0, exists: false);
    }

    /// <summary>Why a dedicated-server ingress notice was refused BEFORE the shared runtime, or that it
    /// was routed through and the runtime decided. Pre-runtime rejections write no receipt.</summary>
    public enum DedicatedIngressRejection
    {
        /// <summary>The notice was routed to the shared runtime (see <see cref="DedicatedIngressOutcome.Runtime"/>).</summary>
        None,
        /// <summary>The notice carried no physical-instance key to revalidate.</summary>
        MissingInstanceKey,
        /// <summary>No such instance exists in the server's authoritative ZDO store (fabricated / stale key).</summary>
        NoSuchInstance,
        /// <summary>The ZDO's recorded creator does not match the authenticated sender principal.</summary>
        CreatorMismatch,
        /// <summary>The acting peer has no admitted, activated bound internal session (IAP-007W fail-closed):
        /// the live gameplay principal is the bound internal (AccountId, CharacterId), never a
        /// provider/platform subject, so an unbound peer credits nothing.</summary>
        UnboundPeer
    }

    /// <summary>The outcome of one dedicated-server ingress notice: either a pre-runtime revalidation
    /// rejection (no receipt), or the shared runtime's outcome once revalidation passed.</summary>
    public readonly struct DedicatedIngressOutcome
    {
        private DedicatedIngressOutcome(DedicatedIngressRejection rejection, bool routed,
            RuntimePlacementOutcome runtime, string instanceKey)
        {
            Rejection = rejection;
            Routed = routed;
            Runtime = runtime;
            InstanceKey = instanceKey ?? string.Empty;
        }

        /// <summary>The pre-runtime rejection reason, or None when the notice reached the shared runtime.</summary>
        public DedicatedIngressRejection Rejection { get; }

        /// <summary>True when revalidation passed and the notice was routed through the shared runtime.</summary>
        public bool Routed { get; }

        /// <summary>The shared-runtime outcome. Only meaningful when <see cref="Routed"/> is true.</summary>
        public RuntimePlacementOutcome Runtime { get; }

        public string InstanceKey { get; }

        /// <summary>Whether this ingress ultimately credited AP (only possible via the routed runtime).</summary>
        public bool Credited => Routed && Runtime.Credited;

        internal static DedicatedIngressOutcome Rejected(DedicatedIngressRejection rejection, string instanceKey) =>
            new DedicatedIngressOutcome(rejection, routed: false, default, instanceKey);

        internal static DedicatedIngressOutcome FromRuntime(RuntimePlacementOutcome runtime) =>
            new DedicatedIngressOutcome(DedicatedIngressRejection.None, routed: true, runtime,
                runtime.StablePieceId);

        /// <summary>One-line, PII-free operator rendering.</summary>
        public string ToOperatorLine() =>
            Routed
                ? "[foundational-dedicated] " + Runtime.ToOperatorLine()
                : $"[foundational-dedicated] rejected={Rejection} instance={InstanceKey}";
    }
}
