using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // Aggregate 3 — CharacterProgressionAggregate (data-model.md §"Aggregate 3"). One server-owned
    // aggregate per world/product-scoped (AccountId, CharacterId). Stone-linked state is indexed by
    // stable StoneId. Gameplay progression belongs to the CHARACTER, not the account.
    //
    // T004 envelope scope: persist earned/selected/provenance state only. Personal AP, Cumulative AP,
    // personal BP, Facet Credit, and node-purchase provenance are authoritative owner state; the
    // derived "active effect" of any purchase is NOT stored here (AT-NO-ACTIVE-LEDGER) — it is
    // recomputed by DerivedActivationView from these persisted facts.
    //
    // net48 audit: engine-free (System.Collections.Generic + snapshot codec). Link-compiles into net8.

    /// <summary>Per-Stone character balances + provenance. Selected/earned state only.</summary>
    public sealed class CharacterStoneRecord
    {
        public CharacterStoneRecord(StoneId stoneId, int personalAp, int cumulativeAp, int personalBp,
            IReadOnlyList<FacetCreditRecord>? facetCredits = null,
            IReadOnlyList<NodePurchaseRecord>? purchases = null,
            IReadOnlyList<RelationshipRecord>? relationships = null)
        {
            StoneId = stoneId;
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            PersonalBp = personalBp;
            FacetCredits = facetCredits ?? Array.Empty<FacetCreditRecord>();
            Purchases = purchases ?? Array.Empty<NodePurchaseRecord>();
            Relationships = relationships ?? Array.Empty<RelationshipRecord>();
        }

        public StoneId StoneId { get; }
        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int PersonalBp { get; }
        public IReadOnlyList<FacetCreditRecord> FacetCredits { get; }
        public IReadOnlyList<NodePurchaseRecord> Purchases { get; }

        /// <summary>Per-Stone Bond/Attunement records for this character (T007). Active and Released
        /// records both persist here; the derived "active effect" is never stored (data-model.md
        /// CharacterProgression: relationships/status/responsibility range/provenance).</summary>
        public IReadOnlyList<RelationshipRecord> Relationships { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("stoneId", StoneId.Value)
            .PutInt("personalAp", PersonalAp)
            .PutInt("cumulativeAp", CumulativeAp)
            .PutInt("personalBp", PersonalBp)
            .PutList("facetCredit", FacetCredits, f => f.Serialize())
            .PutList("purchases", Purchases, p => p.Serialize())
            .PutList("relationships", Relationships, x => x.Serialize())
            .Build();

        public static CharacterStoneRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new CharacterStoneRecord(
                new StoneId(r.GetString("stoneId")),
                r.GetInt("personalAp"),
                r.GetInt("cumulativeAp"),
                r.GetInt("personalBp"),
                r.GetList("facetCredit", FacetCreditRecord.Deserialize),
                r.GetList("purchases", NodePurchaseRecord.Deserialize),
                // Backward-compatible: pre-T007 snapshots carry no relationships list.
                r.HasKey("relationships.count")
                    ? r.GetList("relationships", RelationshipRecord.Deserialize)
                    : null);
        }
    }

    /// <summary>Facet Credit keyed by StoneId + FacetId, separate from Personal AP, with source
    /// purchase/revocation provenance (data-model.md CharacterProgression "Revocation credit").</summary>
    public sealed class FacetCreditRecord
    {
        public FacetCreditRecord(string facetId, int amount, string sourceProvenance)
        {
            FacetId = facetId ?? string.Empty;
            Amount = amount;
            SourceProvenance = sourceProvenance ?? string.Empty;
        }

        public string FacetId { get; }
        public int Amount { get; }
        public string SourceProvenance { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("facet", FacetId).PutInt("amount", Amount).Put("prov", SourceProvenance).Build();

        public static FacetCreditRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new FacetCreditRecord(r.GetString("facet"), r.GetInt("amount"), r.GetString("prov"));
        }
    }

    /// <summary>Personal node purchase record keyed by Stone/Tree/node/version, AP source, and
    /// refundable/durable outcome class. A purchase is authoritative provenance; whether its effect is
    /// currently active is derived, never stored (AT-NO-ACTIVE-LEDGER).</summary>
    public sealed class NodePurchaseRecord
    {
        public NodePurchaseRecord(VersionedId tree, VersionedId node, string apSource,
            string outcomeClass, VersionedId offeredSet, string sourceOperationId)
        {
            Tree = tree;
            Node = node;
            ApSource = apSource ?? string.Empty;
            OutcomeClass = outcomeClass ?? string.Empty;
            OfferedSet = offeredSet;
            SourceOperationId = sourceOperationId ?? string.Empty;
        }

        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public string ApSource { get; }
        public string OutcomeClass { get; }
        public VersionedId OfferedSet { get; }
        public string SourceOperationId { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("tree", Tree.Serialize())
            .Put("node", Node.Serialize())
            .Put("apSource", ApSource)
            .Put("outcomeClass", OutcomeClass)
            .Put("offeredSet", OfferedSet.Serialize())
            .Put("srcOp", SourceOperationId)
            .Build();

        public static NodePurchaseRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new NodePurchaseRecord(
                VersionedId.Deserialize(r.GetString("tree")),
                VersionedId.Deserialize(r.GetString("node")),
                r.GetString("apSource"),
                r.GetString("outcomeClass"),
                VersionedId.Deserialize(r.GetString("offeredSet")),
                r.GetString("srcOp"));
        }
    }

    public sealed class CharacterProgressionAggregate
    {
        public const int CurrentSchemaVersion = 1;

        public CharacterProgressionAggregate(
            AccountId account,
            CharacterId character,
            string worldProductScope,
            long revision,
            int bondSlots,
            int attunementSlots,
            string lastAppliedReceiptId,
            IReadOnlyList<CharacterStoneRecord>? stoneRecords = null,
            int schemaVersion = CurrentSchemaVersion)
        {
            Account = account;
            Character = character;
            WorldProductScope = worldProductScope ?? string.Empty;
            Revision = revision;
            BondSlots = bondSlots;
            AttunementSlots = attunementSlots;
            LastAppliedReceiptId = lastAppliedReceiptId ?? string.Empty;
            StoneRecords = stoneRecords ?? Array.Empty<CharacterStoneRecord>();
            SchemaVersion = schemaVersion;
        }

        // Envelope
        public int SchemaVersion { get; }
        public AccountId Account { get; }
        public CharacterId Character { get; }
        public string WorldProductScope { get; }
        public long Revision { get; }

        // Capacity
        public int BondSlots { get; }
        public int AttunementSlots { get; }

        // Per-Stone earned/selected/provenance state
        public IReadOnlyList<CharacterStoneRecord> StoneRecords { get; }

        // Recovery
        public string LastAppliedReceiptId { get; }

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("account", Account.Value)
            .Put("character", Character.Value)
            .Put("scope", WorldProductScope)
            .PutLong("revision", Revision)
            .PutInt("bondSlots", BondSlots)
            .PutInt("attSlots", AttunementSlots)
            .Put("lastReceipt", LastAppliedReceiptId)
            .PutList("stones", StoneRecords, x => x.Serialize())
            .Build();

        public static CharacterProgressionAggregate Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new CharacterProgressionAggregate(
                new AccountId(r.GetString("account")),
                new CharacterId(r.GetString("character")),
                r.GetString("scope"),
                r.GetLong("revision"),
                r.GetInt("bondSlots"),
                r.GetInt("attSlots"),
                r.GetString("lastReceipt"),
                r.GetList("stones", CharacterStoneRecord.Deserialize),
                r.GetInt("schema"));
        }

        public bool StructurallyEquals(CharacterProgressionAggregate o)
        {
            if (o == null) return false;
            if (!(SchemaVersion == o.SchemaVersion
                  && Account.Equals(o.Account)
                  && Character.Equals(o.Character)
                  && string.Equals(WorldProductScope, o.WorldProductScope, StringComparison.Ordinal)
                  && Revision == o.Revision
                  && BondSlots == o.BondSlots
                  && AttunementSlots == o.AttunementSlots
                  && string.Equals(LastAppliedReceiptId, o.LastAppliedReceiptId, StringComparison.Ordinal)
                  && StoneRecords.Count == o.StoneRecords.Count))
                return false;
            for (int i = 0; i < StoneRecords.Count; i++)
                if (!string.Equals(StoneRecords[i].Serialize(), o.StoneRecords[i].Serialize(), StringComparison.Ordinal))
                    return false;
            return true;
        }
    }
}
