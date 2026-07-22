using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Qa.SplitSessionHarness
{
    // ════════════════════════════════════════════════════════════════════════
    //  IAP-015 Option-B — same-account concurrent-session split-proof harness.
    // ------------------------------------------------------------------------
    //  The SHIPPED-BINARY half of AT-AIP-DEDICATED-SECOND-SESSION-REJECT.
    //
    //  WHY THIS EXISTS (architect DECIDE t_13db2c95, comment 1886): the server-
    //  authoritative one-account/one-session invariant (AIP-FR-013 / spec rule #5 /
    //  AIP-SC-002) is enforced at the AccountAdmissionIndex.TryReserve /
    //  LiveSessionAdmission.Admit seam, which sits UPSTREAM of and independent from
    //  Steam's transport layer. Steam enforces one live session per account
    //  CLIENT-SIDE (a second login kicks the first), so no supported joined-GUI
    //  path can ever deliver two concurrent transport peers of ONE account to the
    //  server seam. The live-GUI half of this AT therefore proves the real
    //  transport/auth/AccountId/admission wiring; THIS harness proves the same-
    //  account concurrent rejection at its real seam, over the shipped compiled
    //  admission code path.
    //
    //  EXACT-BINARY LINKAGE (task requirement, not a source double): this harness
    //  BINARY-references the compiled candidate product assembly and calls the
    //  shipped types with typed calls (no reflection into internals, no source
    //  <Compile Include>). Before exercising anything it ATTESTS the loaded
    //  assembly's SHA-256 against the caller-supplied expected hash, so the proof is
    //  pinned to the exact candidate binary built from this implementation/review
    //  head. If the expected hash does not match, it fails closed BEFORE any assert.
    //
    //  PRIVACY: the harness feeds only a synthetic QA-only opaque subject
    //  ("ForTheWort_QA-*"); no real provider subject enters the path and the
    //  admission seam already carries only the internal opaque AccountId. It emits
    //  no raw subject, no HMAC, no PII — only PII-free result codes / internal ids.
    //
    //  WHAT THIS DOES NOT PROVE (honest scope, per the normative rider): it does
    //  NOT prove Steam's transport layer independently rejects a duplicate account
    //  login. Steam enforces that client-side; that is exactly why the server seam
    //  is unreachable by two concurrent Steam GUI clients and why this direct-peer
    //  harness is the only mechanism that can exercise same-account concurrency.
    //
    //  NON-VACUITY: --bypass-guard drives the SAME two-peer scenario but reserves
    //  each peer's lease under a DISTINCT synthetic AccountId (simulating a guard
    //  that failed to collapse two transport handles onto one account). The harness
    //  asserts that in that world the second admission WRONGLY succeeds — proving the
    //  green assertions above are only reachable because the shipped guard actually
    //  fences. The regression test runs both modes and requires bypass to fail the
    //  invariant, so a broken guard cannot pass silently.
    // ════════════════════════════════════════════════════════════════════════
    public static class Program
    {
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;

        public static int Main(string[] args)
        {
            string? expectedSha = null;
            bool bypassGuard = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-e":
                    case "--expect-sha256":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("[split-proof] -e requires a SHA-256 argument"); return 2; }
                        expectedSha = args[++i].Trim().ToLowerInvariant();
                        break;
                    case "--bypass-guard":
                        bypassGuard = true;
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("usage: QaSplitSessionHarness -e <expected-sha256-of-candidate-dll> [--bypass-guard]");
                        return 0;
                    default:
                        Console.Error.WriteLine("[split-proof] unknown arg: " + args[i]);
                        return 2;
                }
            }

            try
            {
                string dllPath = AttestCandidateAssembly(expectedSha);
                Console.WriteLine("[split-proof] mode=" + (bypassGuard ? "BYPASS-GUARD (non-vacuity negative control)" : "SHIPPED-GUARD (invariant proof)"));
                Console.WriteLine("[split-proof] candidate assembly: " + dllPath);

                var result = bypassGuard ? RunBypassControl() : RunShippedProof();
                Console.WriteLine(result.Report());
                return result.Passed ? 0 : 1;
            }
            catch (AttestationException ax)
            {
                Console.Error.WriteLine("[split-proof] ATTESTATION FAILED: " + ax.Message);
                return 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[split-proof] HARNESS ERROR: " + ex);
                return 4;
            }
        }

        // ── exact-binary attestation ────────────────────────────────────────────

        private sealed class AttestationException : Exception
        {
            public AttestationException(string message) : base(message) { }
        }

        /// <summary>Resolve the on-disk location of the LOADED candidate assembly (the one whose types
        /// this harness is compiled against and will actually call), hash it, and require the hash to
        /// match the caller-supplied expected value. Fails closed if the expected hash is absent or
        /// mismatched, so the proof can only run against the exact attested candidate binary.</summary>
        private static string AttestCandidateAssembly(string? expectedSha)
        {
            Assembly asm = typeof(LiveSessionAdmission).Assembly;
            string location = asm.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
                throw new AttestationException("candidate assembly has no on-disk location to attest (is it embedded/single-file?)");

            string actual = Sha256OfFile(location);
            Console.WriteLine("[split-proof] candidate assembly SHA-256: " + actual);

            if (string.IsNullOrEmpty(expectedSha))
                throw new AttestationException(
                    "no expected SHA-256 supplied (-e <sha256>). Refusing to run: an unattested binary cannot be split-proof evidence. " +
                    "Actual hash of the loaded assembly was: " + actual);

            if (!string.Equals(actual, expectedSha, StringComparison.Ordinal))
                throw new AttestationException(
                    "candidate assembly SHA-256 mismatch. expected=" + expectedSha + " actual=" + actual +
                    " — the referenced binary is not the attested candidate.");

            Console.WriteLine("[split-proof] SHA-256 attestation OK (matches expected candidate).");
            return location;
        }

        private static string Sha256OfFile(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(fs);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ── admission stack construction from the compiled binary ───────────────

        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private static LookupKeyRing FixedRing() => new LookupKeyRing(FixedKey("k1", 10));

        // A synthetic QA-only opaque subject. NOT a real Steam id: the value is fabricated for the QA
        // fixture (ForTheWort_QA), never a live provider subject, so no PII enters the admission path.
        private static VerifiedProviderPrincipal QaProvider(string qaSubject, long transportHandle) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), qaSubject, transportHandle);

        private sealed class Stack
        {
            public PilotAccountService Accounts { get; }
            public PilotCharacterAdmissionService Characters { get; }
            public BoundSessionPrincipalIndex BoundSessions { get; }
            public LiveSessionAdmission Live { get; }
            public AccountAdmissionIndex Index { get; }

            public Stack(string journalPath)
            {
                var ring = FixedRing();
                var store = new PilotAccountStore(journalPath);
                Accounts = new PilotAccountService(store, ring, NoticeV, RetentionV);
                Index = new AccountAdmissionIndex();
                Characters = new PilotCharacterAdmissionService(store, ring, Index);
                BoundSessions = new BoundSessionPrincipalIndex();
                Live = new LiveSessionAdmission(Accounts, Characters, BoundSessions);
            }
        }

        private static string PeerKey(long playerId) => ServerCreatorIdentity.CharacterSubject(playerId);

        // ── result accumulator ──────────────────────────────────────────────────

        private sealed class ProofResult
        {
            private readonly List<string> _lines = new List<string>();
            public bool Passed { get; private set; } = true;

            public void Check(bool condition, string label)
            {
                _lines.Add((condition ? "  [PASS] " : "  [FAIL] ") + label);
                if (!condition) Passed = false;
            }

            public void Note(string line) => _lines.Add("  · " + line);

            public string Report()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[split-proof] assertions:");
                foreach (var l in _lines) sb.AppendLine(l);
                sb.Append("[split-proof] RESULT: ").Append(Passed ? "PASS" : "FAIL");
                return sb.ToString();
            }
        }

        // ── the shipped-guard proof (invariant) ─────────────────────────────────

        private static ProofResult RunShippedProof()
        {
            var r = new ProofResult();
            string dir = Path.Combine(Path.GetTempPath(), "aip-iap015-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var s = new Stack(Path.Combine(dir, "account-journal.bin"));

                // ONE QA-only opaque subject; TWO distinct transport handles / sibling profiles that both
                // resolve to the SAME internal AccountId (the production shape: two peers, one account).
                const string qaSubject = "ForTheWort_QA-000000000000001";
                long h1 = 700_001L, h2 = 700_002L;
                long p1 = 91_001L, p2 = 91_002L;

                // First peer: full admission + mint succeeds and holds the sole lease.
                var first = s.Live.Admit(PeerKey(p1), QaProvider(qaSubject, h1),
                    new VerifiedProfileSubject(p1, h1), h1, T0, "conn-1");
                r.Check(first.Admitted, "first peer admission+mint succeeds (" + first.ResultCode + ")");
                r.Check(first.Account.Value.StartsWith("acct-", StringComparison.Ordinal), "first peer resolves an internal AccountId");
                r.Check(first.Character.Value.StartsWith("char-", StringComparison.Ordinal), "first peer mints an internal CharacterId");
                r.Check(s.Index.ActiveLeaseCount == 1, "exactly one admission lease is held after first admit");
                r.Note("internal AccountId (opaque, PII-free): " + first.Account.Value);

                // Prove the two peers resolve to ONE AccountId: begin admission for the same subject on a
                // fresh account-service call would resolve the SAME account. We assert via the lease fence:
                // the second peer of the same account must be refused BEFORE any character mint.
                long charsBefore = CharacterCount(s);

                // Second peer (different sibling profile / transport handle) of the SAME account connects
                // concurrently. The shipped guard must reject it at the admission-lease stage.
                var second = s.Live.Admit(PeerKey(p2), QaProvider(qaSubject, h2),
                    new VerifiedProfileSubject(p2, h2), h2, T0, "conn-2");
                r.Check(!second.Admitted, "second concurrent same-account peer is REJECTED");
                r.Check(second.FailedStage == LiveAdmissionStage.Admission,
                    "second peer rejected at the Admission (lease) stage, before character mint (stage=" + second.FailedStage + ")");
                r.Check(second.ResultCode == CharacterRejectionCode.AccountAlreadyConnected.ToString(),
                    "second peer rejection code is AccountAlreadyConnected (got " + second.ResultCode + ")");

                long charsAfter = CharacterCount(s);
                r.Check(charsAfter == charsBefore, "NO character was minted for the rejected second peer (before=" + charsBefore + " after=" + charsAfter + ")");
                r.Check(!s.BoundSessions.TryResolve(PeerKey(p2), out _), "second peer never publishes a bound principal");

                // First lease still valid and its bound principal still resolvable.
                r.Check(s.Index.HasLease(first.Account), "first peer's lease remains valid after the second is rejected");
                r.Check(s.BoundSessions.TryResolve(PeerKey(p1), out _), "first peer's bound principal is still live");
                r.Check(s.Index.ActiveLeaseCount == 1, "still exactly one lease (the second reserved nothing)");

                // Close the first peer: releases the lease.
                bool closed = s.Live.Close(h1);
                r.Check(closed, "closing the first peer releases its live bound principal");
                r.Check(s.Index.ActiveLeaseCount == 0, "the admission lease is released after close");
                r.Check(!s.BoundSessions.TryResolve(PeerKey(p1), out _), "first peer's bound principal is gone after close");

                // A later admission for the same account (after release) succeeds — the fence is not sticky.
                var later = s.Live.Admit(PeerKey(p2), QaProvider(qaSubject, h2),
                    new VerifiedProfileSubject(p2, h2), h2, T0 + 10, "conn-3");
                r.Check(later.Admitted, "a later admission for the same account succeeds after the lease is released (" + later.ResultCode + ")");
                r.Check(later.Account.Value == first.Account.Value, "the later admission resolves the SAME internal AccountId (one account, one identity)");
                r.Check(s.Index.ActiveLeaseCount == 1, "exactly one lease again after the later admission");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
            return r;
        }

        // ── the bypass-guard negative control (non-vacuity) ─────────────────────

        // Drives the SAME same-account concurrency scenario against the shipped
        // AccountAdmissionIndex.TryReserve, but deliberately BYPASSES the one-session guard by reserving
        // each peer's lease under a DISTINCT synthetic AccountId — the exact failure a broken guard would
        // exhibit (two transport handles NOT collapsed onto one account). It then asserts the SAME
        // one-session invariant the shipped-guard proof asserts (second peer must reject
        // AccountAlreadyConnected; exactly one lease). Because the guard is bypassed, those identical
        // assertions FAIL and the harness returns RESULT: FAIL / exit 1. That is the whole point: flip the
        // guard and the green assertions go red, proving the shipped-guard PASS is non-vacuous.
        private static ProofResult RunBypassControl()
        {
            var r = new ProofResult();
            var index = new AccountAdmissionIndex();

            // Two DISTINCT account ids (the bypass: the guard failed to resolve both peers to one account).
            var acctA = new PilotAccountId("acct-bypass-A");
            var acctB = new PilotAccountId("acct-bypass-B");
            long h1 = 800_001L, h2 = 800_002L;

            var res1 = index.TryReserve(acctA, new SessionId("sess-1"), h1, T0);
            r.Check(res1.Outcome == AdmissionReservationOutcome.Reserved, "first peer reserves a lease");

            var res2 = index.TryReserve(acctB, new SessionId("sess-2"), h2, T0);

            // The SAME invariant assertions the shipped-guard proof makes. With the guard bypassed they
            // FAIL, which is exactly what makes the positive proof non-vacuous.
            r.Check(res2.Outcome == AdmissionReservationOutcome.AlreadyConnected,
                "second concurrent same-account peer is REJECTED as AccountAlreadyConnected (got " + res2.Outcome + ")");
            r.Check(index.ActiveLeaseCount == 1,
                "still exactly one admission lease after the second peer (got " + index.ActiveLeaseCount + ")");
            r.Note("bypass-guard mode reserves under two DISTINCT AccountIds, so the invariant assertions above are EXPECTED to fail here; the shipped-guard mode collapses both peers onto ONE AccountId and passes them.");
            return r;
        }

        private static long CharacterCount(Stack s)
        {
            // The store exposes CharacterCount for test/operator visibility; reach it via the account
            // service's Store property (public), so we read the SHIPPED projection, not a private field.
            return s.Accounts.Store.CharacterCount;
        }
    }
}
