using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness
{
    // ============================================================================
    //  Niflheim 0003 — engine-free PRE/POST cold-reload COMPARISON (fail-closed).
    // ----------------------------------------------------------------------------
    //  SCOPE HONESTY: this decides whether two INDEPENDENTLY captured boots
    //  (a warm-authored PRE and a cold-reloaded POST of the SAME disposable world)
    //  are identity- and count-stable. It is a pure comparator over already-captured
    //  primitive facts. It DOES NOT run Valheim and it CANNOT, by itself, prove a
    //  cold reload happened — that is enforced structurally by requiring the two
    //  captures to differ on process/session/boot-generation and to carry a real
    //  save receipt. A PASS here is "the captured facts are consistent with a real
    //  cold reload"; the LIVE run that produces those captures is the acceptance
    //  gate, still owned by kanban t_1a1164f4.
    //
    //  Every REJECT below is fail-closed: an ambiguous or missing precondition is a
    //  FAIL, never a silent pass. The controller/runbook consumes the typed verdict.
    // ============================================================================

    internal enum HomesteadReloadVerdict
    {
        /// <summary>PRE and POST are identity/count stable AND every cold-reload precondition held.</summary>
        Pass,
        /// <summary>A precondition failed or the sets diverged. See <see cref="HomesteadReloadComparison.Failures"/>.</summary>
        Fail,
    }

    /// <summary>The typed result of comparing a PRE and a POST capture. Immutable; carries every failure reason so
    /// the controller can print an exact, machine-readable rejection instead of a bare boolean.</summary>
    internal sealed class HomesteadReloadComparison
    {
        internal HomesteadReloadComparison(HomesteadReloadVerdict verdict, IReadOnlyList<string> failures)
        {
            Verdict = verdict;
            Failures = failures;
        }

        internal HomesteadReloadVerdict Verdict { get; }
        internal IReadOnlyList<string> Failures { get; }
        internal bool IsPass => Verdict == HomesteadReloadVerdict.Pass;
    }

    /// <summary>The pure PRE/POST fail-closed comparator. Rejects wrong world UID, same process/session, a missing
    /// POST save receipt, mismatched build hashes, duplicate/missing hosts, count drift, and identity-set drift.</summary>
    internal static class HomesteadReloadComparer
    {
        internal static HomesteadReloadComparison Compare(
            HomesteadReloadCapture pre,
            HomesteadReloadCapture post,
            long expectedWorldUid)
        {
            if (pre == null) throw new ArgumentNullException(nameof(pre));
            if (post == null) throw new ArgumentNullException(nameof(post));

            var failures = new List<string>();

            // ── Phase separation ────────────────────────────────────────────────
            if (pre.Phase != HomesteadReloadPhase.Pre)
                failures.Add($"PRE capture has phase {pre.Phase}, expected Pre.");
            if (post.Phase != HomesteadReloadPhase.Post)
                failures.Add($"POST capture has phase {post.Phase}, expected Post.");

            // ── World-UID fail-closed check (both boots + the OPERATE-declared fixture) ──
            if (pre.WorldUid != expectedWorldUid)
                failures.Add($"PRE world UID {pre.WorldUid} != expected disposable fixture UID {expectedWorldUid}.");
            if (post.WorldUid != expectedWorldUid)
                failures.Add($"POST world UID {post.WorldUid} != expected disposable fixture UID {expectedWorldUid}.");
            if (pre.WorldUid != post.WorldUid)
                failures.Add($"PRE world UID {pre.WorldUid} != POST world UID {post.WorldUid}.");
            if (pre.WorldIdentity != post.WorldIdentity)
                failures.Add($"World identity drift: PRE '{pre.WorldIdentity}' != POST '{post.WorldIdentity}'.");

            // ── Selector identity must be stable ────────────────────────────────
            if (pre.SelectorVersion != post.SelectorVersion)
                failures.Add($"Selector version drift: PRE '{pre.SelectorVersion}' != POST '{post.SelectorVersion}'.");
            if (!pre.MinimumDistance.Equals(post.MinimumDistance))
                failures.Add("Selector minimumDistance drift between PRE and POST.");
            if (!pre.Density.Equals(post.Density))
                failures.Add("Selector density drift between PRE and POST.");

            // ── Build/artifact provenance must be identical (same bytes ran both boots) ──
            if (!pre.Provenance.Equals(post.Provenance))
                failures.Add("Build/artifact hash drift: PRE and POST ran different source/product/harness bytes.");

            // ── Cold-reload separation: two DIFFERENT processes/sessions/generations ──
            if (pre.Session.ProcessId == post.Session.ProcessId)
                failures.Add($"Same process id {pre.Session.ProcessId} for PRE and POST — no full client exit occurred.");
            if (pre.Session.SessionId == post.Session.SessionId)
                failures.Add($"Same session id {pre.Session.SessionId} for PRE and POST — same-session round-trip, not a cold reload.");
            if (pre.Session.BootId == post.Session.BootId)
                failures.Add("Same boot id for PRE and POST — not two independent boots.");
            if (post.Session.BootGeneration <= pre.Session.BootGeneration)
                failures.Add($"POST boot generation {post.Session.BootGeneration} does not strictly follow PRE {pre.Session.BootGeneration}.");

            // ── Save receipt: the POST cold-load must have had real durable bytes to load ──
            if (!post.SaveReceipt.Present || string.IsNullOrEmpty(post.SaveReceipt.DbFileHash))
                failures.Add("POST capture carries no world-save receipt — nothing durable was saved to cold-load.");

            // ── Count stability ─────────────────────────────────────────────────
            if (pre.CandidateCount != post.CandidateCount)
                failures.Add($"Candidate count drift: PRE {pre.CandidateCount} != POST {post.CandidateCount}.");
            if (pre.AssignedCount != post.AssignedCount)
                failures.Add($"Assigned count drift: PRE {pre.AssignedCount} != POST {post.AssignedCount}.");

            // ── Per-host identity set: no duplicates, no missing, exact set equality ──
            AssertDistinctHosts(pre, "PRE", failures);
            AssertDistinctHosts(post, "POST", failures);

            var preSet = pre.Hosts.Select(h => h.Canonical).ToHashSet(StringComparer.Ordinal);
            var postSet = post.Hosts.Select(h => h.Canonical).ToHashSet(StringComparer.Ordinal);
            var missing = preSet.Except(postSet).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var added = postSet.Except(preSet).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var m in missing)
                failures.Add($"Host present in PRE but MISSING after reload: {m}.");
            foreach (var a in added)
                failures.Add($"Host present in POST but ABSENT before reload (accumulation): {a}.");

            // The assigned count must equal the host-set size (each assignment is one host).
            if (post.Hosts.Count != post.AssignedCount)
                failures.Add($"POST host-set size {post.Hosts.Count} != assigned count {post.AssignedCount}.");

            var verdict = failures.Count == 0 ? HomesteadReloadVerdict.Pass : HomesteadReloadVerdict.Fail;
            return new HomesteadReloadComparison(verdict, failures);
        }

        private static void AssertDistinctHosts(HomesteadReloadCapture capture, string label, List<string> failures)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var host in capture.Hosts)
            {
                if (!seen.Add(host.Canonical))
                    failures.Add($"{label} host set contains a DUPLICATE: {host.Canonical}.");
            }

            // Canonical ordering guard: the emitted host list must be sorted (deterministic serialization).
            for (var i = 1; i < capture.Hosts.Count; i++)
            {
                if (capture.Hosts[i - 1].CompareTo(capture.Hosts[i]) > 0)
                {
                    failures.Add($"{label} host set is not canonically sorted at index {i}.");
                    break;
                }
            }
        }
    }
}
