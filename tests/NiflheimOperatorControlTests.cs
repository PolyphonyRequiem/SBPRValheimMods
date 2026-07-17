// ============================================================================
//  IAP-009 — Operator foundation: bootstrap, inspect, disable, and drain.
// ----------------------------------------------------------------------------
//  Executable evidence for the operator-control acceptance IDs. Exercises the
//  engine-free CLEAN core (AccountMutationFence, PilotSessionRegistry,
//  OperatorAdminGate, OperatorAccountService, LocalAllowlistBootstrap) that
//  ships under net48. No file under test references UnityEngine/Valheim/BepInEx.
//
//  Named acceptance (spec §Requirement-to-acceptance; plan §Tracer 4 subset):
//    AT-AIP-ADMIN-INSPECT           AT-AIP-ADMIN-DISABLE
//    AT-AIP-LOCAL-BOOTSTRAP-SCOPE   AT-AIP-NONADMIN-REJECT
//    AT-AIP-MUTATION-FENCE          AT-AIP-DISABLE-CLOSES-SESSION
//    AT-AIP-DELETE-DRAIN-BARRIER
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Features.PilotIdentity;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimOperatorControlTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;
        private const string AdminHost = "Steam_76561198000000001";
        private const string NonAdminHost = "Steam_76561198999999999";

        private readonly string _dir;

        public NiflheimOperatorControlTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t009-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- helpers ----

        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private LookupKeyRing Ring() => new LookupKeyRing(FixedKey("k1", 10));

        private PilotAccountService NewService(PilotAccountStore store) =>
            new PilotAccountService(store, Ring(), NoticeV, RetentionV);

        private static VerifiedProviderPrincipal Principal(string subject) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), subject, transportHandle: 1L);

        private static PilotDisclosure CompleteDisclosure()
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        private static DisclosureAcknowledgement Ack() => new DisclosureAcknowledgement(NoticeV, T0);

        private PilotAccountResolution ProvisionAndBind(PilotAccountService svc, string subject)
        {
            svc.ProvisionAllowlistEntry("prov-" + subject, ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);
            return svc.ResolveOrCreateAccount("bind-" + subject, Principal(subject), T0);
        }

        private static OperatorAdminGate AdminGate() =>
            new OperatorAdminGate(new List<string> { AdminHost });

        private static ServerObservedAdminContext Admin() =>
            new ServerObservedAdminContext(AdminHost, ProviderNs);

        private static ServerObservedAdminContext NonAdmin() =>
            new ServerObservedAdminContext(NonAdminHost, ProviderNs);

        private OperatorAccountService NewOperator(PilotAccountStore store, AccountMutationFence fence,
            PilotSessionRegistry sessions, TimeSpan drainTimeout) =>
            new OperatorAccountService(store, AdminGate(), fence, sessions, drainTimeout);

        // ── AT-AIP-ADMIN-INSPECT ─────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_ADMIN_INSPECT_ReturnsSafeSummary_NoRawSubjectOrHmac()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var bind = ProvisionAndBind(svc, "76561198000000001");
            Assert.True(bind.Accepted);

            var sessions = new PilotSessionRegistry();
            var op = NewOperator(store, new AccountMutationFence(), sessions, TimeSpan.FromSeconds(5));

            var result = op.Inspect(Admin(), bind.AccountId);
            Assert.True(result.Accepted);
            Assert.NotNull(result.Summary);
            var s = result.Summary!;
            Assert.Equal(bind.AccountId.Value, s.AccountId);
            Assert.Equal("Active", s.Status);
            Assert.Equal(1, s.CredentialCount);
            Assert.Equal(new[] { ProviderNs }, s.CredentialClasses);
            Assert.False(s.HasLiveSession);

            // No raw subject / HMAC / secret anywhere in the projection.
            var ring = Ring();
            string hmacHex = ring.CredentialHmacActive(ProviderNs, Backend, "76561198000000001").Hex;
            foreach (var field in new[] { s.AccountId, s.Status, string.Join(",", s.CredentialClasses),
                                          s.NoticeVersion, s.RetentionPolicyVersion })
            {
                Assert.DoesNotContain("76561198000000001", field, StringComparison.Ordinal);
                Assert.DoesNotContain(hmacHex, field, StringComparison.Ordinal);
            }
        }

        // ── AT-AIP-NONADMIN-REJECT ──────────────────────────────────────────────
        [Fact]
        public void AT_AIP_NONADMIN_REJECT_NonAdminAndUnauthenticated_RejectWithoutMutation()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var bind = ProvisionAndBind(svc, "76561198000000001");
            var op = NewOperator(store, new AccountMutationFence(), new PilotSessionRegistry(), TimeSpan.FromSeconds(5));

            // Non-admin authenticated peer cannot inspect, disable, or delete.
            Assert.Equal(OperatorOutcome.Rejected, op.Inspect(NonAdmin(), bind.AccountId).Outcome);
            var dis = op.Disable(NonAdmin(), bind.AccountId, "op-dis", T0);
            Assert.Equal(OperatorOutcome.Rejected, dis.Outcome);
            Assert.Equal("NotAdmin", dis.ResultCode);
            var del = op.Delete(NonAdmin(), bind.AccountId, "op-del", T0);
            Assert.Equal("NotAdmin", del.ResultCode);

            // Unauthenticated (empty host) also rejects.
            var none = op.Disable(ServerObservedAdminContext.None, bind.AccountId, "op-dis2", T0);
            Assert.Equal("UnauthenticatedPeer", none.ResultCode);

            // No mutation occurred: account is still Active, and a rebuilt store proves nothing was journaled.
            Assert.True(store.TryGetAccount(bind.AccountId, out var acct));
            Assert.Equal(PilotAccountStatus.Active, acct.Status);
            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(bind.AccountId, out var acct2));
            Assert.Equal(PilotAccountStatus.Active, acct2.Status);
        }

        // ── AT-AIP-ADMIN-DISABLE + AT-AIP-DISABLE-CLOSES-SESSION ────────────────
        [Fact]
        public void AT_AIP_ADMIN_DISABLE_ClosesAdmissionAndSession_DurableAcrossReboot()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var bind = ProvisionAndBind(svc, "76561198000000001");

            var sessions = new PilotSessionRegistry();
            Assert.True(sessions.TryReservePending(bind.AccountId.Value, "sess-1", 4242L));
            Assert.True(sessions.TryActivate(bind.AccountId.Value, "sess-1", 4242L));

            var op = NewOperator(store, new AccountMutationFence(), sessions, TimeSpan.FromSeconds(5));
            var result = op.Disable(Admin(), bind.AccountId, "op-dis", T0);

            Assert.Equal(OperatorOutcome.Applied, result.Outcome);
            Assert.True(result.SessionClosed);
            Assert.Equal(4242L, result.ClosedTransportHandle);

            // Status is Disabled and durable across a reboot (replayed from journal).
            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(bind.AccountId, out var acct));
            Assert.Equal(PilotAccountStatus.Disabled, acct.Status);

            // Post-disable admission rejects (AccountDisabled) — a delayed reconnect cannot reopen authority.
            var reboundSvc = NewService(reboot);
            var rejoin = reboundSvc.ResolveOrCreateAccount("rejoin", Principal("76561198000000001"), T0 + 10);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountDisabled, rejoin.RejectionCode);

            // Session was removed deterministically.
            Assert.False(sessions.HasSession(bind.AccountId.Value));

            // Idempotent replay of the same op.
            var again = op.Disable(Admin(), bind.AccountId, "op-dis", T0);
            Assert.Equal(OperatorOutcome.Replayed, again.Outcome);
        }

        // ── AT-AIP-MUTATION-FENCE ───────────────────────────────────────────────
        // A disable draining an in-flight mutation waits for it, then commits atomically. If the drain
        // cannot complete within the bounded timeout, the disable aborts leaving a coherent Active state.
        [Fact]
        public async Task AT_AIP_MUTATION_FENCE_DrainsInFlight_AndFailedDrainLeavesRecoverableState()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var bind = ProvisionAndBind(svc, "76561198000000001");
            var fence = new AccountMutationFence();
            var sessions = new PilotSessionRegistry();

            // (1) Drain success: hold an in-flight mutation on another thread, disable must wait for it.
            var opFast = NewOperator(store, fence, sessions, TimeSpan.FromSeconds(10));
            var released = new ManualResetEventSlim(false);
            var mutationEntered = new ManualResetEventSlim(false);
            var holder = Task.Run(() =>
            {
                using (fence.EnterMutation(bind.AccountId.Value))
                {
                    mutationEntered.Set();
                    released.Wait(TimeSpan.FromSeconds(5));
                }
            });
            Assert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)));

            var disableTask = Task.Run(() => opFast.Disable(Admin(), bind.AccountId, "op-dis", T0));
            // Disable is blocked on the drain barrier while the mutation is held.
            var completedEarly = await Task.WhenAny(disableTask, Task.Delay(300));
            Assert.NotSame(disableTask, completedEarly);
            Assert.Equal(PilotAccountStatus.Active, GetStatus(store, bind.AccountId)); // not yet disabled

            released.Set();
            await holder;
            var disResult = await disableTask;
            Assert.Equal(OperatorOutcome.Applied, disResult.Outcome);
            Assert.Equal(PilotAccountStatus.Disabled, GetStatus(store, bind.AccountId));

            // (2) Failed drain: a stuck in-flight mutation + tiny timeout aborts the lifecycle op with NO
            //     mutation. Use a fresh account so we start Active.
            var store2 = new PilotAccountStore(Path.Combine(_dir, "j2.bin"));
            var svc2 = NewService(store2);
            var bind2 = ProvisionAndBind(svc2, "76561198000000002");
            var fence2 = new AccountMutationFence();
            var opSlow = NewOperator(store2, fence2, new PilotSessionRegistry(), TimeSpan.FromMilliseconds(100));
            var stuck = fence2.EnterMutation(bind2.AccountId.Value); // never released within timeout
            var timedOut = opSlow.Delete(Admin(), bind2.AccountId, "op-del", T0);
            Assert.Equal(OperatorOutcome.Rejected, timedOut.Outcome);
            Assert.Equal("DrainTimeout", timedOut.ResultCode);
            Assert.Equal(PilotAccountStatus.Active, GetStatus(store2, bind2.AccountId)); // recoverable, untouched
            stuck.Dispose();
        }

        private static PilotAccountStatus GetStatus(PilotAccountStore store, PilotAccountId id)
        {
            Assert.True(store.TryGetAccount(id, out var acct));
            return acct.Status;
        }

        // ── AT-AIP-DELETE-DRAIN-BARRIER ─────────────────────────────────────────
        // Delete drains, commits DeletionPending, revokes credential + allowlist so a stale allowlist
        // cannot immediately recreate the account, and closes the session — all durable across reboot.
        [Fact]
        public void AT_AIP_DELETE_DRAIN_BARRIER_RevokesCredentialAndAllowlist_AndBlocksRecreation()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var bind = ProvisionAndBind(svc, "76561198000000001");
            var sessions = new PilotSessionRegistry();
            Assert.True(sessions.TryReservePending(bind.AccountId.Value, "sess-1", 77L));

            var op = NewOperator(store, new AccountMutationFence(), sessions, TimeSpan.FromSeconds(5));
            var result = op.Delete(Admin(), bind.AccountId, "op-del", T0);
            Assert.Equal(OperatorOutcome.Applied, result.Outcome);
            Assert.True(result.SessionClosed);
            Assert.Equal(77L, result.ClosedTransportHandle);

            // Durable across reboot: account DeletionPending, credential revoked, allowlist revoked.
            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(bind.AccountId, out var acct));
            Assert.Equal(PilotAccountStatus.DeletionPending, acct.Status);
            Assert.True(reboot.TryGetCredential(bind.CredentialBindingId, out var cred));
            Assert.Equal(CredentialStatus.Revoked, cred.Status);
            Assert.True(reboot.TryGetAllowlistEntry(cred.AllowlistEntryId, out var allow));
            Assert.Equal(AllowlistStatus.Revoked, allow.Status);

            // A re-join for the SAME subject cannot recreate the account: the credential is revoked (so it
            // no longer resolves the old account) AND the allowlist is revoked (so first-bind has no active
            // entry) — the join is rejected as NotAllowlisted, proving no immediate recreation.
            var reboundSvc = NewService(reboot);
            var rejoin = reboundSvc.ResolveOrCreateAccount("rejoin", Principal("76561198000000001"), T0 + 5);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.NotAllowlisted, rejoin.RejectionCode);
        }

        // ── AT-AIP-LOCAL-BOOTSTRAP-SCOPE ────────────────────────────────────────
        // The local utility is OS-owner-scoped, allowlist-only, no-echo-stdin, and can NEVER perform an
        // account-lifecycle verb or accept a subject off argv/env.
        [Fact]
        public void AT_AIP_LOCAL_BOOTSTRAP_SCOPE_OwnerOnly_AllowlistOnly_NoEchoStdin()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store);
            var boot = new LocalAllowlistBootstrap(new PilotProvisioningInputGate(), svc);
            var ownerOnly = PathOwnershipState.OwnerOnly();

            // Happy path: owner-only path + protected no-echo stdin + provision verb → provisions.
            var ok = boot.Provision(ProvisioningInputChannel.ProtectedNoEchoStdin, ownerOnly,
                "prov-op", ProviderNs, Backend, "76561198000000009", CompleteDisclosure(), Ack(), T0);
            Assert.True(ok.Accepted);
            Assert.Equal("Provisioned", ok.ResultCode);
            Assert.StartsWith("allow-", ok.AllowlistEntryId);
            // Output line never carries the raw subject.
            Assert.DoesNotContain("76561198000000009", ok.ToOutputLine(), StringComparison.Ordinal);

            // A subject off the command line / env / chat is refused BEFORE any HMAC.
            foreach (var badChannel in new[] { ProvisioningInputChannel.CommandLineArgument,
                                               ProvisioningInputChannel.EnvironmentVariable,
                                               ProvisioningInputChannel.ChatOrConsoleCommand })
            {
                var r = boot.Provision(badChannel, ownerOnly, "prov-bad", ProviderNs, Backend,
                    "76561198000000010", CompleteDisclosure(), Ack(), T0);
                Assert.False(r.Accepted);
                Assert.Equal("SubjectChannelForbidden", r.ResultCode);
            }

            // A group/other-readable key path fails closed.
            var tooOpen = new PathOwnershipState(ownedByServiceAccount: true, groupReadable: true,
                groupWritable: false, otherReadable: false, otherWritable: false);
            var permissive = boot.Provision(ProvisioningInputChannel.ProtectedNoEchoStdin, tooOpen,
                "prov-open", ProviderNs, Backend, "76561198000000011", CompleteDisclosure(), Ack(), T0);
            Assert.False(permissive.Accepted);
            Assert.Equal("KeyPathTooPermissive", permissive.ResultCode);

            // The utility can NEVER perform an account-lifecycle verb (inspect/disable/delete/reset/etc).
            foreach (var verb in new[] { LocalBootstrapVerb.InspectAccount, LocalBootstrapVerb.DisableAccount,
                                         LocalBootstrapVerb.DeleteAccount, LocalBootstrapVerb.ResetAccount,
                                         LocalBootstrapVerb.ExportAccount, LocalBootstrapVerb.ChangeRetention,
                                         LocalBootstrapVerb.InvokeGameplayCommand })
            {
                var r = boot.RejectOutOfScope(verb, ownerOnly);
                Assert.False(r.Accepted);
                Assert.Equal("VerbOutOfLocalScope", r.ResultCode);
            }

            // Revoke (allowlist-only, internal-id selector) works within scope.
            var revoke = boot.Revoke(ownerOnly, "rev-op", new AllowlistEntryId(ok.AllowlistEntryId), T0);
            Assert.True(revoke.Accepted);
            Assert.Equal("Revoked", revoke.ResultCode);
        }

        // ── Stale-disconnect guard (spec edge case; deterministic close correctness) ──
        [Fact]
        public void StaleDisconnect_CannotCloseNewerSession()
        {
            var sessions = new PilotSessionRegistry();
            const string acct = "acct-x";
            Assert.True(sessions.TryReservePending(acct, "sess-old", 1L));
            // Old session disconnects but a newer admission already replaced it.
            Assert.True(sessions.CloseMatching(acct, "sess-old", 1L).Closed);
            Assert.True(sessions.TryReservePending(acct, "sess-new", 2L));
            // A late close for the OLD session/handle is a no-op — the new session survives.
            Assert.False(sessions.CloseMatching(acct, "sess-old", 1L).Closed);
            Assert.True(sessions.HasSession(acct));
            Assert.True(sessions.TryGet(acct, out var live));
            Assert.Equal("sess-new", live.SessionId);
        }
    }
}
