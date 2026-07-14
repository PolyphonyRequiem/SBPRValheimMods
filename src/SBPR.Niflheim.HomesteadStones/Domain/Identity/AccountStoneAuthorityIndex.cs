using System;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.Identity
{
    // Aggregate 2 — AccountStoneAuthorityIndex (data-model.md §"Aggregate 2"). One server-owned
    // authority record per (AccountId, StoneId). This index owns NO gameplay balance or outcome — it
    // is pure authority: which one character on this account, if any, actively holds a relationship to
    // this Stone, and the provenance of the activation/release.
    //
    // T004 envelope scope: persist the authoritative owner + revision + provenance. The Homestead
    // sibling-exclusivity ENFORCEMENT that consults this index lands in T007 (relationships); here we
    // only round-trip its state.
    //
    // net48 audit: engine-free value objects + snapshot codec. Link-compiles into net8 tests.

    public enum RelationshipKind
    {
        None = 0,
        Bond = 1,
        Attunement = 2
    }

    public sealed class AccountStoneAuthorityIndex
    {
        public const int CurrentSchemaVersion = 1;

        public AccountStoneAuthorityIndex(
            AccountId account,
            StoneId stoneId,
            long revision,
            CharacterId activeCharacter,
            RelationshipKind activeKind,
            string activeRelationshipId,
            string activationReceiptId,
            string releaseReceiptId,
            int schemaVersion = CurrentSchemaVersion)
        {
            Account = account;
            StoneId = stoneId;
            Revision = revision;
            ActiveCharacter = activeCharacter;
            ActiveKind = activeKind;
            ActiveRelationshipId = activeRelationshipId ?? string.Empty;
            ActivationReceiptId = activationReceiptId ?? string.Empty;
            ReleaseReceiptId = releaseReceiptId ?? string.Empty;
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
        public AccountId Account { get; }
        public StoneId StoneId { get; }
        public long Revision { get; }

        /// <summary>Currently active character holding a relationship to this Stone for this account,
        /// or the empty CharacterId when none. When <see cref="ActiveKind"/> is None this is vacant.</summary>
        public CharacterId ActiveCharacter { get; }
        public RelationshipKind ActiveKind { get; }
        public string ActiveRelationshipId { get; }

        // Activation/release receipt provenance
        public string ActivationReceiptId { get; }
        public string ReleaseReceiptId { get; }

        public bool IsVacant => ActiveKind == RelationshipKind.None;

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("account", Account.Value)
            .Put("stoneId", StoneId.Value)
            .PutLong("revision", Revision)
            .Put("activeChar", ActiveCharacter.Value)
            .PutInt("activeKind", (int)ActiveKind)
            .Put("relId", ActiveRelationshipId)
            .Put("activationReceipt", ActivationReceiptId)
            .Put("releaseReceipt", ReleaseReceiptId)
            .Build();

        public static AccountStoneAuthorityIndex Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new AccountStoneAuthorityIndex(
                new AccountId(r.GetString("account")),
                new StoneId(r.GetString("stoneId")),
                r.GetLong("revision"),
                new CharacterId(r.GetString("activeChar")),
                (RelationshipKind)r.GetInt("activeKind"),
                r.GetString("relId"),
                r.GetString("activationReceipt"),
                r.GetString("releaseReceipt"),
                r.GetInt("schema"));
        }

        public bool StructurallyEquals(AccountStoneAuthorityIndex o)
        {
            if (o == null) return false;
            return SchemaVersion == o.SchemaVersion
                   && Account.Equals(o.Account)
                   && StoneId.Equals(o.StoneId)
                   && Revision == o.Revision
                   && ActiveCharacter.Equals(o.ActiveCharacter)
                   && ActiveKind == o.ActiveKind
                   && string.Equals(ActiveRelationshipId, o.ActiveRelationshipId, StringComparison.Ordinal)
                   && string.Equals(ActivationReceiptId, o.ActivationReceiptId, StringComparison.Ordinal)
                   && string.Equals(ReleaseReceiptId, o.ReleaseReceiptId, StringComparison.Ordinal);
        }
    }
}
