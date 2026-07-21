// ============================================================================
//  T022 — QA-ONLY EPHEMERAL ACCOUNT BYPASS (isolated HomesteadT009L).
// ----------------------------------------------------------------------------
//  Executable evidence for the SHIPPED engine-free QA-bypass core (QaAccountBypass.cs,
//  link-compiled from ../src). TEST INFRASTRUCTURE under test — the whole point is that
//  it admits configured server-observed Steam peers under EPHEMERAL opaque identities on
//  the isolated t009l fixture WITHOUT any durable account/credential/journal mutation, and
//  that every gate off-path leaves normal `NotAllowlisted` admission unchanged.
//
//  Properties proven (task Verification list):
//    * disabled/default → gate refuses (Disabled); nothing composes;
//    * partial gate combinations refuse (tag / world / data-root / empty / wildcard);
//    * production names/roots/tags HARD-refuse (ProductionMarker);
//    * unsupported/anonymous/ambiguous transport principal refuses (unresolved provider);
//    * configured primary and valbot server-observed IDs → DISTINCT opaque principals;
//    * distinct profiles of one subject → distinct opaque characters;
//    * no durable journal store is ever touched (the admission takes no store);
//    * result/marker rendering carries NO raw subject;
//    * session duplicate (one-session fence) + stale-disconnect refusal;
//    * a fresh mint (restart) clears ephemeral mapping.
// ============================================================================

