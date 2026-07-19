using System;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T029 — the engine-free server-validation CORE for the Warrior T.W.I.G. Training Local placement
    // capability. It is the missing runtime caller of the shipped, pure
    // Adapters/Warrior/LocalPlacementProvider (QA t_92e47866 / PR #366 FAIL: the provider had zero
    // runtime callers, so on a joined client a T.W.I.G. placement ran through vanilla Player.PlacePiece
    // with NO SBPR gating and the FR-016 effect-active/policy/build-Permission AND never fired in-world).
    //
    // AUTHORITATIVE BINDING (t_02c13405 / PR #368 merged): this gate reads the SAME authoritative Stone
    // progression aggregate + governance projection the shared Local Effect activation runtime
    // (LocalProgressionServer) composes from the accepted, receipt-backed Facet/Development/Activity/
    // LocalPolicy handlers. There is NO provisional second progression truth: the developed T.W.I.G.
    // node, the committed Warrior Tree, the Active Stone Level, and the Settlement Local policy all come
    // from IStoneAggregateStore, and the owner / authorized-Governor governance facts are DERIVED on
    // demand from committed bond state via GovernorPresenceResolver — exactly the projection
    // LocalActivationService.Derive consumes. A T.W.I.G. placement and a Local Effect snapshot for the
    // same occupant therefore agree by construction.
    //
    // This mirrors the Foundational precedent (DedicatedPlacementIngress): the engine-bound net48 layer
    // observes a server-authoritative placement, hands this core ONLY server-owned facts, and this core
    // reconstructs the pure LocalEffectActivationView and routes the exact placement through
    // LocalPlacementProvider.Admit. Neither host shape (listen-host observer / dedicated ingress) makes a
    // gating decision itself — every conjunct (developed node, committed Warrior Tree, Active Stone Level,
    // authorized Governor, Stone Area occupancy, single Settlement Local policy, ordinary build Permission)
    // is decided here from the shared engine-free grammar.
    //
    // Per-occupant relationship / occupancy facts are the SAME server-owned truths the Foundational path
    // already composes (BoundSessionPrincipalIndex + IAccountStoneAuthorityStore + StoneAreaMembership).
    //
    // net48 audit: value objects + engine-free provider/view/store types only. No net5+ surface, no
    // UnityEngine/Valheim/BepInEx, so it link-compiles into the net8 test project and every branch is
    // unit-tested against the real composed runtime.
    public sealed class WarriorLocalPlacementGate
    {
        private readonly LocalPlacementProvider _provider;
        private readonly IStoneAggregateStore _stones;
        private readonly GovernorPresenceResolver _governorPresence;
        private readonly HomesteadProgressionCatalog _catalog;
        private readonly IAccountStoneAuthorityStore _authority;
        private readonly IBoundSessionPrincipalSource _boundSessions;
        private readonly StoneAreaMembership _stoneAreas;

        public WarriorLocalPlacementGate(
            IStoneAggregateStore stones,
            GovernorPresenceResolver governorPresence,
            IAccountStoneAuthorityStore authority,
            IBoundSessionPrincipalSource boundSessions,
            StoneAreaMembership stoneAreas,
            LocalPlacementProvider? provider = null,
            HomesteadProgressionCatalog? catalog = null)
        {
            _stones = stones ?? throw new ArgumentNullException(nameof(stones));
            _governorPresence = governorPresence ?? throw new ArgumentNullException(nameof(governorPresence));
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
            _boundSessions = boundSessions ?? throw new ArgumentNullException(nameof(boundSessions));
            _stoneAreas = stoneAreas ?? throw new ArgumentNullException(nameof(stoneAreas));
            _provider = provider ?? new LocalPlacementProvider();
            _catalog = catalog ?? new HomesteadProgressionCatalog();
        }

        /// <summary>The exact vanilla T.W.I.G. prefab this gate governs. A placement whose server-observed
        /// prefab is anything else is not a T.W.I.G. placement at all and this gate declines to act on it
        /// (<see cref="WarriorPlacementGateDisposition.NotTwig"/>) so the net48 layer leaves it untouched —
        /// the node never widens into a general build gate.</summary>
        public string TwigPrefabName => _provider.PrefabName;

        /// <summary>Authorize one server-observed T.W.I.G. placement. Every argument is a server-owned fact:
        /// <paramref name="peerKey"/> is the durable player:&lt;s_playerID&gt; subject the net48 layer read
        /// off the TRANSPORT-AUTHENTICATED occupant (never a payload); <paramref name="requestedPrefabName"/>
        /// is the placed piece's ZDO prefab; <paramref name="x"/>/<paramref name="z"/> is the placed piece's
        /// server-owned world position; <paramref name="hasOrdinaryBuildPermission"/> is the independent
        /// vanilla build-Permission (ward) result at that position.
        ///
        /// The gate FAILS CLOSED whenever a required server-owned fact is unavailable: an unbound peer, a
        /// position inside no Stone Area, or a Stone with no authoritative aggregate all deny placement.
        /// The T.W.I.G. Training node never grants a build capability by default.</summary>
        public WarriorPlacementGateOutcome Admit(
            string peerKey,
            string requestedPrefabName,
            double x,
            double z,
            bool hasOrdinaryBuildPermission)
        {
            // Not our piece — decline so the net48 layer leaves a non-T.W.I.G. placement entirely alone.
            if (!string.Equals(requestedPrefabName, _provider.PrefabName, StringComparison.Ordinal))
                return WarriorPlacementGateOutcome.NotTwig();

            // Fail closed: the occupant MUST have an admitted, activated bound internal session — the live
            // gameplay principal is the bound internal (AccountId, CharacterId), never a provider subject
            // and never the payload. An unbound peer places nothing.
            if (string.IsNullOrEmpty(peerKey) ||
                !_boundSessions.TryResolve(peerKey, out var principal) ||
                string.IsNullOrEmpty(principal.Account.Value))
                return WarriorPlacementGateOutcome.Denied(WarriorPlacementAdmission.EffectNotActive, "UnboundPeer");

            var occupant = new AccountId(principal.Account.Value);
            var character = new CharacterId(principal.Character.Value);

            // Position -> Stone Area membership from the server-owned transform (never a claimed area). A
            // T.W.I.G. placed outside every Stone Area is dormant/ungoverned -> denied.
            if (!_stoneAreas.TryResolve(x, z, out var stoneId))
                return WarriorPlacementGateOutcome.Denied(WarriorPlacementAdmission.EffectNotActive, "OutsideStoneArea");

            // Authoritative Stone progression aggregate for THIS Stone (developed T.W.I.G. node, committed
            // Warrior Tree, Active Stone Level, single Settlement Local policy) — the SAME store the shared
            // activation runtime derives from. Absent -> nothing developed -> denied.
            var stone = _stones.GetStone(stoneId);
            if (stone == null)
                return WarriorPlacementGateOutcome.Denied(WarriorPlacementAdmission.EffectNotActive, "NoStoneState");

            // Per-occupant server-owned relationship + governance facts, DERIVED from committed state (never
            // a stored flag, never a client claim):
            //   * owner: is this occupant the validated Homestead owner (holds the authorized Governor bond);
            //   * active relationship: does this occupant currently hold a reservation at this Stone;
            //   * authorized Governor present: does ANY authorized Governor currently hold this Stone.
            bool occupantIsOwner = _governorPresence.IsOwner(occupant, stoneId);
            bool occupantHasActiveRelationship =
                _authority.GetAuthority(occupant, stoneId).HasActive(character);
            bool authorizedGovernorPresent = _governorPresence.AuthorizedGovernorPresent(stoneId);

            // Occupancy inside THIS Stone's Area is already proven (TryResolve returned this stoneId).
            var view = LocalEffectActivationView.Derive(
                stone,
                _catalog,
                occupant,
                occupantIsOwner,
                occupantHasActiveRelationship,
                insideStoneArea: true,
                authorizedGovernorPresent: authorizedGovernorPresent);

            // The admit decision for the exact piece is exactly LocalEffectActivationView.CanExercisePlacement
            // (effect active for occupant AND ordinary build Permission). The provider reports which conjunct
            // failed via a precise machine code.
            var decision = _provider.Admit(view, requestedPrefabName, hasOrdinaryBuildPermission);
            return decision.IsAdmitted
                ? WarriorPlacementGateOutcome.Admitted(stoneId)
                : WarriorPlacementGateOutcome.Denied(decision.Admission, decision.Admission.ToString());
        }
    }

    /// <summary>Whether the gate acted on a placement and how.</summary>
    public enum WarriorPlacementGateDisposition
    {
        /// <summary>The server-observed prefab is not the exact T.W.I.G.; the gate declined to act. The net48
        /// layer leaves the placement untouched (some other system, or vanilla, owns it).</summary>
        NotTwig,

        /// <summary>The exact T.W.I.G. may stand: the Local Effect is active for the occupant AND the
        /// occupant holds ordinary build Permission.</summary>
        Admitted,

        /// <summary>The exact T.W.I.G. must be refused/undone: a required conjunct failed (see
        /// <see cref="WarriorPlacementGateOutcome.Admission"/> / <see cref="WarriorPlacementGateOutcome.Reason"/>).</summary>
        Denied
    }

    /// <summary>The outcome of one Warrior T.W.I.G. gate decision.</summary>
    public readonly struct WarriorPlacementGateOutcome
    {
        private WarriorPlacementGateOutcome(WarriorPlacementGateDisposition disposition,
            WarriorPlacementAdmission admission, string reason, StoneId stoneId)
        {
            Disposition = disposition;
            Admission = admission;
            Reason = reason ?? string.Empty;
            StoneId = stoneId;
        }

        public WarriorPlacementGateDisposition Disposition { get; }

        /// <summary>The precise provider machine code when the gate acted on a T.W.I.G. (Admitted /
        /// EffectNotActive / MissingBuildPermission / NotTwigPiece). Defaults to NotTwigPiece when the gate
        /// declined the piece.</summary>
        public WarriorPlacementAdmission Admission { get; }

        /// <summary>A short PII-free reason tag for the operator log.</summary>
        public string Reason { get; }

        /// <summary>The governing Stone when the placement was inside a Stone Area (Admitted, or Denied after
        /// area resolution). Default when unresolved.</summary>
        public StoneId StoneId { get; }

        public bool IsAdmitted => Disposition == WarriorPlacementGateDisposition.Admitted;

        /// <summary>True when this was the exact T.W.I.G. and it was refused — the net48 layer must undo the
        /// placement (destroy the placed piece) so the ungated build does not stand.</summary>
        public bool RequiresUndo => Disposition == WarriorPlacementGateDisposition.Denied;

        internal static WarriorPlacementGateOutcome NotTwig() =>
            new WarriorPlacementGateOutcome(WarriorPlacementGateDisposition.NotTwig,
                WarriorPlacementAdmission.NotTwigPiece, "NotTwig", default);

        internal static WarriorPlacementGateOutcome Admitted(StoneId stoneId) =>
            new WarriorPlacementGateOutcome(WarriorPlacementGateDisposition.Admitted,
                WarriorPlacementAdmission.Admitted, "Admitted", stoneId);

        internal static WarriorPlacementGateOutcome Denied(WarriorPlacementAdmission admission, string reason) =>
            new WarriorPlacementGateOutcome(WarriorPlacementGateDisposition.Denied, admission, reason, default);

        /// <summary>One-line, PII-free operator rendering.</summary>
        public string ToOperatorLine() =>
            $"[warrior-twig] disposition={Disposition} admission={Admission} reason={Reason} stone={StoneId.Value}";
    }
}
