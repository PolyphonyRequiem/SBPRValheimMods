// ============================================================================
//  T029 remediation — WARRIOR T.W.I.G. runtime GATE wiring tests.
// ----------------------------------------------------------------------------
//  QA t_92e47866 / PR #366 FAILED T029 because the pure LocalPlacementProvider
//  had ZERO runtime callers: on a joined client a T.W.I.G. (TrainingDummy)
//  placement ran through vanilla Player.PlacePiece with NO SBPR gating. These
//  tests exercise the SHIPPED runtime seam that closes that gap (link-compiled
//  from ../src) — the missing caller — end to end:
//
//    * WarriorLocalPlacementGate         composes the pure provider + the
//                                        provisional Stone state + the composed
//                                        relationship authority + bound sessions
//                                        + Stone-area membership into ONE server
//                                        decision from server-owned facts only.
//    * WarriorProvisionalStoneStateSource  the provisional Stone-owned Local
//                                        state (Attuned policy, developed T.W.I.G.
//                                        node, committed Warrior tree, level 2),
//                                        the analogue of the Foundational runtime's
//                                        provisional family/bond policies.
//    * WarriorTwigDedicatedIngress       the joined-dedicated-client path: a
//                                        server-observed ZDO is re-derived and
//                                        gated; refusal requires undo.
//    * WarriorTwigPendingUndoQueue       absorbs the ZDO replication race and
//                                        acts once (or drops on deadline).
//
//  The gate is resolved off the composed FoundationalProgressionServer, so these
//  assertions prove the REAL production wiring, not just the pure value object
//  (which NiflheimWarriorTwigPlacementTests already covers).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimWarriorTwigRuntimeGateTests : IDisposable
    {
        private const string Twig = "TrainingDummy";
        private const double StoneX = 100.0;
        private const double StoneZ = 100.0;

        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t029-gate");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct:steam-100");
        private readonly CharacterId _character = new CharacterId("player:5555");

        public NiflheimWarriorTwigRuntimeGateTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t029-gate-" + Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        public void Dispose()
        {
            if (Directory.Exists(_durableDir)) Directory.Delete(_durableDir, recursive: true);
        }

        // ── fixtures (mirror the shipped Foundational provisional composition) ─────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            { family = "Settlement"; variant = "Homestead"; return true; }
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
            private readonly Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new Dictionary<string, ServerPlacedInstanceFacts>(StringComparer.Ordinal);

            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);

            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        private FoundationalProgressionServer NewServer()
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir,
                familyResolver: new FixedFamilyResolver(),
                bondAuthority: new HomesteadBondPolicy(),
                stoneApStore: new InMemoryMirroredStoneApStore());
            server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);
            return server;
        }

        private void Seed(FoundationalProgressionServer server, CharacterId who)
        {
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, who,
                    worldProductScope: "t029/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
        }

        private void Attune(FoundationalProgressionServer server, CharacterId who)
        {
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-attune-" + who.Value), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, who.Value), default, "rel-att-" + who.Value));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }

        private void Bind(FoundationalProgressionServer server, CharacterId who) =>
            server.BoundSessions.Bind(who.Value, new PilotSessionPrincipal(_account, who, "sess-" + who.Value));

        private string PeerKey => _character.Value;

        // ── listen-host gate: the exact admit path ─────────────────────────────────

        [Fact]
        public void Gate_is_composed_on_the_production_server()
        {
            var server = NewServer();
            Assert.NotNull(server.WarriorTwigGate);
            Assert.Equal(Twig, server.WarriorTwigGate.TwigPrefabName);
            Assert.NotNull(server.WarriorTwigPending);
        }

        [Fact]
        public void Attuned_bound_occupant_inside_area_with_build_permission_is_admitted()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);   // Attuned policy -> an active relationship makes the effect active
            Bind(server, _character);

            var outcome = server.WarriorTwigGate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Admitted, outcome.Disposition);
            Assert.True(outcome.IsAdmitted);
            Assert.False(outcome.RequiresUndo);
            Assert.Equal(_stone.Value, outcome.StoneId.Value);
        }

        [Fact]
        public void Attuned_occupant_without_build_permission_is_refused_and_must_undo()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var outcome = server.WarriorTwigGate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: false);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.MissingBuildPermission, outcome.Admission);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Bound_but_unattuned_occupant_is_refused_outside_policy_and_must_undo()
        {
            var server = NewServer();
            Seed(server, _character);
            Bind(server, _character);   // no Attunement -> outside the Attuned policy

            var outcome = server.WarriorTwigGate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, outcome.Admission);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Unbound_peer_fails_closed()
        {
            var server = NewServer();
            // No Bind -> no bound internal session.
            var outcome = server.WarriorTwigGate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal("UnboundPeer", outcome.Reason);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Placement_outside_every_stone_area_is_refused()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            // Far outside the single registered Stone Area (radius 20 at 100,100).
            var outcome = server.WarriorTwigGate.Admit(PeerKey, Twig, 5000.0, 5000.0, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal("OutsideStoneArea", outcome.Reason);
        }

        [Fact]
        public void A_non_twig_prefab_is_declined_not_gated()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            foreach (var other in new[] { "wood_floor", "piece_workbench", "trainingdummy", "" })
            {
                var outcome = server.WarriorTwigGate.Admit(PeerKey, other, StoneX, StoneZ, hasOrdinaryBuildPermission: true);
                Assert.Equal(WarriorPlacementGateDisposition.NotTwig, outcome.Disposition);
                Assert.False(outcome.RequiresUndo);   // the net48 layer leaves a non-T.W.I.G. untouched
            }
        }

        [Fact]
        public void Governance_dormancy_refuses_even_an_attuned_occupant()
        {
            // A source with no authorized Governor present dormants every Local Effect (spec US5 sc2).
            var gate = new WarriorLocalPlacementGate(
                new WarriorProvisionalStoneStateSource(LocalBeneficiaryMode.Attuned, authorizedGovernorPresent: false),
                new InMemoryAccountStoneAuthorityStore(),
                BoundWith(),
                AreaWith());

            var outcome = gate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);
            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, outcome.Admission);
        }

        [Fact]
        public void Everyone_policy_admits_any_bound_inside_permitted_occupant()
        {
            // Everyone policy: no relationship needed. Proves the provisional policy override path.
            var gate = new WarriorLocalPlacementGate(
                new WarriorProvisionalStoneStateSource(LocalBeneficiaryMode.Everyone),
                new InMemoryAccountStoneAuthorityStore(),
                BoundWith(),
                AreaWith());

            var outcome = gate.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);
            Assert.True(outcome.IsAdmitted);
        }

        private BoundSessionPrincipalIndex BoundWith()
        {
            var idx = new BoundSessionPrincipalIndex();
            idx.Bind(_character.Value, new PilotSessionPrincipal(_account, _character, "sess"));
            return idx;
        }

        private StoneAreaMembership AreaWith()
        {
            var m = new StoneAreaMembership();
            m.Register(_stone, StoneX, StoneZ, radius: 20.0);
            return m;
        }

        // ── dedicated ingress: re-derive server-side, gate, undo on refusal ────────

        [Fact]
        public void Dedicated_ingress_admits_an_attuned_creator_bound_twig()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var instances = new FakeInstanceSource();
            instances.Put("77:1", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:1", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.True(result.Outcome.IsAdmitted);
            Assert.False(result.RequiresUndo);
        }

        [Fact]
        public void Dedicated_ingress_refuses_and_requires_undo_without_build_permission()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var instances = new FakeInstanceSource();
            instances.Put("77:2", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:2", (x, z) => false);   // no ward access

            Assert.True(result.IsResolved);
            Assert.True(result.RequiresUndo);
            Assert.Equal("77:2", result.InstanceKey);
            Assert.Equal(WarriorPlacementAdmission.MissingBuildPermission, result.Outcome.Admission);
        }

        [Fact]
        public void Dedicated_ingress_refuses_outside_policy_and_requires_undo()
        {
            var server = NewServer();
            Seed(server, _character);
            Bind(server, _character);   // no Attunement

            var instances = new FakeInstanceSource();
            instances.Put("77:3", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:3", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.True(result.RequiresUndo);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, result.Outcome.Admission);
        }

        [Fact]
        public void Dedicated_ingress_declines_a_non_twig_instance_without_undo()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var instances = new FakeInstanceSource();
            instances.Put("77:4", "wood_floor", creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:4", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.Equal(WarriorPlacementGateDisposition.NotTwig, result.Outcome.Disposition);
            Assert.False(result.RequiresUndo);
        }

        [Fact]
        public void Dedicated_ingress_rejects_a_creator_mismatch_without_touching_the_piece()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var instances = new FakeInstanceSource();
            // The ZDO was created by a DIFFERENT player than the authenticated sender.
            instances.Put("77:5", Twig, creatorPrincipal: "player:9999", x: StoneX, z: StoneZ);

            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:5", (x, z) => true);

            Assert.False(result.IsResolved);
            Assert.Equal("CreatorMismatch", result.UnresolvedReason);
            Assert.False(result.RequiresUndo);   // never undo a piece the sender did not place
        }

        [Fact]
        public void Dedicated_ingress_awaits_replication_for_an_unresolved_zdo()
        {
            var server = NewServer();
            Bind(server, _character);

            var instances = new FakeInstanceSource();   // empty — the ZDO has not replicated yet
            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:6", (x, z) => true);

            Assert.False(result.IsResolved);
            Assert.True(result.IsAwaitingReplication);
        }

        // ── pending undo queue: race-safety ────────────────────────────────────────

        [Fact]
        public void Pending_queue_keeps_awaiting_then_acts_once_the_zdo_resolves()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);
            Bind(server, _character);

            var instances = new FakeInstanceSource();
            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var queue = server.WarriorTwigPending;

            Assert.Equal(WarriorTwigPendingUndoQueue.EnqueueResult.Enqueued,
                queue.Enqueue(PeerKey, "88:1", nowTicks: 0));

            // Tick 1: ZDO absent -> kept (awaiting replication), nothing acted on.
            var r1 = queue.Pump(nowTicks: 1, (pk, key) => ingress.Ingest(pk, key, (x, z) => false));
            Assert.Empty(r1);
            Assert.Equal(1, queue.Count);

            // ZDO replicates as a refusable (no build permission) T.W.I.G.
            instances.Put("88:1", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var r2 = queue.Pump(nowTicks: 2, (pk, key) => ingress.Ingest(pk, key, (x, z) => false));
            Assert.Single(r2);
            Assert.True(r2[0].RequiresUndo);
            Assert.Equal(0, queue.Count);   // resolved -> removed
        }

        [Fact]
        public void Pending_queue_drops_an_entry_whose_zdo_never_replicates_by_deadline()
        {
            var server = NewServer();
            Bind(server, _character);

            var instances = new FakeInstanceSource();   // never resolves
            var ingress = server.CreateWarriorTwigDedicatedIngress(instances);
            var queue = new WarriorTwigPendingUndoQueue(TimeSpan.FromTicks(10));

            queue.Enqueue(PeerKey, "88:2", nowTicks: 0);

            // Before deadline: kept.
            Assert.Empty(queue.Pump(nowTicks: 5, (pk, key) => ingress.Ingest(pk, key, (x, z) => true)));
            Assert.Equal(1, queue.Count);

            // Past deadline: dropped with no action.
            Assert.Empty(queue.Pump(nowTicks: 100, (pk, key) => ingress.Ingest(pk, key, (x, z) => true)));
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Pending_queue_converges_duplicate_notices()
        {
            var queue = new WarriorTwigPendingUndoQueue(TimeSpan.FromSeconds(30));
            Assert.Equal(WarriorTwigPendingUndoQueue.EnqueueResult.Enqueued, queue.Enqueue(PeerKey, "88:3", 0));
            Assert.Equal(WarriorTwigPendingUndoQueue.EnqueueResult.Converged, queue.Enqueue(PeerKey, "88:3", 5));
            Assert.Equal(1, queue.Count);
        }
    }
}
