// ============================================================================
//  AIP — Wound-down re-admission barrier under the PREVIOUS key version.
// ----------------------------------------------------------------------------
//  Regression evidence for PR #399: after a wound-down operation
//  (disable / delete / quarantine) followed by a SUPPORTED key rotation, the
//  revoked credential for that subject is still stored under the PREVIOUS key.
//  The active-only barrier probe would miss it and silently auto-mint a fresh
//  account — contradicting the contracts.md active-or-previous-key barrier.
//
//  These tests prove:
//    * disable    -> rotate (k2 active / k1 previous) -> same-subject rejoin REJECTS.
//    * delete     -> rotate -> rejoin REJECTS.
//    * quarantine -> rotate -> rejoin REJECTS.
//    * a physical purge (whole-fixture reset) still permits a legitimate fresh mint.
//
//  Every file under test is engine-free (System.*+LINQ), so the asserted
//  behaviour IS the shipped net48 behaviour.
// ============================================================================

using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPreviousKeyBarrierTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;
        private const long Day = 86_400L;
        private const string AdminHost = "76561198000000001";
        private const string Subject = "76561198000000900";

        private readonly string _dir;

        public NiflheimPreviousKeyBarrierTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-prevkey-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- helpers ----

        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        // Deterministic key so a value reproduces across store reboots within one test.
        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        // k1-only ring (initial boot) and k2-active/k1-previous ring (post-rotation).
        private static LookupKeyRing RingK1() => new LookupKeyRing(FixedKey("k1", 10));
        private static LookupKeyRing RingK2overK1() => new LookupKeyRing(FixedKey("k2", 70), FixedKey("k1", 10));

        private static VerifiedProviderPrincipal Principal() =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), Subject, transportHandle: 900L);

        private PilotAccountService Service(PilotAccountStore store, LookupKeyRing ring) =>
            new PilotAccountService(store, ring, NoticeV, RetentionV);

        private static OperatorAdminGate AdminGate() => new OperatorAdminGate(new[] { AdminHost });
        private static ServerObservedAdminContext Op() => new ServerObservedAdminContext(AdminHost, ProviderNs);

        private OperatorAccountService Operator(PilotAccountStore store, AccountMutationFence fence) =>
            new OperatorAccountService(store, AdminGate(), fence, new PilotSessionRegistry(), TimeSpan.FromSeconds(5));

        private PilotDestructionService Destruction(PilotAccountStore store, AccountMutationFence fence)
        {
            var privacy = new PilotPrivacyService(store, AdminGate(), fence, TimeSpan.FromSeconds(5));
            return new PilotDestructionService(store, AdminGate(), fence, privacy, TimeSpan.FromSeconds(5));
        }

        /// <summary>First-bind the subject under the k1 ring, mint the opaque account.</summary>
        private PilotAccountResolution BindUnderK1(PilotAccountStore store)
        {
            var res = Service(store, RingK1()).ResolveOrCreateAccount("bind", Principal(), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);
            return res;
        }

        /// <summary>Reboot with a k2-active/k1-previous ring and re-join the SAME subject. Its stored
        /// credential is under k1 (previous). Auto-mint MUST NOT occur: the wound-down barrier under the
        /// previous key rejects.</summary>
        private PilotAccountResolution RejoinAfterRotation()
        {
            var reboot = new PilotAccountStore(JournalPath);
            return Service(reboot, RingK2overK1()).ResolveOrCreateAccount("rejoin", Principal(), T0 + 10 * Day);
        }

        // ── disable -> rotate -> rejoin REJECTS ─────────────────────────────────
        [Fact]
        public void Disable_ThenRotate_SameSubjectRejoin_RejectsUnderPreviousKey_NotCreated()
        {
            var store = new PilotAccountStore(JournalPath);
            var bind = BindUnderK1(store);

            var fence = new AccountMutationFence();
            Assert.Equal(OperatorOutcome.Applied, Operator(store, fence).Disable(Op(), bind.AccountId, "op-dis", T0 + Day).Outcome);

            var rejoin = RejoinAfterRotation();

            Assert.NotEqual(AccountAdmissionOutcome.Created, rejoin.Outcome);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountDisabled, rejoin.RejectionCode);
        }

        // ── delete -> rotate -> rejoin REJECTS ──────────────────────────────────
        [Fact]
        public void Delete_ThenRotate_SameSubjectRejoin_RejectsUnderPreviousKey_NotCreated()
        {
            var store = new PilotAccountStore(JournalPath);
            var bind = BindUnderK1(store);

            var fence = new AccountMutationFence();
            Assert.Equal(OperatorOutcome.Applied, Operator(store, fence).Delete(Op(), bind.AccountId, "op-del", T0 + Day).Outcome);

            var rejoin = RejoinAfterRotation();

            Assert.NotEqual(AccountAdmissionOutcome.Created, rejoin.Outcome);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountDeletionPending, rejoin.RejectionCode);
        }

        // ── quarantine -> rotate -> rejoin REJECTS ──────────────────────────────
        [Fact]
        public void Quarantine_ThenRotate_SameSubjectRejoin_RejectsUnderPreviousKey_NotCreated()
        {
            var store = new PilotAccountStore(JournalPath);
            var bind = BindUnderK1(store);

            var fence = new AccountMutationFence();
            Destruction(store, fence).Quarantine(Op(), "op-quar", bind.AccountId, "ambiguous-state", T0 + Day);

            var rejoin = RejoinAfterRotation();

            Assert.NotEqual(AccountAdmissionOutcome.Created, rejoin.Outcome);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountQuarantined, rejoin.RejectionCode);
        }

        // ── physical purge (whole-fixture reset) STILL permits a legitimate fresh mint ──
        // Once the credential is physically gone (no journal, whole-fixture reset), no barrier record
        // remains under any key version, so a later join under the rotated ring legitimately mints anew.
        [Fact]
        public void PhysicalReset_ThenRotate_SameSubjectRejoin_LegitimatelyMintsFreshAccount()
        {
            var store = new PilotAccountStore(JournalPath);
            var bind = BindUnderK1(store);

            var fence = new AccountMutationFence();
            Assert.Equal(OperatorOutcome.Applied, Operator(store, fence).Delete(Op(), bind.AccountId, "op-del", T0 + Day).Outcome);

            // Whole-fixture reset: physically remove the journal so no credential/account record survives.
            File.Delete(JournalPath);

            var fresh = new PilotAccountStore(JournalPath);
            var mint = Service(fresh, RingK2overK1()).ResolveOrCreateAccount("fresh", Principal(), T0 + 40 * Day);

            Assert.Equal(AccountAdmissionOutcome.Created, mint.Outcome);
            Assert.True(mint.Accepted);
            Assert.NotEqual(bind.AccountId, mint.AccountId);
        }
    }
}
