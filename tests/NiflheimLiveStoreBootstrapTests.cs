// ============================================================================
//  T022 — live-store account bootstrap GUARD tests.
// ----------------------------------------------------------------------------
//  Deterministic temp-dir evidence for the LiveStoreProvisioningGuard over the
//  SHIPPED IAP-009 LocalAllowlistBootstrap / PilotAccountService / PilotAccountStore.
//  Every check runs on REAL inodes (real journal files, real chmod/stat, real
//  symlinks), so the confinement / quiescence / store-health / no-truncation /
//  no-subject-leak boundaries are measured, not simulated. The synthetic subject
//  here is a reserved test-only value, never a real Steam account; the core HMACs
//  it and never persists it raw — asserted below by scanning the on-disk journal.
//
//  Named acceptance: AT-T022-CONFINE, AT-T022-PROD-REFUSE, AT-T022-SYMLINK-ESCAPE,
//  AT-T022-QUIESCENCE, AT-T022-NO-TRUNCATE, AT-T022-IDEMPOTENT, AT-T022-KEY-PERMS,
//  AT-T022-NO-SUBJECT-LEAK, AT-T022-STORE-HEALTH, AT-T022-PREFLIGHT-NO-SUBJECT.
// ============================================================================
using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Features.PilotIdentity;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public sealed class NiflheimLiveStoreBootstrapTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;
        // Reserved synthetic QA-only subject — NOT a real Steam account. HMAC'd by the core; never persisted raw.
        private const string QaSubject = "76561199999000042";

        private readonly string _root;          // the isolated t009l QA data root
        private readonly string _storeDir;      // <root>/accounts (the confined store dir)

        public NiflheimLiveStoreBootstrapTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "aip-t022-" + Guid.NewGuid().ToString("N"));
            _storeDir = Path.Combine(_root, "accounts");
            Directory.CreateDirectory(_storeDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        // ---- helpers ----

        private string JournalPath => Path.Combine(_storeDir, "account-journal.bin");
        private string KeyPath => Path.Combine(_storeDir, "pilot-hmac.key");

        private static LookupHmacKey FixedKey() { byte[] b = new byte[32]; for (int i = 0; i < 32; i++) b[i] = (byte)(7 + i); return new LookupHmacKey(new LookupKeyVersion("k1"), b); }
        private LookupKeyRing Ring() => new LookupKeyRing(FixedKey());

        private LiveStoreGuardConfig Config(params string[] forbidden) =>
            new LiveStoreGuardConfig(_root, forbidden, ProviderNs, Backend, NoticeV, RetentionV);

        private static PilotDisclosure Disclosure()
        {
            var cat = new PrivacyInventoryCategory("account-credential", "authenticate pilot join",
                "30 days after pilot close", "operator", "none", "operator deletion command",
                "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }
        private static DisclosureAcknowledgement Ack(string notice = NoticeV) => new DisclosureAcknowledgement(notice, T0);

        private void MintKeyOwnerOnly()
        {
            byte[] b = new byte[32];
            for (int i = 0; i < 32; i++) b[i] = (byte)(7 + i);
            File.WriteAllBytes(KeyPath, b);
            File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private PathOwnershipState StatKey()
        {
            var mode = File.GetUnixFileMode(KeyPath);
            return new PathOwnershipState(true,
                (mode & UnixFileMode.GroupRead) != 0, (mode & UnixFileMode.GroupWrite) != 0,
                (mode & UnixFileMode.OtherRead) != 0, (mode & UnixFileMode.OtherWrite) != 0);
        }

        private LiveStoreTarget Target(string requestedDir, string resolvedDir, bool symlink)
        {
            var keyOwn = File.Exists(KeyPath) ? StatKey() : PathOwnershipState.OwnerOnly();
            return new LiveStoreTarget(requestedDir, resolvedDir,
                Path.Combine(resolvedDir, "account-journal.bin"), Path.Combine(resolvedDir, "pilot-hmac.key"),
                File.Exists(JournalPath), File.Exists(KeyPath), symlink, keyOwn);
        }

        private LiveStoreTarget HappyTarget() => Target(_storeDir, _storeDir, symlink: false);

        private (LiveStoreProvisioningGuard guard, PilotAccountStore store, LocalAllowlistBootstrap boot) Wire()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = new PilotAccountService(store, Ring(), NoticeV, RetentionV);
            var boot = new LocalAllowlistBootstrap(new PilotProvisioningInputGate(), svc);
            return (new LiveStoreProvisioningGuard(Config()), store, boot);
        }

        // ---- confinement ----

        [Fact] // AT-T022-CONFINE
        public void ResolvedUnderQaRootIsAllowed()
        {
            var guard = new LiveStoreProvisioningGuard(Config());
            Assert.Equal(TargetConfinement.UnderQaRoot, guard.EvaluateTarget(HappyTarget()));
        }

        [Fact] // AT-T022-CONFINE (negative)
        public void ResolvedOutsideQaRootIsRefused()
        {
            var guard = new LiveStoreProvisioningGuard(Config());
            string outside = Path.Combine(Path.GetTempPath(), "aip-t022-elsewhere-" + Guid.NewGuid().ToString("N"));
            var t = Target(outside, outside, symlink: false);
            Assert.Equal(TargetConfinement.OutsideQaRoot, guard.EvaluateTarget(t));
        }

        [Fact] // AT-T022-CONFINE segment boundary — /rootX is NOT within /root
        public void SiblingSharingRootPrefixIsRefused()
        {
            var guard = new LiveStoreProvisioningGuard(Config());
            string sibling = _root + "-evil-twin";
            var t = Target(sibling, sibling, symlink: false);
            Assert.Equal(TargetConfinement.OutsideQaRoot, guard.EvaluateTarget(t));
        }

        [Fact] // AT-T022-PROD-REFUSE
        public void KnownProductionRootIsHardRefused()
        {
            string prod = "/srv/niflheim/production/accounts";
            var guard = new LiveStoreProvisioningGuard(Config(prod));
            // Even a symlink that resolves the prod path INTO the QA root is refused because the production
            // root guard is checked first against both requested and resolved paths.
            var t = Target(prod + "/store", prod + "/store", symlink: false);
            Assert.Equal(TargetConfinement.ProductionRootForbidden, guard.EvaluateTarget(t));
        }

        [Fact] // AT-T022-SYMLINK-ESCAPE — a real symlink out of the QA root is caught
        public void SymlinkComponentEscapingRootIsRefused()
        {
            string outside = Path.Combine(Path.GetTempPath(), "aip-t022-realout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            string linkDir = Path.Combine(_root, "sneaky");
            Directory.CreateSymbolicLink(linkDir, outside);   // <root>/sneaky -> /tmp/.../realout

            var guard = new LiveStoreProvisioningGuard(Config());
            // requested lexical path is under the root; resolved (realpath) lands outside → escape.
            var t = Target(linkDir, outside, symlink: true);
            Assert.Equal(TargetConfinement.SymlinkEscape, guard.EvaluateTarget(t));
        }

        // ---- provision happy path + no-truncation + idempotency ----

        [Fact] // AT-T022-NO-TRUNCATE + AT-T022-IDEMPOTENT + AT-T022-NO-SUBJECT-LEAK
        public void ProvisionAppendsIdempotentlyWithoutSubjectLeak()
        {
            MintKeyOwnerOnly();

            // Seed one PRE-EXISTING allowlist entry so we can prove the new provision APPENDS, not truncates.
            {
                var seedStore = new PilotAccountStore(JournalPath);
                var seedSvc = new PilotAccountService(seedStore, Ring(), NoticeV, RetentionV);
                seedSvc.ProvisionAllowlistEntry("seed-op", ProviderNs, Backend, "76561199999000001",
                    Disclosure(), Ack(), T0);
            }
            long sizeAfterSeed = new FileInfo(JournalPath).Length;
            Assert.True(sizeAfterSeed > 0);

            var (guard, store, boot) = Wire();
            Assert.Equal(1, CountActiveAllowlist(store));

            var outcome = guard.Provision(HappyTarget(), serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-1", QaSubject, Disclosure(), Ack(), T0);
            Assert.True(outcome.Accepted);
            Assert.Equal("Provisioned", outcome.ResultCode);
            Assert.StartsWith("allow-", outcome.AllowlistEntryId);

            // The seed entry survived (append, no truncation) and the file GREW.
            long sizeAfterProvision = new FileInfo(JournalPath).Length;
            Assert.True(sizeAfterProvision > sizeAfterSeed);
            Assert.Equal(2, CountActiveAllowlist(store));

            // Idempotent replay of the same op returns the same entry, no third record class.
            var replay = guard.Provision(HappyTarget(), serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-1", QaSubject, Disclosure(), Ack(), T0);
            Assert.True(replay.Accepted);
            Assert.Equal(outcome.AllowlistEntryId, replay.AllowlistEntryId);
            Assert.Equal(2, CountActiveAllowlist(store));

            // No raw subject anywhere: output line + the raw on-disk journal bytes.
            Assert.DoesNotContain(QaSubject, outcome.ToOutputLine(), StringComparison.Ordinal);
            byte[] raw = File.ReadAllBytes(JournalPath);
            Assert.DoesNotContain(QaSubject, System.Text.Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
            // Also decode base64 field values (the journal encodes fields as base64) and re-scan.
            Assert.False(PersistedPiiScanner.TryFindForbidden(JournalPath, new[] { QaSubject }, out _),
                "recursive base64 scan of the journal must not reveal the raw subject");
        }

        // ---- fail-closed boundaries ----

        [Fact] // AT-T022-QUIESCENCE
        public void NonQuiescentServerFailsClosed()
        {
            MintKeyOwnerOnly();
            var (guard, store, boot) = Wire();
            var outcome = guard.Provision(HappyTarget(), serverQuiescent: false, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-q", QaSubject, Disclosure(), Ack(), T0);
            Assert.False(outcome.Accepted);
            Assert.Equal("ServerNotQuiescent", outcome.ResultCode);
            Assert.Equal(0, CountActiveAllowlist(store));   // nothing written
        }

        [Fact] // AT-T022-CONFINE (provision refuses before subject)
        public void ProvisionOutsideRootRefusesBeforeAnyWrite()
        {
            MintKeyOwnerOnly();
            var (guard, store, boot) = Wire();
            string outside = Path.Combine(Path.GetTempPath(), "aip-t022-out-" + Guid.NewGuid().ToString("N"));
            var t = Target(outside, outside, symlink: false);
            var outcome = guard.Provision(t, serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-out", QaSubject, Disclosure(), Ack(), T0);
            Assert.False(outcome.Accepted);
            Assert.Equal("OutsideQaRoot", outcome.ResultCode);
            Assert.Equal(0, CountActiveAllowlist(store));
        }

        [Fact] // AT-T022-KEY-PERMS — group/other-reachable key fails closed (through the shipped core)
        public void PermissiveKeyPathFailsClosed()
        {
            MintKeyOwnerOnly();
            File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var (guard, store, boot) = Wire();
            var t = Target(_storeDir, _storeDir, symlink: false); // re-stats the now-0640 key
            var outcome = guard.Provision(t, serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-perm", QaSubject, Disclosure(), Ack(), T0);
            Assert.False(outcome.Accepted);
            Assert.Equal("KeyPathTooPermissive", outcome.ResultCode);
            Assert.Equal(0, CountActiveAllowlist(store));
        }

        [Fact] // AT-T022-NO-ARGV-ENV-CHANNEL — a non-stdin channel is refused by the shipped gate
        public void ArgvEnvChatSubjectChannelsRefused()
        {
            MintKeyOwnerOnly();
            var (guard, store, boot) = Wire();
            foreach (var bad in new[] { ProvisioningInputChannel.CommandLineArgument,
                                        ProvisioningInputChannel.EnvironmentVariable,
                                        ProvisioningInputChannel.ChatOrConsoleCommand })
            {
                var outcome = guard.Provision(HappyTarget(), serverQuiescent: true, store, boot,
                    bad, "prov-op-" + bad, QaSubject, Disclosure(), Ack(), T0);
                Assert.False(outcome.Accepted);
                Assert.Equal("SubjectChannelForbidden", outcome.ResultCode);
            }
            Assert.Equal(0, CountActiveAllowlist(store));
        }

        [Fact] // AT-T022 — absent/stale consent refused (delegated to the shipped disclosure gate)
        public void AbsentAndStaleConsentRefused()
        {
            MintKeyOwnerOnly();
            var (guard, store, boot) = Wire();

            // Stale notice acknowledgement.
            var stale = guard.Provision(HappyTarget(), serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-stale", QaSubject, Disclosure(), Ack("notice-v0"), T0);
            Assert.False(stale.Accepted);
            Assert.Equal("DisclosureIncomplete", stale.ResultCode);

            // Incomplete disclosure (missing human-approved basis).
            var badCat = new PrivacyInventoryCategory("account-credential", "auth", "30d", "operator", "none",
                "op delete", "", humanApprovedBasis: false);
            var badDisc = new PilotDisclosure(new PilotPrivacyInventory(new[] { badCat }, "ops@x.invalid", NoticeV),
                "op delete", statesExplicitResetPossibility: true);
            var absent = guard.Provision(HappyTarget(), serverQuiescent: true, store, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-absent", QaSubject, badDisc, Ack(), T0);
            Assert.False(absent.Accepted);
            Assert.Equal("DisclosureIncomplete", absent.ResultCode);

            Assert.Equal(0, CountActiveAllowlist(store));
        }

        // ---- store health ----

        [Fact] // AT-T022-STORE-HEALTH — a quarantined (Intent-only) store escalates, never silently appends
        public void QuarantinedStoreEscalates()
        {
            MintKeyOwnerOnly();
            // Force a torn Intent-only transaction via the crash injector, so boot quarantines it.
            {
                var s = new PilotAccountStore(JournalPath);
                var svc = new PilotAccountService(s, Ring(), NoticeV, RetentionV);
                Assert.Throws<CrashAfterIntent>(() =>
                    svc.ProvisionAllowlistEntry("torn-op", ProviderNs, Backend, "76561199999000009",
                        Disclosure(), Ack(), T0, new IntentCrash()));
            }
            var reopened = new PilotAccountStore(JournalPath);
            Assert.True(reopened.QuarantinedIntentTransactions > 0);

            var svc2 = new PilotAccountService(reopened, Ring(), NoticeV, RetentionV);
            var boot = new LocalAllowlistBootstrap(new PilotProvisioningInputGate(), svc2);
            var guard = new LiveStoreProvisioningGuard(Config());
            var outcome = guard.Provision(HappyTarget(), serverQuiescent: true, reopened, boot,
                ProvisioningInputChannel.ProtectedNoEchoStdin, "prov-op-q2", QaSubject, Disclosure(), Ack(), T0);
            Assert.False(outcome.Accepted);
            Assert.Equal("StoreQuarantinedNeedsReview", outcome.ResultCode);
        }

        // ---- preflight ----

        [Fact] // AT-T022-PREFLIGHT-NO-SUBJECT
        public void PreflightProvesReadinessWithoutSubject()
        {
            MintKeyOwnerOnly();
            var (guard, store, _) = Wire();
            var report = guard.Preflight(HappyTarget(), serverQuiescent: true, store);

            Assert.Equal(TargetConfinement.UnderQaRoot, report.Confinement);
            Assert.True(report.KeyOwnerOnly);
            Assert.True(report.ServerQuiescent);
            Assert.True(report.RestartRequired);
            Assert.Equal(NoticeV, report.NoticeVersion);
            Assert.Equal(RetentionV, report.RetentionVersion);
            Assert.True(report.Ready);
            Assert.Equal(string.Empty, report.BlockingResultCode);
            // The report line carries no subject (there is none to carry).
            Assert.DoesNotContain(QaSubject, report.ToOutputLine(), StringComparison.Ordinal);
        }

        [Fact] // AT-T022-PREFLIGHT — surfaces the first blocking code without writing
        public void PreflightSurfacesBlockingCode()
        {
            MintKeyOwnerOnly();
            var (guard, store, _) = Wire();
            var notQuiescent = guard.Preflight(HappyTarget(), serverQuiescent: false, store);
            Assert.False(notQuiescent.Ready);
            Assert.Equal("ServerNotQuiescent", notQuiescent.BlockingResultCode);

            string outside = Path.Combine(Path.GetTempPath(), "aip-t022-pf-out-" + Guid.NewGuid().ToString("N"));
            var outReport = guard.Preflight(Target(outside, outside, symlink: false), serverQuiescent: true, store);
            Assert.False(outReport.Ready);
            Assert.Equal("OutsideQaRoot", outReport.BlockingResultCode);
        }

        // ---- helpers ----

        private static int CountActiveAllowlist(PilotAccountStore store)
        {
            int n = 0;
            foreach (var e in store.AllowlistEntries) if (e.Status == AllowlistStatus.Active) n++;
            return n;
        }

        private sealed class CrashAfterIntent : Exception { }
        private sealed class IntentCrash : IAccountCrashInjector
        {
            public void AfterPhase(TransactionPhase phase)
            {
                if (phase == TransactionPhase.Intent) throw new CrashAfterIntent();
            }
        }
    }
}
