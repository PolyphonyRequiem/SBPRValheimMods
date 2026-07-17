using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T002 (Gate A) — the ConnectionAggregate (data-model Aggregate 1). One server-owned account-pair
    // loyalty aggregate with an active qualifying-source set, a lifecycle (Active/Grace/Reset), a durable
    // accumulated age, and a current-segment anchor. This is the PURE aggregate: its transitions validate
    // accepted policy and produce the next authoritative value; they never mutate in place, read Unity
    // state, or persist. Durable, receipt-backed commit lives in the application/receipt layer.
    //
    // Load-bearing invariants encoded here (data-model Aggregate 1):
    //   * exactly two distinct canonical accounts (owned by ConnectionId);
    //   * Active requires >=1 source and advances age from server time;
    //   * Grace requires 0 sources, freezes age, and carries one 72h expiry;
    //   * adding a source during Grace clears grace and resumes from the frozen age;
    //   * Grace expiry resets accumulated age to zero (Reset), recording terminal provenance;
    //   * negative elapsed time contributes zero and flags a clock anomaly (never advances age).
    //
    // net48 audit: System.Collections.Generic + value objects + the snapshot codec. Engine-free — no
    // UnityEngine/Valheim/BepInEx — so it link-compiles into the net8 test project.

    public enum ConnectionLifecycle
    {
        /// <summary>No sources ever added / fresh identity. Distinct from Reset (which followed a
        /// terminal grace expiry). Age is zero; nothing accrues.</summary>
        None = 0,
        /// <summary>At least one qualifying source; age advances from server time.</summary>
        Active = 1,
        /// <summary>Zero sources after a final-link removal; age frozen; one 72h expiry pending.</summary>
        Grace = 2,
        /// <summary>Grace expired; accumulated age reset to zero; terminal provenance recorded.</summary>
        Reset = 3
    }

    /// <summary>One qualifying Stone role-pair source (data-model <c>ConnectionSourceId</c>). Several
    /// sources keep one Connection Active; source count never multiplies maturity.</summary>
    public sealed class ConnectionSource
    {
        public ConnectionSource(StoneId stoneId, string lowerRelationshipId, string higherRelationshipId,
            int sourceVersion, string activationProvenance)
        {
            StoneId = stoneId;
            // Canonicalize the relationship-id pair ordinally so the same Stone role-pair produces the
            // same source id regardless of which relationship was named first.
            if (string.CompareOrdinal(lowerRelationshipId ?? string.Empty, higherRelationshipId ?? string.Empty) <= 0)
            {
                RelationshipLow = lowerRelationshipId ?? string.Empty;
                RelationshipHigh = higherRelationshipId ?? string.Empty;
            }
            else
            {
                RelationshipLow = higherRelationshipId ?? string.Empty;
                RelationshipHigh = lowerRelationshipId ?? string.Empty;
            }
            SourceVersion = sourceVersion;
            ActivationProvenance = activationProvenance ?? string.Empty;
        }

        public StoneId StoneId { get; }
        public string RelationshipLow { get; }
        public string RelationshipHigh { get; }
        public int SourceVersion { get; }
        public string ActivationProvenance { get; }

        /// <summary>Canonical, replay-stable source identity: (StoneId, lowRel, highRel, version).</summary>
        public string SourceId =>
            StoneId.Value + "\u0001" + RelationshipLow + "\u0001" + RelationshipHigh + "\u0001" +
            SourceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string Serialize() => new SnapshotWriter()
            .Put("stone", StoneId.Value)
            .Put("relLow", RelationshipLow)
            .Put("relHigh", RelationshipHigh)
            .PutInt("ver", SourceVersion)
            .Put("actProv", ActivationProvenance)
            .Build();

        public static ConnectionSource Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new ConnectionSource(
                new StoneId(r.GetString("stone")),
                r.GetString("relLow"),
                r.GetString("relHigh"),
                r.GetInt("ver"),
                r.GetString("actProv"));
        }
    }

    /// <summary>Pure account-pair loyalty aggregate (data-model Aggregate 1). Immutable value; every
    /// transition returns a new instance. Age accounting is entirely in whole seconds against
    /// <see cref="AccumulatedSeconds"/> (frozen age) plus, while Active, the live segment since
    /// <see cref="CurrentSegmentAnchorSeconds"/>.</summary>
    public sealed class ConnectionAggregate
    {
        /// <summary>72-hour grace, in whole seconds (spec RD-004 / data-model Aggregate 1).</summary>
        public const long GraceSeconds = 72L * 3600L;

        public ConnectionAggregate(
            ConnectionId id,
            long revision,
            ConnectionLifecycle lifecycle,
            long accumulatedSeconds,
            long currentSegmentAnchorSeconds,
            long graceExpiresAtSeconds,
            IReadOnlyList<ConnectionSource>? sources,
            int schemaVersion = 1)
        {
            Id = id;
            Revision = revision;
            Lifecycle = lifecycle;
            AccumulatedSeconds = accumulatedSeconds < 0 ? 0 : accumulatedSeconds;
            CurrentSegmentAnchorSeconds = currentSegmentAnchorSeconds;
            GraceExpiresAtSeconds = graceExpiresAtSeconds;
            Sources = sources ?? Array.Empty<ConnectionSource>();
            SchemaVersion = schemaVersion;
        }

        public ConnectionId Id { get; }
        public long Revision { get; }
        public ConnectionLifecycle Lifecycle { get; }

        /// <summary>Durable frozen age in whole seconds. While Active, live age =
        /// AccumulatedSeconds + (serverTime - CurrentSegmentAnchorSeconds).</summary>
        public long AccumulatedSeconds { get; }

        /// <summary>Server time (whole seconds) at which the current Active segment began. Only
        /// meaningful while <see cref="Lifecycle"/> is Active.</summary>
        public long CurrentSegmentAnchorSeconds { get; }

        /// <summary>Server time (whole seconds) at which Grace expires. Only meaningful while Grace.</summary>
        public long GraceExpiresAtSeconds { get; }

        public IReadOnlyList<ConnectionSource> Sources { get; }
        public int SchemaVersion { get; }

        public bool HasSources => Sources.Count > 0;

        /// <summary>A fresh, sourceless, zero-age Connection for the given identity.</summary>
        public static ConnectionAggregate CreateEmpty(ConnectionId id) =>
            new ConnectionAggregate(id, 0, ConnectionLifecycle.None, 0, 0, 0, null);

        /// <summary>The live accumulated age at <paramref name="serverTimeSeconds"/>. While Active this
        /// includes the current segment; otherwise it is the frozen accumulated age. Negative elapsed
        /// time (clock ran backwards) contributes zero — the segment never subtracts age.</summary>
        public long LiveAgeSeconds(long serverTimeSeconds)
        {
            if (Lifecycle != ConnectionLifecycle.Active) return AccumulatedSeconds;
            long segment = serverTimeSeconds - CurrentSegmentAnchorSeconds;
            if (segment < 0) segment = 0; // clock anomaly: never advance age backwards
            return AccumulatedSeconds + segment;
        }

        /// <summary>The exact maturity multiplier at <paramref name="serverTimeSeconds"/> (RD-003).
        /// A Grace/Reset/None Connection is NOT a qualifying contribution source; callers gate on
        /// <see cref="Lifecycle"/> before applying this, but the band itself is defined for any age.</summary>
        public MaturityMultiplier MaturityAt(long serverTimeSeconds) =>
            ConnectionMaturity.ForAccumulatedSeconds(LiveAgeSeconds(serverTimeSeconds));

        // ---- Transitions (pure; each returns a new aggregate) ----

        /// <summary>Add a qualifying source at <paramref name="serverTimeSeconds"/>. A source added to a
        /// None/Reset Connection begins a fresh Active segment from the current (possibly zero) age. A
        /// source added during Grace CLEARS grace and RESUMES from the frozen age (spec RD-004 /
        /// data-model Aggregate 1 "adding a valid source during Grace clears grace and resumes"). An
        /// already-Active Connection folds its live age into AccumulatedSeconds and re-anchors, so the
        /// running segment is never lost.</summary>
        public ConnectionAggregate AddSource(ConnectionSource source, long serverTimeSeconds)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            // Idempotent: re-adding an identical source id does not double it.
            var newSources = new List<ConnectionSource>(Sources.Count + 1);
            bool exists = false;
            foreach (var s in Sources)
            {
                newSources.Add(s);
                if (string.Equals(s.SourceId, source.SourceId, StringComparison.Ordinal)) exists = true;
            }
            if (!exists) newSources.Add(source);

            // Freeze the live age up to now, then re-anchor a single Active segment from here. For
            // None/Grace/Reset the live age equals AccumulatedSeconds (no running segment).
            long frozen = LiveAgeSeconds(serverTimeSeconds);

            return new ConnectionAggregate(
                Id, Revision + 1, ConnectionLifecycle.Active,
                accumulatedSeconds: frozen,
                currentSegmentAnchorSeconds: serverTimeSeconds,
                graceExpiresAtSeconds: 0,
                sources: newSources,
                schemaVersion: SchemaVersion);
        }

        /// <summary>Remove a source at <paramref name="serverTimeSeconds"/>. If sources remain the
        /// Connection stays Active (age folded + re-anchored). If this removes the FINAL source, the
        /// Connection freezes its reconciled age and enters Grace with expiry exactly
        /// serverTime + 72h (spec RD-004). Removing an unknown source is a no-op that still folds age.</summary>
        public ConnectionAggregate RemoveSource(string sourceId, long serverTimeSeconds)
        {
            var remaining = new List<ConnectionSource>(Sources.Count);
            foreach (var s in Sources)
                if (!string.Equals(s.SourceId, sourceId, StringComparison.Ordinal))
                    remaining.Add(s);

            long frozen = LiveAgeSeconds(serverTimeSeconds);

            if (remaining.Count > 0)
            {
                return new ConnectionAggregate(
                    Id, Revision + 1, ConnectionLifecycle.Active,
                    accumulatedSeconds: frozen,
                    currentSegmentAnchorSeconds: serverTimeSeconds,
                    graceExpiresAtSeconds: 0,
                    sources: remaining,
                    schemaVersion: SchemaVersion);
            }

            // Final source removed -> frozen grace.
            return new ConnectionAggregate(
                Id, Revision + 1, ConnectionLifecycle.Grace,
                accumulatedSeconds: frozen,
                currentSegmentAnchorSeconds: 0,
                graceExpiresAtSeconds: serverTimeSeconds + GraceSeconds,
                sources: Array.Empty<ConnectionSource>(),
                schemaVersion: SchemaVersion);
        }

        /// <summary>Idempotent grace-expiry transition. If the Connection is in Grace and
        /// <paramref name="serverTimeSeconds"/> is at or past the expiry, reset accumulated age to zero
        /// (Reset) with terminal provenance. Otherwise returns <c>this</c> unchanged (idempotent).</summary>
        public ConnectionAggregate ReconcileGraceExpiry(long serverTimeSeconds)
        {
            if (Lifecycle != ConnectionLifecycle.Grace) return this;
            if (serverTimeSeconds < GraceExpiresAtSeconds) return this;

            return new ConnectionAggregate(
                Id, Revision + 1, ConnectionLifecycle.Reset,
                accumulatedSeconds: 0,
                currentSegmentAnchorSeconds: 0,
                graceExpiresAtSeconds: 0,
                sources: Array.Empty<ConnectionSource>(),
                schemaVersion: SchemaVersion);
        }

        /// <summary>True when this Connection is a qualifying CONTRIBUTION source at command time: it
        /// is Active with at least one source. A Grace-only, Reset, or sourceless Connection contributes
        /// nothing (spec RD-005).</summary>
        public bool IsContributionQualifying => Lifecycle == ConnectionLifecycle.Active && HasSources;

        // ---- Snapshot codec (round-trips every authoritative field) ----

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("world", Id.World.Value)
            .Put("product", Id.Product.Value)
            .Put("accLow", Id.AccountLow.Value)
            .Put("accHigh", Id.AccountHigh.Value)
            .PutLong("rev", Revision)
            .PutInt("life", (int)Lifecycle)
            .PutLong("accum", AccumulatedSeconds)
            .PutLong("anchor", CurrentSegmentAnchorSeconds)
            .PutLong("graceExp", GraceExpiresAtSeconds)
            .PutList("sources", (IReadOnlyList<ConnectionSource>)Sources, s => s.Serialize())
            .Build();

        public static ConnectionAggregate Deserialize(string snapshot)
        {
            var r = new SnapshotReader(snapshot);
            var resolution = ConnectionId.TryCreate(
                new WorldId(r.GetString("world")),
                new ProductScope(r.GetString("product")),
                new AccountId(r.GetString("accLow")),
                new AccountId(r.GetString("accHigh")),
                out var id);
            if (resolution != ConnectionIdentityResolution.Valid)
                throw new FormatException("Snapshot carries a non-canonical Connection identity: " + resolution);

            var sources = r.GetList("sources", ConnectionSource.Deserialize);
            return new ConnectionAggregate(
                id,
                r.GetLong("rev"),
                (ConnectionLifecycle)r.GetInt("life"),
                r.GetLong("accum"),
                r.GetLong("anchor"),
                r.GetLong("graceExp"),
                sources,
                r.GetInt("schema"));
        }
    }
}
