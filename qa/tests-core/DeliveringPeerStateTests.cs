// ADR-0009 M2 — delivering-peer / connection-generation state tests.
// Covers AT-QA-SERVER-NO-LISTENER / AT-QA-PEER-SUBSTITUTION-REJECT (state half):
// nothing bound => PeerUnbound; delivering peer != bound => PeerUnbound (substitution);
// stale generation after rebind => StaleGeneration; and the inert fixture-seam fakes
// that let the owned-resource ledger be exercised headlessly.
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class DeliveringPeerStateTests
    {
        [Fact]
        public void NothingBound_Unbound()
        {
            var s = new DeliveringPeerState();
            Assert.Equal(0, s.Generation);
            Assert.Equal(ControlPlaneReason.PeerUnbound, s.Validate("peerA", 1).Reason);
        }

        [Fact]
        public void Bind_FirstGenerationIsOne_ValidatesCurrent()
        {
            var s = new DeliveringPeerState();
            var b = s.Bind("peerA");
            Assert.Equal(1, b.Generation);
            Assert.Equal("peerA", b.PeerId);
            var v = s.Validate("peerA", 1);
            Assert.True(v.Ok);
            Assert.Equal("peerA", v.Bound!.PeerId);
        }

        [Fact]
        public void PeerSubstitution_Rejected()
        {
            var s = new DeliveringPeerState();
            s.Bind("peerA");
            // Request delivered by a DIFFERENT actual peer than the bound one.
            Assert.Equal(ControlPlaneReason.PeerUnbound, s.Validate("peerB", 1).Reason);
        }

        [Fact]
        public void ClaimedIdentityIgnored_OnlyDeliveringPeerTrusted()
        {
            var s = new DeliveringPeerState();
            s.Bind("peerA");
            // The delivering peer is authoritative; there is no way to pass a "claimed" id
            // that overrides it — Validate only takes the actual delivering peer.
            Assert.True(s.Validate("peerA", 1).Ok);
        }

        [Fact]
        public void StaleGeneration_AfterRebind_Rejected()
        {
            var s = new DeliveringPeerState();
            s.Bind("peerA");                 // gen 1
            s.Bind("peerA");                 // reconnect: gen 2
            Assert.Equal(2, s.Generation);
            // A request captured on gen 1 replayed after reconnect is stale.
            Assert.Equal(ControlPlaneReason.StaleGeneration, s.Validate("peerA", 1).Reason);
            Assert.True(s.Validate("peerA", 2).Ok);
        }

        [Fact]
        public void Unbind_ThenValidate_Unbound()
        {
            var s = new DeliveringPeerState();
            s.Bind("peerA");
            s.Unbind();
            Assert.Equal(ControlPlaneReason.PeerUnbound, s.Validate("peerA", 1).Reason);
            // Unbind does not advance the generation; the next bind takes gen 2.
            Assert.Equal(2, s.Bind("peerA").Generation);
        }

        [Fact]
        public void FutureGenerationClaim_Rejected()
        {
            var s = new DeliveringPeerState();
            s.Bind("peerA"); // gen 1
            Assert.Equal(ControlPlaneReason.StaleGeneration, s.Validate("peerA", 99).Reason);
        }
    }

    /// <summary>
    /// AT-QA-REMOTE-FIXTURE-REJECT — a fixture verb (Server role) may only execute when it
    /// arrives over the authenticated per-peer ZRpc seam bound to the ACTUAL owner peer.
    /// This proves the two engine-free rejections that compose that guarantee:
    ///   (1) channel/role — every fixture verb is ServerRpc-only and is refused for the
    ///       Client (loopback) role, so a fixture request can never ride the client channel; and
    ///   (2) delivering-peer binding — a fixture request delivered by any peer other than the
    ///       bound owner (or with nothing bound) is refused PeerUnbound. Together: no remote
    ///       client can drive a fixture. The live ZRpc wiring is a later slice; the decision is here.
    /// </summary>
    public class RemoteFixtureRejectTests
    {
        // The fixture verb family (ADR-0009 §3.1) — all Server-role, per-peer ZRpc only.
        public static readonly object[][] FixtureVerbs =
        {
            new object[] { "SpawnStation" },
            new object[] { "GrantVanillaMaterials" },
            new object[] { "PlaceVanillaPiece" },
            new object[] { "SetWorldTime" },
        };

        [Theory]
        [MemberData(nameof(FixtureVerbs))]
        public void FixtureVerb_IsServerRpcOnly_RejectedForClientRole(string verbName)
        {
            var verb = VerbCatalog.Get(verbName);
            Assert.NotNull(verb);
            Assert.Equal(VerbChannel.ServerRpc, verb!.Channel);
            // A fixture verb offered on the client (loopback) role is refused — it can never
            // ride the owner-local loopback channel, only authenticated per-peer server ZRpc.
            Assert.False(verb.AllowsRole(HarnessRole.Client));
            Assert.True(verb.AllowsRole(HarnessRole.Server));
        }

        [Fact]
        public void FixtureFromRemotePeer_Rejected()
        {
            // Owner peer bound on the server control context.
            var s = new DeliveringPeerState();
            s.Bind("owner-peer");
            // A fixture request delivered by a DIFFERENT (remote) peer is refused, even though
            // it claims the current generation — only the delivering peer is trusted.
            Assert.Equal(ControlPlaneReason.PeerUnbound, s.Validate("remote-peer", 1).Reason);
        }

        [Fact]
        public void FixtureWithNoBoundPeer_Rejected()
        {
            // No peer bound yet: a fixture request cannot be admitted at all.
            var s = new DeliveringPeerState();
            Assert.Equal(ControlPlaneReason.PeerUnbound, s.Validate("any-peer", 1).Reason);
        }
    }

    public class GameBindingAdapterFakeTests
    {
        [Fact]
        public void FakeFixtureSeam_TracksAndDespawns()
        {
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench", "Wood" });
            Assert.True(seam.PrefabExists("piece_workbench"));
            Assert.False(seam.PrefabExists("unknown_prefab"));

            string st = seam.SpawnPrefab("piece_workbench", 2.0, "m1");
            string it = seam.GrantItem("Wood", 5, "m2");
            Assert.Equal(2, seam.Live.Count);

            Assert.True(seam.Despawn(st));
            Assert.True(seam.Despawn(it));
            Assert.Empty(seam.Live);
            Assert.False(seam.Despawn(st)); // idempotent: already gone
        }

        [Fact]
        public void FakeScheduler_DrainsPostedActions_InOrder()
        {
            var sched = new FakeMainThreadScheduler { NowUnixMs = 100 };
            var log = new System.Collections.Generic.List<int>();
            sched.Post(() => log.Add(1));
            sched.Post(() => log.Add(2));
            Assert.Equal(2, sched.Drain());
            Assert.Equal(new[] { 1, 2 }, log);
            sched.Advance(50);
            Assert.Equal(150, sched.NowUnixMs);
        }

        [Fact]
        public void FakeWorldIdentity_ReportsFacts()
        {
            var w = new FakeWorldIdentitySource { WorldLoaded = true, WorldUid = 9001, WorldName = "sbpr-qa", IsServer = true };
            Assert.True(w.WorldLoaded);
            Assert.Equal(9001, w.WorldUid);
            Assert.Equal("sbpr-qa", w.WorldName);
            Assert.True(w.IsServer);
        }
    }
}
