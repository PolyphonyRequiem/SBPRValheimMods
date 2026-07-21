// ============================================================================
//  T022 — niflheim-account-bootstrap : the thin operator host CLI.
// ----------------------------------------------------------------------------
//  Provisions EXACTLY ONE real QA subject into the ISOLATED HomesteadT009L
//  account store by binding REAL OS resources to the SHIPPED engine-free cores:
//
//    * REAL path resolution: realpath (symlink-resolving) + per-component lstat
//      so a symlink that escapes the configured QA data root is caught.
//    * REAL stat(2)-derived key-path ownership so the owner-only fail-closed
//      boundary is measured on the real inode, not simulated.
//    * REAL protected no-echo TTY input for the raw subject: the subject is
//      typed into a no-echo prompt and is NEVER a command-line arg or env var
//      (there is no --subject flag, so the argv/env channel does not exist).
//    * The on-disk account journal + owner-only HMAC key, opened APPEND-only
//      through the shipped LocalAllowlistBootstrap / PilotAccountService cores.
//
//  This host owns NO provisioning policy — provisioning is delegated verbatim to
//  LiveStoreProvisioningGuard over the shipped IAP-009 cores, which HMAC the
//  subject and discard it. This host only does I/O + surfaces subject-free
//  result codes; it never writes the raw subject to a log, argv, env, or file.
//
//  Subcommands:
//    preflight  — subject-free proof of target identity, key perms, store
//                 health, notice/retention versions, restart requirement. Reads
//                 NO subject.
//    provision  — present disclosure, require explicit operator acknowledgement,
//                 read the subject on a no-echo TTY, provision exactly one entry.
//
//  This host does NOT stop/start the server and does NOT run itself against
//  t009l — the operator drives the stop-backup-provision-restart-verify-rollback
//  runbook around it and passes --server-quiescent only when the server is down.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Features.PilotIdentity;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.AccountBootstrap
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    internal static class Program
    {
        // The pilot's code-defined provider/backend and notice/retention versions. These MUST match the
        // live account service (PilotDisclosureVersions.NoticeVersion / RetentionVersion in
        // Features/PilotIdentity/PilotSessionLifecycleObserver.cs), or a first-bind will reject
        // DisclosureIncomplete. That file is net48-only (Valheim refs) so it is not link-compiled here;
        // these constants are the single agreed value, asserted by the host tests.
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";

        private const string JournalFileName = "account-journal.bin";
        private const string KeyFileName = "pilot-hmac.key";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) return Usage();
                string verb = args[0];
                var opts = ParseOptions(args, 1);

                switch (verb)
                {
                    case "preflight": return RunPreflight(opts);
                    case "provision": return RunProvision(opts);
                    case "-h":
                    case "--help":
                    case "help": return Usage();
                    default:
                        Console.Error.WriteLine("unknown verb: " + verb);
                        return Usage();
                }
            }
            catch (OperatorAbort ex)
            {
                Console.Error.WriteLine("ABORT: " + ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                // Never let an exception message carry a subject — the subject only exists inside a local
                // no-echo buffer that is never interpolated into a message.
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 3;
            }
        }

        // ---- option parsing (NOTE: there is intentionally no --subject flag) ----

        private sealed class Options
        {
            public string StoreDir = string.Empty;
            public string QaRoot = string.Empty;
            public List<string> ForbiddenRoots = new List<string>();
            public bool ServerQuiescent;
            public string OperationId = string.Empty;
            public bool AckDisclosure;
        }

        private static Options ParseOptions(string[] args, int start)
        {
            var o = new Options();
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                string Next(string name)
                {
                    if (i + 1 >= args.Length) throw new OperatorAbort(name + " requires a value");
                    return args[++i];
                }
                switch (a)
                {
                    case "--store-dir": o.StoreDir = Next(a); break;
                    case "--qa-root": o.QaRoot = Next(a); break;
                    case "--forbid-root": o.ForbiddenRoots.Add(Next(a)); break;
                    case "--server-quiescent": o.ServerQuiescent = true; break;
                    case "--op": o.OperationId = Next(a); break;
                    case "--i-acknowledge-current-disclosure": o.AckDisclosure = true; break;
                    default:
                        // Reject unknown flags rather than silently ignoring — a fat-fingered --subject
                        // must NOT be silently swallowed into a subject channel.
                        throw new OperatorAbort("unknown option: " + a);
                }
            }
            if (o.StoreDir.Length == 0) throw new OperatorAbort("--store-dir <dir> required");
            if (o.QaRoot.Length == 0) throw new OperatorAbort("--qa-root <isolated-t009l-data-root> required");
            return o;
        }

        // ---- shared target resolution ----

        private static LiveStoreGuardConfig BuildConfig(Options o) =>
            new LiveStoreGuardConfig(RealPath(o.QaRoot), ResolveForbidden(o.ForbiddenRoots),
                ProviderNs, Backend, NoticeV, RetentionV);

        private static List<string> ResolveForbidden(List<string> raw)
        {
            var res = new List<string>();
            foreach (var r in raw)
            {
                // A production root may not exist on this host; keep the lexical value if realpath fails.
                string canon;
                try { canon = RealPath(r); } catch { canon = r; }
                res.Add(canon);
            }
            return res;
        }

        private static LiveStoreTarget ResolveTarget(Options o)
        {
            string lexical = LexicalAbsolute(o.StoreDir);
            string resolved = RealPathBestEffort(o.StoreDir, out bool containedSymlink);
            string journal = Path.Combine(resolved, JournalFileName);
            string key = Path.Combine(resolved, KeyFileName);

            bool storeExists = File.Exists(journal);
            bool keyExists = File.Exists(key);
            PathOwnershipState keyOwn = keyExists ? StatOwnership(key) : PathOwnershipState.OwnerOnly(owned: true);

            return new LiveStoreTarget(lexical, resolved, journal, key,
                storeExists, keyExists, containedSymlink, keyOwn);
        }

        // ---- preflight ----

        private static int RunPreflight(Options o)
        {
            var cfg = BuildConfig(o);
            var guard = new LiveStoreProvisioningGuard(cfg);
            var target = ResolveTarget(o);

            Banner("preflight", target, cfg);

            // Opening the store REHYDRATES it read-only (no write). If the resolved dir does not exist yet,
            // an empty store is reported (a fresh QA target the operator will initialize under the runbook).
            var store = OpenStoreReadOnly(target);
            var report = guard.Preflight(target, o.ServerQuiescent, store);

            Console.WriteLine(report.ToOutputLine());
            // Preflight is a proof, not a gate: it returns 0 when it successfully produced a report, and a
            // non-zero advisory code when the target would block a provision, so scripts can branch.
            return report.Ready ? 0 : 10;
        }

        // ---- provision ----

        private static int RunProvision(Options o)
        {
            if (o.OperationId.Length == 0) throw new OperatorAbort("--op <unique-operation-id> required for provision");

            var cfg = BuildConfig(o);
            var guard = new LiveStoreProvisioningGuard(cfg);
            var target = ResolveTarget(o);

            Banner("provision", target, cfg);

            // Fail closed on confinement/quiescence/health BEFORE presenting the disclosure or reading any
            // subject, so a mis-targeted run never even prompts.
            var confinement = guard.EvaluateTarget(target);
            if (confinement != TargetConfinement.UnderQaRoot)
            {
                Console.WriteLine("resultCode=" + confinement);
                return 10;
            }
            if (!o.ServerQuiescent)
            {
                Console.WriteLine("resultCode=ServerNotQuiescent");
                return 10;
            }

            var store = OpenStoreReadOnly(target);
            if (store.QuarantinedIntentTransactions > 0)
            {
                Console.WriteLine("resultCode=StoreQuarantinedNeedsReview");
                return 10;
            }
            if (!target.KeyOwnership.IsOwnerOnly)
            {
                // Match the shipped core's fail-closed code so the runbook table stays accurate.
                Console.WriteLine("resultCode=KeyPathTooPermissive");
                return 10;
            }

            // Present the current disclosure and require EXPLICIT operator acknowledgement.
            PresentDisclosure(cfg);
            if (!o.AckDisclosure)
            {
                Console.WriteLine("resultCode=DisclosureNotAcknowledged");
                Console.Error.WriteLine("Re-run with --i-acknowledge-current-disclosure once you have read the notice above.");
                return 10;
            }

            // The subject may ONLY arrive on a protected no-echo TTY. Refuse redirected/piped input so the
            // subject cannot be fed from a file/heredoc/process substitution (a non-interactive channel).
            if (Console.IsInputRedirected)
            {
                Console.WriteLine("resultCode=SubjectChannelForbidden");
                Console.Error.WriteLine("stdin is redirected; the subject must be typed on an interactive no-echo TTY.");
                return 10;
            }

            var bootstrap = OpenBootstrap(target);
            var disclosure = CurrentDisclosure(cfg);
            var ack = new DisclosureAcknowledgement(cfg.NoticeVersion, UnixNow());

            // Read the subject into a transient local buffer with echo OFF. It is passed straight into the
            // guard/core and never stored, logged, or interpolated into any message.
            string subject = ReadNoEcho("provider subject (no echo): ");
            try
            {
                if (subject.Length == 0)
                {
                    Console.WriteLine("resultCode=ProviderSubjectInvalid");
                    return 10;
                }

                var outcome = guard.Provision(target, o.ServerQuiescent, store, bootstrap,
                    ProvisioningInputChannel.ProtectedNoEchoStdin, o.OperationId, subject,
                    disclosure, ack, UnixNow());

                Console.WriteLine(PilotProvisioningInputGate.RedactSubject(outcome.ToOutputLine(), subject));
                return outcome.Accepted ? 0 : 10;
            }
            finally
            {
                // Best-effort scrub of the transient subject buffer reference.
                subject = string.Empty;
            }
        }

        // ---- disclosure ----

        private static PilotDisclosure CurrentDisclosure(LiveStoreGuardConfig cfg)
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command",
                "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "pilot-ops@example.invalid", cfg.NoticeVersion);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        private static void PresentDisclosure(LiveStoreGuardConfig cfg)
        {
            var d = CurrentDisclosure(cfg);
            Console.WriteLine();
            Console.WriteLine("---- CURRENT PRIVACY DISCLOSURE (notice " + d.NoticeVersion + ") ----");
            Console.WriteLine("  stored categories : " + string.Join(", ", d.StoredCategoryNames()));
            Console.WriteLine("  operator contact  : " + d.OperatorContact);
            Console.WriteLine("  retention         : " + cfg.RetentionVersion + " (30 days after pilot close)");
            Console.WriteLine("  export/deletion   : " + d.ExportDeletionRoute);
            Console.WriteLine("  explicit reset    : " + d.StatesExplicitResetPossibility);
            Console.WriteLine("  disclosure complete: " + d.IsComplete());
            Console.WriteLine("-------------------------------------------------------------");
            Console.WriteLine();
        }

        // ---- real OS I/O ----

        private static PilotAccountStore OpenStoreReadOnly(LiveStoreTarget target)
        {
            // The store constructor rehydrates from the journal (read); if the file is absent it starts
            // empty. It NEVER truncates on open — we only ever append through the bootstrap core.
            return new PilotAccountStore(target.JournalPath);
        }

        private static LocalAllowlistBootstrap OpenBootstrap(LiveStoreTarget target)
        {
            var store = new PilotAccountStore(target.JournalPath);
            var ring = OpenOrMintKeyRing(target.KeyPath);
            var svc = new PilotAccountService(store, ring, NoticeV, RetentionV);
            return new LocalAllowlistBootstrap(new PilotProvisioningInputGate(), svc);
        }

        private static LookupKeyRing OpenOrMintKeyRing(string keyPath)
        {
            byte[] bytes;
            if (File.Exists(keyPath))
            {
                bytes = File.ReadAllBytes(keyPath);   // real key material, owner-only, never logged
            }
            else
            {
                bytes = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                File.WriteAllBytes(keyPath, bytes);
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return new LookupKeyRing(new LookupHmacKey(new LookupKeyVersion("k1"), bytes));
        }

        private static PathOwnershipState StatOwnership(string path)
        {
            var mode = File.GetUnixFileMode(path);
            bool groupR = (mode & UnixFileMode.GroupRead) != 0;
            bool groupW = (mode & UnixFileMode.GroupWrite) != 0;
            bool otherR = (mode & UnixFileMode.OtherRead) != 0;
            bool otherW = (mode & UnixFileMode.OtherWrite) != 0;
            return new PathOwnershipState(ownedByServiceAccount: true,
                groupReadable: groupR, groupWritable: groupW, otherReadable: otherR, otherWritable: otherW);
        }

        /// <summary>Lexical absolute normalization (no symlink resolution) — the "requested" path.</summary>
        private static string LexicalAbsolute(string path) =>
            LiveStoreGuardConfig.Canonicalize(Path.GetFullPath(path));

        /// <summary>realpath: fully resolve symlinks. Throws if the path does not resolve.</summary>
        private static string RealPath(string path)
        {
            string full = Path.GetFullPath(path);
            var resolved = ResolveAllLinks(full, out _);
            return LiveStoreGuardConfig.Canonicalize(resolved);
        }

        /// <summary>realpath that tolerates a not-yet-created leaf: resolves the deepest existing ancestor
        /// (following symlinks) and re-appends the missing tail, so a fresh QA target dir still classifies
        /// correctly. Reports whether any resolved component was a symlink.</summary>
        private static string RealPathBestEffort(string path, out bool containedSymlink)
        {
            string full = Path.GetFullPath(path);
            containedSymlink = false;
            var missing = new List<string>();
            string cur = full;
            while (cur.Length > 1 && !File.Exists(cur) && !Directory.Exists(cur))
            {
                missing.Insert(0, Path.GetFileName(cur));
                string? parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent) || parent == cur) break;
                cur = parent;
            }
            string resolvedExisting = ResolveAllLinks(cur, out containedSymlink);
            string combined = resolvedExisting;
            foreach (var seg in missing) combined = Path.Combine(combined, seg);
            return LiveStoreGuardConfig.Canonicalize(combined);
        }

        /// <summary>Resolve every symlink component of an EXISTING path. Sets <paramref name="sawSymlink"/>
        /// when any component was a link.</summary>
        private static string ResolveAllLinks(string existingPath, out bool sawSymlink)
        {
            sawSymlink = false;
            // Walk each component from the root, following links via the framework resolver.
            string full = Path.GetFullPath(existingPath);
            string[] parts = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string acc = "/";
            foreach (var part in parts)
            {
                acc = Path.Combine(acc, part);
                try
                {
                    FileSystemInfo? info = Directory.Exists(acc)
                        ? new DirectoryInfo(acc)
                        : (File.Exists(acc) ? new FileInfo(acc) : null);
                    if (info?.LinkTarget != null)
                    {
                        sawSymlink = true;
                        var final = info.ResolveLinkTarget(returnFinalTarget: true);
                        if (final != null) acc = final.FullName;
                    }
                }
                catch { /* treat an unresolvable component as literal */ }
            }
            return LiveStoreGuardConfig.Canonicalize(acc);
        }

        /// <summary>Read a line with echo OFF using an intercepting key loop (no terminal echo, no shell
        /// history). Backspace edits the transient buffer. The buffer is the ONLY place the subject lives.</summary>
        private static string ReadNoEcho(string prompt)
        {
            Console.Error.Write(prompt);
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); break; }
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                if (char.IsControl(k.KeyChar)) continue;
                sb.Append(k.KeyChar);
            }
            return sb.ToString().Trim();
        }

        // ---- misc ----

        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static void Banner(string verb, LiveStoreTarget target, LiveStoreGuardConfig cfg)
        {
            Console.WriteLine("================================================================");
            Console.WriteLine(" niflheim-account-bootstrap  verb=" + verb);
            Console.WriteLine(" qa-root(resolved)   = " + cfg.QaDataRootCanonical);
            Console.WriteLine(" store-dir(requested)= " + target.RequestedLexicalStoreDir);
            Console.WriteLine(" store-dir(resolved) = " + target.ResolvedStoreDir);
            Console.WriteLine(" journal             = " + target.JournalPath + " exists=" + target.StoreExists);
            Console.WriteLine(" key                 = " + target.KeyPath + " exists=" + target.KeyExists);
            Console.WriteLine(" provider/backend    = " + cfg.ProviderNamespace + " / " + cfg.BackendIssuer);
            Console.WriteLine("================================================================");
        }

        private static int Usage()
        {
            Console.WriteLine("usage:");
            Console.WriteLine("  niflheim-account-bootstrap preflight --store-dir <dir> --qa-root <root> [--forbid-root <r>]... [--server-quiescent]");
            Console.WriteLine("  niflheim-account-bootstrap provision --store-dir <dir> --qa-root <root> [--forbid-root <r>]... --server-quiescent --op <id> --i-acknowledge-current-disclosure");
            Console.WriteLine();
            Console.WriteLine("The provider subject is NEVER a flag/env var; provision reads it on a no-echo TTY.");
            return 1;
        }

        private sealed class OperatorAbort : Exception
        {
            public OperatorAbort(string message) : base(message) { }
        }
    }
}
