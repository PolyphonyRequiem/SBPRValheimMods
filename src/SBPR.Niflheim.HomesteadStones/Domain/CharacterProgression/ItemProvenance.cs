using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // ============================================================================
    // T022 (Tracer 6, Crafting node 2 of 3) — Masterwork exact-instance Workmanship
    // issuance: the ENGINE-FREE item-provenance codec + integrity boundary.
    //
    // Spec (§Acceptance scenario 2 "Crafting"): "Masterwork issues one deterministic
    // visible validated Workmanship Property on an eligible non-stackable durable item
    // while active." contracts.md §Crafting: "WorkmanshipIssuanceProvider: active
    // Masterwork may issue one deterministic property on an eligible exact non-stackable
    // durable output" and "Both item providers bind a server-validated ItemProvenanceId,
    // survive upgrade/transfer where valid, explicitly dirty persistence, and degrade
    // tampered/unknown metadata to vanilla behavior." research.md line 137: "Exact
    // ItemData.m_customData survives clone/inventory/drop/container transfer | Upgrade
    // forwarding, tamper validation, dirty/save signaling, non-stackable restriction,
    // safe fallback."
    //
    // This file mirrors the accepted Stone provenance codec (Domain/HomesteadProvenance.cs):
    // it operates over an ABSTRACT key/value metadata surface (IItemMetadataWriter /
    // IItemMetadataReader) that the net48 adapter implements over the real
    // ItemDrop.ItemData.m_customData string dictionary, and tests implement over an
    // in-memory dictionary so the EXACT stamp/read/validate code runs headless.
    //
    // Tamper resistance is a server-keyed HMAC-SHA-256 integrity token over the canonical,
    // length-framed provenance fields (the same domain-separated, length-prefixed framing
    // the identity LookupKeyRing uses). A client can copy the visible property strings, but
    // cannot forge the token without the server key — so a hand-edited / unknown / partial
    // stamp fails validation and DEGRADES TO VANILLA (no Workmanship, plain item), never a
    // trusted forged property.
    //
    // Crucially the integrity token binds ONLY the immutable provenance identity (schema,
    // issuing node, provenance id, crafter, item type, the one Workmanship property). It
    // deliberately does NOT bind mutable per-instance facts (quality/upgrade level,
    // durability, stack, world position) so a legitimate UPGRADE or TRANSFER — which changes
    // those mutable facts but preserves m_customData — keeps validating (AT-ITEM-UPGRADE-
    // PRESERVE / AT-ITEM-TRANSFER).
    //
    // net48 audit: System.* + System.Security.Cryptography.HMACSHA256 + Encoding.UTF8 +
    // engine-free VersionedId — all exist in .NET Framework 4.8. No UnityEngine/Valheim/
    // BepInEx, so this link-compiles into the net8 test project.
    // ============================================================================

    /// <summary>Abstract owner-only writer over one item's persisted custom-data string map. The net48
    /// adapter implements this over <c>ItemDrop.ItemData.m_customData</c>; tests implement it over an
    /// in-memory dictionary so the SAME stamp code runs headless.</summary>
    public interface IItemMetadataWriter
    {
        void SetString(string key, string value);
        void Remove(string key);
    }

    /// <summary>Abstract reader over one item's persisted custom-data string map. Mirrors
    /// <see cref="IItemMetadataWriter"/> so a stamp can be read back and validated through the same
    /// abstraction the runtime reads through.</summary>
    public interface IItemMetadataReader
    {
        /// <summary>Return the value for <paramref name="key"/> or <paramref name="missing"/> when absent.</summary>
        string GetString(string key, string missing);
        bool Contains(string key);
    }

    /// <summary>A server-validated, exact-instance provenance identifier bound onto an item's custom data.
    /// Stable across upgrade/transfer; it is minted by the server at issuance and is the anchor the
    /// integrity token protects. Never author it from a client payload.</summary>
    public readonly struct ItemProvenanceId : IEquatable<ItemProvenanceId>
    {
        public ItemProvenanceId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);
        public bool Equals(ItemProvenanceId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ItemProvenanceId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>The ONE deterministic visible Workmanship Property an active Masterwork issues. It is a
    /// single named seal (name + value) — NOT a random roll and NOT a catalog of tiers (the spec defers a
    /// "final Workmanship catalog" to future work). "Deterministic" means: the same issuance inputs always
    /// produce this exact property, with no RNG, so a replayed issuance is idempotent.</summary>
    public readonly struct WorkmanshipProperty : IEquatable<WorkmanshipProperty>
    {
        public WorkmanshipProperty(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }

        /// <summary>The visible property name (e.g. "Workmanship").</summary>
        public string Name { get; }

        /// <summary>The visible property value/seal (e.g. "Masterwork"). One deterministic value.</summary>
        public string Value { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(Value);

        public bool Equals(WorkmanshipProperty other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is WorkmanshipProperty other && Equals(other);
        public override int GetHashCode() =>
            (StringComparer.Ordinal.GetHashCode(Name ?? string.Empty) * 397) ^
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
    }

    /// <summary>The complete Workmanship provenance fact stamped onto an eligible item: schema version,
    /// the issuing node (Masterwork@1), the server-minted <see cref="ItemProvenanceId"/>, the crafter
    /// account that issued it, the exact item type (prefab) it was issued on, and the one Workmanship
    /// property. Two stamps are equal only when EVERY immutable field matches — the integrity token is
    /// computed over exactly these fields, so validation compares the whole fact.</summary>
    public readonly struct WorkmanshipStamp : IEquatable<WorkmanshipStamp>
    {
        public WorkmanshipStamp(
            int schemaVersion,
            VersionedId issuingNode,
            ItemProvenanceId provenanceId,
            string crafterAccount,
            string itemType,
            WorkmanshipProperty property)
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

        /// <summary>The internal account id of the crafter who issued this Workmanship (server-minted; never
        /// a raw provider subject). Visible provenance, part of the integrity-protected fact.</summary>
        public string CrafterAccount { get; }

        /// <summary>The exact item type (prefab id) the Workmanship was issued on. Bound so a stamp lifted
        /// off one item type and pasted onto a different one fails validation.</summary>
        public string ItemType { get; }

        public WorkmanshipProperty Property { get; }

        public bool Equals(WorkmanshipStamp other) =>
            SchemaVersion == other.SchemaVersion &&
            IssuingNode.Equals(other.IssuingNode) &&
            ProvenanceId.Equals(other.ProvenanceId) &&
            string.Equals(CrafterAccount, other.CrafterAccount, StringComparison.Ordinal) &&
            string.Equals(ItemType, other.ItemType, StringComparison.Ordinal) &&
            Property.Equals(other.Property);
        public override bool Equals(object? obj) => obj is WorkmanshipStamp other && Equals(other);
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

    /// <summary>The outcome of reading an item's Workmanship stamp through the codec. Absent (no stamp
    /// keys), Valid (a complete stamp whose integrity token verifies under the server key), or Tampered
    /// (stamp keys present but the token is missing/forged/partial, or the schema is unknown). Both Absent
    /// and Tampered degrade to vanilla item behaviour — the difference exists so the runtime can log a
    /// tamper distinctly and so tests can prove the tamper path.</summary>
    public enum WorkmanshipReadState
    {
        Absent = 0,
        Valid,
        Tampered
    }

    /// <summary>The typed result of a codec read: the state plus, when Valid, the recovered stamp.</summary>
    public readonly struct WorkmanshipReadResult
    {
        private WorkmanshipReadResult(WorkmanshipReadState state, WorkmanshipStamp stamp)
        {
            State = state;
            Stamp = stamp;
        }

        public WorkmanshipReadState State { get; }
        public WorkmanshipStamp Stamp { get; }

        public bool IsValid => State == WorkmanshipReadState.Valid;

        internal static WorkmanshipReadResult Valid(WorkmanshipStamp stamp) =>
            new WorkmanshipReadResult(WorkmanshipReadState.Valid, stamp);
        internal static readonly WorkmanshipReadResult Absent =
            new WorkmanshipReadResult(WorkmanshipReadState.Absent, default);
        internal static readonly WorkmanshipReadResult Tampered =
            new WorkmanshipReadResult(WorkmanshipReadState.Tampered, default);
    }

    /// <summary>The server-held integrity key that signs and validates a Workmanship stamp. A full-length
    /// HMAC-SHA-256 over the canonical, length-framed immutable provenance fields (the same framing the
    /// identity <c>LookupKeyRing</c> uses). The raw key lives only server-side; a client can read the
    /// visible property strings but cannot forge the token, so a hand-edited stamp fails validation.</summary>
    public sealed class WorkmanshipIntegrityKey
    {
        /// <summary>Minimum key length in bits. 256 bits == 32 bytes.</summary>
        public const int MinKeyBits = 256;

        private readonly byte[] _key;

        public WorkmanshipIntegrityKey(byte[] keyBytes)
        {
            if (keyBytes == null) throw new ArgumentNullException(nameof(keyBytes));
            if (keyBytes.Length * 8 < MinKeyBits)
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "Workmanship integrity key must be at least {0} bits; got {1}.", MinKeyBits, keyBytes.Length * 8),
                    nameof(keyBytes));
            _key = (byte[])keyBytes.Clone();
        }

        /// <summary>Mint a fresh 256-bit random integrity key.</summary>
        public static WorkmanshipIntegrityKey Generate()
        {
            byte[] bytes = new byte[MinKeyBits / 8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return new WorkmanshipIntegrityKey(bytes);
        }

        /// <summary>Compute the lowercase-hex HMAC-SHA-256 token over the canonical stamp bytes.</summary>
        internal string ComputeToken(byte[] canonicalMessage)
        {
            using (var hmac = new HMACSHA256(_key))
            {
                byte[] mac = hmac.ComputeHash(canonicalMessage);
                var sb = new StringBuilder(mac.Length * 2);
                foreach (byte b in mac) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    /// <summary>The engine-free Workmanship stamp/read/validate codec. The net48 issuance/transfer seam
    /// delegates to <see cref="Stamp"/> and <see cref="Read"/>; both operate over the abstract item
    /// metadata surface so the exact production stamp + integrity validation are exercised headless.</summary>
    public static class WorkmanshipCodec
    {
        // Canonical custom-data key names, domain-prefixed like the Stone provenance codec. These are the
        // durable ItemData.m_customData keys the net48 adapter writes.
        internal const string Prefix = "niflheim.workmanship.";
        internal const string SchemaKey = Prefix + "schema";
        internal const string NodeKeyKey = Prefix + "node_key";
        internal const string NodeVersionKey = Prefix + "node_ver";
        internal const string ProvenanceIdKey = Prefix + "prov_id";
        internal const string CrafterKey = Prefix + "crafter";
        internal const string ItemTypeKey = Prefix + "item_type";
        internal const string PropertyNameKey = Prefix + "prop_name";
        internal const string PropertyValueKey = Prefix + "prop_value";
        internal const string IntegrityTokenKey = Prefix + "token";

        /// <summary>Current stamp schema. A stamp read under a different schema is treated as Tampered
        /// (unknown metadata) and degrades to vanilla rather than being trusted.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Whether an item is eligible to receive a Workmanship stamp: it must be NON-STACKABLE
        /// (an exact instance, not one of a fungible pile) AND DURABLE (has a max-durability / can wear —
        /// arrows, food, stackable materials are excluded). The non-stackable restriction is load-bearing:
        /// a stack shares one ItemData, so a per-instance provenance stamp on a stack is meaningless.</summary>
        public static bool IsEligible(bool nonStackable, bool durable) => nonStackable && durable;

        /// <summary>Persist the full Workmanship stamp onto an item's custom data via the abstract writer,
        /// including the server-keyed integrity token over the immutable fields. Every field is written so a
        /// read-back can detect a partial/torn write. The caller is responsible for EXPLICITLY dirtying the
        /// item's persistence after this returns (the net48 seam raises the save/replication flag).</summary>
        public static void Stamp(IItemMetadataWriter writer, WorkmanshipStamp stamp, WorkmanshipIntegrityKey integrity)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));

            writer.SetString(SchemaKey, stamp.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            writer.SetString(NodeKeyKey, stamp.IssuingNode.Key);
            writer.SetString(NodeVersionKey, stamp.IssuingNode.Version.ToString(CultureInfo.InvariantCulture));
            writer.SetString(ProvenanceIdKey, stamp.ProvenanceId.Value);
            writer.SetString(CrafterKey, stamp.CrafterAccount);
            writer.SetString(ItemTypeKey, stamp.ItemType);
            writer.SetString(PropertyNameKey, stamp.Property.Name);
            writer.SetString(PropertyValueKey, stamp.Property.Value);
            writer.SetString(IntegrityTokenKey, integrity.ComputeToken(Canonical(stamp)));
        }

        /// <summary>Read and VALIDATE an item's Workmanship stamp. Returns Absent when no stamp keys are
        /// present at all; Tampered when the stamp is present but its schema is unknown, a required field is
        /// missing, or the integrity token does not verify under the server key (a forged/hand-edited/partial
        /// stamp); Valid (with the recovered stamp) only when every field is present, the schema matches, and
        /// the token verifies. The runtime treats anything but Valid as a plain vanilla item.</summary>
        public static WorkmanshipReadResult Read(IItemMetadataReader reader, WorkmanshipIntegrityKey integrity)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));

            // No provenance id and no token => this item was never stamped. Absent, not tampered.
            bool anyKey = reader.Contains(ProvenanceIdKey) || reader.Contains(IntegrityTokenKey)
                || reader.Contains(SchemaKey);
            if (!anyKey) return WorkmanshipReadResult.Absent;

            // From here the item CLAIMS to carry a stamp. Any structural defect => Tampered (degrade).
            string schemaRaw = reader.GetString(SchemaKey, string.Empty);
            if (!int.TryParse(schemaRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema)
                || schema != SchemaVersion)
                return WorkmanshipReadResult.Tampered;

            string nodeKey = reader.GetString(NodeKeyKey, string.Empty);
            string nodeVerRaw = reader.GetString(NodeVersionKey, string.Empty);
            string provId = reader.GetString(ProvenanceIdKey, string.Empty);
            string crafter = reader.GetString(CrafterKey, string.Empty);
            string itemType = reader.GetString(ItemTypeKey, string.Empty);
            string propName = reader.GetString(PropertyNameKey, string.Empty);
            string propValue = reader.GetString(PropertyValueKey, string.Empty);
            string token = reader.GetString(IntegrityTokenKey, string.Empty);

            if (!int.TryParse(nodeVerRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nodeVer))
                return WorkmanshipReadResult.Tampered;
            if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(provId) || string.IsNullOrEmpty(token))
                return WorkmanshipReadResult.Tampered;

            var stamp = new WorkmanshipStamp(schema, new VersionedId(nodeKey, nodeVer),
                new ItemProvenanceId(provId), crafter, itemType, new WorkmanshipProperty(propName, propValue));

            // Recompute the token over the recovered immutable fields and compare in fixed time. A forged
            // property, a lifted-and-pasted stamp, or a truncated write cannot match the server key.
            string expected = integrity.ComputeToken(Canonical(stamp));
            if (!FixedTimeEquals(expected, token))
                return WorkmanshipReadResult.Tampered;

            return WorkmanshipReadResult.Valid(stamp);
        }

        /// <summary>Length-prefixed, field-count-framed canonical encoding of the immutable stamp fields.
        /// Mutable per-instance facts (quality/durability/stack) are deliberately excluded so a legitimate
        /// upgrade/transfer keeps validating. Matches the framing discipline of the identity codec so two
        /// distinct field tuples can never alias onto the same message bytes.</summary>
        internal static byte[] Canonical(WorkmanshipStamp stamp)
        {
            var fields = new[]
            {
                "workmanship-v1",
                stamp.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                stamp.IssuingNode.Key,
                stamp.IssuingNode.Version.ToString(CultureInfo.InvariantCulture),
                stamp.ProvenanceId.Value,
                stamp.CrafterAccount,
                stamp.ItemType,
                stamp.Property.Name,
                stamp.Property.Value,
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

        /// <summary>Ordinal, length-independent-leak-resistant string comparison for the hex token.</summary>
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
