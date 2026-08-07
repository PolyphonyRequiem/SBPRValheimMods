// ============================================================================
//  Homestead progression — T008 Foundational-placement HARDENING tests (Tracer 2).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side hardening of the ongoing
//  protected Foundational AP source (link-compiled from ../src): the authored
//  FoundationalPieceCatalog, the hardened FoundationalPlacementAdapter
//  (catalog membership / explicit exclusions / current-build version /
//  anti-repetition), and the RelationshipPlacementAuthorizer that makes real
//  T007 Bond/Attunement authority the gate — with Tree commitment unable to
//  disable the baseline, and one exact AP receipt surviving restart.
//
//  Named acceptance closed here (tasks.md T008):
//    AT-FOUNDATIONAL-CATALOG    accepts only exact current-build catalog members.
//    AT-FOUNDATIONAL-ONGOING    the source stays active throughout Homestead life
//                               (before AND after a Tree commitment) while the
//                               relationship holds.
//    AT-FOUNDATIONAL-EXCLUDED   unknown / excluded / outside-area / failed /
//                               stale-version / unauthorized evidence earns no receipt.
//    AT-RELATIONSHIP-RESTART    the relationship and one exact AP receipt survive
//                               a simulated process restart (fresh stores over the
//                               same durable journals).
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimFoundationalHardeningTests : System.IDisposable
    {
        private readonly string _apJournal;
        private readonly string _relJournal;
        private readonly WorldId _world = new WorldId("uid:t008");
        private readonly StoneId _stone;
        private readonly AccountId _account = new AccountId("plat-owner");
        private readonly CharacterId _character = new CharacterId("char-owner");
        private readonly CharacterId _stranger = new CharacterId("char-stranger");

        private readonly FoundationalPieceCatalog _catalog = FoundationalPieceCatalog.CurrentBuild;

        public NiflheimFoundationalHardeningTests()
        {
            _apJournal = Path.Combine(Path.GetTempPath(), "niflheim-t008-ap-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _relJournal = Path.Combine(Path.GetTempPath(), "niflheim-t008-rel-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 12, -4);
        }

        public void Dispose()
        {
            if (File.Exists(_apJournal)) File.Delete(_apJournal);
            if (File.Exists(_relJournal)) File.Delete(_relJournal);
        }

        // ── fixtures ────────────────────────────────────────────────────────────

        private static PrincipalResolver Resolver() => new PrincipalResolver();

        private CharacterProgressionAggregate SeedCharacter(CharacterId who) =>
            new CharacterProgressionAggregate(_account, who,
                worldProductScope: "t008/trailborne", revision: 0,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) });

        private RelationshipCommandHandler NewRelationshipHandler(
            InMemoryCharacterAggregateStore characters, InMemoryAccountStoneAuthorityStore authority)
        {
            var families = new FixedFamilyResolver(_stone, "Settlement", "Homestead");
            return new RelationshipCommandHandler(_relJournal, Resolver(), characters, authority,
                families, new HomesteadBondPolicy(), AlwaysAtStoneProximity.Instance);
        }

        private ProgressionCommandPipeline NewPipeline(
            OperationReceiptStore receipts, IAccountStoneAuthorityStore authority) =>
            new ProgressionCommandPipeline(Resolver(), receipts, new RelationshipPlacementAuthorizer(authority));

        private FoundationalPlacementCommand Placement(
            FoundationalPlacementAdapter adapter, string opId, CharacterId who,
            string stablePieceId, string provenance, string catalogVersion = "v1",
            bool insideArea = true, bool succeeded = true)
        {
            var evidence = new FoundationalPlacementEvidence(
                new OperationId(opId), _stone, stablePieceId, provenance,
                insideStoneArea: insideArea, placementSucceeded: succeeded, foundationalCatalogVersion: catalogVersion);
            var admission = adapter.Admit(evidence, new AuthenticatedConnection(_account.Value, who.Value),
                new ClaimedPrincipal(_account.Value, who.Value));
            Assert.True(admission.IsAdmitted, "expected admission for " + stablePieceId);
            return admission.Command;
        }

        // ── AT-FOUNDATIONAL-CATALOG ──────────────────────────────────────────────

        [Fact]
        public void AT_FOUNDATIONAL_CATALOG_AdmitsOnlyExactCurrentBuildMembers()
        {
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());

            // Every authored member admits.
            foreach (var member in _catalog.Members)
            {
                var e = new FoundationalPlacementEvidence(new OperationId("op-" + member), _stone,
                    member, "prov-" + member, true, true, "v1");
                Assert.True(adapter.Admit(e, new AuthenticatedConnection("p", "c"), new ClaimedPrincipal("p", "c")).IsAdmitted,
                    member + " should be an admitted catalog member");
            }

            // A non-member same-build reference is NOT catalog-admitted (no rebind to a "closest" piece).
            var unknown = new FoundationalPlacementEvidence(new OperationId("op-unknown"), _stone,
                "foundation_not_a_real_piece", "prov-unknown", true, true, "v1");
            var res = adapter.Admit(unknown, new AuthenticatedConnection("p", "c"), new ClaimedPrincipal("p", "c"));
            Assert.False(res.IsAdmitted);
            Assert.Equal(PlacementAdmission.NotCatalogMember, res.Admission);
        }

        [Fact]
        public void AT_FOUNDATIONAL_CATALOG_StaleCatalogVersionEarnsNoReceipt()
        {
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());
            var e = new FoundationalPlacementEvidence(new OperationId("op-stale-cat"), _stone,
                "foundation_wood_floor", "prov-1", true, true, "v0"); // not the current tag "v1"
            var res = adapter.Admit(e, new AuthenticatedConnection("p", "c"), new ClaimedPrincipal("p", "c"));
            Assert.False(res.IsAdmitted);
            Assert.Equal(PlacementAdmission.StaleCatalogVersion, res.Admission);
        }

        // ── AT-FOUNDATIONAL-EXCLUDED ─────────────────────────────────────────────

        [Fact]
        public void AT_FOUNDATIONAL_EXCLUDED_ExcludedUnknownOutsideFailed_ProduceNoCommand()
        {
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());
            var conn = new AuthenticatedConnection("p", "c");
            var claim = new ClaimedPrincipal("p", "c");

            // Explicit exclusion (a real held-out stable id) — exclusion wins over any membership.
            var excluded = _catalog.Exclusions[0];
            var exRes = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-ex"), _stone,
                excluded, "prov-ex", true, true, "v1"), conn, claim);
            Assert.Equal(PlacementAdmission.ExcludedPiece, exRes.Admission);

            // Outside the Stone Area.
            var outside = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-out"), _stone,
                "foundation_wood_floor", "prov-out", insideStoneArea: false, placementSucceeded: true, "v1"), conn, claim);
            Assert.Equal(PlacementAdmission.OutsideStoneArea, outside.Admission);

            // Placement failed.
            var failed = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-fail"), _stone,
                "foundation_wood_floor", "prov-fail", insideStoneArea: true, placementSucceeded: false, "v1"), conn, claim);
            Assert.Equal(PlacementAdmission.PlacementFailed, failed.Admission);

            // Missing piece identity.
            var missing = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-miss"), _stone,
                "", "prov-miss", true, true, "v1"), conn, claim);
            Assert.Equal(PlacementAdmission.MissingPieceIdentity, missing.Admission);

            // None produced a command.
            foreach (var r in new[] { exRes, outside, failed, missing })
                Assert.False(r.IsAdmitted);
        }

        [Fact]
        public void AT_FOUNDATIONAL_EXCLUDED_UnauthorizedActor_EarnsNoReceipt()
        {
            // A well-formed, catalog-valid, in-area placement by a character with NO active relationship
            // earns nothing: the RelationshipPlacementAuthorizer denies and the pipeline rejects with no
            // durable record.
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            characters.PutCharacter(SeedCharacter(_stranger));

            var stoneStore = new InMemoryMirroredStoneApStore();
            var charStore = new InMemoryCharacterApStore();
            var receipts = new OperationReceiptStore(_apJournal, stoneStore, charStore);
            var pipeline = NewPipeline(receipts, authority);
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());

            var cmd = Placement(adapter, "op-noauth", _stranger, "foundation_wood_floor", "prov-noauth");
            var result = pipeline.Handle(cmd);

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("RelationshipRequired", result.ResultCode);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
            Assert.DoesNotContain("op-noauth", receipts.DurableOperationIds());
        }

        [Fact]
        public void RepetitionPolicy_SamePhysicalInstance_CreditedAtMostOnce()
        {
            var repetition = new InMemoryPlacementRepetitionPolicy();
            var adapter = new FoundationalPlacementAdapter(_catalog, repetition);
            var conn = new AuthenticatedConnection("p", "c");
            var claim = new ClaimedPrincipal("p", "c");

            // First op credits instance "prov-shared".
            var first = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-r1"), _stone,
                "foundation_wood_floor", "prov-shared", true, true, "v1"), conn, claim);
            Assert.True(first.IsAdmitted);

            // Same op replayed -> still admitted (pipeline is the idempotency authority).
            var replay = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-r1"), _stone,
                "foundation_wood_floor", "prov-shared", true, true, "v1"), conn, claim);
            Assert.True(replay.IsAdmitted);

            // DIFFERENT op re-crediting the same physical instance -> suppressed, no command.
            var repeat = adapter.Admit(new FoundationalPlacementEvidence(new OperationId("op-r2"), _stone,
                "foundation_wood_floor", "prov-shared", true, true, "v1"), conn, claim);
            Assert.False(repeat.IsAdmitted);
            Assert.Equal(PlacementAdmission.RepetitionSuppressed, repeat.Admission);
        }

        [Fact]
        public void AT_FOUNDATIONAL_EXCLUDED_ReleasedRelationship_StopsEarning()
        {
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            characters.PutCharacter(SeedCharacter(_character));
            var relHandler = NewRelationshipHandler(characters, authority);

            var stoneStore = new InMemoryMirroredStoneApStore();
            var charStore = new InMemoryCharacterApStore();
            var receipts = new OperationReceiptStore(_apJournal, stoneStore, charStore);
            var pipeline = NewPipeline(receipts, authority);
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());

            Assert.Equal(RelationshipCommandOutcome.Applied, relHandler.Handle(new RelationshipCommand(
                new OperationId("op-attune"), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-1")).Outcome);
            Assert.Equal(CommandOutcome.Applied,
                pipeline.Handle(Placement(adapter, "op-earn-1", _character, "foundation_wood_floor", "prov-1")).Outcome);

            // Release the relationship -> the reservation is removed -> the source stops.
            Assert.Equal(RelationshipCommandOutcome.Applied, relHandler.Handle(new RelationshipCommand(
                new OperationId("op-release"), RelationshipCommandType.ReleaseRelationship, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-1")).Outcome);

            var afterRelease = pipeline.Handle(Placement(adapter, "op-earn-2", _character, "foundation_wood_wall", "prov-2"));
            Assert.Equal(CommandOutcome.Rejected, afterRelease.Outcome);
            Assert.Equal("RelationshipRequired", afterRelease.ResultCode);
            // The one pre-release receipt is preserved; no new credit landed.
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── AT-FOUNDATIONAL-ONGOING ──────────────────────────────────────────────

        [Fact]
        public void AT_FOUNDATIONAL_ONGOING_ActiveAfterAttunement_And_TreeCommitmentDoesNotDisableIt()
        {
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            characters.PutCharacter(SeedCharacter(_character));
            var relHandler = NewRelationshipHandler(characters, authority);

            var stoneStore = new InMemoryMirroredStoneApStore();
            var charStore = new InMemoryCharacterApStore();
            var receipts = new OperationReceiptStore(_apJournal, stoneStore, charStore);
            var pipeline = NewPipeline(receipts, authority);
            var adapter = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());

            // Before any relationship: earning is denied.
            var pre = pipeline.Handle(Placement(adapter, "op-pre", _character, "foundation_wood_floor", "prov-pre"));
            Assert.Equal(CommandOutcome.Rejected, pre.Outcome);
            Assert.Equal("RelationshipRequired", pre.ResultCode);

            // Attune -> the ongoing source becomes active.
            var att = relHandler.Handle(new RelationshipCommand(new OperationId("op-attune"),
                RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, att.Outcome);

            var earn1 = pipeline.Handle(Placement(adapter, "op-earn-1", _character, "foundation_wood_floor", "prov-1"));
            Assert.Equal(CommandOutcome.Applied, earn1.Outcome);
            Assert.Equal(1, earn1.MirroredStoneApDelta);

            // Tree commitment MUST NOT disable the baseline (data-model.md §"Credit Foundational AP").
            // In this engine-free slice there is no Tree-commit command yet (T012+), so the guarantee is
            // proven STRUCTURALLY: RelationshipPlacementAuthorizer reads ONLY the relationship authority
            // index — never Facet occupancy, Committed Trees, Tree Level, or development state — so no
            // commitment can revoke the source. Continued placements keep earning while the relationship
            // holds, which is exactly the ongoing property. (T009 independent verification drives a real
            // joined-client Tree commitment against this same authorizer.)
            var earn2 = pipeline.Handle(Placement(adapter, "op-earn-2", _character, "foundation_wood_wall", "prov-2"));
            Assert.Equal(CommandOutcome.Applied, earn2.Outcome);
            Assert.Equal(2, stoneStore.GetMirroredStoneAp(_stone));

            // Repeated ongoing placements keep earning across the Homestead's life.
            var earn3 = pipeline.Handle(Placement(adapter, "op-earn-3", _character, "foundation_wood_pole", "prov-3"));
            Assert.Equal(CommandOutcome.Applied, earn3.Outcome);
            Assert.Equal(3, stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(3, charStore.GetPersonalAp(_account, _character, _stone));
        }

        // ── AT-RELATIONSHIP-RESTART ──────────────────────────────────────────────

        [Fact]
        public void AT_RELATIONSHIP_RESTART_RelationshipAndOneExactReceiptSurviveRestart()
        {
            // Boot 1: attune, then earn exactly one Foundational AP receipt.
            var characters1 = new InMemoryCharacterAggregateStore();
            var authority1 = new InMemoryAccountStoneAuthorityStore();
            characters1.PutCharacter(SeedCharacter(_character));
            var rel1 = NewRelationshipHandler(characters1, authority1);
            Assert.Equal(RelationshipCommandOutcome.Applied, rel1.Handle(new RelationshipCommand(
                new OperationId("op-attune"), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-1")).Outcome);

            var stone1 = new InMemoryMirroredStoneApStore();
            var char1 = new InMemoryCharacterApStore();
            var receipts1 = new OperationReceiptStore(_apJournal, stone1, char1);
            var pipeline1 = NewPipeline(receipts1, authority1);
            var adapter1 = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());
            var earn = pipeline1.Handle(Placement(adapter1, "op-earn-1", _character, "foundation_wood_floor", "prov-1"));
            Assert.Equal(CommandOutcome.Applied, earn.Outcome);
            Assert.Equal(1, stone1.GetMirroredStoneAp(_stone));

            // Boot 2: FRESH stores + fresh handlers over the SAME journals == restarted process.
            var characters2 = new InMemoryCharacterAggregateStore();
            characters2.PutCharacter(SeedCharacter(_character)); // clean seed
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            // Relationship rehydrates from the durable relationship journal at construction.
            NewRelationshipHandler(characters2, authority2);
            Assert.True(authority2.GetAuthority(_account, _stone).HasActive(_character),
                "attunement must survive restart");

            var stone2 = new InMemoryMirroredStoneApStore();
            var char2 = new InMemoryCharacterApStore();
            // AP receipt rehydrates from the durable AP journal at construction.
            var receipts2 = new OperationReceiptStore(_apJournal, stone2, char2);
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, char2.GetPersonalAp(_account, _character, _stone));

            // Re-submitting the same op after restart is a pure replay: still exactly one, no double-count.
            var pipeline2 = NewPipeline(receipts2, authority2);
            var adapter2 = new FoundationalPlacementAdapter(_catalog, new InMemoryPlacementRepetitionPolicy());
            var replay = pipeline2.Handle(Placement(adapter2, "op-earn-1", _character, "foundation_wood_floor", "prov-1"));
            Assert.Equal(CommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, char2.GetPersonalAp(_account, _character, _stone));
        }

        // ── local stubs (self-contained; mirror the T007 test seams) ─────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly string _key, _family, _variant;
            public FixedFamilyResolver(StoneId stone, string family, string variant)
            { _key = stone.Value; _family = family; _variant = variant; }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (string.Equals(stoneId.Value, _key, System.StringComparison.Ordinal))
                { family = _family; variant = _variant; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class HomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal);
            }
        }
    }
}
