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

        /// <summary>R6 (Blocker 1) — catalog/identity temporarily unavailable. RETRYABLE: this outcome is
        /// never persisted as terminal, so the host is re-attempted next tick.</summary>
        CatalogUnavailable,
    }

    /// <summary>One durable ledger entry: the host zone, its terminal outcome, the selector version under which
    /// it was decided, and a short detail. Keyed by (zoneX, zoneZ).</summary>
    internal readonly struct HomesteadEventRecord : IEquatable<HomesteadEventRecord>
    {
        internal HomesteadEventRecord(int zoneX, int zoneZ, HomesteadEventOutcome outcome, string selectorVersion, string detail, long manifestGeneration = 0)
        {
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            Outcome = outcome;
            SelectorVersion = selectorVersion ?? string.Empty;
            Detail = detail ?? string.Empty;
            ManifestGeneration = manifestGeneration;
        }

        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal HomesteadEventOutcome Outcome { get; }
        internal string SelectorVersion { get; }
        internal string Detail { get; }

        /// <summary>For a ManifestRequired outcome, the manifest generation that was current when it was
        /// decided. A ManifestRequired recorded at generation G stays terminal only while the live manifest
        /// generation == G; a newer generation permits a retry (R6 Blocker 6). Zero for all other outcomes.</summary>
        internal long ManifestGeneration { get; }

        public bool Equals(HomesteadEventRecord other) =>
            ZoneX == other.ZoneX && ZoneZ == other.ZoneZ && Outcome == other.Outcome &&
            string.Equals(SelectorVersion, other.SelectorVersion, StringComparison.Ordinal) &&
            string.Equals(Detail, other.Detail, StringComparison.Ordinal) &&
            ManifestGeneration == other.ManifestGeneration;

        public override bool Equals(object? obj) => obj is HomesteadEventRecord other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ZoneX;
                hash = (hash * 397) ^ ZoneZ;
                hash = (hash * 397) ^ (int)Outcome;
                hash = (hash * 397) ^ (SelectorVersion?.GetHashCode() ?? 0);
                return (hash * 397) ^ ManifestGeneration.GetHashCode();
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
        internal const int SchemaVersion = 2;
        private const string Header = "niflheim-homestead-ledger-v2";

        private readonly Dictionary<string, HomesteadEventRecord> byZone;
        private bool wellFormed = true;

        internal HomesteadWorldLedger()
        {
            byZone = new Dictionary<string, HomesteadEventRecord>(StringComparer.Ordinal);
        }

        internal string WorldIdentity { get; private set; } = string.Empty;

        internal int Count => byZone.Count;

        /// <summary>False when this ledger was produced from a serialized blob whose header was missing or
        /// unrecognized (i.e. not a genuine, parseable ledger). The durable store uses this to distinguish a
        /// corrupt/foreign file from a real empty ledger so it can recover from a temp/backup instead of
        /// silently adopting a phantom-empty history.</summary>
        internal bool IsWellFormed => wellFormed;

        internal IReadOnlyCollection<HomesteadEventRecord> Records => byZone.Values.ToList();

        internal bool TryGet(int zoneX, int zoneZ, out HomesteadEventRecord record) =>
            byZone.TryGetValue(Key(zoneX, zoneZ), out record);

        /// <summary>Record a terminal outcome for a host zone. Idempotent within the same selector version:
        /// a failure outcome does not overwrite an existing terminal record under the same selector version
        /// (no phantom retry). A Created always wins over a prior failure. A ManifestRequired carries the
        /// manifest generation it was decided under so a newer generation can supersede it.
        ///
        /// R6 (Blocker 5): Created is NOT unconditionally sticky against a NEWER decision — it is only sticky
        /// against re-observation. Recovery (a missing Stone whose Created must be re-attempted) is handled by
        /// <see cref="ClearForRecovery"/>, which the caller invokes when the persisted Stone reality no longer
        /// matches the Created record; the ledger never overrides that ZDO reality on its own.</summary>
        internal void Record(int zoneX, int zoneZ, HomesteadEventOutcome outcome, string selectorVersion, string detail, long manifestGeneration = 0)
        {
            // CatalogUnavailable is explicitly RETRYABLE — never persisted as a terminal outcome, so a host
            // whose catalog/identity was momentarily unavailable is re-attempted on a later tick.
            if (outcome == HomesteadEventOutcome.CatalogUnavailable) return;
            var key = Key(zoneX, zoneZ);
            if (byZone.TryGetValue(key, out var existing))
            {
                if (existing.Outcome == HomesteadEventOutcome.Created && outcome != HomesteadEventOutcome.Created)
                    return;   // success is sticky against re-observation (recovery uses ClearForRecovery)
                if (outcome != HomesteadEventOutcome.Created &&
                    string.Equals(existing.SelectorVersion, selectorVersion, StringComparison.Ordinal))
                {
                    // A ManifestRequired can be superseded by a NEWER manifest generation (retryable); every
                    // other same-version failure re-observation is a no-op (no phantom retry).
                    if (!(existing.Outcome == HomesteadEventOutcome.ManifestRequired &&
                          outcome == HomesteadEventOutcome.ManifestRequired &&
                          manifestGeneration > existing.ManifestGeneration))
                        return;
                }
            }
            byZone[key] = new HomesteadEventRecord(zoneX, zoneZ, outcome, selectorVersion, detail, manifestGeneration);
        }

        /// <summary>R6 (Blocker 5) — the ledger records provenance, NEVER creation truth. When the caller finds
        /// no persisted Stone matching a Created record, it calls this to drop the stale Created so recovery
        /// can re-create the Stone. The persisted ZDO reality is authoritative; the ledger obeys it.</summary>
        internal void ClearForRecovery(int zoneX, int zoneZ)
        {
            var key = Key(zoneX, zoneZ);
            if (byZone.TryGetValue(key, out var existing) && existing.Outcome == HomesteadEventOutcome.Created)
                byZone.Remove(key);
        }

        /// <summary>True when this host zone already has a terminal record that blocks re-attempt under the
        /// given selector version and live manifest generation. A Created blocks under any version. A
        /// ManifestRequired blocks only while the live manifest generation has NOT advanced past the one it
        /// was recorded under (R6 Blocker 6 retryability). Every other same-version failure blocks.</summary>
        internal bool IsTerminal(int zoneX, int zoneZ, string selectorVersion, long liveManifestGeneration = 0)
        {
            if (!byZone.TryGetValue(Key(zoneX, zoneZ), out var record)) return false;
            if (record.Outcome == HomesteadEventOutcome.Created) return true;
            if (!string.Equals(record.SelectorVersion, selectorVersion, StringComparison.Ordinal)) return false;
            if (record.Outcome == HomesteadEventOutcome.ManifestRequired)
                return liveManifestGeneration <= record.ManifestGeneration;   // newer generation ⇒ retry
            return true;
        }

        internal string Serialize()
        {
            // Strict envelope (v2): header, world identity, exact record count, the records (sorted for a
            // stable byte image), then a checksum line over the world identity + count + record body. A reader
            // validates the world identity matches, the count equals the number of record lines, and the
            // checksum matches the body — so a truncated valid-prefix, an extra/duplicate row, a world-identity
            // mismatch, or any garbage is rejected as NOT well-formed (recovery falls back to temp/backup).
            var recordLines = new List<string>();
            foreach (var record in byZone.Values.OrderBy(r => r.ZoneX).ThenBy(r => r.ZoneZ))
            {
                recordLines.Add(string.Join("\t",
                    record.ZoneX.ToString(CultureInfo.InvariantCulture),
                    record.ZoneZ.ToString(CultureInfo.InvariantCulture),
                    record.Outcome.ToString(),
                    Escape(record.SelectorVersion),
                    Escape(record.Detail),
                    record.ManifestGeneration.ToString(CultureInfo.InvariantCulture)));
            }
            var count = recordLines.Count;
            var body = string.Join("\n", recordLines);
            var checksum = BodyChecksum(WorldIdentity, count, body);
            var lines = new List<string>
            {
                Header,
                WorldIdentity,
                count.ToString(CultureInfo.InvariantCulture),
            };
            lines.AddRange(recordLines);
            lines.Add("checksum\t" + checksum);
            return string.Join("\n", lines);
        }

        internal static HomesteadWorldLedger Deserialize(string worldIdentity, string? serialized)
        {
            var ledger = new HomesteadWorldLedger { WorldIdentity = worldIdentity ?? string.Empty };
            if (string.IsNullOrEmpty(serialized))
            {
                // Absent content is a genuine empty ledger (well-formed), not a corrupt one.
                return ledger;
            }
            var lines = serialized!.Split('\n');
            // Minimum envelope: header, world, count, checksum (4 lines).
            if (lines.Length < 4 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
            {
                ledger.wellFormed = false;
                return ledger;
            }

            // The serialized world identity MUST match the world we are loading for. A ledger blob carrying a
            // different world's identity is NOT our history — reject it rather than adopt a foreign/mismatched
            // ledger (which could suppress or fabricate outcomes for the wrong world).
            var serializedWorld = lines[1];
            if (!string.Equals(serializedWorld, worldIdentity ?? string.Empty, StringComparison.Ordinal))
            {
                ledger.wellFormed = false;
                return ledger;
            }

            if (!int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedCount) ||
                expectedCount < 0)
            {
                ledger.wellFormed = false;
                return ledger;
            }

            // Exactly: header(1) + world(1) + count(1) + expectedCount records + checksum(1).
            if (lines.Length != 3 + expectedCount + 1)
            {
                // Wrong number of lines ⇒ truncated (valid-prefix) or extra rows. Reject the whole candidate.
                ledger.wellFormed = false;
                return ledger;
            }

            var recordLines = new List<string>();
            var parsed = new List<HomesteadEventRecord>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < expectedCount; i++)
            {
                var line = lines[3 + i];
                recordLines.Add(line);
                var parts = line.Split('\t');
                if (parts.Length < 6) { ledger.wellFormed = false; return ledger; }
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zx)) { ledger.wellFormed = false; return ledger; }
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zz)) { ledger.wellFormed = false; return ledger; }
                if (!Enum.TryParse<HomesteadEventOutcome>(parts[2], out var outcome)) { ledger.wellFormed = false; return ledger; }
                if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation)) { ledger.wellFormed = false; return ledger; }
                var key = Key(zx, zz);
                if (!seenKeys.Add(key)) { ledger.wellFormed = false; return ledger; }   // duplicate zone row ⇒ corrupt
                parsed.Add(new HomesteadEventRecord(zx, zz, outcome, Unescape(parts[3]), Unescape(parts[4]), generation));
            }

            // Checksum line: "checksum\t<hex>" over (world, count, sorted-record-body).
            var checksumLine = lines[3 + expectedCount];
            var checksumParts = checksumLine.Split('\t');
            if (checksumParts.Length != 2 || !string.Equals(checksumParts[0], "checksum", StringComparison.Ordinal))
            {
                ledger.wellFormed = false;
                return ledger;
            }
            var body = string.Join("\n", recordLines);
            if (!string.Equals(checksumParts[1], BodyChecksum(serializedWorld, expectedCount, body), StringComparison.Ordinal))
            {
                ledger.wellFormed = false;
                return ledger;
            }

            foreach (var record in parsed)
                ledger.byZone[Key(record.ZoneX, record.ZoneZ)] = record;
            return ledger;
        }

        private static string BodyChecksum(string worldIdentity, int count, string body) =>
            StableHash.Hex(worldIdentity ?? string.Empty, count.ToString(CultureInfo.InvariantCulture), body);

        internal void SetWorldIdentity(string worldIdentity) =>
            WorldIdentity = worldIdentity ?? string.Empty;

        private static string Key(int zoneX, int zoneZ) =>
            zoneX.ToString(CultureInfo.InvariantCulture) + ":" + zoneZ.ToString(CultureInfo.InvariantCulture);

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");
        private static string Unescape(string value) => value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }
}
