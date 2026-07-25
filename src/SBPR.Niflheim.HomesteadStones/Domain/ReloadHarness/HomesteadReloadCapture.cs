using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness
{
    // ============================================================================
    //  Niflheim 0003 — engine-free cold-reload CAPTURE model (QA-only harness core).
    // ----------------------------------------------------------------------------
    //  SCOPE HONESTY (read first): this file is the engine-free brain of the live
    //  cold-reload capture harness. It does NOT run Valheim, does NOT save/load a
    //  world, and does NOT prove reload, persistence, deployment, or playability.
    //  It:
    //    * builds ONE bounded, machine-readable set of PRIMITIVE FACTS per client
    //      boot (PRE or POST) by calling the SHIPPED production HomesteadSelector.Select
    //      over the candidate facts a live ZoneSystem enumerated — it never reimplements
    //      the selector and never projects two snapshots from one literal;
    //    * redacts secrets / personal / provider identity from every emitted field;
    //    * canonically serializes the facts so two independent boots produce comparable,
    //      stable bytes.
    //  The net48 harness observer (Features/ReloadHarness/) supplies the live
    //  ZoneSystem candidate facts and the authoritative reconciliation receipt; this
    //  core stays free of UnityEngine / HarmonyLib / Valheim so it link-compiles into
    //  the net8 test project and is exercised headless.
    // ============================================================================

    /// <summary>Which of the two independent client boots produced a capture. PRE is captured on the first
    /// (warm-authored) boot before the save + full client exit; POST is captured after the cold reload of the
    /// SAME disposable world in a DIFFERENT process/session. The two are never derived from one literal.</summary>
    internal enum HomesteadReloadPhase
    {
        Pre,
        Post,
    }

    /// <summary>One authoritative reconciliation receipt element: a resident Stone's full stable ZDO id and the
    /// decision the shipped <see cref="StoneReconciler"/> reached for it. Engine-free string form so the harness
    /// never leaks a live ZDO handle into emitted facts.</summary>
    internal readonly struct HomesteadReloadReconcileEntry : IEquatable<HomesteadReloadReconcileEntry>
    {
        internal HomesteadReloadReconcileEntry(string zdoId, string prefab, int zoneX, int zoneZ, bool removed)
        {
            ZdoId = zdoId ?? throw new ArgumentNullException(nameof(zdoId));
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            Removed = removed;
        }

        /// <summary>Full stable ZDO id as "UserId:Id" (never truncated).</summary>
        internal string ZdoId { get; }
        internal string Prefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        /// <summary>True when the reconciler reaped this Stone (stale/unselected/duplicate); false when kept/selected.</summary>
        internal bool Removed { get; }

        internal string Canonical => string.Join("|", new[]
        {
            ZdoId,
            Prefab,
            ZoneX.ToString(CultureInfo.InvariantCulture),
            ZoneZ.ToString(CultureInfo.InvariantCulture),
            Removed ? "removed" : "selected",
        });

        public bool Equals(HomesteadReloadReconcileEntry other) =>
            ZdoId == other.ZdoId && Prefab == other.Prefab && ZoneX == other.ZoneX &&
            ZoneZ == other.ZoneZ && Removed == other.Removed;
        public override bool Equals(object? obj) => obj is HomesteadReloadReconcileEntry other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ZdoId.GetHashCode();
                hash = (hash * 397) ^ Prefab.GetHashCode();
                hash = (hash * 397) ^ ZoneX;
                hash = (hash * 397) ^ ZoneZ;
                return (hash * 397) ^ Removed.GetHashCode();
            }
        }
    }

    /// <summary>One per-host assignment identity fact: (prefab, zoneX, zoneZ). The SET of these across a boot is
    /// the identity surface the reload gate compares. Sorted canonically so enumeration order never matters.</summary>
    internal readonly struct HomesteadReloadHost : IEquatable<HomesteadReloadHost>, IComparable<HomesteadReloadHost>
    {
        internal HomesteadReloadHost(string prefab, int zoneX, int zoneZ)
        {
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
        }

        internal string Prefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }

        internal string Canonical =>
            Prefab + "|" + ZoneX.ToString(CultureInfo.InvariantCulture) + "|" + ZoneZ.ToString(CultureInfo.InvariantCulture);

        public int CompareTo(HomesteadReloadHost other)
        {
            var byPrefab = string.CompareOrdinal(Prefab, other.Prefab);
            if (byPrefab != 0) return byPrefab;
            if (ZoneX != other.ZoneX) return ZoneX.CompareTo(other.ZoneX);
            return ZoneZ.CompareTo(other.ZoneZ);
        }

        public bool Equals(HomesteadReloadHost other) =>
            Prefab == other.Prefab && ZoneX == other.ZoneX && ZoneZ == other.ZoneZ;
        public override bool Equals(object? obj) => obj is HomesteadReloadHost other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Prefab.GetHashCode();
                hash = (hash * 397) ^ ZoneX;
                return (hash * 397) ^ ZoneZ;
            }
        }
    }

    /// <summary>The per-boot session generation identity. Two independent boots MUST differ on all three of these
    /// or the "full client exit + cold reload" claim is unproven (an in-process reload / same-session round-trip
    /// would share them). Engine-free strings; no secrets.</summary>
    internal readonly struct HomesteadReloadSession : IEquatable<HomesteadReloadSession>
    {
        internal HomesteadReloadSession(string bootId, string sessionId, string processId, long bootGeneration)
        {
            BootId = bootId ?? throw new ArgumentNullException(nameof(bootId));
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            ProcessId = processId ?? throw new ArgumentNullException(nameof(processId));
            BootGeneration = bootGeneration;
        }

        /// <summary>Per-boot random id minted once at client boot (a fresh GUID). Distinguishes two cold boots.</summary>
        internal string BootId { get; }
        /// <summary>The engine session/world-connection generation (e.g. ZDOMan session id) for this boot.</summary>
        internal string SessionId { get; }
        /// <summary>The OS process id of the graphical client that produced this capture.</summary>
        internal string ProcessId { get; }
        /// <summary>A monotonic counter the controller stamps (PRE=1, POST=2) so PRE strictly precedes POST.</summary>
        internal long BootGeneration { get; }

        public bool Equals(HomesteadReloadSession other) =>
            BootId == other.BootId && SessionId == other.SessionId &&
            ProcessId == other.ProcessId && BootGeneration == other.BootGeneration;
        public override bool Equals(object? obj) => obj is HomesteadReloadSession other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = BootId.GetHashCode();
                hash = (hash * 397) ^ SessionId.GetHashCode();
                hash = (hash * 397) ^ ProcessId.GetHashCode();
                return (hash * 397) ^ BootGeneration.GetHashCode();
            }
        }
    }

    /// <summary>The immutable build/artifact provenance a boot ran under. PRE and POST must share these exactly —
    /// a hash drift means the two boots ran different bytes and the comparison is invalid.</summary>
    internal readonly struct HomesteadReloadProvenance : IEquatable<HomesteadReloadProvenance>
    {
        internal HomesteadReloadProvenance(string sourceHash, string productHash, string harnessHash)
        {
            SourceHash = sourceHash ?? throw new ArgumentNullException(nameof(sourceHash));
            ProductHash = productHash ?? throw new ArgumentNullException(nameof(productHash));
            HarnessHash = harnessHash ?? throw new ArgumentNullException(nameof(harnessHash));
        }

        internal string SourceHash { get; }
        internal string ProductHash { get; }
        internal string HarnessHash { get; }

        public bool Equals(HomesteadReloadProvenance other) =>
            SourceHash == other.SourceHash && ProductHash == other.ProductHash && HarnessHash == other.HarnessHash;
        public override bool Equals(object? obj) => obj is HomesteadReloadProvenance other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SourceHash.GetHashCode();
                hash = (hash * 397) ^ ProductHash.GetHashCode();
                return (hash * 397) ^ HarnessHash.GetHashCode();
            }
        }
    }

    /// <summary>The bounded save receipt a POST boot must carry: proof a REAL Valheim world save was requested and
    /// acknowledged before the client was terminated. A PRE capture with no save receipt is fine; a POST capture
    /// with no save receipt is fail-closed (nothing durable was written to cold-load).</summary>
    internal readonly struct HomesteadReloadSaveReceipt : IEquatable<HomesteadReloadSaveReceipt>
    {
        internal static readonly HomesteadReloadSaveReceipt None = new HomesteadReloadSaveReceipt(false, string.Empty, string.Empty);

        internal HomesteadReloadSaveReceipt(bool present, string dbFileHash, string savedAtUtc)
        {
            Present = present;
            DbFileHash = dbFileHash ?? string.Empty;
            SavedAtUtc = savedAtUtc ?? string.Empty;
        }

        /// <summary>True when a real world-save was observed and its bytes hashed.</summary>
        internal bool Present { get; }
        /// <summary>SHA-256 of the saved world .db bytes at save time (never the file path, never contents).</summary>
        internal string DbFileHash { get; }
        internal string SavedAtUtc { get; }

        public bool Equals(HomesteadReloadSaveReceipt other) =>
            Present == other.Present && DbFileHash == other.DbFileHash && SavedAtUtc == other.SavedAtUtc;
        public override bool Equals(object? obj) => obj is HomesteadReloadSaveReceipt other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Present.GetHashCode();
                hash = (hash * 397) ^ DbFileHash.GetHashCode();
                return (hash * 397) ^ SavedAtUtc.GetHashCode();
            }
        }
    }

    /// <summary>The complete set of PRIMITIVE FACTS captured on ONE client boot. Immutable; canonically
    /// serializable; secret-free by construction (built through <see cref="HomesteadReloadCaptureBuilder"/>,
    /// which runs the shipped selector and scrubs every string field).</summary>
    internal sealed class HomesteadReloadCapture
    {
        internal const string Schema = "niflheim-homestead-reload-capture-v1";

        internal HomesteadReloadCapture(
            HomesteadReloadPhase phase,
            long worldUid,
            string worldIdentity,
            string selectorVersion,
            double minimumDistance,
            double density,
            int candidateCount,
            int assignedCount,
            double minimumPairwiseDistance,
            IReadOnlyList<HomesteadReloadHost> hosts,
            IReadOnlyList<HomesteadReloadReconcileEntry> reconciliation,
            HomesteadReloadSession session,
            HomesteadReloadProvenance provenance,
            HomesteadReloadSaveReceipt saveReceipt,
            string capturedAtUtc)
        {
            Phase = phase;
            WorldUid = worldUid;
            WorldIdentity = worldIdentity ?? throw new ArgumentNullException(nameof(worldIdentity));
            SelectorVersion = selectorVersion ?? throw new ArgumentNullException(nameof(selectorVersion));
            MinimumDistance = minimumDistance;
            Density = density;
            CandidateCount = candidateCount;
            AssignedCount = assignedCount;
            MinimumPairwiseDistance = minimumPairwiseDistance;
            Hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
            Reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
            Session = session;
            Provenance = provenance;
            SaveReceipt = saveReceipt;
            CapturedAtUtc = capturedAtUtc ?? throw new ArgumentNullException(nameof(capturedAtUtc));
        }

        internal HomesteadReloadPhase Phase { get; }
        internal long WorldUid { get; }
        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal double MinimumDistance { get; }
        internal double Density { get; }
        internal int CandidateCount { get; }
        internal int AssignedCount { get; }
        internal double MinimumPairwiseDistance { get; }
        /// <summary>The sorted per-host (prefab, zoneX, zoneZ) identity set.</summary>
        internal IReadOnlyList<HomesteadReloadHost> Hosts { get; }
        internal IReadOnlyList<HomesteadReloadReconcileEntry> Reconciliation { get; }
        internal HomesteadReloadSession Session { get; }
        internal HomesteadReloadProvenance Provenance { get; }
        internal HomesteadReloadSaveReceipt SaveReceipt { get; }
        internal string CapturedAtUtc { get; }

        /// <summary>The selected/kept reconciliation ZDO ids (identity receipt for created Stones).</summary>
        internal IEnumerable<string> SelectedZdoIds => Reconciliation.Where(r => !r.Removed).Select(r => r.ZdoId);
        /// <summary>The stale/removed reconciliation ZDO ids (proof stale assignments were reaped, not accumulated).</summary>
        internal IEnumerable<string> RemovedZdoIds => Reconciliation.Where(r => r.Removed).Select(r => r.ZdoId);

        /// <summary>Deterministic, whitespace-stable canonical text of every primitive fact. Two independent boots
        /// that observed the same world produce byte-identical canonical text for the world/selector/host surface;
        /// the session/process fields deliberately DIFFER (that is what proves a real cold reload). Contains no
        /// JSON dependency so it stays free of System.Text.Json versioning.</summary>
        internal string ToCanonicalText()
        {
            var sb = new StringBuilder();
            sb.Append("schema=").Append(Schema).Append('\n');
            sb.Append("phase=").Append(Phase == HomesteadReloadPhase.Pre ? "PRE" : "POST").Append('\n');
            sb.Append("world.uid=").Append(WorldUid.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("world.identity=").Append(WorldIdentity).Append('\n');
            sb.Append("selector.version=").Append(SelectorVersion).Append('\n');
            sb.Append("selector.minimumDistance=").Append(MinimumDistance.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("selector.density=").Append(Density.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("counts.candidates=").Append(CandidateCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("counts.assigned=").Append(AssignedCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("counts.minimumPairwiseDistance=").Append(MinimumPairwiseDistance.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("hosts.count=").Append(Hosts.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var host in Hosts)
                sb.Append("host=").Append(host.Canonical).Append('\n');
            sb.Append("reconcile.count=").Append(Reconciliation.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var entry in Reconciliation.OrderBy(e => e.Canonical, StringComparer.Ordinal))
                sb.Append("reconcile=").Append(entry.Canonical).Append('\n');
            sb.Append("session.bootId=").Append(Session.BootId).Append('\n');
            sb.Append("session.sessionId=").Append(Session.SessionId).Append('\n');
            sb.Append("session.processId=").Append(Session.ProcessId).Append('\n');
            sb.Append("session.bootGeneration=").Append(Session.BootGeneration.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("provenance.sourceHash=").Append(Provenance.SourceHash).Append('\n');
            sb.Append("provenance.productHash=").Append(Provenance.ProductHash).Append('\n');
            sb.Append("provenance.harnessHash=").Append(Provenance.HarnessHash).Append('\n');
            sb.Append("save.present=").Append(SaveReceipt.Present ? "true" : "false").Append('\n');
            sb.Append("save.dbFileHash=").Append(SaveReceipt.DbFileHash).Append('\n');
            sb.Append("save.savedAtUtc=").Append(SaveReceipt.SavedAtUtc).Append('\n');
            sb.Append("capturedAtUtc=").Append(CapturedAtUtc).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Redacts secrets / personal / provider identity from any string that would be emitted. A capture that
    /// still trips the scan is REJECTED at build time — the harness never writes a secret-bearing fact.</summary>
    internal static class HomesteadReloadSecretScan
    {
        // Ordinal, case-insensitive substrings that must never appear in an emitted fact. Intentionally broad:
        // the harness emits only primitive world/selector geometry + opaque ids, so any of these is a bug.
        private static readonly string[] Forbidden =
        {
            "password", "passwd", "secret", "token", "apikey", "api_key", "bearer",
            "private_key", "privatekey", "-----begin", "steamid", "steam_id",
            "provider=", "auth=", "session_pass", "server_pass",
        };

        internal static bool IsClean(string? value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            var lower = value!.ToLowerInvariant();
            return Forbidden.All(term => lower.IndexOf(term, StringComparison.Ordinal) < 0);
        }

        /// <summary>Every emitted string field of a capture, for scanning.</summary>
        internal static IEnumerable<string> EmittedStrings(HomesteadReloadCapture capture)
        {
            yield return capture.WorldIdentity;
            yield return capture.SelectorVersion;
            foreach (var host in capture.Hosts) yield return host.Prefab;
            foreach (var entry in capture.Reconciliation)
            {
                yield return entry.ZdoId;
                yield return entry.Prefab;
            }
            yield return capture.Session.BootId;
            yield return capture.Session.SessionId;
            yield return capture.Session.ProcessId;
            yield return capture.Provenance.SourceHash;
            yield return capture.Provenance.ProductHash;
            yield return capture.Provenance.HarnessHash;
            yield return capture.SaveReceipt.DbFileHash;
            yield return capture.SaveReceipt.SavedAtUtc;
            yield return capture.CapturedAtUtc;
        }

        internal static bool CaptureIsClean(HomesteadReloadCapture capture) =>
            EmittedStrings(capture).All(IsClean);
    }

    /// <summary>Thrown when a capture cannot be built from valid, secret-free production-selector output. The harness
    /// fails closed — it never emits a partial or scrubbed-around fact set.</summary>
    internal sealed class HomesteadReloadCaptureException : Exception
    {
        internal HomesteadReloadCaptureException(string message) : base(message) { }
    }

    /// <summary>Builds a <see cref="HomesteadReloadCapture"/> by running the SHIPPED production
    /// <see cref="HomesteadSelector.Select"/> over the candidate facts a live ZoneSystem enumerated. The net48
    /// observer calls this with real candidates + the authoritative reconciliation receipt; the net8 tests call it
    /// with fixture candidates to prove the production selector is reachable and the schema is complete.</summary>
    internal static class HomesteadReloadCaptureBuilder
    {
        internal static HomesteadReloadCapture Build(
            HomesteadReloadPhase phase,
            long worldUid,
            string selectorVersion,
            double minimumDistance,
            double density,
            IReadOnlyCollection<HomesteadCandidate> candidates,
            IReadOnlyList<HomesteadReloadReconcileEntry> reconciliation,
            HomesteadReloadSession session,
            HomesteadReloadProvenance provenance,
            HomesteadReloadSaveReceipt saveReceipt,
            string capturedAtUtc)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (reconciliation == null) throw new ArgumentNullException(nameof(reconciliation));

            var worldIdentity = HomesteadWorldIdentity.FromUid(worldUid);
            var config = new HomesteadSelectionConfig(worldIdentity, selectorVersion, minimumDistance, density);

            // PRODUCTION PATH: the shipped selector decides the assignment set. The harness never reimplements it.
            var selection = HomesteadSelector.Select(candidates, config);

            var hosts = selection.Selected
                .Select(c => new HomesteadReloadHost(c.Prefab, c.ZoneX, c.ZoneZ))
                .OrderBy(h => h)
                .ToList();

            var minimumPairwise = selection.Selected.Count < 2
                ? 0.0
                : selection.Selected
                    .SelectMany((a, i) => selection.Selected.Skip(i + 1).Select(b => Math.Sqrt(a.DistanceSquaredTo(b))))
                    .Min();

            var capture = new HomesteadReloadCapture(
                phase,
                worldUid,
                worldIdentity,
                selectorVersion,
                minimumDistance,
                density,
                candidates.Count,
                selection.Selected.Count,
                minimumPairwise,
                hosts,
                reconciliation.OrderBy(e => e.Canonical, StringComparer.Ordinal).ToList(),
                session,
                provenance,
                saveReceipt,
                capturedAtUtc);

            if (!HomesteadReloadSecretScan.CaptureIsClean(capture))
                throw new HomesteadReloadCaptureException(
                    "Capture rejected: an emitted fact tripped the secret/PII scan. The harness fails closed rather " +
                    "than emit a secret-bearing capture.");

            return capture;
        }
    }
}
