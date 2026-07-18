// ============================================================================
//  Homestead progression — Foundational AP receipt CONTRACT tests (T002, Gate A).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side slice (link-compiled from
//  ../src, not copied — see the .csproj) that consumes the T001-selected
//  mechanism: PrincipalResolver, OperationReceiptStore, ProgressionCommandPipeline,
//  FoundationalPlacementAdapter, and the in-memory Stone/character projection sinks.
//
//  Named acceptance covered here:
//    AT-P0-AP-ATOMIC              one Personal + one Cumulative + one Mirrored
//                                 Stone AP delta commit together.
//    AT-P0-HOSTILE-PRINCIPAL      account/character substitution rejects with no
//                                 mutation.
//    AT-P0-MIRRORED-ACCUMULATES-ONLY  Mirrored Stone AP only accumulates; the sink
//                                 exposes no spend/threshold/Facet operation.
//    AT-P0-REPLAY (partial)       same-op replay + conflicting-op rejection here;
//                                 crash/restart recovery lives in the recovery suite.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionContractTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:777");
        private readonly StoneId _stone;
        private readonly AccountId _ownerAccount = new AccountId("plat-owner");
        private readonly CharacterId _ownerCharacter = new CharacterId("char-owner");

        private readonly OperationReceiptStore _receipts;
        private readonly InMemoryMirroredStoneApStore _stoneStore;
        private readonly InMemoryCharacterApStore _characterStore;
        private readonly ProgressionCommandPipeline _pipeline;

        public NiflheimProgressionContractTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t002-contract-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 12, -4);
            _stoneStore = new InMemoryMirroredStoneApStore();
            _characterStore = new InMemoryCharacterApStore();
            _receipts = new OperationReceiptStore(_journalPath, _stoneStore, _characterStore);

            // The connection now carries the BOUND INTERNAL account/character (Tracer 3); the resolver
            // reads them off the connection with no provider lookup.
            var resolver = new PrincipalResolver();
            var authorizer = new PreconfiguredTestAuthorizer().Allow(_ownerAccount, _stone);
            _pipeline = new ProgressionCommandPipeline(resolver, _receipts, authorizer);
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private AuthenticatedConnection OwnerConnection() =>
            new AuthenticatedConnection("plat-owner", "char-owner");

        private FoundationalPlacementCommand PlacementCommand(string operationId, ClaimedPrincipal claim, string evidence = "foundation_wood_floor")
        {
            var adapter = new FoundationalPlacementAdapter();
            var evidenceFacts = new FoundationalPlacementEvidence(
                new OperationId(operationId), _stone,
                stablePieceId: evidence, pieceInstanceProvenance: "prov-" + evidence,
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1");
            var admission = adapter.Admit(evidenceFacts, OwnerConnection(), claim);
            Assert.True(admission.IsAdmitted);
            return admission.Command;
        }

        // ---- AT-P0-AP-ATOMIC ----

        [Fact]
        public void AtP0ApAtomic_SingleOperation_CommitsExactlyOneOfEachDelta()
        {
            var claim = new ClaimedPrincipal("plat-owner", "char-owner");
            var result = _pipeline.Handle(PlacementCommand("op-1", claim));

            Assert.Equal(CommandOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.PersonalApDelta);
            Assert.Equal(1, result.CumulativeApDelta);
            Assert.Equal(1, result.MirroredStoneApDelta);

            // The three deltas landed in their respective aggregates together.
            Assert.Equal(1, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(1, _characterStore.GetCumulativeAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void AtP0ApAtomic_ClaimMayBeOmitted_ConnectionIsAuthority()
        {
            // No claim in the payload -> connection identity is authoritative; still commits.
            var result = _pipeline.Handle(PlacementCommand("op-noclaim", new ClaimedPrincipal(null, null)));
            Assert.Equal(CommandOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.MirroredStoneApDelta);
        }

        // ---- AT-P0-REPLAY (idempotency, no restart) ----

        [Fact]
        public void AtP0Replay_SameOperationTwice_SecondReplaysWithoutDoubleCount()
        {
            var claim = new ClaimedPrincipal("plat-owner", "char-owner");
            var first = _pipeline.Handle(PlacementCommand("op-replay", claim));
            var second = _pipeline.Handle(PlacementCommand("op-replay", claim));

            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            Assert.Equal(CommandOutcome.Replayed, second.Outcome);
            Assert.Equal(first.ReceiptId, second.ReceiptId);

            // Balances are still exactly one — no duplicate state.
            Assert.Equal(1, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(1, _characterStore.GetCumulativeAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void AtP0Replay_ConflictingBindingUnderSameOperationId_Rejects()
        {
            var claim = new ClaimedPrincipal("plat-owner", "char-owner");
            _pipeline.Handle(PlacementCommand("op-conflict", claim, evidence: "foundation_wood_wall"));
            // Same operationId, different placement evidence -> conflict, no mutation change.
            var conflict = _pipeline.Handle(PlacementCommand("op-conflict", claim, evidence: "foundation_wood_pole"));

            Assert.Equal(CommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(1, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
        }

        // ---- AT-P0-HOSTILE-PRINCIPAL ----

        [Fact]
        public void AtP0HostilePrincipal_AccountSubstitution_RejectsWithoutMutation()
        {
            // Authenticated socket is the owner, but the payload claims a different account.
            var hostile = new ClaimedPrincipal("plat-victim", "char-owner");
            var result = _pipeline.Handle(PlacementCommand("op-hostile-acct", hostile));

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("PrincipalMismatch", result.ResultCode);
            AssertNothingMutated();
        }

        [Fact]
        public void AtP0HostilePrincipal_CharacterSubstitution_RejectsWithoutMutation()
        {
            var hostile = new ClaimedPrincipal("plat-owner", "char-victim");
            var result = _pipeline.Handle(PlacementCommand("op-hostile-char", hostile));

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("PrincipalMismatch", result.ResultCode);
            AssertNothingMutated();
        }

        [Fact]
        public void AtP0HostilePrincipal_UnauthenticatedPeer_RejectsWithoutMutation()
        {
            var adapter = new FoundationalPlacementAdapter();
            var evidence = new FoundationalPlacementEvidence(
                new OperationId("op-unauth"), _stone, "foundation_wood_door", "prov-x", true, true, "v1");
            // Empty platform id -> no authenticated connection.
            var admission = adapter.Admit(evidence, new AuthenticatedConnection("", ""), new ClaimedPrincipal("plat-owner", "char-owner"));
            var result = _pipeline.Handle(admission.Command);

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("Unauthenticated", result.ResultCode);
            AssertNothingMutated();
        }

        [Fact]
        public void AtP0HostilePrincipal_UnauthorizedPrincipal_RejectsWithoutMutation()
        {
            // A different, authenticated but NOT preconfigured-authorized account earns nothing.
            var resolver = new PrincipalResolver();
            var authorizer = new PreconfiguredTestAuthorizer().Allow(_ownerAccount, _stone);
            var pipeline = new ProgressionCommandPipeline(resolver, _receipts, authorizer);

            var adapter = new FoundationalPlacementAdapter();
            var evidence = new FoundationalPlacementEvidence(
                new OperationId("op-unauthorized"), _stone, "foundation_wood_stakewall", "prov-y", true, true, "v1");
            var admission = adapter.Admit(evidence, new AuthenticatedConnection("plat-stranger", "char-stranger"), new ClaimedPrincipal(null, null));
            var result = pipeline.Handle(admission.Command);

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("RelationshipRequired", result.ResultCode);
            AssertNothingMutated();
        }

        // ---- AT-P0-MIRRORED-ACCUMULATES-ONLY ----

        [Fact]
        public void AtP0MirroredAccumulatesOnly_MultipleOperations_MirroredMonotonicallyAccumulates()
        {
            var claim = new ClaimedPrincipal("plat-owner", "char-owner");
            _pipeline.Handle(PlacementCommand("op-m1", claim, "foundation_wood_beam"));
            _pipeline.Handle(PlacementCommand("op-m2", claim, "foundation_wood_roof"));
            _pipeline.Handle(PlacementCommand("op-m3", claim, "foundation_wood_stair"));

            Assert.Equal(3, _stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(3, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(3, _characterStore.GetCumulativeAp(_ownerAccount, _ownerCharacter, _stone));
        }

        [Fact]
        public void AtP0MirroredAccumulatesOnly_StoreExposesNoSpendOrThresholdSurface()
        {
            // Contract guard: the Mirrored Stone AP sink type has no debit/spend/threshold/Facet
            // member. This pins data-model.md's "never debited or applied to a threshold/Facet".
            var members = typeof(IMirroredStoneApStore).GetMembers();
            foreach (var m in members)
            {
                var name = m.Name.ToLowerInvariant();
                Assert.DoesNotContain("debit", name);
                Assert.DoesNotContain("spend", name);
                Assert.DoesNotContain("threshold", name);
                Assert.DoesNotContain("facet", name);
                Assert.DoesNotContain("subtract", name);
            }
        }

        private void AssertNothingMutated()
        {
            Assert.Equal(0, _stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(0, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.Equal(0, _characterStore.GetCumulativeAp(_ownerAccount, _ownerCharacter, _stone));
            // No durable journal record was written for a pre-commit rejection.
            Assert.Empty(_receipts.DurableOperationIds());
        }
    }
}
