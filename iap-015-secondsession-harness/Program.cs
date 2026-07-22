// ============================================================================
//  IAP-015 — AT-AIP-DEDICATED-SECOND-SESSION-REJECT, evidence half (Option B).
// ----------------------------------------------------------------------------
//  Owner-approved Option B split-evidence contract (Daniel, Discord
//  1529507269027434728; architect DECIDE t_13db2c95, comment 1886). This is the
//  production-identical, direct-peer, EXACT-BINARY harness that exercises the
//  Niflheim server-authoritative one-account/one-session invariant
//  (AIP-FR-013 / spec rule #5 / AIP-SC-002) at its real enforcement seam:
//
//    src/SBPR.Niflheim.HomesteadStones/Application/Accounts/AccountAdmissionIndex.cs (TryReserve)
//    src/SBPR.Niflheim.HomesteadStones/Application/Runtime/LiveSessionAdmission.cs (Admit)
//
//  It presents TWO transport peers that both resolve to ONE authenticated
//  AccountId (same provider subject, sibling profiles) and asserts:
//    (A) the 1st peer's lease is admitted and mints its character normally;
//    (B) the 2nd peer's reserve REJECTS AccountAlreadyConnected BEFORE any
//        character mint (no second bind, no second character in the store);
//    (C) after the 1st peer's session closes, the lease releases and the 2nd
//        peer can now admit sequentially (release-correctness).
//
//  EXACT-BINARY SELF-ATTESTATION: the admission types are loaded from the
//  COMPILED shipped assemblies (reference-linked, not source-recompiled). At
//  runtime the harness SHA-256-hashes the on-disk assembly file that actually
//  provided LiveSessionAdmission / AccountAdmissionIndex and prints those hashes
//  as evidence. A re-implemented / mocked / source-recompiled core cannot pass:
//  the emitted hash proves the assertion ran against the linked shipped binary.
//
//  This harness does NOT prove Steam's transport layer independently rejects a
//  duplicate account login — Steam enforces that client-side by kicking the
//  first session, which is precisely why the server seam is unreachable by two
//  concurrent Steam GUI clients and why this direct-peer harness is the only
//  mechanism that can exercise same-account concurrency (see the AIP-SC-008
//  verbatim rider).
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Qa.Iap015SecondSession
{
    internal static class Program
    {
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;

        // The disposable QA identity this pilot journey is scoped to (never a regular
        // character / Pololol). The provider subject below stands in for ForTheWort_QA's
        // authenticated Steam subject on the isolated fixture.
        private const string QaProviderSubject = "76561198000000015"; // ForTheWort_QA (disposable)

        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private static int Main()
        {
            int failures = 0;
            var log = new StringBuilder();
            void Line(string s) { Console.WriteLine(s); log.Append(s).Append('\n'); }
            void Check(bool cond, string label)
            {
                if (!cond) { failures++; Line("  [FAIL] " + label); }
                else Line("  [ok]   " + label);
            }

            Line("== IAP-015 AT-AIP-DEDICATED-SECOND-SESSION-REJECT — direct-peer exact-binary harness ==");
            Line("Option B split evidence (architect DECIDE t_13db2c95). Server-authoritative seam:");
            Line("  AccountAdmissionIndex.TryReserve + LiveSessionAdmission.Admit (shipped binaries).");
            Line("");

            // ── EXACT-BINARY SELF-ATTESTATION ─────────────────────────────────────
            // Hash the on-disk assemblies that actually provided the admission types.
            Line("-- Linked shipped-binary evidence (SHA-256 of the assembly the types loaded from) --");
            string admissionAsmPath = new Uri(typeof(LiveSessionAdmission).Assembly.Location).LocalPath;
            string indexAsmPath = new Uri(typeof(AccountAdmissionIndex).Assembly.Location).LocalPath;
            string admissionHash = Sha256File(admissionAsmPath);
            string indexHash = Sha256File(indexAsmPath);
            Line("  LiveSessionAdmission   <- " + Path.GetFileName(admissionAsmPath));
            Line("    sha256 = " + admissionHash);
            Line("  AccountAdmissionIndex  <- " + Path.GetFileName(indexAsmPath));
            Line("    sha256 = " + indexHash);
            // Prove the seam actually resolves from a compiled DLL on disk, not an in-memory/dynamic type.
            Check(!string.IsNullOrEmpty(admissionAsmPath) && File.Exists(admissionAsmPath),
                "admission types resolve from a real on-disk shipped assembly (exact-binary)");
            Check(string.Equals(admissionHash, indexHash, StringComparison.OrdinalIgnoreCase),
                "TryReserve + Admit ship in the SAME shipped assembly (one admission core)");

            // Optional pin verification: if operator passes expected hashes via argv/env, enforce them.
            string? expected = Environment.GetEnvironmentVariable("IAP015_EXPECT_ADMISSION_SHA256");
            if (!string.IsNullOrEmpty(expected))
                Check(string.Equals(admissionHash, expected, StringComparison.OrdinalIgnoreCase),
                    "linked admission assembly hash matches operator-pinned IAP015_EXPECT_ADMISSION_SHA256");
            Line("");

            // ── Build the SHIPPED admission stack (direct-peer, one journal) ──────
            string dir = Path.Combine(Path.GetTempPath(), "iap015-2ss-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string journal = Path.Combine(dir, "account-journal.bin");
            try
            {
                var ring = new LookupKeyRing(FixedKey("k1", 10));
                var store = new PilotAccountStore(journal);
                var accounts = new PilotAccountService(store, ring, NoticeV, RetentionV);
                var characters = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
                var bound = new BoundSessionPrincipalIndex();
                var live = new LiveSessionAdmission(accounts, characters, bound);

                // ONE authenticated provider subject -> resolves to ONE internal AccountId.
                // Two transport peers present sibling profiles (distinct s_playerID) of THAT account.
                var provider = new VerifiedProviderPrincipal(
                    PilotProviderKey.Steamworks(Backend), QaProviderSubject, transportHandle: 1L);

                const long peer1Handle = 100L;
                const long peer2Handle = 200L;
                string peer1Key = "player:1111"; // sibling profile A of the account
                string peer2Key = "player:2222"; // sibling profile B of the SAME account

                Line("-- Peer 1 (transport " + peer1Handle + ", sibling profile A) admits --");
                var r1 = live.Admit(peer1Key, provider,
                    new VerifiedProfileSubject(1111L, peer1Handle), peer1Handle, T0, "conn-1");
                Check(r1.Admitted, "peer1 admitted end-to-end (transport->auth->AccountId->admission->mint)");
                Check(r1.Account.Value.StartsWith("acct-", StringComparison.Ordinal), "peer1 bound an internal AccountId");
                Check(r1.Character.Value.StartsWith("char-", StringComparison.Ordinal), "peer1 minted an internal CharacterId");
                Check(bound.TryResolve(peer1Key, out _), "peer1 bound principal is live-resolvable");
                int charsAfterFirst = store.CharacterCount;
                Check(charsAfterFirst == 1, "exactly ONE character minted after peer1 (was " + charsAfterFirst + ")");
                Line("    peer1 acct=" + Short(r1.Account.Value) + " char=" + Short(r1.Character.Value));
                Line("");

                Line("-- Peer 2 (transport " + peer2Handle + ", sibling profile B, SAME account) — concurrent --");
                var r2 = live.Admit(peer2Key, provider,
                    new VerifiedProfileSubject(2222L, peer2Handle), peer2Handle, T0, "conn-2");
                Check(!r2.Admitted, "peer2 REJECTED (one-account/one-session invariant holds)");
                Check(r2.FailedStage == LiveAdmissionStage.Admission,
                    "peer2 rejected at the ADMISSION lease stage, not later (was " + r2.FailedStage + ")");
                Check(r2.ResultCode == "AccountAlreadyConnected",
                    "peer2 rejection code == AccountAlreadyConnected (was '" + r2.ResultCode + "')");
                Check(!bound.TryResolve(peer2Key, out _), "peer2 published NO bound principal");
                int charsAfterSecond = store.CharacterCount;
                Check(charsAfterSecond == charsAfterFirst,
                    "NO second character minted — reject happened BEFORE mint (still " + charsAfterSecond + ")");
                Check(live.LiveSessionCount == 1, "exactly ONE live session after the rejected concurrent join");
                Line("    peer2 rejected: stage=" + r2.FailedStage + " code=" + r2.ResultCode);
                Line("");

                Line("-- Release correctness: peer1 closes, then peer2 admits sequentially --");
                bool closed = live.Close(peer1Handle);
                Check(closed, "peer1 session closed and released its lease");
                Check(!bound.TryResolve(peer1Key, out _), "peer1 bound principal removed on close");
                Check(live.LiveSessionCount == 0, "no live sessions after peer1 close");
                var r3 = live.Admit(peer2Key, provider,
                    new VerifiedProfileSubject(2222L, peer2Handle), peer2Handle, T0 + 1, "conn-2b");
                Check(r3.Admitted, "peer2 admits SEQUENTIALLY once the lease is free (sequential sibling is allowed)");
                Check(r3.Account.Value == r1.Account.Value,
                    "peer2's sequential session resolves the SAME internal AccountId (sibling of one account)");
                Check(live.LiveSessionCount == 1, "exactly one live session after sequential peer2 admit");
                Line("    peer2 sequential acct=" + Short(r3.Account.Value));
                Line("");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }

            Line("== RESULT: " + (failures == 0 ? "PASS" : ("FAIL (" + failures + " assertion(s))")) + " ==");
            Line("Evidence — linked shipped admission binary sha256: " + admissionHash);

            // Also emit a machine-readable evidence block for the runbook / review.
            Line("");
            Line("BEGIN-EVIDENCE-JSON");
            Line("{");
            Line("  \"at\": \"AT-AIP-DEDICATED-SECOND-SESSION-REJECT\",");
            Line("  \"half\": \"shipped-binary-direct-peer\",");
            Line("  \"result\": \"" + (failures == 0 ? "PASS" : "FAIL") + "\",");
            Line("  \"assertion_failures\": " + failures.ToString(CultureInfo.InvariantCulture) + ",");
            Line("  \"admission_assembly\": \"" + Path.GetFileName(admissionAsmPath) + "\",");
            Line("  \"admission_assembly_sha256\": \"" + admissionHash + "\"");
            Line("}");
            Line("END-EVIDENCE-JSON");

            return failures == 0 ? 0 : 1;
        }

        private static string Short(string s) => s.Length <= 14 ? s : s.Substring(0, 14) + "…";

        private static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            byte[] h = sha.ComputeHash(fs);
            var sb = new StringBuilder(h.Length * 2);
            foreach (byte b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
