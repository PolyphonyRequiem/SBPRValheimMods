// ============================================================================
//  T029 remediation — WARRIOR T.W.I.G. runtime GATE wiring tests.
// ----------------------------------------------------------------------------
//  QA t_92e47866 / PR #366 FAILED T029 because the pure LocalPlacementProvider
//  had ZERO runtime callers: on a joined client a T.W.I.G. (TrainingDummy)
//  placement ran through vanilla Player.PlacePiece with NO SBPR gating. These
//  tests exercise the SHIPPED runtime seam that closes that gap (link-compiled
//  from ../src) — the missing caller — end to end, bound to the AUTHORITATIVE
//  Local Effect activation runtime merged as t_02c13405 / PR #368:
//
//    * WarriorLocalPlacementGate         composes the pure provider + the
//                                        AUTHORITATIVE Stone aggregate store +
//                                        the committed-bond GovernorPresence
//                                        projection + the composed relationship
//                                        authority + bound sessions + Stone-area
//                                        membership into ONE server decision
//                                        from server-owned facts only.
//    * (no provisional Stone state)      the developed T.W.I.G. node, committed
//                                        Warrior tree, Active Stone Level, and
//                                        Settlement Local policy come from the
//                                        real IStoneAggregateStore the shared
//                                        runtime drives via ACCEPTED commands
//                                        (LocalNodeProvisioningDriver) — there is
//                                        exactly ONE progression truth.
//    * WarriorTwigDedicatedIngress       the joined-dedicated-client path: a
//                                        server-observed ZDO is re-derived and
//                                        gated; refusal requires undo.
//    * WarriorTwigPendingUndoQueue       absorbs the ZDO replication race and
//                                        acts once (or drops on deadline).
//
//  The gate is armed off the composed FoundationalProgressionServer against the
//  SAME authoritative stores the LocalProgressionServer derives activation from,
//  so a T.W.I.G. placement and a Local Effect snapshot agree by construction.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
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

        private static readonly VersionedId TwigNode = new VersionedId("TwigTraining", 1);

        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t029-gate");
        private readonly StoneId _stone;

        // The Governor/owner occupant (holds the authorized Homestead:All Governor bond).
        private readonly AccountId _account = new AccountId("acct:steam-100");
        private readonly CharacterId _character = new CharacterId("player:5555");

        // A guest occupant (bound, but not owner, no relationship).
        private readonly AccountId _guest = new AccountId("acct:steam-200");
        private readonly CharacterId _guestChar = new CharacterId("player:6666");

        public NiflheimWarriorTwigRuntimeGateTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t029-gate-" + Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        public void Dispose()
        {
            if (Directory.Exists(_durableDir)) Directory.Delete(_durableDir, recursive: true);
        }

        // ── server-owned authority policy stubs (mirror the shipped provisional composition) ─────

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

        private sealed class AllowGovernorAuthority : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) =>
                string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
                && category != FacetCategory.None;
        }

        private sealed class AllowDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) =>
                string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
                && !tree.IsNone;
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

        // ── authoritative composition ───────────────────────────────────────────────

        // A bare Stone-Level-2 Homestead: Warrior Tree NOT committed and NO node development. The
        // provisioning driver must reach Developed purely through accepted commands.
        private StoneProgressionAggregate BareStone(long revision) =>
            new StoneProgressionAggregate(_stone, revision, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null);

        // Compose the FULL authoritative runtime: the Foundational server (owns Authority/Characters/
        // BoundSessions/StoneAreas), the shared Local runtime over the SAME shared stores + a real Stone
        // aggregate store, then ARM the Warrior gate against that authoritative store + governance resolver.
        // Returns everything a test needs to drive accepted commands and query the gate.
        private sealed class Rig
        {
            public FoundationalProgressionServer Server = null!;
            public LocalProgressionServer Local = null!;
            public InMemoryStoneAggregateStore Stones = null!;
            public GovernorPresenceResolver GovernorPresence = null!;
        }

        private Rig NewRig()
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir,
                familyResolver: new FixedFamilyResolver(),
                bondAuthority: new HomesteadBondPolicy(),
                stoneApStore: new InMemoryMirroredStoneApStore());
            server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);

            var stones = new InMemoryStoneAggregateStore();
            stones.PutStone(BareStone(revision: 10));

            var governorPresence = new GovernorPresenceResolver(server.Characters, server.Authority);
            var ownerAuthority = new CommittedGovernorOwnerAuthority(governorPresence);

            var local = LocalProgressionServer.Create(
                _durableDir, stones, server.Characters, server.Authority, server.Relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                ownerAuthority);

            server.ArmWarriorTwig(stones, governorPresence);
            return new Rig { Server = server, Local = local, Stones = stones, GovernorPresence = governorPresence };
        }

        private void SeedChar(FoundationalProgressionServer server, AccountId acct, CharacterId ch, int bondSlots) =>
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(acct, ch,
                    worldProductScope: "t029/trailborne", revision: 0,
                    bondSlots: bondSlots, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));

        // Create the authorized Homestead:All Governor bond for the owner through the ACCEPTED relationship
        // handler (writes the character relationship record + the account–Stone authority reservation).
        private void BondGovernor(FoundationalProgressionServer server, AccountId acct, CharacterId ch)
        {
            // ADO #138: the relationship handler checks proximity itself now, so this fixture must
            // state the server-observed fact that the acting character is standing AT the Stone.
            server.CharacterPositions.Publish(ch, StoneX, StoneZ);
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-bond-" + ch.Value), RelationshipCommandType.CreateBond, _stone,
                new AuthenticatedConnection(acct.Value, ch.Value), default,
                "rel-bond-" + ch.Value, responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner"));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }

        // Develop the T.W.I.G. node to completion via ACCEPTED commands only (commit Warrior tree ->
        // credit BP -> develop node) on the authoritative Stone aggregate. No provisional grant.
        private void ProvisionTwig(Rig rig, AccountId acct, CharacterId ch)
        {
            var driver = new LocalNodeProvisioningDriver(rig.Local);
            var result = driver.Provision(new AuthoritativeSubject(acct, ch), _stone, TwigNode, "qa-twig");
            Assert.True(result.IsDeveloped,
                "QA provisioning must develop the T.W.I.G. node through accepted commands: "
                + result.FailedStep + "/" + result.ResultCode);
        }

        private void SetPolicy(Rig rig, AccountId owner, CharacterId ownerCh, LocalBeneficiaryMode mode,
            IReadOnlyList<string> allowlist)
        {
            var driver = new LocalNodeProvisioningDriver(rig.Local);
            var code = driver.SetPolicy(new AuthoritativeSubject(owner, ownerCh), _stone, mode, allowlist, "qa-policy");
            Assert.Equal("Applied", code);
        }

        private void Bind(FoundationalProgressionServer server, AccountId acct, CharacterId ch) =>
            server.BoundSessions.Bind(ch.Value, new PilotSessionPrincipal(acct, ch, "sess-" + ch.Value));

        // A fully-provisioned owner: developed T.W.I.G. node, committed Warrior tree, authorized Governor
        // bonded, bound session. Everyone policy (default) is active for anyone inside the Area.
        private Rig ProvisionedOwner()
        {
            var rig = NewRig();
            SeedChar(rig.Server, _account, _character, bondSlots: 1);
            BondGovernor(rig.Server, _account, _character);
            ProvisionTwig(rig, _account, _character);
            Bind(rig.Server, _account, _character);
            return rig;
        }

        private string PeerKey => _character.Value;
        private string GuestPeerKey => _guestChar.Value;

        // ── listen-host gate: the exact admit path ─────────────────────────────────

        [Fact]
        public void Gate_is_armed_on_the_production_server_against_the_authoritative_runtime()
        {
            var rig = ProvisionedOwner();
            Assert.NotNull(rig.Server.WarriorTwigGate);
            Assert.Equal(Twig, rig.Server.WarriorTwigGate!.TwigPrefabName);
            Assert.NotNull(rig.Server.WarriorTwigPending);
        }

        [Fact]
        public void Gate_is_null_before_arming()
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir, new FixedFamilyResolver(), new HomesteadBondPolicy(),
                new InMemoryMirroredStoneApStore());
            Assert.Null(server.WarriorTwigGate);
            Assert.Null(server.WarriorTwigPending);
        }

        [Fact]
        public void Provisioned_owner_inside_area_with_build_permission_is_admitted()
        {
            var rig = ProvisionedOwner();

            var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Admitted, outcome.Disposition);
            Assert.True(outcome.IsAdmitted);
            Assert.False(outcome.RequiresUndo);
            Assert.Equal(_stone.Value, outcome.StoneId.Value);
        }

        [Fact]
        public void Provisioned_owner_without_build_permission_is_refused_and_must_undo()
        {
            var rig = ProvisionedOwner();

            var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: false);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.MissingBuildPermission, outcome.Admission);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Occupant_outside_private_policy_is_refused_and_must_undo()
        {
            var rig = ProvisionedOwner();
            // Owner sets Private policy with an empty allowlist through the accepted owner-only handler.
            SetPolicy(rig, _account, _character, LocalBeneficiaryMode.Private, new List<string>());

            // A bound guest (not the owner, no relationship, not in the allowlist) is outside the policy.
            SeedChar(rig.Server, _guest, _guestChar, bondSlots: 1);
            Bind(rig.Server, _guest, _guestChar);

            var outcome = rig.Server.WarriorTwigGate!.Admit(GuestPeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, outcome.Admission);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Governance_dormancy_refuses_even_the_owner_after_governor_release()
        {
            var rig = ProvisionedOwner();

            // Release the Governor's Bond through the accepted handler: no authorized Governor remains, so
            // every Local Effect is dormant (spec US5 sc2) even for the (former) owner.
            var release = rig.Server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-release-gov"), RelationshipCommandType.ReleaseRelationship, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default,
                "rel-bond-" + _character.Value));
            Assert.Equal(RelationshipCommandOutcome.Applied, release.Outcome);

            var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, outcome.Admission);
        }

        [Fact]
        public void Undeveloped_node_is_refused()
        {
            // A rig whose Stone has NO developed T.W.I.G. node (never provisioned): the effect cannot be
            // active because nothing is developed, even for a bonded, bound owner inside the Area.
            var rig = NewRig();
            SeedChar(rig.Server, _account, _character, bondSlots: 1);
            BondGovernor(rig.Server, _account, _character);
            Bind(rig.Server, _account, _character);

            var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, outcome.Admission);
        }

        [Fact]
        public void Unbound_peer_fails_closed()
        {
            var rig = ProvisionedOwner();
            var outcome = rig.Server.WarriorTwigGate!.Admit("player:nobody", Twig, StoneX, StoneZ, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal("UnboundPeer", outcome.Reason);
            Assert.True(outcome.RequiresUndo);
        }

        [Fact]
        public void Placement_outside_every_stone_area_is_refused()
        {
            var rig = ProvisionedOwner();

            // Far outside the single registered Stone Area (radius 20 at 100,100).
            var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, Twig, 5000.0, 5000.0, hasOrdinaryBuildPermission: true);

            Assert.Equal(WarriorPlacementGateDisposition.Denied, outcome.Disposition);
            Assert.Equal("OutsideStoneArea", outcome.Reason);
        }

        [Fact]
        public void A_non_twig_prefab_is_declined_not_gated()
        {
            var rig = ProvisionedOwner();

            foreach (var other in new[] { "wood_floor", "piece_workbench", "trainingdummy", "" })
            {
                var outcome = rig.Server.WarriorTwigGate!.Admit(PeerKey, other, StoneX, StoneZ, hasOrdinaryBuildPermission: true);
                Assert.Equal(WarriorPlacementGateDisposition.NotTwig, outcome.Disposition);
                Assert.False(outcome.RequiresUndo);   // the net48 layer leaves a non-T.W.I.G. untouched
            }
        }

        // ── dedicated ingress: re-derive server-side, gate, undo on refusal ────────

        [Fact]
        public void Dedicated_ingress_admits_a_provisioned_creator_bound_twig()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();
            instances.Put("77:1", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:1", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.True(result.Outcome.IsAdmitted);
            Assert.False(result.RequiresUndo);
        }

        [Fact]
        public void Dedicated_ingress_refuses_and_requires_undo_without_build_permission()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();
            instances.Put("77:2", Twig, creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:2", (x, z) => false);   // no ward access

            Assert.True(result.IsResolved);
            Assert.True(result.RequiresUndo);
            Assert.Equal("77:2", result.InstanceKey);
            Assert.Equal(WarriorPlacementAdmission.MissingBuildPermission, result.Outcome.Admission);
        }

        [Fact]
        public void Dedicated_ingress_refuses_outside_policy_and_requires_undo()
        {
            var rig = ProvisionedOwner();
            SetPolicy(rig, _account, _character, LocalBeneficiaryMode.Private, new List<string>());
            SeedChar(rig.Server, _guest, _guestChar, bondSlots: 1);
            Bind(rig.Server, _guest, _guestChar);

            var instances = new FakeInstanceSource();
            instances.Put("77:3", Twig, creatorPrincipal: GuestPeerKey, x: StoneX, z: StoneZ);

            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(GuestPeerKey, "77:3", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.True(result.RequiresUndo);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, result.Outcome.Admission);
        }

        [Fact]
        public void Dedicated_ingress_declines_a_non_twig_instance_without_undo()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();
            instances.Put("77:4", "wood_floor", creatorPrincipal: PeerKey, x: StoneX, z: StoneZ);

            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:4", (x, z) => true);

            Assert.True(result.IsResolved);
            Assert.Equal(WarriorPlacementGateDisposition.NotTwig, result.Outcome.Disposition);
            Assert.False(result.RequiresUndo);
        }

        [Fact]
        public void Dedicated_ingress_rejects_a_creator_mismatch_without_touching_the_piece()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();
            // The ZDO was created by a DIFFERENT player than the authenticated sender.
            instances.Put("77:5", Twig, creatorPrincipal: "player:9999", x: StoneX, z: StoneZ);

            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:5", (x, z) => true);

            Assert.False(result.IsResolved);
            Assert.Equal("CreatorMismatch", result.UnresolvedReason);
            Assert.False(result.RequiresUndo);   // never undo a piece the sender did not place
        }

        [Fact]
        public void Dedicated_ingress_awaits_replication_for_an_unresolved_zdo()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();   // empty — the ZDO has not replicated yet
            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var result = ingress.Ingest(PeerKey, "77:6", (x, z) => true);

            Assert.False(result.IsResolved);
            Assert.True(result.IsAwaitingReplication);
        }

        // ── pending undo queue: race-safety ────────────────────────────────────────

        [Fact]
        public void Pending_queue_keeps_awaiting_then_acts_once_the_zdo_resolves()
        {
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();
            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
            var queue = rig.Server.WarriorTwigPending!;

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
            var rig = ProvisionedOwner();

            var instances = new FakeInstanceSource();   // never resolves
            var ingress = rig.Server.CreateWarriorTwigDedicatedIngress(instances);
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
