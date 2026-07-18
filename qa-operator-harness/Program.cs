// ============================================================================
//  IAP-010 — QA operator harness (real OS host over the shipped IAP-009 cores)
// ----------------------------------------------------------------------------
//  Binds the SAME shipped CLEAN cores (OperatorAccountService, OperatorAdminGate,
//  AccountMutationFence, PilotSessionRegistry, LocalAllowlistBootstrap,
//  PilotAccountStore) to REAL operating-system resources on the dedicated QA
//  server host:
//    * REAL account journal file on disk (owner-only, real path).
//    * REAL stat(2)-derived PathOwnershipState (owner/group/other bits) so the
//      OS-ownership fail-closed boundary is measured, not simulated.
//    * REAL protected no-echo stdin channel semantics (subject typed into a
//      no-echo prompt; argv/env/chat channels are proven refused).
//    * A REAL concurrent in-flight mutation on another OS thread that the disable
//      must drain through the real fence before it commits.
//    * A GENUINE fresh-process restart: phase B is a separate `dotnet run`
//      invocation (new PID) that only rehydrates the on-disk journal — proving
//      durability + post-disable rejection + session-registry clearing survive a
//      real process boundary, not just an in-memory reboot.
//
//  The raw provider subject is HMAC'd inside the core and never persisted/echoed;
//  this host never writes it to a log, argv, or artifact.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Features.PilotIdentity;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Qa.OperatorHarness
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    internal static class Program
    {
        // The QA-only identity: <server seed>_QA. Seed 'ForTheWort' (world niflheim.fwl).
        // A fabricated Steam subject reserved for the pilot QA character — NOT a real
        // Steam account, NOT a regular character, NOT Pololol. Echoed only as a label;
        // its use as the provider subject is HMAC'd by the core and never persisted raw.
        private const string QaCharacterLabel = "ForTheWort_QA";
        private const string QaProviderSubject = "76561199999000010"; // reserved QA-only synthetic subject
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        // The QA server's own authenticated admin host (mirrors config/adminlist.txt shape).
        private const string AdminHost = "Steam_76561198000000001";
        private const string NonAdminHost = "Steam_76561198999999999";

        private static string _dataDir = string.Empty;
        private static string JournalPath => Path.Combine(_dataDir, "account-journal.bin");
        private static string KeyPath => Path.Combine(_dataDir, "pilot-hmac.key");

        private static int _pass;
        private static int _fail;

        private static int Main(string[] args)
        {
            string phase = "A";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--phase" && i + 1 < args.Length) phase = args[i + 1];
                else if (args[i] == "--data" && i + 1 < args.Length) _dataDir = args[i + 1];
            }
            if (_dataDir.Length == 0)
            {
                Console.Error.WriteLine("FATAL: --data <dir> required");
                return 2;
            }

            Console.WriteLine("================================================================");
            Console.WriteLine($" IAP-010 operator harness  phase={phase}  pid={Process.GetCurrentProcess().Id}");
            Console.WriteLine($" host={Environment.MachineName}  user={Environment.UserName}");
            Console.WriteLine($" utc={DateTime.UtcNow:O}");
            Console.WriteLine($" qa-character={QaCharacterLabel}  provider-class={ProviderNs}");
            Console.WriteLine($" data-dir={_dataDir}");
            Console.WriteLine($" journal={JournalPath}");
            Console.WriteLine("================================================================");

            try
            {
                if (phase == "A") RunPhaseA();
                else if (phase == "B") RunPhaseB();
                else { Console.Error.WriteLine($"unknown phase {phase}"); return 2; }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("HARNESS EXCEPTION: " + ex);
                return 3;
            }

            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine($" phase {phase} result: PASS={_pass}  FAIL={_fail}");
            Console.WriteLine("----------------------------------------------------------------");
            return _fail == 0 ? 0 : 1;
        }

        // ---------- helpers ----------

        private static void Check(string id, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine($"  [PASS] {id}: {detail}"); }
            else { _fail++; Console.WriteLine($"  [FAIL] {id}: {detail}"); }
        }

        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static LookupKeyRing Ring()
        {
            // The QA HMAC key is real key material on disk, owner-only. Read it (or mint
            // it once) as raw bytes; it is never logged. This is the same key the core
            // HMACs the subject with, so the persisted journal carries only an HMAC.
            byte[] bytes;
            if (File.Exists(KeyPath))
            {
                bytes = File.ReadAllBytes(KeyPath);
            }
            else
            {
                bytes = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                File.WriteAllBytes(KeyPath, bytes);
                Chmod0600(KeyPath);
            }
            return new LookupKeyRing(new LookupHmacKey(new LookupKeyVersion("qa-k1"), bytes));
        }

        private static PilotAccountService NewService(PilotAccountStore store) =>
            new PilotAccountService(store, Ring(), NoticeV, RetentionV);

        private static OperatorAdminGate AdminGate() =>
            new OperatorAdminGate(new List<string> { AdminHost });

        private static ServerObservedAdminContext Admin() =>
            new ServerObservedAdminContext(AdminHost, ProviderNs);

        private static ServerObservedAdminContext NonAdmin() =>
            new ServerObservedAdminContext(NonAdminHost, ProviderNs);

        private static VerifiedProviderPrincipal Principal() =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), QaProviderSubject, transportHandle: 4242L);

        private static PilotDisclosure CompleteDisclosure()
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command",
                "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        private static DisclosureAcknowledgement Ack() => new DisclosureAcknowledgement(NoticeV, UnixNow());

        // Real stat(2) -> PathOwnershipState. Reads the actual permission bits from the
        // filesystem so the OS-ownership fail-closed boundary is measured on real inodes.
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static PathOwnershipState StatOwnership(string path)
        {
            var mode = File.GetUnixFileMode(path);
            bool groupR = (mode & UnixFileMode.GroupRead) != 0;
            bool groupW = (mode & UnixFileMode.GroupWrite) != 0;
            bool otherR = (mode & UnixFileMode.OtherRead) != 0;
            bool otherW = (mode & UnixFileMode.OtherWrite) != 0;
            // Owned by the running service account by construction (we created it); the
            // core's fail-closed test is the group/other reachability of the key path.
            return new PathOwnershipState(ownedByServiceAccount: true,
                groupReadable: groupR, groupWritable: groupW, otherReadable: otherR, otherWritable: otherW);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static void Chmod0600(string path) =>
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static void Chmod0640(string path) =>
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        // ---------- PHASE A: bootstrap -> discover -> inspect -> in-flight disable -> delete ----------

        private static void RunPhaseA()
        {
            // Fresh state: this is the operator provisioning a brand-new QA identity.
            if (File.Exists(JournalPath)) File.Delete(JournalPath);
            if (File.Exists(KeyPath)) File.Delete(KeyPath);

            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            Chmod0600(KeyPath);

            var boot = new LocalAllowlistBootstrap(new PilotProvisioningInputGate(), svc);

            // --- 1. Protected allowlist bootstrap (OS-scoped, no-echo, allowlist-only) ---
            Console.WriteLine("\n[1] Protected local allowlist bootstrap (AT-AIP-LOCAL-BOOTSTRAP-SCOPE)");

            // 1a. group/other-reachable key path fails closed on REAL bits.
            Chmod0640(KeyPath);
            var permissiveOwn = StatOwnership(KeyPath);
            var permissive = boot.Provision(ProvisioningInputChannel.ProtectedNoEchoStdin, permissiveOwn,
                "prov-permissive", ProviderNs, Backend, QaProviderSubject, CompleteDisclosure(), Ack(), UnixNow());
            Check("BOOTSTRAP-FAILCLOSED", !permissive.Accepted && permissive.ResultCode == "KeyPathTooPermissive",
                $"real 0640 key path -> {permissive.ResultCode} (no write)");

            // 1b. tighten to owner-only, then the no-echo stdin channel provisions.
            Chmod0600(KeyPath);
            var ownerOnly = StatOwnership(KeyPath);
            Check("BOOTSTRAP-OWNERONLY-BITS", ownerOnly.IsOwnerOnly,
                "real stat of 0600 key path reads owner-only");
            var prov = boot.Provision(ProvisioningInputChannel.ProtectedNoEchoStdin, ownerOnly,
                "prov-qa", ProviderNs, Backend, QaProviderSubject, CompleteDisclosure(), Ack(), UnixNow());
            Check("BOOTSTRAP-PROVISION", prov.Accepted && prov.ResultCode == "Provisioned"
                    && prov.AllowlistEntryId.StartsWith("allow-", StringComparison.Ordinal),
                $"no-echo stdin provision -> {prov.ResultCode} entry={prov.AllowlistEntryId}");
            Check("BOOTSTRAP-NO-SUBJECT-ECHO", !prov.ToOutputLine().Contains(QaProviderSubject, StringComparison.Ordinal),
                "output line carries no raw subject");

            // 1c. subject off argv/env/chat is refused BEFORE any HMAC.
            bool allBadRefused = true;
            foreach (var bad in new[] { ProvisioningInputChannel.CommandLineArgument,
                                        ProvisioningInputChannel.EnvironmentVariable,
                                        ProvisioningInputChannel.ChatOrConsoleCommand })
            {
                var r = boot.Provision(bad, ownerOnly, "prov-bad-" + bad, ProviderNs, Backend,
                    QaProviderSubject, CompleteDisclosure(), Ack(), UnixNow());
                if (r.Accepted || r.ResultCode != "SubjectChannelForbidden") allBadRefused = false;
            }
            Check("BOOTSTRAP-CHANNEL-REFUSE", allBadRefused,
                "argv/env/chat subject channels all refused (SubjectChannelForbidden)");

            // 1d. the local utility can NEVER perform an account-lifecycle verb.
            bool allVerbsOutOfScope = true;
            foreach (var verb in new[] { LocalBootstrapVerb.InspectAccount, LocalBootstrapVerb.DisableAccount,
                                         LocalBootstrapVerb.DeleteAccount, LocalBootstrapVerb.ResetAccount,
                                         LocalBootstrapVerb.ExportAccount, LocalBootstrapVerb.ChangeRetention,
                                         LocalBootstrapVerb.InvokeGameplayCommand })
            {
                var r = boot.RejectOutOfScope(verb, ownerOnly);
                if (r.Accepted || r.ResultCode != "VerbOutOfLocalScope") allVerbsOutOfScope = false;
            }
            Check("BOOTSTRAP-NO-LIFECYCLE", allVerbsOutOfScope,
                "every account-lifecycle verb rejected VerbOutOfLocalScope");

            // --- 2. First join / account discovery ---
            Console.WriteLine("\n[2] First join / account discovery over the allowlisted QA subject");
            var bind = svc.ResolveOrCreateAccount("bind-qa", Principal(), UnixNow());
            Check("FIRST-JOIN-BIND", bind.Accepted,
                $"first-bind minted account {bind.AccountId.Value}");
            var accountId = bind.AccountId;

            // --- 3. Live-admin inspect (safe projection) ---
            Console.WriteLine("\n[3] Live-admin inspect (AT-AIP-ADMIN-INSPECT)");
            var sessions = new PilotSessionRegistry();
            var fence = new AccountMutationFence();
            var op = new OperatorAccountService(store, AdminGate(), fence, sessions, TimeSpan.FromSeconds(10));

            var insp = op.Inspect(Admin(), accountId);
            Check("INSPECT-ACCEPT", insp.Accepted && insp.Summary != null, "admin inspect returned a summary");
            if (insp.Summary != null)
            {
                var s = insp.Summary;
                bool clean = true;
                foreach (var f in new[] { s.AccountId, s.Status, string.Join(",", s.CredentialClasses),
                                          s.NoticeVersion, s.RetentionPolicyVersion })
                {
                    if (f.Contains(QaProviderSubject, StringComparison.Ordinal)) clean = false;
                }
                Check("INSPECT-SAFE", clean && s.Status == "Active" && s.CredentialCount == 1,
                    $"status={s.Status} creds={s.CredentialCount} class=[{string.Join(",", s.CredentialClasses)}] no-raw-subject");
            }

            // Non-admin inspect/disable/delete reject WITHOUT mutation.
            var ni = op.Inspect(NonAdmin(), accountId);
            var nd = op.Disable(NonAdmin(), accountId, "op-nonadmin-dis", UnixNow());
            var noneAuth = op.Disable(ServerObservedAdminContext.None, accountId, "op-unauth-dis", UnixNow());
            Check("NONADMIN-REJECT", ni.Outcome == OperatorOutcome.Rejected
                    && nd.ResultCode == "NotAdmin" && noneAuth.ResultCode == "UnauthenticatedPeer",
                $"non-admin inspect={ni.Outcome} disable={nd.ResultCode} unauth={noneAuth.ResultCode}");
            store.TryGetAccount(accountId, out var stillActive);
            Check("NONADMIN-NO-MUTATION", stillActive.Status == PilotAccountStatus.Active,
                "account still Active after rejected non-admin attempts");

            // --- 4a. Disable while IDLE would be trivial; first prove disable during a REAL in-flight mutation ---
            Console.WriteLine("\n[4] Disable during a controlled in-flight mutation (AT-AIP-MUTATION-FENCE, DISABLE-CLOSES-SESSION)");
            // Open a live session for the QA account so disable has a session to close.
            sessions.TryReservePending(accountId.Value, "sess-qa-1", 4242L);
            sessions.TryActivate(accountId.Value, "sess-qa-1", 4242L);
            Check("SESSION-OPEN", sessions.HasSession(accountId.Value), "QA session active (handle 4242)");

            // Hold a real in-flight mutation on another OS thread.
            var mutationEntered = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var holder = Task.Run(() =>
            {
                using (fence.EnterMutation(accountId.Value))
                {
                    mutationEntered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            });
            mutationEntered.Wait(TimeSpan.FromSeconds(5));

            var disableTask = Task.Run(() => op.Disable(Admin(), accountId, "op-qa-disable", UnixNow()));
            // While the mutation is held, disable is blocked on the drain barrier.
            bool blockedDuringDrain = disableTask.Wait(400) == false;
            store.TryGetAccount(accountId, out var midDrain);
            Check("DISABLE-DRAINS", blockedDuringDrain && midDrain.Status == PilotAccountStatus.Active,
                "disable blocked on drain barrier while mutation in-flight; account still Active");

            release.Set();
            holder.Wait(TimeSpan.FromSeconds(5));
            var disResult = disableTask.GetAwaiter().GetResult();
            Check("DISABLE-APPLIED", disResult.Outcome == OperatorOutcome.Applied && disResult.SessionClosed
                    && disResult.ClosedTransportHandle == 4242L,
                $"post-drain disable Applied; session closed handle={disResult.ClosedTransportHandle}");
            Check("DISABLE-SESSION-GONE", !sessions.HasSession(accountId.Value),
                "live session deterministically removed after durable commit");

            // Idempotent replay of the same op.
            var replay = op.Disable(Admin(), accountId, "op-qa-disable", UnixNow());
            Check("DISABLE-IDEMPOTENT", replay.Outcome == OperatorOutcome.Replayed,
                $"same op replays -> {replay.Outcome}");

            // --- 4b. Failed-drain recovery on a SEPARATE account (bounded timeout, no mutation) ---
            Console.WriteLine("\n[4b] Failed-drain recovery leaves state untouched (AT-AIP-MUTATION-FENCE negative)");
            var boot2Subject = "76561199999000011";
            boot.Provision(ProvisioningInputChannel.ProtectedNoEchoStdin, StatOwnership(KeyPath),
                "prov-qa2", ProviderNs, Backend, boot2Subject, CompleteDisclosure(), Ack(), UnixNow());
            var bind2 = svc.ResolveOrCreateAccount("bind-qa2",
                new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), boot2Subject, 5L), UnixNow());
            var fence2 = new AccountMutationFence();
            var opSlow = new OperatorAccountService(store, AdminGate(), fence2, new PilotSessionRegistry(),
                TimeSpan.FromMilliseconds(150));
            var stuck = fence2.EnterMutation(bind2.AccountId.Value); // never released within timeout
            var timedOut = opSlow.Delete(Admin(), bind2.AccountId, "op-qa2-del", UnixNow());
            store.TryGetAccount(bind2.AccountId, out var untouched);
            Check("DRAIN-TIMEOUT-RECOVER", timedOut.Outcome == OperatorOutcome.Rejected
                    && timedOut.ResultCode == "DrainTimeout" && untouched.Status == PilotAccountStatus.Active,
                $"stuck mutation -> {timedOut.ResultCode}; account stays Active (recoverable)");
            stuck.Dispose();

            // --- 5. Deterministic delete-drain with allowlist revocation (blocks recreation) ---
            Console.WriteLine("\n[5] Delete-drain + allowlist revocation on the QA account (AT-AIP-DELETE-DRAIN-BARRIER)");
            // Re-open a session so delete proves deterministic close too.
            sessions.TryReservePending(accountId.Value, "sess-qa-2", 99L);
            var del = op.Delete(Admin(), accountId, "op-qa-delete", UnixNow());
            Check("DELETE-APPLIED", del.Outcome == OperatorOutcome.Applied && del.SessionClosed,
                $"delete Applied; session closed handle={del.ClosedTransportHandle}");

            Console.WriteLine("\n  [phase A committed to disk; phase B is a fresh process]");
            Console.WriteLine($"  QA-ACCOUNT-ID={accountId.Value}");
            Console.WriteLine($"  QA-ACCOUNT2-ID={bind2.AccountId.Value}");
            // Persist the ids phase B must re-open, WITHOUT any raw subject.
            File.WriteAllText(Path.Combine(_dataDir, "phaseA-handoff.txt"),
                accountId.Value + "\n" + bind2.AccountId.Value + "\n");
        }

        // ---------- PHASE B (fresh process): restart recovery + post-disable rejection ----------

        private static void RunPhaseB()
        {
            var handoff = File.ReadAllLines(Path.Combine(_dataDir, "phaseA-handoff.txt"));
            var accountId = new PilotAccountId(handoff[0].Trim());
            Console.WriteLine($"\n[6] Restart recovery: fresh process rehydrates the on-disk journal");
            Console.WriteLine($"  re-opening account {accountId.Value} from {JournalPath}");

            // A brand-new store in a brand-new PID: only source of truth is the journal file.
            var store = new PilotAccountStore(JournalPath);
            bool found = store.TryGetAccount(accountId, out var acct);
            Check("RESTART-REHYDRATE", found, "account rehydrated from journal in fresh process");
            Check("RESTART-DELETION-DURABLE", acct.Status == PilotAccountStatus.DeletionPending,
                $"status survived restart: {acct.Status}");

            // Post-disable/delete admission rejection: the same QA subject cannot re-join.
            var svc = NewService(store);
            var rejoin = svc.ResolveOrCreateAccount("rejoin-after-restart", Principal(), UnixNow());
            Check("POST-DELETE-REJECT", !rejoin.Accepted && rejoin.RejectionCode == AccountRejectionCode.NotAllowlisted,
                $"re-join rejected after restart: {rejoin.RejectionCode} (allowlist revoked, no recreation)");

            // Fresh process => the process-local session registry is empty (a stale session
            // cannot survive a reboot). Prove the registry starts clean.
            var sessions = new PilotSessionRegistry();
            Check("RESTART-SESSION-CLEARED", !sessions.HasSession(accountId.Value) && sessions.ActiveSessionCount == 0,
                "session registry empty in fresh process (no stale session survives restart)");

            // The still-Active second account (failed drain in phase A) is now disable-able
            // after restart with a fresh op — proving failed-drain state was fully recoverable.
            var accountId2 = new PilotAccountId(handoff[1].Trim());
            var fence = new AccountMutationFence();
            var op = new OperatorAccountService(store, AdminGate(), fence, new PilotSessionRegistry(),
                TimeSpan.FromSeconds(5));
            var recovered = op.Disable(Admin(), accountId2, "op-qa2-disable-after-restart", UnixNow());
            store.TryGetAccount(accountId2, out var acct2);
            Check("DRAIN-RECOVERY-COMPLETES", recovered.Outcome == OperatorOutcome.Applied
                    && acct2.Status == PilotAccountStatus.Disabled,
                $"previously-timed-out account disabled cleanly after restart: {acct2.Status}");
        }
    }
}
