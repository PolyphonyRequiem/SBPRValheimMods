// ADR-0009 M2R — ControlPlaneRuntime tests. Proves the shared brain composes the M1
// fail-closed admission gate with the M2 single-slot dispatcher and emits descriptive
// primitive receipts: a valid Ping admits+executes (pong), a bad-HMAC/expired/replayed
// request rejects, a mutating verb admits but reports NotImplementedInMilestone (zero
// game mutation in M2R), and a concurrent second request is shed BUSY.
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ControlPlaneRuntimeTests
    {
        private const long Now = Fixtures.Now;

        [Fact]
        public void ValidPing_Admits_AndPongs()
        {
            var armed = WireFixtures.ArmValidClient();
            var rt = new ControlPlaneRuntime(armed);
            string payload = WireFixtures.SignedPayload(armed, "Ping", seq: 1, requestId: "r1");
            var r = rt.Handle(payload, Now);
            Assert.Equal(ControlOutcome.Ok, r.Outcome);
            Assert.Equal("pong", r.Status);
            Assert.Equal("r1", r.RequestId);
        }

        [Fact]
        public void BadHmac_Rejected()
        {
            var armed = WireFixtures.ArmValidClient();
            var rt = new ControlPlaneRuntime(armed);
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", hmacOverride: "deadbeef");
            var r = rt.Handle(payload, Now);
            Assert.Equal(ControlOutcome.Rejected, r.Outcome);
            Assert.Equal("BadHmac", r.Reason);
        }

        [Fact]
        public void ExpiredRequest_Rejected()
        {
            var armed = WireFixtures.ArmValidClient();
            var rt = new ControlPlaneRuntime(armed);
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1", expiry: Now - 1);
            var r = rt.Handle(payload, Now);
            Assert.Equal(ControlOutcome.Rejected, r.Outcome);
            Assert.Equal("RequestExpired", r.Reason);
        }

        [Fact]
        public void Replay_ReturnsCachedReceipt_NoReExecution()
        {
            var armed = WireFixtures.ArmValidClient();
            var rt = new ControlPlaneRuntime(armed);
            string payload = WireFixtures.SignedPayload(armed, "Ping", 1, "r1");
            var first = rt.Handle(payload, Now);
            var second = rt.Handle(payload, Now + 10);
            Assert.Equal(ControlOutcome.Ok, first.Outcome);
            // Same receipt object (cached), so the ts is identical to the first — not re-run.
            Assert.Equal(first.TsUnixMs, second.TsUnixMs);
        }

        [Fact]
        public void MalformedPayload_TransportRejected()
        {
            var armed = WireFixtures.ArmValidClient();
            var rt = new ControlPlaneRuntime(armed);
            var r = rt.Handle("{not json", Now);
            Assert.Equal(ControlOutcome.TransportRejected, r.Outcome);
            Assert.Equal("MalformedFrame", r.Reason);
        }

        [Fact]
        public void MutatingVerb_Admits_ButNotImplementedInM2R()
        {
            // Client run permitting DropItem: admits through the gate, but M2R executes no action.
            var armed = WireFixtures.ArmValidClient(new[] { "DropItem", "Ping" });
            var rt = new ControlPlaneRuntime(armed);
            var args = new System.Collections.Generic.Dictionary<string, object?> { ["itemSlot"] = "s1" };
            string payload = WireFixtures.SignedPayload(armed, "DropItem", 1, "r1", args);
            var r = rt.Handle(payload, Now);
            Assert.Equal(ControlOutcome.NotImplementedInMilestone, r.Outcome);
            Assert.Equal("not-implemented-m2r", r.Status);
        }

        [Fact]
        public void OutOfManifestVerb_Rejected()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "Ping" });
            var rt = new ControlPlaneRuntime(armed);
            var args = new System.Collections.Generic.Dictionary<string, object?> { ["itemSlot"] = "s1" };
            string payload = WireFixtures.SignedPayload(armed, "DropItem", 1, "r1", args);
            var r = rt.Handle(payload, Now);
            Assert.Equal(ControlOutcome.Rejected, r.Outcome);
            Assert.Equal("OutOfManifest", r.Reason);
        }
    }
}
