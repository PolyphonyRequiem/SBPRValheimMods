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
}
