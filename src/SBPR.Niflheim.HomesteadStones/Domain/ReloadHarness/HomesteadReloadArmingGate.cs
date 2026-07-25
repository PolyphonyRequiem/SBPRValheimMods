using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness
{
    // ============================================================================
    //  Niflheim 0003 — engine-free QA-only ENABLEMENT + FIXTURE PRECONDITIONS.
    // ----------------------------------------------------------------------------
    //  SCOPE HONESTY: this decides whether the cold-reload harness is ALLOWED to
    //  arm and run at all. It is fail-closed on every axis:
    //    * the harness is INERT unless an explicit QA-only manifest enables it;
    //    * it refuses to proceed without an OPERATE-supplied lease, rollback bytes,
    //      and a disposable Astley .db/.fwl fixture at the exact expected UID;
    //    * it refuses any production Niflheim/Heistan world/target/port name;
    //    * it requires bounded (finite, positive) waits and at most one readiness retry.
    //  It does NOT run Valheim; it only gates. The net48 observer asks this model
    //  "may I arm?" before touching anything.
    // ============================================================================

    /// <summary>The QA-only enablement manifest. Absent/disabled ⇒ the harness is dead code at runtime. Never
    /// defaulted on: normal product boots leave <see cref="Enabled"/> false so the harness never arms in shipping.</summary>
    internal sealed class HomesteadReloadHarnessManifest
    {
        internal HomesteadReloadHarnessManifest(
            bool enabled,
            long expectedWorldUid,
            string leaseId,
            string rollbackBytesHash,
            bool disposableDbPresent,
            bool disposableFwlPresent,
            string targetWorldName,
            int targetPort,
            double readinessWaitSeconds,
            double phaseWaitSeconds,
            int readinessRetries)
        {
            Enabled = enabled;
            ExpectedWorldUid = expectedWorldUid;
            LeaseId = leaseId ?? string.Empty;
            RollbackBytesHash = rollbackBytesHash ?? string.Empty;
            DisposableDbPresent = disposableDbPresent;
            DisposableFwlPresent = disposableFwlPresent;
            TargetWorldName = targetWorldName ?? string.Empty;
            TargetPort = targetPort;
            ReadinessWaitSeconds = readinessWaitSeconds;
            PhaseWaitSeconds = phaseWaitSeconds;
            ReadinessRetries = readinessRetries;
        }

        /// <summary>Master enable. The ONLY thing that arms the harness; false on any non-QA boot.</summary>
        internal bool Enabled { get; }
        internal long ExpectedWorldUid { get; }
        /// <summary>The OPERATE-issued exclusive runtime lease id. Empty ⇒ refuse.</summary>
        internal string LeaseId { get; }
        /// <summary>SHA-256 of the pre-run rollback bytes OPERATE staged. Empty ⇒ refuse.</summary>
        internal string RollbackBytesHash { get; }
        internal bool DisposableDbPresent { get; }
        internal bool DisposableFwlPresent { get; }
        internal string TargetWorldName { get; }
        internal int TargetPort { get; }
        internal double ReadinessWaitSeconds { get; }
        internal double PhaseWaitSeconds { get; }
        internal int ReadinessRetries { get; }
    }

    /// <summary>Names/ports the harness must NEVER target. Production worlds and their ports are hard-forbidden;
    /// a manifest naming any of them is refused even if every other field is valid.</summary>
    internal static class HomesteadReloadProductionGuard
    {
        // Ordinal, case-insensitive forbidden world/target name substrings.
        internal static readonly string[] ForbiddenNames = { "niflheim", "heistan" };

        // Production ports the harness must never bind/target.
        internal static readonly int[] ForbiddenPorts = { 2456, 2457, 2466, 2467 };

        internal static bool IsForbiddenName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name!.ToLowerInvariant();
            return ForbiddenNames.Any(term => lower.IndexOf(term, StringComparison.Ordinal) >= 0);
        }

        internal static bool IsForbiddenPort(int port) => ForbiddenPorts.Contains(port);
    }

    internal enum HomesteadReloadReadiness
    {
        Armed,
        Refused,
    }

    /// <summary>The typed arming decision: whether the harness may proceed, and every reason it may not.</summary>
    internal sealed class HomesteadReloadArmingDecision
    {
        internal HomesteadReloadArmingDecision(HomesteadReloadReadiness readiness, IReadOnlyList<string> refusals)
        {
            Readiness = readiness;
            Refusals = refusals;
        }

        internal HomesteadReloadReadiness Readiness { get; }
        internal IReadOnlyList<string> Refusals { get; }
        internal bool IsArmed => Readiness == HomesteadReloadReadiness.Armed;
    }

    /// <summary>The pure fail-closed arming gate. Given a manifest and the expected disposable fixture UID, returns
    /// Armed only when the harness is explicitly enabled, leased, rollback-backed, fixture-present, non-production,
    /// and bounded-waited. Any missing/ambiguous precondition is a refusal.</summary>
    internal static class HomesteadReloadArmingGate
    {
        internal const double MaxWaitSeconds = 900.0; // 15 minutes hard ceiling on any single bounded wait.
        internal const int MaxReadinessRetries = 1;    // "one controlled readiness retry at most".

        internal static HomesteadReloadArmingDecision Evaluate(
            HomesteadReloadHarnessManifest? manifest,
            long expectedFixtureUid)
        {
            var refusals = new List<string>();

            if (manifest == null)
            {
                refusals.Add("No QA-only harness manifest present — harness stays inert.");
                return new HomesteadReloadArmingDecision(HomesteadReloadReadiness.Refused, refusals);
            }

            if (!manifest.Enabled)
                refusals.Add("Harness manifest is not enabled — inert in normal product use.");

            if (manifest.ExpectedWorldUid != expectedFixtureUid)
                refusals.Add(
                    $"Manifest world UID {manifest.ExpectedWorldUid} != expected disposable fixture UID {expectedFixtureUid}.");

            if (string.IsNullOrWhiteSpace(manifest.LeaseId))
                refusals.Add("No OPERATE-supplied lease id — refuse to proceed.");

            if (string.IsNullOrWhiteSpace(manifest.RollbackBytesHash))
                refusals.Add("No rollback-bytes hash — refuse to proceed without a rollback path.");

            if (!manifest.DisposableDbPresent)
                refusals.Add("Disposable Astley .db fixture is absent — cannot cold-load.");
            if (!manifest.DisposableFwlPresent)
                refusals.Add("Disposable Astley .fwl fixture is absent — cannot cold-load.");

            if (HomesteadReloadProductionGuard.IsForbiddenName(manifest.TargetWorldName))
                refusals.Add($"Target world name '{manifest.TargetWorldName}' names a forbidden production world.");
            if (HomesteadReloadProductionGuard.IsForbiddenPort(manifest.TargetPort))
                refusals.Add($"Target port {manifest.TargetPort} is a forbidden production port.");

            AssertBoundedWait("readiness", manifest.ReadinessWaitSeconds, refusals);
            AssertBoundedWait("phase", manifest.PhaseWaitSeconds, refusals);

            if (manifest.ReadinessRetries < 0 || manifest.ReadinessRetries > MaxReadinessRetries)
                refusals.Add(
                    $"Readiness retries {manifest.ReadinessRetries} out of bounds [0..{MaxReadinessRetries}].");

            var readiness = refusals.Count == 0
                ? HomesteadReloadReadiness.Armed
                : HomesteadReloadReadiness.Refused;
            return new HomesteadReloadArmingDecision(readiness, refusals);
        }

        private static void AssertBoundedWait(string label, double seconds, List<string> refusals)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0)
                refusals.Add($"{label} wait must be a finite positive number of seconds (got {seconds.ToString("R", CultureInfo.InvariantCulture)}).");
            else if (seconds > MaxWaitSeconds)
                refusals.Add($"{label} wait {seconds.ToString("R", CultureInfo.InvariantCulture)}s exceeds the {MaxWaitSeconds}s bounded ceiling.");
        }
    }
}
