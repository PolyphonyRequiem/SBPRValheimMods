// ADR-0009 M2R — ArmBootstrapParser + end-to-end arming tests. Proves the runner's local
// bootstrap document parses into an ArmManifest whose ArmingGate evaluation ARMS only under
// the full AND-gate, that a default-disabled bootstrap refuses, and that a production world
// is hard-denied even when the bootstrap is otherwise well-formed.
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ArmBootstrapParserTests
    {
        private static string ValidBootstrapJson(bool enabled = true, long worldUid = Fixtures.DisposableUid, string worldName = Fixtures.DisposableName)
            => "{"
             + "\"enabled\":" + (enabled ? "true" : "false") + ","
             + "\"role\":\"Client\",\"actor\":\"primary\","
             + "\"worldUid\":" + worldUid + ",\"worldName\":\"" + worldName + "\","
             + "\"nonce\":\"" + Fixtures.Nonce + "\",\"expiry\":" + Fixtures.ArmExpiry + ","
             + "\"hmacSecret\":\"" + Fixtures.Secret + "\",\"operatorToken\":\"op-tok\","
             + "\"loopbackPort\":0,\"verbs\":\"Ping,Disarm\","
             + "\"hashes\":{\"product\":\"p1\",\"helper\":\"h1\",\"game\":\"g1\",\"bepinex\":\"b1\",\"harmony\":\"y1\",\"scenario\":\"s1\"}"
             + "}";

        [Fact]
        public void ValidBootstrap_Parses_AndArmsUnderFullGate()
        {
            var boot = ArmBootstrapParser.Parse(ValidBootstrapJson());
            Assert.True(boot.Ok);
            Assert.Equal("op-tok", boot.OperatorToken);
            var decision = ArmingGate.Evaluate(
                boot.Manifest, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.True(decision.Armed);
            Assert.Equal(HarnessRole.Client, decision.State!.Role);
        }

        [Fact]
        public void DisabledBootstrap_RefusesToArm()
        {
            var boot = ArmBootstrapParser.Parse(ValidBootstrapJson(enabled: false));
            Assert.True(boot.Ok); // parses fine
            var decision = ArmingGate.Evaluate(
                boot.Manifest, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.False(decision.Armed);
            Assert.Equal(RejectReason.DisabledByDefault, decision.Reason);
        }

        [Fact]
        public void MalformedBootstrap_FailsClosed()
        {
            var boot = ArmBootstrapParser.Parse("{not json");
            Assert.False(boot.Ok);
            Assert.Equal("MalformedBootstrap", boot.Reason);
        }

        [Fact]
        public void MissingWorldUid_FailsClosed()
        {
            var boot = ArmBootstrapParser.Parse("{\"enabled\":true,\"role\":\"Client\"}");
            Assert.False(boot.Ok);
            Assert.Equal("MissingWorldUid", boot.Reason);
        }

        [Fact]
        public void ProductionWorld_HardDenied_EvenWhenMisconfiguredAllowlist()
        {
            // Bootstrap points at a production-marked world; a misconfigured allowlist that
            // includes it must STILL be hard-denied by the gate.
            var prodWorld = new WorldIdentity(2456, "Niflheim");
            string json = ValidBootstrapJson(worldUid: 2456, worldName: "Niflheim");
            var boot = ArmBootstrapParser.Parse(json);
            var misconfigured = new WorldPolicy(new[] { prodWorld });
            var observedHashes = new Dictionary<string, string>
            {
                ["product"] = "p1", ["helper"] = "h1", ["game"] = "g1",
                ["bepinex"] = "b1", ["harmony"] = "y1", ["scenario"] = "s1",
            };
            var decision = ArmingGate.Evaluate(boot.Manifest, prodWorld, observedHashes, misconfigured, Fixtures.Now);
            Assert.False(decision.Armed);
            Assert.Equal(RejectReason.ProductionWorldDenied, decision.Reason);
        }
    }
}
