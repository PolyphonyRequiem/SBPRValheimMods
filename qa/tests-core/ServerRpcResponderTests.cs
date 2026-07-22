// ADR-0009 M2R — AT-QA-SERVER-NO-LISTENER + peer-substitution + stale-generation +
// admin-recheck tests for the server-role responder. Proves the server owns NO host
// listener (no socket field), binds the ACTUAL delivering peer, rejects a substituted
// peer and a stale post-reconnect generation, and re-checks admin authority at execution.
using System.Linq;
using System.Reflection;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ServerRpcResponderTests
    {
        private const long Now = Fixtures.Now;

        private static ServerRpcResponder MakeResponder(bool authorized = true)
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping", "SpawnStation" });
            return new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(authorized));
        }

        [Fact]
        public void OwnsNoSocketOrListenerField()
        {
            // The dedicated server exposes NO host listener (ADR-0009 §2). The responder must not
            // hold a TcpListener / Socket / TcpClient — server verbs arrive over per-peer ZRpc only.
            var forbidden = new[] { "TcpListener", "Socket", "TcpClient", "LoopbackControlServer" };
            var fields = typeof(ServerRpcResponder).GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var f in fields)
                Assert.DoesNotContain(f.FieldType.Name, forbidden);
        }

        [Fact]
        public void BindsDeliveringPeer_ThenAdmitsValidPing()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen = r.OnPeerConnected("peerA");
            Assert.Equal(1, gen);
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            var receipt = r.Handle("peerA", gen, payload, Now);
            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
        }

        [Fact]
        public void PeerSubstitution_Rejected()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen = r.OnPeerConnected("peerA");
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            // A different delivering peer than the bound one — substitution.
            var receipt = r.Handle("peerB", gen, payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("PeerUnbound", receipt.Reason);
        }

        [Fact]
        public void StaleGeneration_Rejected()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen1 = r.OnPeerConnected("peerA");
            r.OnPeerDisconnected();
            long gen2 = r.OnPeerConnected("peerA"); // reconnect bumps generation
            Assert.True(gen2 > gen1);
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            // Envelope claims the OLD generation — stale.
            var receipt = r.Handle("peerA", gen1, payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("StaleGeneration", receipt.Reason);
        }

        [Fact]
        public void AdminRecheckFails_Rejected_EvenWithValidEnvelope()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(authorized: false));
            long gen = r.OnPeerConnected("peerA");
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            var receipt = r.Handle("peerA", gen, payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("admin-recheck-failed", receipt.Status);
        }

        [Fact]
        public void UnboundPeer_Rejected()
        {
            var r = MakeResponder();
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            var receipt = r.Handle("peerA", 1, payload, Now); // nothing bound yet
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("PeerUnbound", receipt.Reason);
        }
    }
}
