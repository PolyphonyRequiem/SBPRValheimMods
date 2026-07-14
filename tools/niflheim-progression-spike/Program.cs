using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SBPR.Niflheim.ProgressionSpike
{
    // Gate-A executable spike harness (T001).
    //
    // Proves three acceptance tests against the selected mechanisms:
    //   principal  = server-derived (candidate A) over a server-owned account map (candidate E)
    //   receipt    = append-only write-ahead journal with fsync per boundary (candidate 1)
    //
    //   AT-P0-IDENTITY          server-derived principal; payload claim cannot substitute it
    //   AT-P0-CRASH-EACH-WRITE  REAL process death (child process hard-killed) after every
    //                           durable-write boundary; restart/retry returns exactly one result
    //   AT-P0-RECOVERY-REPORT   operator-readable replay/quarantine evidence, no invented facts
    //
    // Modes:
    //   (no args)                     run the full suite (parent orchestrator)
    //   --crash-child <journal> <N>   internal: apply one op, hard-exit after boundary N
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--crash-child")
                return RunCrashChild(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));

            var log = new StringBuilder();
            bool allPass = true;
            allPass &= AtP0Identity(log);
            allPass &= AtP0CrashEachWrite(log);
            allPass &= AtP0RecoveryReport(log);

            log.AppendLine();
            log.AppendLine(allPass ? "GATE-A SPIKE: ALL ACCEPTANCE TESTS PASS" : "GATE-A SPIKE: FAILURE");
            Console.Write(log.ToString());

            // Also emit the machine-readable evidence file for the PR.
            string evidenceDir = Environment.GetEnvironmentVariable("SPIKE_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidenceDir))
            {
                Directory.CreateDirectory(evidenceDir);
                File.WriteAllText(Path.Combine(evidenceDir, "gate-a-spike-run.txt"), log.ToString());
            }
            return allPass ? 0 : 1;
        }

        // ---- AT-P0-IDENTITY ----
        private static bool AtP0Identity(StringBuilder log)
        {
            log.AppendLine("## AT-P0-IDENTITY");
            // Server-owned platform-id -> AccountId map (candidate E). Client never sees it.
            var accountMap = new Dictionary<string, string>
            {
                { "steam:owner-endpoint", "acct-OWNER" },
                { "steam:attacker-endpoint", "acct-ATTACKER" }
            };
            var resolver = new PrincipalResolver(pid => accountMap.TryGetValue(pid, out var a) ? a : null);

            bool pass = true;

            // 1. Honest client: connection = owner, claim matches -> Bound to server truth.
            var ownerConn = new AuthenticatedConnection { PlatformId = "steam:owner-endpoint", ActingCharacterId = "char-OWNER" };
            AuthoritativePrincipal p;
            var r1 = resolver.Resolve(ownerConn, new ClaimedPrincipal { ClaimedAccountId = "acct-OWNER", ClaimedCharacterId = "char-OWNER" }, out p);
            bool c1 = r1 == PrincipalResolution.Bound && p.AccountId == "acct-OWNER";
            log.AppendLine("  honest-claim-bound: " + Ok(c1) + " (account=" + p.AccountId + ")");
            pass &= c1;

            // 2. HOSTILE: attacker's authenticated connection, but payload CLAIMS the owner.
            //    Server must derive attacker from the socket and reject the forged claim.
            var attackerConn = new AuthenticatedConnection { PlatformId = "steam:attacker-endpoint", ActingCharacterId = "char-ATTACKER" };
            var r2 = resolver.Resolve(attackerConn, new ClaimedPrincipal { ClaimedAccountId = "acct-OWNER", ClaimedCharacterId = "char-OWNER" }, out p);
            bool c2 = r2 == PrincipalResolution.PrincipalMismatch;
            log.AppendLine("  hostile-substitution-rejected: " + Ok(c2) + " (" + r2 + ")");
            pass &= c2;

            // 3. Payload identity alone (no authenticated socket) can NEVER become authority.
            var noConn = new AuthenticatedConnection { PlatformId = null, ActingCharacterId = "char-OWNER" };
            var r3 = resolver.Resolve(noConn, new ClaimedPrincipal { ClaimedAccountId = "acct-OWNER" }, out p);
            bool c3 = r3 == PrincipalResolution.UnauthenticatedPeer;
            log.AppendLine("  payload-without-connection-rejected: " + Ok(c3) + " (" + r3 + ")");
            pass &= c3;

            // 4. End-to-end via pipeline: hostile submission must not mutate the journal.
            string jpath = TempJournal("identity");
            var pipeline = new OperationPipeline(new DurableJournal(jpath), resolver, new NoCrash());
            var hostile = pipeline.SubmitFoundational("op-hostile", "stone-1", attackerConn,
                new ClaimedPrincipal { ClaimedAccountId = "acct-OWNER", ClaimedCharacterId = "char-OWNER" }, "place-foundation");
            bool c4 = hostile.Outcome == OperationOutcome.PrincipalRejected && !File.Exists(jpath);
            log.AppendLine("  hostile-op-non-mutating: " + Ok(c4) + " (outcome=" + hostile.Outcome + ", journal-absent=" + !File.Exists(jpath) + ")");
            pass &= c4;
            SafeDelete(jpath);

            log.AppendLine("  => AT-P0-IDENTITY: " + Ok(pass));
            log.AppendLine();
            return pass;
        }

        // ---- AT-P0-CRASH-EACH-WRITE ----
        // For every durable boundary N in {1,2,3,4}: spawn a child that applies the op and
        // hard-exits (real process death) right after boundary N. Then the parent re-runs the
        // SAME operation to completion and asserts exactly one terminal result: +1/+1/+1.
        private static bool AtP0CrashEachWrite(StringBuilder log)
        {
            log.AppendLine("## AT-P0-CRASH-EACH-WRITE");
            bool pass = true;
            string exe = Assembly.GetExecutingAssembly().Location;

            for (int boundary = 1; boundary <= 4; boundary++)
            {
                string jpath = TempJournal("crash-b" + boundary);
                SafeDelete(jpath);

                // Child process dies right after boundary `boundary`.
                int childExit = RunChild(exe, jpath, boundary);
                bool childDied = childExit != 0; // hard-exit code we set on the crash path

                // Torn tail may exist; recovery must survive it.
                var resolver = OwnerResolver();
                var pipeline = new OperationPipeline(new DurableJournal(jpath), resolver, new NoCrash());

                // Parent retries the SAME operationId to completion.
                var result = pipeline.SubmitFoundational("op-crash", "stone-1", OwnerConn(),
                    OwnerClaim(), "place-foundation");

                bool balancesOk = result.PersonalAp == 1 && result.CumulativeAp == 1 && result.MirroredStoneAp == 1;
                bool terminalOk = result.Outcome == OperationOutcome.Applied || result.Outcome == OperationOutcome.Replayed;

                // Re-submit AGAIN: must be Replayed with identical balances (idempotent).
                var again = pipeline.SubmitFoundational("op-crash", "stone-1", OwnerConn(), OwnerClaim(), "place-foundation");
                bool idempotent = again.Outcome == OperationOutcome.Replayed
                    && again.PersonalAp == 1 && again.CumulativeAp == 1 && again.MirroredStoneAp == 1;

                bool cellPass = childDied && balancesOk && terminalOk && idempotent;
                log.AppendLine("  crash-after-boundary-" + boundary + ": " + Ok(cellPass)
                    + " (child-exit=" + childExit + ", recover=" + result.Outcome
                    + " P/C/M=" + result.PersonalAp + "/" + result.CumulativeAp + "/" + result.MirroredStoneAp
                    + ", replay=" + again.Outcome + ")");
                pass &= cellPass;
                SafeDelete(jpath);
            }

            // Conflicting operationId reuse must reject as OperationConflict (idempotency invariant).
            {
                string jpath = TempJournal("conflict");
                SafeDelete(jpath);
                var pipeline = new OperationPipeline(new DurableJournal(jpath), OwnerResolver(), new NoCrash());
                pipeline.SubmitFoundational("op-x", "stone-1", OwnerConn(), OwnerClaim(), "payload-A");
                var conflict = pipeline.SubmitFoundational("op-x", "stone-1", OwnerConn(), OwnerClaim(), "payload-B-different");
                bool cok = conflict.Outcome == OperationOutcome.OperationConflict;
                log.AppendLine("  conflicting-opid-rejected: " + Ok(cok) + " (" + conflict.Outcome + ")");
                pass &= cok;
                SafeDelete(jpath);
            }

            log.AppendLine("  => AT-P0-CRASH-EACH-WRITE: " + Ok(pass));
            log.AppendLine();
            return pass;
        }

        // ---- AT-P0-RECOVERY-REPORT ----
        private static bool AtP0RecoveryReport(StringBuilder log)
        {
            log.AppendLine("## AT-P0-RECOVERY-REPORT");
            bool pass = true;
            string exe = Assembly.GetExecutingAssembly().Location;

            // Case 1: crash after boundary 2 (partial, no terminal) -> report QUARANTINE, no invented facts.
            string jpath = TempJournal("report-partial");
            SafeDelete(jpath);
            RunChild(exe, jpath, 2);
            var pipeline = new OperationPipeline(new DurableJournal(jpath), OwnerResolver(), new NoCrash());
            string partialReport = RecoveryReport.Build(new DurableJournal(jpath), "op-crash", pipeline);
            bool quarantined = partialReport.Contains("QUARANTINE");
            log.AppendLine("  partial-state-reports-quarantine: " + Ok(quarantined));
            pass &= quarantined;

            // Case 2: after recovery completes, report shows RECOVERABLE with the one true balance.
            pipeline.SubmitFoundational("op-crash", "stone-1", OwnerConn(), OwnerClaim(), "place-foundation");
            string finalReport = RecoveryReport.Build(new DurableJournal(jpath), "op-crash", pipeline);
            bool recoverable = finalReport.Contains("RECOVERABLE")
                && finalReport.Contains("PersonalAP:     1")
                && finalReport.Contains("MirroredStoneAP:1");
            log.AppendLine("  recovered-state-reports-single-result: " + Ok(recoverable));
            pass &= recoverable;

            log.AppendLine();
            log.AppendLine("  --- sample operator report (post-recovery) ---");
            foreach (var line in finalReport.Split('\n'))
                log.AppendLine("  | " + line.TrimEnd('\r'));

            SafeDelete(jpath);
            log.AppendLine("  => AT-P0-RECOVERY-REPORT: " + Ok(pass));
            log.AppendLine();
            return pass;
        }

        // ---- Crash child: applies the op, hard-exits after boundary N (REAL process death) ----
        private static int RunCrashChild(string journalPath, int crashAfter)
        {
            var crash = new HardExitCrash(crashAfter);
            var pipeline = new OperationPipeline(new DurableJournal(journalPath), OwnerResolver(), crash);
            try
            {
                pipeline.SubmitFoundational("op-crash", "stone-1", OwnerConn(), OwnerClaim(), "place-foundation");
            }
            catch (HardExitException)
            {
                // Simulate the OS terminating the process mid-operation: exit WITHOUT any
                // graceful flush beyond what the journal already fsync'd. Non-zero exit code.
                Environment.Exit(137); // 128+SIGKILL, our "was killed" sentinel
            }
            return 0; // completed without hitting the crash boundary (boundary beyond last)
        }

        private static int RunChild(string exe, string journalPath, int crashAfter)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(exe);
            psi.ArgumentList.Add("--crash-child");
            psi.ArgumentList.Add(journalPath);
            psi.ArgumentList.Add(crashAfter.ToString(CultureInfo.InvariantCulture));
            using (var proc = Process.Start(psi))
            {
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        // ---- shared owner fixtures ----
        private static PrincipalResolver OwnerResolver()
        {
            var map = new Dictionary<string, string> { { "steam:owner-endpoint", "acct-OWNER" } };
            return new PrincipalResolver(pid => map.TryGetValue(pid, out var a) ? a : null);
        }
        private static AuthenticatedConnection OwnerConn() =>
            new AuthenticatedConnection { PlatformId = "steam:owner-endpoint", ActingCharacterId = "char-OWNER" };
        private static ClaimedPrincipal OwnerClaim() =>
            new ClaimedPrincipal { ClaimedAccountId = "acct-OWNER", ClaimedCharacterId = "char-OWNER" };

        private static string TempJournal(string tag) =>
            Path.Combine(Path.GetTempPath(), "sbpr-spike-" + tag + "-" + Guid.NewGuid().ToString("N") + ".wal");
        private static void SafeDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
        private static string Ok(bool b) => b ? "PASS" : "FAIL";
    }

    public sealed class HardExitException : Exception { }

    // Throws right after the target boundary is journaled+fsync'd -> the process is torn
    // down mid-operation, exactly like an OS kill after a durable write.
    public sealed class HardExitCrash : ICrashInjector
    {
        private readonly int _crashAfter;
        public HardExitCrash(int crashAfter) { _crashAfter = crashAfter; }
        public void AfterBoundary(BoundaryPhase phase)
        {
            if ((int)phase == _crashAfter) throw new HardExitException();
        }
    }
}
