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
        internal const int SchemaVersion = 1;
        private const string Header = "niflheim-homestead-ledger-v1";

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
            var lines = new List<string> { Header, WorldIdentity };
            foreach (var record in byZone.Values.OrderBy(r => r.ZoneX).ThenBy(r => r.ZoneZ))
            {
                lines.Add(string.Join("\t",
                    record.ZoneX.ToString(CultureInfo.InvariantCulture),
                    record.ZoneZ.ToString(CultureInfo.InvariantCulture),
                    record.Outcome.ToString(),
                    Escape(record.SelectorVersion),
                    Escape(record.Detail),
                    record.ManifestGeneration.ToString(CultureInfo.InvariantCulture)));
            }
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
            if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
            {
                // Unknown/garbage header → NOT a valid ledger source. Mark it so the durable store can tell
                // this apart from a real empty ledger and recover from a temp/backup instead.
                ledger.wellFormed = false;
                return ledger;
            }
            for (var i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                var parts = lines[i].Split('\t');
                if (parts.Length < 5) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zx)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zz)) continue;
                if (!Enum.TryParse<HomesteadEventOutcome>(parts[2], out var outcome)) continue;
                long generation = 0;
                if (parts.Length >= 6)
                    long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out generation);
                ledger.byZone[Key(zx, zz)] = new HomesteadEventRecord(
                    zx, zz, outcome, Unescape(parts[3]), Unescape(parts[4]), generation);
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
