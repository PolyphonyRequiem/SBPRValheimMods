using System;
using System.Collections.Generic;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness
{
    // ============================================================================
    //  Niflheim 0003 — engine-free QA-only cold-reload CONFIGURATION INGRESS.
    // ----------------------------------------------------------------------------
    //  SCOPE HONESTY (read first): this is the ONE explicit, reviewable, default-off
    //  binding seam that turns the committed controller's out-of-band environment
    //  contract into the four inputs the net48 capture observer needs BEFORE it may
    //  arm: the QA-only manifest, the capture output directory, the build provenance,
    //  and (POST only) the save receipt — plus the intended boot phase. Without this
    //  seam those four observer inputs had ZERO committed writers, so the observer
    //  always evaluated Manifest=null and refused, and the controller→observer handoff
    //  was broken (the controller exported variables the C# side never read). This
    //  class closes that reachability defect.
    //
    //  It is FAIL-CLOSED on every axis and engine-free (System.* only), so it
    //  link-compiles into the net8 test project and the net48 observer alike:
    //    * a missing/blank/malformed variable is a REFUSAL, never a default-on arm;
    //    * the manifest is armed against the SOURCE-FIXED disposable fixture UID
    //      (2413287143), so a drifted WORLD_UID actually refuses (the observer used
    //      to compare the manifest UID against itself — a tautology that could never
    //      catch a wrong fixture);
    //    * forbidden production world names/ports, non-isolated fixture identity,
    //      unbounded waits, over-retry, missing lease/rollback/provenance, an unsafe
    //      (non-absolute) capture path, an unknown phase, and a POST boot with no save
    //      receipt (or a PRE boot carrying one) are all refusals;
    //    * it emits NO secrets and NO raw personal paths — only the already-approved
    //      bounded primitive fields flow through.
    //
    //  Building/registering/binding this does NOT prove live reload, persistence,
    //  deployment, or playability. It only makes the committed observer CONFIGURABLE
    //  and ARMABLE from the committed controller contract, fail-closed.
    // ============================================================================

    /// <summary>The canonical environment-variable contract the controller exports and the C# ingress reads.
    /// These names MUST agree byte-for-byte with tools/niflheim-homestead-reload-harness/controller.sh; the
    /// controller-drift guard test fails if any name here is absent from the shipped controller.</summary>
    internal static class HomesteadReloadEnv
    {
        internal const string Phase = "NIFLHEIM_RELOAD_HARNESS_PHASE";
        internal const string CaptureDir = "NIFLHEIM_RELOAD_HARNESS_CAPTURE_DIR";
        internal const string WorldUid = "NIFLHEIM_RELOAD_HARNESS_WORLD_UID";
        internal const string LeaseId = "NIFLHEIM_RELOAD_HARNESS_LEASE_ID";
        internal const string RollbackHash = "NIFLHEIM_RELOAD_HARNESS_ROLLBACK_HASH";
        internal const string DisposableDbPresent = "NIFLHEIM_RELOAD_HARNESS_DISPOSABLE_DB_PRESENT";
        internal const string DisposableFwlPresent = "NIFLHEIM_RELOAD_HARNESS_DISPOSABLE_FWL_PRESENT";
        internal const string TargetWorldName = "NIFLHEIM_RELOAD_HARNESS_TARGET_WORLD_NAME";
        internal const string TargetPort = "NIFLHEIM_RELOAD_HARNESS_TARGET_PORT";
        internal const string ReadinessWaitSeconds = "NIFLHEIM_RELOAD_HARNESS_READINESS_WAIT_SECONDS";
        internal const string PhaseWaitSeconds = "NIFLHEIM_RELOAD_HARNESS_PHASE_WAIT_SECONDS";
        internal const string ReadinessRetries = "NIFLHEIM_RELOAD_HARNESS_READINESS_RETRIES";
        internal const string ProvSourceHash = "NIFLHEIM_RELOAD_HARNESS_PROV_SOURCE_HASH";
        internal const string ProvProductHash = "NIFLHEIM_RELOAD_HARNESS_PROV_PRODUCT_HASH";
        internal const string ProvHarnessHash = "NIFLHEIM_RELOAD_HARNESS_PROV_HARNESS_HASH";
        internal const string SavePresent = "NIFLHEIM_RELOAD_HARNESS_SAVE_PRESENT";
        internal const string SaveDbHash = "NIFLHEIM_RELOAD_HARNESS_SAVE_DB_HASH";
        internal const string SaveAtUtc = "NIFLHEIM_RELOAD_HARNESS_SAVE_AT_UTC";

        /// <summary>Every variable the ingress binds. Used by the drift guard so a controller that drops a
        /// name is caught deterministically instead of silently breaking the handoff at runtime.</summary>
        internal static readonly IReadOnlyList<string> All = new[]
        {
            Phase, CaptureDir, WorldUid, LeaseId, RollbackHash, DisposableDbPresent, DisposableFwlPresent,
            TargetWorldName, TargetPort, ReadinessWaitSeconds, PhaseWaitSeconds, ReadinessRetries,
            ProvSourceHash, ProvProductHash, ProvHarnessHash, SavePresent, SaveDbHash, SaveAtUtc,
        };
    }

    /// <summary>The fully-bound, validated observer configuration the ingress produces, plus the arming decision it
    /// reached. When <see cref="IsReady"/> is false the observer MUST refuse — every reason is in <see cref="Refusals"/>.</summary>
    internal sealed class HomesteadReloadConfiguration
    {
        internal HomesteadReloadConfiguration(
            bool isReady,
            IReadOnlyList<string> refusals,
            HomesteadReloadHarnessManifest? manifest,
            string captureOutputDir,
            HomesteadReloadProvenance provenance,
            HomesteadReloadSaveReceipt saveReceipt,
            HomesteadReloadPhase phase,
            HomesteadReloadArmingDecision arming)
        {
            IsReady = isReady;
            Refusals = refusals;
            Manifest = manifest;
            CaptureOutputDir = captureOutputDir;
            Provenance = provenance;
            SaveReceipt = saveReceipt;
            Phase = phase;
            Arming = arming;
        }

        /// <summary>True only when every variable parsed, every fail-closed precondition held, AND the arming gate armed.</summary>
        internal bool IsReady { get; }
        internal IReadOnlyList<string> Refusals { get; }
        internal HomesteadReloadHarnessManifest? Manifest { get; }
        internal string CaptureOutputDir { get; }
        internal HomesteadReloadProvenance Provenance { get; }
        internal HomesteadReloadSaveReceipt SaveReceipt { get; }
        internal HomesteadReloadPhase Phase { get; }
        internal HomesteadReloadArmingDecision Arming { get; }

        internal static HomesteadReloadConfiguration Refused(IReadOnlyList<string> refusals) =>
            new HomesteadReloadConfiguration(
                false, refusals, null, string.Empty,
                new HomesteadReloadProvenance(string.Empty, string.Empty, string.Empty),
                HomesteadReloadSaveReceipt.None, HomesteadReloadPhase.Pre,
                new HomesteadReloadArmingDecision(HomesteadReloadReadiness.Refused, refusals));
    }

    /// <summary>Binds the controller's environment contract into a validated <see cref="HomesteadReloadConfiguration"/>.
    /// The net48 observer calls <see cref="Bind"/> with <see cref="Environment.GetEnvironmentVariable(string)"/>; the
    /// net8 tests call it with a dictionary-backed reader to prove reachability + every refusal deterministically.</summary>
    internal static class HomesteadReloadConfigurationIngress
    {
        /// <summary>The one source-fixed disposable Astley fixture UID the harness may ever target. Mirrors the
        /// controller's EXPECTED_UID and the runbook's fixture value; a manifest WORLD_UID that differs refuses.</summary>
        internal const long SourceFixedFixtureUid = 2413287143L;

        /// <summary>Bind + validate. <paramref name="read"/> returns the raw value for a variable name, or null when unset.
        /// Never throws for bad input — malformed values become refusals so the caller stays fail-closed.</summary>
        internal static HomesteadReloadConfiguration Bind(Func<string, string?> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            var refusals = new List<string>();

            // ── Phase ────────────────────────────────────────────────────────────
            var phaseRaw = Trimmed(read(HomesteadReloadEnv.Phase));
            HomesteadReloadPhase phase = HomesteadReloadPhase.Pre;
            bool phaseKnown = false;
            if (string.IsNullOrEmpty(phaseRaw))
                refusals.Add($"{HomesteadReloadEnv.Phase} is unset — refuse to arm without an explicit PRE/POST phase.");
            else if (string.Equals(phaseRaw, "PRE", StringComparison.OrdinalIgnoreCase))
            {
                phase = HomesteadReloadPhase.Pre;
                phaseKnown = true;
            }
            else if (string.Equals(phaseRaw, "POST", StringComparison.OrdinalIgnoreCase))
            {
                phase = HomesteadReloadPhase.Post;
                phaseKnown = true;
            }
            else
            {
                refusals.Add($"{HomesteadReloadEnv.Phase}='{phaseRaw}' is not one of PRE/POST.");
            }

            // ── Capture output directory (must be a non-empty, rooted/absolute path) ──
            var captureDir = Trimmed(read(HomesteadReloadEnv.CaptureDir));
            if (string.IsNullOrEmpty(captureDir))
                refusals.Add($"{HomesteadReloadEnv.CaptureDir} is unset — no capture destination.");
            else if (!IsSafeAbsolutePath(captureDir!))
                refusals.Add($"{HomesteadReloadEnv.CaptureDir} must be an absolute, non-relative path (got '{captureDir}').");

            // ── Manifest fields ─────────────────────────────────────────────────
            long worldUid = ParseLong(read(HomesteadReloadEnv.WorldUid), HomesteadReloadEnv.WorldUid, refusals);
            string leaseId = Trimmed(read(HomesteadReloadEnv.LeaseId)) ?? string.Empty;
            string rollbackHash = Trimmed(read(HomesteadReloadEnv.RollbackHash)) ?? string.Empty;
            bool dbPresent = ParseBool(read(HomesteadReloadEnv.DisposableDbPresent), HomesteadReloadEnv.DisposableDbPresent, refusals);
            bool fwlPresent = ParseBool(read(HomesteadReloadEnv.DisposableFwlPresent), HomesteadReloadEnv.DisposableFwlPresent, refusals);
            string targetName = Trimmed(read(HomesteadReloadEnv.TargetWorldName)) ?? string.Empty;
            if (string.IsNullOrEmpty(targetName))
                refusals.Add($"{HomesteadReloadEnv.TargetWorldName} is unset — refuse without an explicit disposable target world name.");
            int targetPort = ParseInt(read(HomesteadReloadEnv.TargetPort), HomesteadReloadEnv.TargetPort, refusals);
            double readinessWait = ParseDouble(read(HomesteadReloadEnv.ReadinessWaitSeconds), HomesteadReloadEnv.ReadinessWaitSeconds, refusals);
            double phaseWait = ParseDouble(read(HomesteadReloadEnv.PhaseWaitSeconds), HomesteadReloadEnv.PhaseWaitSeconds, refusals);
            int readinessRetries = ParseInt(read(HomesteadReloadEnv.ReadinessRetries), HomesteadReloadEnv.ReadinessRetries, refusals);

            // The ingress-bound manifest is always ENABLED: reaching this seam means the server-owned enable flag
            // was on AND the controller staged a manifest. The arming gate still fail-closes on every other axis.
            var manifest = new HomesteadReloadHarnessManifest(
                enabled: true,
                expectedWorldUid: worldUid,
                leaseId: leaseId,
                rollbackBytesHash: rollbackHash,
                disposableDbPresent: dbPresent,
                disposableFwlPresent: fwlPresent,
                targetWorldName: targetName,
                targetPort: targetPort,
                readinessWaitSeconds: readinessWait,
                phaseWaitSeconds: phaseWait,
                readinessRetries: readinessRetries);

            // ── Provenance (all three build hashes required) ─────────────────────
            string src = Trimmed(read(HomesteadReloadEnv.ProvSourceHash)) ?? string.Empty;
            string prod = Trimmed(read(HomesteadReloadEnv.ProvProductHash)) ?? string.Empty;
            string harn = Trimmed(read(HomesteadReloadEnv.ProvHarnessHash)) ?? string.Empty;
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(prod) || string.IsNullOrEmpty(harn))
                refusals.Add("Provenance is incomplete — source/product/harness build hashes are all required.");
            var provenance = new HomesteadReloadProvenance(src, prod, harn);

            // ── Save receipt (POST must carry one; PRE must not) ─────────────────
            bool savePresent = ParseBoolOptional(read(HomesteadReloadEnv.SavePresent));
            string saveDbHash = Trimmed(read(HomesteadReloadEnv.SaveDbHash)) ?? string.Empty;
            string saveAt = Trimmed(read(HomesteadReloadEnv.SaveAtUtc)) ?? string.Empty;
            var saveReceipt = savePresent
                ? new HomesteadReloadSaveReceipt(true, saveDbHash, saveAt)
                : HomesteadReloadSaveReceipt.None;

            if (phaseKnown && phase == HomesteadReloadPhase.Post)
            {
                if (!savePresent)
                    refusals.Add("POST boot has no save receipt — nothing durable was written to cold-load; refuse.");
                else if (string.IsNullOrEmpty(saveDbHash) || string.IsNullOrEmpty(saveAt))
                    refusals.Add("POST save receipt is incomplete — both the saved-db hash and save timestamp are required.");
            }
            else if (phaseKnown && phase == HomesteadReloadPhase.Pre && savePresent)
            {
                refusals.Add("PRE boot must not carry a save receipt (it precedes the save) — refuse.");
            }

            // ── Arming gate against the SOURCE-FIXED fixture UID (fixes the tautology) ──
            var arming = HomesteadReloadArmingGate.Evaluate(manifest, SourceFixedFixtureUid);
            if (!arming.IsArmed)
                refusals.AddRange(arming.Refusals);

            if (refusals.Count > 0)
                return HomesteadReloadConfiguration.Refused(refusals);

            return new HomesteadReloadConfiguration(
                true, Array.Empty<string>(), manifest, captureDir!, provenance, saveReceipt, phase, arming);
        }

        private static string? Trimmed(string? value) => value?.Trim();

        /// <summary>Fail-closed path check: must be a rooted absolute path, no relative traversal. Engine-free; we
        /// reject anything not clearly absolute rather than trust a caller-supplied relative/ambiguous destination.</summary>
        private static bool IsSafeAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            // POSIX absolute (/...) or Windows absolute (X:\ or X:/). The harness runs on the net48 client host.
            if (path[0] == '/') return true;
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return true;
            return false;
        }

        private static bool ParseBool(string? raw, string name, List<string> refusals)
        {
            var t = Trimmed(raw);
            if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;
            refusals.Add($"{name} must be 'true' or 'false' (got '{raw}').");
            return false;
        }

        private static bool ParseBoolOptional(string? raw)
        {
            var t = Trimmed(raw);
            return string.Equals(t, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static long ParseLong(string? raw, string name, List<string> refusals)
        {
            var t = Trimmed(raw);
            if (!string.IsNullOrEmpty(t) &&
                long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            refusals.Add($"{name} must be an integer (got '{raw}').");
            return 0L;
        }

        private static int ParseInt(string? raw, string name, List<string> refusals)
        {
            var t = Trimmed(raw);
            if (!string.IsNullOrEmpty(t) &&
                int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            refusals.Add($"{name} must be an integer (got '{raw}').");
            return int.MinValue;
        }

        private static double ParseDouble(string? raw, string name, List<string> refusals)
        {
            var t = Trimmed(raw);
            if (!string.IsNullOrEmpty(t) &&
                double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            refusals.Add($"{name} must be a number (got '{raw}').");
            return double.NaN;
        }
    }
}
