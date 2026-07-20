using System;
using System.Collections.Generic;
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

        /// <summary>The complete ordered set of custom-data keys a Workmanship stamp occupies. Used by the
        /// upgrade carry-forward seam to lift the EXACT stamp map off an upgraded source item and restore it
        /// byte-for-byte onto the replacement, and by nothing else — the codec reads individual keys directly.</summary>
        internal static readonly string[] StampKeys =
        {
            SchemaKey, NodeKeyKey, NodeVersionKey, ProvenanceIdKey, CrafterKey,
            ItemTypeKey, PropertyNameKey, PropertyValueKey, IntegrityTokenKey,
        };

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
        /// the token verifies. The runtime treats anything but Valid as a plain vanilla item.
        ///
        /// This is the AUTHORITATIVE-SIDE read: it needs the server integrity key. A pure client that holds
        /// no key uses <see cref="TryReadRaw"/> to recover the stamp fields + token WITHOUT validating and
        /// then asks the server to <see cref="Validate"/> them — the raw key never crosses the wire.</summary>
        public static WorkmanshipReadResult Read(IItemMetadataReader reader, WorkmanshipIntegrityKey integrity)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));

            // Keyless structural parse: Absent (never stamped), Malformed (present but torn/unknown schema),
            // or Present (well-formed, token recovered). Only a well-formed stamp is worth a key comparison.
            var parse = TryReadRaw(reader, out var stamp, out string token);
            if (parse == RawReadState.Absent) return WorkmanshipReadResult.Absent;
            if (parse == RawReadState.Malformed) return WorkmanshipReadResult.Tampered;

            // Recompute the token over the recovered immutable fields and compare in fixed time. A forged
            // property, a lifted-and-pasted stamp, or a truncated write cannot match the server key.
            return Validate(stamp, token, integrity) == WorkmanshipReadState.Valid
                ? WorkmanshipReadResult.Valid(stamp)
                : WorkmanshipReadResult.Tampered;
        }

        /// <summary>The outcome of the keyless structural parse in <see cref="TryReadRaw"/>: no stamp keys at
        /// all (Absent), a present-but-torn/unknown-schema stamp (Malformed — degrades to vanilla without any
        /// key), or a well-formed stamp whose fields + token were recovered (Present — still UNVALIDATED; only
        /// the server key can confirm it is genuine).</summary>
        public enum RawReadState
        {
            Absent = 0,
            Malformed,
            Present
        }

        /// <summary>KEYLESS structural read of an item's Workmanship stamp — the read a pure client performs.
        /// It recovers the immutable stamp fields + the integrity token WITHOUT the server key, reporting only
        /// whether the stamp is Absent, structurally Malformed, or well-formed (Present). A well-formed stamp
        /// is NOT trusted here: the client must hand <paramref name="stamp"/> + <paramref name="token"/> to the
        /// server for <see cref="Validate"/>, since only the server holds the key. This is what lets a joined
        /// client present/relay a stamp without ever receiving the secret.</summary>
        public static RawReadState TryReadRaw(IItemMetadataReader reader, out WorkmanshipStamp stamp, out string token)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            stamp = default;
            token = string.Empty;

            // No provenance id and no token and no schema => this item was never stamped. Absent, not torn.
            bool anyKey = reader.Contains(ProvenanceIdKey) || reader.Contains(IntegrityTokenKey)
                || reader.Contains(SchemaKey);
            if (!anyKey) return RawReadState.Absent;

            // From here the item CLAIMS to carry a stamp. Any structural defect => Malformed (degrade).
            string schemaRaw = reader.GetString(SchemaKey, string.Empty);
            if (!int.TryParse(schemaRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema)
                || schema != SchemaVersion)
                return RawReadState.Malformed;

            string nodeKey = reader.GetString(NodeKeyKey, string.Empty);
            string nodeVerRaw = reader.GetString(NodeVersionKey, string.Empty);
            string provId = reader.GetString(ProvenanceIdKey, string.Empty);
            string crafter = reader.GetString(CrafterKey, string.Empty);
            string itemType = reader.GetString(ItemTypeKey, string.Empty);
            string propName = reader.GetString(PropertyNameKey, string.Empty);
            string propValue = reader.GetString(PropertyValueKey, string.Empty);
            string tok = reader.GetString(IntegrityTokenKey, string.Empty);

            if (!int.TryParse(nodeVerRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nodeVer))
                return RawReadState.Malformed;
            if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(provId) || string.IsNullOrEmpty(tok))
                return RawReadState.Malformed;

            stamp = new WorkmanshipStamp(schema, new VersionedId(nodeKey, nodeVer),
                new ItemProvenanceId(provId), crafter, itemType, new WorkmanshipProperty(propName, propValue));
            token = tok;
            return RawReadState.Present;
        }

        /// <summary>SERVER-SIDE: sign a freshly minted stamp — the lowercase-hex HMAC-SHA-256 integrity token
        /// over the canonical immutable fields. The server sends the stamp fields + this token to the crafting
        /// client, which writes them verbatim via <see cref="WriteSigned"/>; the raw key never leaves the
        /// server. Equivalent to what <see cref="Stamp"/> computes, exposed so the mint/deliver seam can ship
        /// the token without shipping the key.</summary>
        public static string Sign(WorkmanshipStamp stamp, WorkmanshipIntegrityKey integrity)
        {
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));
            return integrity.ComputeToken(Canonical(stamp));
        }

        /// <summary>SERVER-SIDE: validate a client-presented stamp + token under the server key. Returns Valid
        /// only when the recomputed token matches in fixed time; Tampered otherwise (forged/hand-edited/foreign
        /// -key/lifted-pasted). The pure client relays the fields it read with <see cref="TryReadRaw"/> and the
        /// server answers this — the key stays server-side.</summary>
        public static WorkmanshipReadState Validate(WorkmanshipStamp stamp, string token, WorkmanshipIntegrityKey integrity)
        {
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));
            string expected = integrity.ComputeToken(Canonical(stamp));
            return FixedTimeEquals(expected, token ?? string.Empty)
                ? WorkmanshipReadState.Valid
                : WorkmanshipReadState.Tampered;
        }

        /// <summary>CLIENT-SIDE: write a server-minted, server-SIGNED stamp onto an item's custom data using a
        /// PRE-COMPUTED integrity <paramref name="token"/> — no key required. This is how a pure joined crafter
        /// persists the Workmanship the server minted for it: the server holds the key and computed the token
        /// (<see cref="Sign"/>); the client only records the exact bytes. The written stamp re-validates
        /// identically to a host-stamped one because the canonical fields + token are byte-identical.</summary>
        public static void WriteSigned(IItemMetadataWriter writer, WorkmanshipStamp stamp, string token)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("A signed stamp requires a token.", nameof(token));

            writer.SetString(SchemaKey, stamp.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            writer.SetString(NodeKeyKey, stamp.IssuingNode.Key);
            writer.SetString(NodeVersionKey, stamp.IssuingNode.Version.ToString(CultureInfo.InvariantCulture));
            writer.SetString(ProvenanceIdKey, stamp.ProvenanceId.Value);
            writer.SetString(CrafterKey, stamp.CrafterAccount);
            writer.SetString(ItemTypeKey, stamp.ItemType);
            writer.SetString(PropertyNameKey, stamp.Property.Name);
            writer.SetString(PropertyValueKey, stamp.Property.Value);
            writer.SetString(IntegrityTokenKey, token);
        }

        /// <summary>Capture the EXACT persisted Workmanship custom-data map off a source item, verbatim. Returns
        /// the complete server-signed key→value set (only the keys actually present) so the upgrade carry-forward
        /// seam can restore it byte-for-byte onto a replacement item WITHOUT re-minting or re-signing anything —
        /// the token, provenance id, and every field ride across the replacement unchanged. Returns an empty map
        /// when the source carries no stamp key at all (nothing to preserve). This lifts whatever is there without
        /// judging it: a well-formed valid stamp captures whole; the restore side is a pure copy.</summary>
        public static Dictionary<string, string> CaptureStamp(IItemMetadataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in StampKeys)
                if (reader.Contains(key))
                    map[key] = reader.GetString(key, string.Empty);
            return map;
        }

        /// <summary>Restore a captured Workmanship map (<see cref="CaptureStamp"/>) onto a replacement item's
        /// custom data, byte-for-byte. Every Workmanship key not in the captured map is first REMOVED from the
        /// target so a partial/foreign residue cannot survive alongside the restored stamp, then each captured
        /// key is written verbatim. An empty captured map therefore clears any Workmanship keys on the target —
        /// a fresh vanilla replacement stays vanilla. No token is recomputed: the restored stamp re-validates
        /// identically to the source because the canonical fields + token are the same bytes.</summary>
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

        /// <summary>Whether a captured stamp map carries any Workmanship key at all (i.e. the source was
        /// stamped). A carry-forward is only meaningful when this is true.</summary>
        public static bool HasStamp(IReadOnlyDictionary<string, string> captured)
        {
            if (captured == null) return false;
            foreach (string key in StampKeys)
                if (captured.ContainsKey(key)) return true;
            return false;
        }

        /// <summary>A stable, ordinal fingerprint of the COMPLETE signed stamp an item currently carries — every
        /// Workmanship key AND its value, length-framed so two distinct maps can never alias. This is what a
        /// client-side verdict must be bound to: a verdict cached for one fingerprint is meaningless the instant
        /// any signed field (including <c>prop_value</c>) changes while the provenance id/token are retained, so
        /// the presentation seam re-validates rather than reusing a stale Valid. Absent keys are framed distinctly
        /// from empty values. Returns the empty-string fingerprint for an unstamped item.</summary>
        public static string Fingerprint(IItemMetadataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var sb = new StringBuilder();
            foreach (string key in StampKeys)
            {
                bool present = reader.Contains(key);
                // Frame as <keyLen>:<key>=<present?><valLen>:<value> so "absent" and "empty" never collide and
                // no value can smuggle a separator to alias a different tuple.
                sb.Append(key.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(key).Append('=');
                if (!present) { sb.Append('-').Append(';'); continue; }
                string value = reader.GetString(key, string.Empty) ?? string.Empty;
                sb.Append('+').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
            }
            return sb.ToString();
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
