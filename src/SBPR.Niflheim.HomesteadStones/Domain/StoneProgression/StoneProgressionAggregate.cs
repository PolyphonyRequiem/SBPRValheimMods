using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // Aggregate 1 — StoneProgressionAggregate (data-model.md §"Aggregate 1"). One authoritative
    // world-scoped aggregate per StoneId. This is the T004 versioned ENVELOPE: it persists earned,
    // selected, and provenance state only. Derived activation is disposable and is NEVER stored here
    // as a second authority (AT-NO-ACTIVE-LEDGER) — the DerivedActivationView recomputes it.
    //
    // Scope guard (T004 is the state skeleton, tasks.md Phase 1): the envelope carries the FIELD
    // GROUPS the data model mandates so state round-trips, but the gameplay MUTATIONS that fill them
    // (Facet commitment, BP development, purchases) land in later tracers. Every field here is either
    // an authoritative owner, a revision, a stable identity, or provenance.
    //
    // net48 audit: only System.Collections.Generic / value objects / the engine-free snapshot codec.
    // No net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 tests.

    /// <summary>Committed Tree record: which authored Tree occupies which Facet, with commit
    /// provenance and cumulative qualifying BP investment. Selected/provenance state, never a
    /// derived effect.</summary>
    public sealed class CommittedTreeRecord
    {
        public CommittedTreeRecord(string facetId, VersionedId tree, string commitOperationId,
            string commitActor, int treeLevel, int cumulativeBpInvested)
        {
            FacetId = facetId ?? throw new ArgumentNullException(nameof(facetId));
            Tree = tree;
            CommitOperationId = commitOperationId ?? string.Empty;
            CommitActor = commitActor ?? string.Empty;
            TreeLevel = treeLevel;
            CumulativeBpInvested = cumulativeBpInvested;
        }

        public string FacetId { get; }
        public VersionedId Tree { get; }
        public string CommitOperationId { get; }
        public string CommitActor { get; }
        public int TreeLevel { get; }
        public int CumulativeBpInvested { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("facet", FacetId)
            .Put("tree", Tree.Serialize())
            .Put("commitOp", CommitOperationId)
            .Put("commitActor", CommitActor)
            .PutInt("treeLevel", TreeLevel)
            .PutInt("cumBp", CumulativeBpInvested)
            .Build();

        public static CommittedTreeRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new CommittedTreeRecord(
                r.GetString("facet"),
                VersionedId.Deserialize(r.GetString("tree")),
                r.GetString("commitOp"),
                r.GetString("commitActor"),
                r.GetInt("treeLevel"),
                r.GetInt("cumBp"));
        }
    }

    /// <summary>Per-node development progress. Persisted earned/selected state: how much BP has been
    /// invested toward a node and whether it has been developed and (for personal nodes) Offered.
    /// The "active" runtime status is NOT here — it is derived.</summary>
    public sealed class NodeDevelopmentRecord
    {
        public NodeDevelopmentRecord(VersionedId node, int bpProgress, int bpCost, bool developed,
            bool offered, string sourceOperationId)
        {
            Node = node;
            BpProgress = bpProgress;
            BpCost = bpCost;
            Developed = developed;
            Offered = offered;
            SourceOperationId = sourceOperationId ?? string.Empty;
        }

        public VersionedId Node { get; }
        public int BpProgress { get; }
        public int BpCost { get; }
        public bool Developed { get; }
        public bool Offered { get; }
        public string SourceOperationId { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("node", Node.Serialize())
            .PutInt("prog", BpProgress)
            .PutInt("cost", BpCost)
            .PutBool("developed", Developed)
            .PutBool("offered", Offered)
            .Put("srcOp", SourceOperationId)
            .Build();

        public static NodeDevelopmentRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new NodeDevelopmentRecord(
                VersionedId.Deserialize(r.GetString("node")),
                r.GetInt("prog"),
                r.GetInt("cost"),
                r.GetBool("developed"),
                r.GetBool("offered"),
                r.GetString("srcOp"));
        }
    }

    public sealed class StoneProgressionAggregate
    {
        public const int CurrentSchemaVersion = 1;

        public StoneProgressionAggregate(
            StoneId stoneId,
            long revision,
            int historicalStoneLevel,
            int activeStoneLevel,
            VersionedId foundationalTree,
            VersionedId foundationalCatalog,
            int contentRegistryVersion,
            string createdProvenance,
            string updatedProvenance,
            long mirroredStoneAp,
            string lastAppliedReceiptId,
            IReadOnlyList<CommittedTreeRecord>? committedTrees = null,
            IReadOnlyList<NodeDevelopmentRecord>? nodeDevelopment = null,
            string family = "Settlement",
            string variant = "Homestead",
            int schemaVersion = CurrentSchemaVersion)
        {
            StoneId = stoneId;
            SchemaVersion = schemaVersion;
            Revision = revision;
            HistoricalStoneLevel = historicalStoneLevel;
            ActiveStoneLevel = activeStoneLevel;
            FoundationalTree = foundationalTree;
            FoundationalCatalog = foundationalCatalog;
            ContentRegistryVersion = contentRegistryVersion;
            CreatedProvenance = createdProvenance ?? string.Empty;
            UpdatedProvenance = updatedProvenance ?? string.Empty;
            MirroredStoneAp = mirroredStoneAp;
            LastAppliedReceiptId = lastAppliedReceiptId ?? string.Empty;
            Family = family ?? string.Empty;
            Variant = variant ?? string.Empty;
            CommittedTrees = committedTrees ?? Array.Empty<CommittedTreeRecord>();
            NodeDevelopment = nodeDevelopment ?? Array.Empty<NodeDevelopmentRecord>();
        }

        // Envelope
        public int SchemaVersion { get; }
        public StoneId StoneId { get; }
        public long Revision { get; }
        public string CreatedProvenance { get; }
        public string UpdatedProvenance { get; }

        // Classification
        public string Family { get; }
        public string Variant { get; }
        public int ContentRegistryVersion { get; }

        // Levels
        public int HistoricalStoneLevel { get; }
        public int ActiveStoneLevel { get; }

        // Foundation
        public VersionedId FoundationalTree { get; }
        public VersionedId FoundationalCatalog { get; }

        // Committed Trees / development (selected + provenance state)
        public IReadOnlyList<CommittedTreeRecord> CommittedTrees { get; }
        public IReadOnlyList<NodeDevelopmentRecord> NodeDevelopment { get; }

        // Stone ledger (Mirrored Stone AP is a receipt-derived accumulate-only projection total;
        // it is authoritative state, never debited/applied to a threshold in this proof).
        public long MirroredStoneAp { get; }

        // Recovery
        public string LastAppliedReceiptId { get; }

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("stoneId", StoneId.Value)
            .PutLong("revision", Revision)
            .Put("family", Family)
            .Put("variant", Variant)
            .PutInt("contentVer", ContentRegistryVersion)
            .PutInt("histLevel", HistoricalStoneLevel)
            .PutInt("activeLevel", ActiveStoneLevel)
            .Put("foundTree", FoundationalTree.Serialize())
            .Put("foundCatalog", FoundationalCatalog.Serialize())
            .PutLong("mirroredAp", MirroredStoneAp)
            .Put("createdProv", CreatedProvenance)
            .Put("updatedProv", UpdatedProvenance)
            .Put("lastReceipt", LastAppliedReceiptId)
            .PutList("committed", CommittedTrees, c => c.Serialize())
            .PutList("nodeDev", NodeDevelopment, n => n.Serialize())
            .Build();

        public static StoneProgressionAggregate Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new StoneProgressionAggregate(
                new StoneId(r.GetString("stoneId")),
                r.GetLong("revision"),
                r.GetInt("histLevel"),
                r.GetInt("activeLevel"),
                VersionedId.Deserialize(r.GetString("foundTree")),
                VersionedId.Deserialize(r.GetString("foundCatalog")),
                r.GetInt("contentVer"),
                r.GetString("createdProv"),
                r.GetString("updatedProv"),
                r.GetLong("mirroredAp"),
                r.GetString("lastReceipt"),
                r.GetList("committed", CommittedTreeRecord.Deserialize),
                r.GetList("nodeDev", NodeDevelopmentRecord.Deserialize),
                r.GetString("family"),
                r.GetString("variant"),
                r.GetInt("schema"));
        }

        /// <summary>Structural equality over every authoritative field. Used by the round-trip proof
        /// (AT-STATE-ROUNDTRIP) to assert nothing was dropped or reinterpreted on reload.</summary>
        public bool StructurallyEquals(StoneProgressionAggregate o)
        {
            if (o == null) return false;
            if (!(SchemaVersion == o.SchemaVersion
                  && StoneId.Equals(o.StoneId)
                  && Revision == o.Revision
                  && string.Equals(Family, o.Family, StringComparison.Ordinal)
                  && string.Equals(Variant, o.Variant, StringComparison.Ordinal)
                  && ContentRegistryVersion == o.ContentRegistryVersion
                  && HistoricalStoneLevel == o.HistoricalStoneLevel
                  && ActiveStoneLevel == o.ActiveStoneLevel
                  && FoundationalTree.Equals(o.FoundationalTree)
                  && FoundationalCatalog.Equals(o.FoundationalCatalog)
                  && MirroredStoneAp == o.MirroredStoneAp
                  && string.Equals(CreatedProvenance, o.CreatedProvenance, StringComparison.Ordinal)
                  && string.Equals(UpdatedProvenance, o.UpdatedProvenance, StringComparison.Ordinal)
                  && string.Equals(LastAppliedReceiptId, o.LastAppliedReceiptId, StringComparison.Ordinal)
                  && CommittedTrees.Count == o.CommittedTrees.Count
                  && NodeDevelopment.Count == o.NodeDevelopment.Count))
                return false;
            for (int i = 0; i < CommittedTrees.Count; i++)
                if (!string.Equals(CommittedTrees[i].Serialize(), o.CommittedTrees[i].Serialize(), StringComparison.Ordinal))
                    return false;
            for (int i = 0; i < NodeDevelopment.Count; i++)
                if (!string.Equals(NodeDevelopment[i].Serialize(), o.NodeDevelopment[i].Serialize(), StringComparison.Ordinal))
                    return false;
            return true;
        }
    }
}