using System;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimQaAccountBypassTests
    {
        private const string Backend = "niflheim-pilot-app-896660";
        private const string Tag = "homestead-t009l";
        private const string World = "HomesteadT009L";
        private const string Root = "/srv/t009l/config/sbpr-niflheim-homestead/HomesteadT009L-42";
        private const string PrimaryId = "76561198000000001";
        private const string ValbotId = "76561198000000002";
        private const long T0 = 1_784_000_000L;

        private static VerifiedProviderPrincipal Provider(string subject) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), subject, transportHandle: 1L);

        private static QaAccountBypassConfig Config(
            bool enabled = true, string tag = Tag, string world = World, string root = Root,
            params string[] ids) =>
            new QaAccountBypassConfig(enabled, tag, world, root, ids.Length == 0 ? new[] { PrimaryId } : ids);

        private static QaIsolationFacts Facts(string world = World, string root = Root) =>
            new QaIsolationFacts(world, root);

        private static string PeerKey(long playerId) => ServerCreatorIdentity.CharacterSubject(playerId);

        private static QaAccountBypassAdmission FreshAdmission(params string[] ids) =>
            new QaAccountBypassAdmission(
                new QaEphemeralIdentityMint(), new AccountAdmissionIndex(), new BoundSessionPrincipalIndex(),
                ids.Length == 0 ? new[] { PrimaryId, ValbotId } : ids);

        // ── GATE: default OFF ───────────────────────────────────────────────────────

        [Fact]
        public void Gate_DisabledByDefault_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(enabled: false, ids: PrimaryId), Facts());
            Assert.Equal(QaBypassGateRejection.Disabled, rej);
        }

        [Fact]
        public void Gate_NullConfig_Refuses()
        {
            Assert.Equal(QaBypassGateRejection.Disabled, QaAccountBypassGate.Evaluate(null!, Facts()));
        }

        [Fact]
        public void Gate_AllGatesMatch_Passes()
        {
            Assert.Equal(QaBypassGateRejection.None,
                QaAccountBypassGate.Evaluate(Config(ids: PrimaryId), Facts()));
        }

        // ── GATE: partial combinations refuse ───────────────────────────────────────

        [Fact]
        public void Gate_WrongEnvironmentTag_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(tag: "homestead-prod", ids: PrimaryId), Facts());
            Assert.Equal(QaBypassGateRejection.EnvironmentTagMismatch, rej);
        }

        [Fact]
        public void Gate_EmptyEnvironmentTag_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(tag: "", ids: PrimaryId), Facts());
            Assert.Equal(QaBypassGateRejection.EnvironmentTagMismatch, rej);
        }

        [Fact]
        public void Gate_WorldNameMismatch_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(ids: PrimaryId), Facts(world: "SomeOtherWorld"));
            Assert.Equal(QaBypassGateRejection.WorldNameMismatch, rej);
        }

        [Fact]
        public void Gate_EmptyExpectedWorld_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(world: "", ids: PrimaryId), Facts(world: ""));
            Assert.Equal(QaBypassGateRejection.WorldNameMismatch, rej);
        }

        [Fact]
        public void Gate_DataRootMismatch_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(ids: PrimaryId), Facts(root: "/srv/other/config"));
            Assert.Equal(QaBypassGateRejection.DataRootMismatch, rej);
        }

        [Fact]
        public void Gate_EmptyExpectedDataRoot_Refuses()
        {
            var rej = QaAccountBypassGate.Evaluate(Config(root: "", ids: PrimaryId), Facts(root: ""));
            Assert.Equal(QaBypassGateRejection.DataRootMismatch, rej);
        }

        [Fact]
        public void Gate_EmptyAllowlist_Refuses()
        {
            // An all-empty id list trims to an empty set.
            var cfg = new QaAccountBypassConfig(true, Tag, World, Root, new[] { "" });
            Assert.Equal(QaBypassGateRejection.EmptyAllowlist, QaAccountBypassGate.Evaluate(cfg, Facts()));
        }

        [Fact]
        public void Gate_WildcardAllowlist_Refuses()
        {
            var cfg = new QaAccountBypassConfig(true, Tag, World, Root, new[] { PrimaryId, "*" });
            Assert.Equal(QaBypassGateRejection.WildcardAllowlist, QaAccountBypassGate.Evaluate(cfg, Facts()));
        }

        [Fact]
        public void Gate_NonNumericAllowlistId_Refuses()
        {
            var cfg = new QaAccountBypassConfig(true, Tag, World, Root, new[] { "not-a-steam-id" });
            Assert.Equal(QaBypassGateRejection.WildcardAllowlist, QaAccountBypassGate.Evaluate(cfg, Facts()));
        }

        [Fact]
        public void Gate_AnonymousZeroId_Refuses()
        {
            var cfg = new QaAccountBypassConfig(true, Tag, World, Root, new[] { "0" });
            Assert.Equal(QaBypassGateRejection.WildcardAllowlist, QaAccountBypassGate.Evaluate(cfg, Facts()));
        }

        // ── GATE: production hard-refuse ────────────────────────────────────────────

        [Theory]
        [InlineData("Niflheim", Root)]          // production world name (configured + observed)
        [InlineData("Heistan", Root)]           // production world (second marker)
        [InlineData("niflheim-test", Root)]     // marker as a substring, case-insensitive
        public void Gate_ProductionMarker_HardRefuses(string world, string root)
        {
            var cfg = new QaAccountBypassConfig(true, Tag, world, root, new[] { PrimaryId });
            var rej = QaAccountBypassGate.Evaluate(cfg, new QaIsolationFacts(world, root));
            Assert.Equal(QaBypassGateRejection.ProductionMarker, rej);
        }

        [Fact]
        public void Gate_ProductionMarkerInObservedFactsOnly_HardRefuses()
        {
            // Config confined to t009l but the server actually loaded a production world — hard refuse.
            var cfg = Config(ids: PrimaryId);
            var rej = QaAccountBypassGate.Evaluate(cfg, new QaIsolationFacts("Niflheim", Root));
            Assert.Equal(QaBypassGateRejection.ProductionMarker, rej);
        }

        // ── ADMISSION: allowlist + ephemeral opaque identity ────────────────────────

        [Fact]
        public void Admit_ConfiguredSubject_ProducesEphemeralOpaquePrincipal()
        {
            var a = FreshAdmission(PrimaryId);
            long playerId = 5555L;
            var res = a.Admit(PeerKey(playerId), Provider(PrimaryId),
                new VerifiedProfileSubject(playerId, 100L), 100L, T0);

            Assert.True(res.Admitted, res.ResultCode);
            Assert.StartsWith("acct-", res.Account.Value);
            Assert.StartsWith("char-", res.Character.Value);
            Assert.StartsWith("sess-", res.Session.Value);
            Assert.Equal(1, a.LiveSessionCount);
        }

        [Fact]
        public void Admit_UnconfiguredSubject_RefusesNotAllowlisted_NoBind()
        {
            var a = FreshAdmission(PrimaryId);  // only PrimaryId allowlisted
            long playerId = 8003L;
            var res = a.Admit(PeerKey(playerId), Provider("76561198000000099"),
                new VerifiedProfileSubject(playerId, 400L), 400L, T0);

            Assert.False(res.Admitted);
            Assert.Equal(QaBypassStage.NotAllowlisted, res.FailedStage);
            Assert.Equal("NotAllowlisted", res.ResultCode);
            Assert.Equal(0, a.LiveSessionCount);
        }

        [Fact]
        public void Admit_UnresolvedProviderPrincipal_Refuses()
        {
            var a = FreshAdmission(PrimaryId);
            // An unresolved (anonymous/unsupported) provider principal — default struct is unresolved.
            var res = a.Admit(PeerKey(1L), default, new VerifiedProfileSubject(1L, 1L), 1L, T0);
            Assert.False(res.Admitted);
            Assert.Equal(QaBypassStage.Provider, res.FailedStage);
        }

        [Fact]
        public void Admit_UnresolvedProfile_Refuses()
        {
            var a = FreshAdmission(PrimaryId);
            // Zero s_playerID is not a real profile fact.
            var res = a.Admit(PeerKey(1L), Provider(PrimaryId), new VerifiedProfileSubject(0L, 1L), 1L, T0);
            Assert.False(res.Admitted);
            Assert.Equal(QaBypassStage.Profile, res.FailedStage);
        }

        [Fact]
        public void Admit_DistinctSubjects_GetDistinctOpaqueAccounts()
        {
            var a = FreshAdmission(PrimaryId, ValbotId);
            var primary = a.Admit(PeerKey(1000L), Provider(PrimaryId), new VerifiedProfileSubject(1000L, 10L), 10L, T0);
            var valbot = a.Admit(PeerKey(2000L), Provider(ValbotId), new VerifiedProfileSubject(2000L, 20L), 20L, T0);

            Assert.True(primary.Admitted);
            Assert.True(valbot.Admitted);
            Assert.NotEqual(primary.Account.Value, valbot.Account.Value);
            Assert.NotEqual(primary.Character.Value, valbot.Character.Value);
        }

        [Fact]
        public void Admit_SameSubjectDistinctProfiles_GetDistinctCharacters_SameAccount()
        {
            var a = FreshAdmission(PrimaryId);
            // Same Steam subject, two different profiles across two (sequential) sessions.
            var s1 = a.Admit(PeerKey(1000L), Provider(PrimaryId), new VerifiedProfileSubject(1000L, 10L), 10L, T0);
            Assert.True(a.Close(10L));
            var s2 = a.Admit(PeerKey(1001L), Provider(PrimaryId), new VerifiedProfileSubject(1001L, 11L), 11L, T0 + 1);

            Assert.True(s1.Admitted);
            Assert.True(s2.Admitted);
            Assert.Equal(s1.Account.Value, s2.Account.Value);         // same subject → same opaque account
            Assert.NotEqual(s1.Character.Value, s2.Character.Value);  // distinct profiles → distinct characters
        }

        [Fact]
        public void Admit_SameSubjectSameProfile_ReconnectResolvesSameEphemeralIds()
        {
            var a = FreshAdmission(PrimaryId);
            var s1 = a.Admit(PeerKey(1000L), Provider(PrimaryId), new VerifiedProfileSubject(1000L, 10L), 10L, T0);
            Assert.True(a.Close(10L));
            var s2 = a.Admit(PeerKey(1000L), Provider(PrimaryId), new VerifiedProfileSubject(1000L, 11L), 11L, T0 + 1);

            Assert.Equal(s1.Account.Value, s2.Account.Value);
            Assert.Equal(s1.Character.Value, s2.Character.Value);
            Assert.NotEqual(s1.Session.Value, s2.Session.Value);   // but a fresh session id each connection
        }

        // ── BOUND INDEX: gameplay path resolves the ephemeral principal ─────────────

        [Fact]
        public void Admit_PublishesEphemeralPrincipalIntoBoundIndex_ClosedRemovesIt()
        {
            var mint = new QaEphemeralIdentityMint();
            var bound = new BoundSessionPrincipalIndex();
            var a = new QaAccountBypassAdmission(mint, new AccountAdmissionIndex(), bound, new[] { PrimaryId });

            long playerId = 5555L;
            var res = a.Admit(PeerKey(playerId), Provider(PrimaryId),
                new VerifiedProfileSubject(playerId, 600L), 600L, T0);
            Assert.True(res.Admitted);

            Assert.True(bound.TryResolve(PeerKey(playerId), out var principal));
            Assert.Equal(res.Account.Value, principal.Account.Value);
            Assert.Equal(res.Character.Value, principal.Character.Value);

            Assert.True(a.Close(600L));
            Assert.False(bound.TryResolve(PeerKey(playerId), out _));
            Assert.Equal(0, a.LiveSessionCount);
        }

        // ── SESSION FENCE: one active/pending session per ephemeral account ─────────

        [Fact]
        public void Admit_SecondConcurrentSessionSameSubject_RejectsAdmission_NoSecondBind()
        {
            var bound = new BoundSessionPrincipalIndex();
            var a = new QaAccountBypassAdmission(
                new QaEphemeralIdentityMint(), new AccountAdmissionIndex(), bound, new[] { PrimaryId });

            var first = a.Admit(PeerKey(9100L), Provider(PrimaryId), new VerifiedProfileSubject(9100L, 500L), 500L, T0);
            Assert.True(first.Admitted);

            // Same subject, different profile/transport connects concurrently — the lease fence rejects.
            var second = a.Admit(PeerKey(9200L), Provider(PrimaryId), new VerifiedProfileSubject(9200L, 501L), 501L, T0);
            Assert.False(second.Admitted);
            Assert.Equal(QaBypassStage.Admission, second.FailedStage);
            Assert.Equal("AccountAlreadyConnected", second.ResultCode);
            Assert.False(bound.TryResolve(PeerKey(9200L), out _));
        }

        // ── STALE DISCONNECT: cannot close a newer session under same peer key ──────

        [Fact]
        public void StaleClose_CannotRemoveNewerBind()
        {
            var bound = new BoundSessionPrincipalIndex();
            var a = new QaAccountBypassAdmission(
                new QaEphemeralIdentityMint(), new AccountAdmissionIndex(), bound, new[] { PrimaryId });
            long playerId = 7002L;
            string peerKey = PeerKey(playerId);

            var s1 = a.Admit(peerKey, Provider(PrimaryId), new VerifiedProfileSubject(playerId, 300L), 300L, T0);
            Assert.True(s1.Admitted);
            Assert.True(a.Close(300L));   // disconnect session 1

            var s2 = a.Admit(peerKey, Provider(PrimaryId), new VerifiedProfileSubject(playerId, 301L), 301L, T0 + 1);
            Assert.True(s2.Admitted);
            Assert.True(bound.TryResolve(peerKey, out var afterReconnect));
            Assert.Equal(s2.Session.Value, afterReconnect.SessionId);

            // A delayed close for the OLD transport 300 arrives late — it must NOT tear down session 2.
            Assert.False(a.Close(300L));   // transport 300 no longer tracked → no-op
            Assert.True(bound.TryResolve(peerKey, out var stillBound));
            Assert.Equal(s2.Session.Value, stillBound.SessionId);
        }

        // ── RESTART: a fresh mint/admission clears the ephemeral mapping ────────────

        [Fact]
        public void FreshMintAfterRestart_ClearsEphemeralMapping()
        {
            var mint1 = new QaEphemeralIdentityMint();
            var a1 = new QaAccountBypassAdmission(mint1, new AccountAdmissionIndex(), new BoundSessionPrincipalIndex(), new[] { PrimaryId });
            var before = a1.Admit(PeerKey(1L), Provider(PrimaryId), new VerifiedProfileSubject(1L, 1L), 1L, T0);
            Assert.True(before.Admitted);

            // Simulate restart: a brand-new mint + admission. Same subject now mints a DIFFERENT opaque id.
            var mint2 = new QaEphemeralIdentityMint();
            var a2 = new QaAccountBypassAdmission(mint2, new AccountAdmissionIndex(), new BoundSessionPrincipalIndex(), new[] { PrimaryId });
            var after = a2.Admit(PeerKey(1L), Provider(PrimaryId), new VerifiedProfileSubject(1L, 1L), 1L, T0);
            Assert.True(after.Admitted);
            Assert.NotEqual(before.Account.Value, after.Account.Value);
            Assert.NotEqual(before.Character.Value, after.Character.Value);
        }

        // ── NO RAW SUBJECT in marker/result rendering ───────────────────────────────

        [Fact]
        public void OperatorLine_CarriesNoRawSubject()
        {
            var a = FreshAdmission(PrimaryId);
            long playerId = 5555L;
            var ok = a.Admit(PeerKey(playerId), Provider(PrimaryId), new VerifiedProfileSubject(playerId, 100L), 100L, T0);
            string okLine = ok.ToOperatorLine();
            Assert.StartsWith("[qa-account-bypass] admitted", okLine);
            Assert.DoesNotContain(PrimaryId, okLine);

            var rej = a.Admit(PeerKey(6L), Provider("76561198000000077"), new VerifiedProfileSubject(6L, 6L), 6L, T0);
            string rejLine = rej.ToOperatorLine();
            Assert.StartsWith("[qa-account-bypass] rejected", rejLine);
            Assert.DoesNotContain("76561198000000077", rejLine);
        }

        // ── Canonical-subject helper edge cases ────────────────────────────────────

        [Theory]
        [InlineData("76561198000000001", true)]
        [InlineData("0", false)]
        [InlineData("*", false)]
        [InlineData("", false)]
        [InlineData("123abc", false)]
        [InlineData("  ", false)]
        public void IsCanonicalSteamSubject_Cases(string id, bool expected)
        {
            Assert.Equal(expected, QaAccountBypassGate.IsCanonicalSteamSubject(id));
        }
    }
}
