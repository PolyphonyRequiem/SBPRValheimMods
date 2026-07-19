using System;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.Crafting
{
    // ============================================================================
    // T022 remediation — the BOUNDED server↔client Masterwork Workmanship ISSUANCE +
    // VALIDATION delivery contract. This is the channel the T022 joined-client QA
    // (t_997667c4) proved missing: the shipped MasterworkIssuanceObserver only issues
    // on a listen-host (it needs BOTH the armed server integrity key AND the crafter to
    // be Player.m_localPlayer), so on an isolated dedicated-server topology neither
    // actor qualifies — the headless server has no local crafter and the pure joined
    // crafter is unarmed/keyless. And because the raw WorkmanshipIntegrityKey is
    // server-only, a joined receiver could not even VALIDATE a stamp for display or
    // distinguish legitimate/upgraded/transferred from forged.
    //
    // This contract fixes both WITHOUT ever shipping the raw integrity secret to a
    // client, mirroring the accepted PersonalActivationDelivery shape:
    //
    //   * ISSUANCE  — CLIENT→SERVER a bounded request carrying ONLY the server-observed
    //     produced-item facts (Stone id, exact item type, non-stackable/durable
    //     eligibility, whether the client already reads a well-formed stamp) plus a
    //     correlation id. The server re-derives the crafter's Masterwork activation from
    //     its OWN authoritative stores (transport-authenticated bound principal — never
    //     the payload), decides issuance through the shipped WorkmanshipIssuanceProvider,
    //     mints the deterministic stamp, and SIGNS it with the server key. SERVER→CLIENT
    //     a WorkmanshipIssuanceGrant carries the stamp FIELDS + the pre-computed HMAC
    //     token (never the key). The client writes the exact bytes via
    //     WorkmanshipCodec.WriteSigned — the persisted stamp re-validates byte-identically
    //     to a host-stamped one.
    //
    //   * VALIDATION — CLIENT→SERVER a bounded request carrying the stamp FIELDS + token
    //     it read keylessly (WorkmanshipCodec.TryReadRaw). SERVER→CLIENT a
    //     WorkmanshipValidationVerdict carries only Valid/Tampered (WorkmanshipCodec.
    //     Validate under the server key). This is what lets a joined receiver show a
    //     legitimate/transferred/upgraded stamp as confirmed and degrade a forged/foreign
    //     one to vanilla — again with the key staying server-side.
    //
    // Client claims are NEVER trusted as authority: issuance activation is re-derived
    // server-side from the bound principal, and the integrity token is computed/checked
    // only server-side. The worst a hostile client can do with the issuance request is
    // ask the server to issue onto an item the server INDEPENDENTLY confirms the client's
    // own active Masterwork entitles — the server refuses otherwise (fail closed). The
    // worst it can do with a validation request is learn whether a stamp it already holds
    // is genuine (a read it is entitled to) — it never learns the key.
    //
    // net48 audit: engine-free (System.* + snapshot codec + engine-free domain codec /
    // value objects). Link-compiles into the net8 test project exactly like the sibling
    // PersonalActivationDelivery.
    // ============================================================================

    /// <summary>CLIENT→SERVER: a bounded request to MINT a Workmanship stamp for a just-produced item. Carries
    /// ONLY server-observed produced-item facts and a correlation id — never any activation, key, or token.
    /// The server re-derives entitlement from its own stores keyed by the transport-authenticated principal.</summary>
    public readonly struct WorkmanshipIssuanceRequest
    {
        public WorkmanshipIssuanceRequest(StoneId stoneId, string correlationId, string itemType,
            bool nonStackable, bool durable, bool alreadyHasWellFormedStamp)
        {
            StoneId = stoneId;
            CorrelationId = correlationId ?? string.Empty;
            ItemType = itemType ?? string.Empty;
            NonStackable = nonStackable;
            Durable = durable;
            AlreadyHasWellFormedStamp = alreadyHasWellFormedStamp;
        }

        public StoneId StoneId { get; }

        /// <summary>An opaque client-minted correlation id (e.g. the produced item's ZDOID + a monotonic
        /// counter) so the client can match a grant back to the exact crafted instance. Never authority.</summary>
        public string CorrelationId { get; }

        public string ItemType { get; }
        public bool NonStackable { get; }
        public bool Durable { get; }

        /// <summary>Whether the client already reads a well-formed (possibly-unvalidated) stamp on the item.
        /// The server treats this as the idempotency hint the provider's AlreadyStamped gate consumes so a
        /// re-request on an already-issued instance is a no-grant no-op.</summary>
        public bool AlreadyHasWellFormedStamp { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("stone", StoneId.Value)
            .Put("corr", CorrelationId)
            .Put("itype", ItemType)
            .PutBool("nonstack", NonStackable)
            .PutBool("durable", Durable)
            .PutBool("stamped", AlreadyHasWellFormedStamp)
            .Build();

        public static WorkmanshipIssuanceRequest Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            return new WorkmanshipIssuanceRequest(
                new StoneId(r.GetString("stone")),
                r.GetString("corr"),
                r.GetString("itype"),
                r.GetBool("nonstack"),
                r.GetBool("durable"),
                r.GetBool("stamped"));
        }
    }

    /// <summary>SERVER→CLIENT: the outcome of an issuance request. When <see cref="ShouldWrite"/> is true it
    /// carries the exact minted <see cref="Stamp"/> fields and the server-computed integrity <see cref="Token"/>
    /// for the client to persist verbatim (WorkmanshipCodec.WriteSigned) — the key never travels. When false it
    /// carries the machine <see cref="Outcome"/> (not active / ineligible / already stamped) so the client
    /// leaves the item plain vanilla. The correlation id echoes the request so the client matches the instance.</summary>
    public readonly struct WorkmanshipIssuanceGrant
    {
        public WorkmanshipIssuanceGrant(string correlationId, bool shouldWrite,
            WorkmanshipIssuanceOutcomeCode outcome, WorkmanshipStamp stamp, string token)
        {
            CorrelationId = correlationId ?? string.Empty;
            ShouldWrite = shouldWrite;
            Outcome = outcome;
            Stamp = stamp;
            Token = token ?? string.Empty;
        }

        public string CorrelationId { get; }

        /// <summary>True only when the server minted + signed a stamp the client must write. False for every
        /// refusal — the client then leaves the item vanilla.</summary>
        public bool ShouldWrite { get; }

        public WorkmanshipIssuanceOutcomeCode Outcome { get; }
        public WorkmanshipStamp Stamp { get; }

        /// <summary>The server-computed HMAC integrity token for <see cref="Stamp"/>. Present only when
        /// <see cref="ShouldWrite"/>. The client writes it verbatim; it never receives the key.</summary>
        public string Token { get; }

        public static WorkmanshipIssuanceGrant Refused(string correlationId, WorkmanshipIssuanceOutcomeCode outcome) =>
            new WorkmanshipIssuanceGrant(correlationId, false, outcome, default, string.Empty);

        public string Serialize()
        {
            var w = new SnapshotWriter()
                .Put("corr", CorrelationId)
                .PutBool("write", ShouldWrite)
                .PutInt("outcome", (int)Outcome)
                .Put("token", Token)
                .PutInt("schema", Stamp.SchemaVersion)
                .Put("nodek", Stamp.IssuingNode.Key)
                .PutInt("nodev", Stamp.IssuingNode.Version)
                .Put("prov", Stamp.ProvenanceId.Value)
                .Put("crafter", Stamp.CrafterAccount)
                .Put("itype", Stamp.ItemType)
                .Put("pname", Stamp.Property.Name)
                .Put("pval", Stamp.Property.Value);
            return w.Build();
        }

        public static WorkmanshipIssuanceGrant Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            var stamp = new WorkmanshipStamp(
                r.GetInt("schema"),
                new VersionedId(r.GetString("nodek"), r.GetInt("nodev")),
                new ItemProvenanceId(r.GetString("prov")),
                r.GetString("crafter"),
                r.GetString("itype"),
                new WorkmanshipProperty(r.GetString("pname"), r.GetString("pval")));
            return new WorkmanshipIssuanceGrant(
                r.GetString("corr"),
                r.GetBool("write"),
                (WorkmanshipIssuanceOutcomeCode)r.GetInt("outcome"),
                stamp,
                r.GetString("token"));
        }
    }

    /// <summary>A stable, wire-safe mirror of <see cref="WorkmanshipIssuanceOutcome"/> so the grant carries a
    /// machine outcome for diagnostics/idempotency without depending on the adapter enum's ordinal layout.</summary>
    public enum WorkmanshipIssuanceOutcomeCode
    {
        Issue = 0,
        EffectNotActive = 1,
        IneligibleItem = 2,
        AlreadyStamped = 3,
        /// <summary>The server could not authoritatively resolve the requester (unbound peer, unknown Stone,
        /// or no composed runtime). Fail closed — the client leaves the item vanilla.</summary>
        Unresolved = 4
    }

    /// <summary>CLIENT→SERVER: a bounded request to VALIDATE a stamp the client read keylessly. Carries the
    /// recovered immutable stamp fields + the integrity token, and a correlation id. No key; the client learns
    /// only whether a stamp it already holds is genuine.</summary>
    public readonly struct WorkmanshipValidationRequest
    {
        public WorkmanshipValidationRequest(string correlationId, WorkmanshipStamp stamp, string token)
        {
            CorrelationId = correlationId ?? string.Empty;
            Stamp = stamp;
            Token = token ?? string.Empty;
        }

        public string CorrelationId { get; }
        public WorkmanshipStamp Stamp { get; }
        public string Token { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("corr", CorrelationId)
            .Put("token", Token)
            .PutInt("schema", Stamp.SchemaVersion)
            .Put("nodek", Stamp.IssuingNode.Key)
            .PutInt("nodev", Stamp.IssuingNode.Version)
            .Put("prov", Stamp.ProvenanceId.Value)
            .Put("crafter", Stamp.CrafterAccount)
            .Put("itype", Stamp.ItemType)
            .Put("pname", Stamp.Property.Name)
            .Put("pval", Stamp.Property.Value)
            .Build();

        public static WorkmanshipValidationRequest Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            var stamp = new WorkmanshipStamp(
                r.GetInt("schema"),
                new VersionedId(r.GetString("nodek"), r.GetInt("nodev")),
                new ItemProvenanceId(r.GetString("prov")),
                r.GetString("crafter"),
                r.GetString("itype"),
                new WorkmanshipProperty(r.GetString("pname"), r.GetString("pval")));
            return new WorkmanshipValidationRequest(r.GetString("corr"), stamp, r.GetString("token"));
        }
    }

    /// <summary>SERVER→CLIENT: the verdict for a validation request. Carries only the correlation id, the
    /// provenance id it concerns, and Valid/Tampered — never the key. The client uses it to present a confirmed
    /// Workmanship (Valid) or degrade to vanilla (Tampered / no verdict).</summary>
    public readonly struct WorkmanshipValidationVerdict
    {
        public WorkmanshipValidationVerdict(string correlationId, ItemProvenanceId provenanceId, bool valid)
        {
            CorrelationId = correlationId ?? string.Empty;
            ProvenanceId = provenanceId;
            Valid = valid;
        }

        public string CorrelationId { get; }
        public ItemProvenanceId ProvenanceId { get; }
        public bool Valid { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("corr", CorrelationId)
            .Put("prov", ProvenanceId.Value)
            .PutBool("valid", Valid)
            .Build();

        public static WorkmanshipValidationVerdict Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            return new WorkmanshipValidationVerdict(
                r.GetString("corr"),
                new ItemProvenanceId(r.GetString("prov")),
                r.GetBool("valid"));
        }
    }
}
