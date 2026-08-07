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
            var resolver = new PrincipalResolver();
            return new RelationshipCommandHandler(_journalPath, resolver, _characters, _authority,
                _families, new StubBondAuthorityPolicy());
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
                purchases: purchases);
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
            var idxRes = Assert.Single(idx.Reservations);
            Assert.Equal(RelationshipKind.Bond, idxRes.Kind);
            Assert.Equal(_charA1, idxRes.Character);
            Assert.Equal("rel-bond-1", idxRes.RelationshipId);

            // Character record carries an Active Bond with the authored role/range; balances UNCHANGED.
            var after = _characters.GetCharacter(_account, _charA1)!;
            var rec = FindStone(after, _homestead);
            var rel = Assert.Single(rec.Relationships);
            Assert.Equal(RelationshipKind.Bond, rel.Kind);
            Assert.True(rel.IsActive);
            // Server-authored role (never client "Owner"): the Bond authority policy stamps "Governor".
            Assert.Equal("Governor", rel.OwnerGovernorRole);
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
            Assert.Equal(RelationshipKind.Attunement, Assert.Single(idx.Reservations).Kind);

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
            Assert.Equal(_charA1, Assert.Single(idx.Reservations).Character);
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
            Assert.Equal(_charA2, Assert.Single(_authority.GetAuthority(_account, _homestead).Reservations).Character);
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
            var resolver = new PrincipalResolver();
            var handler2 = new RelationshipCommandHandler(_journalPath, resolver, characters2, authority2,
                _families, new StubBondAuthorityPolicy());

            // Rehydrated: the index and the character relationship are restored from journal truth.
            var idx = authority2.GetAuthority(_account, _homestead);
            Assert.False(idx.IsVacant);
            Assert.Equal(_charA1, Assert.Single(idx.Reservations).Character);
            var rel = Assert.Single(FindStone(characters2.GetCharacter(_account, _charA1)!, _homestead).Relationships);
            Assert.True(rel.IsActive);

            // Re-submitting the same op after restart is a pure replay.
            var replay = handler2.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
        }

        // ── AT-COMMUNITY-ATTUNEMENT-EXCEPTION (multi-active, design call 2026-07-15) ──

        [Fact]
        public void Community_SiblingAttunements_AreSimultaneouslyActive_ReleaseRemovesOnlyThatSibling()
        {
            _characters.PutCharacter(BuildCharacter(_account, _charA1, 5, 3, true, _community));
            _characters.PutCharacter(BuildCharacter(_account, _charA2, 1, 0, false, _community));
            var h = NewHandler();

            Assert.Equal(RelationshipCommandOutcome.Applied, h.Handle(Attune(_account, _charA1, _community, "rel-catt-1")).Outcome);
            Assert.Equal(RelationshipCommandOutcome.Applied, h.Handle(Attune(_account, _charA2, _community, "rel-catt-2")).Outcome);

            // Both siblings hold a SIMULTANEOUS reservation in the one authoritative index — not one
            // overwriting the other (the pre-fix bug). The index carries two entries.
            var idx = _authority.GetAuthority(_account, _community);
            Assert.Equal(2, idx.Reservations.Count);
            Assert.True(idx.HasActive(_charA1));
            Assert.True(idx.HasActive(_charA2));

            // Derived activation confirms A1 is active (not dormant) even while sibling A2 is also active.
            var a1View = DeriveView(_account, _charA1, _community);
            Assert.Equal(DerivedNodeState.Active, FindNode(a1View, "SavorTheHearth").State);

            // Release A1: removes ONLY A1's reservation; A2 stays active.
            Assert.Equal(RelationshipCommandOutcome.Applied,
                h.Handle(Release(_charA1, _community, "rel-catt-1", "op-catt-rel-1")).Outcome);
            var afterIdx = _authority.GetAuthority(_account, _community);
            Assert.False(afterIdx.HasActive(_charA1));
            Assert.True(afterIdx.HasActive(_charA2));
        }

        [Fact]
        public void Community_ExistingSiblingBond_BlocksLaterAttunement()
        {
            _characters.PutCharacter(BuildCharacter(_account, _charA1, 5, 3, true, _community));
            _characters.PutCharacter(BuildCharacter(_account, _charA2, 1, 0, false, _community));
            var h = NewHandler();

            Assert.Equal(RelationshipCommandOutcome.Applied,
                h.Handle(Bond(_charA1, _community, "rel-cbond-first")).Outcome);
            var siblingAttunement = h.Handle(Attune(_account, _charA2, _community, "rel-catt-second"));

            Assert.Equal(RelationshipCommandOutcome.Rejected, siblingAttunement.Outcome);
            Assert.Equal("SiblingCharacterActive", siblingAttunement.ResultCode);
            var idx = _authority.GetAuthority(_account, _community);
            Assert.Equal(_charA1, Assert.Single(idx.Reservations).Character);
        }

        // ── character-wide slot scarcity (FR-003; defect: was per-Stone) ─────────

        [Fact]
        public void CharacterWideSlots_SingleBondSlot_CannotBondASecondStone()
        {
            // A1 has 1 Bond Slot. It bonds the Homestead, then attempts to bond a SECOND Stone. Because
            // Bond Slots are character-wide, the second bond exceeds capacity even though the second
            // Stone's own index is vacant.
            var stone2 = StoneId.FromHostZone(_world, 88, 88);
            _families.Set(stone2, "Settlement", "Homestead");
            // Give A1 a record at stone2 as well so it has somewhere to place the bond.
            var a1 = _characters.GetCharacter(_account, _charA1)!;
            var records = new List<CharacterStoneRecord>(a1.StoneRecords)
            {
                new CharacterStoneRecord(stone2, 0, 0, 0)
            };
            _characters.PutCharacter(new CharacterProgressionAggregate(_account, _charA1,
                a1.WorldProductScope, a1.Revision, a1.BondSlots, a1.AttunementSlots,
                a1.LastAppliedReceiptId, records));
            var h = NewHandler();

            Assert.Equal(RelationshipCommandOutcome.Applied, h.Handle(Bond(_charA1, _homestead, "rel-b1")).Outcome);
            var second = h.Handle(Bond(_charA1, stone2, "rel-b2"));
            Assert.Equal(RelationshipCommandOutcome.Rejected, second.Outcome);
            Assert.Equal("RelationshipCapacityExceeded", second.ResultCode);
        }

        // ── replay must not roll a newer projection backward (defect 3) ──────────

        [Fact]
        public void ReplayOfOlderOp_AfterRelease_DoesNotRestorePreReleaseState()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));       // op1: index active
            _handler.Handle(Release(_charA1, _homestead, "rel-bond-1", "op-rel-1")); // op2: index vacant
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);

            // Retry the ORIGINAL bond op (op1). It is a committed replay; it must return the recorded
            // result WITHOUT overwriting the newer (released) projection back to the active state.
            var replay = _handler.Handle(Bond(_charA1, _homestead, "rel-bond-1"));
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);

            // Newer state preserved: index still vacant, relationship still Released.
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);
            var rel = Assert.Single(FindStone(_characters.GetCharacter(_account, _charA1)!, _homestead).Relationships);
            Assert.Equal(RelationshipStatus.Released, rel.Status);
        }

        // ── operation binding covers the full payload (defect 4) ─────────────────

        [Fact]
        public void ReusedOperationId_WithChangedPayload_RejectsOperationConflict()
        {
            var op = new OperationId("op-payload");
            _handler.Handle(new RelationshipCommand(op, RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-p",
                responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner"));
            // SAME op id, SAME binding (op/type/Stone/principal/relationshipId), but a DIFFERENT payload
            // (responsibilityRange). Must conflict, not replay stale intent.
            var conflict = _handler.Handle(new RelationshipCommand(op, RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-p",
                responsibilityRange: "Homestead:KitchenOnly", ownerGovernorRole: "Owner"));
            Assert.Equal("OperationConflict", conflict.ResultCode);
        }

        [Fact]
        public void PartialIntent_ReusedWithConflictingBinding_RejectsOperationConflict()
        {
            var original = Bond(_charA1, _homestead, "rel-partial");
            Assert.Equal(RelationshipCommandOutcome.Applied, _handler.Handle(original).Outcome);

            // Keep only the first durable record (IntentJournaled), simulating death before Committed.
            byte[] journal = File.ReadAllBytes(_journalPath);
            int firstPayloadLength = System.BitConverter.ToInt32(journal, 0);
            System.Array.Resize(ref journal, 8 + firstPayloadLength);
            File.WriteAllBytes(_journalPath, journal);

            var characters2 = new InMemoryCharacterAggregateStore();
            characters2.PutCharacter(BuildCharacter(_account, _charA1, 5, 3, true));
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            var resolver = new PrincipalResolver();
            var handler2 = new RelationshipCommandHandler(_journalPath, resolver, characters2, authority2,
                _families, new StubBondAuthorityPolicy());

            var conflict = handler2.Handle(new RelationshipCommand(original.OperationId,
                RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default,
                relationshipId: "rel-different", responsibilityRange: "Homestead:All"));

            Assert.Equal(RelationshipCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.True(authority2.GetAuthority(_account, _homestead).IsVacant);
        }

        // ── Bond authority is server-authored, not client-authored (defect 5) ────

        [Fact]
        public void CreateBond_WithNoRequestedResponsibilityRange_RejectedOutsideResponsibilityRange()
        {
            var cmd = new RelationshipCommand(new OperationId("op-noauth"), RelationshipCommandType.CreateBond,
                _homestead, new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-noauth",
                responsibilityRange: "", ownerGovernorRole: "Owner");
            var result = _handler.Handle(cmd);
            Assert.Equal(RelationshipCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("OutsideResponsibilityRange", result.ResultCode);
            // No mutation.
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);
        }

        [Fact]
        public void CreateBond_WithUnauthoredResponsibilityRange_IsRejected()
        {
            var cmd = new RelationshipCommand(new OperationId("op-unauthored"),
                RelationshipCommandType.CreateBond, _homestead,
                new AuthenticatedConnection(_account.Value, _charA1.Value), default, "rel-unauthored",
                responsibilityRange: "Homestead:InventedClientRange", ownerGovernorRole: "Owner");

            var result = _handler.Handle(cmd);

            Assert.Equal(RelationshipCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("OutsideResponsibilityRange", result.ResultCode);
            Assert.True(_authority.GetAuthority(_account, _homestead).IsVacant);
        }

        // ── explicit authenticated identity on every durable boundary (defect 6) ─

        [Fact]
        public void DurableJournalRecord_CarriesExplicitAuthenticatedIdentity_NotOnlyDigests()
        {
            _handler.Handle(Bond(_charA1, _homestead, "rel-id"));

            // The boot-rehydration invariant (data-model.md 236-243) requires the authenticated
            // AccountId/CharacterId/StoneId on every durable boundary record, not only payload/binding
            // digests. The codec base64-encodes those fields, so their tokens must appear in the journal.
            string journal = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(_journalPath));
            string encAccount = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_account.Value));
            string encChar = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_charA1.Value));
            string encStone = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_homestead.Value));
            Assert.Contains(encAccount, journal);
            Assert.Contains(encChar, journal);
            Assert.Contains(encStone, journal);
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

        private sealed class StubBondAuthorityPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = string.Empty;
                grantedRole = string.Empty;
                bool authored =
                    string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal) ||
                    string.Equals(requestedResponsibilityRange, "Community:All", System.StringComparison.Ordinal);
                if (!authored) return false;
                grantedRange = requestedResponsibilityRange;
                grantedRole = "Governor";
                return true;
            }
        }
    }
}
