// ============================================================================
//  Homestead progression — FACET COMMITMENT tests (T010, Tracer 3).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side Facet-commitment slice
//  (link-compiled from ../src): the pure StoneFacets.CommitTreeToFacet
//  transition + the receipt-backed FacetCommandHandler, its engine-free Stone
//  aggregate projection sink, and the hints-only HomesteadProgressionPanel.
//
//  Named acceptance closed here (tasks.md T010 / plan.md Tracer 3):
//    AT-COMMIT-PROFESSION-FACET   commit Cooking into the Profession Facet;
//                                 persists the exact authored choice, receipt-backed.
//    AT-COMMIT-MARTIAL-FACET      commit Warrior into the Martial Facet;
//                                 persists the exact authored choice.
//    AT-COMMIT-STALE              a stale expected Stone revision rejects with no state.
//    AT-FACET-OCCUPIED            committing into an already-occupied Facet rejects.
//    AT-FACET-CATEGORY            committing a Tree of the wrong category rejects.
//    AT-COMMIT-UNAUTHORIZED       a non-Governor (Attunement-only / no relationship /
//                                 hostile principal / outside Responsibility Range) rejects.
//    AT-COMMIT-REPLAY             the same operation returns the recorded result;
//                                 a conflicting reuse of the op id rejects.
//    AT-NO-STONE-LEVEL-MUTATION   commitment changes no Historical/Active Stone Level,
//                                 AP, BP, or purchase state.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimFacetCommitTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:facet-777");
        private readonly StoneId _stone;
        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");   // active Bond
        private readonly CharacterId _attuned = new CharacterId("char-att");    // Attunement only
        private readonly AccountId _accountB = new AccountId("acct-B");
        private readonly CharacterId _charB = new CharacterId("char-B");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();
        private FacetCommandHandler _handler;

        private const string BondRelId = "rel-bond-gov";
        private const string AttRelId = "rel-att";

        public NiflheimFacetCommitTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(),
                "niflheim-t010-facet-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 12, -4);

            // Preconfigured Stone-Level-2 Homestead with empty Facets.
            _stones.PutStone(BuildStone(_stone, revision: 5, activeLevel: 2, committed: null));

            // Governor: an ACTIVE Bond to this Stone (in both the character record and the index).
            _characters.PutCharacter(BuildGovernor(_account, _governor, personalAp: 7, personalBp: 4));
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex(_account, _stone, _governor));

            // Attuned-only sibling would-be actor on account B (no cultivation authority).
            _characters.PutCharacter(BuildAttuned(_accountB, _charB));
            _authority.ApplyAuthorityProjection("seed-att", AttIndex(_accountB, _stone, _charB));

            _handler = NewHandler();
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private FacetCommandHandler NewHandler()
        {
            var resolver = new PrincipalResolver();
            return new FacetCommandHandler(_journalPath, resolver, _stones, _characters, _authority,
                new StubGovernorAuthorityPolicy());
        }

        // ── Fixtures ──

        private static StoneProgressionAggregate BuildStone(StoneId stone, long revision, int activeLevel,
            IReadOnlyList<CommittedTreeRecord>? committed)
        {
            return new StoneProgressionAggregate(stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 9, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: null);
        }

        private static CharacterProgressionAggregate BuildGovernor(AccountId account, CharacterId character,
            int personalAp, int personalBp)
        {
            var bond = new RelationshipRecord(BondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(StoneId.FromHostZone(new WorldId("uid:facet-777"), 12, -4),
                personalAp, personalAp, personalBp, purchases: null,
                relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "facet-777/trailborne",
                revision: 2, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private static CharacterProgressionAggregate BuildAttuned(AccountId account, CharacterId character)
        {
            var att = new RelationshipRecord(AttRelId, RelationshipKind.Attunement, RelationshipStatus.Active,
                string.Empty, string.Empty, "relreceipt:seed-att", string.Empty);
            var stoneRecord = new CharacterStoneRecord(StoneId.FromHostZone(new WorldId("uid:facet-777"), 12, -4),
                1, 1, 0, purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "facet-777/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private static AccountStoneAuthorityIndex BondIndex(AccountId account, StoneId stone, CharacterId who) =>
            AccountStoneAuthorityIndex.Vacant(account, stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Bond, BondRelId, "relreceipt:seed-bond"), 1);

        private static AccountStoneAuthorityIndex AttIndex(AccountId account, StoneId stone, CharacterId who) =>
            AccountStoneAuthorityIndex.Vacant(account, stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Attunement, AttRelId, "relreceipt:seed-att"), 1);

        private CommitTreeToFacetCommand Commit(string op, AccountId account, CharacterId who,
            string facetId, string treeKey, int treeVer, int paletteVer, long? expRev)
            => new CommitTreeToFacetCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                facetId, treeKey, treeVer, paletteVer, expRev);

        private CommitTreeToFacetCommand GovCommit(string op, string facetId, string treeKey, long? expRev = 5)
            => Commit(op, _account, _governor, facetId, treeKey, 1, StoneFacetPalette.CurrentPaletteVersion, expRev);

        private CommittedTreeRecord? CommittedFor(string facetId)
        {
            var stone = _stones.GetStone(_stone)!;
            foreach (var c in stone.CommittedTrees)
                if (c.FacetId == facetId) return c;
            return null;
        }

        // ── AT-COMMIT-PROFESSION-FACET ──
        [Fact]
        public void CommitProfessionFacet_persists_exact_authored_choice()
        {
            var result = _handler.Handle(GovCommit("op-prof", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));

            Assert.Equal(FacetCommandOutcome.Applied, result.Outcome);
            Assert.Equal("Applied", result.ResultCode);
            Assert.NotEqual(string.Empty, result.ReceiptId);

            var committed = CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId);
            Assert.NotNull(committed);
            Assert.Equal("Cooking", committed!.Tree.Key);
            Assert.Equal(1, committed.Tree.Version);
            Assert.Equal(StoneFacets.InitialTreeLevel, committed.TreeLevel);
            Assert.Equal(0, committed.CumulativeBpInvested);
            Assert.Equal("op-prof", committed.CommitOperationId);
            Assert.Equal(_governor.Value, committed.CommitActor);
            // Revision advanced exactly once.
            Assert.Equal(6, _stones.GetStone(_stone)!.Revision);
        }

        // ── AT-COMMIT-MARTIAL-FACET ──
        [Fact]
        public void CommitMartialFacet_persists_exact_authored_choice()
        {
            var result = _handler.Handle(GovCommit("op-mart", HomesteadProgressionCatalog.MartialFacetId, "Warrior"));

            Assert.Equal(FacetCommandOutcome.Applied, result.Outcome);
            var committed = CommittedFor(HomesteadProgressionCatalog.MartialFacetId);
            Assert.NotNull(committed);
            Assert.Equal("Warrior", committed!.Tree.Key);
            Assert.Equal(StoneFacets.InitialTreeLevel, committed.TreeLevel);
            Assert.Equal(0, committed.CumulativeBpInvested);
        }

        // ── AT-COMMIT-PROFESSION-FACET + AT-COMMIT-MARTIAL-FACET together ──
        [Fact]
        public void Commit_both_facets_coexist_independently()
        {
            Assert.Equal(FacetCommandOutcome.Applied,
                _handler.Handle(GovCommit("op-p", HomesteadProgressionCatalog.ProfessionFacetId, "Crafting", 5)).Outcome);
            Assert.Equal(FacetCommandOutcome.Applied,
                _handler.Handle(GovCommit("op-m", HomesteadProgressionCatalog.MartialFacetId, "Archer", 6)).Outcome);

            Assert.Equal("Crafting", CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId)!.Tree.Key);
            Assert.Equal("Archer", CommittedFor(HomesteadProgressionCatalog.MartialFacetId)!.Tree.Key);
            Assert.Equal(2, _stones.GetStone(_stone)!.CommittedTrees.Count);
        }

        // ── AT-COMMIT-STALE ──
        [Fact]
        public void CommitStale_rejects_with_no_state()
        {
            var result = _handler.Handle(GovCommit("op-stale", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking", expRev: 4));

            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("StaleStoneRevision", result.ResultCode);
            Assert.Null(CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId));
            Assert.Equal(5, _stones.GetStone(_stone)!.Revision);
            Assert.False(File.Exists(_journalPath)); // nothing journaled on a pre-transition reject
        }

        // ── AT-FACET-OCCUPIED ──
        [Fact]
        public void FacetOccupied_rejects_second_commit()
        {
            Assert.Equal(FacetCommandOutcome.Applied,
                _handler.Handle(GovCommit("op-first", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking", 5)).Outcome);

            var result = _handler.Handle(GovCommit("op-second", HomesteadProgressionCatalog.ProfessionFacetId, "Crafting", 6));
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("FacetOccupied", result.ResultCode);
            // The original commitment is unchanged.
            Assert.Equal("Cooking", CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId)!.Tree.Key);
            Assert.Single(_stones.GetStone(_stone)!.CommittedTrees);
        }

        // ── AT-FACET-CATEGORY ──
        [Fact]
        public void FacetCategoryMismatch_rejects_wrong_category_tree()
        {
            // Cooking is a Profession Tree; committing it into the Martial Facet is a category mismatch.
            var result = _handler.Handle(GovCommit("op-cat", HomesteadProgressionCatalog.MartialFacetId, "Cooking", 5));
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("FacetCategoryMismatch", result.ResultCode);
            Assert.Null(CommittedFor(HomesteadProgressionCatalog.MartialFacetId));
        }

        [Fact]
        public void IneligibleTree_rejects_unknown_candidate()
        {
            var result = _handler.Handle(GovCommit("op-elig", HomesteadProgressionCatalog.ProfessionFacetId, "NotATree", 5));
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("TreeNotEligible", result.ResultCode);
        }

        [Fact]
        public void StalePaletteVersion_rejects()
        {
            var cmd = Commit("op-pal", _account, _governor, HomesteadProgressionCatalog.ProfessionFacetId,
                "Cooking", 1, StoneFacetPalette.CurrentPaletteVersion + 1, 5);
            var result = _handler.Handle(cmd);
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("ContentVersionMismatch", result.ResultCode);
        }

        [Fact]
        public void StaleTreeVersion_rejects_known_candidate_wrong_version()
        {
            var cmd = Commit("op-tv", _account, _governor, HomesteadProgressionCatalog.ProfessionFacetId,
                "Cooking", 2, StoneFacetPalette.CurrentPaletteVersion, 5);
            var result = _handler.Handle(cmd);
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("ContentVersionMismatch", result.ResultCode);
        }

        // ── AT-COMMIT-UNAUTHORIZED ──
        [Fact]
        public void Unauthorized_attunement_only_actor_rejects()
        {
            var cmd = Commit("op-unauth", _accountB, _charB, HomesteadProgressionCatalog.ProfessionFacetId,
                "Cooking", 1, StoneFacetPalette.CurrentPaletteVersion, 5);
            var result = _handler.Handle(cmd);
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("Unauthorized", result.ResultCode);
            Assert.Null(CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId));
        }

        [Fact]
        public void Unauthorized_hostile_principal_claim_rejects()
        {
            // Authenticated as the attuned actor but CLAIMS to be the governor -> PrincipalMismatch.
            var cmd = new CommitTreeToFacetCommand(new OperationId("op-hostile"), _stone,
                new AuthenticatedConnection(_accountB.Value, _charB.Value),
                new ClaimedPrincipal(_account.Value, _governor.Value),
                HomesteadProgressionCatalog.ProfessionFacetId, "Cooking", 1,
                StoneFacetPalette.CurrentPaletteVersion, 5);
            var result = _handler.Handle(cmd);
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("PrincipalMismatch", result.ResultCode);
        }

        [Fact]
        public void Unauthorized_outside_responsibility_range_rejects()
        {
            // A handler whose Governor policy denies the range: the Bond exists but is not authorized.
            var resolver = new PrincipalResolver();
            var handler = new FacetCommandHandler(_journalPath, resolver, _stones, _characters, _authority,
                new DenyAllGovernorAuthorityPolicy());
            var result = handler.Handle(GovCommit("op-range", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("OutsideResponsibilityRange", result.ResultCode);
            Assert.Null(CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId));
        }

        // ── AT-COMMIT-REPLAY ──
        [Fact]
        public void Replay_same_operation_returns_recorded_result()
        {
            var first = _handler.Handle(GovCommit("op-replay", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Applied, first.Outcome);

            var replay = _handler.Handle(GovCommit("op-replay", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal("Applied", replay.ResultCode);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(first.StoneRevision, replay.StoneRevision);
            // No second commitment / no double revision bump.
            Assert.Single(_stones.GetStone(_stone)!.CommittedTrees);
            Assert.Equal(6, _stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Replay_conflicting_payload_rejects_operation_conflict()
        {
            Assert.Equal(FacetCommandOutcome.Applied,
                _handler.Handle(GovCommit("op-conf", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking")).Outcome);

            // Same op id, DIFFERENT tree -> conflict, not a silent replay.
            var conflict = _handler.Handle(GovCommit("op-conf", HomesteadProgressionCatalog.ProfessionFacetId, "Crafting"));
            Assert.Equal(FacetCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.Equal("Cooking", CommittedFor(HomesteadProgressionCatalog.ProfessionFacetId)!.Tree.Key);
        }

        [Fact]
        public void Replay_survives_restart_from_journal()
        {
            var first = _handler.Handle(GovCommit("op-restart", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Applied, first.Outcome);

            // Simulate a restart: fresh store + fresh handler rehydrating from the same journal.
            var freshStones = new InMemoryStoneAggregateStore();
            freshStones.PutStone(BuildStone(_stone, revision: 5, activeLevel: 2, committed: null));
            var resolver = new PrincipalResolver();
            var rehydrated = new FacetCommandHandler(_journalPath, resolver, freshStones, _characters, _authority,
                new StubGovernorAuthorityPolicy());

            // Projection rebuilt from journal truth: the commitment is present after boot.
            var stone = freshStones.GetStone(_stone)!;
            Assert.Single(stone.CommittedTrees);
            Assert.Equal("Cooking", stone.CommittedTrees[0].Tree.Key);

            // Re-submit is a pure replay, no double-commit.
            var replay = rehydrated.Handle(GovCommit("op-restart", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.StoneRevision, replay.StoneRevision);
        }

        // ── AT-JOURNAL-DELIMITER-SAFE (ADO #127) ──
        // A StoneId is "world|zoneX|zoneZ" by construction (ProgressionIdentity.FromHostZone), so
        // caller-composed operation ids legitimately embed '|'. Writing them raw into the
        // pipe-delimited frame explodes the field count and the strict parser rejects EVERY record —
        // the journal IS the save, so that is total, silent progression loss.
        [Fact]
        public void Commit_with_pipes_in_operation_id_survives_restart_from_journal()
        {
            const string PipedOp = "savor-seam-on-uid:-898655635|3|2";
            var first = _handler.Handle(GovCommit(PipedOp, HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Applied, first.Outcome);

            var freshStones = new InMemoryStoneAggregateStore();
            freshStones.PutStone(BuildStone(_stone, revision: 5, activeLevel: 2, committed: null));
            var rehydrated = new FacetCommandHandler(_journalPath, new PrincipalResolver(), freshStones,
                _characters, _authority, new StubGovernorAuthorityPolicy());

            var stone = freshStones.GetStone(_stone)!;
            Assert.Single(stone.CommittedTrees);
            Assert.Equal("Cooking", stone.CommittedTrees[0].Tree.Key);
            Assert.Equal(PipedOp, stone.CommittedTrees[0].CommitOperationId);

            var replay = rehydrated.Handle(GovCommit(PipedOp, HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.StoneRevision, replay.StoneRevision);
        }

        // ── AT-NO-STONE-LEVEL-MUTATION ──
        [Fact]
        public void Commit_changes_no_stone_level_ap_bp_or_purchase()
        {
            var before = _stones.GetStone(_stone)!;
            var charBefore = _characters.GetCharacter(_account, _governor)!;

            Assert.Equal(FacetCommandOutcome.Applied,
                _handler.Handle(GovCommit("op-nolevel", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking")).Outcome);

            var after = _stones.GetStone(_stone)!;
            // Levels untouched.
            Assert.Equal(before.HistoricalStoneLevel, after.HistoricalStoneLevel);
            Assert.Equal(before.ActiveStoneLevel, after.ActiveStoneLevel);
            // Mirrored AP + foundational identities untouched.
            Assert.Equal(before.MirroredStoneAp, after.MirroredStoneAp);
            Assert.True(before.FoundationalTree.Equals(after.FoundationalTree));
            Assert.True(before.FoundationalCatalog.Equals(after.FoundationalCatalog));
            // Node development untouched (still empty).
            Assert.Equal(before.NodeDevelopment.Count, after.NodeDevelopment.Count);

            // The character aggregate (AP/BP/purchases) is not touched by a Facet commit at all.
            var charAfter = _characters.GetCharacter(_account, _governor)!;
            Assert.True(charBefore.StructurallyEquals(charAfter));
        }

        // ── ActiveStoneLevel capacity gate ──
        [Fact]
        public void ActiveStoneLevelTooLow_rejects_when_stone_below_initial_tree_level()
        {
            var lowStones = new InMemoryStoneAggregateStore();
            lowStones.PutStone(BuildStone(_stone, revision: 5, activeLevel: 0, committed: null));
            var resolver = new PrincipalResolver();
            var handler = new FacetCommandHandler(_journalPath, resolver, lowStones, _characters, _authority,
                new StubGovernorAuthorityPolicy());

            var result = handler.Handle(GovCommit("op-low", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Assert.Equal(FacetCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("ActiveStoneLevelTooLow", result.ResultCode);
        }

        // ── Panel affordances are hints only ──
        [Fact]
        public void Panel_shows_candidate_hints_when_empty_and_commitment_when_occupied()
        {
            var catalog = new HomesteadProgressionCatalog();
            var empty = HomesteadProgressionPanel.Derive(_stones.GetStone(_stone)!, StoneFacetPalette.Current, catalog);
            var profEmpty = empty.FacetFor(HomesteadProgressionCatalog.ProfessionFacetId)!;
            Assert.False(profEmpty.Occupied);
            Assert.Equal(2, profEmpty.CandidateHints.Count); // Cooking + Crafting hints
            Assert.Equal(2, empty.ActiveStoneLevel);

            _handler.Handle(GovCommit("op-panel", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));

            var after = HomesteadProgressionPanel.Derive(_stones.GetStone(_stone)!, StoneFacetPalette.Current, catalog);
            var profOccupied = after.FacetFor(HomesteadProgressionCatalog.ProfessionFacetId)!;
            Assert.True(profOccupied.Occupied);
            Assert.Equal("Cooking", profOccupied.CommittedTree.Key);
            Assert.Empty(profOccupied.CandidateHints); // no hints once occupied
            // The Martial Facet is still empty with its own hints.
            Assert.False(after.FacetFor(HomesteadProgressionCatalog.MartialFacetId)!.Occupied);
        }

        // ── Pure-transition immutability guard ──
        [Fact]
        public void Pure_transition_does_not_mutate_input_aggregate()
        {
            var stone = BuildStone(_stone, revision: 5, activeLevel: 2, committed: null);
            var palette = StoneFacetPalette.Current;
            var catalog = new HomesteadProgressionCatalog();
            var t = StoneFacets.CommitTreeToFacet(stone, palette, catalog,
                HomesteadProgressionCatalog.ProfessionFacetId, new VersionedId("Cooking", 1),
                palette.PaletteVersion, "op", "actor", 5);

            Assert.True(t.Accepted);
            Assert.Empty(stone.CommittedTrees);                // input untouched
            Assert.Single(t.NextStone.CommittedTrees);         // new state carries the commitment
            Assert.Equal(6, t.NextStone.Revision);
        }

        // ── Stubs ──

        private sealed class StubGovernorAuthorityPolicy : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category)
            {
                // Server-authored: an authored Homestead Governor range covering all Facets authorizes
                // any authored Facet/category. An empty range (Attunement) never authorizes.
                return string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                    && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                    && category != FacetCategory.None;
            }
        }

        private sealed class DenyAllGovernorAuthorityPolicy : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) => false;
        }
    }
}
