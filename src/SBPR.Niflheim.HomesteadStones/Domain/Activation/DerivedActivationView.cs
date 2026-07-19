using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Domain.Activation
{
    // DerivedActivationView (data-model.md §"DerivedActivationView"). A read-only projection derived
    // from current aggregate snapshots. Its cardinal rule: "No result from this view is persisted as
    // an independently mutable authority."
    //
    // T004 proves exactly that boundary (AT-NO-ACTIVE-LEDGER): this type is DISPOSABLE. It is
    // constructed on demand from the Stone + character + authority snapshots, exposes no Serialize()
    // / no persistence surface, and every field is a pure function of the aggregates. There is no
    // "active effects" table anywhere in the aggregates that a mutation could poke independently; the
    // active/dormant/offered status of every node is RECOMPUTED here from persisted earned/selected/
    // provenance state each time.
    //
    // net48 audit: engine-free. Link-compiles into the net8 test project.

    public enum DerivedNodeState
    {
        Invalid = 0,
        Authored,     // exists in content, no character interaction yet
        Developed,    // Stone-side developed (from NodeDevelopmentRecord.Developed)
        Offered,      // personal node made Offered to eligible attuned players
        Purchased,    // character holds a purchase record for it
        Active,       // purchased AND currently deliverable (relationship + gates satisfied)
        Dormant       // purchased/developed but a relationship/level gate currently suppresses delivery
    }

    /// <summary>One derived row per node. Pure projection — carries no mutable authority.</summary>
    public readonly struct DerivedNodeStatus
    {
        public DerivedNodeStatus(VersionedId node, DerivedNodeState state, bool developed, bool offered,
            bool purchased, bool active)
        {
            Node = node;
            State = state;
            Developed = developed;
            Offered = offered;
            Purchased = purchased;
            Active = active;
        }

        public VersionedId Node { get; }
        public DerivedNodeState State { get; }
        public bool Developed { get; }
        public bool Offered { get; }
        public bool Purchased { get; }
        public bool Active { get; }
    }

    public sealed class DerivedActivationView
    {
        private readonly List<DerivedNodeStatus> _nodes;

        private DerivedActivationView(int activeStoneLevel, bool callerHasActiveRelationship,
            List<DerivedNodeStatus> nodes)
        {
            ActiveStoneLevel = activeStoneLevel;
            CallerHasActiveRelationship = callerHasActiveRelationship;
            _nodes = nodes;
        }

        public int ActiveStoneLevel { get; }
        public bool CallerHasActiveRelationship { get; }
        public IReadOnlyList<DerivedNodeStatus> Nodes => _nodes;

        /// <summary>Derive the activation view for one caller from current aggregate snapshots. This
        /// is the ONLY constructor: the view cannot exist except as a function of persisted state, so
        /// it can never become a second authority (data-model.md; AT-NO-ACTIVE-LEDGER).</summary>
        public static DerivedActivationView Derive(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            // The authority index is keyed by (AccountId, StoneId). Refuse a mismatched row rather
            // than projecting another account's/Stone's relationship onto this caller. The query
            // boundary supplies authenticated aggregates; a key mismatch is an invariant failure,
            // never an inactive relationship that can be silently tolerated.
            if (!authority.Account.Equals(character.Account))
                throw new ArgumentException("Authority account does not match the caller aggregate.", nameof(authority));
            if (!authority.StoneId.Equals(stone.StoneId))
                throw new ArgumentException("Authority Stone does not match the Stone aggregate.", nameof(authority));

            // Caller relationship eligibility: this account/character actively holds a relationship to
            // this Stone. Delivery of any Character/Permanent effect requires it (dormant otherwise).
            // With the multi-active reservation index, "active" == this character holds a reservation.
            bool callerActive = authority.HasActive(character.Character);

            // Collect this caller's purchases at this Stone (provenance state), keyed by node identity.
            var purchasedNodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stone.StoneId)) continue;
                foreach (var p in sr.Purchases)
                    purchasedNodes.Add(NodeKey(p.Node));
            }

            var rows = new List<DerivedNodeStatus>();
            foreach (var dev in stone.NodeDevelopment)
            {
                bool purchased = purchasedNodes.Contains(NodeKey(dev.Node));
                // Active is a PURE derivation: purchased effect delivers only while the caller holds an
                // active relationship. Nothing is read from a stored active-effects ledger — there is
                // none. Change the relationship and re-derive: the same persisted purchase flips
                // active<->dormant with zero writes.
                bool active = purchased && callerActive;

                DerivedNodeState state;
                if (purchased) state = active ? DerivedNodeState.Active : DerivedNodeState.Dormant;
                else if (dev.Offered) state = DerivedNodeState.Offered;
                else if (dev.Developed) state = DerivedNodeState.Developed;
                else state = DerivedNodeState.Authored;

                rows.Add(new DerivedNodeStatus(dev.Node, state, dev.Developed, dev.Offered, purchased, active));
            }

            return new DerivedActivationView(stone.ActiveStoneLevel, callerActive, rows);
        }

        private static string NodeKey(VersionedId node) => node.Key + "@" + node.Version;
    }

    /// <summary>One derived Local Effect row for a single occupant. Pure projection of Stone-owned
    /// developed Local state — carries no mutable authority (spec FR-019, AT-NO-ACTIVE-LEDGER). A Local
    /// Node is Stone-owned developed state (spec FR-015): it is never a purchase, so there is no
    /// per-character record — the row distinguishes RETAINED/DEVELOPED Stone state from whether the
    /// effect is currently ACTIVE for this occupant under the Settlement policy + dormancy.</summary>
    public readonly struct LocalEffectStatus
    {
        public LocalEffectStatus(VersionedId node, VersionedId tree, bool developed, bool policyEligible,
            bool dormant, bool active)
        {
            Node = node;
            Tree = tree;
            Developed = developed;
            PolicyEligible = policyEligible;
            Dormant = dormant;
            Active = active;
        }

        public VersionedId Node { get; }
        public VersionedId Tree { get; }

        /// <summary>The Local Node is Stone-owned developed state (completed). Retained across
        /// relationship loss/policy change — this is the "developed" half the read model must
        /// distinguish from "currently active".</summary>
        public bool Developed { get; }

        /// <summary>This occupant is a beneficiary under the current Settlement Local policy
        /// (Everyone/Attuned/Private). Policy membership only — occupancy/dormancy are separate.</summary>
        public bool PolicyEligible { get; }

        /// <summary>The Local Effect is suppressed by a relationship/level/governance gate even though it
        /// remains developed (spec US5 sc2: no authorized Governor ⇒ Local Effects stop; Active Stone
        /// Level below the node level; Tree no longer committed). Dormancy deletes nothing.</summary>
        public bool Dormant { get; }

        /// <summary>The effect is currently delivered to this occupant: developed AND not dormant AND
        /// the occupant is inside the Stone Area AND policy-eligible. Pure derivation — flip any input
        /// and re-derive with zero writes.</summary>
        public bool Active { get; }
    }

    /// <summary>Pure per-occupant projection of every Stone-owned active Local Effect governed by the
    /// single Settlement Local policy (spec FR-016/FR-019; contracts.md §"SetSettlementLocalPolicy",
    /// §Effect delivery). Constructed on demand from the Stone aggregate + content catalog + the
    /// server-observed occupancy/governance/owner facts. It stores nothing and can never become a
    /// second authority: relationship release, a missing authorized Governor, Stone/Tree dormancy, a
    /// policy change, and rejoin all re-derive active/dormant here with zero mutation.
    ///
    /// The occupancy/governance/owner facts are supplied by the caller because they are cross-account
    /// server truth (whether ANY authorized Governor is bonded Stone-wide; whether this occupant is the
    /// validated Homestead owner; whether the occupant currently stands inside the Stone Area). The
    /// projection never reads or writes a build ACL — a Local PLACEMENT capability additionally requires
    /// ordinary build Permission, evaluated independently via <see cref="CanExercisePlacement"/>.</summary>
    public sealed class LocalEffectActivationView
    {
        private readonly List<LocalEffectStatus> _effects;
        private readonly Dictionary<string, LocalEffectStatus> _byNodeKey;

        private LocalEffectActivationView(LocalBeneficiaryMode policyMode, long policyRevision,
            bool occupantPolicyEligible, bool insideStoneArea, bool authorizedGovernorPresent,
            List<LocalEffectStatus> effects)
        {
            PolicyMode = policyMode;
            PolicyRevision = policyRevision;
            OccupantPolicyEligible = occupantPolicyEligible;
            InsideStoneArea = insideStoneArea;
            AuthorizedGovernorPresent = authorizedGovernorPresent;
            _effects = effects;
            _byNodeKey = new Dictionary<string, LocalEffectStatus>(StringComparer.Ordinal);
            foreach (var e in effects) _byNodeKey[e.Node.Key] = e;
        }

        public LocalBeneficiaryMode PolicyMode { get; }
        public long PolicyRevision { get; }

        /// <summary>This occupant is a beneficiary under the current Settlement policy (membership only).</summary>
        public bool OccupantPolicyEligible { get; }

        /// <summary>The occupant currently stands inside this Stone's Area (server-observed).</summary>
        public bool InsideStoneArea { get; }

        /// <summary>An authorized Governor currently holds governance of this Stone (spec US5 sc2). When
        /// false every Local Effect is dormant regardless of policy (no authorized Governor ⇒ stop).</summary>
        public bool AuthorizedGovernorPresent { get; }

        public IReadOnlyList<LocalEffectStatus> Effects => _effects;

        /// <summary>The derived status for one Local node key, or a developed=false/inactive default when
        /// the node is not a developed Local node here.</summary>
        public LocalEffectStatus StatusFor(VersionedId node) =>
            _byNodeKey.TryGetValue(node.Key, out var s)
                ? s
                : new LocalEffectStatus(node, VersionedId.None, false, OccupantPolicyEligible, true, false);

        /// <summary>Whether this occupant may exercise a Local PLACEMENT capability for the given Local
        /// node. This is the load-bearing AND (spec FR-016 final sentence; edge case "Private policy and
        /// ordinary build access disagree"): the Local Effect must be currently active for the occupant
        /// (developed + governance + occupancy + policy) AND the occupant must independently pass
        /// ordinary build Permission. Neither relationship nor policy silently grants the build ACL, so
        /// <paramref name="hasOrdinaryBuildPermission"/> is a hard, separate conjunct — a policy-eligible
        /// occupant without build Permission cannot place, and a build-permitted occupant outside the
        /// policy cannot place.</summary>
        public bool CanExercisePlacement(VersionedId node, bool hasOrdinaryBuildPermission)
        {
            return StatusFor(node).Active && hasOrdinaryBuildPermission;
        }

        /// <summary>Derive the Local Effect projection for one occupant from current Stone state + the
        /// server-observed facts. This is the ONLY constructor: the view is a pure function of persisted
        /// Stone state and observed occupancy/governance, so it can never become a mutable ledger
        /// (AT-NO-ACTIVE-LEDGER / AT-RELATIONSHIP-DORMANCY).</summary>
        public static LocalEffectActivationView Derive(
            StoneProgressionAggregate stone,
            Content.HomesteadProgressionCatalog catalog,
            AccountId occupant,
            bool occupantIsOwner,
            bool occupantHasActiveRelationship,
            bool insideStoneArea,
            bool authorizedGovernorPresent)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var policy = stone.LocalPolicy;
            bool policyEligible = policy.IsBeneficiary(occupant, occupantIsOwner, occupantHasActiveRelationship);

            var rows = new List<LocalEffectStatus>();
            foreach (var dev in stone.NodeDevelopment)
            {
                if (!dev.Developed) continue;
                var def = catalog.TryResolveNode(dev.Node);
                // Only Stone-cultivated Local nodes produce a Local Effect. Personal nodes are projected
                // by DerivedActivationView; unavailable/unknown nodes never appear here.
                if (def == null || def.Ownership != Content.NodeOwnership.StoneCultivated) continue;

                // The owning Tree must still be committed and the Active Stone Level must still meet the
                // node's authored level; otherwise the developed effect is dormant (data-model.md
                // §Dormancy: "re-derive active outcomes from Active Stone Level and current
                // requirements"). Retained development is never deleted by dormancy.
                bool treeCommitted = IsTreeCommitted(stone, def.Tree);
                bool levelOk = stone.ActiveStoneLevel >= def.TreeLevel;

                // Governance dormancy (spec US5 sc2 / contracts.md line 121): with no authorized Governor
                // present, ALL Local Effects stop even for a policy-eligible occupant.
                bool dormant = !authorizedGovernorPresent || !treeCommitted || !levelOk;

                // Active delivery requires: not dormant, the occupant inside the Area, and policy
                // eligibility. Flip any input and re-derive — the same developed record flips
                // active<->dormant with zero writes.
                bool active = !dormant && insideStoneArea && policyEligible;

                rows.Add(new LocalEffectStatus(def.Node, def.Tree, true, policyEligible, dormant, active));
            }

            return new LocalEffectActivationView(policy.Mode, policy.Revision, policyEligible,
                insideStoneArea, authorizedGovernorPresent, rows);
        }

        private static bool IsTreeCommitted(StoneProgressionAggregate stone, VersionedId tree)
        {
            foreach (var c in stone.CommittedTrees)
                if (string.Equals(c.Tree.Key, tree.Key, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
