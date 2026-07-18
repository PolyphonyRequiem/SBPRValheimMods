// ============================================================================
//  IAP-007W — Wire bound session principals into live admission.
// ----------------------------------------------------------------------------
//  Executable evidence that the account+character admission lifecycle (Tracer 1/2)
//  now PUBLISHES the bound INTERNAL gameplay principal into the live
//  BoundSessionPrincipalIndex on session activation and REMOVES it on close, and
//  that both the listen-host observer path and the dedicated-server ingress path
//  resolve a bound principal — while an unbound peer credits nothing. Exercises the
//  SHIPPED engine-free wiring (BoundSessionAdmission, LiveSessionAdmission,
//  BoundSessionPrincipalIndex.TryUnbind, DedicatedPlacementIngress fail-closed),
//  link-compiled from ../src, so the asserted behaviour IS the shipped behaviour.
//
//  Properties proven (task acceptance):
//    * listen + dedicated ingress can resolve a BOUND principal;
//    * session close REMOVES the bound principal (observer/ingress then fail closed);
//    * a STALE close cannot remove a NEWER bind under the same server-owned peer key;
//    * an UNBOUND peer cannot credit (dedicated ingress rejects UnboundPeer);
//    * the whole thing is driven from SERVER-OBSERVED facts (peer key = the durable
//      player:<s_playerID> character subject), never a client payload;
//    * fail-closed ordering: an un-allowlisted subject never binds.
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimBoundSessionWiringTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;

        private readonly string _dir;
        private readonly string _journalPath;

        public NiflheimBoundSessionWiringTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t007w-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _journalPath = Path.Combine(_dir, "account-journal.bin");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── engine-free admission fixtures ─────────────────────────────────────────

        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private static LookupKeyRing FixedRing() => new LookupKeyRing(FixedKey("k1", 10));

        private static VerifiedProviderPrincipal Provider(string subject) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), subject, transportHandle: 1L);

        private static PilotDisclosure CompleteDisclosure()
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var gameplay = new PrivacyInventoryCategory(
                "gameplay-progression", "run cooperative pilot", "while active", "operator", "none",
                "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat, gameplay }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        private static DisclosureAcknowledgement Ack() => new DisclosureAcknowledgement(NoticeV, T0);

        private sealed class Admission
        {
            public PilotAccountService Accounts { get; }
            public PilotCharacterAdmissionService Characters { get; }
            public BoundSessionPrincipalIndex BoundSessions { get; }
            public LiveSessionAdmission Live { get; }

            public Admission(string journalPath)
            {
                var ring = FixedRing();
                var store = new PilotAccountStore(journalPath);
                Accounts = new PilotAccountService(store, ring, NoticeV, RetentionV);
                Characters = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
                BoundSessions = new BoundSessionPrincipalIndex();
                Live = new LiveSessionAdmission(Accounts, Characters, BoundSessions);
            }
        }

        /// <summary>Allowlist + first-bind a subject so live admission can resolve/mint its account.</summary>
        private void Allow(Admission a, string subject) =>
            a.Accounts.ProvisionAllowlistEntry("prov-" + subject, ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);

        /// <summary>The server-owned peer key the gameplay path keys the bound index by: the durable
        /// player:&lt;s_playerID&gt; character subject.</summary>
        private static string PeerKey(long playerId) => ServerCreatorIdentity.CharacterSubject(playerId);

        // ── listen-host resolution: admission publishes; the index resolves the bound principal ──────

        [Fact]
        public void Admit_PublishesBoundInternalPrincipal_ResolvableByPeerKey()
        {
            var a = new Admission(_journalPath);
            Allow(a, "76561198000000001");

            long playerId = 5555L;
            var res = a.Live.Admit(PeerKey(playerId), Provider("76561198000000001"),
                new VerifiedProfileSubject(playerId, transportHandle: 100L), transportHandle: 100L, occurredAt: T0, opSeed: "conn-1");

            Assert.True(res.Admitted, "live admission should succeed: " + res.ResultCode);
            Assert.StartsWith("acct-", res.Account.Value);
            Assert.StartsWith("char-", res.Character.Value);

            // The gameplay path (observer/ingress) resolves the BOUND INTERNAL principal by the peer key.
            Assert.True(a.BoundSessions.TryResolve(PeerKey(playerId), out var bound));
            Assert.Equal(res.Account.Value, bound.Account.Value);
            Assert.Equal(res.Character.Value, bound.Character.Value);
            // The peer key is NOT a provider subject and NOT the internal id — it is player:<s_playerID>.
            Assert.Equal("player:5555", PeerKey(playerId));
            Assert.Equal(1, a.Live.LiveSessionCount);
        }

        // ── close removes the bound principal (gameplay then fails closed) ───────────

        [Fact]
        public void Close_RemovesBoundPrincipal_GameplayThenFailsClosed()
        {
            var a = new Admission(_journalPath);
            Allow(a, "76561198000000002");
            long playerId = 6001L;
            a.Live.Admit(PeerKey(playerId), Provider("76561198000000002"),
                new VerifiedProfileSubject(playerId, 200L), 200L, T0, "conn-1");
            Assert.True(a.BoundSessions.TryResolve(PeerKey(playerId), out _));

            bool removed = a.Live.Close(transportHandle: 200L);

            Assert.True(removed);
            Assert.False(a.BoundSessions.TryResolve(PeerKey(playerId), out _));
            Assert.Equal(0, a.Live.LiveSessionCount);
        }

        // ── stale close cannot remove a NEWER bind under the same peer key ───────────

        [Fact]
        public void StaleClose_CannotRemoveNewerBind_UnderSamePeerKey()
        {
            var a = new Admission(_journalPath);
            Allow(a, "76561198000000003");
            long playerId = 7002L;
            string peerKey = PeerKey(playerId);

            // Session 1 on transport 300 admits and binds.
            var s1 = a.Live.Admit(peerKey, Provider("76561198000000003"),
                new VerifiedProfileSubject(playerId, 300L), 300L, T0, "conn-1");
            Assert.True(s1.Admitted);

            // The peer disconnects — Close releases the lease AND unbinds session 1.
            Assert.True(a.Live.Close(transportHandle: 300L));
            Assert.False(a.BoundSessions.TryResolve(peerKey, out _));

            // It reconnects on a NEW transport 301, republishing a NEWER session under the SAME durable peer
            // key (the lease is now free).
            var s2 = a.Live.Admit(peerKey, Provider("76561198000000003"),
                new VerifiedProfileSubject(playerId, 301L), 301L, T0 + 1, "conn-2");
            Assert.True(s2.Admitted, "reconnect should re-admit: " + s2.ResultCode);
            Assert.True(a.BoundSessions.TryResolve(peerKey, out var afterReconnect));
            Assert.Equal(s2.Session.Value, afterReconnect.SessionId);

            // A DELAYED/duplicate close for the OLD session 1 (its session id no longer occupies the peer
            // key) arrives late. The session-qualified index unbind is a no-op — it must NOT tear down the
            // newer session 2's live bind. Exercise the coupler directly with session 1's identity.
            var coupler = new BoundSessionAdmission(a.Characters, a.BoundSessions);
            bool removedByStale = coupler.CloseAndUnbind(peerKey, s1.Account, s1.Session, transportHandle: 300L);
            Assert.False(removedByStale);
            Assert.True(a.BoundSessions.TryResolve(peerKey, out var stillBound));
            Assert.Equal(s2.Session.Value, stillBound.SessionId);
        }

        // ── fail-closed: an un-allowlisted subject never binds ──────────────────────

        [Fact]
        public void Admit_UnallowlistedSubject_FailsClosed_NoBind()
        {
            var a = new Admission(_journalPath);
            // No Allow(...) — the subject is not on the allowlist.
            long playerId = 8003L;
            var res = a.Live.Admit(PeerKey(playerId), Provider("76561198000000009"),
                new VerifiedProfileSubject(playerId, 400L), 400L, T0, "conn-1");

            Assert.False(res.Admitted);
            Assert.Equal(LiveAdmissionStage.Account, res.FailedStage);
            Assert.Equal("NotAllowlisted", res.ResultCode);
            Assert.False(a.BoundSessions.TryResolve(PeerKey(playerId), out _));
            Assert.Equal(0, a.Live.LiveSessionCount);
        }

        // ── one-session fence: a second concurrent session for the same account rejects ─

        [Fact]
        public void Admit_SecondConcurrentSessionSameAccount_RejectsAdmission_NoSecondBind()
        {
            var a = new Admission(_journalPath);
            Allow(a, "76561198000000004");
            string subject = "76561198000000004";

            var first = a.Live.Admit(PeerKey(9100L), Provider(subject),
                new VerifiedProfileSubject(9100L, 500L), 500L, T0, "conn-1");
            Assert.True(first.Admitted);

            // A different sibling profile (different s_playerID/peer key) of the SAME account connects
            // concurrently on a new transport — the admission lease fence rejects it.
            var second = a.Live.Admit(PeerKey(9200L), Provider(subject),
                new VerifiedProfileSubject(9200L, 501L), 501L, T0, "conn-2");
            Assert.False(second.Admitted);
            Assert.Equal(LiveAdmissionStage.Admission, second.FailedStage);
            Assert.False(a.BoundSessions.TryResolve(PeerKey(9200L), out _));
        }

        // ── dedicated ingress: a bound peer credits; an unbound peer cannot ─────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly string _key;
            public FixedFamilyResolver(StoneId stone) { _key = stone.Value; }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (string.Equals(stoneId.Value, _key, StringComparison.Ordinal))
                { family = "Settlement"; variant = "Homestead"; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class HomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal);
            }
        }

        private sealed class FakeInstanceSource : IServerPlacedInstanceSource
        {
            private readonly System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts>(StringComparer.Ordinal);
            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);
            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        [Fact]
        public void DedicatedIngress_BoundPeerCredits_UnboundPeerCannot()
        {
            var world = new WorldId("uid:t007w");
            var stone = StoneId.FromHostZone(world, 7, 3);
            const double sx = 100.0, sz = 100.0;

            var runtimeDir = Path.Combine(_dir, "runtime");
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = FoundationalProgressionServer.Create(
                runtimeDir, new FixedFamilyResolver(stone), new HomesteadBondPolicy(), stoneStore);
            server.StoneAreas.Register(stone, sx, sz, radius: 20.0);

            // One account admission stack over one journal, sharing the SERVER's bound-session index (the
            // net48 composition shape). Provision the allowlist on that same store, then admit live.
            var store = new PilotAccountStore(_journalPath);
            var accounts = new PilotAccountService(store, FixedRing(), NoticeV, RetentionV);
            accounts.ProvisionAllowlistEntry("prov-x", ProviderNs, Backend, "76561198000000006", CompleteDisclosure(), Ack(), T0);
            var live = new LiveSessionAdmission(accounts,
                new PilotCharacterAdmissionService(store, FixedRing(), new AccountAdmissionIndex()),
                server.BoundSessions);

            long playerId = 5555L;
            string peerKey = PeerKey(playerId);   // player:5555 — matches the placed ZDO s_creator below
            var admitted = live.Admit(peerKey, Provider("76561198000000006"),
                new VerifiedProfileSubject(playerId, 600L), 600L, T0, "conn-1");
            Assert.True(admitted.Admitted, "admission should succeed: " + admitted.ResultCode);

            // Seed + attune the INTERNAL character so a credited placement is authorized.
            var internalAccount = new AccountId(admitted.Account.Value);
            var internalCharacter = new CharacterId(admitted.Character.Value);
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(internalAccount, internalCharacter,
                    worldProductScope: "t007w/trailborne", revision: 0, bondSlots: 1, attunementSlots: 2,
                    lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(stone, 0, 0, 0, null, null) }));
            var att = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-attune"), RelationshipCommandType.CreateAttunement, stone,
                new AuthenticatedConnection(internalAccount.Value, internalCharacter.Value), default, "rel-att-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, att.Outcome);

            // The bound peer's placement credits (creator == peer key == player:5555).
            var source = new FakeInstanceSource();
            source.Put("100:1", "wood_floor", peerKey, sx, sz);
            var credited = server.CreateDedicatedIngress(source).Ingest(peerKey, "100:1");
            Assert.True(credited.Routed);
            Assert.Equal(RuntimePlacementDisposition.Earned, credited.Runtime.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(stone));

            // An UNBOUND peer (never admitted) cannot credit — the ingress fails closed BEFORE any ZDO check.
            string strangerKey = PeerKey(9999L);
            source.Put("100:2", "wood_floor", strangerKey, sx, sz);
            var stranger = server.CreateDedicatedIngress(source).Ingest(strangerKey, "100:2");
            Assert.False(stranger.Routed);
            Assert.Equal(DedicatedIngressRejection.UnboundPeer, stranger.Rejection);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(stone)); // unchanged — the stranger credited nothing

            // After the bound peer's session closes, its next notice also fails closed.
            Assert.True(live.Close(transportHandle: 600L));
            source.Put("100:3", "wood_floor", peerKey, sx, sz);
            var afterClose = server.CreateDedicatedIngress(source).Ingest(peerKey, "100:3");
            Assert.False(afterClose.Routed);
            Assert.Equal(DedicatedIngressRejection.UnboundPeer, afterClose.Rejection);
        }
    }
}
