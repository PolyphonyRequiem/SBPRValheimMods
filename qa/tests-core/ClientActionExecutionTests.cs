// ADR-0009 M5-BIND — client action/observation EXECUTION wiring tests.
//
// WHAT THIS PROVES AND WHY IT MATTERS: before this wiring, every client verb
// (Craft/UpgradeItem/DropItem/PickUpNearest/TamperField + the Read* family) passed the
// full fail-closed admission gate and then returned NotImplementedInMilestone — the
// M4-BIND adapters were compiled, unit-tested, and unreachable. That is the reason no
// automated T022 leg has ever executed and every proof to date required a human typing
// at the game console.
//
// These tests bind the SEAM, not a stub of the seam: they drive the real signed-envelope
// codec → real RequestAdmission → real ControlDispatcher → executor path, and assert both
// that an admitted verb REACHES the executor and — critically — that every fail-closed
// gate still refuses BEFORE reaching it. A test suite that only proved the happy path
// would be exactly the green-against-stubs failure this harness exists to avoid.
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ClientActionExecutionTests
    {
        private const long Now = Fixtures.Now;

        /// <summary>
        /// A recording executor. It is NOT a stand-in for the game seam under test — the real
        /// engine-bound bridge is exercised under net48 — it is a probe that records whether the
        /// control plane actually reached the executor, and with what.
        /// </summary>
        private sealed class RecordingExecutor : IClientActionVerbExecutor
        {
            private readonly bool _executes;
            public readonly List<string> Calls = new();
            public IReadOnlyDictionary<string, object?>? LastArgs;
            public string? LastRequestId;
            public string? LastNonce;
            public long LastSeq;
            public long LastWorldUid;

            public RecordingExecutor(bool executes = true) { _executes = executes; }

            public bool Handles(string? verb) =>
                verb == "Craft" || verb == "ReadWorldName" || verb == "TamperField" || verb == "sbpr_master";

            public ClientVerbOutcome Execute(
                string verb, IReadOnlyDictionary<string, object?> args,
                string requestId, string nonce, long seq, long worldUid, long nowUnixMs)
            {
                Calls.Add(verb);
                LastArgs = args;
                LastRequestId = requestId;
                LastNonce = nonce;
                LastSeq = seq;
                LastWorldUid = worldUid;
                return _executes
                    ? ClientVerbOutcome.Ran(verb + ":ok")
                    : ClientVerbOutcome.Refused(verb + ":rejected");
            }
        }

        // ── The wiring itself ────────────────────────────────────────────────

        [Fact]
        public void AdmittedClientVerb_ReachesExecutor_AndReportsOk()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(armed, "ReadWorldName", 1, "r1");
            var receipt = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
            Assert.Equal("ReadWorldName:ok", receipt.Status);
            Assert.Equal(new[] { "ReadWorldName" }, exec.Calls);
        }

        [Fact]
        public void WithoutExecutor_AdmittedVerbStillReportsNotImplemented()
        {
            // The historical M2R behaviour must be preserved exactly when no executor is
            // injected — this is what makes the wiring additive and reversible.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var rt = new ControlPlaneRuntime(armed);

            var receipt = rt.Handle(WireFixtures.SignedPayload(armed, "ReadWorldName", 1, "r1"), Now);

            Assert.Equal(ControlOutcome.NotImplementedInMilestone, receipt.Outcome);
            Assert.Equal("not-implemented-m2r", receipt.Status);
        }

        [Fact]
        public void ExecutorRefusal_ReportsRejected_NotOk()
        {
            // A refused primitive must never read as success (no manufactured green).
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor(executes: false);
            var rt = new ControlPlaneRuntime(armed, exec);

            var receipt = rt.Handle(WireFixtures.SignedPayload(armed, "ReadWorldName", 1, "r1"), Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal("ReadWorldName:rejected", receipt.Status);
        }

        [Fact]
        public void VerbTheExecutorDoesNotHandle_FallsBackToNotImplemented()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldUid" });
            var exec = new RecordingExecutor(); // Handles() says no to ReadWorldUid
            var rt = new ControlPlaneRuntime(armed, exec);

            var receipt = rt.Handle(WireFixtures.SignedPayload(armed, "ReadWorldUid", 1, "r1"), Now);

            Assert.Equal(ControlOutcome.NotImplementedInMilestone, receipt.Outcome);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void ReceiptIdentity_IsPassedToExecutor()
        {
            // The adapters emit correlatable receipts only if the envelope identity reaches them.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            rt.Handle(WireFixtures.SignedPayload(armed, "ReadWorldName", 7, "req-xyz"), Now);

            Assert.Equal("req-xyz", exec.LastRequestId);
            Assert.Equal(armed.Nonce, exec.LastNonce);
            Assert.Equal(7, exec.LastSeq);
            Assert.Equal(armed.World.WorldUid, exec.LastWorldUid);
        }

        [Fact]
        public void TypedArgs_ReachExecutorIntact()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "Craft" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var args = new Dictionary<string, object?>
            {
                ["recipeName"] = "Club",
                ["station"] = "piece_workbench",
            };
            var receipt = rt.Handle(WireFixtures.SignedPayload(armed, "Craft", 1, "r1", args), Now);

            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
            Assert.NotNull(exec.LastArgs);
            Assert.Equal("Club", exec.LastArgs!["recipeName"]);
            Assert.Equal("piece_workbench", exec.LastArgs!["station"]);
        }

        // ── The fail-closed gates must still refuse BEFORE the executor ──────
        // Each of these would be a security regression if the executor ran anyway.

        [Fact]
        public void OutOfManifestVerb_NeverReachesExecutor()
        {
            // Craft is a catalog verb but NOT permitted this run.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var args = new Dictionary<string, object?>
            {
                ["recipeName"] = "Club",
                ["station"] = "piece_workbench",
            };
            var receipt = rt.Handle(WireFixtures.SignedPayload(armed, "Craft", 1, "r1", args), Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.OutOfManifest.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void BadHmac_NeverReachesExecutor()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(
                armed, "ReadWorldName", 1, "r1", hmacOverride: "deadbeef");
            var receipt = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.BadHmac.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void WrongWorldUid_NeverReachesExecutor()
        {
            // The exact-world gate is what keeps this off a production world.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(
                armed, "ReadWorldName", 1, "r1", worldUidOverride: 999_999);
            var receipt = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.RequestWorldMismatch.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void ExpiredRequest_NeverReachesExecutor()
        {
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(
                armed, "ReadWorldName", 1, "r1", expiry: Now - 1);
            var receipt = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.RequestExpired.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void Replay_ReturnsCachedReceipt_AndExecutesExactlyOnce()
        {
            // A re-delivered request must not craft/tamper a second time.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(armed, "ReadWorldName", 1, "r1");
            var first = rt.Handle(payload, Now);
            var second = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Ok, first.Outcome);
            Assert.Equal(ControlOutcome.Ok, second.Outcome);
            Assert.Single(exec.Calls); // executed once, not twice
        }

        [Fact]
        public void ServerRoleEnvelope_NeverReachesClientExecutor()
        {
            // Role admission is defence in depth against a client running a server verb.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            string payload = WireFixtures.SignedPayload(
                armed, "ReadWorldName", 1, "r1", roleOverride: "Server");
            var receipt = rt.Handle(payload, Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.RoleMismatch.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void ExecutorCannotWidenTheVerbSurface()
        {
            // An executor claiming to Handle() a verb that is not in the static catalog must
            // still never run it: admission rejects UnknownVerb long before Handles() is asked.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var receipt = rt.Handle(
                WireFixtures.SignedPayload(armed, "GrantEntitlement", 1, "r1"), Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.UnknownVerb.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        // ── The product admin relay verb (ADR-0009 §4 boundary) ──────────────

        [Fact]
        public void SbprMaster_IsClientRoleOnly()
        {
            // The dedicated server starts NO host listener, so an entitlement relay can only
            // ride the client loopback. A Server-role run must not be able to run this verb.
            var verb = VerbCatalog.Get("sbpr_master");
            Assert.NotNull(verb);
            Assert.True(verb!.AllowsRole(HarnessRole.Client));
            Assert.False(verb.AllowsRole(HarnessRole.Server));
        }

        [Fact]
        public void SbprMaster_DiscriminatorBoundsPinTheProductsRealValues()
        {
            // The retired QaT022Driver sent 0/1 against the product's real 1/2 — a false-sent
            // defect where the wire looked healthy and nothing applied. These bounds are what
            // stop that returning: 0 and 3 must be structurally unrepresentable.
            var armed = WireFixtures.ArmValidClient(new[] { "sbpr_master" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            foreach (long bad in new long[] { 0, 3, -1, 99 })
            {
                var args = new Dictionary<string, object?> { ["discriminator"] = bad };
                var receipt = rt.Handle(
                    WireFixtures.SignedPayload(armed, "sbpr_master", 1, "bad" + bad, args), Now);
                Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
                Assert.Equal(RejectReason.OutOfBoundsArg.ToString(), receipt.Reason);
            }
            Assert.Empty(exec.Calls);
        }

        [Theory]
        [InlineData(1L)] // OFFER
        [InlineData(2L)] // BUY
        public void SbprMaster_AdmitsBothRealDiscriminators(long discriminator)
        {
            var armed = WireFixtures.ArmValidClient(new[] { "sbpr_master" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var args = new Dictionary<string, object?> { ["discriminator"] = discriminator };
            var receipt = rt.Handle(
                WireFixtures.SignedPayload(armed, "sbpr_master", 1, "r1", args), Now);

            Assert.Equal(ControlOutcome.Ok, receipt.Outcome);
            Assert.Equal(new[] { "sbpr_master" }, exec.Calls);
            Assert.Equal(discriminator, exec.LastArgs!["discriminator"]);
        }

        [Fact]
        public void SbprMaster_NotInManifest_IsRefused()
        {
            // Entitlement relay is reachable ONLY when the run's capability manifest names it.
            // A run that did not ask for it cannot drive the product's admin path.
            var armed = WireFixtures.ArmValidClient(new[] { "ReadWorldName" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var args = new Dictionary<string, object?> { ["discriminator"] = 1L };
            var receipt = rt.Handle(
                WireFixtures.SignedPayload(armed, "sbpr_master", 1, "r1", args), Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.OutOfManifest.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void SbprMaster_RejectsUndeclaredArgs()
        {
            // The closed schema is what stops an extra field (a smuggled identity, a forged
            // subject) riding along with the discriminator.
            var armed = WireFixtures.ArmValidClient(new[] { "sbpr_master" });
            var exec = new RecordingExecutor();
            var rt = new ControlPlaneRuntime(armed, exec);

            var args = new Dictionary<string, object?>
            {
                ["discriminator"] = 1L,
                ["subject"] = "somebody-else",
            };
            var receipt = rt.Handle(
                WireFixtures.SignedPayload(armed, "sbpr_master", 1, "r1", args), Now);

            Assert.Equal(ControlOutcome.Rejected, receipt.Outcome);
            Assert.Equal(RejectReason.OutOfBoundsArg.ToString(), receipt.Reason);
            Assert.Empty(exec.Calls);
        }

        [Fact]
        public void SbprMaster_CarriesNoKeyOrSubjectOnTheWire()
        {
            // Structural firewall: the verb's ENTIRE declared surface is one bounded integer.
            // There is no argument through which a key, signature, identity, or amount could
            // be passed, so the harness cannot express a grant even if it wanted to.
            var verb = VerbCatalog.Get("sbpr_master");
            Assert.NotNull(verb);
            Assert.Single(verb!.Args);
            Assert.Equal("discriminator", verb.Args[0].Name);
            Assert.Equal(ArgKind.BoundedInt, verb.Args[0].Kind);
        }
    }
}
