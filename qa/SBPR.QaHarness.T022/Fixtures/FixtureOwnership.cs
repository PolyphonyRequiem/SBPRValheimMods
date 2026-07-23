// ============================================================================
//  QA-M3R repair (t_0e3a88bd) — durable exact ownership markers + bounded recovery.
// ----------------------------------------------------------------------------
//  The owner review of PR #414 found crash-safe ownership was FALSE: world
//  creation preceded the durable snapshot, so a crash after spawn but before
//  Save leaked an untracked object, and a fresh/corrupt ledger (empty handles)
//  could never rediscover that survivor. The claim that deterministic logical
//  ids alone recover a survivor was wrong — a logical id is not a world handle.
//
//  This module makes the DURABLE record of ownership live ON the spawned object
//  itself, not in a separate file written later. Every fixture object carries a
//  QA ownership MARKER — (world uid, run nonce, fixture id, owned-resource id) —
//  written atomically as part of creation. The marker is the game-persisted ZDO
//  truth, so a crash at ANY point after spawn leaves a self-describing survivor
//  the next run can find and adopt WITHOUT trusting the snapshot file at all.
//
//  Recovery is NARROWLY BOUNDED and FAIL-CLOSED:
//    * scope = exactly (this world uid, this run nonce, this fixture id);
//    * adopt exactly one survivor per expected owned-resource id;
//    * a marker that is malformed, duplicated, names a resource the current plan
//      does not expect, or carries a foreign world/run FAILS THE RECOVERY CLOSED
//      (no adoption, no world side effect) rather than guessing;
//    * objects that carry NO QA marker are never returned by discovery, so an
//      unrelated same-prefab world object is structurally un-adoptable and
//      un-deletable — it is preserved.
//
//  Engine-free: System.* only. No product identity/AP/ownership/signature/verdict
//  — a QA scaffolding marker is not a product ownership token; it names only which
//  disposable QA run stood up which disposable scaffolding object.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>
    /// The per-run identity a fixture lifecycle executes under: the disposable world's uid and
    /// the armed run's nonce. Recovery is scoped to exactly this pair — a survivor whose marker
    /// names a different world or run is foreign and fails recovery closed.
    /// </summary>
    public readonly struct FixtureRunContext : IEquatable<FixtureRunContext>
    {
        public FixtureRunContext(long worldUid, string runNonce)
        {
            if (string.IsNullOrEmpty(runNonce)) throw new ArgumentException("runNonce must be non-empty.", nameof(runNonce));
            WorldUid = worldUid;
            RunNonce = runNonce;
        }

        /// <summary>The durable per-world uid (ZNet.GetWorldUID / World.m_uid) of the disposable QA world.</summary>
        public long WorldUid { get; }

        /// <summary>The armed run nonce — distinguishes two runs that reuse the same disposable world.</summary>
        public string RunNonce { get; }

        public bool Equals(FixtureRunContext other) =>
            WorldUid == other.WorldUid && string.Equals(RunNonce, other.RunNonce, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FixtureRunContext o && Equals(o);

        public override int GetHashCode()
        {
            unchecked { return (WorldUid.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(RunNonce); }
        }
    }

    /// <summary>
    /// The exact, durable QA ownership marker stamped onto a spawned fixture object's ZDO. It is a
    /// pure value: (world uid, run nonce, fixture id, owned-resource canonical id). Two markers are
    /// equal iff all four fields match. Encoded to a single opaque string the engine-bound seam
    /// stores verbatim; the engine-free layer owns encode/decode + all validation, so the fail-closed
    /// recovery logic is headlessly tested and the seam stays thin.
    /// </summary>
    public readonly struct FixtureOwnershipMarker : IEquatable<FixtureOwnershipMarker>
    {
        /// <summary>The ZDO key the marker payload is stored under (single namespaced key, never collides with vanilla).</summary>
        public const string ZdoKey = "SBPRQA_FixtureOwner";

        private const string Magic = "SBPRQA-OWN";
        private const int Version = 1;

        public FixtureOwnershipMarker(long worldUid, string runNonce, string fixtureId, string resourceCanonical)
        {
            if (string.IsNullOrEmpty(runNonce)) throw new ArgumentException("runNonce must be non-empty.", nameof(runNonce));
            if (string.IsNullOrEmpty(fixtureId)) throw new ArgumentException("fixtureId must be non-empty.", nameof(fixtureId));
            if (string.IsNullOrEmpty(resourceCanonical)) throw new ArgumentException("resourceCanonical must be non-empty.", nameof(resourceCanonical));
            WorldUid = worldUid;
            RunNonce = runNonce;
            FixtureId = fixtureId;
            ResourceCanonical = resourceCanonical;
        }

        public long WorldUid { get; }
        public string RunNonce { get; }
        public string FixtureId { get; }

        /// <summary>The <see cref="OwnedResourceId.Canonical"/> this object realises (exact owned id).</summary>
        public string ResourceCanonical { get; }

        /// <summary>Build the marker for one owned resource under a run context.</summary>
        public static FixtureOwnershipMarker For(FixtureRunContext ctx, string fixtureId, OwnedResourceId id) =>
            new FixtureOwnershipMarker(ctx.WorldUid, ctx.RunNonce, fixtureId, id.Canonical);

        /// <summary>Serialize to the opaque payload string stored on the ZDO. Total function.</summary>
        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append(Magic).Append('\u241F')
              .Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\u241F')
              .Append(WorldUid.ToString(CultureInfo.InvariantCulture)).Append('\u241F')
              .Append(Esc(RunNonce)).Append('\u241F')
              .Append(Esc(FixtureId)).Append('\u241F')
              .Append(Esc(ResourceCanonical));
            return sb.ToString();
        }

        /// <summary>
        /// Parse a payload string back to a marker. Fail-closed: any structural defect returns false
        /// (the caller treats an undecodable candidate as a malformed marker and refuses recovery).
        /// </summary>
        public static bool TryDecode(string? payload, out FixtureOwnershipMarker marker)
        {
            marker = default;
            if (string.IsNullOrEmpty(payload)) return false;
            var f = payload!.Split('\u241F');
            if (f.Length != 6) return false;
            if (!string.Equals(f[0], Magic, StringComparison.Ordinal)) return false;
            if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver) || ver != Version) return false;
            if (!long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long worldUid)) return false;
            string runNonce = Unesc(f[3]);
            string fixtureId = Unesc(f[4]);
            string resource = Unesc(f[5]);
            if (string.IsNullOrEmpty(runNonce) || string.IsNullOrEmpty(fixtureId) || string.IsNullOrEmpty(resource))
                return false;
            marker = new FixtureOwnershipMarker(worldUid, runNonce, fixtureId, resource);
            return true;
        }

        public bool Equals(FixtureOwnershipMarker other) =>
            WorldUid == other.WorldUid
            && string.Equals(RunNonce, other.RunNonce, StringComparison.Ordinal)
            && string.Equals(FixtureId, other.FixtureId, StringComparison.Ordinal)
            && string.Equals(ResourceCanonical, other.ResourceCanonical, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is FixtureOwnershipMarker o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + WorldUid.GetHashCode();
                h = h * 31 + StringComparer.Ordinal.GetHashCode(RunNonce);
                h = h * 31 + StringComparer.Ordinal.GetHashCode(FixtureId);
                h = h * 31 + StringComparer.Ordinal.GetHashCode(ResourceCanonical);
                return h;
            }
        }

        // Escape the unit-separator + backslash so no field can break framing.
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\u241F': sb.Append("\\u"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == '\\') sb.Append('\\');
                    else if (n == 'u') sb.Append('\u241F');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>One live world object that carries a QA ownership marker: its raw payload + world handle.</summary>
    public readonly struct MarkedInstance
    {
        public MarkedInstance(string markerPayload, string spawnedInstanceId)
        {
            MarkerPayload = markerPayload ?? string.Empty;
            SpawnedInstanceId = spawnedInstanceId ?? string.Empty;
        }

        /// <summary>The opaque marker payload read off the object's ZDO (decode via <see cref="FixtureOwnershipMarker.TryDecode"/>).</summary>
        public string MarkerPayload { get; }

        /// <summary>The world handle (ZDOID string) used to resolve/despawn the object.</summary>
        public string SpawnedInstanceId { get; }
    }

    /// <summary>
    /// The bounded region the engine-free recovery hands <see cref="IFixtureWorld.DiscoverMarked"/> so a
    /// survivor scan is a PINNED spatial lookup around the fixture origin, never a whole-world walk. It
    /// names the allowlisted prefab names the current plan expects, the max fixture radius (meters), and a
    /// hard cap on marked candidates (overflow ⇒ refuse). Derived purely from the validated plan + bounds.
    /// </summary>
    public readonly struct FixtureWorldScope
    {
        public FixtureWorldScope(IReadOnlyCollection<string> allowedPrefabNames, double maxRadiusMeters, int maxCandidates)
        {
            AllowedPrefabNames = allowedPrefabNames ?? Array.Empty<string>();
            MaxRadiusMeters = maxRadiusMeters;
            MaxCandidates = maxCandidates;
        }

        public IReadOnlyCollection<string> AllowedPrefabNames { get; }
        public double MaxRadiusMeters { get; }
        public int MaxCandidates { get; }

        /// <summary>
        /// Build the bounded scope from a validated plan: the DISTINCT allowlisted logical ids the plan
        /// names, the plan's largest requested radius (capped at the bounds max), and a hard candidate
        /// cap of the bounds' MaxTotalObjects. The cap bounds the scan against a pathological world (it
        /// may never return more than a fixture's worth of objects); plan-exactness (a survivor naming an
        /// unexpected owned id, or two survivors claiming one id) is enforced per-candidate downstream,
        /// which needs headroom above the plan's own count to observe and refuse those violations. Pure.
        /// </summary>
        public static FixtureWorldScope ForPlan(ValidatedFixturePlan plan, FixtureBounds bounds)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var prefabs = new HashSet<string>(StringComparer.Ordinal);
            double maxRadius = 0.0;
            foreach (var r in plan.Resources)
            {
                prefabs.Add(r.LogicalId);
                if (r.RadiusMeters > maxRadius) maxRadius = r.RadiusMeters;
            }
            if (maxRadius <= 0.0) maxRadius = bounds.MaxRadiusMeters;
            if (maxRadius > bounds.MaxRadiusMeters) maxRadius = bounds.MaxRadiusMeters;
            return new FixtureWorldScope(prefabs, maxRadius, bounds.MaxTotalObjects);
        }
    }

    /// <summary>Whether the bounded marker scan produced a complete candidate set or refused (fail-closed).</summary>
    public enum WorldDiscoveryOutcome
    {
        /// <summary>The scan completed; <see cref="WorldDiscoveryResult.Marked"/> is the exact in-region set (possibly empty).</summary>
        Complete = 0,

        /// <summary>The scan could not be completed safely (binding/enumeration/read fault or cap overflow) — refuse, adopt nothing.</summary>
        Refused = 1,
    }

    /// <summary>The typed outcome of a bounded marker scan the engine-free recovery consumes fail-closed.</summary>
    public sealed class WorldDiscoveryResult
    {
        private WorldDiscoveryResult(WorldDiscoveryOutcome outcome, IReadOnlyList<MarkedInstance> marked, string detail)
        {
            Outcome = outcome;
            Marked = marked ?? Array.Empty<MarkedInstance>();
            Detail = detail ?? string.Empty;
        }

        public WorldDiscoveryOutcome Outcome { get; }
        public IReadOnlyList<MarkedInstance> Marked { get; }
        public string Detail { get; }

        public bool Ok => Outcome == WorldDiscoveryOutcome.Complete;

        public static WorldDiscoveryResult Complete(IReadOnlyList<MarkedInstance> marked) =>
            new WorldDiscoveryResult(WorldDiscoveryOutcome.Complete, marked, string.Empty);

        public static WorldDiscoveryResult Refused(string detail) =>
            new WorldDiscoveryResult(WorldDiscoveryOutcome.Refused, Array.Empty<MarkedInstance>(), detail);
    }

    /// <summary>Why a bounded marker recovery could not produce a clean adoptable survivor set.</summary>
    public enum FixtureRecoveryStatus
    {
        /// <summary>Recovery succeeded — <see cref="FixtureRecoveryResult.Survivors"/> is the exact adoptable set (possibly empty).</summary>
        Ok = 0,

        /// <summary>A marked candidate did not decode — refuse rather than guess ownership.</summary>
        MalformedMarker = 1,

        /// <summary>Two survivors claim the SAME owned-resource id — ambiguous ownership, refuse.</summary>
        DuplicateMarker = 2,

        /// <summary>A candidate marker names a different world uid than the current run — foreign, refuse.</summary>
        ForeignWorld = 3,

        /// <summary>A candidate marker names a different run nonce than the current run — foreign, refuse.</summary>
        ForeignRun = 4,

        /// <summary>A candidate matches this world/run/fixture but names a resource the current plan does not expect — refuse.</summary>
        UnexpectedResource = 5,

        /// <summary>The bounded world scan could not complete safely (binding/enumeration/read fault or cap overflow) — refuse, adopt nothing.</summary>
        DiscoveryRefused = 6,
    }

    /// <summary>One adoptable survivor: the exact owned-resource id its marker names + the live world handle.</summary>
    public readonly struct MarkedSurvivor
    {
        public MarkedSurvivor(OwnedResourceId id, string handle)
        {
            Id = id;
            Handle = handle ?? string.Empty;
        }

        public OwnedResourceId Id { get; }
        public string Handle { get; }
    }

    /// <summary>The typed outcome of a bounded marker recovery scan (fail-closed).</summary>
    public sealed class FixtureRecoveryResult
    {
        private FixtureRecoveryResult(FixtureRecoveryStatus status, IReadOnlyList<MarkedSurvivor> survivors, string detail)
        {
            Status = status;
            Survivors = survivors;
            Detail = detail ?? string.Empty;
        }

        public FixtureRecoveryStatus Status { get; }
        public IReadOnlyList<MarkedSurvivor> Survivors { get; }
        public string Detail { get; }

        /// <summary>True iff the scan produced a clean, adoptable survivor set (no integrity violation).</summary>
        public bool Ok => Status == FixtureRecoveryStatus.Ok;

        public static FixtureRecoveryResult Adopt(IReadOnlyList<MarkedSurvivor> survivors) =>
            new FixtureRecoveryResult(FixtureRecoveryStatus.Ok, survivors, string.Empty);

        public static FixtureRecoveryResult Fail(FixtureRecoveryStatus status, string detail) =>
            new FixtureRecoveryResult(status, Array.Empty<MarkedSurvivor>(), detail);
    }
}
