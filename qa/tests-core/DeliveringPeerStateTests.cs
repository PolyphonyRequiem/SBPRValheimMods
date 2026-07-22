// ADR-0009 M2 — delivering-peer / connection-generation state tests.
// Covers AT-QA-SERVER-NO-LISTENER / AT-QA-PEER-SUBSTITUTION-REJECT (state half):
// nothing bound => PeerUnbound; delivering peer != bound => PeerUnbound (substitution);
// stale generation after rebind => StaleGeneration; and the inert fixture-seam fakes
// that let the owned-resource ledger be exercised headlessly.
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

    public class GameBindingAdapterFakeTests
    {
        [Fact]
        public void FakeFixtureSeam_TracksAndDespawns()
        {
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench", "Wood" });
            Assert.True(seam.PrefabExists("piece_workbench"));
            Assert.False(seam.PrefabExists("unknown_prefab"));

            string st = seam.SpawnPrefab("piece_workbench", 2.0);
            string it = seam.GrantItem("Wood", 5);
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
