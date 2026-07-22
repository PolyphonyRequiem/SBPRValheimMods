// ADR-0009 M2R — AT-QA-SERVER-NO-LISTENER + peer-substitution + stale-generation +
// admin-recheck tests for the server-role responder. Proves the server owns NO host
// listener (no socket field), binds the ACTUAL delivering peer, rejects a substituted
// peer and a stale post-reconnect generation, and re-checks admin authority at execution.
//
// M2R repair (t_48ec6fdb): the claimed connection generation is decoded from the
// authenticated envelope (part of the HMAC input), NOT a caller argument. These tests
// drive the real codec→responder path: current generation accepted; stale pre-reconnect
// generation rejected StaleGeneration; missing/nonpositive generation malformed; a
// generation tampered without recomputing the HMAC rejected; a recomputed stale
// generation still rejected (peer/gen binding is post-HMAC transport state).
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
            // The envelope carries the CURRENT authenticated generation.
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
            // Receipts stamp the server's current generation so the runner can form its next request.
            Assert.Equal(gen, receipt.Generation);
        }

        [Fact]
        public void PeerSubstitution_Rejected()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen = r.OnPeerConnected("peerA");
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen);
            // A different delivering peer than the bound one — substitution.
            var receipt = r.Handle("peerB", payload, Now);
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
            // Envelope authentically signs the OLD generation — a pre-reconnect stale replay.
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen1);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("StaleGeneration", receipt.Reason);
            // The receipt still exposes the CURRENT generation so the runner can recover.
            Assert.Equal(gen2, receipt.Generation);
        }

        [Fact]
        public void ReconnectAdvancesGeneration_NewGenerationAccepted()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            r.OnPeerConnected("peerA");
            r.OnPeerDisconnected();
            long gen2 = r.OnPeerConnected("peerA");
            // A request signed with the NEW generation is admitted (reconnect recovery path).
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen2);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
        }

        [Fact]
        public void MissingGeneration_Malformed()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen = r.OnPeerConnected("peerA");
            // No connectionGeneration field on the wire — fail-closed decode.
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen, omitGeneration: true);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("MalformedFrame", receipt.Reason);
        }

        [Fact]
        public void NonPositiveGeneration_Malformed()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            r.OnPeerConnected("peerA");
            // Zero/negative generation is not a valid wire value.
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: 0);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("MalformedFrame", receipt.Reason);
        }

        [Fact]
        public void GenerationTamperedWithoutResigning_RejectedBadHmac()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen = r.OnPeerConnected("peerA");
            // Wire generation matches the bound one (so peer/gen binding passes), but the HMAC was
            // signed over a DIFFERENT generation — admission's HMAC check must reject it.
            string payload = WireFixtures.SignedPayload(
                armed, "Ping", 1, "r1", connectionGeneration: gen, signedGenerationOverride: gen + 5);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal("BadHmac", receipt.Reason);
        }

        [Fact]
        public void RecomputedStaleGeneration_StillRejected()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(true));
            long gen1 = r.OnPeerConnected("peerA");
            r.OnPeerDisconnected();
            r.OnPeerConnected("peerA"); // gen2
            // Attacker recomputes a VALID HMAC over the stale generation (envelope authenticates),
            // but the transport peer/gen binding still rejects it — auth alone does not grant currency.
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen1);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("StaleGeneration", receipt.Reason);
        }

        [Fact]
        public void AdminRecheckFails_Rejected_EvenWithValidEnvelope()
        {
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var r = new ServerRpcResponder(armed, new FakeServerAuthorityRecheck(authorized: false));
            long gen = r.OnPeerConnected("peerA");
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: gen);
            var receipt = r.Handle("peerA", payload, Now);
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("admin-recheck-failed", receipt.Status);
        }

        [Fact]
        public void UnboundPeer_Rejected()
        {
            var r = MakeResponder();
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", connectionGeneration: 1);
            var receipt = r.Handle("peerA", payload, Now); // nothing bound yet
            Assert.Equal(ControlOutcome.TransportRejected, receipt.Outcome);
            Assert.Equal("PeerUnbound", receipt.Reason);
        }
    }
}
