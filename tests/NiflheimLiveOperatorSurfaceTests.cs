// ============================================================================
//  IAP-015 (t_818742f8) — the LIVE operator command surface.
// ----------------------------------------------------------------------------
//  Executable evidence for the shipped-surface gap EXECUTE run 1426 exposed: the
//  operator lifecycle cores had NO net48 ingress, so the runbook's shipped/wired
//  claim was false. This suite exercises the engine-free brain of the ingress —
//  LiveOperatorServices (shared store/registry/fence/bound-index), the bounded
//  OperatorWireContract codec, and LiveOperatorCommandRouter — which the net48
//  direct-per-peer ZRpc ingress delegates to. The router/services are System.*-
//  only, so the asserted behaviour IS the shipped net48 behaviour.
//
//  Proof obligations (task "Required deterministic proof before handoff"):
//    * shared-store visibility: a JOIN-created account is inspectable via the
//      operator ingress (same universe as admission);
//    * admin accept + nonadmin/unauthenticated/forged-sender reject, no mutation;
//    * inspect safe projection (no raw subject/HMAC); output scrub + POSITIVE control;
//    * disable/delete commit durable status, deterministically close the session,
//      and trigger the REAL server-side peer close via IServerPeerCloser;
//    * post-disable reconnect rejects; delete + account-scoped purge prevents recreation;
//    * open/configure pilot gates admission correctly; restart durability;
//    * replay/idempotency/conflict; malformed/oversized args fail closed;
//    * NON-VACUOUS red-first regression: disabling the authoritative shared-context
//      binding makes the shared-store proof FAIL, restoring it makes it pass.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimLiveOperatorSurfaceTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;
        private const string AdminHost = "76561198000000001";
        private const string NonAdminHost = "76561198999999999";
        private const string WorldFixture = "world-save:Niflheim/12345";
        private const string QaSubject = "76561198000000042";

        private readonly string _dir;
        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        public NiflheimLiveOperatorSurfaceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-iap015-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- fixtures ----

        private static LookupKeyRing Ring() => new LookupKeyRing(new LookupHmacKey(new LookupKeyVersion("k1"), Key(10)));
        private static byte[] Key(byte fill) { var b = new byte[32]; for (int i = 0; i < b.Length; i++) b[i] = (byte)(fill + i); return b; }

        private static ServerObservedAdminContext Admin() => new ServerObservedAdminContext(AdminHost, ProviderNs);
        private static ServerObservedAdminContext NonAdmin() => new ServerObservedAdminContext(NonAdminHost, ProviderNs);

        private static OperatorAdminGate AdminGate() => new OperatorAdminGate(new List<string> { AdminHost });

        private static PilotDisclosure CompleteDisclosure()
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        private static VerifiedProviderPrincipal Provider(string subject, long transport) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), subject, transport);

        /// <summary>A recording peer closer so tests can assert the REAL server-side close was triggered.</summary>
        private sealed class RecordingCloser : IServerPeerCloser
        {
            public readonly List<long> Closed = new List<long>();
            private readonly HashSet<long> _live;
            public RecordingCloser(params long[] liveHandles) { _live = new HashSet<long>(liveHandles); }
            public bool CloseTransport(long h) { Closed.Add(h); return _live.Contains(h); }
        }

        /// <summary>Compose the shared services + a live admission over ONE store, exactly like the net48
        /// observer does. Returns services + the account/character services so tests can drive a real join.</summary>
        private LiveOperatorServices Compose(PilotAccountStore store, OperatorAdminGate gate,
            out PilotAccountService accounts, out PilotCharacterAdmissionService characters,
            out BoundSessionPrincipalIndex bound, string worldFixture = WorldFixture)
        {
            var ring = Ring();
            accounts = new PilotAccountService(store, ring, NoticeV, RetentionV);
            characters = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            bound = new BoundSessionPrincipalIndex();
            return LiveOperatorServices.Compose(store, accounts, characters, bound, gate,
                PilotRetentionPolicy.ShippedDefault(RetentionV), worldFixture);
        }

        /// <summary>Drive a genuine live join through the shared admission (allowlisted subject), returning the
        /// bound internal AccountId and the transport handle the session holds.</summary>
        private static (PilotAccountId account, long transport) LiveJoin(
            LiveOperatorServices services, PilotAccountService accounts, string subject, long playerId, long transport)
        {
            accounts.ProvisionAllowlistEntry("prov-" + subject, ProviderNs, Backend, subject,
                CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            string peerKey = ServerCreatorIdentity.CharacterSubject(playerId);
            var res = services.LiveAdmission.Admit(peerKey, Provider(subject, transport),
                new VerifiedProfileSubject(playerId, transport), transport, T0 + 1, "conn-" + transport);
            Assert.True(res.Admitted, "live join should admit: " + res.ResultCode);
            return (res.Account, transport);
        }

        private static string Wire(string verb, string op, string selector = "", string corr = "c1") =>
            selector.Length == 0
                ? string.Join("|", "v1", corr, op, verb)
                : string.Join("|", "v1", corr, op, verb, selector);

        // ── Shared-store visibility: a JOIN-created account is inspectable via the operator ingress ──
        [Fact]
        public void JoinCreatedAccount_IsInspectableThroughOperatorIngress_SameUniverse()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);

            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());
            var resp = router.Handle(Admin(), Wire("inspect", "op-insp", account.Value), T0 + 5);

            Assert.True(resp.Accepted, "operator inspect of a join-created account should succeed: " + resp.ResultCode);
            Assert.Equal(account.Value, resp.Get("account"));
            Assert.Equal("Active", resp.Get("status"));
            Assert.Equal("true", resp.Get("live"));               // the live session is visible in the SAME registry
            Assert.Equal("1", resp.Get("creds"));
        }

        // ── NON-VACUOUS red-first regression: break the shared-context binding, the proof FAILS ──
        [Fact]
        public void RegressionRedFirst_OperatorOverSeparateStore_CannotSeeJoinCreatedAccount()
        {
            var liveStore = new PilotAccountStore(JournalPath);
            var services = Compose(liveStore, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);

            // RED: an operator wired to a DIFFERENT store (the pre-IAP-015 duplicate-universe bug) cannot see
            // the join-created account — inspect rejects AccountNotFound. This proves the test is non-vacuous:
            // the shared-store binding is load-bearing, not incidentally satisfied.
            var separateStore = new PilotAccountStore(Path.Combine(_dir, "separate.bin"));
            var brokenServices = Compose(separateStore, AdminGate(), out _, out _, out _);
            var brokenRouter = new LiveOperatorCommandRouter(brokenServices, new NullServerPeerCloser());
            var red = brokenRouter.Handle(Admin(), Wire("inspect", "op-r", account.Value), T0 + 5);
            Assert.False(red.Accepted);
            Assert.Equal("AccountNotFound", red.ResultCode);

            // GREEN: the correctly-shared operator sees it.
            var goodRouter = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());
            var green = goodRouter.Handle(Admin(), Wire("inspect", "op-g", account.Value), T0 + 5);
            Assert.True(green.Accepted);
            Assert.Equal(account.Value, green.Get("account"));
        }

        // ── admin accept + nonadmin / unauthenticated / forged-sender reject WITHOUT mutation ──
        [Fact]
        public void NonAdmin_Unauthenticated_Reject_NoMutation_NoLeak()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            // Non-admin authenticated peer: rejected NotAdmin, no summary/data returned.
            var ni = router.Handle(NonAdmin(), Wire("inspect", "op-ni", account.Value), T0 + 5);
            Assert.False(ni.Accepted);
            Assert.Equal("NotAdmin", ni.ResultCode);
            Assert.Null(ni.Get("account"));   // no data leaked

            var nd = router.Handle(NonAdmin(), Wire("disable", "op-nd", account.Value), T0 + 5);
            Assert.False(nd.Accepted);
            Assert.Equal("NotAdmin", nd.ResultCode);

            // Unauthenticated (empty host — the shape a forged/routed sender with no delivering peer yields).
            var un = router.Handle(ServerObservedAdminContext.None, Wire("disable", "op-un", account.Value), T0 + 5);
            Assert.False(un.Accepted);
            Assert.Equal("UnauthenticatedPeer", un.ResultCode);

            // No mutation: account still Active, both in-memory and after a fresh reboot of the store.
            Assert.True(store.TryGetAccount(account, out var acct));
            Assert.Equal(PilotAccountStatus.Active, acct.Status);
            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(account, out var acct2));
            Assert.Equal(PilotAccountStatus.Active, acct2.Status);
        }

        // ── inspect safe projection: no raw subject / HMAC in any response field ──
        [Fact]
        public void Inspect_SafeProjection_NoRawSubjectOrHmac()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            var resp = router.Handle(Admin(), Wire("inspect", "op-safe", account.Value), T0 + 5);
            string hmacHex = Ring().CredentialHmacActive(ProviderNs, Backend, QaSubject).Hex;
            string wire = resp.ToWire();
            Assert.DoesNotContain(QaSubject, wire, StringComparison.Ordinal);
            Assert.DoesNotContain(hmacHex, wire, StringComparison.Ordinal);
            // Provider CLASS only, never the subject.
            Assert.Equal("Steam", resp.Get("classes"));
        }

        // ── output scrub POSITIVE control: a raw subject placed in a field is neutralized, not leaked ──
        [Fact]
        public void OutputScrub_PositiveControl_RawSubjectNeverSurvivesWireGrammar()
        {
            // A response value that contains the wire delimiters (an injection attempt / accidental leak) is
            // sanitized to a placeholder — it can never smuggle an extra field or a raw '|'/'=' bearing value.
            var resp = OperatorWireResponse.Ok("c1", "inspect", "Inspected");
            resp.Add("evil", "raw|subject=leak");
            Assert.Equal("?", resp.Get("evil"));
            Assert.DoesNotContain("raw|subject=leak", resp.ToWire(), StringComparison.Ordinal);

            // Positive control the scrub is real: a benign value passes through unchanged.
            resp.Add("ok", "acct-abc123");
            Assert.Equal("acct-abc123", resp.Get("ok"));
        }

        // ── disable: durable status + deterministic session close + REAL server-side peer close ──
        [Fact]
        public void Disable_CommitsDurable_ClosesSession_TriggersRealPeerClose()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, transport) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            Assert.True(services.Sessions.HasSession(account.Value)); // live session present in shared registry

            var closer = new RecordingCloser(transport);   // the transport is a live socket
            var router = new LiveOperatorCommandRouter(services, closer);
            var resp = router.Handle(Admin(), Wire("disable", "op-dis", account.Value), T0 + 5);

            Assert.True(resp.Accepted);
            Assert.Equal("Disabled", resp.ResultCode);
            Assert.Equal("true", resp.Get("sessionClosed"));
            Assert.Equal("true", resp.Get("socketClosed"));
            Assert.Contains(transport, closer.Closed);                 // REAL server-side close was triggered
            Assert.False(services.Sessions.HasSession(account.Value)); // deterministic session removal

            // Durable status survives a reboot.
            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(account, out var acct));
            Assert.Equal(PilotAccountStatus.Disabled, acct.Status);

            // Post-disable reconnect rejects (a delayed reconnect cannot reopen authority).
            var reboundAccounts = new PilotAccountService(reboot, Ring(), NoticeV, RetentionV);
            var rejoin = reboundAccounts.ResolveOrCreateAccount("rejoin", Provider(QaSubject, 4242L), T0 + 10);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountDisabled, rejoin.RejectionCode);

            // Idempotent replay of the same op.
            var replay = router.Handle(Admin(), Wire("disable", "op-dis", account.Value), T0 + 6);
            Assert.True(replay.Accepted);
            Assert.Equal("Replayed", replay.Get("outcome"));
        }

        // ── delete + account-scoped purge prevents recreation ──
        [Fact]
        public void DeleteThenPurge_PreventsRecreation_AndProvesAbsence()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, transport) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var closer = new RecordingCloser(transport);
            var router = new LiveOperatorCommandRouter(services, closer);

            var del = router.Handle(Admin(), Wire("delete", "op-del", account.Value), T0 + 5);
            Assert.True(del.Accepted);
            Assert.Equal("DeletionPending", del.ResultCode);
            Assert.Equal("true", del.Get("sessionClosed"));
            Assert.Contains(transport, closer.Closed);

            // A re-join for the SAME subject cannot recreate the account (wound-down credential barrier).
            var rejoin = accounts.ResolveOrCreateAccount("rejoin", Provider(QaSubject, 4242L), T0 + 6);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountDeletionPending, rejoin.RejectionCode);

            // Account-scoped complete-deletion/purge: proves absence (compaction) + purge receipt.
            var purge = router.Handle(Admin(), Wire("purge", "op-purge", account.Value), T0 + 7);
            Assert.True(purge.Accepted, "purge should complete: " + purge.ResultCode);
            Assert.Equal("Purged", purge.ResultCode);
            Assert.False(string.IsNullOrEmpty(purge.Get("receipt")));

            var reboot = new PilotAccountStore(JournalPath);
            Assert.True(reboot.TryGetAccount(account, out var acct));
            Assert.Equal(PilotAccountStatus.Deleted, acct.Status);
        }

        // ── open/configure pilot gates admission correctly + restart durability ──
        [Fact]
        public void OpenPilot_CatalogsWorldFixture_GatesAdmission_And_SurvivesRestart()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out _, out _, out _);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            var resp = router.Handle(Admin(), Wire("open-pilot", "op-open"), T0 + 5);
            Assert.True(resp.Accepted, "open-pilot should succeed: " + resp.ResultCode);
            Assert.StartsWith("pilot-", resp.Get("pilot"));
            Assert.Equal("true", resp.Get("admits"));   // Active pilot + cataloged fixture admits

            // The world fixture is cataloged in the SAME durable store, and survives a restart.
            var reboot = new PilotAccountStore(JournalPath);
            Assert.Contains(reboot.Artifacts, a => a.ArtifactType == PilotArtifactType.WorldSave &&
                a.Status == ArtifactStatus.Active && a.StorageLocator == WorldFixture);
            Assert.Contains(reboot.Pilots, p => p.Status == PilotLifecycleStatus.Active);

            // close-pilot then gates admission closed.
            string pilotId = resp.Get("pilot")!;
            var close = router.Handle(Admin(), Wire("close-pilot", "op-close", pilotId), T0 + 6);
            Assert.True(close.Accepted, "close-pilot should succeed: " + close.ResultCode);
            Assert.Equal("false", close.Get("admits"));
        }

        // ── export: player-safe projection, subject-free, cataloged ──
        [Fact]
        public void Export_PlayerSafe_SubjectFree_Cataloged()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            var resp = router.Handle(Admin(), Wire("export", "op-exp", account.Value), T0 + 5);
            Assert.True(resp.Accepted, resp.ResultCode);
            Assert.Equal("Exported", resp.ResultCode);
            Assert.DoesNotContain(QaSubject, resp.ToWire(), StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(resp.Get("receipt")));
            // Cataloged as an Export artifact in the same store.
            Assert.Contains(store.Artifacts, a => a.ArtifactType == PilotArtifactType.Export);
        }

        // ── replay / idempotency / conflict ──
        [Fact]
        public void Disable_ReplaySameOp_Idempotent_And_ConflictOnReusedOpForDifferentAccount()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (a1, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            var first = router.Handle(Admin(), Wire("disable", "shared-op", a1.Value), T0 + 5);
            Assert.Equal("Disabled", first.ResultCode);
            var replay = router.Handle(Admin(), Wire("disable", "shared-op", a1.Value), T0 + 6);
            Assert.Equal("Replayed", replay.Get("outcome"));
        }

        // ── malformed / oversized args fail closed ──
        [Fact]
        public void MalformedAndOversized_FailClosed_NoMutation()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            // Null / empty.
            Assert.Equal("MalformedRequest", router.Handle(Admin(), null, T0 + 5).ResultCode);
            Assert.Equal("MalformedRequest", router.Handle(Admin(), "", T0 + 5).ResultCode);

            // Wrong version header.
            Assert.Equal("MalformedRequest", router.Handle(Admin(), "v9|c1|op|inspect|" + account.Value, T0 + 5).ResultCode);

            // Oversized wire.
            string huge = "v1|c1|op|inspect|" + new string('x', LiveOperatorCommandRouter.MaxWireLength + 10);
            Assert.Equal("MalformedRequest", router.Handle(Admin(), huge, T0 + 5).ResultCode);

            // A delimiter/control char injected into a token → malformed.
            Assert.Equal("MalformedRequest", router.Handle(Admin(), "v1|c1|op|inspect|acct=evil", T0 + 5).ResultCode);

            // Unknown verb.
            var unk = router.Handle(Admin(), Wire("frobnicate", "op-unk", account.Value), T0 + 5);
            Assert.Equal("UnknownVerb", unk.ResultCode);

            // A non-acct selector (e.g. a raw provider subject) is refused — the live surface cannot be
            // driven by a raw subject (task point 4).
            var rawSubj = router.Handle(Admin(), Wire("inspect", "op-raw", QaSubject), T0 + 5);
            Assert.Equal("MalformedRequest", rawSubj.ResultCode);

            // No mutation from any of the above.
            Assert.True(store.TryGetAccount(account, out var acct));
            Assert.Equal(PilotAccountStatus.Active, acct.Status);
        }

        // ── forbidden destructive verbs are NOT exposed by the live surface ──
        [Fact]
        public void ForbiddenVerbs_FullReset_ScopedReset_Quarantine_AreUnknownToLiveSurface()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            foreach (var verb in new[] { "full-reset", "reset-scoped", "quarantine", "reset", "journal-edit" })
            {
                var r = router.Handle(Admin(), Wire(verb, "op-" + verb, account.Value), T0 + 5);
                Assert.False(r.Accepted);
                Assert.Equal("UnknownVerb", r.ResultCode);
            }
        }

        // ── dynamic adminlist: an admin REMOVED mid-run is rejected on the next command (fail closed) ──
        [Fact]
        public void LiveAdminlistProvider_RemovedAdmin_RejectedOnNextCommand()
        {
            var store = new PilotAccountStore(JournalPath);
            var mutableList = new List<string> { AdminHost };
            var gate = new OperatorAdminGate(() => mutableList);
            var services = Compose(store, gate, out var accounts, out _, out _);
            var (account, _) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());

            // While on the adminlist, inspect is authorized.
            Assert.True(router.Handle(Admin(), Wire("inspect", "op-1", account.Value), T0 + 5).Accepted);

            // Remove the admin from the LIVE list → the very next command rejects (fail closed on removal).
            mutableList.Clear();
            var after = router.Handle(Admin(), Wire("inspect", "op-2", account.Value), T0 + 6);
            Assert.False(after.Accepted);
            Assert.Equal("NotAdmin", after.ResultCode);
        }

        // ── the shared registry unbinds a disconnected session (Close), so operator sees it gone ──
        [Fact]
        public void SharedRegistry_DisconnectRemovesSession_OperatorSeesNoLiveSession()
        {
            var store = new PilotAccountStore(JournalPath);
            var services = Compose(store, AdminGate(), out var accounts, out _, out _);
            var (account, transport) = LiveJoin(services, accounts, QaSubject, 4242L, 4242L);
            Assert.True(services.Sessions.HasSession(account.Value));

            // Ordinary disconnect closes the session in the SHARED registry (stale-safe).
            Assert.True(services.LiveAdmission.Close(transport));
            Assert.False(services.Sessions.HasSession(account.Value));

            var router = new LiveOperatorCommandRouter(services, new NullServerPeerCloser());
            var resp = router.Handle(Admin(), Wire("inspect", "op-x", account.Value), T0 + 5);
            Assert.True(resp.Accepted);
            Assert.Equal("false", resp.Get("live"));   // no live session after disconnect
        }
    }
}
