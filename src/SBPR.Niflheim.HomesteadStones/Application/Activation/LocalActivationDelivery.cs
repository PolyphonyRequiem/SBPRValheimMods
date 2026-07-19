using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 shared runtime substrate — the BOUNDED server→client Local Effect delivery contract
    // (contracts.md §"Notification contract"; data-model.md §"DerivedActivationView"/§Dormancy). This
    // file defines the two engine-free wire value objects the delivery seam carries:
    //
    //   * LocalActivationSnapshot   — the per-occupant READ MODEL a client fetches. It is a pure
    //     projection of LocalEffectActivationView plus the authoritative Stone/policy revisions and a
    //     monotonic delivery sequence. It carries ONLY derived active/dormant/developed status per Local
    //     node — never a mutable active-effects ledger, never another account's data, never a build ACL.
    //
    //   * LocalActivationNotification — the BOUNDED invalidation event published after a committed
    //     operation (or an observed occupancy/governance change). It carries stable IDs (Stone + occupant),
    //     the new Stone/policy revisions, a monotonic sequence, and a result code — NOT the whole read
    //     model. A client that misses or reorders notifications refetches the current snapshot; it never
    //     treats notification order as authority (contracts.md: "Clients that miss or reorder
    //     notifications fetch the current read model").
    //
    // Fail-closed is the default: LocalActivationSnapshot.Denied(...) is an EMPTY, all-inactive snapshot a
    // caller returns when authority is missing/stale so the client delivers no effect rather than a stale
    // one. Clients never author activation — these types have no server-mutating surface.
    //
    // net48 audit: engine-free (System.Text/Globalization + snapshot codec + engine-free domain types).
    // Link-compiles into the net8 test project.

    /// <summary>One Local Effect row on the wire: the node identity + owning Tree and the derived
    /// developed/policy-eligible/dormant/active status for THIS occupant. A pure projection of
    /// <see cref="LocalEffectStatus"/>; carries no mutable authority.</summary>
    public readonly struct LocalActivationRow
    {
        public LocalActivationRow(VersionedId node, VersionedId tree, bool developed, bool policyEligible,
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
        public bool Developed { get; }
        public bool PolicyEligible { get; }
        public bool Dormant { get; }
        public bool Active { get; }

        internal static LocalActivationRow FromStatus(LocalEffectStatus s) =>
            new LocalActivationRow(s.Node, s.Tree, s.Developed, s.PolicyEligible, s.Dormant, s.Active);
    }

    /// <summary>The per-occupant Local Effect READ MODEL delivered to a client. Immutable, bounded, and a
    /// pure projection of the authoritative Stone aggregate + server-observed occupancy/governance facts
    /// (via <see cref="LocalEffectActivationView"/>). It carries the durable Stone/policy revisions plus a
    /// monotonic delivery <see cref="Sequence"/> so a client can drop a stale/reordered notification and
    /// refetch. Clients never author it (no server-mutating surface).</summary>
    public sealed class LocalActivationSnapshot
    {
        private readonly List<LocalActivationRow> _rows;
        private readonly Dictionary<string, LocalActivationRow> _byNodeKey;

        public LocalActivationSnapshot(
            StoneId stoneId,
            AccountId occupant,
            long sequence,
            long stoneRevision,
            long policyRevision,
            LocalBeneficiaryMode policyMode,
            bool authorityPresent,
            bool occupantPolicyEligible,
            bool insideStoneArea,
            bool authorizedGovernorPresent,
            IReadOnlyList<LocalActivationRow> rows)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            StoneId = stoneId;
            Occupant = occupant;
            Sequence = sequence;
            StoneRevision = stoneRevision;
            PolicyRevision = policyRevision;
            PolicyMode = policyMode;
            AuthorityPresent = authorityPresent;
            OccupantPolicyEligible = occupantPolicyEligible;
            InsideStoneArea = insideStoneArea;
            AuthorizedGovernorPresent = authorizedGovernorPresent;
            _rows = new List<LocalActivationRow>(rows ?? Array.Empty<LocalActivationRow>());
            _byNodeKey = new Dictionary<string, LocalActivationRow>(StringComparer.Ordinal);
            foreach (var r in _rows) _byNodeKey[r.Node.Key] = r;
        }

        public StoneId StoneId { get; }
        public AccountId Occupant { get; }

        /// <summary>Monotonic per-occupant delivery sequence. Higher = newer. A client applies a snapshot
        /// only when its sequence is at least the last one it holds, so a reordered late fetch cannot roll
        /// delivery backward.</summary>
        public long Sequence { get; }

        public long StoneRevision { get; }
        public long PolicyRevision { get; }
        public LocalBeneficiaryMode PolicyMode { get; }

        /// <summary>False when the server could not resolve authoritative Stone state for this occupant
        /// (missing/stale authority). A denied snapshot is empty and all-inactive — fail closed.</summary>
        public bool AuthorityPresent { get; }

        public bool OccupantPolicyEligible { get; }
        public bool InsideStoneArea { get; }
        public bool AuthorizedGovernorPresent { get; }

        public IReadOnlyList<LocalActivationRow> Rows => _rows;

        /// <summary>The delivered row for one Local node key, or an inactive default when the node is not a
        /// developed Local node in this snapshot. A caller that reads a non-present node gets Active=false,
        /// so an unknown/undeveloped node can never deliver an effect.</summary>
        public LocalActivationRow RowFor(VersionedId node) =>
            _byNodeKey.TryGetValue(node.Key, out var r)
                ? r
                : new LocalActivationRow(node, VersionedId.None, false, false, true, false);

        /// <summary>Whether the Local Effect for <paramref name="node"/> is currently delivered to this
        /// occupant. Pure read of the derived snapshot — no gate is re-evaluated here.</summary>
        public bool IsActive(VersionedId node) => RowFor(node).Active;

        /// <summary>Whether this occupant may exercise a Local PLACEMENT capability for <paramref name="node"/>.
        /// The load-bearing AND (spec FR-016 final sentence): the effect must be currently active AND the
        /// occupant must independently pass ordinary build Permission. Neither relationship nor policy
        /// silently grants the build ACL.</summary>
        public bool CanExercisePlacement(VersionedId node, bool hasOrdinaryBuildPermission) =>
            IsActive(node) && hasOrdinaryBuildPermission;

        /// <summary>A fail-closed EMPTY snapshot for an occupant the server cannot authoritatively resolve
        /// (missing/stale Stone authority). All effects inactive; AuthorityPresent=false. The client
        /// delivers nothing.</summary>
        public static LocalActivationSnapshot Denied(StoneId stoneId, AccountId occupant, long sequence) =>
            new LocalActivationSnapshot(stoneId, occupant, sequence, 0, 0,
                LocalBeneficiaryMode.Everyone, authorityPresent: false, occupantPolicyEligible: false,
                insideStoneArea: false, authorizedGovernorPresent: false,
                Array.Empty<LocalActivationRow>());

        /// <summary>Deterministic wire serialization (stable field order, ordinal). Used by the net48 RPC
        /// glue and asserted for equality by the recovery/reorder tests.</summary>
        public string Serialize()
        {
            var w = new SnapshotWriter()
                .Put("stone", StoneId.Value)
                .Put("occ", Occupant.Value)
                .PutLong("seq", Sequence)
                .PutLong("srev", StoneRevision)
                .PutLong("prev", PolicyRevision)
                .PutInt("pmode", (int)PolicyMode)
                .PutInt("auth", AuthorityPresent ? 1 : 0)
                .PutInt("elig", OccupantPolicyEligible ? 1 : 0)
                .PutInt("inside", InsideStoneArea ? 1 : 0)
                .PutInt("gov", AuthorizedGovernorPresent ? 1 : 0)
                .PutInt("n", _rows.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                w.Put("nk" + i.ToString(CultureInfo.InvariantCulture), r.Node.Key)
                 .PutInt("nv" + i.ToString(CultureInfo.InvariantCulture), r.Node.Version)
                 .Put("tk" + i.ToString(CultureInfo.InvariantCulture), r.Tree.Key)
                 .PutInt("tv" + i.ToString(CultureInfo.InvariantCulture), r.Tree.Version)
                 .PutInt("d" + i.ToString(CultureInfo.InvariantCulture), r.Developed ? 1 : 0)
                 .PutInt("pe" + i.ToString(CultureInfo.InvariantCulture), r.PolicyEligible ? 1 : 0)
                 .PutInt("dm" + i.ToString(CultureInfo.InvariantCulture), r.Dormant ? 1 : 0)
                 .PutInt("ac" + i.ToString(CultureInfo.InvariantCulture), r.Active ? 1 : 0);
            }
            return w.Build();
        }

        public static LocalActivationSnapshot Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            int count = r.GetInt("n");
            var rows = new List<LocalActivationRow>(count);
            for (int i = 0; i < count; i++)
            {
                var node = new VersionedId(
                    r.GetString("nk" + i.ToString(CultureInfo.InvariantCulture)),
                    r.GetInt("nv" + i.ToString(CultureInfo.InvariantCulture)));
                var tree = new VersionedId(
                    r.GetString("tk" + i.ToString(CultureInfo.InvariantCulture)),
                    r.GetInt("tv" + i.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new LocalActivationRow(node, tree,
                    r.GetInt("d" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("pe" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("dm" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("ac" + i.ToString(CultureInfo.InvariantCulture)) == 1));
            }
            return new LocalActivationSnapshot(
                new StoneId(r.GetString("stone")),
                new AccountId(r.GetString("occ")),
                r.GetLong("seq"),
                r.GetLong("srev"),
                r.GetLong("prev"),
                (LocalBeneficiaryMode)r.GetInt("pmode"),
                r.GetInt("auth") == 1,
                r.GetInt("elig") == 1,
                r.GetInt("inside") == 1,
                r.GetInt("gov") == 1,
                rows);
        }
    }

    /// <summary>The BOUNDED invalidation event published after a committed operation or an observed
    /// occupancy/governance change (contracts.md §"Notification contract"). It carries stable IDs (Stone +
    /// occupant), the new authoritative revisions, a monotonic sequence, and a result code — never the
    /// full read model. A client applies it only to decide whether to REFETCH the snapshot; it is never
    /// authority for delivery on its own.</summary>
    public readonly struct LocalActivationNotification
    {
        public LocalActivationNotification(StoneId stoneId, AccountId occupant, long sequence,
            long stoneRevision, long policyRevision, string resultCode)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            StoneId = stoneId;
            Occupant = occupant;
            Sequence = sequence;
            StoneRevision = stoneRevision;
            PolicyRevision = policyRevision;
            ResultCode = resultCode ?? string.Empty;
        }

        public StoneId StoneId { get; }
        public AccountId Occupant { get; }
        public long Sequence { get; }
        public long StoneRevision { get; }
        public long PolicyRevision { get; }
        public string ResultCode { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("stone", StoneId.Value)
            .Put("occ", Occupant.Value)
            .PutLong("seq", Sequence)
            .PutLong("srev", StoneRevision)
            .PutLong("prev", PolicyRevision)
            .Put("rc", ResultCode)
            .Build();

        public static LocalActivationNotification Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            return new LocalActivationNotification(
                new StoneId(r.GetString("stone")),
                new AccountId(r.GetString("occ")),
                r.GetLong("seq"),
                r.GetLong("srev"),
                r.GetLong("prev"),
                r.GetString("rc"));
        }
    }
}
