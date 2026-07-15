// ============================================================================
//  Homestead progression — RELATIONSHIP lifecycle tests (T007, Tracer 2).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side relationship slice (link-compiled
//  from ../src): the pure Relationships transitions + the receipt-backed
//  RelationshipCommandHandler and its authority/character projection sinks.
//
//  Named acceptance closed here (tasks.md T007 / plan.md Tracer 2):
//    AT-BOND                          CreateBond consumes a Bond Slot, occupies the
//                                     account-Stone index, grants no AP/BP, receipt-backed.
//    AT-ATTUNEMENT                    CreateAttunement consumes an Attunement Slot,
//                                     grants no cultivation authority.
//    AT-SIBLING-EXCLUSIVE             a sibling character on the same account cannot
//                                     hold a second active Homestead relationship.
//    AT-SEQUENTIAL-SIBLING            once the active sibling releases, another sibling
//                                     may bond/attune.
//    AT-COMMUNITY-ATTUNEMENT-EXCEPTION  Community Attunement permits siblings; Community
//                                     Bond stays account-exclusive.
//    AT-ATTUNEMENT-RELEASE            release preserves AP/purchases/permanent state and
//                                     dormants supplied Character Effects by re-derivation,
//                                     with no invented refund/cooldown.
//    AT-BOND-RELEASE-DORMANCY         voluntary Bond release preserves BP + Stone-owned
//                                     development, dormants effects, and a later valid Bond
//                                     restores governance (Active re-derives).
//  Plus revision/authority (CAS + hostile-principal) and process-kill recovery.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimRelationshipLifecycleTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:rel-777");
        private readonly StoneId _homestead;
        private readonly StoneId _community;
        private readonly AccountId _account = new AccountId("acct-A");
        private readonly CharacterId _charA1 = new CharacterId("char-A1");
        private readonly CharacterId _charA2 = new CharacterId("char-A2"); // sibling (same account)
        private readonly AccountId _accountB = new AccountId("acct-B");
        private readonly CharacterId _charB1 = new CharacterId("char-B1");

        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();
        private readonly StubFamilyResolver _families = new StubFamilyResolver();
        private RelationshipCommandHandler _handler;

        public NiflheimRelationshipLifecycleTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t007-rel-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _homestead = StoneId.FromHostZone(_world, 12, -4);
            _community = StoneId.FromHostZone(_world, 40, 40);
            _families.Set(_homestead, "Settlement", "Homestead");
            _families.Set(_community, "Community", "Community");

            // Seed characters. Two siblings on account A, one on account B. Each has 1 Bond + 2
            // Attunement slots and some pre-existing per-Stone balances/purchases we assert are preserved.
            _characters.PutCharacter(BuildCharacter(_account, _charA1, personalAp: 5, personalBp: 3, withPurchase: true));
            _characters.PutCharacter(BuildCharacter(_account, _charA2, personalAp: 1, personalBp: 0, withPurchase: false));
            _characters.PutCharacter(BuildCharacter(_accountB, _charB1, personalAp: 2, personalBp: 0, withPurchase: false));

            _handler = NewHandler();
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private RelationshipCommandHandler NewHandler()
        {
            var resolver = new PrincipalResolver(platform => platform);
            return new RelationshipCommandHandler(_journalPath, resolver, _characters, _authority, _families);
        }

        private static CharacterProgressionAggregate BuildCharacter(AccountId account, CharacterId character,
            int personalAp, int personalBp, bool withPurchase, StoneId? stoneOverride = null)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(new VersionedId("Cooking", 1), new VersionedId("SavorTheHearth", 1),
                        "PersonalAP", "CharacterEffect", new VersionedId("OfferedSet-1", 1), "op-purchase-1"),
                }
                : null;
            var stone = stoneOverride ?? StoneId.FromHostZone(new WorldId("uid:rel-777"), 12, -4);
            var stoneRecord = new CharacterStoneRecord(stone, personalAp, personalAp, personalBp,
                facetCredits: null, purchases: purchases);
            return new CharacterProgressionAggregate(account, character,
                worldProductScope: "rel-777/trailborne", revision: 0,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private RelationshipCommand Bond(CharacterId who, StoneId stone, string relId, long? expChar = null, long? expAuth = null)
            => new RelationshipCommand(new OperationId("op-bond-" + relId), RelationshipCommandType.CreateBond, stone,
                new AuthenticatedConnection(_account.Value, who.Value), default,
                relId, responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner",
                expectedCharacterRevision: expChar, expectedAuthorityRevision: expAuth);

        private RelationshipCommand Attune(AccountId account, CharacterId who, StoneId stone, string relId)
            => new RelationshipCommand(new OperationId("op-att-" + account.Value + "-" + relId),
                RelationshipCommandType.CreateAttunement, stone,
                new AuthenticatedConnection(account.Value, who.Value), default, relId);

        private RelationshipCommand Release(CharacterId who, StoneId stone, string relId, string op)
            => new RelationshipCommand(new OperationId(op), RelationshipCommandType.ReleaseRelationship, stone,
                new AuthenticatedConnection(_account.Value, who.Value), default, relId);

        // ── AT-BOND ───────────────────────────────────────────────────────────

        [Fact]
        public void AT_BOND_CreateBond_ConsumesSlot_OccupiesIndex_GrantsNoBalance_ReceiptBacked()
        {
            var before = _characters.GetCharacter(_account, _charA1)!;
            var result = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));

            Assert.Equal(RelationshipCommandOutcome.Applied, result.Outcome);
            Assert.NotEqual("", result.ReceiptId);
            Assert.Equal("rel-bond-1", result.RelationshipId);

            // Index now occupied by this character, kind Bond.
            var idx = _authority.GetAuthority(_account, _homestead);
            Assert.False(idx.IsVacant);
            Assert.Equal(RelationshipKind.Bond, idx.ActiveKind);
            Assert.Equal(_charA1, idx.ActiveCharacter);
            Assert.Equal("rel-bond-1", idx.ActiveRelationshipId);

            // Character record carries an Active Bond with the authored role/range; balances UNCHANGED.
            var after = _characters.GetCharacter(_account, _charA1)!;
            var rec = FindStone(after, _homestead);
            var rel = Assert.Single(rec.Relationships);
            Assert.Equal(RelationshipKind.Bond, rel.Kind);
            Assert.True(rel.IsActive);
            Assert.Equal("Owner", rel.OwnerGovernorRole);
            Assert.Equal("Homestead:All", rel.ResponsibilityRange);
            Assert.Equal(FindStone(before, _homestead).PersonalAp, rec.PersonalAp);
            Assert.Equal(FindStone(before, _homestead).PersonalBp, rec.PersonalBp);
        }

        [Fact]
        public void AT_BOND_SecondBondBeyondSlotCapacity_Rejected()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            // Release then re-bond would be sequential; but a same-character second active bond while
            // already active is a conflict (only 1 bond slot).
            var second = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-2"));
            Assert.Equal(RelationshipCommandOutcome.Rejected, second.Outcome);
            Assert.Equal("RelationshipConflict", second.ResultCode);
        }

        // ── AT-ATTUNEMENT ───────────────────────────────────────────────────────

        [Fact]
        public void AT_ATTUNEMENT_CreateAttunement_ConsumesSlot_NoCultivationAuthority()
        {
            var result = _handler.Handle(Attune(_accountB, _charB1, _homestead, "rel-att-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, result.Outcome);

            var idx = _authority.GetAuthority(_accountB, _homestead);
            Assert.Equal(RelationshipKind.Attunement, idx.ActiveKind);

            var after = _characters.GetCharacter(_accountB, _charB1)!;
            var rel = Assert.Single(FindStone(after, _homestead).Relationships);
            Assert.Equal(RelationshipKind.Attunement, rel.Kind);
            // No cultivation authority: empty role and empty Responsibility Range.
            Assert.Equal("", rel.OwnerGovernorRole);
            Assert.Equal("", rel.ResponsibilityRange);
        }

        // ── AT-SIBLING-EXCLUSIVE ────────────────────────────────────────────────

        [Fact]
        public void AT_SIBLING_EXCLUSIVE_SecondSiblingOnSameAccount_RejectedNoMutation()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));

            // Sibling A2 (same account) tries to attune the same Homestead -> SiblingCharacterActive.
            var siblingAttempt = _handler.Handle(Attune(_account, _charA2, _homestead, "rel-att-sib"));
            Assert.Equal(RelationshipCommandOutcome.Rejected, siblingAttempt.Outcome);
            Assert.Equal("SiblingCharacterActive", siblingAttempt.ResultCode);

            // No mutation: the index still points at A1's Bond, and A2 has no relationship.
            var idx = _authority.GetAuthority(_account, _homestead);
            Assert.Equal(_charA1, idx.ActiveCharacter);
            var a2 = _characters.GetCharacter(_account, _charA2)!;
            Assert.Empty(FindStone(a2, _homestead).Relationships);
        }

        // ── AT-SEQUENTIAL-SIBLING ───────────────────────────────────────────────

        [Fact]
        public void AT_SEQUENTIAL_SIBLING_AfterRelease_AnotherSiblingMayBond()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            var rel = _handler.Handle(Release(_charA1, _homestead, "rel-bond-1", "op-rel-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, rel.Outcome);

            // Index vacant -> sibling A2 may now bond.
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);
            var a2Bond = _handler.Handle(Bond(_charA2, _homestead, "rel-bond-a2"));
            Assert.Equal(RelationshipCommandOutcome.Applied, a2Bond.Outcome);
            Assert.Equal(_charA2, _authority.GetAuthority(_account, _homestead).ActiveCharacter);
        }

        // ── AT-COMMUNITY-ATTUNEMENT-EXCEPTION ───────────────────────────────────

        [Fact]
        public void AT_COMMUNITY_ATTUNEMENT_EXCEPTION_SiblingAttunementAllowed_ButSiblingBondExclusive()
        {
            // Seed both siblings with a record at the Community stone.
            _characters.PutCharacter(BuildCharacter(_account, _charA1, 5, 3, true, _community));
            _characters.PutCharacter(BuildCharacter(_account, _charA2, 1, 0, false, _community));
            var h = NewHandler();

            // A1 attunes the Community stone.
            var first = h.Handle(Attune(_account, _charA1, _community, "rel-catt-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, first.Outcome);

            // Sibling A2 ALSO attunes -> permitted by variant-authored exception.
            var sibling = h.Handle(Attune(_account, _charA2, _community, "rel-catt-2"));
            Assert.Equal(RelationshipCommandOutcome.Applied, sibling.Outcome);

            // But a Community BOND stays account-exclusive: with a sibling (A2) holding the index,
            // A1 attempting a Community Bond is rejected SiblingCharacterActive.
            var siblingBond = h.Handle(new RelationshipCommand(new OperationId("op-cbond"),
                RelationshipCommandType.CreateBond, _community,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-cbond",
                responsibilityRange: "Community:All", ownerGovernorRole: "Owner"));
            Assert.Equal("SiblingCharacterActive", siblingBond.ResultCode);
        }

        // ── AT-ATTUNEMENT-RELEASE ───────────────────────────────────────────────

        [Fact]
        public void AT_ATTUNEMENT_RELEASE_PreservesStateAndDormantsEffectsByReDerivation_NoRefund()
        {
            // A1 (has a purchase + AP + BP) attunes then releases the Homestead.
            _handler.Handle(Attune(_account, _charA1, _homestead, "rel-att-1"));
            var beforeRelease = _characters.GetCharacter(_account, _charA1)!;
            var beforeStone = FindStone(beforeRelease, _homestead);

            var rel = _handler.Handle(Release(_charA1, _homestead, "rel-att-1", "op-att-rel"));
            Assert.Equal(RelationshipCommandOutcome.Applied, rel.Outcome);

            var after = _characters.GetCharacter(_account, _charA1)!;
            var afterStone = FindStone(after, _homestead);

            // Preserved verbatim: AP, cumulative AP, BP, purchases (no invented refund/cooldown).
            Assert.Equal(beforeStone.PersonalAp, afterStone.PersonalAp);
            Assert.Equal(beforeStone.CumulativeAp, afterStone.CumulativeAp);
            Assert.Equal(beforeStone.PersonalBp, afterStone.PersonalBp);
            Assert.Equal(beforeStone.Purchases.Count, afterStone.Purchases.Count);

            // Relationship record persists but is Released; the index is now vacant.
            var relRec = Assert.Single(afterStone.Relationships);
            Assert.Equal(RelationshipStatus.Released, relRec.Status);
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);

            // The purchased Character Effect is now DORMANT purely by re-derivation (vacant index).
            var view = DeriveView(_account, _charA1, _homestead);
            var savor = FindNode(view, "SavorTheHearth");
            Assert.Equal(DerivedNodeState.Dormant, savor.State);
            Assert.False(savor.Active);
            Assert.True(savor.Purchased); // purchase record preserved
        }

        // ── AT-BOND-RELEASE-DORMANCY ────────────────────────────────────────────

        [Fact]
        public void AT_BOND_RELEASE_DORMANCY_PreservesBpAndDevelopment_LaterBondRestoresGovernance()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            var afterBond = _characters.GetCharacter(_account, _charA1)!;

            // Effect active while bonded.
            var boundView = DeriveView(_account, _charA1, _homestead);
            Assert.Equal(DerivedNodeState.Active, FindNode(boundView, "SavorTheHearth").State);

            // Voluntary release: BP + purchases preserved, effect dormant, no refund.
            _handler.Handle(Release(_charA1, _homestead, "rel-bond-1", "op-bond-rel"));
            var released = _characters.GetCharacter(_account, _charA1)!;
            Assert.Equal(FindStone(afterBond, _homestead).PersonalBp, FindStone(released, _homestead).PersonalBp);
            Assert.Equal(DerivedNodeState.Dormant, FindNode(DeriveView(_account, _charA1, _homestead), "SavorTheHearth").State);

            // A LATER valid Bond restores eligible governance: the same preserved purchase re-derives Active.
            var rebond = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-2"));
            Assert.Equal(RelationshipCommandOutcome.Applied, rebond.Outcome);
            Assert.Equal(DerivedNodeState.Active, FindNode(DeriveView(_account, _charA1, _homestead), "SavorTheHearth").State);
        }

        // ── capacity / authority revision (CAS + hostile principal) ─────────────

        [Fact]
        public void StaleAuthorityRevision_LosingConcurrentClient_RejectedNoMutation()
        {
            // Correct current authority revision is 0; supply a stale expectation.
            var cmd = new RelationshipCommand(new OperationId("op-cas"), RelationshipCommandType.CreateBond,
                _homestead, new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-cas",
                responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner",
                expectedAuthorityRevision: 99);
            var result = _handler.Handle(cmd);
            Assert.Equal("StaleAuthorityRevision", result.ResultCode);
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);
        }

        [Fact]
        public void HostilePrincipal_ClaimMismatch_Rejected()
        {
            var cmd = new RelationshipCommand(new OperationId("op-hostile"), RelationshipCommandType.CreateBond,
                _homestead, new AuthenticatedConnection(_account.Value, _charA1.Value),
                new ClaimedPrincipal("acct-B", null), "rel-hostile",
                responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner");
            var result = _handler.Handle(cmd);
            Assert.Equal("PrincipalMismatch", result.ResultCode);
        }

        // ── recovery: idempotent replay + simulated restart ─────────────────────

        [Fact]
        public void Replay_SameOperation_ReturnsRecordedResult_NoDuplicate()
        {
            var first = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            var replay = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(first.AuthorityRevision, replay.AuthorityRevision);
            // Still exactly one active relationship record.
            var after = _characters.GetCharacter(_account, _charA1)!;
            Assert.Single(FindStone(after, _homestead).Relationships);
        }

        [Fact]
        public void ConflictingBindingUnderCommittedOp_RejectedOperationConflict()
        {
            var op = new OperationId("op-shared");
            _handler.Handle(new RelationshipCommand(op, RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-x",
                responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner"));
            // Same operationId, DIFFERENT relationshipId -> conflict.
            var conflict = _handler.Handle(new RelationshipCommand(op, RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-y",
                responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner"));
            Assert.Equal("OperationConflict", conflict.ResultCode);
        }

        [Fact]
        public void SimulatedRestart_RehydratesCommittedRelationshipFromJournal()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));

            // Fresh stores + fresh handler over the SAME journal == restarted process for replay.
            var characters2 = new InMemoryCharacterAggregateStore();
            characters2.PutCharacter(BuildCharacter(_account, _charA1, 5, 3, true)); // clean seed
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            var resolver = new PrincipalResolver(platform => platform);
            var handler2 = new RelationshipCommandHandler(_journalPath, resolver, characters2, authority2, _families);

            // Rehydrated: the index and the character relationship are restored from journal truth.
            var idx = authority2.GetAuthority(_account, _homestead);
            Assert.False(idx.IsVacant);
            Assert.Equal(_charA1, idx.ActiveCharacter);
            var rel = Assert.Single(FindStone(characters2.GetCharacter(_account, _charA1)!, _homestead).Relationships);
            Assert.True(rel.IsActive);

            // Re-submitting the same op after restart is a pure replay.
            var replay = handler2.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private static CharacterStoneRecord FindStone(CharacterProgressionAggregate c, StoneId s)
        {
            foreach (var sr in c.StoneRecords) if (sr.StoneId.Equals(s)) return sr;
            throw new KeyNotFoundException("no stone record");
        }

        private DerivedActivationView DeriveView(AccountId account, CharacterId character, StoneId stone)
        {
            var c = _characters.GetCharacter(account, character)!;
            var idx = _authority.GetAuthority(account, stone);
            var stoneAgg = BuildStoneWithDevelopedSavor(stone);
            return DerivedActivationView.Derive(stoneAgg, c, idx);
        }

        private static StoneProgressionAggregate BuildStoneWithDevelopedSavor(StoneId stone)
        {
            // The Stone has SavorTheHearth developed so a purchase can derive Active/Dormant.
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("SavorTheHearth", 1), 10, 10, developed: true, offered: false, "op-dev"),
            };
            return new StoneProgressionAggregate(stone, revision: 3, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r", committedTrees: null, nodeDevelopment: nodes);
        }

        private static DerivedNodeStatus FindNode(DerivedActivationView view, string key)
        {
            foreach (var n in view.Nodes) if (n.Node.Key == key) return n;
            throw new KeyNotFoundException("no node " + key);
        }

        private sealed class StubFamilyResolver : IStoneFamilyResolver
        {
            private readonly Dictionary<string, (string family, string variant)> _map =
                new Dictionary<string, (string, string)>(System.StringComparer.Ordinal);
            public void Set(StoneId stone, string family, string variant) => _map[stone.Value] = (family, variant);
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (_map.TryGetValue(stoneId.Value, out var v)) { family = v.family; variant = v.variant; return true; }
                family = variant = string.Empty; return false;
            }
        }
    }
}
