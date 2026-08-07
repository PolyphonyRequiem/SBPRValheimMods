using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // ============================================================================
    // T023 (Tracer 6, Crafting node 3 of 3) — Built to Last durable FUTURE-output
    // provenance: the ENGINE-FREE item-provenance codec for the maximum-durability
    // property.
    //
    // Spec (§Acceptance scenario 2 "Crafting"): "Built to Last permanently improves
    // maximum durability on future eligible outputs with exact-item provenance."
    // contracts.md §Crafting: "DurabilityIssuanceProvider: acquired Built to Last
    // supplies the configured maximum-durability property on future eligible outputs
    // after relationship loss as well." and "Both item providers bind a server-validated
    // ItemProvenanceId, survive upgrade/transfer where valid, explicitly dirty
    // persistence, and degrade tampered/unknown metadata to vanilla behavior."
    // data-model.md fixed roster: "Crafting | 1 | Built to Last | Permanent Effect |
    // personal Offered | executable".
    //
    // This is the SIBLING of the shipped T022 WorkmanshipCodec and deliberately mirrors
    // its shape (abstract IItemMetadataWriter/IItemMetadataReader surface; server-keyed
    // HMAC integrity token over canonical, length-framed IMMUTABLE fields only; Absent /
    // Valid / Tampered read states that degrade to vanilla). Two deliberate differences:
    //
    //   * DOMAIN SEPARATION. The canonical message is prefixed "builttolast-v1" and the
    //     custom-data keys live under "niflheim.durability." — so a Workmanship token can
    //     never be replayed as a durability token (or vice versa) even though both are
    //     signed with the SAME server-owned WorkmanshipIntegrityKey. One server secret,
    //     two domain-separated messages; that is exactly what the length-framed prefix is
    //     for. (Reusing the key type avoids a second key file / second rotation surface.)
    //
    //   * THE STAMP CARRIES A VALUE, NOT JUST A SEAL. Masterwork's property is one named
    //     seal ("Workmanship=Masterwork"); Built to Last's property is the configured
    //     maximum-durability FACTOR that was in force at issuance, frozen onto the exact
    //     instance. Freezing the factor into the SIGNED stamp is what makes the effect
    //     "durable future-output provenance": the item keeps the improvement it was issued
    //     with forever, independent of the crafter's later relationship state, and a later
    //     retune of the configured factor cannot reach back and rewrite already-crafted
    //     items (the retroactive-mutation trap this card exists to avoid).
    //
    // NO RETROACTIVE MUTATION is a property of the READ path, not just the write path: the
    // effective maximum durability of an item is derived ONLY from the stamp the instance
    // actually carries. An unstamped item — crafted before the effect was acquired, or by
    // someone who never had it — reads Absent and gets the vanilla maximum, forever, with
    // zero writes. Nothing in this codec can widen an existing item's durability.
    //
    // net48 audit: System.* + Encoding.UTF8 + the engine-free VersionedId /
    // ItemProvenanceId / WorkmanshipIntegrityKey. No UnityEngine/Valheim/BepInEx, so this
    // link-compiles into the net8 test project exactly like its T022 sibling.
    // ============================================================================

    /// <summary>The ONE deterministic maximum-durability property an acquired Built to Last issues onto an
    /// eligible output: a multiplicative factor over the item's vanilla maximum durability, frozen at
    /// issuance time. Deterministic — no RNG — so a replayed issuance produces the identical stamp.</summary>
    public readonly struct DurabilityProperty : IEquatable<DurabilityProperty>
    {
        /// <summary>The neutral factor (vanilla maximum durability, unchanged).</summary>
        public const double NeutralFactor = 1.0;

        public DurabilityProperty(double factor)
        {
            Factor = factor;
        }

        /// <summary>The multiplicative maximum-durability factor frozen onto the instance. Always &gt;= 1.0 for
        /// an issued stamp — Built to Last IMPROVES durability and can never reduce it.</summary>
        public double Factor { get; }

        /// <summary>Whether this property actually improves anything (factor strictly above vanilla).</summary>
        public bool Improves => Factor > NeutralFactor;

        /// <summary>The inert property: vanilla maximum durability.</summary>
        public static readonly DurabilityProperty Neutral = new DurabilityProperty(NeutralFactor);

        public bool Equals(DurabilityProperty other) => Factor.Equals(other.Factor);
        public override bool Equals(object? obj) => obj is DurabilityProperty other && Equals(other);
        public override int GetHashCode() => Factor.GetHashCode();
        public override string ToString() =>
            Factor.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The complete Built to Last provenance fact stamped onto an eligible item: schema version, the
    /// issuing node (BuiltToLast@1), the server-minted <see cref="ItemProvenanceId"/>, the crafter account, the
    /// exact item type it was issued on, and the frozen maximum-durability property. The integrity token is
    /// computed over exactly these immutable fields — mutable per-instance facts (current durability, quality,
    /// stack, position) are deliberately NOT bound, so a legitimate upgrade or transfer keeps validating.</summary>
    public readonly struct DurabilityStamp : IEquatable<DurabilityStamp>
    {
        public DurabilityStamp(
            int schemaVersion,
            VersionedId issuingNode,
            ItemProvenanceId provenanceId,
            string crafterAccount,
            string itemType,
            DurabilityProperty property)
        {
            SchemaVersion = schemaVersion;
            IssuingNode = issuingNode;
            ProvenanceId = provenanceId;
            CrafterAccount = crafterAccount ?? string.Empty;
            ItemType = itemType ?? string.Empty;
            Property = property;
        }

        public int SchemaVersion { get; }
        public VersionedId IssuingNode { get; }
        public ItemProvenanceId ProvenanceId { get; }

        /// <summary>The internal account id of the crafter this was issued for (server-minted, never a raw
        /// provider subject). Visible provenance, part of the integrity-protected fact.</summary>
        public string CrafterAccount { get; }

        /// <summary>The exact item type (prefab id) the property was issued on. Bound so a stamp lifted off one
        /// item type and pasted onto another fails validation.</summary>
        public string ItemType { get; }

        public DurabilityProperty Property { get; }

        public bool Equals(DurabilityStamp other) =>
            SchemaVersion == other.SchemaVersion &&
            IssuingNode.Equals(other.IssuingNode) &&
            ProvenanceId.Equals(other.ProvenanceId) &&
            string.Equals(CrafterAccount, other.CrafterAccount, StringComparison.Ordinal) &&
            string.Equals(ItemType, other.ItemType, StringComparison.Ordinal) &&
            Property.Equals(other.Property);
        public override bool Equals(object? obj) => obj is DurabilityStamp other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SchemaVersion;
                hash = (hash * 397) ^ IssuingNode.GetHashCode();
                hash = (hash * 397) ^ ProvenanceId.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(CrafterAccount ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ItemType ?? string.Empty);
                return (hash * 397) ^ Property.GetHashCode();
            }
        }
    }

    /// <summary>The outcome of reading an item's Built to Last stamp: Absent (never stamped — vanilla maximum
    /// durability), Valid (complete stamp whose token verifies under the server key), or Tampered (stamp keys
    /// present but torn / unknown schema / forged token). Both Absent and Tampered degrade to vanilla; the
    /// distinction exists so the runtime can log a tamper and tests can prove that path.</summary>
    public enum DurabilityReadState
    {
        Absent = 0,
        Valid,
        Tampered
    }

    /// <summary>The typed result of a durability codec read: the state plus, when Valid, the recovered stamp.</summary>
    public readonly struct DurabilityReadResult
    {
        private DurabilityReadResult(DurabilityReadState state, DurabilityStamp stamp)
        {
            State = state;
            Stamp = stamp;
        }

        public DurabilityReadState State { get; }
        public DurabilityStamp Stamp { get; }
        public bool IsValid => State == DurabilityReadState.Valid;

        internal static DurabilityReadResult Valid(DurabilityStamp stamp) =>
            new DurabilityReadResult(DurabilityReadState.Valid, stamp);
        internal static readonly DurabilityReadResult Absent =
            new DurabilityReadResult(DurabilityReadState.Absent, default);
        internal static readonly DurabilityReadResult Tampered =
            new DurabilityReadResult(DurabilityReadState.Tampered, default);
    }

    /// <summary>The engine-free Built to Last stamp/read/validate codec. The net48 issuance and
    /// maximum-durability seams delegate to <see cref="Stamp"/> / <see cref="Read"/>; both operate over the same
    /// abstract item-metadata surface the T022 Workmanship codec uses, so the exact production stamp and
    /// integrity validation run headless in tests.</summary>
    public static class DurabilityCodec
    {
        // Canonical custom-data key names, domain-prefixed and DISJOINT from the Workmanship key set so the two
        // provenances coexist on one item without interfering.
        internal const string Prefix = "niflheim.durability.";
        internal const string SchemaKey = Prefix + "schema";
        internal const string NodeKeyKey = Prefix + "node_key";
        internal const string NodeVersionKey = Prefix + "node_ver";
        internal const string ProvenanceIdKey = Prefix + "prov_id";
        internal const string CrafterKey = Prefix + "crafter";
        internal const string ItemTypeKey = Prefix + "item_type";
        internal const string FactorKey = Prefix + "factor";
        internal const string IntegrityTokenKey = Prefix + "token";

        /// <summary>The domain-separation label bound as the FIRST canonical field. Distinct from the
        /// Workmanship codec's "workmanship-v1", so the same server key produces disjoint token spaces and a
        /// Workmanship token can never be replayed as a durability token.</summary>
        internal const string CanonicalDomain = "builttolast-v1";

        /// <summary>Current stamp schema. A stamp read under a different schema is Tampered (unknown metadata)
        /// and degrades to vanilla rather than being trusted.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The complete ordered set of custom-data keys a Built to Last stamp occupies. Used by the
        /// upgrade carry-forward seam to lift the EXACT stamp map off an upgraded source and restore it
        /// byte-for-byte onto the replacement.</summary>
        internal static readonly string[] StampKeys =
        {
            SchemaKey, NodeKeyKey, NodeVersionKey, ProvenanceIdKey, CrafterKey,
            ItemTypeKey, FactorKey, IntegrityTokenKey,
        };

        /// <summary>Whether an item is eligible to receive a maximum-durability property: it must be
        /// NON-STACKABLE (an exact instance, not one of a fungible pile) AND DURABLE (it actually HAS a maximum
        /// durability — a non-durable item has nothing to improve). Identical to the Workmanship eligibility
        /// predicate by construction: both are exact-instance item provenances.</summary>
        public static bool IsEligible(bool nonStackable, bool durable) => nonStackable && durable;

        /// <summary>Persist the full Built to Last stamp onto an item's custom data via the abstract writer,
        /// including the server-keyed integrity token over the immutable fields. The caller EXPLICITLY dirties
        /// the item's persistence after this returns (the net48 seam raises the save/replication flag).</summary>
        public static void Stamp(IItemMetadataWriter writer, DurabilityStamp stamp, WorkmanshipIntegrityKey integrity)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));
            WriteSigned(writer, stamp, integrity.ComputeToken(Canonical(stamp)));
        }

        /// <summary>Read and VALIDATE an item's Built to Last stamp under the server key. Absent when no stamp
        /// keys are present; Tampered when present but torn / unknown schema / forged token; Valid (with the
        /// recovered stamp) only when every field is present, the schema matches, and the token verifies. The
        /// runtime treats anything but Valid as a plain vanilla item — no durability improvement.</summary>
        public static DurabilityReadResult Read(IItemMetadataReader reader, WorkmanshipIntegrityKey integrity)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));

            var parse = TryReadRaw(reader, out var stamp, out string token);
            if (parse == RawReadState.Absent) return DurabilityReadResult.Absent;
            if (parse == RawReadState.Malformed) return DurabilityReadResult.Tampered;

            return Validate(stamp, token, integrity) == DurabilityReadState.Valid
                ? DurabilityReadResult.Valid(stamp)
                : DurabilityReadResult.Tampered;
        }

        /// <summary>The outcome of the keyless structural parse in <see cref="TryReadRaw"/>: no stamp keys at all
        /// (Absent), a present-but-torn/unknown-schema stamp (Malformed — degrades to vanilla with no key), or a
        /// well-formed stamp whose fields + token were recovered (Present — still UNVALIDATED).</summary>
        public enum RawReadState
        {
            Absent = 0,
            Malformed,
            Present
        }

        /// <summary>KEYLESS structural read — the read a pure client performs. Recovers the immutable stamp
        /// fields + integrity token WITHOUT the server key, reporting only Absent / Malformed / Present. A
        /// well-formed stamp is NOT trusted here; only the key holder can <see cref="Validate"/> it.</summary>
        public static RawReadState TryReadRaw(IItemMetadataReader reader, out DurabilityStamp stamp, out string token)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            stamp = default;
            token = string.Empty;

            bool anyKey = reader.Contains(ProvenanceIdKey) || reader.Contains(IntegrityTokenKey)
                || reader.Contains(SchemaKey);
            if (!anyKey) return RawReadState.Absent;

            string schemaRaw = reader.GetString(SchemaKey, string.Empty);
            if (!int.TryParse(schemaRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema)
                || schema != SchemaVersion)
                return RawReadState.Malformed;

            string nodeKey = reader.GetString(NodeKeyKey, string.Empty);
            string nodeVerRaw = reader.GetString(NodeVersionKey, string.Empty);
            string provId = reader.GetString(ProvenanceIdKey, string.Empty);
            string crafter = reader.GetString(CrafterKey, string.Empty);
            string itemType = reader.GetString(ItemTypeKey, string.Empty);
            string factorRaw = reader.GetString(FactorKey, string.Empty);
            string tok = reader.GetString(IntegrityTokenKey, string.Empty);

            if (!int.TryParse(nodeVerRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nodeVer))
                return RawReadState.Malformed;
            if (!double.TryParse(factorRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor))
                return RawReadState.Malformed;
            if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(provId) || string.IsNullOrEmpty(tok))
                return RawReadState.Malformed;

            stamp = new DurabilityStamp(schema, new VersionedId(nodeKey, nodeVer),
                new ItemProvenanceId(provId), crafter, itemType, new DurabilityProperty(factor));
            token = tok;
            return RawReadState.Present;
        }

        /// <summary>SERVER-SIDE: sign a freshly minted stamp. The server sends the stamp fields + this token to
        /// the crafting client, which writes them verbatim via <see cref="WriteSigned"/>; the raw key never
        /// leaves the server.</summary>
        public static string Sign(DurabilityStamp stamp, WorkmanshipIntegrityKey integrity)
        {
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));
            return integrity.ComputeToken(Canonical(stamp));
        }

        /// <summary>SERVER-SIDE: validate a presented stamp + token under the server key. Valid only when the
        /// recomputed token matches; Tampered otherwise (forged / hand-edited / foreign-key / lifted-pasted /
        /// cross-domain replay of a Workmanship token).</summary>
        public static DurabilityReadState Validate(DurabilityStamp stamp, string token, WorkmanshipIntegrityKey integrity)
        {
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));
            string expected = integrity.ComputeToken(Canonical(stamp));
            return FixedTimeEquals(expected, token ?? string.Empty)
                ? DurabilityReadState.Valid
                : DurabilityReadState.Tampered;
        }

        /// <summary>CLIENT-SIDE: write a server-minted, server-SIGNED stamp using a PRE-COMPUTED token — no key
        /// required. The written stamp re-validates identically to a host-stamped one because the canonical
        /// fields + token are byte-identical.</summary>
        public static void WriteSigned(IItemMetadataWriter writer, DurabilityStamp stamp, string token)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("A signed stamp requires a token.", nameof(token));

            writer.SetString(SchemaKey, stamp.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            writer.SetString(NodeKeyKey, stamp.IssuingNode.Key);
            writer.SetString(NodeVersionKey, stamp.IssuingNode.Version.ToString(CultureInfo.InvariantCulture));
            writer.SetString(ProvenanceIdKey, stamp.ProvenanceId.Value);
            writer.SetString(CrafterKey, stamp.CrafterAccount);
            writer.SetString(ItemTypeKey, stamp.ItemType);
            writer.SetString(FactorKey, FormatFactor(stamp.Property.Factor));
            writer.SetString(IntegrityTokenKey, token);
        }

        /// <summary>Capture the EXACT persisted Built to Last custom-data map off a source item, verbatim, so an
        /// upgrade carry-forward can restore it byte-for-byte onto the replacement WITHOUT re-minting or
        /// re-signing. Empty when the source carries no durability stamp key at all.</summary>
        public static Dictionary<string, string> CaptureStamp(IItemMetadataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in StampKeys)
                if (reader.Contains(key))
                    map[key] = reader.GetString(key, string.Empty);
            return map;
        }

        /// <summary>Restore a captured durability map onto a replacement item byte-for-byte. Every durability key
        /// not in the captured map is REMOVED first so a partial/foreign residue cannot survive alongside the
        /// restored stamp. An empty captured map therefore clears any durability keys — a fresh vanilla
        /// replacement stays vanilla. No token is recomputed.</summary>
        public static void RestoreStamp(IItemMetadataWriter writer, IReadOnlyDictionary<string, string> captured)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (captured == null) throw new ArgumentNullException(nameof(captured));

            foreach (string key in StampKeys)
            {
                if (captured.TryGetValue(key, out string? value))
                    writer.SetString(key, value ?? string.Empty);
                else
                    writer.Remove(key);
            }
        }

        /// <summary>Whether a captured map carries any durability stamp key at all (i.e. the source was stamped).</summary>
        public static bool HasStamp(IReadOnlyDictionary<string, string> captured)
        {
            if (captured == null) return false;
            foreach (string key in StampKeys)
                if (captured.ContainsKey(key)) return true;
            return false;
        }

        /// <summary>A stable ordinal fingerprint of the COMPLETE signed durability stamp an item carries — every
        /// key AND value, length-framed so two distinct maps can never alias. A client-side verdict must be bound
        /// to this, never to the provenance id alone: the instant any signed field changes the fingerprint
        /// changes, so a cached Valid cannot be reused over mutated bytes (the exact stale-verdict hole the T022
        /// remediation closed for Workmanship).</summary>
        public static string Fingerprint(IItemMetadataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var sb = new StringBuilder();
            foreach (string key in StampKeys)
            {
                bool present = reader.Contains(key);
                sb.Append(key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(key).Append('=');
                if (!present) { sb.Append('-').Append(';'); continue; }
                string value = reader.GetString(key, string.Empty) ?? string.Empty;
                sb.Append('+').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Round-trip-exact invariant-culture rendering of the frozen factor. "R" guarantees the parsed
        /// value is bit-identical to the written one, so the recovered stamp re-signs to the same token.</summary>
        internal static string FormatFactor(double factor) =>
            factor.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Length-prefixed, field-count-framed canonical encoding of the immutable stamp fields, opened
        /// with the <see cref="CanonicalDomain"/> label. Mutable per-instance facts (current durability, quality,
        /// stack, position) are deliberately excluded so a legitimate upgrade/transfer keeps validating.</summary>
        internal static byte[] Canonical(DurabilityStamp stamp)
        {
            var fields = new[]
            {
                CanonicalDomain,
                stamp.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                stamp.IssuingNode.Key,
                stamp.IssuingNode.Version.ToString(CultureInfo.InvariantCulture),
                stamp.ProvenanceId.Value,
                stamp.CrafterAccount,
                stamp.ItemType,
                FormatFactor(stamp.Property.Factor),
            };
            using (var ms = new MemoryStream())
            {
                WriteInt(ms, fields.Length);
                foreach (string f in fields)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(f ?? string.Empty);
                    WriteInt(ms, bytes.Length);
                    ms.Write(bytes, 0, bytes.Length);
                }
                return ms.ToArray();
            }
        }

        private static void WriteInt(MemoryStream ms, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            ms.Write(b, 0, b.Length);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
