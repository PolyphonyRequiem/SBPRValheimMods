using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R5 — durable per-world event provenance ledger.
    //
    // Acceptance: "Fresh event failure provenance must survive restart if diagnostics
    // claim it is not migration; use a persisted/versioned minimal world ledger or
    // honest terminal failure marker, not session-only dictionaries." and "Every event
    // outcome including exceptions is captured. No fake counter-only retries after
    // vanilla sets generated flag."
    //
    // This is the engine-free, versioned, serializable ledger. The net48 layer persists
    // its serialized form (one small text blob keyed by world identity) and rehydrates it
    // on startup so a fresh-world realization FAILURE (no valid seat / manifest required /
    // exception) is a durable terminal fact — not a session-only dictionary that a restart
    // silently clears into a phantom retry. It records exactly ONE terminal outcome per
    // host zone; once an outcome is terminal, re-observing the same zone is a no-op (no
    // fake retries). It is a pure value type: fully unit-tested headless.
    // ============================================================================

    /// <summary>The terminal outcome recorded for one host zone in one world.</summary>
    internal enum HomesteadEventOutcome
    {
        /// <summary>A Stone was created/persisted for this host zone.</summary>
        Created,

        /// <summary>Ordinary host produced no valid static-geometry seat (terminal for this selector version).</summary>
        NoValidSeat,

        /// <summary>Generator host had no matching manifest row (terminal until a manifest is supplied).</summary>
        ManifestRequired,

        /// <summary>Host geometry could not be read at all (terminal data fault).</summary>
        GeometryUnavailable,

        /// <summary>An exception was thrown while handling this host zone (captured, not swallowed).</summary>
        Exception,

        /// <summary>Existing generated world with no Stone and no runtime geometry to reconstruct: migration is
        /// deferred, explicitly, rather than guessed.</summary>
        MigrationDeferred,
    }

    /// <summary>One durable ledger entry: the host zone, its terminal outcome, the selector version under which
    /// it was decided, and a short detail. Keyed by (zoneX, zoneZ).</summary>
    internal readonly struct HomesteadEventRecord : IEquatable<HomesteadEventRecord>
    {
        internal HomesteadEventRecord(int zoneX, int zoneZ, HomesteadEventOutcome outcome, string selectorVersion, string detail)
        {
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            Outcome = outcome;
            SelectorVersion = selectorVersion ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal HomesteadEventOutcome Outcome { get; }
        internal string SelectorVersion { get; }
        internal string Detail { get; }

        public bool Equals(HomesteadEventRecord other) =>
            ZoneX == other.ZoneX && ZoneZ == other.ZoneZ && Outcome == other.Outcome &&
            string.Equals(SelectorVersion, other.SelectorVersion, StringComparison.Ordinal) &&
            string.Equals(Detail, other.Detail, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is HomesteadEventRecord other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ZoneX;
                hash = (hash * 397) ^ ZoneZ;
                hash = (hash * 397) ^ (int)Outcome;
                return (hash * 397) ^ (SelectorVersion?.GetHashCode() ?? 0);
            }
        }
    }

    /// <summary>The versioned, serializable per-world ledger. Records exactly one terminal outcome per host
    /// zone; a Created outcome is never overwritten, and a failure outcome is only overwritten by a later
    /// Created (a genuine success) or a selector-version change — never by a re-run under the same version
    /// (which would be a phantom retry). Serialization is a tiny newline/pipe text format so the net48 layer
    /// can persist it as a single ZDO/world blob with no engine dependency.</summary>
    internal sealed class HomesteadWorldLedger
    {
        internal const int SchemaVersion = 1;
        private const string Header = "niflheim-homestead-ledger-v1";

        private readonly Dictionary<string, HomesteadEventRecord> byZone;

        internal HomesteadWorldLedger()
        {
            byZone = new Dictionary<string, HomesteadEventRecord>(StringComparer.Ordinal);
        }

        internal string WorldIdentity { get; private set; } = string.Empty;

        internal int Count => byZone.Count;

        internal IReadOnlyCollection<HomesteadEventRecord> Records => byZone.Values.ToList();

        internal bool TryGet(int zoneX, int zoneZ, out HomesteadEventRecord record) =>
            byZone.TryGetValue(Key(zoneX, zoneZ), out record);

        /// <summary>Record a terminal outcome for a host zone. Idempotent within the same selector version:
        /// a Created outcome is sticky, and a failure outcome does not overwrite an existing terminal record
        /// under the same selector version (no phantom retry). A Created always wins over a prior failure.</summary>
        internal void Record(int zoneX, int zoneZ, HomesteadEventOutcome outcome, string selectorVersion, string detail)
        {
            var key = Key(zoneX, zoneZ);
            if (byZone.TryGetValue(key, out var existing))
            {
                if (existing.Outcome == HomesteadEventOutcome.Created) return;   // success is sticky
                if (outcome != HomesteadEventOutcome.Created &&
                    string.Equals(existing.SelectorVersion, selectorVersion, StringComparison.Ordinal))
                    return;   // same-version failure re-observed → not a new event, no phantom retry
            }
            byZone[key] = new HomesteadEventRecord(zoneX, zoneZ, outcome, selectorVersion, detail);
        }

        /// <summary>True when this host zone already has a terminal record under the given selector version, so
        /// the caller must NOT re-attempt it (prevents counter-only phantom retries after vanilla's generated
        /// flag is set). A Created record blocks under any version.</summary>
        internal bool IsTerminal(int zoneX, int zoneZ, string selectorVersion)
        {
            if (!byZone.TryGetValue(Key(zoneX, zoneZ), out var record)) return false;
            if (record.Outcome == HomesteadEventOutcome.Created) return true;
            return string.Equals(record.SelectorVersion, selectorVersion, StringComparison.Ordinal);
        }

        internal string Serialize()
        {
            var lines = new List<string> { Header, WorldIdentity };
            foreach (var record in byZone.Values.OrderBy(r => r.ZoneX).ThenBy(r => r.ZoneZ))
            {
                lines.Add(string.Join("\t",
                    record.ZoneX.ToString(CultureInfo.InvariantCulture),
                    record.ZoneZ.ToString(CultureInfo.InvariantCulture),
                    record.Outcome.ToString(),
                    Escape(record.SelectorVersion),
                    Escape(record.Detail)));
            }
            return string.Join("\n", lines);
        }

        internal static HomesteadWorldLedger Deserialize(string worldIdentity, string? serialized)
        {
            var ledger = new HomesteadWorldLedger { WorldIdentity = worldIdentity ?? string.Empty };
            if (string.IsNullOrEmpty(serialized)) return ledger;
            var lines = serialized!.Split('\n');
            if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                return ledger;   // unknown/absent schema → empty (honest, no guessed history)
            for (var i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                var parts = lines[i].Split('\t');
                if (parts.Length < 5) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zx)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zz)) continue;
                if (!Enum.TryParse<HomesteadEventOutcome>(parts[2], out var outcome)) continue;
                ledger.byZone[Key(zx, zz)] = new HomesteadEventRecord(
                    zx, zz, outcome, Unescape(parts[3]), Unescape(parts[4]));
            }
            return ledger;
        }

        internal void SetWorldIdentity(string worldIdentity) =>
            WorldIdentity = worldIdentity ?? string.Empty;

        private static string Key(int zoneX, int zoneZ) =>
            zoneX.ToString(CultureInfo.InvariantCulture) + ":" + zoneZ.ToString(CultureInfo.InvariantCulture);

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        private static string Unescape(string value) => value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }
}
