using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain.Snapshots
{
    // Engine-free, deterministic snapshot codec for the T004 versioned aggregate envelopes.
    //
    // Persistence contract (data-model.md, "This document defines logical contracts, not a storage
    // engine"): each authoritative aggregate serializes to a canonical, self-describing string and
    // rehydrates back to an equal value. Round-trip preserves every authoritative owner, revision,
    // stable identity, and provenance field (AT-STATE-ROUNDTRIP). This is NOT a storage engine — the
    // real durable substrate (journal/ZDO) is Gate-A's OperationReceiptStore; this codec is the pure
    // in-memory serialization seam the domain round-trip tests exercise.
    //
    // Format: newline-separated "key=base64(value)" lines. Values are base64-encoded so embedded
    // '=', '\n', or '|' cannot break framing. A list field writes "<key>.count=N" plus one
    // "<key>.<i>=base64(childSnapshot)" per element, where the child snapshot is itself a full
    // newline block. Deterministic key order in, order-independent lookup out.
    //
    // net48 audit: only System.Text / System.Collections.Generic / Convert.ToBase64String — all
    // present in .NET Framework 4.8. No net5+ surface, no UnityEngine/Valheim/BepInEx reference, so
    // this file link-compiles into the net8 test project.

    /// <summary>Accumulates key/value fields into a canonical snapshot string.</summary>
    public sealed class SnapshotWriter
    {
        private readonly List<string> _lines = new List<string>();

        public SnapshotWriter Put(string key, string? value)
        {
            _lines.Add(key + "=" + Encode(value ?? string.Empty));
            return this;
        }

        public SnapshotWriter PutInt(string key, int value) =>
            Put(key, value.ToString(CultureInfo.InvariantCulture));

        public SnapshotWriter PutLong(string key, long value) =>
            Put(key, value.ToString(CultureInfo.InvariantCulture));

        public SnapshotWriter PutBool(string key, bool value) =>
            Put(key, value ? "1" : "0");

        /// <summary>Write a list field. Each element is serialized to its own child snapshot string.</summary>
        public SnapshotWriter PutList<T>(string key, IReadOnlyList<T> items, Func<T, string> serializeItem)
        {
            PutInt(key + ".count", items.Count);
            for (int i = 0; i < items.Count; i++)
                Put(key + "." + i.ToString(CultureInfo.InvariantCulture), serializeItem(items[i]));
            return this;
        }

        public string Build() => string.Join("\n", _lines);

        internal static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    }

    /// <summary>Reads key/value fields back out of a canonical snapshot string.</summary>
    public sealed class SnapshotReader
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

        public SnapshotReader(string snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Length == 0) return;
            foreach (var line in snapshot.Split('\n'))
            {
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) throw new FormatException("Malformed snapshot line: missing '='.");
                _map[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
        }

        public string GetString(string key)
        {
            if (!_map.TryGetValue(key, out var enc))
                throw new KeyNotFoundException("Snapshot missing key: " + key);
            return Decode(enc);
        }

        public int GetInt(string key) => int.Parse(GetString(key), CultureInfo.InvariantCulture);
        public long GetLong(string key) => long.Parse(GetString(key), CultureInfo.InvariantCulture);
        public bool GetBool(string key) => GetString(key) == "1";

        /// <summary>Read a list field, deserializing each child snapshot back into an element.</summary>
        public List<T> GetList<T>(string key, Func<string, T> deserializeItem)
        {
            int count = GetInt(key + ".count");
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
                result.Add(deserializeItem(GetString(key + "." + i.ToString(CultureInfo.InvariantCulture))));
            return result;
        }

        private static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    /// <summary>Stable content reference: an authored key plus its version. Display names are never
    /// identity (data-model.md TreeId/NodeId/CatalogId rules); the version pins the current build so a
    /// display-name change does not silently rebind. An empty <see cref="Key"/> means "none".</summary>
    public readonly struct VersionedId : IEquatable<VersionedId>
    {
        public VersionedId(string key, int version)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version;
        }

        public string Key { get; }
        public int Version { get; }

        public bool IsNone => string.IsNullOrEmpty(Key);
        public static readonly VersionedId None = new VersionedId(string.Empty, 0);

        public string Serialize() => new SnapshotWriter().Put("k", Key).PutInt("v", Version).Build();

        public static VersionedId Deserialize(string snapshot)
        {
            var r = new SnapshotReader(snapshot);
            return new VersionedId(r.GetString("k"), r.GetInt("v"));
        }

        public bool Equals(VersionedId other) =>
            string.Equals(Key, other.Key, StringComparison.Ordinal) && Version == other.Version;
        public override bool Equals(object? obj) => obj is VersionedId other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((Key == null ? 0 : StringComparer.Ordinal.GetHashCode(Key)) * 397) ^ Version);
        public override string ToString() => IsNone ? "(none)" : Key + "@v" + Version.ToString(CultureInfo.InvariantCulture);
    }
}
